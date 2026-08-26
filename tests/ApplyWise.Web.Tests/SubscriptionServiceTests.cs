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
    public async Task Free_account_receives_two_successful_lifetime_ai_trials()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var first = await service.TryReserveAiUseAsync("user-1", "bullet", 100);
        Assert.True(first.Allowed);
        await service.CompleteAiUseAsync(first.UsageRecordId!.Value, true, 200, "test-model");

        var second = await service.TryReserveAiUseAsync("user-1", "bullet", 100);
        Assert.True(second.Allowed);
        await service.CompleteAiUseAsync(second.UsageRecordId!.Value, true, 200, "test-model");

        var third = await service.TryReserveAiUseAsync("user-1", "bullet", 100);
        Assert.False(third.Allowed);
        Assert.Equal(0, third.Snapshot.AiRemaining);
        Assert.Equal(2, await db.AiUsageRecords.CountAsync(
            item => item.Status == AiUsageStatus.Succeeded));
    }

    [Fact]
    public async Task Failed_ai_call_does_not_consume_the_allowance()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var reservation = await service.TryReserveAiUseAsync("user-1", "bullet", 100);
        await service.CompleteAiUseAsync(reservation.UsageRecordId!.Value, false, 0, "test-model");

        var snapshot = await service.GetSnapshotAsync("user-1");
        Assert.Equal(2, snapshot.AiRemaining);
        Assert.Equal(0, snapshot.AiUsed);
        Assert.Equal(AiUsageStatus.Failed, (await db.AiUsageRecords.SingleAsync()).Status);
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
        Assert.Equal(100, snapshot.AiLimit);
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
                FreeAiTrialLimit = 2,
                ProMonthlyAiLimit = 100,
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
