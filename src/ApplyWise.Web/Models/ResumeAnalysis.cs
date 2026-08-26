using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public class ResumeAnalysis
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public int ResumeId { get; set; }
    public int? JobApplicationId { get; set; }
    public ResumeAnalysisType AnalysisType { get; set; }
    public int MatchScore { get; set; }
    public int? AtsReadinessScore { get; set; }
    public int? JobMatchScore { get; set; }
    public int? ConfidenceScore { get; set; }
    public int? DetectedJobRequirementCount { get; set; }
    public double? MustHaveCoverage { get; set; }
    public double? RequiredCoverage { get; set; }
    public double? EvidenceQuality { get; set; }
    public string? ScoreVersion { get; set; }
    public string? InputHash { get; set; }
    public required string MatchedKeywordsJson { get; set; }
    public required string MissingKeywordsJson { get; set; }
    public required string SuggestionsJson { get; set; }
    public string? ScoreBreakdownJson { get; set; }
    public string? EvidenceJson { get; set; }
    public string? WarningsJson { get; set; }
    public string? ReviewJson { get; set; }
    public string? AiFeedbackJson { get; set; }
    public string? AiModel { get; set; }
    public DateTimeOffset? AiGeneratedAt { get; set; }
    public required string ResumeTextSnapshot { get; set; }
    public required string JobDescriptionSnapshot { get; set; }
    public long SnapshotSizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public IdentityUser? User { get; set; }
    public Resume? Resume { get; set; }
    public JobApplication? JobApplication { get; set; }
}
