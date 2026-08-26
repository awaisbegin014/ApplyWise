using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.ViewModels.JobApplications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Security;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("applications")]
public class JobApplicationsController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IProductEventRecorder events,
    IWorkspaceQuotaService quotas,
    IWorkspaceQuotaGate quotaGate) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(JobApplicationIndexViewModel filters)
    {
        var userId = GetUserId();
        var query = dbContext.JobApplications
            .AsNoTracking()
            .Where(application => application.UserId == userId);

        if (!string.IsNullOrWhiteSpace(filters.SearchTerm))
        {
            var searchTerm = filters.SearchTerm.Trim();
            query = query.Where(application =>
                application.CompanyName.Contains(searchTerm) || application.JobTitle.Contains(searchTerm));
        }

        if (filters.StatusFilter.HasValue)
        {
            query = query.Where(application => application.Status == filters.StatusFilter.Value);
        }

        if (filters.SourceFilter.HasValue)
        {
            query = query.Where(application => application.Source == filters.SourceFilter.Value);
        }

        query = filters.SortBy switch
        {
            "oldest" => query.OrderBy(application => application.CreatedAt),
            "deadline" => query.OrderBy(application => application.Deadline == null)
                .ThenBy(application => application.Deadline),
            "status" => query.OrderBy(application => application.Status)
                .ThenByDescending(application => application.CreatedAt),
            _ => query.OrderByDescending(application => application.CreatedAt)
        };

        filters.SearchTerm = filters.SearchTerm?.Trim();
        filters.SortBy = filters.SortBy is "oldest" or "deadline" or "status" ? filters.SortBy : "newest";
        filters.Page = Math.Max(1, filters.Page);
        filters.PageSize = 20;
        filters.Total = await query.CountAsync();
        filters.Applications = await query.Skip((filters.Page - 1) * filters.PageSize).Take(filters.PageSize)
            .Select(application => new JobApplicationListItemViewModel(
                application.Id,
                application.CompanyName,
                application.JobTitle,
                application.Status,
                application.Source,
                application.Resume != null ? application.Resume.VersionName : null,
                application.AppliedDate,
                application.Deadline,
                application.CreatedAt))
            .ToListAsync();

        return View(filters);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create()
    {
        var userId = GetUserId();
        var model = new JobApplicationCreateViewModel
        {
            AppliedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ResumeId = await dbContext.Resumes
                .Where(resume => resume.UserId == userId && resume.IsDefault)
                .Select(resume => (int?)resume.Id)
                .SingleOrDefaultAsync()
        };
        await PopulateResumesAsync(model);
        return View(model);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(JobApplicationCreateViewModel model)
    {
        await ValidateFormAsync(model);
        if (!ModelState.IsValid)
        {
            await PopulateResumesAsync(model);
            return View(model);
        }

        var userId = GetUserId();
        if (!await quotas.CanCreateApplicationAsync(userId, HttpContext.RequestAborted))
        {
            ModelState.AddModelError(string.Empty, "Your workspace reached its application limit. Delete old applications before adding another.");
            await PopulateResumesAsync(model);
            return View(model);
        }

        var now = DateTimeOffset.UtcNow;
        var application = new JobApplication
        {
            UserId = userId,
            Status = ApplicationStatus.Applied,
            CreatedAt = now,
            UpdatedAt = now
        };
        ApplyForm(application, model);
        application.Status = ApplicationStatus.Applied;

        var saved = await quotaGate.RunAsync(
            WorkspaceQuotaResources.Applications,
            userId,
            async cancellationToken =>
            {
                if (!await quotas.CanCreateApplicationAsync(userId, cancellationToken))
                {
                    return false;
                }

                dbContext.JobApplications.Add(application);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            HttpContext.RequestAborted);
        if (!saved)
        {
            ModelState.AddModelError(string.Empty, "Your workspace reached its application limit. Delete old applications before adding another.");
            await PopulateResumesAsync(model);
            return View(model);
        }

        await events.RecordAsync(
            ProductEventNames.ApplicationCreated,
            "manual",
            application.UserId,
            cancellationToken: HttpContext.RequestAborted);
        TempData["SuccessMessage"] = $"{application.JobTitle} at {application.CompanyName} was added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var application = await FindOwnedApplicationAsync(id, true, true);
        if (application is null) return NotFound();
        return View(ToDetailsViewModel(application));
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var application = await FindOwnedApplicationAsync(id, true);
        if (application is null)
        {
            return NotFound();
        }

        var model = new JobApplicationEditViewModel
        {
            Id = application.Id,
            CompanyName = application.CompanyName,
            JobTitle = application.JobTitle,
            JobLocation = application.JobLocation,
            JobType = application.JobType,
            SalaryRange = application.SalaryRange,
            Source = application.Source,
            JobUrl = application.JobUrl,
            JobDescription = application.JobDescription,
            Status = application.Status,
            ResumeId = application.ResumeId,
            AppliedDate = application.AppliedDate,
            Deadline = application.Deadline,
            Notes = application.Notes,
            CustomFields = ReadCustomFields(application.CustomFieldsJson)
        };
        await PopulateResumesAsync(model);
        return View(model);
    }

    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, JobApplicationEditViewModel model)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        var application = await FindOwnedApplicationAsync(id);
        if (application is null)
        {
            return NotFound();
        }

        await ValidateFormAsync(model);
        if (!ModelState.IsValid)
        {
            await PopulateResumesAsync(model);
            return View(model);
        }

        ApplyForm(application, model);
        application.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{application.JobTitle} at {application.CompanyName} was updated.";
        return RedirectToAction(nameof(Details), new { id = application.Id });
    }

    [HttpPost("{id:int}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, ApplicationStatus status, string? returnUrl = null)
    {
        if (!Enum.IsDefined(status))
        {
            return BadRequest();
        }

        var application = await FindOwnedApplicationAsync(id);
        if (application is null)
        {
            return NotFound();
        }

        application.Status = status;
        application.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        TempData["SuccessMessage"] = $"{application.JobTitle} moved to {status.GetDisplayName()}.";
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }
        return RedirectToAction(nameof(Details), new { id = application.Id });
    }

    [HttpGet("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var application = await FindOwnedApplicationAsync(id, true, true);
        return application is null ? NotFound() : View(ToDetailsViewModel(application));
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var application = await FindOwnedApplicationAsync(id);
        if (application is null)
        {
            return NotFound();
        }

        await quotaGate.RunAsync(
            WorkspaceQuotaResources.Applications,
            application.UserId,
            async cancellationToken =>
            {
                await dbContext.ResumeAnalyses
                    .Where(analysis => analysis.UserId == application.UserId && analysis.JobApplicationId == application.Id)
                    .ExecuteDeleteAsync(cancellationToken);
                await dbContext.Interviews
                    .Where(interview => interview.UserId == application.UserId && interview.JobApplicationId == application.Id)
                    .ExecuteDeleteAsync(cancellationToken);
                await dbContext.JobScamChecks
                    .Where(check => check.UserId == application.UserId && check.JobApplicationId == application.Id)
                    .ExecuteDeleteAsync(cancellationToken);
                dbContext.JobApplications.Remove(application);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            HttpContext.RequestAborted);
        TempData["SuccessMessage"] = $"{application.JobTitle} at {application.CompanyName} was deleted.";
        return RedirectToAction(nameof(Index));
    }

    private string GetUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The current user does not have an identifier.");

    private async Task ValidateFormAsync(JobApplicationFormViewModel model)
    {
        var latestAllowedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        if (model.AppliedDate > latestAllowedDate)
        {
            ModelState.AddModelError(nameof(model.AppliedDate), "Applied date cannot be more than seven days in the future.");
        }

        if (model.ResumeId.HasValue)
        {
            var userId = GetUserId();
            var ownsResume = await dbContext.Resumes
                .AnyAsync(resume => resume.Id == model.ResumeId.Value && resume.UserId == userId);
            if (!ownsResume)
            {
                ModelState.AddModelError(nameof(model.ResumeId), "Select a resume from your own resume library.");
            }
        }

        if (model.CustomFields.Count > 12)
        {
            ModelState.AddModelError(nameof(model.CustomFields), "Add up to 12 custom fields.");
        }
        for (var index = 0; index < model.CustomFields.Count; index++)
        {
            var field = model.CustomFields[index];
            var label = Clean(field.Label);
            var value = Clean(field.Value);
            if (label is null && value is null) continue;
            if (label is null) ModelState.AddModelError($"CustomFields[{index}].Label", "Add a field name.");
            if (value is null) ModelState.AddModelError($"CustomFields[{index}].Value", "Add a value or remove this field.");
        }
    }

    private async Task PopulateResumesAsync(JobApplicationFormViewModel model)
    {
        var userId = GetUserId();
        model.AvailableResumes = await dbContext.Resumes
            .AsNoTracking()
            .Where(resume => resume.UserId == userId)
            .OrderByDescending(resume => resume.IsDefault)
            .ThenByDescending(resume => resume.UploadedAt)
            .Select(resume => new SelectListItem
            {
                Value = resume.Id.ToString(),
                Text = resume.VersionName + (resume.IsDefault ? " (Default)" : string.Empty),
                Selected = model.ResumeId == resume.Id
            })
            .ToListAsync();
    }

    private Task<JobApplication?> FindOwnedApplicationAsync(int id, bool readOnly = false, bool includeResume = false)
    {
        IQueryable<JobApplication> query = dbContext.JobApplications;
        if (includeResume)
        {
            query = query.Include(application => application.Resume);
        }
        if (readOnly)
        {
            query = query.AsNoTracking();
        }

        var userId = GetUserId();
        return query.SingleOrDefaultAsync(application => application.Id == id && application.UserId == userId);
    }

    private static void ApplyForm(JobApplication application, JobApplicationFormViewModel model)
    {
        application.CompanyName = model.CompanyName.Trim();
        application.JobTitle = model.JobTitle.Trim();
        application.JobLocation = Clean(model.JobLocation);
        application.JobType = model.JobType;
        application.SalaryRange = Clean(model.SalaryRange);
        application.Source = model.Source;
        application.JobUrl = Clean(model.JobUrl);
        application.JobDescription = Clean(model.JobDescription);
        application.Status = model.Status;
        application.ResumeId = model.ResumeId;
        application.AppliedDate = model.AppliedDate;
        application.Deadline = model.Deadline;
        application.Notes = Clean(model.Notes);
        application.CustomFieldsJson = SerializeCustomFields(model.CustomFields);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static JobApplicationDetailsViewModel ToDetailsViewModel(
        JobApplication application) =>
        new(application.Id, application.CompanyName, application.JobTitle, application.JobLocation,
            application.JobType, application.SalaryRange, application.Source, application.JobUrl,
            application.JobDescription, application.Status, application.Resume?.VersionName,
            application.AppliedDate, application.Deadline, application.Notes, ReadCustomFieldsForDetails(application.CustomFieldsJson),
            application.CreatedAt, application.UpdatedAt);

    private static List<CustomApplicationFieldInput> ReadCustomFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<CustomApplicationFieldInput>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<ApplicationCustomFieldViewModel> ReadCustomFieldsForDetails(string? json) =>
        ReadCustomFields(json).Select(field => new ApplicationCustomFieldViewModel(Clean(field.Label) ?? string.Empty, Clean(field.Value) ?? string.Empty))
            .Where(field => field.Label.Length > 0 && field.Value.Length > 0).ToArray();

    private static string? SerializeCustomFields(IEnumerable<CustomApplicationFieldInput> fields)
    {
        var cleaned = fields.Select(field => new CustomApplicationFieldInput { Label = Clean(field.Label), Value = Clean(field.Value) })
            .Where(field => field.Label is not null && field.Value is not null).ToArray();
        return cleaned.Length == 0 ? null : JsonSerializer.Serialize(cleaned);
    }
}
