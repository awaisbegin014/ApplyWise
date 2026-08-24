using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Contact;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.ViewModels.Contact;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Controllers;

[AllowAnonymous]
[Route("contact")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ContactController(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IContactMessageStore contactMessages,
    IHumanChallengeVerifier humanChallenge) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var model = new ContactFormViewModel();
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            model.Email = User.FindFirstValue(ClaimTypes.Email)
                ?? User.Identity.Name
                ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(userId))
            {
                model.FullName = await dbContext.CareerProfiles
                    .AsNoTracking()
                    .Where(profile => profile.UserId == userId)
                    .Select(profile => profile.FullName)
                    .SingleOrDefaultAsync(HttpContext.RequestAborted)
                    ?? string.Empty;
            }
        }

        return View(model);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("contact")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Index(ContactFormViewModel model)
    {
        model.FullName = model.FullName?.Trim() ?? string.Empty;
        model.Email = model.Email?.Trim() ?? string.Empty;
        model.Subject = model.Subject?.Trim() ?? string.Empty;
        model.Message = model.Message?.Trim() ?? string.Empty;
        model.Website = model.Website?.Trim();

        if (!string.IsNullOrWhiteSpace(model.Website))
        {
            return RedirectToAction(nameof(Sent));
        }

        if (!ValidateNormalizedModel(model))
        {
            return View(model);
        }

        if (User.Identity?.IsAuthenticated != true
            && !await humanChallenge.VerifyAsync(
                HttpContext,
                HumanChallengeActions.Contact,
                HttpContext.RequestAborted))
        {
            ModelState.AddModelError(
                string.Empty,
                "Complete the human verification and try again.");
            return View(model);
        }

        var message = new ContactMessage
        {
            UserId = User.Identity?.IsAuthenticated == true
                ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                : null,
            FullName = model.FullName,
            Email = model.Email,
            Topic = model.Topic!.Value,
            Subject = model.Subject,
            Body = model.Message,
            CreatedAt = timeProvider.GetUtcNow()
        };

        var stored = await contactMessages.TryStoreAsync(
            message,
            HttpContext.RequestAborted);
        if (!stored)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            Response.Headers.RetryAfter = "60";
            ModelState.AddModelError(
                string.Empty,
                "Our support inbox is temporarily busy. Please wait a minute and try again.");
            return View(model);
        }

        return RedirectToAction(nameof(Sent));
    }

    [HttpGet("sent")]
    public IActionResult Sent() => View();

    private bool ValidateNormalizedModel(ContactFormViewModel model)
    {
        ModelState.Clear();
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            validationResults,
            validateAllProperties: true);
        foreach (var result in validationResults)
        {
            var memberNames = result.MemberNames.DefaultIfEmpty(string.Empty);
            foreach (var memberName in memberNames)
            {
                ModelState.AddModelError(
                    memberName,
                    result.ErrorMessage ?? "The provided value is invalid.");
            }
        }

        if (!MailboxAddressNormalizer.TryNormalize(model.Email, out var normalizedEmail))
        {
            ModelState.AddModelError(
                nameof(model.Email),
                "Enter one email address without a display name or link parameters.");
        }
        else
        {
            model.Email = normalizedEmail;
        }

        return ModelState.IsValid;
    }
}
