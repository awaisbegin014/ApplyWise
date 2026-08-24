using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.ViewModels.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApplyWise.Web.Services.Monitoring;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("onboarding")]
public class OnboardingController(
    ApplicationDbContext db,
    UserManager<IdentityUser> users,
    IProductEventRecorder events) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var profile = await db.CareerProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == GetUserId());
        return View(new OnboardingViewModel { FullName = profile?.FullName ?? User.Identity?.Name?.Split('@')[0] ?? string.Empty, Gender = profile?.Gender, DateOfBirth = profile?.DateOfBirth, CareerStage = profile?.CareerStage, Institution = profile?.Institution, DegreeProgram = profile?.DegreeProgram, FieldOfStudy = profile?.FieldOfStudy, GraduationYear = profile?.GraduationYear, CurrentSemester = profile?.CurrentSemester, PreferredLocations = profile?.PreferredLocations, PreferredWorkModes = profile?.PreferredWorkModes, Skills = profile?.Skills, CareerInterests = profile?.CareerInterests, AcademicHighlights = profile?.AcademicHighlights });
    }

    [HttpPost(""), ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(OnboardingViewModel model)
    {
        if (model.Skip) return RedirectToAction("Index", "Dashboard");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (model.DateOfBirth is { } dateOfBirth
            && (dateOfBirth > today || dateOfBirth < today.AddYears(-120)))
        {
            ModelState.AddModelError(
                nameof(OnboardingViewModel.DateOfBirth),
                "Enter a valid date of birth that is not in the future.");
        }
        if (!ModelState.IsValid) return View(model);
        var userId = GetUserId(); var profile = await db.CareerProfiles.SingleOrDefaultAsync(p => p.UserId == userId);
        if (profile is null) { profile = new CareerProfile { UserId = userId, CreatedAt = DateTimeOffset.UtcNow }; db.CareerProfiles.Add(profile); }
        profile.FullName = model.FullName.Trim(); profile.Gender = model.Gender; profile.DateOfBirth = model.DateOfBirth; profile.CareerStage = model.CareerStage; profile.Institution = model.Institution?.Trim(); profile.DegreeProgram = model.DegreeProgram?.Trim(); profile.FieldOfStudy = model.FieldOfStudy?.Trim(); profile.GraduationYear = model.GraduationYear; profile.CurrentSemester = model.CurrentSemester; profile.PreferredLocations = model.PreferredLocations?.Trim(); profile.PreferredWorkModes = model.PreferredWorkModes?.Trim(); profile.Skills = model.Skills?.Trim(); profile.CareerInterests = model.CareerInterests?.Trim(); profile.AcademicHighlights = model.AcademicHighlights?.Trim(); profile.OnboardingCompleted = true; profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await events.RecordAsync(ProductEventNames.OnboardingCompleted, "onboarding", userId, cancellationToken: HttpContext.RequestAborted);
        return RedirectToAction("Index", "Dashboard");
    }
    private string GetUserId() => users.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user is missing an identifier.");
}
