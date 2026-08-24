using System.Net.Mail;

namespace ApplyWise.Web.Services.Email;

public static class ProductionEmailConfiguration
{
    public static bool IsReady(EmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var host = options.Host?.Trim() ?? string.Empty;
        var from = options.From?.Trim() ?? string.Empty;
        var usernameConfigured = !string.IsNullOrWhiteSpace(options.UserName);
        var passwordConfigured = !string.IsNullOrWhiteSpace(options.Password);

        return host.Length is > 0 and <= 253
            && Uri.CheckHostName(host) != UriHostNameType.Unknown
            && MailAddress.TryCreate(from, out var fromAddress)
            && string.Equals(fromAddress.Address, from, StringComparison.OrdinalIgnoreCase)
            && options.Port is > 0 and <= 65535
            && options.UseSsl
            && usernameConfigured == passwordConfigured;
    }
}
