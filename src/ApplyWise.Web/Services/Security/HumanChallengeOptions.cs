namespace ApplyWise.Web.Services.Security;

public sealed class HumanChallengeOptions
{
    public const string SectionName = "HumanChallenge";

    public bool Enabled { get; set; }

    public string SiteKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string ExpectedHostname { get; set; } = string.Empty;
}
