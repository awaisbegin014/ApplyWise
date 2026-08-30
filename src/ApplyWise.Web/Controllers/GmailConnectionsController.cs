using System.Security.Claims;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Gmail;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("connections/gmail")]
public sealed class GmailConnectionsController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    IGmailCredentialProtector credentialProtector,
    IGmailImportService gmailImportService,
    IApplicationLockProvider operationLocks,
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleIntegrationOptions> googleOptions,
    ILogger<GmailConnectionsController> logger,
    IWorkspaceQuotaGate? gmailStateGate = null) : Controller
{
    private readonly IWorkspaceQuotaGate _gmailStateGate =
        gmailStateGate ?? new WorkspaceQuotaGate(dbContext);

    [HttpGet("failure")]
    public IActionResult Failure(
        [FromQuery(Name = GmailOAuthFailure.QueryParameter)] string? reason)
    {
        TempData["ImportError"] = GmailOAuthFailure.GetUserMessage(reason);
        return RedirectToAction("Index", "ApplicationImports");
    }

    [HttpPost("connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect()
    {
        if (!googleOptions.Value.IsGmailImportConfigured)
        {
            TempData["ImportError"] =
                "Google integration is not configured on this ApplyWise deployment.";
            return RedirectToAction("Index", "ApplicationImports");
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var flowId = RandomNumberGenerator.GetHexString(
            GmailAuthenticationDefaults.FlowIdHexLength);
        await using (var operationLease = await operationLocks.TryAcquireAsync(
            $"gmail-user:{user.Id}",
            Timeout.InfiniteTimeSpan,
            HttpContext.RequestAborted)
            ?? throw new ResourceLockUnavailableException(
                "The Gmail connection could not be locked for authorization."))
        {
            var flowStored = await _gmailStateGate.RunAsync(
                WorkspaceQuotaResources.GmailConnections,
                user.Id,
                async gateCancellationToken =>
                {
                    dbContext.ChangeTracker.Clear();
                    if (!await dbContext.Users.AsNoTracking().AnyAsync(
                            candidate => candidate.Id == user.Id,
                            gateCancellationToken))
                    {
                        return false;
                    }

                    var flow = await dbContext.GmailOAuthFlows.SingleOrDefaultAsync(
                        candidate => candidate.UserId == user.Id,
                        gateCancellationToken);
                    if (flow is null)
                    {
                        dbContext.GmailOAuthFlows.Add(new GmailOAuthFlow
                        {
                            UserId = user.Id,
                            FlowId = flowId,
                            StartedAt = DateTimeOffset.UtcNow
                        });
                    }
                    else
                    {
                        flow.FlowId = flowId;
                        flow.StartedAt = DateTimeOffset.UtcNow;
                    }

                    await dbContext.SaveChangesAsync(gateCancellationToken);
                    return true;
                },
                HttpContext.RequestAborted);
            if (!flowStored) return Challenge();
        }

        var properties = signInManager.ConfigureExternalAuthenticationProperties(
            GmailAuthenticationDefaults.Scheme,
            Url.Action(nameof(Callback)),
            user.Id);
        properties.Items[GmailAuthenticationDefaults.FlowIdProperty] = flowId;
        return Challenge(properties, GmailAuthenticationDefaults.Scheme);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback()
    {
        if (!googleOptions.Value.IsGmailImportConfigured)
        {
            TempData["ImportError"] =
                "Gmail import is not enabled on this ApplyWise deployment.";
            return RedirectToAction("Index", "ApplicationImports");
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        try
        {
            var info = await signInManager.GetExternalLoginInfoAsync(user.Id);
            if (info is null
                || !string.Equals(
                    info.LoginProvider,
                    GmailAuthenticationDefaults.Scheme,
                    StringComparison.Ordinal)
                || !info.Principal.HasClaim(
                    GmailAuthenticationDefaults.FlowClaimType,
                    GmailAuthenticationDefaults.FlowClaimValue))
            {
                TempData["ImportError"] =
                    "Gmail authorization could not be verified. Please try connecting again.";
                return RedirectToAction("Index", "ApplicationImports");
            }

            var email = (
                    info.Principal.FindFirstValue(ClaimTypes.Email)
                    ?? info.Principal.FindFirstValue("email"))
                ?.Trim();
            var refreshToken = info.AuthenticationTokens?
                .FirstOrDefault(token => token.Name == "refresh_token")?
                .Value;
            var flowId = info.AuthenticationProperties is { } authenticationProperties
                && authenticationProperties.Items.TryGetValue(
                    GmailAuthenticationDefaults.FlowIdProperty,
                    out var storedFlowId)
                    ? storedFlowId
                    : null;

            var emailMissing = string.IsNullOrWhiteSpace(email);
            var flowIdMissing = string.IsNullOrWhiteSpace(flowId);
            var flowIdHasValidLength = !flowIdMissing
                && flowId!.Length == GmailAuthenticationDefaults.FlowIdHexLength;
            var flowIdHasValidFormat = flowIdHasValidLength
                && flowId!.All(Uri.IsHexDigit);
            if (emailMissing || !flowIdHasValidFormat)
            {
                logger.LogWarning(
                    "Gmail callback validation failed. Missing email claim: {MissingEmailClaim}; missing flow identifier: {MissingFlowIdentifier}; valid flow length: {ValidFlowLength}; valid flow format: {ValidFlowFormat}.",
                    emailMissing,
                    flowIdMissing,
                    flowIdHasValidLength,
                    flowIdHasValidFormat);
                TempData["ImportError"] = emailMissing
                    ? "Google authenticated the account but did not return its email address. For a school or work Google Workspace account, confirm that Gmail and profile access are allowed, then start a new connection request."
                    : "That Gmail connection request expired or lost its browser session. Return here, click Connect Gmail once, and finish Google's screens in the same browser session.";
                return RedirectToAction("Index", "ApplicationImports");
            }

            await using (var operationLease = await operationLocks.TryAcquireAsync(
                $"gmail-user:{user.Id}",
                Timeout.InfiniteTimeSpan,
                HttpContext.RequestAborted)
                ?? throw new ResourceLockUnavailableException(
                    "The Gmail connection could not be locked for authorization."))
            {
                var persistenceOutcome = await _gmailStateGate.RunAsync(
                    WorkspaceQuotaResources.GmailConnections,
                    user.Id,
                    async gateCancellationToken =>
                    {
                        dbContext.ChangeTracker.Clear();
                        var localUserStillExists = await dbContext.Users
                            .AsNoTracking()
                            .AnyAsync(
                                candidate => candidate.Id == user.Id,
                                gateCancellationToken);
                        if (!localUserStillExists)
                        {
                            return GmailCallbackPersistenceOutcome.UserMissing;
                        }

                        var activeFlow = await dbContext.GmailOAuthFlows
                            .SingleOrDefaultAsync(
                                candidate => candidate.UserId == user.Id,
                                gateCancellationToken);
                        if (activeFlow is null
                            || !activeFlow.FlowId.Equals(
                                flowId,
                                StringComparison.Ordinal))
                        {
                            return GmailCallbackPersistenceOutcome.StaleAuthorization;
                        }

                        var connection = await dbContext.GmailConnections
                            .SingleOrDefaultAsync(
                                item => item.UserId == user.Id,
                                gateCancellationToken);
                        if (connection?.LastErrorCode
                            == GmailConnectionStates.RevocationPending)
                        {
                            dbContext.GmailOAuthFlows.Remove(activeFlow);
                            await dbContext.SaveChangesAsync(gateCancellationToken);
                            return GmailCallbackPersistenceOutcome.DisconnectionPending;
                        }

                        if (string.IsNullOrWhiteSpace(refreshToken)
                            && connection is null)
                        {
                            dbContext.GmailOAuthFlows.Remove(activeFlow);
                            await dbContext.SaveChangesAsync(gateCancellationToken);
                            return GmailCallbackPersistenceOutcome.OfflineAccessMissing;
                        }

                        var now = DateTimeOffset.UtcNow;
                        if (connection is null)
                        {
                            connection = new GmailConnection
                            {
                                UserId = user.Id,
                                EmailAddress = email!,
                                ProtectedRefreshToken =
                                    credentialProtector.Protect(refreshToken!),
                                ConnectedAt = now,
                                UpdatedAt = now,
                                NextSyncAt = now,
                                AutoAddHighConfidenceApplications = true
                            };
                            dbContext.GmailConnections.Add(connection);
                        }
                        else
                        {
                            connection.EmailAddress = email!;
                            if (!string.IsNullOrWhiteSpace(refreshToken))
                            {
                                connection.ProtectedRefreshToken =
                                    credentialProtector.Protect(refreshToken);
                            }
                            connection.UpdatedAt = now;
                            connection.NextSyncAt = now;
                            connection.LastErrorCode = null;
                        }

                        dbContext.GmailOAuthFlows.Remove(activeFlow);
                        await dbContext.SaveChangesAsync(gateCancellationToken);
                        return GmailCallbackPersistenceOutcome.Saved;
                    },
                    HttpContext.RequestAborted);

                if (persistenceOutcome is
                    GmailCallbackPersistenceOutcome.UserMissing
                    or GmailCallbackPersistenceOutcome.DisconnectionPending
                    or GmailCallbackPersistenceOutcome.StaleAuthorization)
                {
                    if (!string.IsNullOrWhiteSpace(refreshToken))
                    {
                        await GmailTokenRevocation.TryRevokeTokenAsync(
                            refreshToken,
                            connectionId: null,
                            httpClientFactory,
                            logger,
                            HttpContext.RequestAborted);
                    }

                    if (persistenceOutcome == GmailCallbackPersistenceOutcome.UserMissing)
                    {
                        await signInManager.SignOutAsync();
                        TempData["ImportError"] =
                            "Your ApplyWise account no longer exists. The new Gmail authorization was discarded.";
                        return RedirectToPage("/Account/Login", new { area = "Identity" });
                    }

                    if (persistenceOutcome == GmailCallbackPersistenceOutcome.StaleAuthorization)
                    {
                        TempData["ImportError"] =
                            "That Gmail authorization request is no longer active. Start a new connection request.";
                        return RedirectToAction("Index", "ApplicationImports");
                    }

                    TempData["ImportError"] =
                        "Gmail disconnection is already pending. The new Google authorization was discarded.";
                    return RedirectToAction("Index", "ApplicationImports");
                }

                if (persistenceOutcome
                    == GmailCallbackPersistenceOutcome.OfflineAccessMissing)
                {
                    TempData["ImportError"] =
                        "Google did not provide offline access. Revoke ApplyWise in your Google Account and connect again.";
                    return RedirectToAction("Index", "ApplicationImports");
                }
            }

            var syncResult = await gmailImportService.SyncUserAsync(
                user.Id,
                HttpContext.RequestAborted);
            TempData[syncResult.Succeeded ? "ImportSuccess" : "ImportError"] =
                syncResult.Message;
            return RedirectToAction("Index", "ApplicationImports");
        }
        finally
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    [HttpPost("disconnect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect()
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();
        var connection = await dbContext.GmailConnections
            .SingleOrDefaultAsync(
                item => item.UserId == userId,
                HttpContext.RequestAborted);
        if (connection is not null)
        {
            connection.AutoAddHighConfidenceApplications = false;
            connection.NextSyncAt = DateTimeOffset.MaxValue;
            connection.LastErrorCode = GmailConnectionStates.RevocationPending;
            connection.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Account deletion or another disconnect may have removed the
                // row. The authoritative check happens under the lifecycle lock.
                dbContext.ChangeTracker.Clear();
            }
        }

        await using var operationLease = await operationLocks.TryAcquireAsync(
            $"gmail-user:{userId}",
            Timeout.InfiniteTimeSpan,
            HttpContext.RequestAborted)
            ?? throw new ResourceLockUnavailableException(
                "The Gmail connection could not be locked for disconnection.");

        dbContext.ChangeTracker.Clear();
        var pendingFlow = await dbContext.GmailOAuthFlows
            .SingleOrDefaultAsync(
                item => item.UserId == userId,
                HttpContext.RequestAborted);
        if (pendingFlow is not null)
        {
            dbContext.GmailOAuthFlows.Remove(pendingFlow);
        }
        connection = await dbContext.GmailConnections
            .SingleOrDefaultAsync(
                item => item.UserId == userId,
                HttpContext.RequestAborted);
        if (connection is null)
        {
            await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
            TempData["ImportSuccess"] = "Gmail is already disconnected.";
            return RedirectToAction("Index", "ApplicationImports");
        }

        // A sync that was already running may have written its completion state
        // before releasing the cross-instance lease. Reassert the user's intent
        // after that sync has fully stopped and before touching the Google token.
        connection.AutoAddHighConfidenceApplications = false;
        connection.NextSyncAt = DateTimeOffset.MaxValue;
        connection.LastErrorCode = GmailConnectionStates.RevocationPending;
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);

        var revoked = await GmailTokenRevocation.TryRevokeStoredTokenAsync(
            connection,
            credentialProtector,
            httpClientFactory,
            logger,
            HttpContext.RequestAborted);

        dbContext.ChangeTracker.Clear();
        var revokedConnection = await dbContext.GmailConnections
            .SingleOrDefaultAsync(
                item => item.Id == connection.Id && item.UserId == userId,
                HttpContext.RequestAborted);
        if (revokedConnection is not null)
        {
            dbContext.GmailConnections.Remove(revokedConnection);
        }
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
        if (revoked)
        {
            TempData["ImportSuccess"] =
                "Gmail was disconnected and pending email imports were removed. Accepted applications were kept.";
        }
        else
        {
            // Never retain a usable local token merely because the remote
            // endpoint was unavailable. The user can finish revocation in
            // Google Account permissions without risking a future local sync.
            TempData["ImportError"] =
                "Gmail was disconnected locally, but Google did not confirm revocation. Remove ApplyWise from your Google Account permissions to finish revoking access.";
        }
        return RedirectToAction("Index", "ApplicationImports");
    }

    private enum GmailCallbackPersistenceOutcome
    {
        Saved,
        UserMissing,
        StaleAuthorization,
        DisconnectionPending,
        OfflineAccessMissing
    }
}
