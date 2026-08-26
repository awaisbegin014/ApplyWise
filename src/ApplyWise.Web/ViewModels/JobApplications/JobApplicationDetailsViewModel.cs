using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.JobApplications;

public sealed record JobApplicationDetailsViewModel(
    int Id,
    string CompanyName,
    string JobTitle,
    string? JobLocation,
    JobType? JobType,
    string? SalaryRange,
    JobSource Source,
    string? JobUrl,
    string? JobDescription,
    ApplicationStatus Status,
    string? ResumeVersionName,
    DateOnly? AppliedDate,
    DateOnly? Deadline,
    string? Notes,
    IReadOnlyList<ApplicationCustomFieldViewModel> CustomFields,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ApplicationCustomFieldViewModel(string Label, string Value);
