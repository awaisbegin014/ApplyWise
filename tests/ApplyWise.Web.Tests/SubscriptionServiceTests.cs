using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.Services.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class SubscriptionServiceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-26T08:00:00Z");

    [Fact]
    public async Task Free_account_receives_two_successful_lifetime_ats_reports()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var first = await service.TryReserveAtsAnalysisAsync("user-1", "ats-check", 100);
        Assert.True(first.Allowed);
        await service.CompleteAtsAnalysisAsync(first.UsageRecordId!.Value, true, 200, "test-model");

        var second = await service.TryReserveAtsAnalysisAsync("user-1", "ats-job-match", 100);
        Assert.True(second.Allowed);
        await service.CompleteAtsAnalysisAsync(second.UsageRecordId!.Value, true, 200, "test-model");

        var third = await service.TryReserveAtsAnalysisAsync("user-1", "ats-check", 100);
        Assert.False(third.Allowed);
        Assert.Equal(0, third.Snapshot.AtsAnalysesRemaining);
        Assert.Equal(2, await db.AiUsageRecords.CountAsync(
            item => item.Status == AiUsageStatus.Succeeded));
    }

    [Fact]
    public async Task Failed_ai_call_does_not_consume_the_ats_allowance()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var reservation = await service.TryReserveAtsAnalysisAsync("user-1", "ats-check", 100);
        await service.CompleteAtsAnalysisAsync(reservation.UsageRecordId!.Value, false, 0, "test-model");

        var snapshot = await service.GetSnapshotAsync("user-1");
        Assert.Equal(2, snapshot.AtsAnalysesRemaining);
        Assert.Equal(0, snapshot.AtsAnalysisUsed);
        Assert.Equal(AiUsageStatus.Failed, (await db.AiUsageRecords.SingleAsync()).Status);
    }

    [Fact]
    public async Task Free_account_can_export_two_resumes_then_requires_pro()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        Assert.True((await service.TryConsumeResumeBuildAsync("user-1", "classic")).Allowed);
        var second = await service.TryConsumeResumeBuildAsync("user-1", "modern");
        Assert.True(second.Allowed);
        Assert.Equal(0, second.Snapshot.ResumeBuildsRemaining);

        var third = await service.TryConsumeResumeBuildAsync("user-1", "compact");
        Assert.False(third.Allowed);
        Assert.Equal(2, await db.ResumeBuildUsageRecords.CountAsync());
    }

    [Fact]
    public async Task Owner_approval_activates_pro_for_the_configured_duration()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var submitted = await service.SubmitUpgradeRequestAsync(
            "user-1",
            new UpgradeSubmission("Bank transfer", "TX-12345", "Taylor", 500m, null));

        Assert.True(submitted.Succeeded);
        Assert.True(await service.ApproveUpgradeRequestAsync(
            submitted.RequestId!.Value,
            "owner-1",
            "Verified"));

        var snapshot = await service.GetSnapshotAsync("user-1");
        Assert.True(snapshot.IsPro);
        Assert.Equal(100, snapshot.AtsAnalysisLimit);
        Assert.True(snapshot.HasUnlimitedResumeBuilds);
        Assert.Equal(Now.AddDays(30), snapshot.ProExpiresAt);
        var request = await db.ProUpgradeRequests.SingleAsync();
        Assert.Equal(ProUpgradeRequestStatus.Approved, request.Status);
        Assert.Equal("Verified", request.AdminNote);
    }

    [Fact]
    public async Task Account_can_have_only_one_pending_upgrade_request()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var first = await service.SubmitUpgradeRequestAsync(
            "user-1",
            new UpgradeSubmission("Easypaisa", "EP-100", "Taylor", 500m, null));
        var second = await service.SubmitUpgradeRequestAsync(
            "user-1",
            new UpgradeSubmission("Easypaisa", "EP-101", "Taylor", 500m, null));

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Single(await db.ProUpgradeRequests.ToListAsync());
    }

    private static ApplicationDbContext CreateDb()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("subscriptions-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(dbOptions);
    }

    private static SubscriptionService CreateService(ApplicationDbContext db) =>
        new(
            db,
            new WorkspaceQuotaGate(db),
            Options.Create(new SubscriptionOptions
            {
                FreeAtsAnalysisLimit = 2,
                ProAtsAnalysisLimit = 100,
                FreeResumeBuildLimit = 2,
                ProDurationDays = 30,
                ProPrice = 500m,
                Currency = "PKR"
            }),
            new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
