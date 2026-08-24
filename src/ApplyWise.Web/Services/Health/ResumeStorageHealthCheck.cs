using ApplyWise.Web.Services.ResumeStorage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Health;

public sealed class ResumeStorageHealthCheck(
    IWebHostEnvironment environment,
    IOptions<ResumeStorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var configuredRoot = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return HealthCheckResult.Unhealthy("Resume storage is not configured.");
        }

        var storageRoot = Path.GetFullPath(Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot));

        if (!Directory.Exists(storageRoot))
        {
            return HealthCheckResult.Unhealthy("Resume storage directory does not exist.");
        }

        var probePath = Path.Combine(storageRoot, $".applywise-health-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(probePath, "ok", cancellationToken);
            File.Delete(probePath);
            return HealthCheckResult.Healthy("Resume storage is writable.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return HealthCheckResult.Unhealthy(
                "Resume storage is not writable.",
                exception);
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch
            {
                // A failed best-effort cleanup must not replace the original health result.
            }
        }
    }
}
