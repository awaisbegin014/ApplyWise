using System.Security.Claims;
using ApplyWise.Web.Controllers;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Gmail;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.ViewModels.ApplicationImports;
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

public sealed class ApplicationImportsControllerTests
{
    private const string UserId = "imports-controller-user";
    private const string OtherUserId = "imports-controller-other";

    [Fact]
    public async Task UpdateAutoAddPreference_ChangesOnlyCurrentUsersConnection()
    {
        await using var scope = await CreateControllerScopeAsync();

        var result = await scope.Controller.UpdateAutoAddPreference(
            autoAddHighConfidenceApplications: true);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True((await scope.Db.GmailConnections.SingleAsync(
            item => item.UserId == UserId))
            .AutoAddHighConfidenceApplications);
        Assert.False((await scope.Db.GmailConnections.SingleAsync(
            item => item.UserId == OtherUserId))
            .AutoAddHighConfidenceApplications);
    }

    [Fact]
    public async Task UpdateAutoAddPreference_ReloadsAfterLifecycleLockAndDoesNotReviveDeletedConnection()
    {
        var operationLocks = new BlockingApplicationLockProvider();
        await using var scope = await CreateControllerScopeAsync(
            _ => operationLocks);

        var updateTask = scope.Controller.UpdateAutoAddPreference(
            autoAddHighConfidenceApplications: true);
        await operationLocks.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            var connection = await scope.Db.GmailConnections.SingleAsync(
                item => item.UserId == UserId);
            scope.Db.GmailConnections.Remove(connection);
            await scope.Db.SaveChangesAsync();
        }
        finally
        {
            operationLocks.Release();
        }

        Assert.IsType<NotFoundResult>(await updateTask);
        Assert.DoesNotContain(
            await scope.Db.GmailConnections.ToListAsync(),
            item => item.UserId == UserId);
    }

    [Fact]
    public async Task Index_ReturnsOwnedPendingImportsAndLatestTenAutoAddedApplications()
    {
        await using var scope = await CreateControllerScopeAsync();
        var ownedConnection = await scope.Db.GmailConnections.SingleAsync(
            item => item.UserId == UserId);
        var otherConnection = await scope.Db.GmailConnections.SingleAsync(
            item => item.UserId == OtherUserId);
        var now = DateTimeOffset.UtcNow;
        var ownedApplications = Enumerable.Range(1, 12)
            .Select(number => new JobApplication
            {
                UserId = UserId,
                CompanyName = $"Company {number}",
                JobTitle = $"Role {number}",
                Source = JobSource.CompanyWebsite,
                Status = ApplicationStatus.Applied,
                AppliedDate = new DateOnly(2026, 7, number),
                CreatedAt = now.AddMinutes(number),
                UpdatedAt = now.AddMinutes(number)
            })
            .ToList();
        var otherApplication = new JobApplication
        {
            UserId = OtherUserId,
            CompanyName = "Foreign Company",
            JobTitle = "Foreign Role",
            Source = JobSource.CompanyWebsite,
            Status = ApplicationStatus.Applied,
            AppliedDate = new DateOnly(2026, 7, 28),
            CreatedAt = now.AddHours(1),
            UpdatedAt = now.AddHours(1)
        };
        scope.Db.JobApplications.AddRange(ownedApplications);
        scope.Db.JobApplications.Add(otherApplication);
        await scope.Db.SaveChangesAsync();

        scope.Db.ApplicationImports.AddRange(
            ownedApplications.Select((application, index) =>
                CreateImport(
                    ownedConnection,
                    $"owned-auto-{index}",
                    ApplicationImportStatus.AutoAccepted,
                    application.Id,
                    now.AddMinutes(index))));
        scope.Db.ApplicationImports.Add(
            CreateImport(
                ownedConnection,
                "owned-pending",
                ApplicationImportStatus.PendingReview,
                applicationId: null,
                now.AddHours(2)));
        scope.Db.ApplicationImports.Add(
            CreateImport(
                otherConnection,
                "other-pending",
                ApplicationImportStatus.PendingReview,
                applicationId: null,
                now.AddHours(3)));
        scope.Db.ApplicationImports.Add(
            CreateImport(
                otherConnection,
                "other-auto",
                ApplicationImportStatus.AutoAccepted,
                otherApplication.Id,
                now.AddHours(4)));
        await scope.Db.SaveChangesAsync();

        var view = Assert.IsType<ViewResult>(
            await scope.Controller.Index());
        var model = Assert.IsType<ApplicationImportIndexViewModel>(
            view.Model);

        Assert.Single(model.PendingImports);
        Assert.Equal("owned-pending", model.PendingImports[0].EmailSubject);
        Assert.Equal(10, model.RecentlyAutoAddedApplications.Count);
        Assert.DoesNotContain(
            model.RecentlyAutoAddedApplications,
            item => item.ApplicationId == otherApplication.Id);
        Assert.Equal(
            ownedApplications[11].Id,
            model.RecentlyAutoAddedApplications[0].ApplicationId);
    }

    private static ApplicationImport CreateImport(
        GmailConnection connection,
        string messageId,
        ApplicationImportStatus status,
        int? applicationId,
        DateTimeOffset timestamp) =>
        new()
        {
            UserId = connection.UserId,
            GmailConnectionId = connection.Id,
            ExternalMessageId = messageId,
            Direction = ApplicationImportDirection.Incoming,
            Status = status,
            Confidence = 95,
            EmailSubject = messageId,
            CompanyName = "Fictional Company",
            JobTitle = "Fictional Role",
            Source = JobSource.CompanyWebsite,
            AppliedDate = new DateOnly(2026, 7, 28),
            CreatedApplicationId = applicationId,
            DetectedAt = timestamp,
            ReviewedAt = status == ApplicationImportStatus.AutoAccepted
                ? timestamp
                : null
        };

    private static async Task<ControllerScope> CreateControllerScopeAsync(
        Func<ApplicationDbContext, IApplicationLockProvider>? createOperationLocks = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(
                "application-imports-controller-"
                + Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        db.Users.AddRange(
            new IdentityUser
            {
                Id = UserId,
                UserName = "owner@example.test"
            },
            new IdentityUser
            {
                Id = OtherUserId,
                UserName = "other@example.test"
            });
        var now = DateTimeOffset.UtcNow;
        db.GmailConnections.AddRange(
            new GmailConnection
            {
                UserId = UserId,
                EmailAddress = "owner@gmail.test",
                ProtectedRefreshToken = "owner-token",
                ConnectedAt = now,
                UpdatedAt = now,
                NextSyncAt = now
            },
            new GmailConnection
            {
                UserId = OtherUserId,
                EmailAddress = "other@gmail.test",
                ProtectedRefreshToken = "other-token",
                ConnectedAt = now,
                UpdatedAt = now,
                NextSyncAt = now
            });
        await db.SaveChangesAsync();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId)],
                authenticationType: "Test"))
        };
        var processor = new ApplicationImportProcessor(
            db,
            NullLogger<ApplicationImportProcessor>.Instance);
        var controller = new ApplicationImportsController(
            db,
            provider.GetRequiredService<UserManager<IdentityUser>>(),
            new UnusedGmailImportService(),
            processor,
            createOperationLocks?.Invoke(db) ?? new ApplicationLockProvider(db),
            Options.Create(new GoogleIntegrationOptions()))
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
        return new ControllerScope(provider, db, controller);
    }

    private sealed class UnusedGmailImportService : IGmailImportService
    {
        public Task<GmailSyncResult> SyncUserAsync(
            string userId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SyncDueConnectionsAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SyncStartupConnectionsAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(
            HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(
            HttpContext context,
            IDictionary<string, object> values)
        {
        }
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

    private sealed class ControllerScope(
        ServiceProvider provider,
        ApplicationDbContext db,
        ApplicationImportsController controller) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public ApplicationImportsController Controller { get; } = controller;

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
