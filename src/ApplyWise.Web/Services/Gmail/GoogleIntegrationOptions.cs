namespace ApplyWise.Web.Services.Gmail;

public sealed class GoogleIntegrationOptions
{
    public const string SectionName = "Google";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public bool GmailImportEnabled { get; set; }
    public bool GmailAutoSyncEnabled { get; set; } = true;
    public int GmailSyncIntervalMinutes { get; set; } = 5;
    public int GmailInitialLookbackDays { get; set; } = 30;
    public int GmailMaxMessagesPerSync { get; set; } = 100;
    public int GmailSyncTimeoutSeconds { get; set; } = 120;
    public int GmailMaxResponseBytes { get; set; } = 2 * 1024 * 1024;

    public bool IsConfigured =>
        HasValidClientIdFormat
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !ClientSecret.Contains("__SET_", StringComparison.Ordinal);

    public bool IsGmailImportConfigured => IsConfigured && GmailImportEnabled;

    public bool HasValidClientIdFormat =>
        !string.IsNullOrWhiteSpace(ClientId)
        && ClientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase)
        && ClientId.Length > ".apps.googleusercontent.com".Length;
}
