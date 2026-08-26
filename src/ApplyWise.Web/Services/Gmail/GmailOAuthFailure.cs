namespace ApplyWise.Web.Services.Gmail;

public static class GmailOAuthFailure
{
    public const string QueryParameter = "reason";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
    public const string GoogleUnavailable = "google_unavailable";
    public const string Configuration = "configuration";
    public const string Failed = "failed";

    public static string Classify(string? providerError, Exception? failure)
    {
        var normalizedError = providerError?.Trim().ToLowerInvariant();
        if (normalizedError == "access_denied") return Cancelled;

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
        _ =>
            "Gmail could not be connected. No Gmail access was saved. Start a new request from this page; if it fails again, ask the administrator to review the Google OAuth configuration."
    };
}
