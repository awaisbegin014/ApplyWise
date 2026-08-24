namespace ApplyWise.Web.Services.Gmail;

public static class GmailAuthenticationDefaults
{
    public const string Scheme = "GoogleGmail";
    public const string DisplayName = "Connect Gmail";
    public const string FlowClaimType = "applywise:google_flow";
    public const string FlowClaimValue = "gmail_connection";
    public const string FlowIdProperty = "ApplyWise.Gmail.FlowId";
}

public static class GmailConnectionStates
{
    public const string RevocationPending = "revocation_pending";
}
