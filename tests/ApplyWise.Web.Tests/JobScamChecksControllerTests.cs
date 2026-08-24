using System.Security.Claims;
using ApplyWise.Web.Controllers;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.JobScamDetection;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.ViewModels.JobScamChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class JobScamChecksControllerTests
{
    private const string UserId = "scam-check-user";

    [Fact]
    public async Task Analyze_at_quota_does_not_run_detector_or_add_a_row()
    {
        await using var fixture = await CreateFixtureAsync(maxChecks: 1);
        var application = await SeedApplicationAsync(fixture.Db);
        fixture.Db.JobScamChecks.Add(CreateCheck(application, DateTimeOffset.UtcNow));
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Analyze(new RunScamCheckViewModel
        {
            JobApplicationId = application.Id
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("JobApplications", redirect.ControllerName);
        Assert.Equal(0, fixture.Detector.CallCount);
        Assert.Single(await fixture.Db.JobScamChecks.ToListAsync());
        Assert.Contains("saved job-post review limit", fixture.Controller.TempData["ErrorMessage"] as string, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_returns_the_requested_bounded_page()
    {
        await using var fixture = await CreateFixtureAsync(maxChecks: 100);
        var application = await SeedApplicationAsync(fixture.Db);
        var start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 30; index++)
        {
            fixture.Db.JobScamChecks.Add(CreateCheck(application, start.AddMinutes(index)));
        }
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.History(page: 2);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<JobScamCheckHistoryViewModel>(view.Model);
        Assert.Equal(30, model.TotalCount);
        Assert.Equal(2, model.TotalPages);
        Assert.Equal(2, model.Page);
        Assert.Equal(5, model.Checks.Count);
        Assert.True(model.HasPreviousPage);
        Assert.False(model.HasNextPage);
        Assert.Equal(start.AddMinutes(4), model.Checks[0].CreatedAt);
    }

    [Fact]
    public void Analyze_is_anti_forgery_and_rate_limited()
    {
        var method = typeof(JobScamChecksController).GetMethod(nameof(JobScamChecksController.Analyze));

        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).SingleOrDefault());
        var limiter = Assert.Single(method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true));
        Assert.Equal("scam-check", Assert.IsType<EnableRateLimitingAttribute>(limiter).PolicyName);
    }

    private static async Task<Fixture> CreateFixtureAsync(int maxChecks)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("scam-controller-" + Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new IdentityUser { Id = UserId, UserName = "candidate@example.test" });
        await db.SaveChangesAsync();

        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId)],
                authenticationType: "Test"))
        };
        var detector = new CountingDetector();
        var quotaOptions = Options.Create(new WorkspaceQuotaOptions
        {
            MaxScamChecksPerUser = maxChecks
        });
        var controller = new JobScamChecksController(
            db,
            provider.GetRequiredService<UserManager<IdentityUser>>(),
            detector,
            new NoOpProductEventRecorder(),
            new WorkspaceQuotaService(db, quotaOptions),
            new WorkspaceQuotaGate(db))
        {
            ControllerContext = new ControllerContext(new ActionContext(
                context,
                new RouteData(),
                new ControllerActionDescriptor())),
            TempData = new TempDataDictionary(context, new StubTempDataProvider())
        };
        return new Fixture(provider, db, controller, detector);
    }

    private static async Task<JobApplication> SeedApplicationAsync(ApplicationDbContext db)
    {
        var application = new JobApplication
        {
            UserId = UserId,
            CompanyName = "Contoso",
            JobTitle = "Product Designer",
            JobDescription = new string('a', 200),
            Status = ApplicationStatus.Applied,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.JobApplications.Add(application);
        await db.SaveChangesAsync();
        return application;
    }

    private static JobScamCheck CreateCheck(JobApplication application, DateTimeOffset createdAt) => new()
    {
        UserId = UserId,
        JobApplicationId = application.Id,
        JobApplication = application,
        RiskScore = 0,
        RiskLevel = JobRiskLevel.Low,
        RedFlagsJson = "[]",
        QualityScore = 80,
        MissingInformationJson = "[]",
        Recommendation = "Verify the employer independently.",
        CreatedAt = createdAt
    };

    private sealed class CountingDetector : IJobScamDetectorService
    {
        public int CallCount { get; private set; }

        public JobScamCheckResult AnalyzeJob(JobApplication application)
        {
            CallCount++;
            return new JobScamCheckResult(0, JobRiskLevel.Low, [], 80, [], "Verify independently.");
        }
    }

    private sealed class NoOpProductEventRecorder : IProductEventRecorder
    {
        public Task RecordAsync(string name, string source, string? userId = null, bool succeeded = true, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordLoginAsync(string userId, string source, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class Fixture(
        ServiceProvider services,
        ApplicationDbContext db,
        JobScamChecksController controller,
        CountingDetector detector) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public JobScamChecksController Controller { get; } = controller;
        public CountingDetector Detector { get; } = detector;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await services.DisposeAsync();
        }
    }
}
