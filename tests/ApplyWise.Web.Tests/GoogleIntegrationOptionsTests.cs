using ApplyWise.Web.Services.Gmail;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class GoogleIntegrationOptionsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("your-client-id")]
    [InlineData("__SET_GOOGLE_CLIENT_ID__")]
    [InlineData(".apps.googleusercontent.com")]
    public void Placeholder_or_malformed_client_ids_do_not_enable_google(string clientId)
    {
        var options = new GoogleIntegrationOptions
        {
            ClientId = clientId,
            ClientSecret = "GOCSPX-test-secret"
        };

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void Google_web_client_credentials_enable_the_integration()
    {
        var options = new GoogleIntegrationOptions
        {
            ClientId = "123456789-example.apps.googleusercontent.com",
            ClientSecret = "GOCSPX-test-secret"
        };

        Assert.True(options.IsConfigured);
        Assert.False(options.IsGmailImportConfigured);
        options.GmailImportEnabled = true;
        Assert.True(options.IsGmailImportConfigured);
    }
}
