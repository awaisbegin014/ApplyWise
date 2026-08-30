using ApplyWise.Web.Services.Gmail;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class GmailOAuthFailureTests
{
    [Theory]
    [InlineData("access_denied", GmailOAuthFailure.Cancelled)]
    [InlineData("server_error", GmailOAuthFailure.GoogleUnavailable)]
    [InlineData("temporarily_unavailable", GmailOAuthFailure.GoogleUnavailable)]
    [InlineData("invalid_client", GmailOAuthFailure.Configuration)]
    [InlineData("unauthorized_client", GmailOAuthFailure.Configuration)]
    [InlineData("invalid_request", GmailOAuthFailure.Configuration)]
    [InlineData("invalid_scope", GmailOAuthFailure.Configuration)]
    [InlineData("redirect_uri_mismatch", GmailOAuthFailure.Configuration)]
    [InlineData("interaction_required", GmailOAuthFailure.Expired)]
    [InlineData("login_required", GmailOAuthFailure.Expired)]
    [InlineData("consent_required", GmailOAuthFailure.Expired)]
    [InlineData("admin_policy_enforced", GmailOAuthFailure.AccessBlocked)]
    [InlineData("org_internal", GmailOAuthFailure.AccessBlocked)]
    [InlineData("unknown_provider_error", GmailOAuthFailure.Failed)]
    public void Provider_errors_are_mapped_to_safe_failure_categories(
        string providerError,
        string expected)
    {
        var result = GmailOAuthFailure.Classify(
            providerError,
            new InvalidOperationException("provider detail must not be reflected"));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("This app is currently being tested and only approved test users can access it.")]
    [InlineData("Access blocked: your organization's administrator needs to review this app.")]
    public void Access_denied_policy_failures_are_not_reported_as_user_cancellation(
        string providerDescription)
    {
        var result = GmailOAuthFailure.Classify(
            "access_denied",
            new InvalidOperationException("The remote server returned access_denied."),
            providerDescription);

        Assert.Equal(GmailOAuthFailure.AccessBlocked, result);
    }

    [Fact]
    public void Plain_access_denied_is_still_reported_as_user_cancellation()
    {
        Assert.Equal(
            GmailOAuthFailure.Cancelled,
            GmailOAuthFailure.Classify(
                "access_denied",
                new InvalidOperationException("The user denied access.")));
    }

    [Theory]
    [InlineData("Correlation failed.")]
    [InlineData("The oauth state was missing or invalid.")]
    [InlineData("The remote authentication ticket expired.")]
    public void Browser_state_failures_are_reported_as_expired(string message)
    {
        Assert.Equal(
            GmailOAuthFailure.Expired,
            GmailOAuthFailure.Classify(null, new InvalidOperationException(message)));
    }

    [Fact]
    public void User_messages_never_reflect_unknown_query_input()
    {
        const string attackerControlledReason = "<script>alert(1)</script>";

        var message = GmailOAuthFailure.GetUserMessage(attackerControlledReason);

        Assert.DoesNotContain(attackerControlledReason, message, StringComparison.Ordinal);
        Assert.Contains("No Gmail access was saved", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GmailOAuthFailure.Cancelled, "cancelled")]
    [InlineData(GmailOAuthFailure.Expired, "expired")]
    [InlineData(GmailOAuthFailure.GoogleUnavailable, "right now")]
    [InlineData(GmailOAuthFailure.Configuration, "administrator")]
    [InlineData(GmailOAuthFailure.AccessBlocked, "test user")]
    [InlineData(GmailOAuthFailure.AccessBlocked, "Workspace administrator")]
    public void Each_known_failure_has_specific_recovery_guidance(
        string reason,
        string expectedText)
    {
        Assert.Contains(
            expectedText,
            GmailOAuthFailure.GetUserMessage(reason),
            StringComparison.OrdinalIgnoreCase);
    }
}
