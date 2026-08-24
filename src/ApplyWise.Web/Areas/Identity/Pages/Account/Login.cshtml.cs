using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Gmail;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ApplyWise.Web.Services.Admin;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.AccountSecurity;

namespace ApplyWise.Web.Areas.Identity.Pages.Account;

[EnableRateLimiting("account-security")]
public class LoginModel(
    SignInManager<IdentityUser> signInManager,
    UserManager<IdentityUser> userManager,
    ApplicationDbContext dbContext,
    IProductEventRecorder events,
    IAdminRoleAssignmentService adminRoles,
    ILoginTimingProtector loginTimingProtector,
    IOptions<AdminAccessOptions> adminOptions,
    IOptions<GoogleIntegrationOptions> googleOptions,
    ILogger<LoginModel> logger) : PageModel
{
    private const string InvalidPasswordLoginMessage =
        "We couldn't log you in with those details. Check your email and password, then try again.";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }
    public bool IsGoogleLoginEnabled => googleOptions.Value.IsConfigured;

    [TempData]
    public string? ErrorMessage { get; set; }

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(GetSafeReturnUrl(returnUrl));
        }

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            ModelState.AddModelError(string.Empty, ErrorMessage);
        }

        returnUrl ??= Url.Content("~/");
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return await LoginWithPasswordAsync(returnUrl);
        }
        finally
        {
            await loginTimingProtector.EnforceMinimumResponseTimeAsync(
                startedAt,
                HttpContext.RequestAborted);
        }
    }

    private async Task<IActionResult> LoginWithPasswordAsync(string? returnUrl)
    {
        returnUrl ??= Url.Content("~/");
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var passwordUser = await userManager.FindByEmailAsync(Input.Email);
        // Clear remembered-client state uniformly for every password attempt.
        // This both requires current-session second-factor proof and avoids an
        // owner-only Set-Cookie response that would disclose row existence.
        await signInManager.ForgetTwoFactorClientAsync();

        Microsoft.AspNetCore.Identity.SignInResult result;
        if (passwordUser is null)
        {
            loginTimingProtector.VerifyDummyPassword(Input.Password);
            result = Microsoft.AspNetCore.Identity.SignInResult.Failed;
        }
        else if (userManager.Options.SignIn.RequireConfirmedAccount
                 && !await userManager.IsEmailConfirmedAsync(passwordUser))
        {
            // Identity rejects an unconfirmed account before hashing its
            // password. Pay the same bounded hashing cost as other failures.
            loginTimingProtector.VerifyDummyPassword(Input.Password);
            result = Microsoft.AspNetCore.Identity.SignInResult.NotAllowed;
        }
        else if (userManager.SupportsUserLockout
                 && await userManager.IsLockedOutAsync(passwordUser))
        {
            // Identity short-circuits pre-locked accounts before password hashing.
            // Perform one equivalent-cost verification so the failure path does
            // not disclose lockout state through a fast response.
            loginTimingProtector.VerifyDummyPassword(Input.Password);
            result = Microsoft.AspNetCore.Identity.SignInResult.LockedOut;
        }
        else
        {
            result = await signInManager.PasswordSignInAsync(
                passwordUser, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        }

        if (result.Succeeded)
        {
            var user = passwordUser;
            if (user is not null)
            {
                var rolesChanged = await adminRoles.SynchronizeUserAsync(user);
                if (rolesChanged)
                {
                    await signInManager.RefreshSignInAsync(user);
                }
                await events.RecordLoginAsync(user.Id, "password", HttpContext.RequestAborted);
            }
            logger.LogInformation("User logged in.");
            return LocalRedirect(returnUrl);
        }

        if (result.RequiresTwoFactor)
        {
            if (passwordUser is not null)
            {
                await adminRoles.SynchronizeUserAsync(passwordUser);
            }
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, Input.RememberMe });
        }

        if (result.IsLockedOut)
        {
            await events.RecordAsync(
                ProductEventNames.LoginFailed,
                "password_locked_out",
                succeeded: false,
                cancellationToken: HttpContext.RequestAborted);
            logger.LogWarning("User account locked out.");
            return InvalidPasswordLogin();
        }

        await events.RecordAsync(
            ProductEventNames.LoginFailed,
            "password",
            succeeded: false,
            cancellationToken: HttpContext.RequestAborted);
        return InvalidPasswordLogin();
    }

    public IActionResult OnPostExternalLogin(string provider, string? returnUrl = null)
    {
        if (!IsGoogleLoginEnabled
            || !string.Equals(
                provider,
                GoogleDefaults.AuthenticationScheme,
                StringComparison.Ordinal))
        {
            return BadRequest();
        }

        returnUrl = GetSafeReturnUrl(returnUrl);
        var redirectUrl = Url.Page(
            "./Login",
            pageHandler: "ExternalLoginCallback",
            values: new { returnUrl });
        var properties = signInManager.ConfigureExternalAuthenticationProperties(
            provider,
            redirectUrl);
        return new ChallengeResult(provider, properties);
    }

    public async Task<IActionResult> OnGetExternalLoginCallbackAsync(
        string? returnUrl = null,
        string? remoteError = null)
    {
        returnUrl = GetSafeReturnUrl(returnUrl);
        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            await events.RecordAsync(
                ProductEventNames.LoginFailed,
                "google_remote",
                succeeded: false,
                cancellationToken: HttpContext.RequestAborted);
            ErrorMessage = "Google sign-in was cancelled or could not be completed.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null
            || !string.Equals(
                info.LoginProvider,
                GoogleDefaults.AuthenticationScheme,
                StringComparison.Ordinal))
        {
            ErrorMessage = "Google sign-in could not be verified. Please try again.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        var externalUser = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (externalUser is not null && adminOptions.Value.Contains(externalUser.Email))
        {
            await adminRoles.SynchronizeUserAsync(externalUser);
            await signInManager.ForgetTwoFactorClientAsync();
        }

        var result = await signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: false);
        if (result.Succeeded)
        {
            if (externalUser is not null)
            {
                var rolesChanged = await adminRoles.SynchronizeUserAsync(externalUser);
                if (rolesChanged)
                {
                    await signInManager.RefreshSignInAsync(externalUser);
                }
                await events.RecordLoginAsync(externalUser.Id, "google", HttpContext.RequestAborted);
            }
            logger.LogInformation("User logged in with Google.");
            return LocalRedirect(returnUrl);
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage(
                "./LoginWith2fa",
                new { ReturnUrl = returnUrl, RememberMe = false });
        }

        if (result.IsLockedOut)
        {
            ErrorMessage = "We couldn’t log you in right now. Please wait and try again.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email)?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            ErrorMessage = "Google did not provide an email address for this account.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            ErrorMessage =
                "An ApplyWise account already exists for this email. Log in with your password; Google can be linked from account settings later.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        var user = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            AddErrors(createResult);
            ReturnUrl = returnUrl;
            return Page();
        }

        var addLoginResult = await userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            AddErrors(addLoginResult);
            ReturnUrl = returnUrl;
            return Page();
        }

        var displayName = info.Principal.FindFirstValue(ClaimTypes.Name)?.Trim();
        displayName = string.IsNullOrWhiteSpace(displayName)
            ? email.Split('@')[0]
            : displayName;
        var claimResult = await userManager.AddClaimAsync(
            user,
            new Claim("display_name", displayName));
        if (!claimResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            AddErrors(claimResult);
            ReturnUrl = returnUrl;
            return Page();
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            dbContext.CareerProfiles.Add(new CareerProfile
            {
                UserId = user.Id,
                FullName = displayName,
                CreatedAt = now,
                UpdatedAt = now
            });
            dbContext.UserAccountActivities.Add(new UserAccountActivity
            {
                UserId = user.Id,
                RegisteredAt = now,
                LastActivityAt = now
            });
            await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
            await events.RecordAsync(
                ProductEventNames.AccountRegistered,
                "google",
                user.Id,
                cancellationToken: HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not create the initial career profile for a Google account.");
            await userManager.DeleteAsync(user);
            ErrorMessage = "We couldn’t finish setting up your account. Please try again.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        await signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
        var adminRoleChanged = await adminRoles.SynchronizeUserAsync(user);
        if (adminRoleChanged)
        {
            await signInManager.RefreshSignInAsync(user);
        }
        await events.RecordLoginAsync(user.Id, "google", HttpContext.RequestAborted);
        logger.LogInformation("User created a new account with Google.");
        return RedirectToAction("Index", "Onboarding");
    }

    private string GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Content("~/");

    private void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }

    private IActionResult InvalidPasswordLogin()
    {
        ModelState.AddModelError(string.Empty, InvalidPasswordLoginMessage);
        return Page();
    }
}
