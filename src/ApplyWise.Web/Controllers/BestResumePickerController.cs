using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApplyWise.Web.Controllers;

/// <summary>
/// Preserves old bookmarks while keeping ATS, job matching, and resume ranking
/// in one user-facing workflow.
/// </summary>
[Authorize]
[Route("best-resume-picker")]
public sealed class BestResumePickerController : Controller
{
    [HttpGet("")]
    public IActionResult Index(int? jobApplicationId) =>
        RedirectToAction(
            nameof(ResumeAnalyzerController.Index),
            "ResumeAnalyzer",
            new { mode = "job", jobApplicationId });
}
