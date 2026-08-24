using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Gmail;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ApplicationImportProcessorTests
{
    private const string UserId = "candidate-user";
    private const string OtherUserId = "other-candidate";

    [Theory]
    [InlineData(false, 95, ApplicationImportDirection.Incoming, "Contoso", "Platform Engineer", true)]
    [InlineData(true, 89, ApplicationImportDirection.Incoming, "Contoso", "Platform Engineer", true)]
    [InlineData(true, 99, ApplicationImportDirection.Outgoing, "Contoso", "Platform Engineer", true)]
    [InlineData(true, 99, ApplicationImportDirection.Incoming, "   ", "Platform Engineer", true)]
    [InlineData(true, 99, ApplicationImportDirection.Incoming, "Contoso", "   ", true)]
    [InlineData(true, 99, ApplicationImportDirection.Incoming, "Contoso", "Platform Engineer", false)]
    public async Task TryAutoAccept_IneligibleImport_RemainsPending(
        bool preferenceEnabled,
        int confidence,
        ApplicationImportDirection direction,
        string companyName,
        string jobTitle,
        bool hasAppliedDate)
    {
        await using var db = CreateContext();
        var connection = await SeedConnectionAsync(
            db,
            UserId,
            preferenceEnabled);
        var import = await SeedImportAsync(
            db,
            connection,
            confidence: confidence,
            direction: direction,
            companyName: companyName,
            jobTitle: jobTitle,
            hasAppliedDate: hasAppliedDate);

        var result = await CreateProcessor(db).TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);

        Assert.Equal(ApplicationImportProcessOutcome.NotEligible, result.Outcome);
        Assert.Empty(await db.JobApplications.ToListAsync());
        var storedImport = await db.ApplicationImports.SingleAsync();
        Assert.Equal(ApplicationImportStatus.PendingReview, storedImport.Status);
        Assert.Null(storedImport.CreatedApplicationId);
        Assert.Null(storedImport.ReviewedAt);
    }

    [Fact]
    public async Task TryAutoAccept_AtThreshold_CreatesAppliedApplication()
    {
        await using var db = CreateContext();
        var connection = await SeedConnectionAsync(db, UserId, autoAdd: true);
        var import = await SeedImportAsync(
            db,
            connection,
            confidence: ApplicationImportPolicy.HighConfidenceThreshold,
            companyName: "  Contoso  ",
            jobTitle: "  Platform Engineer  ",
            jobLocation: "  Islamabad  ",
            jobUrl: "https://jobs.contoso.test/roles/42?utm_source=gmail",
            appliedDate: new DateOnly(2026, 7, 28));

        var result = await CreateProcessor(db).TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);

        Assert.Equal(ApplicationImportProcessOutcome.Created, result.Outcome);
        var application = await db.JobApplications.SingleAsync();
        Assert.Equal(UserId, application.UserId);
        Assert.Equal("Contoso", application.CompanyName);
        Assert.Equal("Platform Engineer", application.JobTitle);
        Assert.Equal("Islamabad", application.JobLocation);
        Assert.Equal(ApplicationStatus.Applied, application.Status);
        Assert.Equal(new DateOnly(2026, 7, 28), application.AppliedDate);
        Assert.Equal(JobSource.LinkedIn, application.Source);
        Assert.Contains("Automatically imported", application.Notes);
        Assert.Equal(TimeSpan.Zero, application.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, application.UpdatedAt.Offset);

        var storedImport = await db.ApplicationImports.SingleAsync();
        Assert.Equal(ApplicationImportStatus.AutoAccepted, storedImport.Status);
        Assert.Equal(application.Id, storedImport.CreatedApplicationId);
        Assert.NotNull(storedImport.ReviewedAt);
    }

    [Fact]
    public async Task TryAutoAccept_RevocationStartsAtQuotaGate_RemainsPending()
    {
        await using var db = CreateContext();
        var connection = await SeedConnectionAsync(db, UserId, autoAdd: true);
        var import = await SeedImportAsync(db, connection);
        var quotaGate = new BeforeActionQuotaGate(async cancellationToken =>
        {
            var currentConnection = await db.GmailConnections.SingleAsync(
                item => item.Id == connection.Id,
                cancellationToken);
            currentConnection.LastErrorCode = GmailConnectionStates.RevocationPending;
            currentConnection.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        });
        var processor = new ApplicationImportProcessor(
            db,
            NullLogger<ApplicationImportProcessor>.Instance,
            quotaGate: quotaGate);

        var result = await processor.TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);

        Assert.Equal(ApplicationImportProcessOutcome.NotEligible, result.Outcome);
        Assert.Empty(await db.JobApplications.ToListAsync());
        var storedImport = await db.ApplicationImports.SingleAsync();
        Assert.Equal(ApplicationImportStatus.PendingReview, storedImport.Status);
        Assert.Null(storedImport.CreatedApplicationId);
    }

    [Fact]
    public async Task TryAutoAccept_NormalizedCompanyTitleAndDate_LinksExistingApplication()
    {
        await using var db = CreateContext();
        var connection = await SeedConnectionAsync(db, UserId, autoAdd: true);
        var existing = await SeedApplicationAsync(
            db,
            UserId,
            companyName: "CONTOSO",
            jobTitle: "Senior   Data Engineer",
            appliedDate: new DateOnly(2026, 7, 28));
        var import = await SeedImportAsync(
            db,
            connection,
            companyName: " contoso ",
            jobTitle: " senior data engineer ",
            appliedDate: new DateOnly(2026, 7, 28));

        var result = await CreateProcessor(db).TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);

        Assert.Equal(ApplicationImportProcessOutcome.LinkedExisting, result.Outcome);
        Assert.Equal(existing.Id, result.ApplicationId);
        Assert.Single(await db.JobApplications.ToListAsync());
        var storedImport = await db.ApplicationImports.SingleAsync();
        Assert.Equal(ApplicationImportStatus.AutoAccepted, storedImport.Status);
        Assert.Equal(existing.Id, storedImport.CreatedApplicationId);
    }

    [Fact]
    public async Task TryAutoAccept_CanonicalUrlIgnoresFragmentAndTrackingParameters()
    {
        await using var db = CreateContext();
        var connection = await SeedConnectionAsync(db, UserId, autoAdd: true);
        var existing = await SeedApplicationAsync(
            db,
            UserId,
            companyName: "Northwind",
            jobTitle: "Cloud Engineer",
            appliedDate: new DateOnly(2026, 7, 20),
            jobUrl:
                "https://careers.northwind.test/jobs/ABC-123?jobId=ABC-123&utm_source=board#description");
        var import = await SeedImportAsync(
            db,
            connection,
            companyName: "Different Company Text",
            jobTitle: "Different Role Text",
            jobUrl:
                "https://CAREERS.northwind.test/jobs/ABC-123/?utm_medium=email&jobId=ABC-123",
            appliedDate: new DateOnly(2026, 7, 28));

        var result = await CreateProcessor(db).TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);

        Assert.Equal(ApplicationImportProcessOutcome.LinkedExisting, result.Outcome);
        Assert.Equal(existing.Id, result.ApplicationId);
        Assert.Single(await db.JobApplications.ToListAsync());
    }

    [Fact]
    public async Task TryAutoAccept_DifferentLegitimateJobIdentifiers_DoNotDeduplicate()
    {
        await using var db = CreateContext();
        var connection = await SeedConnectionAsync(db, UserId, autoAdd: true);
        await SeedApplicationAsync(
            db,
            UserId,
            companyName: "Northwind",
            jobTitle: "Cloud Engineer",
            appliedDate: new DateOnly(2026, 7, 20),
            jobUrl: "https://careers.northwind.test/opening?jobId=ABC-123");
        var import = await SeedImportAsync(
            db,
            connection,
            companyName: "Northwind",
            jobTitle: "Cloud Engineer II",
            jobUrl: "https://careers.northwind.test/opening?jobId=XYZ-987",
            appliedDate: new DateOnly(2026, 7, 28));

        var result = await CreateProcessor(db).TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);

        Assert.Equal(ApplicationImportProcessOutcome.Created, result.Outcome);
        Assert.Equal(2, await db.JobApplications.CountAsync());
    }

    [Fact]
    public async Task TryAutoAccept_ReprocessingSameImport_DoesNotCreateDuplicate()
    {
        await using var db = CreateContext();
        var connection = await SeedConnectionAsync(db, UserId, autoAdd: true);
        var import = await SeedImportAsync(db, connection);
        var processor = CreateProcessor(db);

        var first = await processor.TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);
        var second = await processor.TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);

        Assert.Equal(ApplicationImportProcessOutcome.Created, first.Outcome);
        Assert.Equal(ApplicationImportProcessOutcome.AlreadyProcessed, second.Outcome);
        Assert.Equal(first.ApplicationId, second.ApplicationId);
        Assert.Single(await db.JobApplications.ToListAsync());
    }

    [Fact]
    public async Task TryAutoAccept_AttachmentFileName_SelectsMatchingOwnedResume()
    {
        await using var db = CreateContext();
        var connection = await SeedConnectionAsync(db, UserId, autoAdd: true);
        var defaultResume = await SeedResumeAsync(
            db,
            UserId,
            "Default Resume.pdf",
            isDefault: true,
            uploadedAt: DateTimeOffset.UtcNow.AddDays(-2));
        var matchingResume = await SeedResumeAsync(
            db,
            UserId,
            "Backend Resume.pdf",
            isDefault: false,
            uploadedAt: DateTimeOffset.UtcNow);
        var import = await SeedImportAsync(
            db,
            connection,
            resumeFileName: "backend resume.PDF");

        await CreateProcessor(db).TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);

        var application = await db.JobApplications.SingleAsync();
        Assert.Equal(matchingResume.Id, application.ResumeId);
        Assert.NotEqual(defaultResume.Id, application.ResumeId);
    }

    [Fact]
    public async Task TryAutoAccept_NoAttachmentMatch_UsesOwnedDefaultResume()
    {
        await using var db = CreateContext();
        var connection = await SeedConnectionAsync(db, UserId, autoAdd: true);
        var defaultResume = await SeedResumeAsync(
            db,
            UserId,
            "Default Resume.pdf",
            isDefault: true);
        var import = await SeedImportAsync(
            db,
            connection,
            resumeFileName: "Unknown Resume.pdf");

        await CreateProcessor(db).TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);

        Assert.Equal(
            defaultResume.Id,
            (await db.JobApplications.SingleAsync()).ResumeId);
    }

    [Fact]
    public async Task TryAutoAccept_NoResumeAvailable_LeavesResumeEmpty()
    {
        await using var db = CreateContext();
        var connection = await SeedConnectionAsync(db, UserId, autoAdd: true);
        var import = await SeedImportAsync(
            db,
            connection,
            resumeFileName: "Unknown Resume.pdf");

        await CreateProcessor(db).TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);

        Assert.Null((await db.JobApplications.SingleAsync()).ResumeId);
    }

    [Fact]
    public async Task TryAutoAccept_NeverLinksAnotherUsersApplicationOrResume()
    {
        await using var db = CreateContext();
        await SeedUserAsync(db, OtherUserId);
        var connection = await SeedConnectionAsync(db, UserId, autoAdd: true);
        var otherResume = await SeedResumeAsync(
            db,
            OtherUserId,
            "Candidate Resume.pdf",
            isDefault: true);
        var otherApplication = await SeedApplicationAsync(
            db,
            OtherUserId,
            companyName: "Contoso",
            jobTitle: "Platform Engineer",
            appliedDate: new DateOnly(2026, 7, 28),
            jobUrl: "https://jobs.contoso.test/roles/42");
        var import = await SeedImportAsync(
            db,
            connection,
            jobUrl: "https://jobs.contoso.test/roles/42#apply",
            resumeFileName: "Candidate Resume.pdf");

        var result = await CreateProcessor(db).TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);

        Assert.Equal(ApplicationImportProcessOutcome.Created, result.Outcome);
        var ownedApplication = await db.JobApplications.SingleAsync(
            application => application.UserId == UserId);
        Assert.NotEqual(otherApplication.Id, ownedApplication.Id);
        Assert.NotEqual(otherResume.Id, ownedApplication.ResumeId);
        Assert.Null(ownedApplication.ResumeId);
    }

    [Fact]
    public async Task TryAutoAccept_ForeignExistingLink_IsRejected()
    {
        await using var db = CreateContext();
        await SeedUserAsync(db, OtherUserId);
        var connection = await SeedConnectionAsync(db, UserId, autoAdd: true);
        var otherApplication = await SeedApplicationAsync(
            db,
            OtherUserId,
            companyName: "Fabrikam",
            jobTitle: "Security Analyst",
            appliedDate: new DateOnly(2026, 7, 28));
        var import = await SeedImportAsync(db, connection);
        import.CreatedApplicationId = otherApplication.Id;
        await db.SaveChangesAsync();

        var result = await CreateProcessor(db).TryAutoAcceptAsync(
            import.Id,
            UserId,
            CancellationToken.None);

        Assert.Equal(ApplicationImportProcessOutcome.OwnershipConflict, result.Outcome);
        Assert.Single(await db.JobApplications.ToListAsync());
        Assert.Equal(
            ApplicationImportStatus.PendingReview,
            (await db.ApplicationImports.SingleAsync()).Status);
    }

    [Fact]
    public async Task AcceptManually_CreatesApplicationAndUsesAcceptedStatus()
    {
        await using var db = CreateContext();
        var connection = await SeedConnectionAsync(db, UserId, autoAdd: false);
        var import = await SeedImportAsync(
            db,
            connection,
            confidence: 50,
            direction: ApplicationImportDirection.Outgoing,
            companyName: "Unclear",
            jobTitle: "Unclear");

        var result = await CreateProcessor(db).AcceptManuallyAsync(
            import.Id,
            UserId,
            new ManualApplicationImportData(
                "Fabrikam",
                "Security Analyst",
                "Lahore",
                JobSource.CompanyWebsite,
                "https://careers.fabrikam.test/jobs/7",
                new DateOnly(2026, 7, 27)),
            CancellationToken.None);

        Assert.Equal(ApplicationImportProcessOutcome.Created, result.Outcome);
        var application = await db.JobApplications.SingleAsync();
        Assert.Equal("Fabrikam", application.CompanyName);
        Assert.Equal("Security Analyst", application.JobTitle);
        Assert.Equal(ApplicationStatus.Applied, application.Status);
        Assert.Contains("Imported from a connected Gmail account", application.Notes);
        var storedImport = await db.ApplicationImports.SingleAsync();
        Assert.Equal(ApplicationImportStatus.Accepted, storedImport.Status);
        Assert.Equal(application.Id, storedImport.CreatedApplicationId);
    }

    [Fact]
    public void ApplicationImportStatus_NumericValuesRemainCompatible()
    {
        Assert.Equal(1, (int)ApplicationImportStatus.PendingReview);
        Assert.Equal(2, (int)ApplicationImportStatus.Accepted);
        Assert.Equal(3, (int)ApplicationImportStatus.Dismissed);
        Assert.Equal(4, (int)ApplicationImportStatus.AutoAccepted);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(
                "application-import-processor-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ApplicationImportProcessor CreateProcessor(
        ApplicationDbContext db) =>
        new(db, NullLogger<ApplicationImportProcessor>.Instance);

    private sealed class BeforeActionQuotaGate(
        Func<CancellationToken, Task> beforeAction) : IWorkspaceQuotaGate
    {
        public async Task<TResult> RunAsync<TResult>(
            string resource,
            string userId,
            Func<CancellationToken, Task<TResult>> action,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(WorkspaceQuotaResources.Applications, resource);
            Assert.Equal(UserId, userId);
            await beforeAction(cancellationToken);
            return await action(cancellationToken);
        }
    }

    private static async Task<GmailConnection> SeedConnectionAsync(
        ApplicationDbContext db,
        string userId,
        bool autoAdd)
    {
        if (!await db.Users.AnyAsync(user => user.Id == userId))
        {
            await SeedUserAsync(db, userId);
        }

        var now = DateTimeOffset.UtcNow;
        var connection = new GmailConnection
        {
            UserId = userId,
            EmailAddress = $"{userId}@example.test",
            ProtectedRefreshToken = "protected-test-token",
            ConnectedAt = now,
            UpdatedAt = now,
            NextSyncAt = now,
            AutoAddHighConfidenceApplications = autoAdd
        };
        db.GmailConnections.Add(connection);
        await db.SaveChangesAsync();
        return connection;
    }

    private static async Task SeedUserAsync(
        ApplicationDbContext db,
        string userId)
    {
        db.Users.Add(new IdentityUser
        {
            Id = userId,
            UserName = $"{userId}@example.test",
            NormalizedUserName = $"{userId}@example.test".ToUpperInvariant(),
            Email = $"{userId}@example.test",
            NormalizedEmail = $"{userId}@example.test".ToUpperInvariant()
        });
        await db.SaveChangesAsync();
    }

    private static async Task<ApplicationImport> SeedImportAsync(
        ApplicationDbContext db,
        GmailConnection connection,
        int confidence = 95,
        ApplicationImportDirection direction =
            ApplicationImportDirection.Incoming,
        string companyName = "Contoso",
        string jobTitle = "Platform Engineer",
        string? jobLocation = null,
        string? jobUrl = null,
        DateOnly? appliedDate = null,
        bool hasAppliedDate = true,
        string? resumeFileName = null)
    {
        var import = new ApplicationImport
        {
            UserId = connection.UserId,
            GmailConnectionId = connection.Id,
            ExternalMessageId = "message-" + Guid.NewGuid().ToString("N"),
            ExternalThreadId = "thread-1",
            Direction = direction,
            Status = ApplicationImportStatus.PendingReview,
            Confidence = confidence,
            EmailSubject = "Application confirmation",
            SenderDomain = "jobs.example.test",
            CompanyName = companyName,
            JobTitle = jobTitle,
            JobLocation = jobLocation,
            Source = JobSource.LinkedIn,
            JobUrl = jobUrl,
            AppliedDate = hasAppliedDate
                ? appliedDate ?? new DateOnly(2026, 7, 28)
                : null,
            ResumeFileName = resumeFileName,
            DetectedAt = DateTimeOffset.UtcNow
        };
        db.ApplicationImports.Add(import);
        await db.SaveChangesAsync();
        return import;
    }

    private static async Task<JobApplication> SeedApplicationAsync(
        ApplicationDbContext db,
        string userId,
        string companyName,
        string jobTitle,
        DateOnly? appliedDate,
        string? jobUrl = null)
    {
        var now = DateTimeOffset.UtcNow.AddDays(-1);
        var application = new JobApplication
        {
            UserId = userId,
            CompanyName = companyName,
            JobTitle = jobTitle,
            Source = JobSource.CompanyWebsite,
            JobUrl = jobUrl,
            Status = ApplicationStatus.Applied,
            AppliedDate = appliedDate,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.JobApplications.Add(application);
        await db.SaveChangesAsync();
        return application;
    }

    private static async Task<Resume> SeedResumeAsync(
        ApplicationDbContext db,
        string userId,
        string originalFileName,
        bool isDefault,
        DateTimeOffset? uploadedAt = null)
    {
        var timestamp = uploadedAt ?? DateTimeOffset.UtcNow;
        var resume = new Resume
        {
            UserId = userId,
            VersionName = Path.GetFileNameWithoutExtension(originalFileName),
            OriginalFileName = originalFileName,
            StoredFileName = Guid.NewGuid().ToString("N") + ".pdf",
            FilePath = "test/" + Guid.NewGuid().ToString("N") + ".pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            IsDefault = isDefault,
            UploadedAt = timestamp,
            UpdatedAt = timestamp
        };
        db.Resumes.Add(resume);
        await db.SaveChangesAsync();
        return resume;
    }
}
