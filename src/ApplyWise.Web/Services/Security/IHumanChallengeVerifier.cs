using Microsoft.AspNetCore.Http;

namespace ApplyWise.Web.Services.Security;

public interface IHumanChallengeVerifier
{
    bool Enabled { get; }

    string SiteKey { get; }

    Task<bool> VerifyAsync(
        HttpContext httpContext,
        string expectedAction,
        CancellationToken cancellationToken);
}
