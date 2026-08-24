using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Services.Admin;
using ApplyWise.Web.Services.Health;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ReleaseHealthCheckTests
{
    [Fact]
    public async Task Data_protection_readiness_probes_writes_and_crypto_round_trip()
    {
        var keysPath = Path.Combine(
            Path.GetTempPath(),
            "applywise-data-protection-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keysPath);
        try
        {
            var provider = DataProtectionProvider.Create(new DirectoryInfo(keysPath));
            var check = new DataProtectionHealthCheck(
                provider,
                new DataProtectionReadinessOptions(keysPath));

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Empty(Directory.EnumerateFiles(keysPath, ".applywise-data-protection-health-*.tmp"));
        }
        finally
        {
            Directory.Delete(keysPath, recursive: true);
        }
    }

    [Fact]
    public async Task Owner_readiness_requires_confirmation_mfa_and_admin_role()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("owner-health-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        var normalizer = new UpperInvariantLookupNormalizer();
        var check = new AdminOwnerHealthCheck(
            db,
            normalizer,
            Options.Create(new AdminAccessOptions
            {
                RequireMfa = true,
                Emails = ["owner@example.test"]
            }));

        Assert.Equal(
            HealthStatus.Unhealthy,
            (await check.CheckHealthAsync(new HealthCheckContext())).Status);

        var owner = new IdentityUser
        {
            Id = "owner-id",
            Email = "owner@example.test",
            NormalizedEmail = normalizer.NormalizeEmail("owner@example.test"),
            UserName = "owner@example.test",
            NormalizedUserName = normalizer.NormalizeName("owner@example.test"),
            EmailConfirmed = true,
            TwoFactorEnabled = true
        };
        var role = new IdentityRole
        {
            Id = "admin-role",
            Name = AdminAccess.Role,
            NormalizedName = normalizer.NormalizeName(AdminAccess.Role)
        };
        db.Users.Add(owner);
        db.Roles.Add(role);
        db.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = owner.Id,
            RoleId = role.Id
        });
        await db.SaveChangesAsync();

        Assert.Equal(
            HealthStatus.Healthy,
            (await check.CheckHealthAsync(new HealthCheckContext())).Status);
    }

    [Fact]
    public void Release_identity_attests_environment_baseline_and_terminal_migration()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=release-health.example.test;Database=ApplyWise;User ID=test;Password=test")
            .Options;
        using var db = new ApplicationDbContext(dbOptions);
        var release = HealthResponseWriter.Release(
            new StubHostEnvironment { EnvironmentName = Environments.Production },
            db);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(release));

        Assert.Equal("Production", json.RootElement.GetProperty("environment").GetString());
        Assert.Equal(
            HealthResponseWriter.SecurityBaseline,
            json.RootElement.GetProperty("securityBaseline").GetString());
        Assert.Matches(
            "^[0-9]{14}_[A-Za-z0-9_]+$",
            json.RootElement.GetProperty("schemaMigration").GetString());
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ApplyWise.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
