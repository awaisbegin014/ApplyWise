using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Dashboard;
using ApplyWise.Web.Services.ResumeAnalysis;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class DashboardReadServiceTests
{
    [Fact]
    public async Task GetAsync_BuildsCompleteTenantScopedDashboardFromProjectedRows()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"dashboard-{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApplicationDbContext(options);

        var now = DateTimeOffset.UtcNow;
        var tomorrow = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        var resume = new Resume
        {
            Id = 1,
            UserId = "user-a",
            VersionName = "Backend",
            OriginalFileName = "backend.pdf",
            StoredFileName = "stored.pdf",
            FilePath = "private/stored.pdf",
            ContentType = "application/pdf",
            UploadedAt = now,
            UpdatedAt = now
        };
        var application = new JobApplication
        {
            Id = 10,
            UserId = "user-a",
            ResumeId = resume.Id,
            CompanyName = "Contoso",
            JobTitle = "Backend Engineer",
            Status = ApplicationStatus.Applied,
            Deadline = tomorrow,
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now
        };
        db.AddRange(
            resume,
            application,
            new JobApplication
            {
                Id = 11,
                UserId = "user-a",
                CompanyName = "Fabrikam",
                JobTitle = "Platform Engineer",
                Status = ApplicationStatus.Offered,
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddMinutes(-1)
            },
            new JobApplication
            {
                Id = 12,
                UserId = "user-b",
                CompanyName = "Private tenant",
                JobTitle = "Must not leak",
                Status = ApplicationStatus.Rejected,
                CreatedAt = now,
                UpdatedAt = now
            },
            new Interview
            {
                Id = 20,
                UserId = "user-a",
                JobApplicationId = application.Id,
                InterviewType = InterviewType.TechnicalInterview,
                Status = InterviewStatus.Scheduled,
                ScheduledAt = now.AddDays(1),
                CreatedAt = now,
                UpdatedAt = now
            },
            new ResumeAnalysis
            {
                Id = 40,
                UserId = "user-a",
                ResumeId = resume.Id,
                JobApplicationId = application.Id,
                AnalysisType = ResumeAnalysisType.SavedApplication,
                MatchScore = 82,
                AtsReadinessScore = 88,
                JobMatchScore = 82,
                EvidenceQuality = 0.75,
                ScoreVersion = ResumeAnalysisResult.CurrentScoreVersion,
                MatchedKeywordsJson = """["C#"]""",
                MissingKeywordsJson = """["Docker"]""",
                SuggestionsJson = "[]",
                ResumeTextSnapshot = "C# ASP.NET Core",
                JobDescriptionSnapshot = "C# ASP.NET Core Docker",
                CreatedAt = now
            });
        await db.SaveChangesAsync();

        var result = await new DashboardReadService(db).GetAsync("user-a", "Awais");

        Assert.Equal("Awais", result.DisplayName);
        Assert.Equal(2, result.TotalApplications);
        Assert.Equal(82, result.AverageMatchScore);
        Assert.Equal("Backend", result.BestResumeVersionName);
        Assert.Equal(82, result.BestResumeScore);
        Assert.Equal(2, result.PipelineApplications.Count);
        Assert.DoesNotContain(result.PipelineApplications, item => item.CompanyName == "Private tenant");
        Assert.Equal(0, result.Funnel.Interview);
        Assert.Equal(1, result.Funnel.Offered);
        Assert.Single(result.TopSkillGaps);
        Assert.Equal("Docker", result.TopSkillGaps[0].SkillName);
        Assert.Single(result.UpcomingDeadlines);
        Assert.Single(result.RecentAnalyses);
    }

    [Fact]
    public async Task GetAsync_bounds_materialized_tenant_rows_but_keeps_the_exact_total()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"dashboard-bounds-{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApplicationDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.JobApplications.AddRange(Enumerable.Range(1, DashboardReadService.MaxApplicationRows + 10)
            .Select(index => new JobApplication
            {
                UserId = "user-a",
                CompanyName = $"Company {index}",
                JobTitle = "Engineer",
                Status = ApplicationStatus.Applied,
                CreatedAt = now.AddMinutes(-index),
                UpdatedAt = now.AddMinutes(-index)
            }));
        await db.SaveChangesAsync();

        var result = await new DashboardReadService(db).GetAsync("user-a", "Candidate");

        Assert.Equal(DashboardReadService.MaxApplicationRows + 10, result.TotalApplications);
        Assert.Equal(DashboardReadService.MaxApplicationRows, result.PipelineApplications.Count);
        Assert.Equal(DashboardReadService.MaxApplicationRows + 10, result.Funnel.Applied);
    }
}
