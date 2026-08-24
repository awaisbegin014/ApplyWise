using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Admin;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.AccountSecurity;

public sealed class PendingRegistrationOptions
{
    public const string SectionName = "PendingRegistrations";
    public int RetentionDays { get; set; } = 7;
    public int MaxPendingAccounts { get; set; } = 10_000;
}

public enum PendingRegistrationStatus
{
    Created,
    Existing,
    CapacityReached,
    Failed
}

public sealed record PendingRegistrationResult(
    PendingRegistrationStatus Status,
    IdentityUser? User,
    IdentityResult IdentityResult);

public interface IPendingRegistrationStore
{
    Task<PendingRegistrationResult> TryCreateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed class PendingRegistrationStore(
    ApplicationDbContext db,
    UserManager<IdentityUser> userManager,
    IWorkspaceQuotaGate quotaGate,
    IOptions<PendingRegistrationOptions> options,
    TimeProvider timeProvider) : IPendingRegistrationStore
{
    public Task<PendingRegistrationResult> TryCreateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default) =>
        quotaGate.RunAsync(
            WorkspaceQuotaResources.PendingRegistrations,
            "global",
            async operationCancellationToken =>
            {
                var normalizedEmail = userManager.NormalizeEmail(email);
                if (await db.Users.AsNoTracking().AnyAsync(
                        user => user.NormalizedEmail == normalizedEmail,
                        operationCancellationToken))
                {
                    return new PendingRegistrationResult(
                        PendingRegistrationStatus.Existing,
                        null,
                        IdentityResult.Success);
                }

                var pendingCount = await db.Users.CountAsync(
                    user => !user.EmailConfirmed,
                    operationCancellationToken);
                if (pendingCount >= options.Value.MaxPendingAccounts)
                {
                    return new PendingRegistrationResult(
                        PendingRegistrationStatus.CapacityReached,
                        null,
                        IdentityResult.Success);
                }

                var user = new IdentityUser { UserName = email, Email = email };
                var result = await userManager.CreateAsync(user, password);
                if (result.Succeeded)
                {
                    var registeredAt = timeProvider.GetUtcNow();
                    db.UserAccountActivities.Add(new UserAccountActivity
                    {
                        UserId = user.Id,
                        RegisteredAt = registeredAt,
                        LastActivityAt = registeredAt
                    });
                    await db.SaveChangesAsync(operationCancellationToken);
                    return new PendingRegistrationResult(
                        PendingRegistrationStatus.Created,
                        user,
                        result);
                }

                var duplicate = result.Errors.Any(error =>
                    error.Code is "DuplicateUserName" or "DuplicateEmail");
                return new PendingRegistrationResult(
                    duplicate
                        ? PendingRegistrationStatus.Existing
                        : PendingRegistrationStatus.Failed,
                    null,
                    result);
            },
            cancellationToken);
}

public interface IPendingRegistrationRetention
{
    Task<int> DeleteExpiredBatchAsync(
        CancellationToken cancellationToken = default);
}

public sealed class PendingRegistrationRetention(
    ApplicationDbContext db,
    IOptions<PendingRegistrationOptions> options,
    IOptions<AdminAccessOptions> adminOptions,
    TimeProvider timeProvider,
    IApplicationLockProvider operationLocks) : IPendingRegistrationRetention
{
    public const int MaximumBatchSize = 200;
    public const string OperationLockResource = "pending-registration-retention";

    public async Task<int> DeleteExpiredBatchAsync(
        CancellationToken cancellationToken = default)
    {
        await using var operationLease = await operationLocks.TryAcquireAsync(
            OperationLockResource,
            TimeSpan.FromSeconds(5),
            cancellationToken) ?? throw new ResourceLockUnavailableException(
                "Pending-account maintenance is busy. Try again shortly.");

        var now = timeProvider.GetUtcNow();
        var cutoff = now.AddDays(-options.Value.RetentionDays);
        var candidateLimit = MaximumBatchSize + adminOptions.Value.Emails.Length;
        var pendingAccounts =
            from user in db.Users
            where !user.EmailConfirmed
            join activity in db.UserAccountActivities
                on user.Id equals activity.UserId into activities
            from activity in activities.DefaultIfEmpty()
            join profile in db.CareerProfiles
                on user.Id equals profile.UserId into profiles
            from profile in profiles.DefaultIfEmpty()
            select new
            {
                User = user,
                RegisteredAt = activity != null
                    ? (DateTimeOffset?)activity.RegisteredAt
                    : profile != null
                        ? profile.CreatedAt
                        : null
            };
        var candidates = await pendingAccounts
            .Where(candidate => candidate.RegisteredAt == null
                || candidate.RegisteredAt < cutoff)
            .Where(candidate => !db.AccountSecurityCodes.Any(code =>
                code.UserId == candidate.User.Id
                && code.Action == AccountSecurityAction.ConfirmEmail
                && code.ConsumedAt == null
                && code.ExpiresAt > now))
            .OrderBy(candidate => candidate.RegisteredAt ?? DateTimeOffset.MinValue)
            .ThenBy(candidate => candidate.User.Id)
            .Take(candidateLimit)
            .Select(candidate => new
            {
                candidate.User.Id,
                candidate.User.Email,
                candidate.RegisteredAt
            })
            .ToListAsync(cancellationToken);
        var expired = candidates
            .Where(candidate => !adminOptions.Value.Contains(candidate.Email))
            .Take(MaximumBatchSize)
            .ToArray();
        if (expired.Length == 0) return 0;

        var orphanIds = expired
            .Where(candidate => candidate.RegisteredAt == null)
            .Select(candidate => candidate.Id)
            .ToArray();
        var staleIds = expired
            .Where(candidate => candidate.RegisteredAt != null)
            .Select(candidate => candidate.Id)
            .ToArray();
        var stillExpired = db.Users.Where(user =>
            !user.EmailConfirmed
            && !db.AccountSecurityCodes.Any(code =>
                code.UserId == user.Id
                && code.Action == AccountSecurityAction.ConfirmEmail
                && code.ConsumedAt == null
                && code.ExpiresAt > now)
            && ((orphanIds.Contains(user.Id)
                    && !db.UserAccountActivities.Any(activity => activity.UserId == user.Id)
                    && !db.CareerProfiles.Any(profile => profile.UserId == user.Id))
                || (staleIds.Contains(user.Id)
                    && (db.UserAccountActivities.Any(activity =>
                            activity.UserId == user.Id && activity.RegisteredAt < cutoff)
                        || (!db.UserAccountActivities.Any(activity => activity.UserId == user.Id)
                            && db.CareerProfiles.Any(profile =>
                                profile.UserId == user.Id && profile.CreatedAt < cutoff))))));

        if (db.Database.IsRelational())
        {
            return await stillExpired.ExecuteDeleteAsync(cancellationToken);
        }

        var expiredUsers = await stillExpired.ToListAsync(cancellationToken);
        if (expiredUsers.Count == 0) return 0;

        db.Users.RemoveRange(expiredUsers);
        await db.SaveChangesAsync(cancellationToken);
        return expiredUsers.Count;
    }
}

public sealed class PendingRegistrationCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<PendingRegistrationCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int removed;
                do
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    removed = await scope.ServiceProvider
                        .GetRequiredService<IPendingRegistrationRetention>()
                        .DeleteExpiredBatchAsync(stoppingToken);
                }
                while (removed >= PendingRegistrationRetention.MaximumBatchSize
                       && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Expired pending-account cleanup failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
