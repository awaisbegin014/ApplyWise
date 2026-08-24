using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Contact;

public sealed class ContactMessageStorageOptions
{
    public const string SectionName = "ContactMessages";
    public int RetentionDays { get; set; } = 180;
    public int MaxStoredMessages { get; set; } = 10_000;
    public int ReservedAuthenticatedSlots { get; set; } = 1_000;
    public int MaxUnreadPerAuthenticatedUser { get; set; } = 20;
}

public interface IContactMessageStore
{
    Task<bool> TryStoreAsync(
        ContactMessage message,
        CancellationToken cancellationToken = default);

    Task<int> CleanupAsync(CancellationToken cancellationToken = default);
}

public sealed class ContactMessageStore(
    ApplicationDbContext db,
    IWorkspaceQuotaGate quotaGate,
    IOptions<ContactMessageStorageOptions> options,
    TimeProvider timeProvider,
    ILogger<ContactMessageStore> logger) : IContactMessageStore
{
    private const int MaximumCleanupBatch = 1_000;

    public Task<bool> TryStoreAsync(
        ContactMessage message,
        CancellationToken cancellationToken = default) =>
        quotaGate.RunAsync(
            WorkspaceQuotaResources.ContactMessages,
            "global",
            async operationCancellationToken =>
            {
                var authenticatedSubmission = !string.IsNullOrWhiteSpace(message.UserId);
                if (authenticatedSubmission)
                {
                    var pendingForUser = await db.ContactMessages.CountAsync(
                        existing => existing.UserId == message.UserId
                            && existing.ReadAt == null,
                        operationCancellationToken);
                    if (pendingForUser >= options.Value.MaxUnreadPerAuthenticatedUser)
                    {
                        logger.LogWarning(
                            "A contact message was not stored because the per-account unread limit was reached for user {UserId}.",
                            message.UserId);
                        return false;
                    }
                }

                var messageLimit = authenticatedSubmission
                    ? options.Value.MaxStoredMessages
                    : Math.Max(
                        1,
                        options.Value.MaxStoredMessages
                        - options.Value.ReservedAuthenticatedSlots);
                await CleanupCoreAsync(
                    allowedCount: messageLimit - 1,
                    operationCancellationToken);
                var count = await db.ContactMessages.CountAsync(
                    operationCancellationToken);
                if (count >= messageLimit)
                {
                    logger.LogWarning(
                        "A contact message was not stored because the {SubmissionClass} inbox limit of {MessageLimit} was reached.",
                        authenticatedSubmission ? "authenticated" : "anonymous",
                        messageLimit);
                    return false;
                }

                db.ContactMessages.Add(message);
                await db.SaveChangesAsync(operationCancellationToken);
                return true;
            },
            cancellationToken);

    public Task<int> CleanupAsync(CancellationToken cancellationToken = default) =>
        quotaGate.RunAsync(
            WorkspaceQuotaResources.ContactMessages,
            "global",
            operationCancellationToken => CleanupCoreAsync(
                allowedCount: options.Value.MaxStoredMessages,
                operationCancellationToken),
            cancellationToken);

    private async Task<int> CleanupCoreAsync(
        int allowedCount,
        CancellationToken cancellationToken)
    {
        var removed = 0;
        var cutoff = timeProvider.GetUtcNow().AddDays(-options.Value.RetentionDays);
        var expired = await db.ContactMessages
            .Where(message => message.CreatedAt < cutoff)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(MaximumCleanupBatch)
            .ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            db.ContactMessages.RemoveRange(expired);
            await db.SaveChangesAsync(cancellationToken);
            removed += expired.Count;
        }

        var count = await db.ContactMessages.CountAsync(cancellationToken);
        var overflow = Math.Min(
            Math.Max(0, count - allowedCount),
            MaximumCleanupBatch);
        if (overflow == 0) return removed;

        // Never let a new anonymous submission evict an unread support request.
        // If read/expired rows cannot make room, the incoming write is rejected.
        var oldest = await db.ContactMessages
            .Where(message => message.ReadAt != null)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(overflow)
            .ToListAsync(cancellationToken);
        if (oldest.Count == 0) return removed;
        db.ContactMessages.RemoveRange(oldest);
        await db.SaveChangesAsync(cancellationToken);
        return removed + oldest.Count;
    }
}

public sealed class ContactMessageCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<ContactMessageCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = 0;
                do
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    removed = await scope.ServiceProvider
                        .GetRequiredService<IContactMessageStore>()
                        .CleanupAsync(stoppingToken);
                }
                while (removed >= 1_000 && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "The contact-message retention cleanup failed.");
            }

            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }
}
