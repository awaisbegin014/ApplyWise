using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.ViewModels.ResumeAnalyzer;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("resume-analyzer")]
public class ResumeAnalyzerController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IResumeStorageService resumeStorage,
    IResumeTextExtractorService textExtractor,
    IResumeIngestionService resumeIngestion,
    IResumeAnalysisStore analysisStore,
    IWorkspaceQuotaService quotas,
    IWorkspaceQuotaGate quotaGate,
    IProductEventRecorder events,
    ILogger<ResumeAnalyzerController> logger) : Controller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? mode,
        int? resumeId,
        int? jobApplicationId,
        int? analysisId)
    {
        var selectedMode = string.Equals(mode, "job", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "saved", StringComparison.OrdinalIgnoreCase)
            || jobApplicationId.HasValue
                ? "job"
                : "ats";
        var model = new AnalyzerIndexViewModel
        {
            Mode = selectedMode,
            SavedAts = new SavedAtsAnalysisViewModel { ResumeId = resumeId },
            Pasted = new PastedRequirementsAnalysisViewModel { ResumeId = resumeId },
            Saved = new SavedApplicationAnalysisViewModel
            {
                ResumeId = resumeId,
                JobApplicationId = jobApplicationId
            }
        };

        if (analysisId.HasValue)
        {
            model.LatestResult = await LoadOwnedResultAsync(analysisId.Value);
            if (model.LatestResult is null)
            {
                return NotFound();
            }

            if (model.LatestResult.AnalysisType == ResumeAnalysisType.PastedRequirements)
            {
                model.Mode = model.LatestResult.HasJobMatch ? "job" : "ats";
                model.Pasted.ResumeId = model.LatestResult.ResumeId;
                model.SavedAts.ResumeId = model.LatestResult.ResumeId;
                model.Pasted.JobRequirements = model.LatestResult.JobDescriptionSnapshot;
            }
            else
            {
                model.Mode = "job";
                model.Saved.ResumeId = model.LatestResult.ResumeId;
                model.Saved.JobApplicationId = model.LatestResult.JobApplicationId;
            }
        }

        await PopulateSelectionsAsync(model);
        return View(model);
    }

    [HttpPost("analyze-pasted-requirements")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("resume-analysis")]
    public async Task<IActionResult> AnalyzePastedRequirements(
        [Bind(Prefix = "Pasted")] PastedRequirementsAnalysisViewModel form)
    {
        var requirements = form.JobRequirements?.Trim() ?? string.Empty;
        if (requirements.Length > 0 && requirements.Length < 30)
        {
            ModelState.AddModelError(
                "Pasted.JobRequirements",
                "Job requirements must be at least 30 characters.");
        }

        var resume = await LoadOwnedResumeAsync(form.ResumeId, "Pasted.ResumeId");
        if (!ModelState.IsValid)
        {
            return await RenderIndexAsync(requirements.Length == 0 ? "ats" : "job", form);
        }

        var resumeText = await GetResumeTextAsync(resume!, "Pasted.ResumeId");
        if (resumeText is null)
        {
            return await RenderIndexAsync(requirements.Length == 0 ? "ats" : "job", form);
        }

        form.JobRequirements = requirements;
        var stored = await analysisStore.AnalyzeAndStageAsync(
            resume!,
            resumeText,
            requirements,
            null,
            ResumeAnalysisType.PastedRequirements,
            HttpContext.RequestAborted);
        var analysisId = await SaveStoredAnalysisAsync(stored);
        if (!analysisId.HasValue) return RedirectToQuotaMessage();
        logger.LogInformation(
            "Resume analysis request completed. AnalysisId={AnalysisId}; CacheHit={CacheHit}; Source={AnalysisSource}.",
            analysisId,
            stored.IsCacheHit,
            ResumeAnalysisType.PastedRequirements);

        return RedirectToAnalysis(analysisId.Value);
    }

    [HttpPost("analyze-saved-resume-ats")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("resume-analysis")]
    public async Task<IActionResult> AnalyzeSavedResumeAts(
        [Bind(Prefix = "SavedAts")] SavedAtsAnalysisViewModel form)
    {
        var resume = await LoadOwnedResumeAsync(form.ResumeId, "SavedAts.ResumeId");
        if (!ModelState.IsValid)
        {
            return await RenderIndexAsync("ats", savedAts: form);
        }

        var resumeText = await GetResumeTextAsync(resume!, "SavedAts.ResumeId");
        if (resumeText is null)
        {
            return await RenderIndexAsync("ats", savedAts: form);
        }

        var ownedResume = resume!;
        var stored = await analysisStore.AnalyzeAndStageAsync(
            ownedResume,
            resumeText,
            string.Empty,
            null,
            ResumeAnalysisType.PastedRequirements,
            HttpContext.RequestAborted);
        var analysisId = await SaveStoredAnalysisAsync(stored);
        if (!analysisId.HasValue) return RedirectToQuotaMessage();
        logger.LogInformation(
            "Saved resume ATS check completed. AnalysisId={AnalysisId}; ResumeId={ResumeId}; CacheHit={CacheHit}.",
            analysisId,
            ownedResume.Id,
            stored.IsCacheHit);

        return RedirectToAnalysis(analysisId.Value);
    }

    [HttpPost("analyze-ats-upload")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(ResumeIngestionLimits.MaxFileSizeBytes + ResumeIngestionLimits.RequestOverheadBytes)]
    public async Task<IActionResult> AnalyzeAtsUpload(
        [Bind(Prefix = "Upload")] AtsResumeUploadViewModel form)
    {
        if (!ModelState.IsValid)
        {
            return await RenderIndexAsync("ats", upload: form);
        }

        var file = form.ResumeFile!;
        var ingestion = await resumeIngestion.IngestAsync(
            new ResumeIngestionRequest(
                GetUserId(),
                VersionNameFromFile(file.FileName),
                file,
                Notes: "Uploaded from ATS Resume Check",
                RequireSelectableText: true),
            HttpContext.RequestAborted);

        if (!ingestion.Succeeded)
        {
            foreach (var error in ingestion.Errors)
            {
                ModelState.AddModelError("Upload.ResumeFile", error);
            }

            return await RenderIndexAsync("ats", upload: form);
        }

        var resume = ingestion.Resume!;
        var resumeText = ingestion.Inspection?.Text;
        if (string.IsNullOrWhiteSpace(resumeText))
        {
            ModelState.AddModelError(
                "Upload.ResumeFile",
                "No readable text was found. Upload a text-based PDF or DOCX exported directly from your editor.");
            return await RenderIndexAsync("ats", upload: form);
        }

        var stored = await analysisStore.AnalyzeAndStageAsync(
            resume,
            resumeText,
            string.Empty,
            null,
            ResumeAnalysisType.PastedRequirements,
            HttpContext.RequestAborted);
        var analysisId = await SaveStoredAnalysisAsync(stored);
        if (!analysisId.HasValue) return RedirectToQuotaMessage();
        logger.LogInformation(
            "Direct ATS upload completed. AnalysisId={AnalysisId}; ResumeId={ResumeId}; CacheHit={CacheHit}.",
            analysisId,
            resume.Id,
            stored.IsCacheHit);

        return RedirectToAnalysis(analysisId.Value);
    }

    [HttpPost("analyze-saved-application")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("resume-analysis")]
    public async Task<IActionResult> AnalyzeSavedApplication(
        [Bind(Prefix = "Saved")] SavedApplicationAnalysisViewModel form)
    {
        var userId = GetUserId();
        var resume = await LoadOwnedResumeAsync(form.ResumeId, "Saved.ResumeId");
        JobApplication? application = null;

        if (form.JobApplicationId.HasValue)
        {
            application = await dbContext.JobApplications
                .AsNoTracking()
                .SingleOrDefaultAsync(item =>
                    item.Id == form.JobApplicationId.Value && item.UserId == userId,
                    HttpContext.RequestAborted);
            if (application is null)
            {
                ModelState.AddModelError(
                    "Saved.JobApplicationId",
                    "Select a job application from your own tracker.");
            }
            else if (string.IsNullOrWhiteSpace(application.JobDescription))
            {
                ModelState.AddModelError(
                    "Saved.JobApplicationId",
                    "This job application does not have a job description. Add one before analyzing.");
            }
        }

        if (!ModelState.IsValid)
        {
            return await RenderIndexAsync("job", saved: form);
        }

        var resumeText = await GetResumeTextAsync(resume!, "Saved.ResumeId");
        if (resumeText is null)
        {
            return await RenderIndexAsync("job", saved: form);
        }

        var stored = await analysisStore.AnalyzeAndStageAsync(
            resume!,
            resumeText,
            application!.JobDescription!,
            application.Id,
            ResumeAnalysisType.SavedApplication,
            HttpContext.RequestAborted);
        var analysisId = await SaveStoredAnalysisAsync(stored);
        if (!analysisId.HasValue) return RedirectToQuotaMessage();
        logger.LogInformation(
            "Resume analysis request completed. AnalysisId={AnalysisId}; CacheHit={CacheHit}; Source={AnalysisSource}.",
            analysisId,
            stored.IsCacheHit,
            ResumeAnalysisType.SavedApplication);

        return RedirectToAnalysis(analysisId.Value);
    }

    [HttpGet("history")]
    public async Task<IActionResult> History()
    {
        var userId = GetUserId();
        var analyses = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Include(item => item.Resume)
            .Include(item => item.JobApplication)
            .OrderByDescending(item => item.CreatedAt)
            .Take(100)
            .ToListAsync(HttpContext.RequestAborted);

        return View(new AnalysisHistoryViewModel
        {
            Analyses = analyses.Select(ToHistoryItemViewModel).ToArray()
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var result = await LoadOwnedResultAsync(id);
        return result is null ? NotFound() : View(result);
    }

    private async Task<int?> SaveStoredAnalysisAsync(StoredResumeAnalysis stored)
    {
        if (stored.IsCacheHit)
        {
            await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
            await events.RecordAsync(
                ProductEventNames.ResumeAnalysisCompleted,
                stored.Analysis.AnalysisType.ToString(),
                stored.Analysis.UserId,
                cancellationToken: HttpContext.RequestAborted);
            return stored.Analysis.Id;
        }

        var analysisId = await quotaGate.RunAsync(
            WorkspaceQuotaResources.ResumeAnalyses,
            stored.Analysis.UserId,
            async cancellationToken =>
            {
                if (!string.IsNullOrWhiteSpace(stored.Analysis.InputHash))
                {
                    var existingId = await dbContext.ResumeAnalyses
                        .AsNoTracking()
                        .Where(item => item.UserId == stored.Analysis.UserId
                            && item.ResumeId == stored.Analysis.ResumeId
                            && item.JobApplicationId == stored.Analysis.JobApplicationId
                            && item.AnalysisType == stored.Analysis.AnalysisType
                            && item.InputHash == stored.Analysis.InputHash
                            && item.ScoreVersion == stored.Analysis.ScoreVersion)
                        .Select(item => (int?)item.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (existingId.HasValue)
                    {
                        dbContext.Entry(stored.Analysis).State = EntityState.Detached;
                        await dbContext.SaveChangesAsync(cancellationToken);
                        return existingId;
                    }
                }

                if (!await quotas.CanCreateAnalysisAsync(
                        stored.Analysis.UserId,
                        stored.Analysis.SnapshotSizeBytes,
                        cancellationToken))
                {
                    dbContext.Entry(stored.Analysis).State = EntityState.Detached;
                    return null;
                }

                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return (int?)stored.Analysis.Id;
                }
                catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(stored.Analysis.InputHash))
                {
                    var candidate = stored.Analysis;
                    dbContext.ChangeTracker.Clear();
                    var existingId = await dbContext.ResumeAnalyses
                        .AsNoTracking()
                        .Where(item => item.UserId == candidate.UserId
                            && item.ResumeId == candidate.ResumeId
                            && item.JobApplicationId == candidate.JobApplicationId
                            && item.AnalysisType == candidate.AnalysisType
                            && item.InputHash == candidate.InputHash
                            && item.ScoreVersion == candidate.ScoreVersion)
                        .Select(item => (int?)item.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (!existingId.HasValue) throw;

                    logger.LogInformation(
                        "A concurrent identical analysis was reused after the cache uniqueness check. AnalysisId={AnalysisId}.",
                        existingId.Value);
                    return existingId;
                }
            },
            HttpContext.RequestAborted);

        if (!analysisId.HasValue)
        {
            return null;
        }

        await events.RecordAsync(
            ProductEventNames.ResumeAnalysisCompleted,
            stored.Analysis.AnalysisType.ToString(),
            stored.Analysis.UserId,
            cancellationToken: HttpContext.RequestAborted);
        return analysisId;
    }

    private IActionResult RedirectToQuotaMessage()
    {
        TempData["AnalysisError"] =
            "Your workspace reached its saved-analysis limit. Delete an old analysis, then try again.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<Resume?> LoadOwnedResumeAsync(int? resumeId, string modelStateKey)
    {
        if (!resumeId.HasValue)
        {
            return null;
        }

        var userId = GetUserId();
        var resume = await dbContext.Resumes.SingleOrDefaultAsync(
            item => item.Id == resumeId.Value && item.UserId == userId,
            HttpContext.RequestAborted);
        if (resume is null)
        {
            ModelState.AddModelError(modelStateKey, "Select a resume from your own resume library.");
        }

        return resume;
    }

    private async Task<string?> GetResumeTextAsync(Resume resume, string modelStateKey)
    {
        var resumeText = resume.ExtractedText;
        if (!string.IsNullOrWhiteSpace(resumeText))
        {
            logger.LogInformation(
                "Resume extraction status {ExtractionStatus}. ExtractedChars={ExtractedCharacters}.",
                "Cached",
                resumeText.Length);
        }
        if (string.IsNullOrWhiteSpace(resumeText))
        {
            var absolutePath = resumeStorage.ResolvePath(resume.FilePath);
            var inspection = System.IO.File.Exists(absolutePath)
                ? await textExtractor.InspectAsync(absolutePath, HttpContext.RequestAborted)
                : new PdfTextExtractionResult(PdfTextExtractionStatus.Unavailable);
            resumeText = inspection.Text;
            logger.LogInformation(
                "Resume extraction status {ExtractionStatus}. ExtractedChars={ExtractedCharacters}.",
                inspection.Status,
                resumeText?.Length ?? 0);
            if (inspection.Status != PdfTextExtractionStatus.Success || string.IsNullOrWhiteSpace(resumeText))
            {
                ModelState.AddModelError(modelStateKey, ExtractionMessage(inspection.Status));
                return null;
            }

            resume.ExtractedText = resumeText;
            resume.PageCount = inspection.PageCount;
            resume.FileDiagnosticsJson = inspection.Diagnostics is null
                ? null
                : JsonSerializer.Serialize(inspection.Diagnostics, JsonOptions);
            resume.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return resumeText;
    }

    private static string ExtractionMessage(PdfTextExtractionStatus status) => status switch
    {
        PdfTextExtractionStatus.NoText => "This document has no readable text and may be image-only. Export a text-based PDF or DOCX and try again.",
        PdfTextExtractionStatus.Encrypted => "This PDF is encrypted or password protected. Upload an unlocked text-based PDF.",
        PdfTextExtractionStatus.Invalid => "This resume file is invalid or exceeds the supported file size. Export a fresh PDF or DOCX and try again.",
        PdfTextExtractionStatus.PageLimitExceeded => "This PDF exceeds the supported page limit. Upload a shorter resume.",
        PdfTextExtractionStatus.TextLimitExceeded => "This PDF contains too much extracted text to analyze safely.",
        PdfTextExtractionStatus.TimedOut => "PDF text extraction timed out. Export a simpler text-based PDF and try again.",
        _ => "We could not read text from this document. Please upload a valid text-based PDF or DOCX resume."
    };

    private async Task<IActionResult> RenderIndexAsync(
        string mode,
        PastedRequirementsAnalysisViewModel? pasted = null,
        SavedApplicationAnalysisViewModel? saved = null,
        AtsResumeUploadViewModel? upload = null,
        SavedAtsAnalysisViewModel? savedAts = null)
    {
        var model = new AnalyzerIndexViewModel
        {
            Mode = mode,
            Upload = upload ?? new AtsResumeUploadViewModel(),
            SavedAts = savedAts ?? new SavedAtsAnalysisViewModel(),
            Pasted = pasted ?? new PastedRequirementsAnalysisViewModel(),
            Saved = saved ?? new SavedApplicationAnalysisViewModel()
        };
        await PopulateSelectionsAsync(model);
        return View("Index", model);
    }

    private async Task PopulateSelectionsAsync(AnalyzerIndexViewModel model)
    {
        var userId = GetUserId();
        model.AvailableResumes = await dbContext.Resumes
            .AsNoTracking()
            .Where(resume => resume.UserId == userId)
            .OrderByDescending(resume => resume.IsDefault)
            .ThenByDescending(resume => resume.UploadedAt)
            .Select(resume => new SelectListItem
            {
                Value = resume.Id.ToString(),
                Text = resume.VersionName + (resume.IsDefault ? " (Default)" : string.Empty)
            })
            .ToListAsync(HttpContext.RequestAborted);

        model.AvailableJobApplications = await dbContext.JobApplications
            .AsNoTracking()
            .Where(application => application.UserId == userId)
            .OrderByDescending(application => application.CreatedAt)
            .Select(application => new SelectListItem
            {
                Value = application.Id.ToString(),
                Text = application.JobTitle + " at " + application.CompanyName
                    + (application.JobDescription == null || application.JobDescription == string.Empty
                        ? " (Description needed)"
                        : string.Empty)
            })
            .ToListAsync(HttpContext.RequestAborted);

        model.Pasted.ResumeId = SelectOwnedOrFirst(model.Pasted.ResumeId, model.AvailableResumes);
        model.SavedAts.ResumeId = SelectOwnedOrFirst(model.SavedAts.ResumeId, model.AvailableResumes);
        model.Saved.ResumeId = SelectOwnedOrFirst(model.Saved.ResumeId, model.AvailableResumes);
        model.Saved.JobApplicationId = SelectOwnedOrFirst(
            model.Saved.JobApplicationId,
            model.AvailableJobApplications);
    }

    private async Task<AnalysisResultViewModel?> LoadOwnedResultAsync(int id)
    {
        var userId = GetUserId();
        var analysis = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Include(item => item.Resume)
            .Include(item => item.JobApplication)
            .SingleOrDefaultAsync(
                item => item.Id == id && item.UserId == userId,
                HttpContext.RequestAborted);

        if (analysis is null) return null;

        var model = ToResultViewModel(analysis);
        var hasJobContext = analysis.JobMatchScore.HasValue
            && !string.IsNullOrWhiteSpace(analysis.JobDescriptionSnapshot);
        var previous = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Where(item => item.UserId == userId
                && item.AnalysisType == analysis.AnalysisType
                && item.ScoreVersion == analysis.ScoreVersion
                && (hasJobContext
                    ? item.JobApplicationId == analysis.JobApplicationId
                        && item.JobDescriptionSnapshot == analysis.JobDescriptionSnapshot
                    : item.ResumeId == analysis.ResumeId
                        && (item.JobDescriptionSnapshot == null || item.JobDescriptionSnapshot == string.Empty))
                && (item.CreatedAt < analysis.CreatedAt
                    || (item.CreatedAt == analysis.CreatedAt && item.Id < analysis.Id)))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (previous is not null)
        {
            model.PreviousScore = previous.MatchScore;
            var previousReview = DeserializeReview(previous.ReviewJson);
            var previousIssueKeys = previousReview.ReviewItems
                .GroupBy(ReviewKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Issue, StringComparer.OrdinalIgnoreCase);
            var currentIssueKeys = model.ReviewItems
                .GroupBy(ReviewKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Issue, StringComparer.OrdinalIgnoreCase);
            model.ResolvedIssues = previousIssueKeys
                .Where(item => !currentIssueKeys.ContainsKey(item.Key))
                .Select(item => item.Value)
                .Take(12)
                .ToArray();
            model.RemainingIssues = currentIssueKeys
                .Where(item => previousIssueKeys.ContainsKey(item.Key))
                .Select(item => item.Value)
                .Take(12)
                .ToArray();
            var previousEvidence = DeserializeArray<MatchEvidence>(previous.EvidenceJson)
                .Select(item => item.RequirementName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            model.NewlyDetectedEvidence = model.Evidence
                .Select(item => item.RequirementName)
                .Where(item => !previousEvidence.Contains(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray();
        }

        return model;
    }

    private string GetUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The current user does not have an identifier.");

    private static IActionResult RedirectToAnalysis(int analysisId) =>
        new RedirectToActionResult(
            nameof(Index),
            controllerName: null,
            routeValues: new { analysisId },
            fragment: $"analysis-result-{analysisId}");

    private static string VersionNameFromFile(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName));
        var cleanName = new string(baseName
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            return "ATS Resume";
        }

        return cleanName.Length <= 100 ? cleanName : cleanName[..100].TrimEnd();
    }

    private static int? SelectOwnedOrFirst(int? selectedId, IReadOnlyList<SelectListItem> items)
    {
        if (selectedId.HasValue && items.Any(item => item.Value == selectedId.Value.ToString()))
        {
            return selectedId;
        }

        return items.Count == 0 ? null : int.Parse(items[0].Value);
    }

    private static AnalysisResultViewModel ToResultViewModel(ResumeAnalysis analysis)
    {
        var isSavedApplication = analysis.AnalysisType == ResumeAnalysisType.SavedApplication
            && analysis.JobApplication is not null;
        var matched = DeserializeArray<string>(analysis.MatchedKeywordsJson);
        var missing = DeserializeArray<string>(analysis.MissingKeywordsJson);
        var review = DeserializeReview(analysis.ReviewJson);
        var hasJobDescription = !string.IsNullOrWhiteSpace(analysis.JobDescriptionSnapshot);
        return new AnalysisResultViewModel
        {
            Id = analysis.Id,
            ResumeId = analysis.ResumeId,
            JobApplicationId = analysis.JobApplicationId,
            AnalysisType = analysis.AnalysisType,
            ResumeVersionName = analysis.Resume?.VersionName ?? "Resume",
            ContextTitle = isSavedApplication
                ? $"{analysis.JobApplication!.JobTitle} at {analysis.JobApplication.CompanyName}"
                : hasJobDescription ? "Pasted job requirements" : "ATS resume check",
            ContextSubtitle = isSavedApplication ? "Saved application" : hasJobDescription ? "Direct input" : "General readiness · no job description needed",
            ResumeTextSnapshot = !string.IsNullOrWhiteSpace(analysis.Resume?.ExtractedText)
                ? analysis.Resume.ExtractedText
                : analysis.ResumeTextSnapshot,
            JobDescriptionSnapshot = analysis.JobDescriptionSnapshot,
            OverallScore = analysis.MatchScore,
            AtsReadinessScore = analysis.AtsReadinessScore,
            JobMatchScore = analysis.JobMatchScore,
            ConfidenceScore = analysis.ConfidenceScore,
            ScoreVersion = analysis.ScoreVersion ?? "legacy-v1",
            MatchedKeywords = matched,
            MissingKeywords = missing,
            Suggestions = DeserializeArray<string>(analysis.SuggestionsJson),
            ScoreBreakdown = DeserializeArray<ScoreComponent>(analysis.ScoreBreakdownJson),
            Evidence = DeserializeArray<MatchEvidence>(analysis.EvidenceJson),
            Warnings = DeserializeArray<AnalysisWarning>(analysis.WarningsJson),
            ReviewItems = review.ReviewItems,
            SectionReviews = review.SectionReviews,
            BulletReviews = review.BulletReviews,
            MissingRequirements = review.MissingRequirements,
            DetectedJobRequirementCount = analysis.DetectedJobRequirementCount ?? matched.Count + missing.Count,
            MustHaveCoverage = analysis.MustHaveCoverage ?? review.MustHaveCoverage,
            RequiredCoverage = analysis.RequiredCoverage ?? review.RequiredCoverage,
            EvidenceQuality = analysis.EvidenceQuality ?? review.EvidenceQuality,
            CreatedAt = analysis.CreatedAt
        };
    }

    private static AnalysisHistoryItemViewModel ToHistoryItemViewModel(ResumeAnalysis analysis)
    {
        var saved = analysis.AnalysisType == ResumeAnalysisType.SavedApplication && analysis.JobApplication is not null;
        var hasJob = !string.IsNullOrWhiteSpace(analysis.JobDescriptionSnapshot);
        return new AnalysisHistoryItemViewModel(
            analysis.Id,
            analysis.Resume?.VersionName ?? "Resume",
            saved ? $"{analysis.JobApplication!.JobTitle} at {analysis.JobApplication.CompanyName}" : hasJob ? "Pasted job requirements" : "ATS resume check",
            saved ? "Saved application" : hasJob ? "Direct input" : "General readiness estimate",
            analysis.AnalysisType,
            analysis.MatchScore,
            analysis.AtsReadinessScore,
            analysis.JobMatchScore,
            analysis.ScoreVersion ?? "legacy-v1",
            analysis.CreatedAt);
    }

    private static IReadOnlyList<T> DeserializeArray<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<T[]>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static ReviewSnapshot DeserializeReview(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ReviewSnapshot();
        try { return JsonSerializer.Deserialize<ReviewSnapshot>(json, JsonOptions) ?? new ReviewSnapshot(); }
        catch (JsonException) { return new ReviewSnapshot(); }
    }

    private static string ReviewKey(ReviewItem item) =>
        string.Join('|', item.Category, item.ResumeSection, item.Issue, item.RelatedJobRequirement);

    private sealed class ReviewSnapshot
    {
        public ReviewItem[] ReviewItems { get; init; } = [];
        public SectionReview[] SectionReviews { get; init; } = [];
        public BulletReview[] BulletReviews { get; init; } = [];
        public JobRequirement[] MissingRequirements { get; init; } = [];
        public double MustHaveCoverage { get; init; }
        public double RequiredCoverage { get; init; }
        public double EvidenceQuality { get; init; }
    }
}
