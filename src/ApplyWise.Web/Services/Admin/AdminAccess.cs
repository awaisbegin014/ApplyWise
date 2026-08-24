using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Admin;

public static class AdminAccess
{
    public const string Role = "Admin";
    public const string Policy = "AdminAccess";
    public const string AuthenticationMethodClaim = "amr";
    public const string MfaAuthenticationMethod = "mfa";

    public static bool HasMfaSession(ClaimsPrincipal principal) =>
        principal.HasClaim(AuthenticationMethodClaim, MfaAuthenticationMethod);
}

public sealed class AdminAccessOptions
{
    public const string SectionName = "AdminAccess";

    public string[] Emails { get; set; } = [];
    public bool RequireMfa { get; set; }

    public bool Contains(string? email) => !string.IsNullOrWhiteSpace(email)
        && Emails.Any(candidate => string.Equals(
            candidate.Trim(),
            email.Trim(),
            StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> ValidEmails() => Emails
        .Select(email => email.Trim())
        .Where(email => new EmailAddressAttribute().IsValid(email))
        .Distinct(StringComparer.OrdinalIgnoreCase);
}

public sealed class AdminMfaRequirement : IAuthorizationRequirement;

public sealed class AdminMfaAuthorizationHandler(
    UserManager<IdentityUser> userManager,
    IOptions<AdminAccessOptions> options) : AuthorizationHandler<AdminMfaRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminMfaRequirement requirement)
    {
        if (!options.Value.RequireMfa)
        {
            context.Succeed(requirement);
            return;
        }

        var user = await userManager.GetUserAsync(context.User);
        if (user is not null
            && await userManager.GetTwoFactorEnabledAsync(user)
            && AdminAccess.HasMfaSession(context.User))
        {
            context.Succeed(requirement);
        }
    }
}

public interface IAdminRoleAssignmentService
{
    Task<bool> SynchronizeUserAsync(IdentityUser user);
}

public sealed class AdminRoleAssignmentService(
    UserManager<IdentityUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<AdminAccessOptions> options,
    ILogger<AdminRoleAssignmentService> logger) : IAdminRoleAssignmentService
{
    public async Task<bool> SynchronizeUserAsync(IdentityUser user)
    {
        var shouldBeAdmin = user.EmailConfirmed
            && options.Value.Contains(user.Email);
        var isAdmin = await userManager.IsInRoleAsync(user, AdminAccess.Role);
        if (shouldBeAdmin == isAdmin)
        {
            return false;
        }

        if (shouldBeAdmin && !await roleManager.RoleExistsAsync(AdminAccess.Role))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole(AdminAccess.Role));
            if (!roleResult.Succeeded)
            {
                logger.LogError(
                    "Could not create the administrator role. Error codes: {ErrorCodes}.",
                    string.Join(',', roleResult.Errors.Select(error => error.Code)));
                return false;
            }
        }

        var result = shouldBeAdmin
            ? await userManager.AddToRoleAsync(user, AdminAccess.Role)
            : await userManager.RemoveFromRoleAsync(user, AdminAccess.Role);
        if (!result.Succeeded)
        {
            logger.LogError(
                "Could not synchronize administrator access for user {UserId}. Error codes: {ErrorCodes}.",
                user.Id,
                string.Join(',', result.Errors.Select(error => error.Code)));
            return false;
        }

        logger.LogInformation(
            "Administrator role {AdminState} for user {UserId}.",
            shouldBeAdmin ? "granted" : "removed",
            user.Id);
        return true;
    }
}

public static class AdminRoleSynchronizer
{
    public static async Task SynchronizeAsync(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<AdminAccessOptions>>().Value;
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var assignment = services.GetRequiredService<IAdminRoleAssignmentService>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AdminAccess");

        if (!await roles.RoleExistsAsync(AdminAccess.Role))
        {
            var result = await roles.CreateAsync(new IdentityRole(AdminAccess.Role));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("The administrator role could not be initialized.");
            }
        }

        var configuredEmails = options.ValidEmails().ToArray();
        foreach (var email in configuredEmails)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                logger.LogWarning(
                    "An administrator email is configured but no account exists for it yet.");
                continue;
            }

            await assignment.SynchronizeUserAsync(user);
        }

        var configuredIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var email in configuredEmails)
        {
            var configuredUser = await users.FindByEmailAsync(email);
            if (configuredUser is not null)
            {
                configuredIds.Add(configuredUser.Id);
            }
        }

        foreach (var existingAdmin in await users.GetUsersInRoleAsync(AdminAccess.Role))
        {
            if (!configuredIds.Contains(existingAdmin.Id))
            {
                await assignment.SynchronizeUserAsync(existingAdmin);
            }
        }
    }
}
