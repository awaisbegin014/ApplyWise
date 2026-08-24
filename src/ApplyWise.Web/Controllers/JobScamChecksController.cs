using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.JobScamDetection;
using ApplyWise.Web.ViewModels.JobScamChecks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("scam-checks")]
public class JobScamChecksController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IJobScamDetectorService detectorService,
    IProductEventRecorder events,
    IWorkspaceQuotaService quotas,
    IWorkspaceQuotaGate quotaGate) : Controller
{
    private const int HistoryPageSize = 25;

    [HttpPost("analyze")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("scam-check")]
    public async Task<IActionResult> Analyze(RunScamCheckViewModel model)
    {
        if (!ModelState.IsValid) return BadRequest();
        var userId = GetUserId();
        var application = await dbContext.JobApplications.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == model.JobApplicationId!.Value && item.UserId == userId);
        if (application is null) return NotFound();
        if (!await quotas.CanCreateScamCheckAsync(userId, HttpContext.RequestAborted))
        {
            TempData["ErrorMessage"] =
                "Your workspace reached its saved job-post review limit.";
            return RedirectToAction("Details", "JobApplications", new { id = application.Id });
        }

        var result = detectorService.AnalyzeJob(application);
        var check = new JobScamCheck
        {
            UserId = userId,
            JobApplicationId = application.Id,
            RiskScore = result.RiskScore,
            RiskLevel = result.RiskLevel,
            RedFlagsJson = JsonSerializer.Serialize(result.RedFlags),
            QualityScore = result.QualityScore,
            MissingInformationJson = JsonSerializer.Serialize(result.MissingInformation),
            Recommendation = result.Recommendation,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var saved = await quotaGate.RunAsync(
            WorkspaceQuotaResources.ScamChecks,
            userId,
            async cancellationToken =>
            {
                if (!await quotas.CanCreateScamCheckAsync(userId, cancellationToken))
                {
                    return false;
                }

                dbContext.JobScamChecks.Add(check);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            HttpContext.RequestAborted);
        if (!saved)
        {
            TempData["ErrorMessage"] =
                "Your workspace reached its saved job-post review limit.";
            return RedirectToAction("Details", "JobApplications", new { id = application.Id });
        }

        await events.RecordAsync(
            ProductEventNames.ScamCheckCompleted,
            "job_application",
            userId,
            cancellationToken: HttpContext.RequestAborted);
        return RedirectToAction(nameof(Details), new { id = check.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var userId = GetUserId();
        var check = await dbContext.JobScamChecks.AsNoTracking()
            .Include(item => item.JobApplication)
            .SingleOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        return check is null ? NotFound() : View(ToDetails(check));
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(int page = 1)
    {
        var userId = GetUserId();
        var totalCount = await dbContext.JobScamChecks
            .CountAsync(check => check.UserId == userId, HttpContext.RequestAborted);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)HistoryPageSize));
        var currentPage = Math.Clamp(page, 1, totalPages);
        var checks = await dbContext.JobScamChecks.AsNoTracking()
            .Where(check => check.UserId == userId)
            .OrderByDescending(check => check.CreatedAt)
            .Skip((currentPage - 1) * HistoryPageSize)
            .Take(HistoryPageSize)
            .Select(check => new JobScamCheckHistoryItemViewModel(
                check.Id, check.JobApplication!.CompanyName, check.JobApplication.JobTitle,
                check.RiskScore, check.RiskLevel, check.QualityScore, check.CreatedAt))
            .ToListAsync(HttpContext.RequestAborted);
        return View(new JobScamCheckHistoryViewModel
        {
            Checks = checks,
            Page = currentPage,
            TotalPages = totalPages,
            TotalCount = totalCount
        });
    }

    private string GetUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The current user does not have an identifier.");

    private static JobScamCheckDetailsViewModel ToDetails(JobScamCheck check) => new(
        check.Id, check.JobApplicationId, check.JobApplication!.CompanyName, check.JobApplication.JobTitle,
        check.RiskScore, check.RiskLevel, Deserialize(check.RedFlagsJson), check.QualityScore,
        Deserialize(check.MissingInformationJson), check.Recommendation, check.CreatedAt);

    private static IReadOnlyList<string> Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
