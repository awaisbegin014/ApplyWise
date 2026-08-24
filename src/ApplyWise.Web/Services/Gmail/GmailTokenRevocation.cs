using ApplyWise.Web.Models;
using System.Net;
using System.Text.Json;

namespace ApplyWise.Web.Services.Gmail;

public static class GmailTokenRevocation
{
    public static async Task<bool> TryRevokeStoredTokenAsync(
        GmailConnection connection,
        IGmailCredentialProtector credentialProtector,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = credentialProtector.Unprotect(connection.ProtectedRefreshToken);
            return await TryRevokeTokenAsync(
                token,
                connection.Id,
                httpClientFactory,
                logger,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "The stored Google token could not be read for Gmail connection {ConnectionId}.",
                connection.Id);
            return false;
        }
    }

    public static async Task<bool> TryRevokeTokenAsync(
        string token,
        int? connectionId,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var client = httpClientFactory.CreateClient("GoogleOAuth");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://oauth2.googleapis.com/revoke")
            {
                Content = new FormUrlEncodedContent(
                    new Dictionary<string, string> { ["token"] = token })
            };
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.IsSuccessStatusCode) return true;

            // Google documents invalid_token as meaning the credential is
            // already expired or revoked. That is the desired end state, so
            // retries must treat it as an idempotent success.
            if (response.StatusCode == HttpStatusCode.BadRequest
                && await IsAlreadyInvalidAsync(response, cancellationToken))
            {
                return true;
            }

            logger.LogWarning(
                "Google token revocation returned {StatusCode} for Gmail connection {ConnectionId}.",
                (int)response.StatusCode,
                connectionId);
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Google token revocation could not be completed for Gmail connection {ConnectionId}.",
                connectionId);
            return false;
        }
    }

    private static async Task<bool> IsAlreadyInvalidAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 4_096)
        {
            return false;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (json.Length > 4_096) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                && error.GetString() == "invalid_token";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
