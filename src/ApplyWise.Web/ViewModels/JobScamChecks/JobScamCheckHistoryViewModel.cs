using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.JobScamChecks;

public sealed class JobScamCheckHistoryViewModel
{
    public IReadOnlyList<JobScamCheckHistoryItemViewModel> Checks { get; set; } = [];
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

public sealed record JobScamCheckHistoryItemViewModel(
    int Id, string CompanyName, string JobTitle, int RiskScore,
    JobRiskLevel RiskLevel, int QualityScore, DateTimeOffset CreatedAt);
