using Microsoft.Data.SqlClient;

namespace ApplyWise.Web.Services.Security;

public static class ProductionSqlConnectionSecurity
{
    public static string Harden(string configuredConnectionString)
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
        return settings.ConnectionString;
    }
}
