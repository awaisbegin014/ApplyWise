using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Security;

public sealed class TurnstileHumanChallengeVerifier : IHumanChallengeVerifier
{
    private const string ResponseFieldName = "cf-turnstile-response";
    private const int MaximumTokenLength = 2_048;
    private const int MaximumResponseBytes = 16 * 1_024;
    private static readonly Uri VerificationEndpoint =
        new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    private readonly HttpClient _httpClient;
    private readonly HumanChallengeOptions _options;
    private readonly ILogger<TurnstileHumanChallengeVerifier> _logger;

    public TurnstileHumanChallengeVerifier(
        HttpClient httpClient,
        IOptions<HumanChallengeOptions> options,
        ILogger<TurnstileHumanChallengeVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled;

    public string SiteKey => _options.SiteKey ?? string.Empty;

    public async Task<bool> VerifyAsync(
        HttpContext httpContext,
        string expectedAction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!Enabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(_options.SecretKey)
            || string.IsNullOrWhiteSpace(_options.ExpectedHostname)
            || string.IsNullOrWhiteSpace(expectedAction))
        {
            _logger.LogWarning("Human challenge verification is enabled but is not fully configured.");
            return false;
        }

        try
        {
            var token = await ReadTokenAsync(httpContext.Request, cancellationToken);
            if (token is null)
            {
                return false;
            }

            var formValues = new Dictionary<string, string>
            {
                ["secret"] = _options.SecretKey,
                ["response"] = token
            };
            if (httpContext.Connection.RemoteIpAddress is { } remoteIpAddress)
            {
                formValues["remoteip"] = remoteIpAddress.ToString();
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, VerificationEndpoint)
            {
                Content = new FormUrlEncodedContent(formValues)
            };
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Human challenge verification returned HTTP status {StatusCode}.",
                    (int)response.StatusCode);
                return false;
            }

            var result = await ReadResponseAsync(response.Content, cancellationToken);
            return result is
            {
                Success: true,
                Action: not null,
                Hostname: not null
            }
                && string.Equals(result.Action, expectedAction, StringComparison.Ordinal)
                && string.Equals(
                    result.Hostname,
                    _options.ExpectedHostname,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Human challenge verification could not be completed ({ErrorType}).",
                exception.GetType().Name);
            return false;
        }
    }

    private static async Task<string?> ReadTokenAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return null;
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var submittedValues = form[ResponseFieldName];
        if (submittedValues.Count != 1)
        {
            return null;
        }

        var token = submittedValues[0];
        return string.IsNullOrWhiteSpace(token) || token.Length > MaximumTokenLength
            ? null
            : token;
    }

    private static async Task<VerificationResponse?> ReadResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaximumResponseBytes + 1];
        var bytesRead = 0;
        while (bytesRead < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(bytesRead, buffer.Length - bytesRead),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            bytesRead += read;
        }

        if (bytesRead > MaximumResponseBytes)
        {
            return null;
        }

        return JsonSerializer.Deserialize<VerificationResponse>(
            buffer.AsSpan(0, bytesRead));
    }

    private sealed class VerificationResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("hostname")]
        public string? Hostname { get; init; }

        [JsonPropertyName("action")]
        public string? Action { get; init; }
    }
}
