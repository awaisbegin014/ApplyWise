using System.Net;
using System.Text;
using ApplyWise.Web.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class GeminiResumeCoachTests
{
    [Fact]
    public async Task Coach_uses_server_header_and_parses_structured_rewrites()
    {
        var handler = new RecordingHandler("""
            {
              "candidates": [{
                "content": { "parts": [{ "text": "{\"headline\":\"Clearer impact\",\"assessment\":\"The original is vague.\",\"missingContextQuestion\":\"What outcome did you verify?\",\"rewrites\":[{\"text\":\"Built customer-facing web features with the development team.\",\"rationale\":\"Starts with a direct action.\"},{\"text\":\"Collaborated with developers to deliver features for the company website.\",\"rationale\":\"Preserves the stated teamwork.\"}],\"truthfulnessNote\":\"No new metric was added.\"}" }] }
              }]
            }
            """);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        var coach = new GeminiResumeCoach(
            client,
            Options.Create(new GeminiOptions
            {
                Enabled = true,
                ApiKey = "server-secret",
                Model = "test-model"
            }),
            NullLogger<GeminiResumeCoach>.Instance);

        var result = await coach.ImproveBulletAsync(
            "Helped the development team build features for the company website.",
            "Build accessible web experiences.");

        Assert.Equal("Clearer impact", result.Headline);
        Assert.Equal(2, result.Rewrites.Count);
        Assert.Equal("server-secret", handler.ApiKey);
        Assert.DoesNotContain("server-secret", handler.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains("responseMimeType", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("application/json", handler.RequestBody, StringComparison.Ordinal);
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string? ApiKey { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.TryGetValues("x-goog-api-key", out var values)
                ? values.Single()
                : null;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
