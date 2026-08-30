using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.ApplicationImports;

public sealed class ApplicationImportIndexViewModel
{
    public bool GoogleIntegrationConfigured { get; init; }
    public bool AutoAddHighConfidenceApplications { get; init; }
    public int AutomaticSyncIntervalMinutes { get; init; }
    public GmailConnectionSummaryViewModel? GmailConnection { get; init; }
    public IReadOnlyList<ApplicationImportListItemViewModel> PendingImports { get; init; } = [];
    public IReadOnlyList<RecentlyAutoAddedApplicationViewModel> RecentlyAutoAddedApplications { get; init; } = [];
}

public sealed record GmailConnectionSummaryViewModel(
    string EmailAddress,
    DateTimeOffset ConnectedAt,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset? LastSyncStartedAt,
    string? LastErrorCode,
    bool AutoAddHighConfidenceApplications);

public sealed record ApplicationImportListItemViewModel(
    int Id,
    string EmailSubject,
    string CompanyName,
    string JobTitle,
    JobSource Source,
    ApplicationImportDirection Direction,
    int Confidence,
    DateOnly? AppliedDate,
    string? ResumeFileName,
    DateTimeOffset DetectedAt);

public sealed record RecentlyAutoAddedApplicationViewModel(
    int ApplicationId,
    string CompanyName,
    string JobTitle,
    DateOnly? AppliedDate,
    DateTimeOffset AutoAddedAt);

public sealed class ApplicationImportReviewViewModel
{
    public int Id { get; set; }
    public string EmailSubject { get; set; } = string.Empty;
    public string? SenderDomain { get; set; }
    public ApplicationImportDirection Direction { get; set; }
    public int Confidence { get; set; }
    public string? ResumeFileName { get; set; }

    [Required]
    [StringLength(150)]
    [Display(Name = "Company name")]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    [StringLength(150)]
    [Display(Name = "Job title")]
    public string JobTitle { get; set; } = string.Empty;

    [StringLength(150)]
    [Display(Name = "Location")]
    public string? JobLocation { get; set; }

    [EnumDataType(typeof(JobSource))]
    [Display(Name = "Source / platform")]
    public JobSource Source { get; set; }

    [Url]
    [StringLength(2048)]
    [Display(Name = "Job post URL")]
    public string? JobUrl { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Applied date")]
    public DateOnly? AppliedDate { get; set; }
}
