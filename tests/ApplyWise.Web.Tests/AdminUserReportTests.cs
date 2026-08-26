using System.Reflection;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Admin;
using ApplyWise.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class AdminUserReportTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-14T10:00:00Z");

    [Fact]
    public async Task Report_returns_complete_metadata_summary_for_target_user()
    {
        const string userId = "report-target";
        await using var db = CreateDbContext();
        db.Users.Add(new IdentityUser
        {
            Id = userId,
            UserName = "candidate@example.test",
            Email = "candidate@example.test",
            EmailConfirmed = true,
            TwoFactorEnabled = true,
            AccessFailedCount = 1
        });
        db.UserAccountActivities.Add(new UserAccountActivity
        {
            UserId = userId,
            RegisteredAt = Now.AddDays(-20),
            LastLoginAt = Now.AddHours(-2),
            LastActivityAt = Now.AddMinutes(-10),
            LastLoginProvider = "password",
            TotalSuccessfulLogins = 8
        });
        db.CareerProfiles.Add(new CareerProfile
        {
            UserId = userId,
            FullName = "Candidate Example",
            CareerStage = CareerStage.EarlyCareer,
            Institution = "Example University",
            DegreeProgram = "BS Computer Science",
            FieldOfStudy = "Computer Science",
            GraduationYear = 2026,
            CurrentSemester = "8",
            PreferredLocations = "Lahore, Remote",
            PreferredWorkModes = "Hybrid, Remote",
            Skills = "C#, SQL",
            CareerInterests = "Backend engineering",
            AcademicHighlights = "Dean's list",
            OnboardingCompletedAt = Now.AddDays(-19),
            CreatedAt = Now.AddDays(-20),
            UpdatedAt = Now.AddDays(-1)
        });
        db.Resumes.Add(new Resume
        {
            Id = 101,
            UserId = userId,
            VersionName = "Backend resume",
            OriginalFileName = "candidate.pdf",
            StoredFileName = "private-name.pdf",
            FilePath = "private/report-target/private-name.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            Notes = "sensitive resume note",
            ExtractedText = "sensitive extracted resume text",
            IsDefault = true,
            PageCount = 2,
            UploadedAt = Now.AddDays(-10),
            UpdatedAt = Now.AddDays(-3)
        });
        db.JobApplications.AddRange(
            new JobApplication
            {
                Id = 201,
                UserId = userId,
                ResumeId = 101,
                CompanyName = "Alpha",
                JobTitle = "Junior Engineer",
                JobLocation = "Lahore",
                JobType = JobType.FullTime,
                SalaryRange = "100-120k",
                Source = JobSource.LinkedIn,
                Status = ApplicationStatus.Applied,
                AppliedDate = new DateOnly(2026, 8, 1),
                CreatedAt = Now.AddDays(-8),
                UpdatedAt = Now.AddDays(-7)
            },
            new JobApplication
            {
                Id = 202,
                UserId = userId,
                ResumeId = 101,
                CompanyName = "Beta",
                JobTitle = "Software Engineer",
                JobLocation = "Remote",
                JobType = JobType.Remote,
                Source = JobSource.Indeed,
                Status = ApplicationStatus.Interview,
                AppliedDate = new DateOnly(2026, 8, 5),
                Deadline = new DateOnly(2026, 8, 20),
                CreatedAt = Now.AddDays(-6),
                UpdatedAt = Now.AddDays(-2)
            });
        db.ResumeAnalyses.AddRange(
            CreateAnalysis(301, userId, 101, 201, 74, Now.AddDays(-4)),
            CreateAnalysis(302, userId, 101, 202, 88, Now.AddDays(-1)));
        db.Interviews.AddRange(
            CreateInterview(501, userId, 201, Now.AddDays(-3)),
            CreateInterview(502, userId, 202, Now.AddHours(-12)));
        db.GmailConnections.Add(new GmailConnection
        {
            Id = 401,
            UserId = userId,
            EmailAddress = "candidate@gmail.test",
            ProtectedRefreshToken = "must-never-reach-the-view-model",
            ConnectedAt = Now.AddDays(-12),
            UpdatedAt = Now.AddHours(-3),
            LastSyncStartedAt = Now.AddHours(-2),
            LastSuccessfulSyncAt = Now.AddHours(-2),
            NextSyncAt = Now.AddHours(1),
            AutoAddHighConfidenceApplications = true
        });
        db.ApplicationImports.Add(new ApplicationImport
        {
            Id = 601,
            UserId = userId,
            GmailConnectionId = 401,
            ExternalMessageId = "external-message",
            Direction = ApplicationImportDirection.Incoming,
            Status = ApplicationImportStatus.AutoAccepted,
            Confidence = 96,
            EmailSubject = "sensitive subject",
            SenderDomain = "beta.test",
            CompanyName = "Beta",
            JobTitle = "Software Engineer",
            JobLocation = "Remote",
            Source = JobSource.Indeed,
            AppliedDate = new DateOnly(2026, 8, 5),
            ResumeFileName = "sensitive-attachment.pdf",
            CreatedApplicationId = 202,
            DetectedAt = Now.AddDays(-2),
            ReviewedAt = Now.AddDays(-2)
        });
        db.ProductEvents.AddRange(
            new ProductEvent
            {
                Id = 701,
                UserId = userId,
                Name = "resume.uploaded",
                Source = "upload",
                Succeeded = true,
                OccurredAt = Now.AddDays(-5)
            },
            new ProductEvent
            {
                Id = 702,
                UserId = userId,
                Name = "application.created",
                Source = "gmail",
                Succeeded = true,
                OccurredAt = Now.AddDays(-2)
            });
        await db.SaveChangesAsync();

        var result = Assert.IsType<AdminUserReportViewModel>(
            await CreateService(db).LoadAsync(userId, 1, 1));

        Assert.Equal(Now, result.GeneratedAt);
        Assert.Equal(userId, result.UserId);
        Assert.Equal("candidate@example.test", result.Account.Email);
        Assert.True(result.Account.EmailConfirmed);
        Assert.True(result.Account.TwoFactorEnabled);
        Assert.Equal(Now.AddDays(-20), result.Account.RegisteredAt);
        Assert.Equal(Now.AddMinutes(-10), result.Account.LastActivityAt);
        Assert.Equal(8, result.Account.SuccessfulLogins);
        Assert.Equal("Candidate Example", Assert.IsType<AdminUserCareerProfileViewModel>(result.Profile).FullName);
        Assert.Equal(new AdminUserTotalsViewModel(1, 2, 2, 1), result.Totals);
        Assert.Equal(
            new[] { ApplicationStatus.Applied, ApplicationStatus.Interview },
            result.ApplicationStatusBreakdown.Select(item => item.Status));
        Assert.All(result.ApplicationStatusBreakdown, item => Assert.Equal(1, item.Count));
        Assert.Equal(
            new[] { JobSource.LinkedIn, JobSource.Indeed },
            result.ApplicationSourceBreakdown.Select(item => item.Source));
        Assert.Equal(101, Assert.Single(result.Resumes).Id);
        Assert.Equal(new[] { 202, 201 }, result.Applications.Items.Select(item => item.Id));
        Assert.Equal("Backend resume", result.Applications.Items[0].ResumeVersionName);
        Assert.Equal(601, Assert.Single(result.Imports.Items).Id);
        Assert.Equal(202, result.Imports.Items[0].CreatedApplicationId);
        Assert.Equal(new[] { 302, 301 }, result.LatestAnalyses.Select(item => item.Id));
        Assert.Equal("candidate@gmail.test", Assert.IsType<AdminGmailConnectionViewModel>(result.GmailConnection).EmailAddress);
        Assert.Equal(
            new[] { "application.created", "resume.uploaded" },
            result.RecentEvents.Select(item => item.Name));
    }

    [Fact]
    public async Task Report_never_mixes_cross_user_records_or_cross_user_links()
    {
        const string targetId = "target-user";
        const string otherId = "other-user";
        await using var db = CreateDbContext();
        db.Users.AddRange(
            CreateUser(targetId, "target@example.test"),
            CreateUser(otherId, "other@example.test"));
        db.Resumes.AddRange(
            CreateResume(101, targetId, "Target resume", Now.AddDays(-2), true),
            CreateResume(102, otherId, "Other resume", Now.AddDays(-1), true));
        db.JobApplications.AddRange(
            CreateApplication(201, targetId, 101, "Target company", Now.AddDays(-3)),
            CreateApplication(202, otherId, 102, "Other company", Now.AddDays(-2)),
            CreateApplication(203, targetId, 102, "Cross-linked resume", Now.AddDays(-1)));
        db.GmailConnections.AddRange(
            CreateGmailConnection(301, targetId, "target@gmail.test"),
            CreateGmailConnection(302, otherId, "other@gmail.test"));
        db.ApplicationImports.AddRange(
            CreateImport(401, targetId, 301, 201, "Target import", Now.AddDays(-3)),
            CreateImport(402, otherId, 302, 202, "Other import", Now.AddDays(-2)),
            CreateImport(403, targetId, 301, 202, "Cross-linked application", Now.AddDays(-1)));
        db.ResumeAnalyses.AddRange(
            CreateAnalysis(501, targetId, 101, 201, 80, Now.AddDays(-3)),
            CreateAnalysis(502, otherId, 102, 202, 81, Now.AddDays(-2)),
            CreateAnalysis(503, targetId, 102, 201, 82, Now.AddDays(-1)),
            CreateAnalysis(504, targetId, 101, 202, 83, Now));
        db.Interviews.AddRange(
            CreateInterview(601, targetId, 201, Now.AddDays(-2)),
            CreateInterview(602, otherId, 202, Now.AddDays(-1)),
            CreateInterview(603, targetId, 202, Now));
        db.ProductEvents.AddRange(
            CreateEvent(701, targetId, "target.event", Now.AddDays(-1)),
            CreateEvent(702, otherId, "other.event", Now));
        await db.SaveChangesAsync();

        var result = Assert.IsType<AdminUserReportViewModel>(
            await CreateService(db).LoadAsync(targetId, 1, 1));

        Assert.DoesNotContain(result.Resumes, item => item.Id == 102);
        Assert.DoesNotContain(result.Applications.Items, item => item.Id == 202);
        Assert.DoesNotContain(result.Imports.Items, item => item.Id == 402);
        Assert.DoesNotContain(result.LatestAnalyses, item => item.Id is 502 or 503 or 504);
        Assert.DoesNotContain(result.RecentEvents, item => item.Name == "other.event");

        var crossLinkedApplication = Assert.Single(
            result.Applications.Items,
            item => item.Id == 203);
        Assert.Null(crossLinkedApplication.ResumeId);
        Assert.Null(crossLinkedApplication.ResumeVersionName);
        Assert.Null(Assert.Single(result.Imports.Items, item => item.Id == 403).CreatedApplicationId);
        Assert.Equal(501, Assert.Single(result.LatestAnalyses).Id);
    }

    [Fact]
    public async Task Report_returns_null_for_unknown_configured_admin_and_role_admin_targets()
    {
        const string configuredAdminId = "configured-owner";
        const string roleAdminId = "role-owner";
        const string roleId = "admin-role";
        await using var db = CreateDbContext();
        db.Users.AddRange(
            CreateUser(configuredAdminId, "OWNER@example.test"),
            CreateUser(roleAdminId, "role-owner@example.test"));
        db.Roles.Add(new IdentityRole
        {
            Id = roleId,
            Name = AdminAccess.Role,
            NormalizedName = AdminAccess.Role.ToUpperInvariant()
        });
        db.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = roleAdminId,
            RoleId = roleId
        });
        await db.SaveChangesAsync();
        var service = CreateService(
            db,
            new AdminAccessOptions { Emails = ["owner@example.test"] });

        Assert.Null(await service.LoadAsync("missing-user", 1, 1));
        Assert.Null(await service.LoadAsync(configuredAdminId, 1, 1));
        Assert.Null(await service.LoadAsync(roleAdminId, 1, 1));
    }

    [Fact]
    public async Task Report_paginates_and_orders_each_collection_deterministically()
    {
        const string userId = "paged-user";
        await using var db = CreateDbContext();
        db.Users.Add(CreateUser(userId, "paged@example.test"));
        db.GmailConnections.Add(CreateGmailConnection(300, userId, "paged@gmail.test"));
        for (var index = 0; index < 30; index++)
        {
            db.JobApplications.Add(CreateApplication(
                100 + index,
                userId,
                null,
                $"Company {index:00}",
                Now.AddMinutes(index)));
            db.ApplicationImports.Add(CreateImport(
                200 + index,
                userId,
                300,
                null,
                $"Import {index:00}",
                Now.AddMinutes(index)));
        }
        db.Resumes.AddRange(
            CreateResume(401, userId, "Default old", Now.AddDays(-10), true),
            CreateResume(402, userId, "Non-default old", Now.AddDays(-5), false),
            CreateResume(403, userId, "Non-default new", Now.AddDays(-1), false));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var secondPage = Assert.IsType<AdminUserReportViewModel>(
            await service.LoadAsync(userId, 2, 2));

        Assert.Equal(30, secondPage.Applications.TotalCount);
        Assert.Equal(2, secondPage.Applications.Page);
        Assert.Equal(AdminUserReportViewModel.PageSize, secondPage.Applications.PageSize);
        Assert.Equal(2, secondPage.Applications.TotalPages);
        Assert.True(secondPage.Applications.HasPreviousPage);
        Assert.False(secondPage.Applications.HasNextPage);
        Assert.Equal(new[] { 104, 103, 102, 101, 100 }, secondPage.Applications.Items.Select(item => item.Id));
        Assert.Equal(new[] { 204, 203, 202, 201, 200 }, secondPage.Imports.Items.Select(item => item.Id));
        Assert.Equal(new[] { 401, 403, 402 }, secondPage.Resumes.Select(item => item.Id));

        var normalizedFirstPage = Assert.IsType<AdminUserReportViewModel>(
            await service.LoadAsync(userId, 0, -10));
        Assert.Equal(1, normalizedFirstPage.Applications.Page);
        Assert.Equal(1, normalizedFirstPage.Imports.Page);
        Assert.Equal(129, normalizedFirstPage.Applications.Items[0].Id);
        Assert.Equal(229, normalizedFirstPage.Imports.Items[0].Id);
        Assert.Equal(AdminUserReportViewModel.PageSize, normalizedFirstPage.Applications.Items.Count);
        Assert.True(normalizedFirstPage.Applications.HasNextPage);

        var clampedLastPage = Assert.IsType<AdminUserReportViewModel>(
            await service.LoadAsync(userId, 999, 999));
        Assert.Equal(2, clampedLastPage.Applications.Page);
        Assert.Equal(2, clampedLastPage.Imports.Page);
        Assert.Equal(5, clampedLastPage.Applications.Items.Count);
        Assert.Equal(5, clampedLastPage.Imports.Items.Count);
    }

    [Fact]
    public void Report_view_model_graph_excludes_sensitive_content_and_storage_properties()
    {
        string[] deniedPropertyFragments =
        [
            "PhoneNumber",
            "Notes",
            "Description",
            "CustomFields",
            "MeetingLink",
            "InterviewerName",
            "EmailSubject",
            "ResumeFileName",
            "Token",
            "Path",
            "Text",
            "Hash",
            "Snapshot"
        ];
        var reportTypes = new HashSet<Type>();
        CollectReportTypes(typeof(AdminUserReportViewModel), reportTypes);

        var violations = reportTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => $"{type.Name}.{property.Name}"))
            .Where(propertyName => deniedPropertyFragments.Any(fragment =>
                propertyName[(propertyName.IndexOf('.') + 1)..]
                    .Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(name => name)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Sensitive properties reached the owner report view-model graph: {string.Join(", ", violations)}");
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("admin-user-report-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AdminUserReportService CreateService(
        ApplicationDbContext db,
        AdminAccessOptions? options = null) =>
        new(
            db,
            new FixedTimeProvider(Now),
            Options.Create(options ?? new AdminAccessOptions()));

    private static IdentityUser CreateUser(string id, string email) =>
        new()
        {
            Id = id,
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true
        };

    private static Resume CreateResume(
        int id,
        string userId,
        string versionName,
        DateTimeOffset updatedAt,
        bool isDefault) =>
        new()
        {
            Id = id,
            UserId = userId,
            VersionName = versionName,
            OriginalFileName = $"{versionName}.pdf",
            StoredFileName = $"{id}.pdf",
            FilePath = $"private/{userId}/{id}.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            IsDefault = isDefault,
            UploadedAt = updatedAt.AddDays(-1),
            UpdatedAt = updatedAt
        };

    private static JobApplication CreateApplication(
        int id,
        string userId,
        int? resumeId,
        string company,
        DateTimeOffset updatedAt) =>
        new()
        {
            Id = id,
            UserId = userId,
            ResumeId = resumeId,
            CompanyName = company,
            JobTitle = "Engineer",
            Source = JobSource.Indeed,
            Status = ApplicationStatus.Applied,
            CreatedAt = updatedAt.AddDays(-1),
            UpdatedAt = updatedAt
        };

    private static GmailConnection CreateGmailConnection(
        int id,
        string userId,
        string email) =>
        new()
        {
            Id = id,
            UserId = userId,
            EmailAddress = email,
            ProtectedRefreshToken = "private-token",
            ConnectedAt = Now.AddDays(-10),
            UpdatedAt = Now.AddDays(-1),
            NextSyncAt = Now.AddHours(1)
        };

    private static ApplicationImport CreateImport(
        int id,
        string userId,
        int gmailConnectionId,
        int? applicationId,
        string company,
        DateTimeOffset detectedAt) =>
        new()
        {
            Id = id,
            UserId = userId,
            GmailConnectionId = gmailConnectionId,
            ExternalMessageId = $"message-{id}",
            Direction = ApplicationImportDirection.Incoming,
            Status = ApplicationImportStatus.PendingReview,
            Confidence = 75,
            EmailSubject = "private subject",
            CompanyName = company,
            JobTitle = "Engineer",
            Source = JobSource.Email,
            ResumeFileName = "private-attachment.pdf",
            CreatedApplicationId = applicationId,
            DetectedAt = detectedAt
        };

    private static ResumeAnalysis CreateAnalysis(
        int id,
        string userId,
        int resumeId,
        int? applicationId,
        int score,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = id,
            UserId = userId,
            ResumeId = resumeId,
            JobApplicationId = applicationId,
            AnalysisType = ResumeAnalysisType.SavedApplication,
            MatchScore = score,
            MatchedKeywordsJson = "[]",
            MissingKeywordsJson = "[]",
            SuggestionsJson = "[]",
            ResumeTextSnapshot = "private resume snapshot",
            JobDescriptionSnapshot = "private job snapshot",
            CreatedAt = createdAt
        };

    private static Interview CreateInterview(
        int id,
        string userId,
        int applicationId,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = id,
            UserId = userId,
            JobApplicationId = applicationId,
            InterviewType = InterviewType.TechnicalInterview,
            Status = InterviewStatus.Scheduled,
            ScheduledAt = createdAt.AddDays(2),
            MeetingLink = "https://private.example.test",
            InterviewerName = "Private interviewer",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

    private static ProductEvent CreateEvent(
        long id,
        string userId,
        string name,
        DateTimeOffset occurredAt) =>
        new()
        {
            Id = id,
            UserId = userId,
            Name = name,
            Source = "test",
            Succeeded = true,
            OccurredAt = occurredAt
        };

    private static void CollectReportTypes(Type type, ISet<Type> collected)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
        {
            CollectReportTypes(type.GetElementType()!, collected);
            return;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                CollectReportTypes(argument, collected);
            }
        }

        if (!string.Equals(
                type.Namespace,
                typeof(AdminUserReportViewModel).Namespace,
                StringComparison.Ordinal)
            || !collected.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            CollectReportTypes(property.PropertyType, collected);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
