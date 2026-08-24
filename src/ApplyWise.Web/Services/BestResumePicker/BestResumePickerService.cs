using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.Services.Security;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.BestResumePicker;

public sealed class BestResumePickerService(
    ApplicationDbContext dbContext,
    IResumeStorageService resumeStorage,
    IResumeTextExtractorService textExtractor,
    IResumeAnalysisStore analysisStore,
    IWorkspaceQuotaService quotas,
    IWorkspaceQuotaGate quotaGate,
    ILogger<BestResumePickerService> logger) : IBestResumePickerService
{
    public const int MaxResumesPerComparison = 8;
    private static readonly SemaphoreSlim ComparisonSlots = new(initialCount: 2, maxCount: 2);
    private static readonly TimeSpan AdmissionTimeout = TimeSpan.FromSeconds(2);
    private sealed record CompletedComparison(
        Resume Resume,
        ResumeAnalysisResult Result,
        StoredResumeAnalysis Stored);

    public async Task<BestResumePickerResult> CompareResumesForJobAsync(
        string userId,
        int jobApplicationId,
        CancellationToken cancellationToken = default)
    {
        var application = await dbContext.JobApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == jobApplicationId && item.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException("The selected job application was not found.");

        if (string.IsNullOrWhiteSpace(application.JobDescription))
        {
            throw new InvalidOperationException("The selected job application does not have a job description.");
        }

        return await CompareAsync(
            userId,
            application.JobDescription,
            application.Id,
            $"{application.JobTitle} at {application.CompanyName}",
            ResumeAnalysisType.SavedApplication,
            cancellationToken);
    }

    public Task<BestResumePickerResult> CompareResumesWithRequirementsAsync(
        string userId,
        string jobRequirements,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobRequirements) || jobRequirements.Trim().Length < 30)
        {
            throw new InvalidOperationException("Paste at least 30 characters of job requirements before comparing.");
        }

        return CompareAsync(
            userId,
            jobRequirements.Trim(),
            null,
            "Pasted job requirements",
            ResumeAnalysisType.PastedRequirements,
            cancellationToken);
    }

    private async Task<BestResumePickerResult> CompareAsync(
        string userId,
        string jobRequirements,
        int? jobApplicationId,
        string contextTitle,
        ResumeAnalysisType analysisType,
        CancellationToken cancellationToken)
    {
        if (!await ComparisonSlots.WaitAsync(AdmissionTimeout, cancellationToken))
        {
            throw new InvalidOperationException(
                "Resume comparison is busy right now. Wait a moment and try again.");
        }

        try
        {
            return await CompareWithinAdmissionAsync(
                userId,
                jobRequirements,
                jobApplicationId,
                contextTitle,
                analysisType,
                cancellationToken);
        }
        finally
        {
            ComparisonSlots.Release();
        }
    }

    private async Task<BestResumePickerResult> CompareWithinAdmissionAsync(
        string userId,
        string jobRequirements,
        int? jobApplicationId,
        string contextTitle,
        ResumeAnalysisType analysisType,
        CancellationToken cancellationToken)
    {
        var resumes = await dbContext.Resumes
            .Where(resume => resume.UserId == userId)
            .OrderByDescending(resume => resume.IsDefault)
            .ThenByDescending(resume => resume.UploadedAt)
            .Take(MaxResumesPerComparison)
            .ToListAsync(cancellationToken);

        if (resumes.Count == 0)
        {
            throw new InvalidOperationException("No resumes are available to compare.");
        }

        var completed = new List<CompletedComparison>();
        var unreadable = new List<ComparedResumeResult>();
        var comparisonTime = DateTimeOffset.UtcNow;

        foreach (var resume in resumes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resumeText = resume.ExtractedText;
            var extractionStatus = "Cached";
            if (string.IsNullOrWhiteSpace(resumeText))
            {
                var absolutePath = resumeStorage.ResolvePath(resume.FilePath);
                if (File.Exists(absolutePath))
                {
                    var inspection = await textExtractor.InspectAsync(absolutePath, cancellationToken);
                    extractionStatus = inspection.Status.ToString();
                    resumeText = inspection.Text;
                    resume.PageCount = inspection.PageCount;
                    resume.FileDiagnosticsJson = inspection.Diagnostics is null
                        ? null
                        : JsonSerializer.Serialize(inspection.Diagnostics, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                }
                else extractionStatus = PdfTextExtractionStatus.Unavailable.ToString();

                if (!string.IsNullOrWhiteSpace(resumeText))
                {
                    resume.ExtractedText = resumeText;
                    resume.UpdatedAt = comparisonTime;
                }
            }

            if (string.IsNullOrWhiteSpace(resumeText))
            {
                unreadable.Add(new ComparedResumeResult(
                    resume.Id,
                    resume.VersionName,
                    resume.OriginalFileName,
                    null,
                    null,
                    false,
                    [],
                    [],
                    [],
                    ExtractionMessage(extractionStatus)));
                continue;
            }

            logger.LogInformation(
                "Best Resume Picker extraction status {ExtractionStatus}. ExtractedChars={ExtractedCharacters}.",
                extractionStatus,
                resumeText.Length);

            var stored = await analysisStore.AnalyzeAndStageAsync(
                resume,
                resumeText,
                jobRequirements,
                jobApplicationId,
                analysisType,
                cancellationToken);
            completed.Add(new CompletedComparison(resume, stored.Result, stored));
        }

        if (!await SaveCompletedAnalysesAsync(userId, completed, cancellationToken))
        {
            throw new InvalidOperationException(
                "Your workspace reached its saved-analysis limit. Delete old analyses before comparing resumes again.");
        }

        var ranked = completed
            .OrderByDescending(item => item.Result.MatchScore)
            .ThenByDescending(item => item.Result.MustHaveCoverage)
            .ThenByDescending(item => item.Result.RequiredCoverage)
            .ThenByDescending(item => item.Result.EvidenceQuality)
            .ThenByDescending(item => item.Result.AtsReadinessScore)
            .ThenByDescending(item => item.Resume.IsDefault)
            .ThenByDescending(item => item.Resume.UploadedAt)
            .ThenBy(item => item.Resume.VersionName)
            .ToList();

        var hasDetectedSkills = ranked.Any(item => item.Result.DetectedJobRequirementCount > 0);
        var winner = hasDetectedSkills && ranked.Count > 0 && ranked[0].Result.ConfidenceScore >= 50
            ? ranked[0]
            : null;
        var topScoreCount = winner is null
            ? 0
            : ranked.Count(item => item.Result.MatchScore == winner.Result.MatchScore);

        var comparedResults = ranked
            .Select((item, index) => new ComparedResumeResult(
                item.Resume.Id,
                item.Resume.VersionName,
                item.Resume.OriginalFileName,
                item.Result.MatchScore,
                index + 1,
                winner is not null && item.Resume.Id == winner.Resume.Id,
                item.Result.MatchedKeywords,
                item.Result.MissingKeywords,
                item.Result.Suggestions,
                null))
            .Concat(unreadable)
            .ToArray();

        return new BestResumePickerResult(
            jobApplicationId,
            contextTitle,
            winner?.Resume.Id,
            winner?.Resume.VersionName,
            winner is null ? null : BuildReason(winner, ranked.Count, topScoreCount),
            resumes.Count,
            completed.Count,
            hasDetectedSkills,
            comparedResults);
    }

    private async Task<bool> SaveCompletedAnalysesAsync(
        string userId,
        IReadOnlyList<CompletedComparison> completed,
        CancellationToken cancellationToken) =>
        await quotaGate.RunAsync(
            WorkspaceQuotaResources.ResumeAnalyses,
            userId,
            async lockCancellationToken =>
            {
                foreach (var item in completed.Where(item => !item.Stored.IsCacheHit))
                {
                    var analysis = item.Stored.Analysis;
                    if (dbContext.Entry(analysis).State != EntityState.Added
                        || string.IsNullOrWhiteSpace(analysis.InputHash))
                    {
                        continue;
                    }

                    var exists = await dbContext.ResumeAnalyses
                        .AsNoTracking()
                        .AnyAsync(candidate =>
                            candidate.UserId == analysis.UserId
                            && candidate.ResumeId == analysis.ResumeId
                            && candidate.JobApplicationId == analysis.JobApplicationId
                            && candidate.AnalysisType == analysis.AnalysisType
                            && candidate.InputHash == analysis.InputHash
                            && candidate.ScoreVersion == analysis.ScoreVersion,
                            lockCancellationToken);
                    if (exists)
                    {
                        dbContext.Entry(analysis).State = EntityState.Detached;
                    }
                }

                var pending = completed
                    .Select(item => item.Stored.Analysis)
                    .Where(analysis => dbContext.Entry(analysis).State == EntityState.Added)
                    .Distinct()
                    .ToArray();
                var incomingBytes = pending.Aggregate(
                    0L,
                    (total, analysis) => checked(total + analysis.SnapshotSizeBytes));
                var withinQuota = await quotas.CanCreateAnalysesAsync(
                    userId,
                    pending.Length,
                    incomingBytes,
                    lockCancellationToken);
                if (!withinQuota)
                {
                    foreach (var analysis in pending)
                    {
                        dbContext.Entry(analysis).State = EntityState.Detached;
                    }
                }

                await dbContext.SaveChangesAsync(lockCancellationToken);
                return withinQuota;
            },
            cancellationToken);

    private static string BuildReason(CompletedComparison winner, int readableCount, int topScoreCount)
    {
        var coverage = $"Its ApplyWise Fit estimate is {winner.Result.OverallScore}%, with {winner.Result.MustHaveCoverage:P0} must-have coverage, {winner.Result.RequiredCoverage:P0} required coverage, {winner.Result.EvidenceQuality:P0} evidence quality, and {winner.Result.AtsReadinessScore}% readiness";
        return topScoreCount > 1
            ? $"{coverage}. It tied for the highest fit estimate; requirement coverage, evidence, readiness, then your default/recency settings resolved the tie among {readableCount} readable resumes."
            : $"{coverage}, the strongest ranked result among {readableCount} readable resume{(readableCount == 1 ? string.Empty : "s")}.";
    }

    private static string ExtractionMessage(string status) => status switch
    {
        nameof(PdfTextExtractionStatus.NoText) => "This document has no readable text and may be image-only. Export a text-based PDF or DOCX to include it in the ranking.",
        nameof(PdfTextExtractionStatus.Encrypted) => "This PDF is encrypted. Upload an unlocked text-based PDF to include it in the ranking.",
        nameof(PdfTextExtractionStatus.PageLimitExceeded) => "This PDF exceeds the supported page limit and was not ranked.",
        nameof(PdfTextExtractionStatus.TextLimitExceeded) => "This PDF exceeds the safe extracted-text limit and was not ranked.",
        nameof(PdfTextExtractionStatus.TimedOut) => "Text extraction timed out for this PDF, so it was not ranked.",
        nameof(PdfTextExtractionStatus.Invalid) => "This PDF is invalid or unsupported and was not ranked.",
        _ => "We could not reliably extract text from this document. Upload a valid text-based PDF or DOCX to include it in the ranking."
    };
}
