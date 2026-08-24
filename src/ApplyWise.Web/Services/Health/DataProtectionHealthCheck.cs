using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ApplyWise.Web.Services.Health;

public sealed record DataProtectionReadinessOptions(string KeysPath);

public sealed class DataProtectionHealthCheck(
    IDataProtectionProvider dataProtectionProvider,
    DataProtectionReadinessOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var probePath = Path.Combine(
            options.KeysPath,
            $".applywise-data-protection-health-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(probePath, "ok", cancellationToken);
            File.Delete(probePath);

            var protector = dataProtectionProvider.CreateProtector(
                "ApplyWise.Health.DataProtection.v1");
            var payload = RandomNumberGenerator.GetBytes(32);
            var protectedPayload = protector.Protect(payload);
            var roundTrip = protector.Unprotect(protectedPayload);
            return CryptographicOperations.FixedTimeEquals(payload, roundTrip)
                ? HealthCheckResult.Healthy(
                    "Data Protection keys are writable and cryptographic round-trip succeeded.")
                : HealthCheckResult.Unhealthy(
                    "Data Protection cryptographic round-trip did not preserve the payload.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Data Protection keys are not ready for durable authentication.",
                exception);
        }
        finally
        {
            try
            {
                if (File.Exists(probePath)) File.Delete(probePath);
            }
            catch
            {
                // Best-effort cleanup must not replace the readiness result.
            }
        }
    }
}
