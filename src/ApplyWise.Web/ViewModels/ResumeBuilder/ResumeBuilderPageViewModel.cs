using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApplyWise.Web.Models;
using ApplyWise.Web.Models.ResumeBuilder;
using ApplyWise.Web.Services.Subscriptions;

namespace ApplyWise.Web.ViewModels.ResumeBuilder;

public sealed class ResumeBuilderPageViewModel
{
    public const string DraftStorageKeyPrefix = "applywise:resume-builder:draft:v1:";

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public required string DraftStorageKey { get; init; }
    public required string SampleResumeJson { get; init; }
    public string? InitialSection { get; init; }
    public required SubscriptionSnapshot Subscription { get; init; }

    public static ResumeBuilderPageViewModel CreateForAccount(
        string accountId,
        string? initialSection = null) =>
        CreateForAccount(accountId, FreePreviewSubscription(), initialSection);

    public static ResumeBuilderPageViewModel CreateForAccount(
        string accountId,
        SubscriptionSnapshot subscription,
        string? initialSection = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        return new ResumeBuilderPageViewModel
        {
            DraftStorageKey = CreateDraftStorageKey(accountId),
            SampleResumeJson = JsonSerializer.Serialize(ResumeSampleFactory.Create(), SerializerOptions),
            InitialSection = NormalizeInitialSection(initialSection),
            Subscription = subscription
        };
    }

    public static string? NormalizeInitialSection(string? section)
    {
        if (string.IsNullOrWhiteSpace(section)) return null;

        var candidate = section.Trim();
        if (candidate.Length > 64) return null;

        return candidate.ToLowerInvariant() switch
        {
            "personalinformation" => "personalInformation",
            "professionalsummary" => "professionalSummary",
            "experience" => "experience",
            "projects" => "projects",
            "education" => "education",
            "skills" => "skills",
            "references" => "references",
            "achievements" or "certifications" or "achievementsandcertifications"
                => "achievementsAndCertifications",
            _ => null
        };
    }

    public static string CreateDraftStorageKey(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        // Identity IDs must not be exposed in DOM attributes or localStorage keys.
        // A full SHA-256 digest keeps keys deterministic and account-specific while
        // restricting them to a conservative ASCII character set.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(accountId));
        return DraftStorageKeyPrefix + Convert.ToHexStringLower(digest);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static SubscriptionSnapshot FreePreviewSubscription() => new(
        SubscriptionTier.Free,
        IsPro: false,
        ProExpiresAt: null,
        AtsAnalysisLimit: 2,
        AtsAnalysisUsed: 0,
        AtsAnalysisReserved: 0,
        ResumeBuildLimit: 2,
        ResumeBuildUsed: 0,
        UsageWindowStartsAt: null);
}
