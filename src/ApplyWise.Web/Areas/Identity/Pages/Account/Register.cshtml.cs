using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.Services.Profiles;
using ApplyWise.Web.Services.Gmail;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Admin;
using ApplyWise.Web.Services.Security;

namespace ApplyWise.Web.Areas.Identity.Pages.Account;

[EnableRateLimiting("account-security")]
public class RegisterModel(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    IAccountSecurityRequestQueue securityRequests,
    ApplicationDbContext dbContext,
    IProductEventRecorder events,
    IAdminRoleAssignmentService adminRoles,
    IPendingRegistrationStore pendingRegistrations,
    ILoginTimingProtector timingProtector,
    IOptions<AdminAccessOptions> adminOptions,
    IOptions<GoogleIntegrationOptions> googleOptions,
    IHumanChallengeVerifier humanChallenge,
    ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }
    public bool IsGoogleLoginEnabled => googleOptions.Value.IsConfigured;

    public sealed class InputModel
    {
        [Required]
        [StringLength(100, MinimumLength = 2)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = PasswordRequirements.MinimumLength)]
        [StrongPassword]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = GetSafeReturnUrl(returnUrl);
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return await RegisterAsync(returnUrl);
        }
        finally
        {
            await timingProtector.EnforceMinimumResponseTimeAsync(
                startedAt,
                HttpContext.RequestAborted);
        }
    }

    private async Task<IActionResult> RegisterAsync(string? returnUrl)
    {
        returnUrl = GetSafeReturnUrl(returnUrl);
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        Input.Email = Input.Email.Trim();
        Input.FullName = Input.FullName.Trim();
        if (!await humanChallenge.VerifyAsync(
                HttpContext,
                HumanChallengeActions.Register,
                HttpContext.RequestAborted))
        {
            ModelState.AddModelError(
                string.Empty,
                "Complete the human verification and try again.");
            return Page();
        }

        if (adminOptions.Value.Contains(Input.Email))
        {
            return ContinueToEmailVerification(returnUrl, queueDelivery: true);
        }

        var pendingRegistration = await pendingRegistrations.TryCreateAsync(
            Input.Email,
            Input.Password,
            HttpContext.RequestAborted);
        if (pendingRegistration.Status == PendingRegistrationStatus.Existing)
        {
            return ContinueToEmailVerification(returnUrl, queueDelivery: true);
        }

        if (pendingRegistration.Status == PendingRegistrationStatus.CapacityReached)
        {
            logger.LogWarning(
                "A registration was not stored because the pending-account capacity was reached.");
            return ContinueToEmailVerification(returnUrl, queueDelivery: true);
        }

        var user = pendingRegistration.User;
        var result = pendingRegistration.IdentityResult;

        if (result.Succeeded && user is not null)
        {
            logger.LogInformation("User created a new account with password.");
            var displayNameResult = await userManager.AddClaimAsync(
                user,
                new Claim("display_name", Input.FullName));
            if (!displayNameResult.Succeeded)
            {
                await userManager.DeleteAsync(user);
                foreach (var error in displayNameResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return Page();
            }

            try
            {
                var registeredAt = DateTimeOffset.UtcNow;
                dbContext.CareerProfiles.Add(new CareerProfile
                {
                    UserId = user.Id,
                    FullName = Input.FullName,
                    SelectedAvatarId = AvatarCatalog.GeneralNeutralId,
                    CreatedAt = registeredAt,
                    UpdatedAt = registeredAt
                });
                await dbContext.SaveChangesAsync();
                await events.RecordAsync(
                    ProductEventNames.AccountRegistered,
                    "password",
                    user.Id,
                    cancellationToken: HttpContext.RequestAborted);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not create the initial career profile for a new account.");
                await userManager.DeleteAsync(user);
                ModelState.AddModelError(string.Empty, "We couldn’t finish setting up your account. Please try again.");
                return Page();
            }

            if (userManager.Options.SignIn.RequireConfirmedAccount)
            {
                return ContinueToEmailVerification(returnUrl, queueDelivery: true);
            }

            var isAdminAccount = await adminRoles.SynchronizeUserAsync(user)
                || await userManager.IsInRoleAsync(user, AdminAccess.Role);
            await signInManager.SignInAsync(user, isPersistent: false);
            return isAdminAccount
                ? RedirectToAction("Settings", "Dashboard")
                : RedirectToAction("Index", "Onboarding");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return Page();
    }

    private string GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Action("Index", "Onboarding") ?? "/onboarding";

    private IActionResult ContinueToEmailVerification(
        string returnUrl,
        bool queueDelivery)
    {
        if (queueDelivery)
        {
            if (!securityRequests.TryQueue(
                    Input.Email,
                    AccountSecurityAction.ConfirmEmail))
            {
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                Response.Headers.RetryAfter = "30";
                ModelState.AddModelError(
                    string.Empty,
                    "Email delivery is busy. Wait 30 seconds and try again.");
                return Page();
            }
        }

        TempData["ConfirmationDeliveryMessage"] =
            "If this address can be registered, a six-digit verification code will arrive shortly.";
        return RedirectToPage(
            "RegisterConfirmation",
            new { email = Input.Email, returnUrl });
    }
}
