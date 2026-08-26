using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.ViewModels.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Admin;

public interface IAdminDashboardService
{
    Task<AdminDashboardViewModel> LoadAsync(
        int days,
        string? search = null,
        int page = 1,
        CancellationToken cancellationToken = default);

    Task<AdminDashboardViewModel> LoadAsync(
        int days,
        CancellationToken cancellationToken);
}

public sealed class AdminDashboardService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<AdminAccessOptions> adminOptions) : IAdminDashboardService
{
    private const int UserPageSize = AdminDashboardViewModel.DefaultUserPageSize;

    private static readonly IReadOnlyDictionary<string, string> EventLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ProductEventNames.AccountRegistered] = "Accounts registered",
            [ProductEventNames.EmailConfirmed] = "Emails confirmed",
            [ProductEventNames.LoginSucceeded] = "Successful logins",
            [ProductEventNames.OnboardingCompleted] = "Onboarding completed",
            [ProductEventNames.ResumeUploaded] = "Resumes uploaded",
            [ProductEventNames.ResumeAnalysisCompleted] = "Analyses completed",
            [ProductEventNames.ApplicationCreated] = "Applications created",
            [ProductEventNames.ScamCheckCompleted] = "Scam checks completed",
            [ProductEventNames.AiBulletCoachCompleted] = "AI bullet coaching",
            [ProductEventNames.ProUpgradeRequested] = "Pro requests",
            [ProductEventNames.ProUpgradeApproved] = "Pro approvals"
        };
    private static readonly string[] AggregatedEventNames = EventLabels.Keys.ToArray();

    public async Task<AdminDashboardViewModel> LoadAsync(
        int days,
        string? search = null,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        days = days is 7 or 30 or 90 ? days : 30;
        search = NormalizeSearch(search);
        var hasSearch = search is not null;
        var normalizedSearch = search?.ToUpperInvariant() ?? string.Empty;
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var rangeStartDate = today.AddDays(-(days - 1));
        var rangeStart = new DateTimeOffset(
            rangeStartDate.ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);
        var adminUserIds = await GetAdminUserIdsAsync(cancellationToken);

        var usersQuery =
            from user in dbContext.Users.AsNoTracking()
            join profile in dbContext.CareerProfiles.AsNoTracking()
                on user.Id equals profile.UserId into profiles
            from profile in profiles.DefaultIfEmpty()
            join activity in dbContext.UserAccountActivities.AsNoTracking()
                on user.Id equals activity.UserId into activities
            from activity in activities.DefaultIfEmpty()
            where !adminUserIds.Contains(user.Id)
                && (!hasSearch
                    || (user.NormalizedEmail != null
                        && user.NormalizedEmail!.Contains(normalizedSearch))
                    || (user.Email != null
                        && user.Email!.ToUpper().Contains(normalizedSearch))
                    || (profile != null
                        && profile.FullName.ToUpper().Contains(normalizedSearch)))
            select new
            {
                user.Id,
                Email = user.Email ?? user.UserName ?? "Unknown account",
                DisplayName = profile != null ? profile.FullName : string.Empty,
                user.EmailConfirmed,
                RegisteredAt = activity != null
                    ? activity.RegisteredAt
                    : profile != null ? profile.CreatedAt : DateTimeOffset.MinValue,
                LastLoginAt = activity != null ? activity.LastLoginAt : null,
                LastActivityAt = activity != null ? activity.LastActivityAt : null,
                SuccessfulLogins = activity != null ? activity.TotalSuccessfulLogins : 0,
                OnboardingCompleted = profile != null && profile.OnboardingCompletedAt != null,
                ResumeCount = dbContext.Resumes.Count(resume => resume.UserId == user.Id),
                AnalysisCount = dbContext.ResumeAnalyses.Count(analysis => analysis.UserId == user.Id),
                ApplicationCount = dbContext.JobApplications.Count(application => application.UserId == user.Id)
            };

        var totalMatchingUsers = await usersQuery.CountAsync(cancellationToken);
        var totalUserPages = Math.Max(
            1,
            (int)Math.Ceiling(totalMatchingUsers / (double)UserPageSize));
        page = Math.Clamp(page, 1, totalUserPages);

        var users = await usersQuery
            .OrderByDescending(user => user.RegisteredAt)
            .ThenBy(user => user.Email)
            .ThenBy(user => user.Id)
            .Skip((page - 1) * UserPageSize)
            .Take(UserPageSize)
            .ToListAsync(cancellationToken);

        var eventQuery = dbContext.ProductEvents
            .AsNoTracking()
            .Where(productEvent => productEvent.OccurredAt >= rangeStart
                && (productEvent.UserId == null || !adminUserIds.Contains(productEvent.UserId)));
        var dailyEventRows = await eventQuery
            .GroupBy(productEvent => productEvent.OccurredAt.Date)
            .Select(productEvent => new
            {
                Date = productEvent.Key,
                Events = productEvent.Count(),
                ActiveUsers = productEvent
                    .Where(item => item.UserId != null)
                    .Select(item => item.UserId)
                    .Distinct()
                    .Count()
            })
            .ToListAsync(cancellationToken);

        var dailyRegistrationRows = await dbContext.UserAccountActivities
            .AsNoTracking()
            .Where(activity => activity.RegisteredAt >= rangeStart
                && !adminUserIds.Contains(activity.UserId))
            .GroupBy(activity => activity.RegisteredAt.Date)
            .Select(group => new
            {
                Date = group.Key,
                Signups = group.Count()
            })
            .ToListAsync(cancellationToken);

        var featureUsageRows = await eventQuery
            .Where(productEvent => productEvent.Succeeded
                && AggregatedEventNames.Contains(productEvent.Name))
            .GroupBy(productEvent => productEvent.Name)
            .Select(group => new
            {
                EventName = group.Key,
                Count = group.Count(),
                UniqueUsers = group
                    .Where(item => item.UserId != null)
                    .Select(item => item.UserId)
                    .Distinct()
                    .Count()
            })
            .ToListAsync(cancellationToken);

        var failedEventsInRange = await eventQuery.CountAsync(
            productEvent => !productEvent.Succeeded,
            cancellationToken);

        var dailyEvents = dailyEventRows.ToDictionary(
            row => DateOnly.FromDateTime(row.Date));
        var dailyRegistrations = dailyRegistrationRows.ToDictionary(
            row => DateOnly.FromDateTime(row.Date));

        var dailyActivity = Enumerable.Range(0, days)
            .Select(offset => rangeStartDate.AddDays(offset))
            .Select(date => new AdminDailyActivityViewModel(
                date,
                dailyRegistrations.TryGetValue(date, out var registration)
                    ? registration.Signups
                    : 0,
                dailyEvents.TryGetValue(date, out var events)
                    ? events.ActiveUsers
                    : 0,
                events?.Events ?? 0))
            .ToArray();

        var featureUsage = featureUsageRows
            .Select(row => new AdminFeatureUsageViewModel(
                row.EventName,
                EventLabels[row.EventName],
                row.Count,
                row.UniqueUsers))
            .OrderByDescending(item => item.Count)
            .ToArray();

        var recentContactRows = await dbContext.ContactMessages
            .AsNoTracking()
            .OrderBy(message => message.ReadAt != null)
            .ThenByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(5)
            .Select(message => new
            {
                message.Id,
                message.FullName,
                message.Email,
                message.Topic,
                message.Subject,
                message.CreatedAt,
                IsRead = message.ReadAt != null
            })
            .ToListAsync(cancellationToken);

        return new AdminDashboardViewModel
        {
            GeneratedAt = now,
            Range = $"Last {days} days",
            Search = search,
            UserPage = page,
            UserPageSize = UserPageSize,
            TotalMatchingUsers = totalMatchingUsers,
            TotalUserPages = totalUserPages,
            TotalUsers = await dbContext.Users.CountAsync(
                user => !adminUserIds.Contains(user.Id),
                cancellationToken),
            ConfirmedUsers = await dbContext.Users.CountAsync(
                user => user.EmailConfirmed && !adminUserIds.Contains(user.Id),
                cancellationToken),
            NewUsersInRange = await dbContext.UserAccountActivities.CountAsync(
                activity => activity.RegisteredAt >= rangeStart
                    && !adminUserIds.Contains(activity.UserId),
                cancellationToken),
            ActiveUsersInRange = await dbContext.UserAccountActivities.CountAsync(
                activity => activity.LastActivityAt >= rangeStart
                    && !adminUserIds.Contains(activity.UserId),
                cancellationToken),
            CompletedOnboardingUsers = await dbContext.CareerProfiles.CountAsync(
                profile => profile.OnboardingCompletedAt != null
                    && !adminUserIds.Contains(profile.UserId),
                cancellationToken),
            TotalApplications = await dbContext.JobApplications.CountAsync(
                application => !adminUserIds.Contains(application.UserId),
                cancellationToken),
            TotalResumes = await dbContext.Resumes.CountAsync(
                resume => !adminUserIds.Contains(resume.UserId),
                cancellationToken),
            TotalAnalyses = await dbContext.ResumeAnalyses.CountAsync(
                analysis => !adminUserIds.Contains(analysis.UserId),
                cancellationToken),
            TotalContactMessages = await dbContext.ContactMessages.CountAsync(cancellationToken),
            UnreadContactMessages = await dbContext.ContactMessages.CountAsync(
                message => message.ReadAt == null,
                cancellationToken),
            PendingProRequests = await dbContext.ProUpgradeRequests.CountAsync(
                request => request.Status == ProUpgradeRequestStatus.Pending,
                cancellationToken),
            FailedEventsInRange = failedEventsInRange,
            Users = users.Select(user => new AdminUserRowViewModel(
                user.Id,
                user.Email,
                string.IsNullOrWhiteSpace(user.DisplayName) ? "Not provided" : user.DisplayName,
                user.EmailConfirmed,
                user.RegisteredAt,
                user.LastLoginAt,
                user.LastActivityAt,
                user.SuccessfulLogins,
                user.ResumeCount,
                user.AnalysisCount,
                user.ApplicationCount,
                user.OnboardingCompleted)).ToArray(),
            DailyActivity = dailyActivity,
            FeatureUsage = featureUsage,
            RecentContactMessages = recentContactRows.Select(message =>
                new AdminContactPreviewViewModel(
                    message.Id,
                    message.FullName,
                    message.Email,
                    message.Topic.GetDisplayName(),
                    message.Subject,
                    message.CreatedAt,
                    message.IsRead)).ToArray()
        };
    }

    public Task<AdminDashboardViewModel> LoadAsync(
        int days,
        CancellationToken cancellationToken) =>
        LoadAsync(days, null, 1, cancellationToken);

    private async Task<string[]> GetAdminUserIdsAsync(CancellationToken cancellationToken)
    {
        var roleUserIds = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where role.NormalizedName == AdminAccess.Role.ToUpperInvariant()
            select userRole.UserId)
            .ToListAsync(cancellationToken);

        var configuredEmails = adminOptions.Value.ValidEmails()
            .Select(email => email.ToUpperInvariant())
            .ToArray();
        if (configuredEmails.Length > 0)
        {
            roleUserIds.AddRange(await dbContext.Users
                .AsNoTracking()
                .Where(user => user.NormalizedEmail != null
                    && configuredEmails.Contains(user.NormalizedEmail))
                .Select(user => user.Id)
                .ToListAsync(cancellationToken));
        }

        return roleUserIds.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var normalized = search.Trim();
        return normalized.Length <= 320 ? normalized : normalized[..320];
    }
}
