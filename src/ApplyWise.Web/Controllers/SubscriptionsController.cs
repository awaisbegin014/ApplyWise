using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Subscriptions;
using ApplyWise.Web.ViewModels.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("pro")]
public sealed class SubscriptionsController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    ISubscriptionService subscriptionService,
    IOptions<SubscriptionOptions> options,
    IProductEventRecorder eventRecorder) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = GetUserId();
        return View(new SubscriptionPageViewModel
        {
            Subscription = await subscriptionService.GetSnapshotAsync(
                userId,
                HttpContext.RequestAborted),
            Options = options.Value,
            LatestRequest = await dbContext.ProUpgradeRequests
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderByDescending(item => item.SubmittedAt)
                .FirstOrDefaultAsync(HttpContext.RequestAborted)
        });
    }

    [HttpGet("upgrade")]
    public async Task<IActionResult> Upgrade()
    {
        var snapshot = await subscriptionService.GetSnapshotAsync(
            GetUserId(),
            HttpContext.RequestAborted);
        if (snapshot.IsPro)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new ProUpgradeInputViewModel
        {
            Amount = options.Value.ProPrice,
            Options = options.Value
        });
    }

    [HttpPost("upgrade")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("pro-upgrade")]
    public async Task<IActionResult> Upgrade(ProUpgradeInputViewModel model)
    {
        model.Options = options.Value;
        if (model.Amount != options.Value.ProPrice)
        {
            ModelState.AddModelError(
                nameof(model.Amount),
                $"Enter the displayed Pro price: {options.Value.Currency} {options.Value.ProPrice:0.##}.");
        }
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = GetUserId();
        var result = await subscriptionService.SubmitUpgradeRequestAsync(
            userId,
            new UpgradeSubmission(
                model.PaymentMethod,
                model.TransactionReference,
                model.PayerName,
                model.Amount,
                model.UserNote),
            HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "The upgrade request could not be submitted.");
            return View(model);
        }

        await eventRecorder.RecordAsync(
            ProductEventNames.ProUpgradeRequested,
            "pro",
            userId,
            cancellationToken: HttpContext.RequestAborted);
        TempData["SuccessMessage"] = "Your Pro request was submitted. Access will activate after the owner verifies your transaction.";
        return RedirectToAction(nameof(Index));
    }

    private string GetUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The current user does not have an identifier.");
}
