using System.Net;
using System.Security.Claims;
using ApplyWise.Web.Controllers;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Gmail;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class GmailConnectionsControllerTests
{
    private const string UserId = "gmail-controller-user";
    private const string FlowId = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Disconnect_WaitsForCallbackRowAndAlwaysDeletesLocalToken(
        HttpStatusCode revocationStatus)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddAuthentication();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(
                "gmail-controller-" + Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager();
        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new IdentityUser
        {
            Id = UserId,
            UserName = "candidate@example.test"
        });
        await db.SaveChangesAsync();

        var operationLocks = new BlockingApplicationLockProvider();
        var revocationHandler = new RecordingRevocationHandler(revocationStatus);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId)],
                authenticationType: "Test"))
        };
        var controller = new GmailConnectionsController(
            db,
            provider.GetRequiredService<UserManager<IdentityUser>>(),
            provider.GetRequiredService<SignInManager<IdentityUser>>(),
            new PassThroughCredentialProtector(),
            new UnusedGmailImportService(),
            operationLocks,
            new StubHttpClientFactory(revocationHandler),
            Options.Create(new GoogleIntegrationOptions()),
            NullLogger<GmailConnectionsController>.Instance)
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

        var disconnectTask = controller.Disconnect();
        await operationLocks.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            var now = DateTimeOffset.UtcNow;
            db.GmailConnections.Add(new GmailConnection
            {
                UserId = UserId,
                EmailAddress = "candidate@gmail.test",
                ProtectedRefreshToken = "callback-refresh-token",
                ConnectedAt = now,
                UpdatedAt = now,
                NextSyncAt = now
            });
            await db.SaveChangesAsync();
        }
        finally
        {
            operationLocks.Release();
        }

        var result = Assert.IsType<RedirectToActionResult>(await disconnectTask);
        Assert.Equal("Index", result.ActionName);
        Assert.Equal("ApplicationImports", result.ControllerName);
        Assert.Empty(await db.GmailConnections.ToListAsync());
        Assert.Equal(1, revocationHandler.RequestCount);
        Assert.Equal("token=callback-refresh-token", revocationHandler.LastRequestBody);
    }

    [Fact]
    public async Task TokenRevocation_AlreadyInvalidToken_IsIdempotentSuccess()
    {
        var handler = new RecordingRevocationHandler(
            HttpStatusCode.BadRequest,
            "{\"error\":\"invalid_token\"}");

        var result = await GmailTokenRevocation.TryRevokeTokenAsync(
            "already-revoked",
            42,
            new StubHttpClientFactory(handler),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Callback_DoesNotRestoreConnectionWhenDisconnectWinsBeforeItsStateGate()
    {
        var authentication = new NoopAuthenticationService();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddSingleton<IAuthenticationService>(authentication);
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(
                "gmail-callback-" + Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager();
        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new IdentityUser
        {
            Id = UserId,
            UserName = "candidate@example.test"
        });
        var now = DateTimeOffset.UtcNow;
        db.GmailConnections.Add(new GmailConnection
        {
            UserId = UserId,
            EmailAddress = "candidate@gmail.test",
            ProtectedRefreshToken = "old-refresh-token",
            ConnectedAt = now,
            UpdatedAt = now,
            NextSyncAt = now,
            AutoAddHighConfidenceApplications = true
        });
        db.GmailOAuthFlows.Add(new GmailOAuthFlow
        {
            UserId = UserId,
            FlowId = FlowId,
            StartedAt = now
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
        var externalPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Email, "candidate@gmail.test"),
            new Claim(
                GmailAuthenticationDefaults.FlowClaimType,
                GmailAuthenticationDefaults.FlowClaimValue)
        ], GmailAuthenticationDefaults.Scheme));
        var externalInfo = new ExternalLoginInfo(
            externalPrincipal,
            GmailAuthenticationDefaults.Scheme,
            "google-user-key",
            GmailAuthenticationDefaults.DisplayName)
        {
            AuthenticationTokens =
            [
                new AuthenticationToken
                {
                    Name = "refresh_token",
                    Value = "new-refresh-token"
                }
            ],
            AuthenticationProperties = new AuthenticationProperties(
                new Dictionary<string, string?>
                {
                    [GmailAuthenticationDefaults.FlowIdProperty] = FlowId
                })
        };
        var userManager = provider.GetRequiredService<UserManager<IdentityUser>>();
        var signInManager = new StubExternalSignInManager(
            userManager,
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>(),
            provider.GetRequiredService<IOptions<IdentityOptions>>(),
            provider.GetRequiredService<ILogger<SignInManager<IdentityUser>>>(),
            provider.GetRequiredService<IAuthenticationSchemeProvider>(),
            provider.GetRequiredService<IUserConfirmation<IdentityUser>>(),
            externalInfo);
        var revocationHandler = new RecordingRevocationHandler();
        var stateGate = new DisconnectionBeforeCallbackStateGate(db);
        var controller = new GmailConnectionsController(
            db,
            userManager,
            signInManager,
            new PassThroughCredentialProtector(),
            new UnusedGmailImportService(),
            new ImmediateApplicationLockProvider(),
            new StubHttpClientFactory(revocationHandler),
            Options.Create(new GoogleIntegrationOptions
            {
                ClientId = "fictional-client.apps.googleusercontent.com",
                ClientSecret = "fictional-client-secret",
                GmailImportEnabled = true
            }),
            NullLogger<GmailConnectionsController>.Instance,
            stateGate)
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

        var result = Assert.IsType<RedirectToActionResult>(
            await controller.Callback());

        Assert.Equal("Index", result.ActionName);
        Assert.Equal("ApplicationImports", result.ControllerName);
        Assert.True(stateGate.DisconnectionInjected);
        db.ChangeTracker.Clear();
        Assert.Empty(await db.GmailConnections.ToListAsync());
        Assert.Empty(await db.GmailOAuthFlows.ToListAsync());
        Assert.Equal(1, revocationHandler.RequestCount);
        Assert.Equal("token=new-refresh-token", revocationHandler.LastRequestBody);
    }

    private sealed class BlockingApplicationLockProvider : IApplicationLockProvider
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public async Task<IAsyncDisposable?> TryAcquireAsync(
            string resource,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal($"gmail-user:{UserId}", resource);
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return NoopLease.Instance;
        }

        public void Release() => _release.TrySetResult();

        private sealed class NoopLease : IAsyncDisposable
        {
            public static NoopLease Instance { get; } = new();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ImmediateApplicationLockProvider : IApplicationLockProvider
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(
            string resource,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IAsyncDisposable?>(NoopLease.Instance);

        private sealed class NoopLease : IAsyncDisposable
        {
            public static NoopLease Instance { get; } = new();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class DisconnectionBeforeCallbackStateGate(
        ApplicationDbContext db) : IWorkspaceQuotaGate
    {
        public bool DisconnectionInjected { get; private set; }

        public async Task<TResult> RunAsync<TResult>(
            string resource,
            string userId,
            Func<CancellationToken, Task<TResult>> action,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(WorkspaceQuotaResources.GmailConnections, resource);
            var connection = await db.GmailConnections.SingleAsync(
                item => item.UserId == userId,
                cancellationToken);
            var flow = await db.GmailOAuthFlows.SingleAsync(
                item => item.UserId == userId,
                cancellationToken);
            db.GmailConnections.Remove(connection);
            db.GmailOAuthFlows.Remove(flow);
            await db.SaveChangesAsync(cancellationToken);
            DisconnectionInjected = true;
            return await action(cancellationToken);
        }
    }

    private sealed class StubExternalSignInManager(
        UserManager<IdentityUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<IdentityUser> claimsFactory,
        IOptions<IdentityOptions> options,
        ILogger<SignInManager<IdentityUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<IdentityUser> confirmation,
        ExternalLoginInfo externalInfo) : SignInManager<IdentityUser>(
            userManager,
            contextAccessor,
            claimsFactory,
            options,
            logger,
            schemes,
            confirmation)
    {
        public override Task<ExternalLoginInfo?> GetExternalLoginInfoAsync(
            string? expectedXsrf = null) =>
            Task.FromResult<ExternalLoginInfo?>(externalInfo);
    }

    private sealed class NoopAuthenticationService : IAuthenticationService
    {
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
            AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }

    private sealed class RecordingRevocationHandler(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? responseBody = null) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal(
                "https://oauth2.googleapis.com/revoke",
                request.RequestUri?.AbsoluteUri);
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = responseBody is null
                    ? null
                    : new StringContent(responseBody)
            };
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

    private sealed class UnusedGmailImportService : IGmailImportService
    {
        public Task<GmailSyncResult> SyncUserAsync(
            string userId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SyncDueConnectionsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SyncStartupConnectionsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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
