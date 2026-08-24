using System.Net;
using System.Text;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class HumanChallengeVerifierTests
{
    [Fact]
    public async Task Enabled_verifier_accepts_a_matching_cloudflare_result()
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"success":true,"hostname":"applywise.example","action":"register"}"""));
        var verifier = CreateVerifier(handler);
        var context = CreateFormContext("valid-token");

        var verified = await verifier.VerifyAsync(context, "register", CancellationToken.None);

        Assert.True(verified);
        Assert.True(verifier.Enabled);
        Assert.Equal("public-site-key", verifier.SiteKey);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            "https://challenges.cloudflare.com/turnstile/v0/siteverify",
            handler.RequestUri?.AbsoluteUri);
        Assert.Contains("secret=private-secret", handler.FormBody, StringComparison.Ordinal);
        Assert.Contains("response=valid-token", handler.FormBody, StringComparison.Ordinal);
        Assert.Contains("remoteip=203.0.113.25", handler.FormBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enabled_verifier_rejects_a_missing_token_without_calling_cloudflare()
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"success":true,"hostname":"applywise.example","action":"register"}"""));
        var verifier = CreateVerifier(handler);
        var context = CreateFormContext(token: null);

        var verified = await verifier.VerifyAsync(context, "register", CancellationToken.None);

        Assert.False(verified);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Enabled_verifier_rejects_an_oversized_token_without_calling_cloudflare()
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"success":true,"hostname":"applywise.example","action":"register"}"""));
        var verifier = CreateVerifier(handler);
        var context = CreateFormContext(new string('x', 2_049));

        var verified = await verifier.VerifyAsync(context, "register", CancellationToken.None);

        Assert.False(verified);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("contact", "applywise.example")]
    [InlineData("register", "attacker.example")]
    public async Task Enabled_verifier_rejects_a_wrong_action_or_hostname(
        string responseAction,
        string responseHostname)
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $$"""{"success":true,"hostname":"{{responseHostname}}","action":"{{responseAction}}"}"""));
        var verifier = CreateVerifier(handler);

        var verified = await verifier.VerifyAsync(
            CreateFormContext("valid-token"),
            "register",
            CancellationToken.None);

        Assert.False(verified);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "service unavailable")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    public async Task Enabled_verifier_fails_closed_for_upstream_or_json_errors(
        HttpStatusCode statusCode,
        string responseBody)
    {
        var handler = new StubHandler(_ => JsonResponse(statusCode, responseBody));
        var verifier = CreateVerifier(handler);

        var verified = await verifier.VerifyAsync(
            CreateFormContext("valid-token"),
            "register",
            CancellationToken.None);

        Assert.False(verified);
    }

    [Fact]
    public async Task Enabled_verifier_fails_closed_when_the_upstream_request_throws()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("Upstream unavailable."));
        var verifier = CreateVerifier(handler);

        var verified = await verifier.VerifyAsync(
            CreateFormContext("valid-token"),
            "register",
            CancellationToken.None);

        Assert.False(verified);
    }

    [Fact]
    public async Task Disabled_verifier_bypasses_the_challenge_without_reading_the_form()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Should not be called."));
        var verifier = CreateVerifier(handler, enabled: false);
        var context = new DefaultHttpContext();

        var verified = await verifier.VerifyAsync(context, "register", CancellationToken.None);

        Assert.True(verified);
        Assert.False(verifier.Enabled);
        Assert.Equal("public-site-key", verifier.SiteKey);
        Assert.Equal(0, handler.CallCount);
    }

    private static TurnstileHumanChallengeVerifier CreateVerifier(
        HttpMessageHandler handler,
        bool enabled = true) =>
        new(
            new HttpClient(handler),
            Options.Create(new HumanChallengeOptions
            {
                Enabled = enabled,
                SiteKey = "public-site-key",
                SecretKey = "private-secret",
                ExpectedHostname = "applywise.example"
            }),
            NullLogger<TurnstileHumanChallengeVerifier>.Instance);

    private static DefaultHttpContext CreateFormContext(string? token)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.25");
        context.Request.ContentType = "application/x-www-form-urlencoded";
        var values = new Dictionary<string, StringValues>();
        if (token is not null)
        {
            values["cf-turnstile-response"] = token;
        }

        context.Request.Form = new FormCollection(values);
        return context;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string FormBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            FormBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
