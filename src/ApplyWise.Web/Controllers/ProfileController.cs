using System.Security.Claims;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Profiles;
using ApplyWise.Web.ViewModels.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("profile")]
public class ProfileController(
    ApplicationDbContext db,
    UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signInManager,
    IWebHostEnvironment environment) : Controller
{
    private const int MaxAvatarSizeBytes = 2 * 1024 * 1024;

    [HttpGet("")] public async Task<IActionResult> Index() => View(await LoadModelAsync());

    [HttpPost("save"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ProfileEditViewModel model)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (model.DateOfBirth is { } dateOfBirth
            && (dateOfBirth > today || dateOfBirth < today.AddYears(-120)))
        {
            ModelState.AddModelError(
                nameof(ProfileEditViewModel.DateOfBirth),
                "Enter a valid date of birth that is not in the future.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateAvatarPresentationAsync(model);
            return View("Index", model);
        }
        var profile = await GetOrCreateAsync();
        Apply(profile, model); profile.OnboardingCompleted = true; profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await RefreshDisplayNameAsync(profile.FullName);
        TempData["SuccessMessage"] = "Your profile was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("avatar")]
    public async Task<IActionResult> Avatar()
    {
        Response.Headers.CacheControl = "private, no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var profile = await db.CareerProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == GetUserId());
        if (profile?.AvatarData is not null)
        {
            return File(profile.AvatarData, profile.AvatarContentType ?? "image/png");
        }

        var avatar = AvatarCatalog.Find(profile?.SelectedAvatarId)
            ?? AvatarCatalog.Find(AvatarCatalog.GetDefaultAvatarId(profile?.Gender));
        if (avatar is null) return NotFound();

        var avatarPath = GetAvatarPath(avatar);
        if (!System.IO.File.Exists(avatarPath)) return NotFound();
        return File(await System.IO.File.ReadAllBytesAsync(avatarPath, HttpContext.RequestAborted), "image/jpeg");
    }

    [HttpPost("avatar"), ValidateAntiForgeryToken, EnableRateLimiting("uploads"), RequestSizeLimit(MaxAvatarSizeBytes + 64 * 1024)]
    public async Task<IActionResult> Avatar(IFormFile? file)
    {
        if (file is null || file.Length <= 0)
        {
            TempData["ErrorMessage"] = "Choose a PNG, JPEG, or WebP picture.";
            return RedirectToAction(nameof(Index));
        }
        if (file.Length > MaxAvatarSizeBytes)
        {
            TempData["ErrorMessage"] = "Choose a picture smaller than 2 MB.";
            return RedirectToAction(nameof(Index));
        }

        var bytes = new byte[(int)file.Length];
        await using var stream = file.OpenReadStream();
        await stream.ReadExactlyAsync(bytes, HttpContext.RequestAborted);
        var image = AvatarImageInspector.Inspect(bytes);
        if (image is null)
        {
            TempData["ErrorMessage"] = "Choose a valid PNG, JPEG, or WebP picture no larger than 4,096 pixels per side.";
            return RedirectToAction(nameof(Index));
        }

        var profile = await GetOrCreateAsync();
        profile.AvatarData = bytes;
        profile.AvatarContentType = image.ContentType;
        profile.SelectedAvatarId = null;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Your custom profile picture was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("avatar/select"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectAvatar(string? avatarId)
    {
        var avatar = AvatarCatalog.Find(avatarId);
        if (avatar is null)
        {
            TempData["ErrorMessage"] = "Choose one of the available profile avatars.";
            return RedirectToAction(nameof(Index));
        }

        var path = GetAvatarPath(avatar);
        if (!System.IO.File.Exists(path))
        {
            TempData["ErrorMessage"] = "That avatar is temporarily unavailable.";
            return RedirectToAction(nameof(Index));
        }

        var profile = await GetOrCreateAsync();
        profile.AvatarData = null;
        profile.AvatarContentType = null;
        profile.SelectedAvatarId = avatar.Id;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"{avatar.Label} avatar selected.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<ProfileEditViewModel> LoadModelAsync()
    {
        var profile = await db.CareerProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == GetUserId());
        var model = profile is null ? new ProfileEditViewModel { FullName = User.Identity?.Name?.Split('@')[0] ?? string.Empty } : new ProfileEditViewModel
        {
            FullName = profile.FullName,
            Gender = profile.Gender,
            DateOfBirth = profile.DateOfBirth,
            CareerStage = profile.CareerStage,
            Institution = profile.Institution,
            DegreeProgram = profile.DegreeProgram,
            FieldOfStudy = profile.FieldOfStudy,
            GraduationYear = profile.GraduationYear,
            CurrentSemester = profile.CurrentSemester,
            PreferredLocations = profile.PreferredLocations,
            PreferredWorkModes = profile.PreferredWorkModes,
            Skills = profile.Skills,
            CareerInterests = profile.CareerInterests,
            AcademicHighlights = profile.AcademicHighlights
        };
        PopulateAvatarPresentation(model, profile);
        return model;
    }

    private async Task PopulateAvatarPresentationAsync(ProfileEditViewModel model)
    {
        var profile = await db.CareerProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == GetUserId());
        PopulateAvatarPresentation(model, profile);
    }

    private void PopulateAvatarPresentation(ProfileEditViewModel model, CareerProfile? profile)
    {
        model.Initials = CreateInitials(model.FullName);
        model.CurrentAvatarUrl = Url.Action(nameof(Avatar), "Profile", new
        {
            version = profile?.UpdatedAt.ToUnixTimeMilliseconds() ?? 0
        });
        var recommendedAvatarId = AvatarCatalog.GetDefaultAvatarId(profile?.Gender ?? model.Gender);
        model.CurrentAvatarLabel = profile?.AvatarData is not null
            ? "Custom photo"
            : AvatarCatalog.Find(profile?.SelectedAvatarId)?.Label
              ?? AvatarCatalog.Find(recommendedAvatarId)?.Label
              ?? "Profile picture";
        model.AvatarOptions = AvatarCatalog.All
            .OrderByDescending(option => string.Equals(option.Id, recommendedAvatarId, StringComparison.Ordinal))
            .Select(option => new ProfileAvatarOption(
            option.Id,
            option.Category,
            option.Label,
            Url.Content($"~/images/avatars/{option.FileName}"),
            string.Equals(option.Id, profile?.SelectedAvatarId, StringComparison.Ordinal),
            string.Equals(option.Id, recommendedAvatarId, StringComparison.Ordinal)))
            .ToArray();
    }

    private async Task<CareerProfile> GetOrCreateAsync()
    {
        var userId = GetUserId(); var profile = await db.CareerProfiles.SingleOrDefaultAsync(p => p.UserId == userId);
        if (profile != null) return profile;
        var user = await users.GetUserAsync(User); var name = User.FindFirstValue("display_name") ?? user?.UserName?.Split('@')[0] ?? "";
        profile = new CareerProfile { UserId = userId, FullName = name, SelectedAvatarId = AvatarCatalog.GeneralNeutralId, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; db.CareerProfiles.Add(profile); await db.SaveChangesAsync(); return profile;
    }

    private void Apply(CareerProfile profile, ProfileEditViewModel model)
    {
        profile.FullName = model.FullName.Trim(); profile.Gender = model.Gender; profile.DateOfBirth = model.DateOfBirth; profile.CareerStage = model.CareerStage; profile.Institution = Clean(model.Institution); profile.DegreeProgram = Clean(model.DegreeProgram);
        profile.FieldOfStudy = Clean(model.FieldOfStudy); profile.GraduationYear = model.GraduationYear; profile.CurrentSemester = model.CurrentSemester; profile.PreferredLocations = Clean(model.PreferredLocations);
        profile.PreferredWorkModes = Clean(model.PreferredWorkModes); profile.Skills = Clean(model.Skills); profile.CareerInterests = Clean(model.CareerInterests);
        profile.AcademicHighlights = Clean(model.AcademicHighlights);
    }
    private async Task RefreshDisplayNameAsync(string name)
    {
        var user = await users.GetUserAsync(User); if (user is null) return;
        var claims = await users.GetClaimsAsync(user); var existing = claims.FirstOrDefault(c => c.Type == "display_name");
        if (existing != null) await users.RemoveClaimAsync(user, existing); await users.AddClaimAsync(user, new Claim("display_name", name)); await signInManager.RefreshSignInAsync(user);
    }
    private string GetUserId() => users.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user is missing an identifier.");
    private string GetAvatarPath(AvatarDefinition avatar) =>
        Path.Combine(environment.WebRootPath, "images", "avatars", avatar.FileName);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateInitials(string? fullName)
    {
        var parts = fullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        return parts.Length switch
        {
            0 => "AW",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => string.Concat(parts[0][0], parts[^1][0]).ToUpperInvariant()
        };
    }

}
