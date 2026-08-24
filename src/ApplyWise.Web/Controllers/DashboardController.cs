using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.Services.Dashboard;
using ApplyWise.Web.Services.Gmail;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.ViewModels.Settings;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Controllers;

[Authorize]
public class DashboardController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IDashboardReadService dashboardReadService,
    IAccountSecurityCodeService securityCodes,
    SignInManager<IdentityUser> signInManager,
    IOptions<GoogleIntegrationOptions> googleOptions,
    IApplicationLockProvider operationLocks,
    IGmailCredentialProtector gmailCredentialProtector,
    IHttpClientFactory httpClientFactory,
    ILogger<DashboardController> logger) : Controller
{
    public async Task<IActionResult> Index(ApplicationStatus? tab)
    {
        var userId = userManager.GetUserId(User)
            ?? throw new InvalidOperationException("The current user does not have an identifier.");
        var displayName = User.FindFirst("display_name")?.Value;
        displayName = string.IsNullOrWhiteSpace(displayName)
            ? User.Identity?.Name?.Split('@')[0] ?? "there"
            : displayName.Trim();

        var model = await dashboardReadService.GetAsync(
            userId,
            displayName,
            HttpContext.RequestAborted);
        ViewData["SelectedPipelineStatus"] = tab is { } selectedStatus && Enum.IsDefined(selectedStatus)
            ? selectedStatus
            : ApplicationStatus.Applied;
        return View(model);
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Settings() => View(await BuildSettingsModelAsync());

    [HttpPost("settings/security-code/{securityAction}"), ValidateAntiForgeryToken]
    [EnableRateLimiting("account-security")]
    public async Task<IActionResult> SendSecurityCode(string securityAction)
    {
        if (!TryParseAction(securityAction, out var accountSecurityAction)) return NotFound();
        var user = await userManager.GetUserAsync(User);
        if (user is null || string.IsNullOrWhiteSpace(user.Email)) return Challenge();

        var issued = await securityCodes.IssueAsync(user.Id, user.Email, accountSecurityAction, HttpContext.RequestAborted);
        if (issued.Succeeded)
        {
            TempData["SettingsSuccess"] = issued.Message;
        }
        else
        {
            TempData["SettingsError"] = issued.Message;
        }
        TempData["SettingsOpenSection"] = securityAction;
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost("settings/change-password"), ValidateAntiForgeryToken]
    [EnableRateLimiting("account-security")]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "ChangePassword")] ChangePasswordInput input)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var hasPassword = await userManager.HasPasswordAsync(user);
        if (hasPassword && string.IsNullOrWhiteSpace(input.CurrentPassword))
        {
            ModelState.AddModelError(
                "ChangePassword.CurrentPassword",
                "Enter your current password.");
        }
        if (!ModelState.IsValid) return await SettingsWithErrorsAsync("password");

        var verified = await securityCodes.VerifyAsync(user.Id, AccountSecurityAction.ChangePassword, input.Code, HttpContext.RequestAborted);
        if (!verified.Succeeded)
        {
            ModelState.AddModelError("ChangePassword.Code", verified.Message);
            return await SettingsWithErrorsAsync("password");
        }

        var result = hasPassword
            ? await userManager.ChangePasswordAsync(
                user,
                input.CurrentPassword,
                input.NewPassword)
            : await userManager.AddPasswordAsync(user, input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("ChangePassword.CurrentPassword", error.Description);
            return await SettingsWithErrorsAsync("password");
        }

        await securityCodes.ConsumeAsync(verified.CodeId!.Value, HttpContext.RequestAborted);
        await signInManager.RefreshSignInAsync(user);
        TempData["SettingsSuccess"] = hasPassword
            ? "Your password was changed successfully."
            : "A password was added to your account.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost("settings/delete-account"), ValidateAntiForgeryToken]
    [EnableRateLimiting("account-security")]
    public async Task<IActionResult> DeleteAccount([Bind(Prefix = "DeleteAccount")] DeleteAccountInput input)
    {
        if (!ModelState.IsValid) return await SettingsWithErrorsAsync("delete");
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var verified = await securityCodes.VerifyAsync(user.Id, AccountSecurityAction.DeleteAccount, input.Code, HttpContext.RequestAborted);
        if (!verified.Succeeded)
        {
            ModelState.AddModelError("DeleteAccount.Code", verified.Message);
            return await SettingsWithErrorsAsync("delete");
        }

        var gmailRevocationConfirmed = true;
        await using (var operationLease = await operationLocks.TryAcquireAsync(
            $"gmail-user:{user.Id}",
            Timeout.InfiniteTimeSpan,
            HttpContext.RequestAborted)
            ?? throw new ResourceLockUnavailableException(
                "The account could not be locked for deletion."))
        {
            dbContext.ChangeTracker.Clear();
            var currentUser = await userManager.FindByIdAsync(user.Id);
            if (currentUser is null)
            {
                await signInManager.SignOutAsync();
                TempData["StatusMessage"] = "Your ApplyWise account was already deleted.";
                return RedirectToPage("/Account/Login", new { area = "Identity" });
            }

            var gmailConnection = await dbContext.GmailConnections
                .SingleOrDefaultAsync(
                    connection => connection.UserId == currentUser.Id,
                    HttpContext.RequestAborted);
            if (gmailConnection is not null)
            {
                gmailConnection.AutoAddHighConfidenceApplications = false;
                gmailConnection.NextSyncAt = DateTimeOffset.MaxValue;
                gmailConnection.LastErrorCode = GmailConnectionStates.RevocationPending;
                gmailConnection.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(HttpContext.RequestAborted);

                gmailRevocationConfirmed =
                    await GmailTokenRevocation.TryRevokeStoredTokenAsync(
                        gmailConnection,
                        gmailCredentialProtector,
                        httpClientFactory,
                        logger,
                        HttpContext.RequestAborted);
            }

            var resumePaths = await dbContext.Resumes.AsNoTracking()
                .Where(resume => resume.UserId == currentUser.Id)
                .Select(resume => resume.FilePath)
                .Distinct()
                .ToListAsync(HttpContext.RequestAborted);
            var queuedPaths = resumePaths.Count == 0
                ? []
                : await dbContext.ResumeFileCleanups
                    .AsNoTracking()
                    .Where(cleanup => resumePaths.Contains(cleanup.FilePath))
                    .Select(cleanup => cleanup.FilePath)
                    .ToListAsync(HttpContext.RequestAborted);
            var queuedPathSet = queuedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var now = DateTimeOffset.UtcNow;
            var newCleanupRows = resumePaths
                .Where(path => queuedPathSet.Add(path))
                .Select(path => new ResumeFileCleanup
                {
                    FilePath = path,
                    CreatedAt = now,
                    NextAttemptAt = now
                })
                .ToList();
            dbContext.ResumeFileCleanups.AddRange(newCleanupRows);

            var result = await userManager.DeleteAsync(currentUser);
            if (!result.Succeeded)
            {
                foreach (var cleanup in newCleanupRows)
                {
                    dbContext.Entry(cleanup).State = EntityState.Detached;
                }
                dbContext.Entry(currentUser).State = EntityState.Unchanged;
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return await SettingsWithErrorsAsync("delete");
            }
        }

        await signInManager.SignOutAsync();
        TempData["StatusMessage"] = gmailRevocationConfirmed
            ? "Your ApplyWise account data was deleted. Private resume files are queued for secure removal."
            : "Your ApplyWise account data was deleted and Gmail syncing was disabled, but Google did not confirm token revocation. Remove ApplyWise from your Google Account permissions to finish disconnecting it.";
        return RedirectToPage("/Account/Login", new { area = "Identity" });
    }

    private async Task<SettingsViewModel> BuildSettingsModelAsync()
    {
        var user = await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("The current user could not be loaded.");
        if (TempData["SettingsOpenSection"] is string requestedSection)
        {
            ViewData["SettingsOpenSection"] = requestedSection;
        }
        return new SettingsViewModel
        {
            Email = user.Email ?? user.UserName ?? string.Empty,
            HasPassword = await userManager.HasPasswordAsync(user),
            GoogleSignInConfigured = googleOptions.Value.IsConfigured,
            GoogleSignInLinked = (await userManager.GetLoginsAsync(user)).Any(login =>
                string.Equals(
                    login.LoginProvider,
                    GoogleDefaults.AuthenticationScheme,
                    StringComparison.Ordinal)),
            IsAdmin = await userManager.IsInRoleAsync(user, Services.Admin.AdminAccess.Role),
            MfaEnabled = await userManager.GetTwoFactorEnabledAsync(user)
        };
    }

    private async Task<IActionResult> SettingsWithErrorsAsync(string section)
    {
        ViewData["SettingsOpenSection"] = section;
        return View("Settings", await BuildSettingsModelAsync());
    }

    private static bool TryParseAction(string action, out AccountSecurityAction securityAction)
    {
        if (string.Equals(action, "password", StringComparison.OrdinalIgnoreCase))
        {
            securityAction = AccountSecurityAction.ChangePassword;
            return true;
        }
        if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
        {
            securityAction = AccountSecurityAction.DeleteAccount;
            return true;
        }
        securityAction = default;
        return false;
    }
}
