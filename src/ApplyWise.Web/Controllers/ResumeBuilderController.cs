using System.Security.Claims;
using ApplyWise.Web.ViewModels.ResumeBuilder;
using ApplyWise.Web.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("resume-builder")]
public sealed class ResumeBuilderController(
    ISubscriptionService subscriptions) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? section = null)
    {
        var accountId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(accountId)) return Challenge();

        var subscription = await subscriptions.GetSnapshotAsync(accountId, HttpContext.RequestAborted);
        return View(ResumeBuilderPageViewModel.CreateForAccount(accountId, subscription, section));
    }

    [HttpPost("authorize-export")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("resume-analysis")]
    public async Task<IActionResult> AuthorizeExport([FromForm] string? templateId)
    {
        var accountId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(accountId)) return Challenge();

        var entitlement = await subscriptions.TryConsumeResumeBuildAsync(
            accountId,
            templateId,
            HttpContext.RequestAborted);
        return entitlement.Allowed
            ? Ok(new
            {
                allowed = true,
                remaining = entitlement.Snapshot.IsPro
                    ? (int?)null
                    : entitlement.Snapshot.ResumeBuildsRemaining,
                isPro = entitlement.Snapshot.IsPro
            })
            : StatusCode(StatusCodes.Status402PaymentRequired, new
            {
                allowed = false,
                message = "Your two free resume downloads are used. Upgrade to Pro for unlimited resume building."
            });
    }
}
