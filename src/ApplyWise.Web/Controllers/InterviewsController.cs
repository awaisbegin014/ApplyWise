using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.ViewModels.Interviews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Security;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("interviews")]
public class InterviewsController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IProductEventRecorder events,
    IWorkspaceQuotaService quotas,
    IWorkspaceQuotaGate quotaGate) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? filter)
    {
        var selectedFilter = filter is "past" or "all" ? filter : "upcoming";
        var userId = GetUserId();
        var now = DateTimeOffset.UtcNow;
        var query = dbContext.Interviews.AsNoTracking().Where(interview => interview.UserId == userId);

        query = selectedFilter switch
        {
            "past" => query.Where(interview => interview.ScheduledAt < now)
                .OrderByDescending(interview => interview.ScheduledAt),
            "all" => query.OrderByDescending(interview => interview.ScheduledAt),
            _ => query.Where(interview => interview.ScheduledAt >= now)
                .OrderBy(interview => interview.ScheduledAt)
        };

        var interviews = await query.Select(interview => new InterviewListItemViewModel(
            interview.Id,
            interview.JobApplication!.CompanyName,
            interview.JobApplication.JobTitle,
            interview.InterviewType,
            interview.Status,
            interview.ScheduledAt,
            interview.MeetingLink,
            interview.ScheduledAt < now)).ToListAsync();

        return View(new InterviewIndexViewModel { Filter = selectedFilter, Interviews = interviews });
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(int? jobApplicationId)
    {
        var model = new InterviewCreateViewModel
        {
            JobApplicationId = await GetOwnedApplicationIdOrDefaultAsync(jobApplicationId),
            ScheduledAt = DateTimeOffset.Now.AddDays(1)
        };
        await PopulateApplicationsAsync(model);
        return View(model);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(InterviewCreateViewModel model)
    {
        await ValidateApplicationOwnershipAsync(model.JobApplicationId);
        if (!ModelState.IsValid)
        {
            await PopulateApplicationsAsync(model);
            return View(model);
        }

        var userId = GetUserId();
        if (!await quotas.CanCreateInterviewAsync(userId, HttpContext.RequestAborted))
        {
            ModelState.AddModelError(string.Empty, "Your workspace reached its interview limit. Delete old interviews before scheduling another.");
            await PopulateApplicationsAsync(model);
            return View(model);
        }

        var now = DateTimeOffset.UtcNow;
        var interview = new Interview { UserId = userId, CreatedAt = now, UpdatedAt = now };
        ApplyForm(interview, model);
        var saved = await quotaGate.RunAsync(
            WorkspaceQuotaResources.Interviews,
            userId,
            async cancellationToken =>
            {
                if (!await quotas.CanCreateInterviewAsync(userId, cancellationToken))
                {
                    return false;
                }

                dbContext.Interviews.Add(interview);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            HttpContext.RequestAborted);
        if (!saved)
        {
            ModelState.AddModelError(string.Empty, "Your workspace reached its interview limit. Delete old interviews before scheduling another.");
            await PopulateApplicationsAsync(model);
            return View(model);
        }

        await events.RecordAsync(
            ProductEventNames.InterviewScheduled,
            "manual",
            interview.UserId,
            cancellationToken: HttpContext.RequestAborted);

        TempData["SuccessMessage"] = "Interview scheduled.";
        return RedirectToAction(nameof(Details), new { id = interview.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var interview = await FindOwnedInterviewAsync(id, readOnly: true, includeApplication: true);
        return interview is null ? NotFound() : View(ToDetailsViewModel(interview));
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var interview = await FindOwnedInterviewAsync(id, readOnly: true);
        if (interview is null)
        {
            return NotFound();
        }

        var model = new InterviewEditViewModel
        {
            Id = interview.Id,
            JobApplicationId = interview.JobApplicationId,
            InterviewType = interview.InterviewType,
            Status = interview.Status,
            ScheduledAt = interview.ScheduledAt,
            MeetingLink = interview.MeetingLink,
            InterviewerName = interview.InterviewerName,
            PreparationNotes = interview.PreparationNotes,
            FeedbackNotes = interview.FeedbackNotes,
            ResultNotes = interview.ResultNotes
        };
        await PopulateApplicationsAsync(model);
        return View(model);
    }

    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, InterviewEditViewModel model)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        var interview = await FindOwnedInterviewAsync(id);
        if (interview is null)
        {
            return NotFound();
        }

        await ValidateApplicationOwnershipAsync(model.JobApplicationId);
        if (!ModelState.IsValid)
        {
            await PopulateApplicationsAsync(model);
            return View(model);
        }

        ApplyForm(interview, model);
        interview.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        TempData["SuccessMessage"] = "Interview updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var interview = await FindOwnedInterviewAsync(id, readOnly: true, includeApplication: true);
        return interview is null ? NotFound() : View(ToDetailsViewModel(interview));
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var interview = await FindOwnedInterviewAsync(id);
        if (interview is null)
        {
            return NotFound();
        }

        dbContext.Interviews.Remove(interview);
        await dbContext.SaveChangesAsync();
        TempData["SuccessMessage"] = "Interview deleted.";
        return RedirectToAction(nameof(Index));
    }

    private string GetUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The current user does not have an identifier.");

    private async Task ValidateApplicationOwnershipAsync(int? jobApplicationId)
    {
        if (!jobApplicationId.HasValue)
        {
            return;
        }

        var userId = GetUserId();
        if (!await dbContext.JobApplications.AnyAsync(application =>
                application.Id == jobApplicationId.Value && application.UserId == userId))
        {
            ModelState.AddModelError(nameof(InterviewFormViewModel.JobApplicationId),
                "Select a job application from your own tracker.");
        }
    }

    private async Task PopulateApplicationsAsync(InterviewFormViewModel model)
    {
        var userId = GetUserId();
        model.AvailableApplications = await dbContext.JobApplications.AsNoTracking()
            .Where(application => application.UserId == userId)
            .OrderByDescending(application => application.CreatedAt)
            .Select(application => new SelectListItem(
                application.JobTitle + " at " + application.CompanyName,
                application.Id.ToString()))
            .ToListAsync();
    }

    private async Task<int?> GetOwnedApplicationIdOrDefaultAsync(int? requestedId)
    {
        var userId = GetUserId();
        if (requestedId.HasValue && await dbContext.JobApplications.AnyAsync(application =>
                application.Id == requestedId.Value && application.UserId == userId))
        {
            return requestedId;
        }

        return await dbContext.JobApplications.Where(application => application.UserId == userId)
            .OrderByDescending(application => application.CreatedAt)
            .Select(application => (int?)application.Id)
            .FirstOrDefaultAsync();
    }

    private Task<Interview?> FindOwnedInterviewAsync(int id, bool readOnly = false, bool includeApplication = false)
    {
        IQueryable<Interview> query = dbContext.Interviews;
        if (includeApplication) query = query.Include(interview => interview.JobApplication);
        if (readOnly) query = query.AsNoTracking();
        var userId = GetUserId();
        return query.SingleOrDefaultAsync(interview => interview.Id == id && interview.UserId == userId);
    }

    private static void ApplyForm(Interview interview, InterviewFormViewModel model)
    {
        interview.JobApplicationId = model.JobApplicationId!.Value;
        interview.InterviewType = model.InterviewType;
        interview.Status = model.Status;
        interview.ScheduledAt = model.ScheduledAt!.Value;
        interview.MeetingLink = Clean(model.MeetingLink);
        interview.InterviewerName = Clean(model.InterviewerName);
        interview.PreparationNotes = Clean(model.PreparationNotes);
        interview.FeedbackNotes = Clean(model.FeedbackNotes);
        interview.ResultNotes = Clean(model.ResultNotes);
    }

    private static InterviewDetailsViewModel ToDetailsViewModel(Interview interview) => new(
        interview.Id, interview.JobApplicationId, interview.JobApplication!.CompanyName,
        interview.JobApplication.JobTitle, interview.InterviewType, interview.Status,
        interview.ScheduledAt, interview.MeetingLink, interview.InterviewerName,
        interview.PreparationNotes, interview.FeedbackNotes, interview.ResultNotes,
        interview.CreatedAt, interview.UpdatedAt);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
