using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Admin;
using ApplyWise.Web.Services.Security;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Areas.Identity.Pages.Account;

[EnableRateLimiting("account-security")]
public class RegisterConfirmationModel(
    UserManager<IdentityUser> userManager,
    IAccountSecurityCodeService securityCodes,
    IAccountSecurityRequestQueue securityRequests,
    IProductEventRecorder events,
    IOptions<AdminAccessOptions> adminOptions,
    ILoginTimingProtector timingProtector,
    IApplicationLockProvider operationLocks,
    ILogger<RegisterConfirmationModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? ConfirmationDeliveryMessage { get; set; }

    public bool Succeeded { get; private set; }
    public string? DeliveryMessage { get; private set; }
    public string LoginUrl { get; private set; } = "/Identity/Account/Login";

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the six-digit code from your email.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Enter a valid six-digit code.")]
        [Display(Name = "Verification code")]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = PasswordRequirements.MinimumLength)]
        [StrongPassword]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
        [Display(Name = "Confirm new password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }

    public IActionResult OnGet(string? email, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToPage("./Register");
        }

        Input.Email = email.Trim();
        Input.ReturnUrl = GetSafeReturnUrl(returnUrl);
        PrepareLinks();
        DeliveryMessage = ConfirmationDeliveryMessage;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return await ConfirmRegistrationAsync();
        }
        finally
        {
            await timingProtector.EnforceMinimumResponseTimeAsync(
                startedAt,
                HttpContext.RequestAborted);
        }
    }

    private async Task<IActionResult> ConfirmRegistrationAsync()
    {
        Input.Email = Input.Email.Trim();
        Input.ReturnUrl = GetSafeReturnUrl(Input.ReturnUrl);
        PrepareLinks();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Keep retention from deleting an old pending account after its code is
        // consumed but before Identity commits the confirmation.
        await using var operationLease = await operationLocks.TryAcquireAsync(
            PendingRegistrationRetention.OperationLockResource,
            TimeSpan.FromSeconds(5),
            HttpContext.RequestAborted) ?? throw new ResourceLockUnavailableException(
                "Account verification is busy. Try again shortly.");

        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is null || adminOptions.Value.Contains(Input.Email))
        {
            AddInvalidCodeError();
            return Page();
        }

        if (await userManager.IsEmailConfirmedAsync(user))
        {
            AddInvalidCodeError();
            return Page();
        }

        var verification = await securityCodes.VerifyAsync(
            user.Id,
            AccountSecurityAction.ConfirmEmail,
            Input.Code,
            HttpContext.RequestAborted);

        if (!verification.Succeeded || verification.CodeId is null)
        {
            AddInvalidCodeError();
            return Page();
        }

        // Email possession, not whichever password happened to be submitted
        // first, owns an unconfirmed account. Replace the pending credential
        // before confirmation to prevent pre-registration account hijacking.
        var passwordToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var passwordResult = await userManager.ResetPasswordAsync(
            user,
            passwordToken,
            Input.Password);
        if (!passwordResult.Succeeded)
        {
            logger.LogWarning(
                "Credential finalization failed after a valid verification code for user {UserId}.",
                user.Id);
            AddIdentityErrors(passwordResult);
            return Page();
        }

        var identityToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmation = await userManager.ConfirmEmailAsync(user, identityToken);
        if (!confirmation.Succeeded)
        {
            logger.LogWarning(
                "Email confirmation failed after a valid verification code for user {UserId}.",
                user.Id);
            ModelState.AddModelError(string.Empty, "We could not confirm your email. Request a new code and try again.");
            return Page();
        }

        await securityCodes.ConsumeAsync(verification.CodeId.Value, HttpContext.RequestAborted);
        await events.RecordAsync(
            ProductEventNames.EmailConfirmed,
            "verification_code",
            user.Id,
            cancellationToken: HttpContext.RequestAborted);
        MarkSucceeded();
        return Page();
    }

    public IActionResult OnPostResend(string? email, string? returnUrl = null)
    {
        ModelState.Clear();
        Input = new InputModel
        {
            Email = (email ?? string.Empty).Trim(),
            ReturnUrl = GetSafeReturnUrl(returnUrl)
        };
        PrepareLinks();

        if (!new EmailAddressAttribute().IsValid(Input.Email))
        {
            ModelState.AddModelError(string.Empty, "Enter the email address used to create your account.");
            return Page();
        }

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
        DeliveryMessage = "If an account is waiting for verification, a new six-digit code will arrive shortly.";
        return Page();
    }

    private void MarkSucceeded()
    {
        Succeeded = true;
        Input.Code = string.Empty;
        Input.Password = string.Empty;
        Input.ConfirmPassword = string.Empty;
        DeliveryMessage = "Your email is verified. You can now log in securely.";
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError("Input.Password", error.Description);
        }
    }

    private void AddInvalidCodeError() =>
        ModelState.AddModelError(
            "Input.Code",
            "That code is invalid or expired. Request a new code and try again.");

    private void PrepareLinks() =>
        LoginUrl = Url.Page("/Account/Login", new { area = "Identity", returnUrl = Input.ReturnUrl })
            ?? "/Identity/Account/Login";

    private string GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Content("~/");
}
