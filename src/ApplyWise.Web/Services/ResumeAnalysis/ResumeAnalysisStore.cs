using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using Microsoft.EntityFrameworkCore;
using ResumeAnalysisEntity = ApplyWise.Web.Models.ResumeAnalysis;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed class ResumeAnalysisStore(
    ApplicationDbContext dbContext,
    IResumeTextNormalizer normalizer,
    ISkillTaxonomyService taxonomy,
    IResumeAnalysisService analysisService,
    ILogger<ResumeAnalysisStore> logger) : IResumeAnalysisStore
{
    private const string ScoringConfigurationVersion = "ats-v3-evidence-layout-taxonomy-v2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StoredResumeAnalysis> AnalyzeAndStageAsync(
        Resume resume,
        string resumeText,
        string? jobDescription,
        int? jobApplicationId,
        ResumeAnalysisType analysisType,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var normalizedJob = normalizer.Normalize(jobDescription ?? string.Empty);
        var fileDiagnostics = DeserializeDiagnostics(resume.FileDiagnosticsJson);
        var diagnosticsJson = fileDiagnostics is null ? string.Empty : JsonSerializer.Serialize(fileDiagnostics, JsonOptions);
        var inputHash = ComputeInputHash(normalizer.Normalize(resumeText), normalizedJob, taxonomy.Version, resume.PageCount, diagnosticsJson);
        var cached = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Where(item => item.UserId == resume.UserId
                && item.ResumeId == resume.Id
                && item.JobApplicationId == jobApplicationId
                && item.AnalysisType == analysisType
                && item.InputHash == inputHash
                && item.ScoreVersion == ResumeAnalysisResult.CurrentScoreVersion)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (cached is not null && TryHydrate(cached, out var cachedResult))
        {
            logger.LogInformation(
                "Resume analysis store completed in {DurationMs:F3} ms. CacheHit={CacheHit}; AnalysisId={AnalysisId}; ResumeChars={ResumeCharacters}; JobChars={JobCharacters}; Requirements={RequirementCount}; Matches={MatchCount}; ScoreVersion={ScoreVersion}; TaxonomyVersion={TaxonomyVersion}.",
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                true,
                cached.Id,
                resumeText.Length,
                normalizedJob.Length,
                cachedResult!.DetectedJobRequirementCount,
                cachedResult.MatchedRequirements.Count,
                cached.ScoreVersion,
                taxonomy.Version);
            return new StoredResumeAnalysis(cached, cachedResult!, true);
        }

        var result = analysisService.Analyze(resumeText, normalizedJob, resume.PageCount, fileDiagnostics);
        logger.LogInformation(
            "Resume analysis store completed in {DurationMs:F3} ms. CacheHit={CacheHit}; ResumeChars={ResumeCharacters}; JobChars={JobCharacters}; Requirements={RequirementCount}; Matches={MatchCount}; ScoreVersion={ScoreVersion}; TaxonomyVersion={TaxonomyVersion}.",
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            false,
            resumeText.Length,
            normalizedJob.Length,
            result.DetectedJobRequirementCount,
            result.MatchedRequirements.Count,
            result.ScoreVersion,
            taxonomy.Version);
        var analysis = new ResumeAnalysisEntity
        {
            UserId = resume.UserId,
            ResumeId = resume.Id,
            JobApplicationId = jobApplicationId,
            AnalysisType = analysisType,
            MatchedKeywordsJson = "[]",
            MissingKeywordsJson = "[]",
            SuggestionsJson = "[]",
            ResumeTextSnapshot = string.Empty,
            JobDescriptionSnapshot = normalizedJob,
            SnapshotSizeBytes = Encoding.Unicode.GetByteCount(normalizedJob),
            CreatedAt = DateTimeOffset.UtcNow
        };
        ApplyResult(analysis, result, inputHash);
        dbContext.ResumeAnalyses.Add(analysis);
        return new StoredResumeAnalysis(analysis, result, false);
    }

    private static void ApplyResult(ResumeAnalysisEntity analysis, ResumeAnalysisResult result, string inputHash)
    {
        analysis.MatchScore = result.OverallScore;
        analysis.AtsReadinessScore = result.AtsReadinessScore;
        analysis.JobMatchScore = result.JobMatchScore;
        analysis.ConfidenceScore = result.ConfidenceScore;
        analysis.DetectedJobRequirementCount = result.DetectedJobRequirementCount;
        analysis.MustHaveCoverage = result.MustHaveCoverage;
        analysis.RequiredCoverage = result.RequiredCoverage;
        analysis.EvidenceQuality = result.EvidenceQuality;
        analysis.ScoreVersion = result.ScoreVersion;
        analysis.InputHash = inputHash;
        analysis.MatchedKeywordsJson = JsonSerializer.Serialize(result.MatchedKeywords, JsonOptions);
        analysis.MissingKeywordsJson = JsonSerializer.Serialize(result.MissingKeywords, JsonOptions);
        analysis.SuggestionsJson = JsonSerializer.Serialize(result.Suggestions, JsonOptions);
        analysis.ScoreBreakdownJson = JsonSerializer.Serialize(result.ScoreBreakdown, JsonOptions);
        analysis.EvidenceJson = JsonSerializer.Serialize(result.Evidence, JsonOptions);
        analysis.WarningsJson = JsonSerializer.Serialize(result.Warnings, JsonOptions);
        analysis.ReviewJson = JsonSerializer.Serialize(new ReviewPayload
        {
            MatchedRequirements = result.MatchedRequirements,
            MissingRequirements = result.MissingRequirements,
            ReviewItems = result.ReviewItems,
            SectionReviews = result.SectionReviews,
            BulletReviews = result.BulletReviews,
            MustHaveCoverage = result.MustHaveCoverage,
            RequiredCoverage = result.RequiredCoverage,
            EvidenceQuality = result.EvidenceQuality
        }, JsonOptions);
    }

    private static bool TryHydrate(ResumeAnalysisEntity analysis, out ResumeAnalysisResult? result)
    {
        result = null;
        if (!analysis.AtsReadinessScore.HasValue
            || !string.Equals(analysis.ScoreVersion, ResumeAnalysisResult.CurrentScoreVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(analysis.ReviewJson))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<ReviewPayload>(analysis.ReviewJson, JsonOptions);
            if (payload is null) return false;
            result = new ResumeAnalysisResult
            {
                OverallScore = analysis.MatchScore,
                AtsReadinessScore = analysis.AtsReadinessScore.Value,
                JobMatchScore = analysis.JobMatchScore,
                ConfidenceScore = analysis.ConfidenceScore ?? 0,
                ScoreVersion = analysis.ScoreVersion!,
                ScoreBreakdown = Deserialize<ScoreComponent>(analysis.ScoreBreakdownJson),
                MatchedRequirements = payload.MatchedRequirements,
                MissingRequirements = payload.MissingRequirements,
                Evidence = Deserialize<MatchEvidence>(analysis.EvidenceJson),
                Warnings = Deserialize<AnalysisWarning>(analysis.WarningsJson),
                Suggestions = Deserialize<string>(analysis.SuggestionsJson),
                ReviewItems = payload.ReviewItems,
                SectionReviews = payload.SectionReviews,
                BulletReviews = payload.BulletReviews,
                DetectedJobRequirementCount = analysis.DetectedJobRequirementCount ?? 0,
                MustHaveCoverage = analysis.MustHaveCoverage ?? payload.MustHaveCoverage,
                RequiredCoverage = analysis.RequiredCoverage ?? payload.RequiredCoverage,
                EvidenceQuality = analysis.EvidenceQuality ?? payload.EvidenceQuality
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<T> Deserialize<T>(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<T[]>(json, JsonOptions) ?? [];

    private static string ComputeInputHash(
        string resumeText,
        string jobDescription,
        string taxonomyVersion,
        int? pageCount,
        string diagnosticsJson)
    {
        var input = string.Join('\u001F',
            ResumeAnalysisResult.CurrentScoreVersion,
            ScoringConfigurationVersion,
            taxonomyVersion,
            pageCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            diagnosticsJson,
            resumeText,
            jobDescription);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private static ResumeFileDiagnostics? DeserializeDiagnostics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ResumeFileDiagnostics>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private sealed class ReviewPayload
    {
        public IReadOnlyList<MatchEvidence> MatchedRequirements { get; init; } = [];
        public IReadOnlyList<JobRequirement> MissingRequirements { get; init; } = [];
        public IReadOnlyList<ReviewItem> ReviewItems { get; init; } = [];
        public IReadOnlyList<SectionReview> SectionReviews { get; init; } = [];
        public IReadOnlyList<BulletReview> BulletReviews { get; init; } = [];
        public double MustHaveCoverage { get; init; }
        public double RequiredCoverage { get; init; }
        public double EvidenceQuality { get; init; }
    }
}
