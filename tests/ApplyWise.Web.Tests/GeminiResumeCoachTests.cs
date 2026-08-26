using System.Net;
using System.Text;
using ApplyWise.Web.Services.Ai;
using ApplyWise.Web.Services.ResumeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class GeminiResumeCoachTests
{
    [Fact]
    public async Task Advisor_preserves_score_redacts_contact_details_and_parses_structured_feedback()
    {
        var handler = new RecordingHandler("""
            {
              "candidates": [{
                "content": { "parts": [{ "text": "{\"overview\":\"The deterministic result shows a sound base.\",\"strengths\":[\"Clear healthcare role\",\"Relevant laboratory evidence\"],\"priorityImprovements\":[{\"title\":\"Clarify outcomes\",\"whyItMatters\":\"Evidence is easier to verify.\",\"recommendedAction\":\"Add a truthful outcome where one is known.\",\"resumeSection\":\"Experience\"},{\"title\":\"Name certification status\",\"whyItMatters\":\"The role asks for it.\",\"recommendedAction\":\"State the credential only if currently held.\",\"resumeSection\":\"Certifications\"}],\"missingEvidence\":[\"No current certification is demonstrated\"],\"jobFitSummary\":\"The resume demonstrates laboratory experience.\",\"truthfulnessNote\":\"Do not add credentials that are not held.\"}" }] }
              }]
            }
            """);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        var advisor = new GeminiAtsAdvisor(
            client,
            Options.Create(new GeminiOptions
            {
                Enabled = true,
                ApiKey = "server-secret",
                Model = "test-model"
            }),
            NullLogger<GeminiAtsAdvisor>.Instance);

        var analysis = CreateAnalysis();
        var result = await advisor.CreateFeedbackAsync(new ResumeAnalysisAiContext(
            "Taylor Example | taylor@example.com | +92 300 1234567 | https://example.com\nMedical Laboratory Technician with five years of specimen processing experience.",
            "Medical laboratory technician. Certification required. Process specimens safely.",
            analysis));

        Assert.Equal(2, result.PriorityImprovements.Count);
        Assert.Equal(76, analysis.OverallScore);
        Assert.Equal("server-secret", handler.ApiKey);
        Assert.DoesNotContain("server-secret", handler.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains("responseMimeType", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("application/json", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("Overall score: 76/100", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("[email redacted]", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("[phone redacted]", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("[link redacted]", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("taylor@example.com", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("including healthcare", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
    }

    private static ResumeAnalysisResult CreateAnalysis() => new()
    {
        OverallScore = 76,
        AtsReadinessScore = 82,
        JobMatchScore = 70,
        ConfidenceScore = 88,
        ScoreBreakdown =
        [
            new ScoreComponent("ats", "ATS readiness", 41, 50, ["Core sections were detected."])
        ],
        MatchedRequirements = [],
        ReviewItems = [],
        SectionReviews = [],
        BulletReviews = [],
        Evidence = [],
        MissingRequirements = [],
        Suggestions = [],
        Warnings = [],
        DetectedJobRequirementCount = 1,
        MustHaveCoverage = 0,
        RequiredCoverage = 0,
        EvidenceQuality = 0.7
    };

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
