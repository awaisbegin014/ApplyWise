using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Analytics;

namespace ApplyWise.Web.ViewModels.Dashboard;

public sealed class DashboardViewModel
{
    public string DisplayName { get; set; } = "there";
    public DateTimeOffset CurrentTime { get; set; }
    public int TotalApplications { get; set; }
    public double AverageMatchScore { get; set; }
    public int TodayActionCount { get; set; }
    public ApplicationFunnelResult Funnel { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0);
    public string? BestResumeVersionName { get; set; }
    public double BestResumeScore { get; set; }
    public IReadOnlyList<DashboardDeadlineItemViewModel> UpcomingDeadlines { get; set; } = [];
    public IReadOnlyList<DashboardActionItemViewModel> TodayActions { get; set; } = [];
    public IReadOnlyList<SkillGapTrendItem> TopSkillGaps { get; set; } = [];
    public IReadOnlyList<RecentApplicationItem> RecentApplications { get; set; } = [];
    public IReadOnlyList<RecentApplicationItem> PipelineApplications { get; set; } = [];
    public IReadOnlyList<RecentAnalysisItem> RecentAnalyses { get; set; } = [];
}

public sealed record DashboardDeadlineItemViewModel(
    int Id, string CompanyName, string JobTitle, DateOnly Deadline);

public sealed record DashboardActionItemViewModel(
    string Kind, string Title, string Context, DateTimeOffset SortAt, string Controller, string Action, int Id);
