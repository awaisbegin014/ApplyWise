using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.AccountSecurity;

public interface ILoginTimingProtector
{
    void VerifyDummyPassword(string suppliedPassword);

    Task EnforceMinimumResponseTimeAsync(
        long startedAt,
        CancellationToken cancellationToken = default);
}

public sealed class LoginTimingProtector : ILoginTimingProtector
{
    private static readonly TimeSpan MinimumResponseTime = TimeSpan.FromMilliseconds(750);
    private readonly IdentityUser _dummyUser = new()
    {
        Id = "login-timing-protection",
        UserName = "login-timing-protection"
    };
    private readonly PasswordHasher<IdentityUser> _passwordHasher;
    private readonly string _dummyPasswordHash;

    public LoginTimingProtector(IOptions<PasswordHasherOptions> options)
    {
        _passwordHasher = new PasswordHasher<IdentityUser>(options);
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _dummyPasswordHash = _passwordHasher.HashPassword(_dummyUser, secret);
    }

    public void VerifyDummyPassword(string suppliedPassword) =>
        _ = _passwordHasher.VerifyHashedPassword(
            _dummyUser,
            _dummyPasswordHash,
            suppliedPassword);

    public async Task EnforceMinimumResponseTimeAsync(
        long startedAt,
        CancellationToken cancellationToken = default)
    {
        var remaining = MinimumResponseTime - Stopwatch.GetElapsedTime(startedAt);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }
    }
}
