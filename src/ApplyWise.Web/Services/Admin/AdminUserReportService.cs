using ApplyWise.Web.Data;
using ApplyWise.Web.ViewModels.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Admin;

public interface IAdminUserReportService
{
    Task<AdminUserReportViewModel?> LoadAsync(
        string userId,
        int applicationsPage,
        int importsPage,
        CancellationToken cancellationToken = default);

}

public sealed class AdminUserReportService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<AdminAccessOptions> adminOptions) : IAdminUserReportService
{
    private const int PageSize = AdminUserReportViewModel.PageSize;
    private const int LatestItemLimit = 25;
    private const int RecentEventLimit = 40;

    public async Task<AdminUserReportViewModel?> LoadAsync(
        string userId,
        int applicationsPage,
        int importsPage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        applicationsPage = NormalizePage(applicationsPage);
        importsPage = NormalizePage(importsPage);

        var identity = await LoadIdentityAsync(userId, cancellationToken);
        if (identity is null || await IsAdminAccountAsync(identity, cancellationToken))
        {
            return null;
        }

        var activity = await dbContext.UserAccountActivities
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new AdminAccountActivityProjection(
                item.RegisteredAt,
                item.LastLoginAt,
                item.LastActivityAt,
                item.LastLoginProvider,
                item.TotalSuccessfulLogins))
            .SingleOrDefaultAsync(cancellationToken);

        var profile = await dbContext.CareerProfiles
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new AdminUserCareerProfileViewModel(
                item.FullName,
                item.CareerStage,
                item.Institution,
                item.DegreeProgram,
                item.FieldOfStudy,
                item.GraduationYear,
                item.CurrentSemester,
                item.PreferredLocations,
                item.PreferredWorkModes,
                item.Skills,
                item.CareerInterests,
                item.AcademicHighlights,
                item.OnboardingCompletedAt,
                item.OnboardingSkippedAt,
                item.CreatedAt,
                item.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        var totals = await dbContext.Users
            .AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(_ => new AdminUserTotalsViewModel(
                dbContext.Resumes.Count(item => item.UserId == userId),
                dbContext.ResumeAnalyses.Count(item => item.UserId == userId),
                dbContext.JobApplications.Count(item => item.UserId == userId),
                dbContext.ApplicationImports.Count(item => item.UserId == userId)))
            .SingleAsync(cancellationToken);

        var applicationStatusCounts = await dbContext.JobApplications
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .GroupBy(item => item.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var applicationStatusBreakdown = applicationStatusCounts
            .OrderBy(item => item.Status)
            .Select(item => new AdminApplicationStatusCountViewModel(
                item.Status,
                item.Count))
            .ToArray();

        var applicationSourceCounts = await dbContext.JobApplications
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .GroupBy(item => item.Source)
            .Select(group => new { Source = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var applicationSourceBreakdown = applicationSourceCounts
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Source)
            .Select(item => new AdminApplicationSourceCountViewModel(
                item.Source,
                item.Count))
            .ToArray();

        var resumes = await dbContext.Resumes
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.IsDefault)
            .ThenByDescending(item => item.UpdatedAt)
            .Select(item => new AdminResumeSummaryViewModel(
                item.Id,
                item.VersionName,
                item.OriginalFileName,
                item.ContentType,
                item.FileSize,
                item.IsDefault,
                item.PageCount,
                item.UploadedAt,
                item.UpdatedAt))
            .ToListAsync(cancellationToken);

        var applicationCount = await dbContext.JobApplications
            .AsNoTracking()
            .CountAsync(item => item.UserId == userId, cancellationToken);
        applicationsPage = ClampPageToCollection(applicationsPage, applicationCount);
        var applications = await dbContext.JobApplications
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.Id)
            .Skip((applicationsPage - 1) * PageSize)
            .Take(PageSize)
            .Select(item => new AdminApplicationSummaryViewModel(
                item.Id,
                item.ResumeId.HasValue
                    && dbContext.Resumes.Any(resume =>
                        resume.Id == item.ResumeId.Value
                        && resume.UserId == userId)
                        ? item.ResumeId
                        : null,
                dbContext.Resumes
                    .Where(resume =>
                        resume.Id == item.ResumeId
                        && resume.UserId == userId)
                    .Select(resume => resume.VersionName)
                    .SingleOrDefault(),
                item.CompanyName,
                item.JobTitle,
                item.JobLocation,
                item.JobType,
                item.SalaryRange,
                item.Source,
                item.Status,
                item.AppliedDate,
                item.Deadline,
                item.CreatedAt,
                item.UpdatedAt))
            .ToListAsync(cancellationToken);

        var importCount = await dbContext.ApplicationImports
            .AsNoTracking()
            .CountAsync(item => item.UserId == userId, cancellationToken);
        importsPage = ClampPageToCollection(importsPage, importCount);
        var imports = await dbContext.ApplicationImports
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.DetectedAt)
            .ThenByDescending(item => item.Id)
            .Skip((importsPage - 1) * PageSize)
            .Take(PageSize)
            .Select(item => new AdminApplicationImportSummaryViewModel(
                item.Id,
                item.Direction,
                item.Status,
                item.Confidence,
                item.SenderDomain,
                item.CompanyName,
                item.JobTitle,
                item.JobLocation,
                item.Source,
                item.AppliedDate,
                dbContext.JobApplications
                    .Where(application =>
                        application.Id == item.CreatedApplicationId
                        && application.UserId == userId)
                    .Select(application => (int?)application.Id)
                    .SingleOrDefault(),
                item.DetectedAt,
                item.ReviewedAt))
            .ToListAsync(cancellationToken);

        var latestAnalyses = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Where(item => item.UserId == userId
                && dbContext.Resumes.Any(resume =>
                    resume.Id == item.ResumeId
                    && resume.UserId == userId)
                && (!item.JobApplicationId.HasValue
                    || dbContext.JobApplications.Any(application =>
                        application.Id == item.JobApplicationId.Value
                        && application.UserId == userId)))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(LatestItemLimit)
            .Select(item => new AdminResumeAnalysisSummaryViewModel(
                item.Id,
                item.ResumeId,
                item.Resume != null ? item.Resume.VersionName : "Deleted resume",
                item.JobApplicationId,
                item.JobApplication != null ? item.JobApplication.CompanyName : null,
                item.JobApplication != null ? item.JobApplication.JobTitle : null,
                item.AnalysisType,
                item.MatchScore,
                item.AtsReadinessScore,
                item.JobMatchScore,
                item.ConfidenceScore,
                item.DetectedJobRequirementCount,
                item.MustHaveCoverage,
                item.RequiredCoverage,
                item.EvidenceQuality,
                item.ScoreVersion,
                item.CreatedAt))
            .ToListAsync(cancellationToken);

        var gmailConnection = await dbContext.GmailConnections
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new AdminGmailConnectionViewModel(
                item.EmailAddress,
                item.ConnectedAt,
                item.UpdatedAt,
                item.LastSyncStartedAt,
                item.LastSuccessfulSyncAt,
                item.NextSyncAt,
                item.LastErrorCode,
                item.AutoAddHighConfidenceApplications))
            .SingleOrDefaultAsync(cancellationToken);

        var recentEvents = await dbContext.ProductEvents
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Take(RecentEventLimit)
            .Select(item => new AdminProductEventViewModel(
                item.Name,
                item.Source,
                item.Succeeded,
                item.OccurredAt))
            .ToListAsync(cancellationToken);

        var registeredAt = activity?.RegisteredAt
            ?? profile?.CreatedAt;
        return new AdminUserReportViewModel
        {
            GeneratedAt = timeProvider.GetUtcNow(),
            UserId = userId,
            Account = new AdminUserAccountViewModel(
                identity.Email,
                identity.EmailConfirmed,
                identity.TwoFactorEnabled,
                identity.LockoutEnd,
                identity.AccessFailedCount,
                registeredAt,
                activity?.LastLoginAt,
                activity?.LastActivityAt,
                activity?.LastLoginProvider,
                activity?.SuccessfulLogins ?? 0),
            Profile = profile,
            Totals = totals,
            ApplicationStatusBreakdown = applicationStatusBreakdown,
            ApplicationSourceBreakdown = applicationSourceBreakdown,
            Resumes = resumes,
            Applications = new AdminPagedCollectionViewModel<AdminApplicationSummaryViewModel>(
                applications,
                applicationsPage,
                PageSize,
                applicationCount),
            Imports = new AdminPagedCollectionViewModel<AdminApplicationImportSummaryViewModel>(
                imports,
                importsPage,
                PageSize,
                importCount),
            LatestAnalyses = latestAnalyses,
            GmailConnection = gmailConnection,
            RecentEvents = recentEvents
        };
    }

    private async Task<AdminIdentityProjection?> LoadIdentityAsync(
        string userId,
        CancellationToken cancellationToken) =>
        await dbContext.Users
            .AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => new AdminIdentityProjection(
                item.Id,
                item.Email ?? item.UserName ?? "Unknown account",
                item.EmailConfirmed,
                item.TwoFactorEnabled,
                item.LockoutEnd,
                item.AccessFailedCount))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<bool> IsAdminAccountAsync(
        AdminIdentityProjection identity,
        CancellationToken cancellationToken)
    {
        if (adminOptions.Value.Contains(identity.Email))
        {
            return true;
        }

        return await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where userRole.UserId == identity.Id
                && role.NormalizedName == AdminAccess.Role.ToUpperInvariant()
            select userRole.UserId)
            .AnyAsync(cancellationToken);
    }

    private static int NormalizePage(int page) =>
        Math.Clamp(page, 1, int.MaxValue / PageSize);

    private static int ClampPageToCollection(int page, int totalCount)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        return Math.Min(page, totalPages);
    }

    private sealed record AdminIdentityProjection(
        string Id,
        string Email,
        bool EmailConfirmed,
        bool TwoFactorEnabled,
        DateTimeOffset? LockoutEnd,
        int AccessFailedCount);

    private sealed record AdminAccountActivityProjection(
        DateTimeOffset RegisteredAt,
        DateTimeOffset? LastLoginAt,
        DateTimeOffset? LastActivityAt,
        string? LastLoginProvider,
        int SuccessfulLogins);

}
