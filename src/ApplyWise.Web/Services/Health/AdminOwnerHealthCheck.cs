using ApplyWise.Web.Data;
using ApplyWise.Web.Services.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Health;

public sealed class AdminOwnerHealthCheck(
    ApplicationDbContext db,
    ILookupNormalizer normalizer,
    IOptions<AdminAccessOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var ownerEmails = options.Value.ValidEmails()
            .Select(normalizer.NormalizeEmail)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ownerEmails.Length == 0)
        {
            return HealthCheckResult.Unhealthy(
                "No valid administrator owner email is configured.");
        }

        var normalizedAdminRole = normalizer.NormalizeName(AdminAccess.Role);
        var ready = await db.Users
            .AsNoTracking()
            .Where(user =>
                user.NormalizedEmail != null
                && ownerEmails.Contains(user.NormalizedEmail)
                && user.EmailConfirmed
                && user.TwoFactorEnabled)
            .AnyAsync(user => db.UserRoles.Any(userRole =>
                    userRole.UserId == user.Id
                    && db.Roles.Any(role =>
                        role.Id == userRole.RoleId
                        && role.NormalizedName == normalizedAdminRole)),
                cancellationToken);

        return ready
            ? HealthCheckResult.Healthy(
                "At least one configured owner is confirmed, MFA-enabled, and assigned the administrator role.")
            : HealthCheckResult.Unhealthy(
                "No configured owner is fully provisioned with confirmed email, MFA, and administrator role.");
    }
}
