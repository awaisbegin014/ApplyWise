using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Analytics;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.ViewModels.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.Dashboard;

public interface IDashboardReadService
{
    Task<DashboardViewModel> GetAsync(
        string userId,
        string displayName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the complete dashboard from four narrow, no-tracking projections.
/// Keep dashboard reads in this service so new cards do not silently add
/// sequential database round trips to the controller.
/// </summary>
public sealed class DashboardReadService(ApplicationDbContext dbContext) : IDashboardReadService
{
    public const int MaxApplicationRows = 500;
    public const int MaxAnalysisRows = 200;
    public const int MaxResumeRows = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DashboardViewModel> GetAsync(
        string userId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var localNow = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var todayStart = new DateTimeOffset(localNow.Date, localNow.Offset).ToUniversalTime();

        // EF Core does not allow concurrent operations on one DbContext. These
        // three projections intentionally replace the former collection of small round trips.
        var applications = await dbContext.JobApplications.AsNoTracking()
            .Where(application => application.UserId == userId)
            .OrderByDescending(application => application.UpdatedAt)
            .Select(application => new ApplicationRow(
                application.Id,
                application.CompanyName,
                application.JobTitle,
                application.Status,
                application.ResumeId,
                application.Deadline,
                application.CreatedAt,
                application.UpdatedAt))
            .Take(MaxApplicationRows + 1)
            .ToListAsync(cancellationToken);
        var applicationsOverflowed = applications.Count > MaxApplicationRows;
        if (applicationsOverflowed)
        {
            applications.RemoveAt(MaxApplicationRows);
        }
        var totalApplications = applicationsOverflowed
            ? await dbContext.JobApplications.CountAsync(
                application => application.UserId == userId,
                cancellationToken)
            : applications.Count;

        var analyses = await dbContext.ResumeAnalyses.AsNoTracking()
            .Where(analysis => analysis.UserId == userId)
            .OrderByDescending(analysis => analysis.CreatedAt)
            .Select(analysis => new AnalysisRow(
                analysis.Id,
                analysis.ResumeId,
                analysis.JobApplicationId,
                analysis.Resume!.VersionName,
                analysis.JobApplication != null ? analysis.JobApplication.CompanyName : "Direct input",
                analysis.JobApplication != null ? analysis.JobApplication.JobTitle : "Pasted requirements",
                analysis.MatchScore,
                analysis.AtsReadinessScore,
                analysis.JobMatchScore,
                analysis.EvidenceQuality,
                analysis.ScoreVersion,
                analysis.MissingKeywordsJson,
                analysis.ReviewJson,
                analysis.CreatedAt))
            .Take(MaxAnalysisRows + 1)
            .ToListAsync(cancellationToken);
        var analysesOverflowed = analyses.Count > MaxAnalysisRows;
        if (analysesOverflowed)
        {
            analyses.RemoveAt(MaxAnalysisRows);
        }

        var resumes = await dbContext.Resumes.AsNoTracking()
            .Where(resume => resume.UserId == userId)
            .OrderByDescending(resume => resume.UpdatedAt)
            .Select(resume => new ResumeRow(resume.Id, resume.VersionName))
            .Take(MaxResumeRows)
            .ToListAsync(cancellationToken);

        var currentAnalyses = analyses
            .Where(analysis => analysis.ScoreVersion == ResumeAnalysisResult.CurrentScoreVersion)
            .ToArray();
        var fitAnalyses = currentAnalyses
            .Where(analysis => analysis.JobMatchScore.HasValue)
            .ToArray();
        var averageMatchScore = analysesOverflowed
            ? await dbContext.ResumeAnalyses
                .Where(analysis =>
                    analysis.UserId == userId
                    && analysis.ScoreVersion == ResumeAnalysisResult.CurrentScoreVersion
                    && analysis.JobMatchScore.HasValue)
                .Select(analysis => (double?)analysis.MatchScore)
                .AverageAsync(cancellationToken) ?? 0
            : fitAnalyses.Length == 0
                ? 0
                : Math.Round(fitAnalyses.Average(analysis => analysis.MatchScore), 1);
        var interviewedApplicationIds = applications
            .Where(application => application.Status == ApplicationStatus.Interview)
            .Select(application => application.Id)
            .ToHashSet();
        var bestResume = FindBestResume(resumes, currentAnalyses, applications, interviewedApplicationIds);
        var funnel = applicationsOverflowed
            ? await BuildExactFunnelAsync(userId, cancellationToken)
            : BuildFunnel(applications, interviewedApplicationIds);

        var deadlineSortAt = todayStart.AddHours(23).AddMinutes(59);
        var todayDeadlines = applications
            .Where(application => application.Deadline == today)
            .Select(application => new DashboardActionItemViewModel(
                "Deadline",
                "Application deadline",
                application.JobTitle + " at " + application.CompanyName,
                deadlineSortAt,
                "JobApplications",
                "Details",
                application.Id))
            .ToArray();

        return new DashboardViewModel
        {
            DisplayName = displayName,
            CurrentTime = localNow,
            TotalApplications = totalApplications,
            AverageMatchScore = Math.Round(averageMatchScore, 1),
            Funnel = funnel,
            BestResumeVersionName = bestResume?.VersionName,
            BestResumeScore = bestResume?.AverageMatchScore ?? 0,
            RecentApplications = applications
                .OrderByDescending(application => application.CreatedAt)
                .Take(5)
                .Select(ToRecentApplication)
                .ToArray(),
            PipelineApplications = applications.Select(ToRecentApplication).ToArray(),
            RecentAnalyses = analyses
                .OrderByDescending(analysis => analysis.CreatedAt)
                .Take(5)
                .Select(analysis => new RecentAnalysisItem(
                    analysis.Id,
                    analysis.ResumeVersionName,
                    analysis.CompanyName,
                    analysis.JobTitle,
                    analysis.MatchScore,
                    analysis.AtsReadinessScore,
                    analysis.JobMatchScore,
                    analysis.ScoreVersion ?? "legacy-v1",
                    analysis.CreatedAt))
                .ToArray(),
            TopSkillGaps = BuildSkillGapTrends(currentAnalyses).Take(4).ToArray(),
            UpcomingDeadlines = applications
                .Where(application => application.Deadline >= today)
                .OrderBy(application => application.Deadline)
                .Take(5)
                .Select(application => new DashboardDeadlineItemViewModel(
                    application.Id,
                    application.CompanyName,
                    application.JobTitle,
                    application.Deadline!.Value))
                .ToArray(),
            TodayActions = todayDeadlines
                .OrderBy(item => item.SortAt)
                .Take(8)
                .ToArray(),
            TodayActionCount = todayDeadlines.Length
        };
    }

    private static RecentApplicationItem ToRecentApplication(ApplicationRow application) =>
        new(
            application.Id,
            application.CompanyName,
            application.JobTitle,
            application.Status,
            application.CreatedAt);

    private static ApplicationFunnelResult BuildFunnel(
        IReadOnlyCollection<ApplicationRow> applications,
        IReadOnlySet<int> interviewedApplicationIds) =>
        new(
            applications.Count(application => application.Status == ApplicationStatus.Applied),
            applications.Count(application => application.Status == ApplicationStatus.Pending),
            applications.Count(application => interviewedApplicationIds.Contains(application.Id)
                || application.Status == ApplicationStatus.Interview),
            applications.Count(application => application.Status == ApplicationStatus.Offered),
            applications.Count(application => application.Status == ApplicationStatus.Accepted),
            applications.Count(application => application.Status == ApplicationStatus.Rejected),
            applications.Count(application => application.Status == ApplicationStatus.UserRejected),
            applications.Count(application => application.Status == ApplicationStatus.Ignored));

    private async Task<ApplicationFunnelResult> BuildExactFunnelAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var statusCounts = await dbContext.JobApplications.AsNoTracking()
            .Where(application => application.UserId == userId)
            .GroupBy(application => application.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
        var interviewCount = statusCounts.GetValueOrDefault(ApplicationStatus.Interview);

        int Count(ApplicationStatus status) => statusCounts.GetValueOrDefault(status);
        return new ApplicationFunnelResult(
            Count(ApplicationStatus.Applied),
            Count(ApplicationStatus.Pending),
            interviewCount,
            Count(ApplicationStatus.Offered),
            Count(ApplicationStatus.Accepted),
            Count(ApplicationStatus.Rejected),
            Count(ApplicationStatus.UserRejected),
            Count(ApplicationStatus.Ignored));
    }

    private static ResumeMetric? FindBestResume(
        IReadOnlyCollection<ResumeRow> resumes,
        IReadOnlyCollection<AnalysisRow> analyses,
        IReadOnlyCollection<ApplicationRow> applications,
        IReadOnlySet<int> interviewedApplicationIds)
    {
        var metrics = resumes.Select(resume =>
        {
            var resumeAnalyses = analyses.Where(analysis => analysis.ResumeId == resume.Id).ToArray();
            var fitAnalyses = resumeAnalyses.Where(analysis => analysis.JobMatchScore.HasValue).ToArray();
            var linkedApplications = applications.Where(application => application.ResumeId == resume.Id).ToArray();
            var interviewCount = linkedApplications.Count(application => interviewedApplicationIds.Contains(application.Id));
            return new ResumeMetric(
                resume.Id,
                resume.VersionName,
                fitAnalyses.Length == 0 ? 0 : Math.Round(fitAnalyses.Average(item => item.MatchScore), 1),
                linkedApplications.Length == 0
                    ? 0
                    : Math.Round(interviewCount * 100d / linkedApplications.Length, 1),
                interviewCount,
                resumeAnalyses.Length > 0 || interviewCount > 0);
        }).ToArray();

        return metrics.Any(metric => metric.HasActivity)
            ? metrics
                .OrderByDescending(metric => metric.InterviewRate)
                .ThenByDescending(metric => metric.AverageMatchScore)
                .ThenByDescending(metric => metric.InterviewCount)
                .First()
            : null;
    }

    private static IReadOnlyList<SkillGapTrendItem> BuildSkillGapTrends(
        IReadOnlyCollection<AnalysisRow> analyses)
    {
        var occurrences = analyses
            .SelectMany(analysis =>
            {
                var requirements = DeserializeMissingRequirements(analysis.ReviewJson)
                    .Where(IsSkillRequirement)
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderBy(item => PriorityRank(item.Priority)).First())
                    .ToArray();
                return requirements.Length > 0
                    ? requirements.Select(item => new MissingOccurrence(
                        item.Name,
                        item.Priority,
                        analysis.JobApplicationId))
                    : Deserialize(analysis.MissingKeywordsJson)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(skill => new MissingOccurrence(
                            skill,
                            RequirementPriority.Informational,
                            analysis.JobApplicationId));
            })
            .GroupBy(item => item.Skill, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Skill = group.Key,
                Count = group.Count(),
                PriorityRank = group.Min(item => PriorityRank(item.Priority)),
                JobCount = group
                    .Select(item => item.JobApplicationId)
                    .Where(jobApplicationId => jobApplicationId.HasValue)
                    .Distinct()
                    .Count()
            })
            .OrderBy(item => item.PriorityRank)
            .ThenByDescending(item => item.Count)
            .ThenBy(item => item.Skill)
            .ToArray();

        return occurrences
            .Select(item => new SkillGapTrendItem(
                item.Skill,
                item.Count,
                item.JobCount,
                item.PriorityRank switch
                {
                    0 => "Critical",
                    1 => "High",
                    2 => "Medium",
                    _ => "Low"
                },
                BuildSkillAction(item.Skill)))
            .ToArray();
    }

    private static string BuildSkillAction(string skill) => skill switch
    {
        "SQL Server" => "If relevant, strengthen a project bullet with database design, queries, or EF Core usage.",
        "ASP.NET Core" => "If you have experience, show where you used ASP.NET Core in a concrete project or role.",
        "Azure" or "AWS" => $"If relevant, document hands-on {skill} services, deployment, or hosting experience.",
        "Unit Testing" => "If you have written automated tests, name the framework and the behavior you verified.",
        _ => $"If you have genuine {skill} experience, add a specific example showing where and how you used it."
    };

    private static int PriorityRank(RequirementPriority priority) => priority switch
    {
        RequirementPriority.MustHave => 0,
        RequirementPriority.Required => 1,
        RequirementPriority.Preferred => 2,
        _ => 3
    };

    private static bool IsSkillRequirement(JobRequirement requirement) => requirement.Category is
        RequirementCategory.TechnicalSkill or RequirementCategory.Tool or RequirementCategory.DomainSkill or
        RequirementCategory.SoftSkill or RequirementCategory.Language;

    private static IReadOnlyList<JobRequirement> DeserializeMissingRequirements(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<AnalyticsReviewPayload>(json, JsonOptions)?.MissingRequirements ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record ApplicationRow(
        int Id,
        string CompanyName,
        string JobTitle,
        ApplicationStatus Status,
        int? ResumeId,
        DateOnly? Deadline,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record AnalysisRow(
        int Id,
        int ResumeId,
        int? JobApplicationId,
        string ResumeVersionName,
        string CompanyName,
        string JobTitle,
        int MatchScore,
        int? AtsReadinessScore,
        int? JobMatchScore,
        double? EvidenceQuality,
        string? ScoreVersion,
        string MissingKeywordsJson,
        string? ReviewJson,
        DateTimeOffset CreatedAt);

    private sealed record ResumeRow(int Id, string VersionName);

    private sealed record ResumeMetric(
        int ResumeId,
        string VersionName,
        double AverageMatchScore,
        double InterviewRate,
        int InterviewCount,
        bool HasActivity);

    private sealed record MissingOccurrence(
        string Skill,
        RequirementPriority Priority,
        int? JobApplicationId);

    private sealed class AnalyticsReviewPayload
    {
        public JobRequirement[] MissingRequirements { get; init; } = [];
    }
}
