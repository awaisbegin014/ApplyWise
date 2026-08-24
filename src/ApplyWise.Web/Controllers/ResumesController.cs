using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.ViewModels.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("resumes")]
public class ResumesController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IResumeStorageService resumeStorage,
    IResumeIngestionService resumeIngestion,
    IWorkspaceQuotaGate quotaGate) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = GetUserId();
        var resumes = await dbContext.Resumes
            .AsNoTracking()
            .Where(resume => resume.UserId == userId)
            .OrderByDescending(resume => resume.IsDefault)
            .ThenByDescending(resume => resume.UploadedAt)
            .Select(resume => new ResumeListItemViewModel(
                resume.Id, resume.VersionName, resume.OriginalFileName, resume.FileSize,
                resume.IsDefault, resume.UploadedAt, resume.Notes))
            .ToListAsync();

        return View(resumes);
    }

    [HttpGet("upload")]
    public IActionResult Create() => View(new ResumeUploadViewModel());

    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(ResumeIngestionLimits.MaxFileSizeBytes + ResumeIngestionLimits.RequestOverheadBytes)]
    public async Task<IActionResult> Create(ResumeUploadViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = GetUserId();
        var ingestion = await resumeIngestion.IngestAsync(
            new ResumeIngestionRequest(
                userId,
                model.VersionName,
                model.ResumeFile,
                model.Notes,
                model.IsDefault),
            HttpContext.RequestAborted);
        if (!ingestion.Succeeded)
        {
            foreach (var error in ingestion.Errors)
            {
                ModelState.AddModelError(nameof(model.ResumeFile), error);
            }

            return View(model);
        }

        var resume = ingestion.Resume!;
        TempData["SuccessMessage"] = $"{resume.VersionName} was uploaded.";
        if (ingestion.InspectionStatus == PdfTextExtractionStatus.NoText)
        {
            TempData["WarningMessage"] =
                "No readable text was found. You can store and download this document, but resume analysis and matching require a text-based PDF or DOCX.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var resume = await FindOwnedResumeAsync(id, true);
        return resume is null ? NotFound() : View(ToDetailsViewModel(resume));
    }

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id)
    {
        var resume = await FindOwnedResumeAsync(id, true);
        if (resume is null)
        {
            return NotFound();
        }

        var absolutePath = resumeStorage.ResolvePath(resume.FilePath);
        if (!System.IO.File.Exists(absolutePath))
        {
            return NotFound();
        }

        return PhysicalFile(absolutePath, resume.ContentType, resume.OriginalFileName, enableRangeProcessing: true);
    }

    [HttpPost("{id:int}/default")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault(int id)
    {
        var userId = GetUserId();
        var resume = await dbContext.Resumes.SingleOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        if (resume is null)
        {
            return NotFound();
        }

        if (resume.IsDefault)
        {
            TempData["SuccessMessage"] = $"{resume.VersionName} is already your default resume.";
            return RedirectToAction(nameof(Index));
        }

        await quotaGate.RunAsync(
            WorkspaceQuotaResources.Resumes,
            userId,
            async cancellationToken =>
            {
                await dbContext.Resumes
                    .Where(item => item.UserId == userId && item.IsDefault)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(item => item.IsDefault, false),
                        cancellationToken);

                resume.IsDefault = true;
                resume.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            HttpContext.RequestAborted);

        TempData["SuccessMessage"] = $"{resume.VersionName} is now your default resume.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var resume = await FindOwnedResumeAsync(id, true);
        return resume is null ? NotFound() : View(ToDetailsViewModel(resume));
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var resume = await FindOwnedResumeAsync(id);
        if (resume is null)
        {
            return NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        await quotaGate.RunAsync(
            WorkspaceQuotaResources.Resumes,
            resume.UserId,
            async cancellationToken =>
            {
                await dbContext.ResumeAnalyses
                    .Where(analysis => analysis.UserId == resume.UserId && analysis.ResumeId == resume.Id)
                    .ExecuteDeleteAsync(cancellationToken);
                await dbContext.JobApplications
                    .Where(application => application.UserId == resume.UserId && application.ResumeId == resume.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(application => application.ResumeId, (int?)null)
                        .SetProperty(application => application.UpdatedAt, now), cancellationToken);

                if (!await dbContext.ResumeFileCleanups.AnyAsync(
                        cleanup => cleanup.FilePath == resume.FilePath,
                        cancellationToken))
                {
                    dbContext.ResumeFileCleanups.Add(new ResumeFileCleanup
                    {
                        FilePath = resume.FilePath,
                        CreatedAt = now,
                        NextAttemptAt = now
                    });
                }

                dbContext.Resumes.Remove(resume);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            HttpContext.RequestAborted);

        TempData["SuccessMessage"] = $"{resume.VersionName} was deleted. Its private file is queued for secure removal.";
        return RedirectToAction(nameof(Index));
    }

    private string GetUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The current user does not have an identifier.");

    private Task<Resume?> FindOwnedResumeAsync(int id, bool readOnly = false)
    {
        IQueryable<Resume> query = dbContext.Resumes;
        if (readOnly)
        {
            query = query.AsNoTracking();
        }

        var userId = GetUserId();
        return query.SingleOrDefaultAsync(resume => resume.Id == id && resume.UserId == userId);
    }

    private static ResumeDetailsViewModel ToDetailsViewModel(Resume resume) =>
        new(resume.Id, resume.VersionName, resume.OriginalFileName, resume.FileSize,
            resume.IsDefault, resume.UploadedAt, resume.UpdatedAt, resume.Notes);
}
