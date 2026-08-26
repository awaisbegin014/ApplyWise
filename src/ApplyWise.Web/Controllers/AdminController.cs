using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Admin;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Subscriptions;
using ApplyWise.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Controllers;

[Authorize(Policy = AdminAccess.Policy)]
[Route("admin")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AdminController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IAdminDashboardService dashboardService,
    IAdminUserReportService userReportService,
    IAdminContactMessageService contactMessageService,
    ISubscriptionService subscriptionService,
    IProductEventRecorder eventRecorder) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        int days = 30,
        string? search = null,
        int page = 1)
    {
        var model = await dashboardService.LoadAsync(
            days,
            search,
            page,
            HttpContext.RequestAborted);
        return View(model);
    }

    [HttpGet("users/{userId}")]
    public async Task<IActionResult> UserDetails(
        string userId,
        int applicationsPage = 1,
        int importsPage = 1)
    {
        var model = await userReportService.LoadAsync(
            userId,
            applicationsPage,
            importsPage,
            HttpContext.RequestAborted);

        return model is null ? NotFound() : View(model);
    }

    [HttpGet("contact")]
    public async Task<IActionResult> ContactMessages(
        string? status = null,
        string? search = null,
        int page = 1)
    {
        var model = await contactMessageService.LoadInboxAsync(
            status,
            search,
            page,
            HttpContext.RequestAborted);
        return View(model);
    }

    [HttpGet("contact/{id:long}")]
    public async Task<IActionResult> ContactMessage(long id)
    {
        var model = await contactMessageService.LoadDetailsAsync(
            id,
            HttpContext.RequestAborted);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("contact/{id:long}/read-state")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetContactMessageReadState(long id, bool isRead)
    {
        var updated = await contactMessageService.SetReadStateAsync(
            id,
            isRead,
            HttpContext.RequestAborted);
        return updated
            ? RedirectToAction(nameof(ContactMessage), new { id })
            : NotFound();
    }

    [HttpGet("pro-requests")]
    public async Task<IActionResult> ProRequests(string status = "pending")
    {
        var normalizedStatus = status.Trim().ToLowerInvariant();
        var query = dbContext.ProUpgradeRequests.AsNoTracking();
        query = normalizedStatus switch
        {
            "approved" => query.Where(item => item.Status == ProUpgradeRequestStatus.Approved),
            "rejected" => query.Where(item => item.Status == ProUpgradeRequestStatus.Rejected),
            "all" => query,
            _ => query.Where(item => item.Status == ProUpgradeRequestStatus.Pending)
        };

        var rows = await (
            from request in query
            join user in dbContext.Users.AsNoTracking()
                on request.UserId equals user.Id
            join profile in dbContext.CareerProfiles.AsNoTracking()
                on request.UserId equals profile.UserId into profiles
            from profile in profiles.DefaultIfEmpty()
            orderby request.SubmittedAt descending
            select new AdminProRequestRowViewModel(
                request.Id,
                request.UserId,
                user.Email ?? "Unknown",
                profile != null && profile.FullName != null ? profile.FullName : user.Email ?? "Unknown",
                request.PaymentMethod,
                request.TransactionReference,
                request.PayerName,
                request.Amount,
                request.Currency,
                request.UserNote,
                request.Status,
                request.SubmittedAt,
                request.ReviewedAt,
                request.AdminNote))
            .Take(200)
            .ToListAsync(HttpContext.RequestAborted);

        return View(new AdminProRequestsViewModel
        {
            Status = normalizedStatus is "approved" or "rejected" or "all"
                ? normalizedStatus
                : "pending",
            PendingCount = await dbContext.ProUpgradeRequests.CountAsync(
                item => item.Status == ProUpgradeRequestStatus.Pending,
                HttpContext.RequestAborted),
            Requests = rows
        });
    }

    [HttpPost("pro-requests/{id:long}/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveProRequest(long id, AdminProReviewInputViewModel model)
    {
        var updated = await subscriptionService.ApproveUpgradeRequestAsync(
            id,
            GetAdminUserId(),
            model.AdminNote,
            HttpContext.RequestAborted);
        if (!updated) return NotFound();

        await eventRecorder.RecordAsync(
            ProductEventNames.ProUpgradeApproved,
            "admin",
            GetAdminUserId(),
            cancellationToken: HttpContext.RequestAborted);
        TempData["SuccessMessage"] = "Transaction verified and Pro access activated.";
        return RedirectToAction(nameof(ProRequests));
    }

    [HttpPost("pro-requests/{id:long}/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectProRequest(long id, AdminProReviewInputViewModel model)
    {
        var updated = await subscriptionService.RejectUpgradeRequestAsync(
            id,
            GetAdminUserId(),
            model.AdminNote,
            HttpContext.RequestAborted);
        if (!updated) return NotFound();

        TempData["SuccessMessage"] = "The upgrade request was rejected without changing access.";
        return RedirectToAction(nameof(ProRequests));
    }

    private string GetAdminUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The current administrator does not have an identifier.");
}
