using ApplyWise.Web.Models;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.Ai;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed class AnalysisResultViewModel
{
    public int Id { get; init; }
    public int ResumeId { get; init; }
    public int? JobApplicationId { get; init; }
    public ResumeAnalysisType AnalysisType { get; init; }
    public required string ResumeVersionName { get; init; }
    public required string ContextTitle { get; init; }
    public string? ContextSubtitle { get; init; }
    public required string ResumeTextSnapshot { get; init; }
    public required string JobDescriptionSnapshot { get; init; }
    public int OverallScore { get; init; }
    public int MatchScore => OverallScore;
    public int? AtsReadinessScore { get; init; }
    public int? JobMatchScore { get; init; }
    public int? ConfidenceScore { get; init; }
    public required string ScoreVersion { get; init; }
    public IReadOnlyList<string> MatchedKeywords { get; init; } = [];
    public IReadOnlyList<string> MissingKeywords { get; init; } = [];
    public IReadOnlyList<string> Suggestions { get; init; } = [];
    public IReadOnlyList<ScoreComponent> ScoreBreakdown { get; init; } = [];
    public IReadOnlyList<MatchEvidence> Evidence { get; init; } = [];
    public IReadOnlyList<AnalysisWarning> Warnings { get; init; } = [];
    public IReadOnlyList<ReviewItem> ReviewItems { get; init; } = [];
    public IReadOnlyList<SectionReview> SectionReviews { get; init; } = [];
    public IReadOnlyList<BulletReview> BulletReviews { get; init; } = [];
    public IReadOnlyList<JobRequirement> MissingRequirements { get; init; } = [];
    public int DetectedJobRequirementCount { get; init; }
    public double MustHaveCoverage { get; init; }
    public double RequiredCoverage { get; init; }
    public double EvidenceQuality { get; init; }
    public int? PreviousScore { get; set; }
    public int? ScoreChange => PreviousScore.HasValue ? OverallScore - PreviousScore.Value : null;
    public IReadOnlyList<string> ResolvedIssues { get; set; } = [];
    public IReadOnlyList<string> RemainingIssues { get; set; } = [];
    public IReadOnlyList<string> NewlyDetectedEvidence { get; set; } = [];
    public required DateTimeOffset CreatedAt { get; init; }
    public AiAtsFeedback? AiFeedback { get; init; }
    public string? AiModel { get; init; }

    public int DetectedSkillCount => MatchedKeywords.Count + MissingKeywords.Count;
    public bool IsLegacy => !string.Equals(ScoreVersion, ResumeAnalysisResult.CurrentScoreVersion, StringComparison.Ordinal);
    public bool HasJobMatch => JobMatchScore.HasValue;
}
