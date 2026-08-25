using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ApplyWise.Web.Services.Email;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ProductionReadinessConfigurationTests
{
    [Fact]
    public void Private_persistent_paths_must_be_outside_the_deployment_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "applywise-deploy-" + Guid.NewGuid().ToString("N"));
        var privateRoot = Path.Combine(Path.GetDirectoryName(root)!, "applywise-private-" + Guid.NewGuid().ToString("N"));

        ProductionPrivatePathSecurity.EnsureOutsideDeploymentRoot(
            root,
            Path.Combine(root, "wwwroot"),
            Path.Combine(privateRoot, "resumes"),
            Path.Combine(privateRoot, "keys"),
            Path.Combine(privateRoot, "certificate.pfx"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProductionPrivatePathSecurity.EnsureOutsideDeploymentRoot(
                root,
                Path.Combine(root, "wwwroot"),
                Path.Combine(root, "App_Data", "resumes"),
                Path.Combine(privateRoot, "keys"),
                Path.Combine(privateRoot, "certificate.pfx")));

        Assert.Contains("ResumeStorage:RootPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Web_root_cannot_hold_data_protection_material()
    {
        var root = Path.Combine(Path.GetTempPath(), "applywise-deploy-" + Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(root, "wwwroot");
        var privateRoot = Path.Combine(Path.GetDirectoryName(root)!, "applywise-private-" + Guid.NewGuid().ToString("N"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProductionPrivatePathSecurity.EnsureOutsideDeploymentRoot(
                root,
                webRoot,
                Path.Combine(privateRoot, "resumes"),
                Path.Combine(privateRoot, "keys"),
                Path.Combine(webRoot, "certificate.pfx")));

        Assert.Contains("DataProtection:CertificatePath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_email_requires_tls_valid_sender_and_complete_credentials()
    {
        var valid = new EmailOptions
        {
            Host = "smtp.example.test",
            Port = 587,
            From = "support@example.test",
            UserName = "smtp-user",
            Password = "test-secret",
            UseSsl = true
        };

        Assert.True(ProductionEmailConfiguration.IsReady(valid));
        Assert.False(ProductionEmailConfiguration.IsReady(Copy(valid, useSsl: false)));
        Assert.False(ProductionEmailConfiguration.IsReady(Copy(valid, from: "not-an-address")));
        Assert.False(ProductionEmailConfiguration.IsReady(Copy(valid, password: string.Empty)));
        Assert.False(ProductionEmailConfiguration.IsReady(Copy(valid, host: "https://smtp.example.test")));
    }

    [Fact]
    public void Data_protection_certificate_requires_a_current_rsa_private_key()
    {
        var now = DateTimeOffset.UtcNow;
        using var current = CreateCertificate(now.AddDays(-1), now.AddDays(30));
        DataProtectionCertificateSecurity.EnsureProductionReady(current, now);

        using var publicOnly = X509CertificateLoader.LoadCertificate(
            current.Export(X509ContentType.Cert));
        Assert.Throws<InvalidOperationException>(() =>
            DataProtectionCertificateSecurity.EnsureProductionReady(publicOnly, now));

        using var expired = CreateCertificate(now.AddDays(-30), now.AddDays(-1));
        Assert.Throws<InvalidOperationException>(() =>
            DataProtectionCertificateSecurity.EnsureProductionReady(expired, now));
    }

    [Fact]
    public void Rotated_data_protection_certificate_can_unprotect_existing_payloads()
    {
        var now = DateTimeOffset.UtcNow;
        using var previousCertificate = CreateCertificate(now.AddDays(-30), now.AddDays(30));
        using var currentCertificate = CreateCertificate(now.AddDays(-1), now.AddDays(365));
        var keyDirectory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "applywise-key-rotation-" + Guid.NewGuid().ToString("N")));

        try
        {
            using var previousServices = new ServiceCollection()
                .AddDataProtection()
                .SetApplicationName("ApplyWise.CertificateRotation.Test")
                .PersistKeysToFileSystem(keyDirectory)
                .ProtectKeysWithCertificate(previousCertificate)
                .Services
                .BuildServiceProvider();
            var protectedValue = previousServices
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("rotation")
                .Protect("durable-secret");

            using var currentServices = new ServiceCollection()
                .AddDataProtection()
                .SetApplicationName("ApplyWise.CertificateRotation.Test")
                .PersistKeysToFileSystem(keyDirectory)
                .ProtectKeysWithCertificate(currentCertificate)
                .UnprotectKeysWithAnyCertificate(
                    currentCertificate,
                    previousCertificate)
                .Services
                .BuildServiceProvider();
            var unprotectedValue = currentServices
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("rotation")
                .Unprotect(protectedValue);

            Assert.Equal("durable-secret", unprotectedValue);
        }
        finally
        {
            Directory.Delete(keyDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public void Production_deployment_injects_Google_sign_in_secrets_into_release_and_rollback_only_at_runtime()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            repositoryRoot,
            ".github",
            "workflows",
            "deploy-monster.yml"));

        Assert.Contains("secrets.APPLYWISE_GOOGLE_CLIENT_ID", workflow, StringComparison.Ordinal);
        Assert.Contains("secrets.APPLYWISE_GOOGLE_CLIENT_SECRET", workflow, StringComparison.Ordinal);
        Assert.Contains("Set-GoogleRuntimeConfiguration $env:RELEASE_PUBLISH_PATH", workflow, StringComparison.Ordinal);
        Assert.Contains("Set-GoogleRuntimeConfiguration $env:ROLLBACK_PUBLISH_PATH", workflow, StringComparison.Ordinal);
        Assert.Contains("Google__ClientId", workflow, StringComparison.Ordinal);
        Assert.Contains("Google__ClientSecret", workflow, StringComparison.Ordinal);
        Assert.Contains("\"Google__GmailImportEnabled\" = \"false\"", workflow, StringComparison.Ordinal);
    }

    private static EmailOptions Copy(
        EmailOptions source,
        string? host = null,
        string? from = null,
        string? password = null,
        bool? useSsl = null) =>
        new()
        {
            Host = host ?? source.Host,
            Port = source.Port,
            From = from ?? source.From,
            UserName = source.UserName,
            Password = password ?? source.Password,
            UseSsl = useSsl ?? source.UseSsl
        };

    private static X509Certificate2 CreateCertificate(
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=ApplyWise Data Protection Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ApplyWise.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate ApplyWise.sln above '{AppContext.BaseDirectory}'.");
    }
}
