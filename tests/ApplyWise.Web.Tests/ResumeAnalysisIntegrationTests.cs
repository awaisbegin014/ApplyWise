using System.Security.Claims;
using ApplyWise.Web.Controllers;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.BestResumePicker;
using ApplyWise.Web.Services.Ai;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.ViewModels.ResumeAnalyzer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.Services.Subscriptions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ResumeAnalysisIntegrationTests
{
    private const string JobDescription = """
        Senior Software Engineer
        Must Have
        C#
        AWS
        Responsibilities
        Develop APIs using C# and collaborate with product teams.
        """;

    [Fact]
    public async Task Analysis_cache_is_stable_and_scoped_to_the_resume_owner_and_context()
    {
        await using var db = CreateDbContext();
        var first = CreateResume("user-a", "Primary", ResumeWithExperience());
        var second = CreateResume("user-b", "Primary", ResumeWithExperience());
        db.Resumes.AddRange(first, second);
        await db.SaveChangesAsync();
        var store = CreateStore(db);

        var created = await store.AnalyzeAndStageAsync(
            first, first.ExtractedText!, JobDescription, null, ResumeAnalysisType.PastedRequirements);
        await db.SaveChangesAsync();
        var cached = await store.AnalyzeAndStageAsync(
            first, first.ExtractedText!, JobDescription, null, ResumeAnalysisType.PastedRequirements);
        var otherOwner = await store.AnalyzeAndStageAsync(
            second, second.ExtractedText!, JobDescription, null, ResumeAnalysisType.PastedRequirements);
        await db.SaveChangesAsync();

        Assert.False(created.IsCacheHit);
        Assert.True(cached.IsCacheHit);
        Assert.Equal(created.Analysis.Id, cached.Analysis.Id);
        Assert.False(otherOwner.IsCacheHit);
        Assert.NotEqual(created.Analysis.Id, otherOwner.Analysis.Id);
        Assert.Equal(2, await db.ResumeAnalyses.CountAsync());
        Assert.Equal(64, created.Analysis.InputHash?.Length);
        Assert.DoesNotContain("user-a", created.Analysis.InputHash!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ResumeAnalysisResult.CurrentScoreVersion, created.Analysis.ScoreVersion);
        Assert.Equal(created.Result.OverallScore, created.Analysis.MatchScore);
        Assert.NotNull(created.Analysis.ReviewJson);
        Assert.NotNull(created.Analysis.EvidenceJson);
        Assert.Empty(created.Analysis.ResumeTextSnapshot);
        Assert.Equal(
            System.Text.Encoding.Unicode.GetByteCount(created.Analysis.JobDescriptionSnapshot),
            created.Analysis.SnapshotSizeBytes);
    }

    [Fact]
    public async Task Best_resume_picker_uses_fit_and_evidence_tie_breaks_and_reuses_cached_rows()
    {
        await using var db = CreateDbContext();
        var skillsOnly = CreateResume("owner", "Skills only", ResumeWithSkillsOnly(), isDefault: true);
        var contextual = CreateResume("owner", "Contextual evidence", ResumeWithExperience());
        var application = new JobApplication
        {
            UserId = "owner",
            CompanyName = "Example Company",
            JobTitle = "Senior Software Engineer",
            JobDescription = JobDescription,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.AddRange(skillsOnly, contextual, application);
        await db.SaveChangesAsync();
        var store = CreateStore(db);
        var quotas = new WorkspaceQuotaService(
            db,
            Options.Create(new WorkspaceQuotaOptions()));
        var picker = new BestResumePickerService(
            db,
            new UnusedStorageService(),
            new UnusedTextExtractor(),
            store,
            quotas,
            new WorkspaceQuotaGate(db),
            NullLogger<BestResumePickerService>.Instance);

        var first = await picker.CompareResumesForJobAsync("owner", application.Id);
        var rowsAfterFirstRun = await db.ResumeAnalyses.CountAsync();
        var second = await picker.CompareResumesForJobAsync("owner", application.Id);

        Assert.True(first.HasDetectedSkills);
        Assert.Equal(contextual.Id, first.RecommendedResumeId);
        Assert.Contains("evidence quality", first.RecommendationReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, rowsAfterFirstRun);
        Assert.Equal(rowsAfterFirstRun, await db.ResumeAnalyses.CountAsync());
        Assert.Equal(first.RecommendedResumeId, second.RecommendedResumeId);
    }

    [Fact]
    public async Task Best_resume_picker_refuses_to_name_a_winner_without_meaningful_requirements()
    {
        await using var db = CreateDbContext();
        var resume = CreateResume("owner", "Primary", ResumeWithExperience());
        db.Add(resume);
        await db.SaveChangesAsync();
        var quotas = new WorkspaceQuotaService(
            db,
            Options.Create(new WorkspaceQuotaOptions()));
        var picker = new BestResumePickerService(
            db,
            new UnusedStorageService(),
            new UnusedTextExtractor(),
            CreateStore(db),
            quotas,
            new WorkspaceQuotaGate(db),
            NullLogger<BestResumePickerService>.Instance);

        var result = await picker.CompareResumesWithRequirementsAsync(
            "owner",
            "Great company culture and competitive salary with equal opportunity employment.");

        Assert.False(result.HasDetectedSkills);
        Assert.Null(result.RecommendedResumeId);
        Assert.Null(result.RecommendationReason);
    }

    [Fact]
    public async Task Best_resume_picker_rejects_the_whole_batch_when_remaining_quota_is_too_small()
    {
        await using var db = CreateDbContext();
        var first = CreateResume("owner", "First", ResumeWithExperience());
        var second = CreateResume("owner", "Second", ResumeWithSkillsOnly());
        db.AddRange(first, second);
        await db.SaveChangesAsync();
        var quotas = new WorkspaceQuotaService(
            db,
            Options.Create(new WorkspaceQuotaOptions
            {
                MaxAnalysesPerUser = 1,
                MaxAnalysisSnapshotBytesPerUser = 1024 * 1024
            }));
        var picker = new BestResumePickerService(
            db,
            new UnusedStorageService(),
            new UnusedTextExtractor(),
            CreateStore(db),
            quotas,
            new WorkspaceQuotaGate(db),
            NullLogger<BestResumePickerService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            picker.CompareResumesWithRequirementsAsync("owner", JobDescription));

        Assert.Contains("saved-analysis limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.ResumeAnalyses.ToListAsync());
    }

    [Fact]
    public void Analysis_posts_keep_antiforgery_and_the_per_user_rate_limit()
    {
        var methods = new[]
        {
            typeof(ResumeAnalyzerController).GetMethod(nameof(ResumeAnalyzerController.AnalyzePastedRequirements))!,
            typeof(ResumeAnalyzerController).GetMethod(nameof(ResumeAnalyzerController.AnalyzeSavedResumeAts))!,
            typeof(ResumeAnalyzerController).GetMethod(nameof(ResumeAnalyzerController.AnalyzeSavedApplication))!,
            typeof(ResumeAnalyzerController).GetMethod(nameof(ResumeAnalyzerController.CompareAllResumes))!
        };

        Assert.All(methods, method =>
        {
            Assert.NotNull(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).SingleOrDefault());
            var rateLimit = Assert.IsType<EnableRateLimitingAttribute>(
                method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Single());
            Assert.Equal(
                method.Name == nameof(ResumeAnalyzerController.CompareAllResumes)
                    ? "resume-comparison"
                    : "resume-analysis",
                rateLimit.PolicyName);
        });
    }

    [Fact]
    public async Task Saved_resume_ats_check_persists_the_score_and_redirects_to_the_report()
    {
        const string userId = "saved-ats-user";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("saved-ats-controller-" + Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new IdentityUser { Id = userId, UserName = "saved-ats@example.test" });
        var resume = CreateResume(userId, "QA", ResumeWithExperience());
        db.Resumes.Add(resume);
        await db.SaveChangesAsync();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                authenticationType: "Test"))
        };
        var controller = new ResumeAnalyzerController(
            db,
            provider.GetRequiredService<UserManager<IdentityUser>>(),
            new UnusedStorageService(),
            new UnusedTextExtractor(),
            new UnusedIngestionService(),
            CreateStore(db),
            new WorkspaceQuotaService(db, Options.Create(new WorkspaceQuotaOptions())),
            new WorkspaceQuotaGate(db),
            new NoOpProductEventRecorder(),
            new DisabledAiAdvisor(),
            CreateSubscriptionService(db),
            new UnusedBestResumePickerService(),
            NullLogger<ResumeAnalyzerController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var action = await controller.AnalyzeSavedResumeAts(
            new SavedAtsAnalysisViewModel { ResumeId = resume.Id });

        var redirect = Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal(nameof(ResumeAnalyzerController.Index), redirect.ActionName);
        var analysisId = Assert.IsType<int>(redirect.RouteValues!["analysisId"]);
        Assert.Equal($"analysis-result-{analysisId}", redirect.Fragment);

        var analysis = await db.ResumeAnalyses.SingleAsync(item => item.Id == analysisId);
        Assert.Equal(userId, analysis.UserId);
        Assert.Equal(resume.Id, analysis.ResumeId);
        Assert.Equal(ResumeAnalysisType.PastedRequirements, analysis.AnalysisType);
        Assert.True(string.IsNullOrEmpty(analysis.JobDescriptionSnapshot));
        Assert.NotNull(analysis.AtsReadinessScore);
        Assert.Null(analysis.JobMatchScore);

        var page = Assert.IsType<ViewResult>(await controller.Index(null, null, null, analysisId));
        var pageModel = Assert.IsType<AnalyzerIndexViewModel>(page.Model);
        Assert.Equal("ats", pageModel.Mode);
        Assert.NotNull(pageModel.LatestResult);
        Assert.Equal(analysis.AtsReadinessScore, pageModel.LatestResult.AtsReadinessScore);
        Assert.False(pageModel.LatestResult.HasJobMatch);
    }

    [Fact]
    public async Task Saved_resume_analysis_at_quota_redirects_with_feedback_instead_of_throwing()
    {
        const string userId = "quota-user";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("saved-ats-quota-" + Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new IdentityUser { Id = userId, UserName = "quota@example.test" });
        var resume = CreateResume(userId, "Quota", ResumeWithExperience());
        db.Resumes.Add(resume);
        await db.SaveChangesAsync();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                authenticationType: "Test"))
        };
        var quotas = new WorkspaceQuotaService(
            db,
            Options.Create(new WorkspaceQuotaOptions
            {
                MaxAnalysesPerUser = 0,
                MaxAnalysisSnapshotBytesPerUser = 0
            }));
        var controller = new ResumeAnalyzerController(
            db,
            provider.GetRequiredService<UserManager<IdentityUser>>(),
            new UnusedStorageService(),
            new UnusedTextExtractor(),
            new UnusedIngestionService(),
            CreateStore(db),
            quotas,
            new WorkspaceQuotaGate(db),
            new NoOpProductEventRecorder(),
            new DisabledAiAdvisor(),
            CreateSubscriptionService(db),
            new UnusedBestResumePickerService(),
            NullLogger<ResumeAnalyzerController>.Instance)
        {
            ControllerContext = new ControllerContext(
                new ActionContext(
                    httpContext,
                    new RouteData(),
                    new ControllerActionDescriptor()))
        };

        var action = await controller.AnalyzeSavedResumeAts(
            new SavedAtsAnalysisViewModel { ResumeId = resume.Id });

        var redirect = Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal(nameof(ResumeAnalyzerController.Index), redirect.ActionName);
        Assert.Null(redirect.RouteValues?["analysisId"]);
        Assert.Contains(
            "saved-analysis limit",
            Assert.IsType<string>(controller.TempData["AnalysisError"]),
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.ResumeAnalyses.ToListAsync());
        Assert.Equal(AiUsageStatus.Failed, (await db.AiUsageRecords.SingleAsync()).Status);
        Assert.DoesNotContain(
            db.ChangeTracker.Entries<ResumeAnalysis>(),
            entry => entry.State == EntityState.Added);
    }

    private sealed class NoOpProductEventRecorder : IProductEventRecorder
    {
        public Task RecordAsync(string name, string source, string? userId = null, bool succeeded = true, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordLoginAsync(string userId, string source, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static SubscriptionService CreateSubscriptionService(ApplicationDbContext db) => new(
        db,
        new WorkspaceQuotaGate(db),
        Options.Create(new SubscriptionOptions()),
        TimeProvider.System);

    private sealed class DisabledAiAdvisor : IGeminiAtsAdvisor
    {
        public bool IsConfigured => false;
        public string ModelName => "disabled";
        public Task<AiAtsFeedback> CreateFeedbackAsync(
            ResumeAnalysisAiContext context,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedBestResumePickerService : IBestResumePickerService
    {
        public Task<BestResumePickerResult> CompareResumesForJobAsync(
            string userId,
            int jobApplicationId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BestResumePickerResult> CompareResumesWithRequirementsAsync(
            string userId,
            string jobRequirements,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public void Direct_ats_upload_is_antiforgery_protected_rate_limited_and_size_bounded()
    {
        var method = typeof(ResumeAnalyzerController)
            .GetMethod(nameof(ResumeAnalyzerController.AnalyzeAtsUpload))!;

        Assert.NotNull(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).SingleOrDefault());
        var rateLimit = Assert.IsType<EnableRateLimitingAttribute>(
            method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Single());
        Assert.Equal("uploads", rateLimit.PolicyName);
        Assert.IsType<RequestSizeLimitAttribute>(
            method.GetCustomAttributes(typeof(RequestSizeLimitAttribute), true).Single());
        var requestLimit = method.CustomAttributes.Single(attribute =>
            attribute.AttributeType == typeof(RequestSizeLimitAttribute));
        Assert.Equal(
            ResumeIngestionLimits.MaxFileSizeBytes + ResumeIngestionLimits.RequestOverheadBytes,
            Assert.IsType<long>(requestLimit.ConstructorArguments.Single().Value));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ats-v2-tests-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ResumeAnalysisStore CreateStore(ApplicationDbContext db)
    {
        var normalizer = new ResumeTextNormalizer();
        var taxonomy = new SkillTaxonomyService(normalizer);
        var service = new ResumeAnalysisService(
            new ResumeSectionDetector(normalizer),
            new JobRequirementExtractor(normalizer, taxonomy),
            new AtsReadinessScorer(),
            new JobMatchScorer(normalizer, taxonomy),
            NullLogger<ResumeAnalysisService>.Instance);
        return new ResumeAnalysisStore(db, normalizer, taxonomy, service, NullLogger<ResumeAnalysisStore>.Instance);
    }

    private static Resume CreateResume(string userId, string name, string text, bool isDefault = false) => new()
    {
        UserId = userId,
        VersionName = name,
        OriginalFileName = name + ".pdf",
        StoredFileName = Guid.NewGuid().ToString("N") + ".pdf",
        FilePath = "private/" + Guid.NewGuid().ToString("N") + ".pdf",
        ContentType = "application/pdf",
        FileSize = 1024,
        IsDefault = isDefault,
        ExtractedText = text,
        UploadedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static string ResumeWithExperience() => """
        Awais Example
        awais@example.com | +1 555 010 1234 | Lahore, Pakistan | linkedin.com/in/awais

        Professional Summary
        Senior Software Engineer building reliable customer-facing platforms with C# and AWS.

        Experience
        Software Engineer | Example Company | Jan 2022 - Present
        • Developed REST APIs using C# and deployed services with AWS, reducing response time by 25%.
        • Collaborated with product teams to deliver customer workflows used by 10k users.

        Education
        Bachelor of Computer Science | Example University | 2018 - 2022

        Technical Skills
        C#, AWS, SQL, Git
        """;

    private static string ResumeWithSkillsOnly() => """
        Awais Example
        awais@example.com | +1 555 010 1234 | Lahore, Pakistan | linkedin.com/in/awais

        Professional Summary
        Software professional supporting customer-facing platforms.

        Experience
        Software Engineer | Example Company | Jan 2022 - Present
        • Supported application delivery and collaborated with product teams on customer workflows.

        Education
        Bachelor of Computer Science | Example University | 2018 - 2022

        Technical Skills
        C#, AWS, SQL, Git
        """;

    private sealed class UnusedStorageService : IResumeStorageService
    {
        public string CreateRelativePath(string userId, string storedFileName) => throw new NotSupportedException();
        public string ResolvePath(string relativePath) => throw new NotSupportedException();
    }

    private sealed class UnusedTextExtractor : IResumeTextExtractorService
    {
        public Task<string?> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PdfTextExtractionResult> InspectAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedIngestionService : IResumeIngestionService
    {
        public Task<ResumeIngestionResult> IngestAsync(
            ResumeIngestionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
