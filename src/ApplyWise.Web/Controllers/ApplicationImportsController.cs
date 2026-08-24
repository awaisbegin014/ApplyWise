using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Gmail;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.ViewModels.ApplicationImports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("application-imports")]
public sealed class ApplicationImportsController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IGmailImportService gmailImportService,
    IApplicationImportProcessor importProcessor,
    IApplicationLockProvider operationLocks,
    IOptions<GoogleIntegrationOptions> googleOptions) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = GetUserId();
        var connection = await dbContext.GmailConnections
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new GmailConnectionSummaryViewModel(
                item.EmailAddress,
                item.ConnectedAt,
                item.LastSuccessfulSyncAt,
                item.LastSyncStartedAt,
                item.LastErrorCode,
                item.AutoAddHighConfidenceApplications))
            .SingleOrDefaultAsync(HttpContext.RequestAborted);
        var pendingImports = await dbContext.ApplicationImports
            .AsNoTracking()
            .Where(item =>
                item.UserId == userId
                && item.Status == ApplicationImportStatus.PendingReview)
            .OrderByDescending(item => item.DetectedAt)
            .Take(100)
            .Select(item => new ApplicationImportListItemViewModel(
                item.Id,
                item.EmailSubject,
                item.CompanyName,
                item.JobTitle,
                item.Source,
                item.Direction,
                item.Confidence,
                item.AppliedDate,
                item.ResumeFileName,
                item.DetectedAt))
            .ToListAsync(HttpContext.RequestAborted);
        var recentRows = await (
                from import in dbContext.ApplicationImports.AsNoTracking()
                join application in dbContext.JobApplications.AsNoTracking()
                    on import.CreatedApplicationId equals application.Id
                where import.UserId == userId
                    && application.UserId == userId
                    && import.Status == ApplicationImportStatus.AutoAccepted
                    && import.CreatedApplicationId.HasValue
                orderby import.ReviewedAt descending, import.DetectedAt descending
                select new RecentlyAutoAddedApplicationViewModel(
                    application.Id,
                    application.CompanyName,
                    application.JobTitle,
                    application.AppliedDate,
                    import.ReviewedAt ?? import.DetectedAt))
            .Take(50)
            .ToListAsync(HttpContext.RequestAborted);
        var recentlyAutoAdded = recentRows
            .DistinctBy(item => item.ApplicationId)
            .Take(10)
            .ToList();

        return View(new ApplicationImportIndexViewModel
        {
            GoogleIntegrationConfigured = googleOptions.Value.IsGmailImportConfigured,
            AutoAddHighConfidenceApplications =
                connection?.AutoAddHighConfidenceApplications ?? false,
            GmailConnection = connection,
            PendingImports = pendingImports,
            RecentlyAutoAddedApplications = recentlyAutoAdded
        });
    }

    [HttpPost("auto-add-preference")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAutoAddPreference(
        bool autoAddHighConfidenceApplications)
    {
        var userId = GetUserId();
        await using var operationLease = await operationLocks.TryAcquireAsync(
            $"gmail-user:{userId}",
            Timeout.InfiniteTimeSpan,
            HttpContext.RequestAborted)
            ?? throw new ResourceLockUnavailableException(
                "The Gmail connection could not be locked while changing its preferences.");

        dbContext.ChangeTracker.Clear();
        var connection = await dbContext.GmailConnections
            .SingleOrDefaultAsync(
                item => item.UserId == userId,
                HttpContext.RequestAborted);
        if (connection is null) return NotFound();
        if (connection.LastErrorCode == GmailConnectionStates.RevocationPending)
        {
            TempData["ImportError"] =
                "Gmail is being disconnected. Reconnect it before changing automatic tracking.";
            return RedirectToAction(nameof(Index));
        }

        connection.AutoAddHighConfidenceApplications =
            autoAddHighConfidenceApplications;
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
        TempData["ImportSuccess"] = autoAddHighConfidenceApplications
            ? "Automatic tracking is enabled. Eligible confirmations will be added during Gmail sync."
            : "Automatic tracking is disabled. New Gmail suggestions will wait for review.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("sync")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("gmail-sync")]
    public async Task<IActionResult> Sync()
    {
        var result = await gmailImportService.SyncUserAsync(
            GetUserId(),
            HttpContext.RequestAborted);
        TempData[result.Succeeded ? "ImportSuccess" : "ImportError"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/review")]
    public async Task<IActionResult> Review(int id)
    {
        var item = await FindPendingImportAsync(id);
        if (item is null) return NotFound();
        return View(ToReviewModel(item));
    }

    [HttpPost("{id:int}/review")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(
        int id,
        ApplicationImportReviewViewModel model)
    {
        if (id != model.Id) return BadRequest();
        var item = await FindPendingImportAsync(id);
        if (item is null) return NotFound();
        if (!ModelState.IsValid)
        {
            CopyEvidence(item, model);
            return View(model);
        }

        var processingResult = await importProcessor.AcceptManuallyAsync(
            id,
            GetUserId(),
            new ManualApplicationImportData(
                model.CompanyName,
                model.JobTitle,
                model.JobLocation,
                model.Source,
                model.JobUrl,
                model.AppliedDate),
            HttpContext.RequestAborted);
        if (processingResult.Outcome
            is ApplicationImportProcessOutcome.NotFound
            or ApplicationImportProcessOutcome.OwnershipConflict)
        {
            return NotFound();
        }
        if (!processingResult.ApplicationId.HasValue)
        {
            ModelState.AddModelError(
                string.Empty,
                processingResult.Outcome == ApplicationImportProcessOutcome.QuotaExceeded
                    ? "Your workspace reached its application limit. Delete old applications before accepting this suggestion."
                    : "This Gmail suggestion could not be added. Refresh the page and try again.");
            CopyEvidence(item, model);
            return View(model);
        }

        TempData["SuccessMessage"] =
            $"{processingResult.JobTitle} at {processingResult.CompanyName} was added from Gmail.";
        return RedirectToAction(
            "Details",
            "JobApplications",
            new { id = processingResult.ApplicationId.Value });
    }

    [HttpPost("{id:int}/dismiss")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(int id)
    {
        var item = await FindPendingImportAsync(id);
        if (item is null) return NotFound();
        item.Status = ApplicationImportStatus.Dismissed;
        item.ReviewedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
        TempData["ImportSuccess"] = "The email suggestion was dismissed.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<ApplicationImport?> FindPendingImportAsync(int id)
    {
        var userId = GetUserId();
        return await dbContext.ApplicationImports.SingleOrDefaultAsync(
            item =>
                item.Id == id
                && item.UserId == userId
                && item.Status == ApplicationImportStatus.PendingReview,
            HttpContext.RequestAborted);
    }

    private static ApplicationImportReviewViewModel ToReviewModel(
        ApplicationImport item) =>
        new()
        {
            Id = item.Id,
            EmailSubject = item.EmailSubject,
            SenderDomain = item.SenderDomain,
            Direction = item.Direction,
            Confidence = item.Confidence,
            ResumeFileName = item.ResumeFileName,
            CompanyName = item.CompanyName,
            JobTitle = item.JobTitle,
            JobLocation = item.JobLocation,
            Source = item.Source,
            JobUrl = item.JobUrl,
            AppliedDate = item.AppliedDate
        };

    private static void CopyEvidence(
        ApplicationImport item,
        ApplicationImportReviewViewModel model)
    {
        model.EmailSubject = item.EmailSubject;
        model.SenderDomain = item.SenderDomain;
        model.Direction = item.Direction;
        model.Confidence = item.Confidence;
        model.ResumeFileName = item.ResumeFileName;
    }

    private string GetUserId() =>
        userManager.GetUserId(User)
        ?? throw new InvalidOperationException(
            "The current user does not have an identifier.");
}
