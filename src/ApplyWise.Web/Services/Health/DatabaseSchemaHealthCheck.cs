using ApplyWise.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ApplyWise.Web.Services.Health;

public sealed class DatabaseSchemaHealthCheck(ApplicationDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pendingMigrations = (await db.Database
                    .GetPendingMigrationsAsync(cancellationToken))
                .Take(5)
                .ToArray();

            return pendingMigrations.Length == 0
                ? HealthCheckResult.Healthy("Database schema is current.")
                : HealthCheckResult.Unhealthy(
                    $"Database has pending migrations: {string.Join(", ", pendingMigrations)}");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Database schema readiness check failed.",
                exception);
        }
    }
}
