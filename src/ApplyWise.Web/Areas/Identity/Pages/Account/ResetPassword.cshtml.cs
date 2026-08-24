using System.ComponentModel.DataAnnotations;
using System.Text;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace ApplyWise.Web.Areas.Identity.Pages.Account;

[EnableRateLimiting("account-security")]
public class ResetPasswordModel(
    UserManager<IdentityUser> userManager,
    IAccountSecurityCodeService securityCodes,
    IAccountSecurityRequestQueue securityRequests,
    ILoginTimingProtector timingProtector,
    ILogger<ResetPasswordModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? DeliveryMessage { get; private set; }
    public bool IsLegacyLink => !string.IsNullOrWhiteSpace(Input.LegacyToken);

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [RegularExpression(@"^\d{6}$", ErrorMessage = "Enter a valid six-digit code.")]
        [Display(Name = "Reset code")]
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

        public string? LegacyToken { get; set; }
    }

    public IActionResult OnGet(string? email = null, string? code = null)
    {
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(code))
        {
            return RedirectToPage("./ForgotPassword");
        }

        Input.Email = email?.Trim() ?? string.Empty;
        Input.LegacyToken = code;
        DeliveryMessage = string.IsNullOrWhiteSpace(code)
            ? "If an account exists for this address, a six-digit reset code has been sent."
            : "This older reset link is still supported. Confirm your email and choose a new password.";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return await ResetPasswordAsync();
        }
        finally
        {
            await timingProtector.EnforceMinimumResponseTimeAsync(
                startedAt,
                HttpContext.RequestAborted);
        }
    }

    private async Task<IActionResult> ResetPasswordAsync()
    {
        Input.Email = Input.Email.Trim();
        if (!IsLegacyLink && string.IsNullOrWhiteSpace(Input.Code))
        {
            ModelState.AddModelError("Input.Code", "Enter the six-digit code from your email.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is null || !await userManager.IsEmailConfirmedAsync(user))
        {
            AddInvalidCodeError();
            return Page();
        }

        if (IsLegacyLink)
        {
            return await ResetFromLegacyLinkAsync(user);
        }

        var verification = await securityCodes.VerifyAsync(
            user.Id,
            AccountSecurityAction.ResetPassword,
            Input.Code,
            HttpContext.RequestAborted);

        if (!verification.Succeeded || verification.CodeId is null)
        {
            AddInvalidCodeError();
            return Page();
        }

        var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, identityToken, Input.Password);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return Page();
        }

        await securityCodes.ConsumeAsync(verification.CodeId.Value, HttpContext.RequestAborted);
        logger.LogInformation("A password was reset using an ApplyWise one-time code.");
        return RedirectToPage("./ResetPasswordConfirmation");
    }

    public IActionResult OnPostResend(string? email)
    {
        ModelState.Clear();
        Input = new InputModel { Email = (email ?? string.Empty).Trim() };

        if (!new EmailAddressAttribute().IsValid(Input.Email))
        {
            ModelState.AddModelError("Input.Email", "Enter a valid email address.");
            return Page();
        }

        if (!securityRequests.TryQueue(
                Input.Email,
                AccountSecurityAction.ResetPassword))
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            Response.Headers.RetryAfter = "30";
            ModelState.AddModelError(
                string.Empty,
                "Email delivery is busy. Wait 30 seconds and try again.");
            return Page();
        }
        DeliveryMessage = "If an account exists for this address, a new six-digit reset code will arrive shortly.";
        return Page();
    }

    private async Task<IActionResult> ResetFromLegacyLinkAsync(IdentityUser user)
    {
        string identityToken;
        try
        {
            identityToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.LegacyToken!));
        }
        catch (FormatException)
        {
            AddInvalidCodeError();
            return Page();
        }

        var result = await userManager.ResetPasswordAsync(user, identityToken, Input.Password);
        if (!result.Succeeded)
        {
            AddInvalidCodeError();
            return Page();
        }

        logger.LogInformation("A password was reset using a legacy ApplyWise reset link.");
        return RedirectToPage("./ResetPasswordConfirmation");
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
            IsLegacyLink ? string.Empty : "Input.Code",
            "That reset code or link is invalid or expired. Request a new code and try again.");
}
