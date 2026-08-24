using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace ApplyWise.Web.Areas.Identity.Pages.Account;

[EnableRateLimiting("account-security")]
public class ResendEmailConfirmationModel(
    IAccountSecurityRequestQueue securityRequests) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }

    public void OnGet(string? email = null, string? returnUrl = null)
    {
        Input.Email = email?.Trim() ?? string.Empty;
        Input.ReturnUrl = GetSafeReturnUrl(returnUrl);
    }

    public IActionResult OnPost()
    {
        Input.Email = Input.Email.Trim();
        Input.ReturnUrl = GetSafeReturnUrl(Input.ReturnUrl);
        if (!ModelState.IsValid)
        {
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

        // Always continue to the same screen so this form cannot reveal registered email addresses.
        return RedirectToPage(
            "./RegisterConfirmation",
            new { email = Input.Email, returnUrl = Input.ReturnUrl });
    }

    private string GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Content("~/");
}
