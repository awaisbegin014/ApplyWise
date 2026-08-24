using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace ApplyWise.Web.Services.Admin;

/// <summary>
/// Keeps administrator identities out of the candidate workspace. Administrators
/// may use only the owner console and the account-security surface needed for MFA,
/// password management, and sign-out.
/// </summary>
public sealed class AdminOnlyAccountMiddleware(
    RequestDelegate next,
    IOptions<AdminAccessOptions> adminOptions)
{
    private static readonly PathString[] StaticAssetPrefixes =
    [
        new("/css"),
        new("/js"),
        new("/lib"),
        new("/images"),
        new("/favicon"),
        new("/_framework"),
        new("/_blazor"),
        new("/_vs")
    ];

    private static readonly PathString[] MfaManagementPaths =
    [
        new("/Identity/Account/Manage/TwoFactorAuthentication"),
        new("/Identity/Account/Manage/EnableAuthenticator"),
        new("/Identity/Account/Manage/GenerateRecoveryCodes"),
        new("/Identity/Account/Manage/ShowRecoveryCodes"),
        new("/Identity/Account/Manage/ResetAuthenticator")
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (context.User.Identity?.IsAuthenticated == true
            && path.StartsWithSegments(
                "/Identity/Account/Manage",
                StringComparison.OrdinalIgnoreCase)
            && !MfaManagementPaths.Any(candidate => IsExactPath(path, candidate)))
        {
            if (HttpMethods.IsGet(context.Request.Method)
                || HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.Redirect("/settings");
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            }

            return;
        }

        var authenticatedEmail = context.User.FindFirstValue(ClaimTypes.Email)
            ?? context.User.Identity?.Name;
        var hasAdminRole = context.User.IsInRole(AdminAccess.Role);
        var isConfiguredAdmin = adminOptions.Value.Contains(authenticatedEmail);
        var isAdminAccount = hasAdminRole || isConfiguredAdmin;
        if (context.User.Identity?.IsAuthenticated != true || !isAdminAccount)
        {
            await next(context);
            return;
        }

        if (isConfiguredAdmin && !hasAdminRole)
        {
            if (IsExactPath(path, "/owner/session"))
            {
                await next(context);
            }
            else if (HttpMethods.IsGet(context.Request.Method)
                     || HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.Redirect("/owner/session");
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            }

            return;
        }

        var hasRequiredMfa = !adminOptions.Value.RequireMfa
            || AdminAccess.HasMfaSession(context.User);

        if (path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase))
        {
            if (hasRequiredMfa)
            {
                await next(context);
            }
            else
            {
                context.Response.Redirect("/settings");
            }

            return;
        }

        if (MfaManagementPaths.Any(candidate => IsExactPath(path, candidate)))
        {
            if (hasRequiredMfa)
            {
                await next(context);
            }
            else if (HttpMethods.IsGet(context.Request.Method)
                     || HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.Redirect("/settings");
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            }

            return;
        }

        if (IsStaticAsset(path)
            || IsExactPath(path, "/Home/Error")
            || path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            || IsExactPath(path, "/Identity/Account/AccessDenied")
            || IsExactPath(path, "/Identity/Account/Logout"))
        {
            await next(context);
            return;
        }

        if (IsExactPath(path, "/settings"))
        {
            if (HttpMethods.IsGet(context.Request.Method)
                || HttpMethods.IsHead(context.Request.Method))
            {
                await next(context);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            }

            return;
        }

        if (IsExactPath(path, "/settings/security-code/password")
            || IsExactPath(path, "/settings/change-password"))
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                await next(context);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            }

            return;
        }

        if (!HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Response.Redirect(hasRequiredMfa ? "/admin" : "/settings");
    }

    private static bool IsStaticAsset(PathString path) =>
        StaticAssetPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsExactPath(PathString path, PathString expected) =>
        path.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
