using ApplyWise.Web.Services.Ai;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Subscriptions;
using ApplyWise.Web.ViewModels.AiCoach;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("ai-coach")]
public sealed class AiCoachController(
    UserManager<IdentityUser> userManager,
    IGeminiResumeCoach resumeCoach,
    ISubscriptionService subscriptionService,
    IProductEventRecorder eventRecorder) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var model = new AiCoachViewModel();
        await PopulateAsync(model);
        return View(model);
    }

    [HttpPost("improve-bullet")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("ai-coach")]
    public async Task<IActionResult> ImproveBullet(AiCoachViewModel model)
    {
        model.Bullet = model.Bullet?.Trim() ?? string.Empty;
        model.JobContext = string.IsNullOrWhiteSpace(model.JobContext)
            ? null
            : model.JobContext.Trim();
        var userId = GetUserId();

        if (!resumeCoach.IsConfigured)
        {
            ModelState.AddModelError(string.Empty, "The AI coach is being configured and is not available yet.");
        }
        if (!model.AiProcessingConsent)
        {
            ModelState.AddModelError(
                nameof(model.AiProcessingConsent),
                "Confirm AI processing before requesting suggestions.");
        }
        if (!ModelState.IsValid)
        {
            await PopulateAsync(model);
            return View("Index", model);
        }

        var promptCharacters = model.Bullet.Length + (model.JobContext?.Length ?? 0);
        var reservation = await subscriptionService.TryReserveAiUseAsync(
            userId,
            "resume-bullet-coach",
            promptCharacters,
            HttpContext.RequestAborted);
        if (!reservation.Allowed || !reservation.UsageRecordId.HasValue)
        {
            model.Subscription = reservation.Snapshot;
            model.AiConfigured = true;
            ModelState.AddModelError(
                string.Empty,
                reservation.Snapshot.IsPro
                    ? "You have used this month's Pro AI allowance. It resets next month."
                    : "Your free AI trials are used. Upgrade to Pro to continue using the AI coach.");
            return View("Index", model);
        }

        var succeeded = false;
        var responseCharacters = 0;
        try
        {
            model.Result = await resumeCoach.ImproveBulletAsync(
                model.Bullet,
                model.JobContext,
                HttpContext.RequestAborted);
            responseCharacters = model.Result.Assessment.Length
                + model.Result.Rewrites.Sum(item => item.Text.Length + item.Rationale.Length);
            succeeded = true;
            await eventRecorder.RecordAsync(
                ProductEventNames.AiBulletCoachCompleted,
                "ai-coach",
                userId,
                cancellationToken: HttpContext.RequestAborted);
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }
        finally
        {
            await subscriptionService.CompleteAiUseAsync(
                reservation.UsageRecordId.Value,
                succeeded,
                responseCharacters,
                resumeCoach.ModelName,
                HttpContext.RequestAborted);
        }

        await PopulateAsync(model);
        return View("Index", model);
    }

    private async Task PopulateAsync(AiCoachViewModel model)
    {
        model.Subscription = await subscriptionService.GetSnapshotAsync(
            GetUserId(),
            HttpContext.RequestAborted);
        model.AiConfigured = resumeCoach.IsConfigured;
    }

    private string GetUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The current user does not have an identifier.");
}
