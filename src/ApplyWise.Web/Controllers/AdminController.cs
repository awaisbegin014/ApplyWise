using ApplyWise.Web.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApplyWise.Web.Controllers;

[Authorize(Policy = AdminAccess.Policy)]
[Route("admin")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AdminController(
    IAdminDashboardService dashboardService,
    IAdminUserReportService userReportService,
    IAdminContactMessageService contactMessageService) : Controller
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
}
