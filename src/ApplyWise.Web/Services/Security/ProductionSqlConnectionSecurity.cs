using Microsoft.Data.SqlClient;

namespace ApplyWise.Web.Services.Security;

public sealed class ProductionSqlConnectionSecurityOptions
{
    public const string SectionName = "SqlTransport";

    public bool AllowMonsterAspManagedCertificate { get; set; }

    public string MonsterAspHost { get; set; } = string.Empty;
}

public static class ProductionSqlConnectionSecurity
{
    public static string Harden(
        string configuredConnectionString,
        ProductionSqlConnectionSecurityOptions? options = null)
    {
        SqlConnectionStringBuilder settings;
        try
        {
            settings = new SqlConnectionStringBuilder(configuredConnectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "The production SQL connection string is invalid.",
                exception);
        }

        if (string.Equals(settings.UserID, "sa", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The production SQL connection must not use the sa login.");
        }

        settings.Encrypt = SqlConnectionEncryptOption.Mandatory;
        settings.TrustServerCertificate = false;

        if (options?.AllowMonsterAspManagedCertificate == true)
        {
            var allowedHost = options.MonsterAspHost.Trim();
            if (Uri.CheckHostName(allowedHost) != UriHostNameType.Dns
                || !allowedHost.EndsWith(".databaseasp.net", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "SqlTransport:MonsterAspHost must be an exact MonsterASP database hostname.");
            }

            if (!string.Equals(
                    settings.DataSource.Trim(),
                    allowedHost,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The production SQL server does not match SqlTransport:MonsterAspHost.");
            }

            // MonsterASP encrypts SQL traffic with a provider-managed certificate that
            // does not chain to a public root. This opt-in remains scoped to one exact
            // Monster hostname; all other production SQL connections validate the chain.
            settings.TrustServerCertificate = true;
        }

        return settings.ConnectionString;
    }
}
