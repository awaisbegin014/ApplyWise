using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using ApplyWise.Web.Data;
using ApplyWise.Web.Services.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class OwnerProvisioningCommandTests
{
    [Fact]
    public async Task Provisioning_creates_a_confirmed_mfa_owner_and_is_idempotent()
    {
        const string email = "owner@example.test";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("owner-provisioning-" + Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<IdentityUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.Configure<AdminAccessOptions>(options =>
        {
            options.Emails = [email];
            options.RequireMfa = true;
        });
        services.AddScoped<IAdminRoleAssignmentService, AdminRoleAssignmentService>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var output = new StringWriter();
        string ReadSecret(string prompt)
        {
            if (prompt.StartsWith("Initial", StringComparison.Ordinal))
            {
                return "StrongOwnerPassword123!";
            }

            var keyLine = output.ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.StartsWith("Authenticator key: ", StringComparison.Ordinal));
            return CurrentTotp(keyLine["Authenticator key: ".Length..]);
        }

        var exitCode = await OwnerProvisioningCommand.RunAsync(
            scope.ServiceProvider,
            new TestWebHostEnvironment(),
            [OwnerProvisioningCommand.Command, $"--email={email}"],
            ReadSecret,
            output);

        var owner = Assert.IsType<IdentityUser>(await users.FindByEmailAsync(email));
        Assert.Equal(0, exitCode);
        Assert.True(owner.EmailConfirmed);
        Assert.True(await users.GetTwoFactorEnabledAsync(owner));
        Assert.True(await users.IsInRoleAsync(owner, AdminAccess.Role));
        Assert.Contains("recovery codes", output.ToString(), StringComparison.OrdinalIgnoreCase);

        var secondExitCode = await OwnerProvisioningCommand.RunAsync(
            scope.ServiceProvider,
            new TestWebHostEnvironment(),
            [OwnerProvisioningCommand.Command, $"--email={email}"],
            _ => throw new InvalidOperationException("Idempotent provisioning must not request new secrets."),
            TextWriter.Null);
        Assert.Equal(0, secondExitCode);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ApplyWise.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string CurrentTotp(string base32Key)
    {
        var key = DecodeBase32(base32Key);
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(
            counter,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter.ToArray());
        var offset = hash[^1] & 0x0f;
        var binaryCode = ((hash[offset] & 0x7f) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        return (binaryCode % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in value.ToUpperInvariant())
        {
            if (char.IsWhiteSpace(character) || character == '=') continue;
            var digit = alphabet.IndexOf(character);
            if (digit < 0) throw new InvalidOperationException("Invalid base32 authenticator key.");
            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            bytes.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }

        return bytes.ToArray();
    }
}
