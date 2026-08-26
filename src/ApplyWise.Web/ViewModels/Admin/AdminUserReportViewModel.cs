using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.Admin;

public sealed class AdminUserReportViewModel
{
    public const int PageSize = 25;

    public required DateTimeOffset GeneratedAt { get; init; }
    public required string UserId { get; init; }
    public required AdminUserAccountViewModel Account { get; init; }
    public AdminUserCareerProfileViewModel? Profile { get; init; }
    public required AdminUserTotalsViewModel Totals { get; init; }
    public IReadOnlyList<AdminApplicationStatusCountViewModel> ApplicationStatusBreakdown { get; init; } = [];
    public IReadOnlyList<AdminApplicationSourceCountViewModel> ApplicationSourceBreakdown { get; init; } = [];
    public IReadOnlyList<AdminResumeSummaryViewModel> Resumes { get; init; } = [];
    public required AdminPagedCollectionViewModel<AdminApplicationSummaryViewModel> Applications { get; init; }
    public required AdminPagedCollectionViewModel<AdminApplicationImportSummaryViewModel> Imports { get; init; }
    public IReadOnlyList<AdminResumeAnalysisSummaryViewModel> LatestAnalyses { get; init; } = [];
    public AdminGmailConnectionViewModel? GmailConnection { get; init; }
    public IReadOnlyList<AdminProductEventViewModel> RecentEvents { get; init; } = [];
}

public sealed record AdminUserAccountViewModel(
    string Email,
    bool EmailConfirmed,
    bool TwoFactorEnabled,
    DateTimeOffset? LockoutEnd,
    int AccessFailedCount,
    DateTimeOffset? RegisteredAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? LastActivityAt,
    string? LastLoginProvider,
    int SuccessfulLogins);

public sealed record AdminUserCareerProfileViewModel(
    string FullName,
    CareerStage? CareerStage,
    string? Institution,
    string? DegreeProgram,
    string? FieldOfStudy,
    int? GraduationYear,
    string? CurrentSemester,
    string? PreferredLocations,
    string? PreferredWorkModes,
    string? Skills,
    string? CareerInterests,
    string? AcademicHighlights,
    DateTimeOffset? OnboardingCompletedAt,
    DateTimeOffset? OnboardingSkippedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AdminUserTotalsViewModel(
    int ResumeCount,
    int AnalysisCount,
    int ApplicationCount,
    int ImportCount);

public sealed record AdminApplicationStatusCountViewModel(
    ApplicationStatus Status,
    int Count);

public sealed record AdminApplicationSourceCountViewModel(
    JobSource Source,
    int Count);

public sealed record AdminResumeSummaryViewModel(
    int Id,
    string VersionName,
    string OriginalFileName,
    string ContentType,
    long FileSize,
    bool IsDefault,
    int? PageCount,
    DateTimeOffset UploadedAt,
    DateTimeOffset UpdatedAt);

public sealed record AdminApplicationSummaryViewModel(
    int Id,
    int? ResumeId,
    string? ResumeVersionName,
    string CompanyName,
    string JobTitle,
    string? JobLocation,
    JobType? JobType,
    string? SalaryRange,
    JobSource Source,
    ApplicationStatus Status,
    DateOnly? AppliedDate,
    DateOnly? Deadline,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AdminApplicationImportSummaryViewModel(
    int Id,
    ApplicationImportDirection Direction,
    ApplicationImportStatus Status,
    int Confidence,
    string? SenderDomain,
    string CompanyName,
    string JobTitle,
    string? JobLocation,
    JobSource Source,
    DateOnly? AppliedDate,
    int? CreatedApplicationId,
    DateTimeOffset DetectedAt,
    DateTimeOffset? ReviewedAt);

public sealed record AdminResumeAnalysisSummaryViewModel(
    int Id,
    int ResumeId,
    string ResumeVersionName,
    int? JobApplicationId,
    string? CompanyName,
    string? JobTitle,
    ResumeAnalysisType AnalysisType,
    int MatchScore,
    int? AtsReadinessScore,
    int? JobMatchScore,
    int? ConfidenceScore,
    int? DetectedJobRequirementCount,
    double? MustHaveCoverage,
    double? RequiredCoverage,
    double? EvidenceQuality,
    string? ScoreVersion,
    DateTimeOffset CreatedAt);

public sealed record AdminGmailConnectionViewModel(
    string EmailAddress,
    DateTimeOffset ConnectedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSyncStartedAt,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset NextSyncAt,
    string? LastErrorCode,
    bool AutoAddHighConfidenceApplications);

public sealed record AdminProductEventViewModel(
    string Name,
    string Source,
    bool Succeeded,
    DateTimeOffset OccurredAt);

public sealed record AdminPagedCollectionViewModel<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
