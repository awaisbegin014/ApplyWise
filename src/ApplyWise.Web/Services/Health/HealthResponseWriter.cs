using System.Reflection;
using System.Text.Json;
using ApplyWise.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ApplyWise.Web.Services.Health;

public static class HealthResponseWriter
{
    public const string SecurityBaseline = "2026-08-release-hardening-v2";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(
            new
            {
                status = report.Status.ToString(),
                durationMilliseconds = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
                checks = report.Entries.ToDictionary(
                    entry => entry.Key,
                    entry => new
                    {
                        status = entry.Value.Status.ToString(),
                        description = entry.Value.Description
                    })
            },
            JsonOptions));
    }

    public static object Release(
        IHostEnvironment environment,
        ApplicationDbContext db)
    {
        var assembly = typeof(Program).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        var separator = informationalVersion.LastIndexOf('+');
        var commit = separator >= 0 && separator < informationalVersion.Length - 1
            ? informationalVersion[(separator + 1)..]
            : "local";

        return new
        {
            status = "Healthy",
            commit,
            version = informationalVersion,
            environment = environment.EnvironmentName,
            securityBaseline = SecurityBaseline,
            schemaMigration = db.Database.GetMigrations().LastOrDefault() ?? "none"
        };
    }
}
