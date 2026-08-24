using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class WorkspaceQuotaTests
{
    [Fact]
    public async Task Application_quota_is_enforced_per_user()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("workspace-quota-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        db.JobApplications.Add(new JobApplication
        {
            UserId = "owner",
            CompanyName = "Contoso",
            JobTitle = "Engineer",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var quotas = new WorkspaceQuotaService(db, Options.Create(new WorkspaceQuotaOptions
        {
            MaxApplicationsPerUser = 1
        }));

        Assert.False(await quotas.CanCreateApplicationAsync("owner"));
        Assert.True(await quotas.CanCreateApplicationAsync("different-user"));
    }

    [Fact]
    public async Task Analysis_quota_counts_snapshot_bytes_per_user()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("analysis-byte-quota-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        db.ResumeAnalyses.Add(new ResumeAnalysis
        {
            UserId = "owner",
            ResumeId = 1,
            AnalysisType = ResumeAnalysisType.PastedRequirements,
            MatchedKeywordsJson = "[]",
            MissingKeywordsJson = "[]",
            SuggestionsJson = "[]",
            ResumeTextSnapshot = string.Empty,
            JobDescriptionSnapshot = "existing",
            SnapshotSizeBytes = 80,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var quotas = new WorkspaceQuotaService(db, Options.Create(new WorkspaceQuotaOptions
        {
            MaxAnalysesPerUser = 10,
            MaxAnalysisSnapshotBytesPerUser = 100
        }));

        Assert.False(await quotas.CanCreateAnalysisAsync("owner", 21));
        Assert.True(await quotas.CanCreateAnalysisAsync("owner", 20));
        Assert.True(await quotas.CanCreateAnalysisAsync("different-user", 100));
    }

    [Fact]
    public async Task Analysis_batch_quota_checks_the_whole_pending_batch()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("analysis-batch-quota-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        db.ResumeAnalyses.Add(new ResumeAnalysis
        {
            UserId = "owner",
            ResumeId = 1,
            AnalysisType = ResumeAnalysisType.PastedRequirements,
            MatchedKeywordsJson = "[]",
            MissingKeywordsJson = "[]",
            SuggestionsJson = "[]",
            ResumeTextSnapshot = string.Empty,
            JobDescriptionSnapshot = "existing",
            SnapshotSizeBytes = 40,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var quotas = new WorkspaceQuotaService(db, Options.Create(new WorkspaceQuotaOptions
        {
            MaxAnalysesPerUser = 2,
            MaxAnalysisSnapshotBytesPerUser = 100
        }));

        Assert.False(await quotas.CanCreateAnalysesAsync("owner", 2, 20));
        Assert.False(await quotas.CanCreateAnalysesAsync("owner", 1, 61));
        Assert.True(await quotas.CanCreateAnalysesAsync("owner", 1, 60));
    }

    [Fact]
    public async Task Application_lock_serializes_the_same_resource_across_contexts()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = "operation-lock-" + Guid.NewGuid().ToString("N");
        var firstOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        var secondOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        await using var firstDb = new ApplicationDbContext(firstOptions);
        await using var secondDb = new ApplicationDbContext(secondOptions);
        var firstProvider = new ApplicationLockProvider(firstDb);
        var secondProvider = new ApplicationLockProvider(secondDb);

        var firstLease = Assert.IsAssignableFrom<IAsyncDisposable>(
            await firstProvider.TryAcquireAsync("shared-resource", TimeSpan.Zero));
        var secondLeaseTask = secondProvider.TryAcquireAsync(
            "shared-resource",
            Timeout.InfiniteTimeSpan);
        await Task.Delay(25);
        Assert.False(secondLeaseTask.IsCompleted);

        await firstLease.DisposeAsync();
        var secondLease = Assert.IsAssignableFrom<IAsyncDisposable>(
            await secondLeaseTask.WaitAsync(TimeSpan.FromSeconds(1)));
        await secondLease.DisposeAsync();
    }

    [Fact]
    public async Task Quota_gate_makes_the_final_count_and_write_atomic_across_contexts()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = "quota-gate-race-" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        await using var firstDb = new ApplicationDbContext(options);
        await using var secondDb = new ApplicationDbContext(options);
        var limits = Options.Create(new WorkspaceQuotaOptions { MaxApplicationsPerUser = 1 });
        var firstQuotas = new WorkspaceQuotaService(firstDb, limits);
        var secondQuotas = new WorkspaceQuotaService(secondDb, limits);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> TryCreateAsync(
            ApplicationDbContext db,
            IWorkspaceQuotaService quotaService,
            string company)
        {
            await start.Task;
            return await new WorkspaceQuotaGate(db).RunAsync(
                WorkspaceQuotaResources.Applications,
                "owner",
                async cancellationToken =>
                {
                    if (!await quotaService.CanCreateApplicationAsync("owner", cancellationToken))
                    {
                        return false;
                    }

                    db.JobApplications.Add(new JobApplication
                    {
                        UserId = "owner",
                        CompanyName = company,
                        JobTitle = "Engineer",
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    });
                    await Task.Delay(20, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                    return true;
                });
        }

        var first = TryCreateAsync(firstDb, firstQuotas, "First");
        var second = TryCreateAsync(secondDb, secondQuotas, "Second");
        start.SetResult();
        var outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, value => value);
        Assert.Single(await firstDb.JobApplications.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Sql_application_lock_serializes_with_retrying_execution_enabled_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "APPLYWISE_SQL_INTEGRATION_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var firstOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        var secondOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        await using var firstDb = new ApplicationDbContext(firstOptions);
        await using var secondDb = new ApplicationDbContext(secondOptions);
        var firstProvider = new ApplicationLockProvider(firstDb);
        var secondProvider = new ApplicationLockProvider(secondDb);
        var resource = "sql-lock-test-" + Guid.NewGuid().ToString("N");

        var firstLease = Assert.IsAssignableFrom<IAsyncDisposable>(
            await firstProvider.TryAcquireAsync(resource, TimeSpan.Zero));
        Assert.Null(await secondProvider.TryAcquireAsync(resource, TimeSpan.Zero));

        await firstLease.DisposeAsync();
        var secondLease = Assert.IsAssignableFrom<IAsyncDisposable>(
            await secondProvider.TryAcquireAsync(resource, TimeSpan.FromSeconds(2)));
        await secondLease.DisposeAsync();
    }

    [Fact]
    public async Task Sql_quota_gate_saves_inside_its_transaction_with_retrying_execution_enabled_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "APPLYWISE_SQL_INTEGRATION_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var marker = "quota-gate-sql-" + Guid.NewGuid().ToString("N");
        var gate = new WorkspaceQuotaGate(db);

        var messageId = await gate.RunAsync(
            "sql-save-integration",
            marker,
            async cancellationToken =>
            {
                var message = new ContactMessage
                {
                    FullName = "Quota gate integration test",
                    Email = "quota-gate@example.test",
                    Topic = ContactTopic.TechnicalSupport,
                    Subject = marker,
                    Body = "Verifies an EF save under a transaction-owned SQL application lock.",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.ContactMessages.Add(message);
                await db.SaveChangesAsync(cancellationToken);
                return message.Id;
            });

        Assert.True(messageId > 0);
        var saved = await db.ContactMessages.SingleAsync(item => item.Id == messageId);
        db.ContactMessages.Remove(saved);
        await db.SaveChangesAsync();
    }
}
