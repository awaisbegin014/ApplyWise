namespace ApplyWise.Web.Services.Gmail;

public static class GmailOAuthFailure
{
    public const string QueryParameter = "reason";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
    public const string GoogleUnavailable = "google_unavailable";
    public const string Configuration = "configuration";
    public const string AccessBlocked = "access_blocked";
    public const string Failed = "failed";

    public static string Classify(
        string? providerError,
        Exception? failure,
        string? providerDescription = null)
    {
        var normalizedError = providerError?.Trim().ToLowerInvariant();
        if (normalizedError == "access_denied")
        {
            return LooksLikeAccessPolicyBlock(providerDescription)
                   || LooksLikeAccessPolicyBlock(failure?.Message)
                ? AccessBlocked
                : Cancelled;
        }

        if (normalizedError is "admin_policy_enforced" or "org_internal")
        {
            return AccessBlocked;
        }

        if (normalizedError is "server_error" or "temporarily_unavailable")
        {
            return GoogleUnavailable;
        }

        if (normalizedError is
            "invalid_client" or
            "unauthorized_client" or
            "invalid_request" or
            "invalid_scope" or
            "redirect_uri_mismatch")
        {
            return Configuration;
        }

        if (normalizedError is
            "interaction_required" or
            "login_required" or
            "consent_required")
        {
            return Expired;
        }

        var failureMessage = failure?.Message;
        if (!string.IsNullOrWhiteSpace(failureMessage)
            && (failureMessage.Contains("correlation", StringComparison.OrdinalIgnoreCase)
                || failureMessage.Contains("state", StringComparison.OrdinalIgnoreCase)
                || failureMessage.Contains("expired", StringComparison.OrdinalIgnoreCase)))
        {
            return Expired;
        }

        return Failed;
    }

    private static bool LooksLikeAccessPolicyBlock(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        return value.Contains("test user", StringComparison.OrdinalIgnoreCase)
            || value.Contains("currently being tested", StringComparison.OrdinalIgnoreCase)
            || value.Contains("developer hasn't given you access", StringComparison.OrdinalIgnoreCase)
            || value.Contains("developer has not given you access", StringComparison.OrdinalIgnoreCase)
            || value.Contains("access blocked", StringComparison.OrdinalIgnoreCase)
            || value.Contains("admin_policy_enforced", StringComparison.OrdinalIgnoreCase)
            || value.Contains("org_internal", StringComparison.OrdinalIgnoreCase)
            || value.Contains("organization's administrator", StringComparison.OrdinalIgnoreCase)
            || value.Contains("organisation's administrator", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetUserMessage(string? reason) => reason switch
    {
        Cancelled =>
            "Gmail connection was cancelled. Google did not grant access, and no Gmail data was connected. Click Connect Gmail when you are ready to try again.",
        Expired =>
            "That Gmail connection attempt expired or was opened in a different browser session. Return here, click Connect Gmail once, and complete Google's screens without refreshing or using an older tab.",
        GoogleUnavailable =>
            "Google could not complete the Gmail connection right now. Your account remains disconnected. Wait a moment, then try again.",
        Configuration =>
            "Google rejected the Gmail connection configuration. An administrator must check the OAuth client, Gmail permission, test-user access, and callback URL before you try again.",
        AccessBlocked =>
            "Google blocked this account from granting Gmail access. If ApplyWise is still in OAuth testing, its administrator must add this exact Google account as a test user. For a school or work Google Workspace account, the Workspace administrator may also need to allow the ApplyWise OAuth client and Gmail read-only access.",
        _ =>
            "Gmail could not be connected. No Gmail access was saved. Start a new request from this page; if it fails again, ask the administrator to review the Google OAuth configuration."
    };
}
