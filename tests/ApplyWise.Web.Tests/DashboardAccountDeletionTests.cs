using System.Net;
using System.Security.Claims;
using ApplyWise.Web.Controllers;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.Services.Dashboard;
using ApplyWise.Web.Services.Gmail;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.ViewModels.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class DashboardAccountDeletionTests
{
    private const string UserId = "delete-account-user";

    [Fact]
    public async Task DeleteAccount_LocksAndRevokesGmailBeforeDeletingLocalAccount()
    {
        var authentication = new RecordingAuthenticationService();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddSingleton<IAuthenticationService>(authentication);
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(
                "delete-account-" + Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager();
        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var user = new IdentityUser
        {
            Id = UserId,
            UserName = "candidate@example.test",
            Email = "candidate@example.test"
        };
        db.Users.Add(user);
        var now = DateTimeOffset.UtcNow;
        db.Resumes.Add(new Resume
        {
            UserId = UserId,
            VersionName = "Primary",
            OriginalFileName = "resume.pdf",
            StoredFileName = "stored-resume.pdf",
            FilePath = "C:\\private\\resumes\\stored-resume.pdf",
            ContentType = "application/pdf",
            UploadedAt = now,
            UpdatedAt = now
        });
        db.GmailConnections.Add(new GmailConnection
        {
            UserId = UserId,
            EmailAddress = "candidate@gmail.test",
            ProtectedRefreshToken = "stored-refresh-token",
            ConnectedAt = now,
            UpdatedAt = now,
            NextSyncAt = now,
            AutoAddHighConfidenceApplications = true
        });
        await db.SaveChangesAsync();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId)],
                authenticationType: "Test"))
        };
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;
        var operationLocks = new RecordingApplicationLockProvider();
        var revocationHandler = new RecordingRevocationHandler();
        var controller = new DashboardController(
            db,
            provider.GetRequiredService<UserManager<IdentityUser>>(),
            new DashboardReadService(db),
            new SuccessfulSecurityCodeService(),
            provider.GetRequiredService<SignInManager<IdentityUser>>(),
            Options.Create(new GoogleIntegrationOptions()),
            operationLocks,
            new PassThroughCredentialProtector(),
            new StubHttpClientFactory(revocationHandler),
            NullLogger<DashboardController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData(),
                ActionDescriptor = new ControllerActionDescriptor()
            },
            TempData = new TempDataDictionary(
                httpContext,
                new InMemoryTempDataProvider())
        };

        var result = await controller.DeleteAccount(new DeleteAccountInput
        {
            Code = "123456",
            Confirmation = "DELETE"
        });

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal($"gmail-user:{UserId}", operationLocks.Resource);
        Assert.Equal(1, revocationHandler.RequestCount);
        Assert.Equal("token=stored-refresh-token", revocationHandler.LastRequestBody);
        db.ChangeTracker.Clear();
        Assert.Null(await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == UserId));
        Assert.Empty(await db.GmailConnections.ToListAsync());
        Assert.Equal(
            "C:\\private\\resumes\\stored-resume.pdf",
            (await db.ResumeFileCleanups.SingleAsync()).FilePath);
        Assert.NotEmpty(authentication.SignedOutSchemes);
    }

    private sealed class SuccessfulSecurityCodeService : IAccountSecurityCodeService
    {
        public Task<SecurityCodeVerificationResult> VerifyAsync(
            string userId,
            AccountSecurityAction action,
            string? code,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SecurityCodeVerificationResult(true, 1, "Verified"));

        public Task<SecurityCodeIssueResult> IssueAsync(
            string userId,
            string email,
            AccountSecurityAction action,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ConsumeAsync(
            int codeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingApplicationLockProvider : IApplicationLockProvider
    {
        public string? Resource { get; private set; }

        public Task<IAsyncDisposable?> TryAcquireAsync(
            string resource,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Resource = resource;
            return Task.FromResult<IAsyncDisposable?>(NoopLease.Instance);
        }

        private sealed class NoopLease : IAsyncDisposable
        {
            public static NoopLease Instance { get; } = new();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRevocationHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StubHttpClientFactory(
        HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler);

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class PassThroughCredentialProtector : IGmailCredentialProtector
    {
        public string Protect(string refreshToken) => refreshToken;

        public string Unprotect(string protectedRefreshToken) =>
            protectedRefreshToken;
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public List<string?> SignedOutSchemes { get; } = [];

        public Task<AuthenticateResult> AuthenticateAsync(
            HttpContext context,
            string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            SignedOutSchemes.Add(scheme);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(
            HttpContext context,
            IDictionary<string, object> values)
        {
        }
    }
}
