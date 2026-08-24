using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.Services.Email;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApplyWise.Web.Tests;

public class AccountSecurityCodeServiceTests
{
    [Fact]
    public async Task IssuedCode_IsSixDigitsHashedAndSingleUse()
    {
        await using var db = CreateContext();
        var emailSender = new CapturingEmailSender();
        var service = CreateService(db, emailSender);

        var issued = await service.IssueAsync(
            "user-1",
            "candidate@example.com",
            AccountSecurityAction.ConfirmEmail);

        Assert.True(issued.Succeeded);
        var delivery = Assert.Single(emailSender.Deliveries);
        Assert.Equal("candidate@example.com", delivery.Email);
        Assert.Equal(AccountSecurityAction.ConfirmEmail, delivery.Action);
        Assert.Matches(@"^\d{6}$", delivery.Code);

        var stored = Assert.Single(await db.AccountSecurityCodes.ToListAsync());
        Assert.True(stored.ProtectedCode.Length > 32);
        Assert.DoesNotContain(delivery.Code, Convert.ToHexString(stored.ProtectedCode));

        var verification = await service.VerifyAsync(
            "user-1",
            AccountSecurityAction.ConfirmEmail,
            delivery.Code);

        Assert.True(verification.Succeeded);
        Assert.NotNull(verification.CodeId);

        var reused = await service.VerifyAsync(
            "user-1",
            AccountSecurityAction.ConfirmEmail,
            delivery.Code);
        Assert.False(reused.Succeeded);
        await service.ConsumeAsync(verification.CodeId!.Value);
    }

    [Fact]
    public async Task IssueAsync_EnforcesTheResendCooldown()
    {
        await using var db = CreateContext();
        var emailSender = new CapturingEmailSender();
        var service = CreateService(db, emailSender);

        var first = await service.IssueAsync(
            "user-2",
            "candidate@example.com",
            AccountSecurityAction.ResetPassword);
        var second = await service.IssueAsync(
            "user-2",
            "candidate@example.com",
            AccountSecurityAction.ResetPassword);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Contains("wait one minute", second.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(emailSender.Deliveries);
    }

    [Fact]
    public async Task VerifyAsync_LocksTheCodeAfterFiveWrongAttempts()
    {
        await using var db = CreateContext();
        var emailSender = new CapturingEmailSender();
        var service = CreateService(db, emailSender);
        await service.IssueAsync(
            "user-3",
            "candidate@example.com",
            AccountSecurityAction.ResetPassword);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var result = await service.VerifyAsync(
                "user-3",
                AccountSecurityAction.ResetPassword,
                "000000");
            Assert.False(result.Succeeded);
        }

        var delivery = Assert.Single(emailSender.Deliveries);
        var correctAfterLockout = await service.VerifyAsync(
            "user-3",
            AccountSecurityAction.ResetPassword,
            delivery.Code);

        Assert.False(correctAfterLockout.Succeeded);
        Assert.Contains("Too many", correctAfterLockout.Message);
    }

    [Fact]
    public async Task ReissuingACode_DoesNotResetTheRollingAttemptBudget()
    {
        await using var db = CreateContext();
        var emailSender = new CapturingEmailSender();
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        var service = CreateService(db, emailSender, timeProvider: timeProvider);

        Assert.True((await service.IssueAsync(
            "rolling-user",
            "candidate@example.com",
            AccountSecurityAction.ResetPassword)).Succeeded);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.False((await service.VerifyAsync(
                "rolling-user",
                AccountSecurityAction.ResetPassword,
                "000000")).Succeeded);
        }

        timeProvider.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(1)));
        Assert.True((await service.IssueAsync(
            "rolling-user",
            "candidate@example.com",
            AccountSecurityAction.ResetPassword)).Succeeded);
        var active = await db.AccountSecurityCodes.SingleAsync(code => code.ConsumedAt == null);
        Assert.Equal(3, active.FailedAttemptCount);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.False((await service.VerifyAsync(
                "rolling-user",
                AccountSecurityAction.ResetPassword,
                "000000")).Succeeded);
        }

        timeProvider.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(1)));
        var lockedIssue = await service.IssueAsync(
            "rolling-user",
            "candidate@example.com",
            AccountSecurityAction.ResetPassword);
        Assert.False(lockedIssue.Succeeded);
        Assert.Contains("Too many", lockedIssue.Message, StringComparison.OrdinalIgnoreCase);

        timeProvider.Advance(TimeSpan.FromMinutes(15).Add(TimeSpan.FromSeconds(1)));
        Assert.True((await service.IssueAsync(
            "rolling-user",
            "candidate@example.com",
            AccountSecurityAction.ResetPassword)).Succeeded);
        active = await db.AccountSecurityCodes
            .Where(code => code.ConsumedAt == null)
            .OrderByDescending(code => code.CreatedAt)
            .FirstAsync();
        Assert.Equal(0, active.FailedAttemptCount);
    }

    [Fact]
    public async Task RetentionCleanup_DeletesExpiredHistoryInBoundedBatches()
    {
        await using var db = CreateContext();
        var now = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        for (var index = 0; index <= AccountSecurityCodeRetention.MaximumBatchSize; index++)
        {
            db.AccountSecurityCodes.Add(CreateStoredCode(
                "obsolete-user-" + index,
                now.AddHours(-3),
                now.AddHours(-2),
                index % 2 == 0 ? now.AddHours(-2) : null));
        }

        db.AccountSecurityCodes.Add(CreateStoredCode(
            "current-user",
            now.AddMinutes(-1),
            now.AddMinutes(9),
            consumedAt: null));
        await db.SaveChangesAsync();
        var retention = new AccountSecurityCodeRetention(db, timeProvider);

        Assert.Equal(
            AccountSecurityCodeRetention.MaximumBatchSize,
            await retention.DeleteObsoleteBatchAsync());
        Assert.Equal(1, await retention.DeleteObsoleteBatchAsync());
        Assert.Equal(0, await retention.DeleteObsoleteBatchAsync());
        Assert.Equal(
            "current-user",
            Assert.Single(await db.AccountSecurityCodes.ToListAsync()).UserId);
    }

    [Fact]
    public async Task VerifyAsync_RejectsTamperedOrForeignProtectedValues()
    {
        await using var db = CreateContext();
        var emailSender = new CapturingEmailSender();
        var provider = new EphemeralDataProtectionProvider();
        var service = CreateService(db, emailSender, provider);
        await service.IssueAsync("user-4", "candidate@example.com", AccountSecurityAction.ResetPassword);
        var delivery = Assert.Single(emailSender.Deliveries);
        var stored = Assert.Single(await db.AccountSecurityCodes.ToListAsync());

        stored.ProtectedCode[0] ^= 0xFF;
        await db.SaveChangesAsync();
        var tampered = await service.VerifyAsync("user-4", AccountSecurityAction.ResetPassword, delivery.Code);
        Assert.False(tampered.Succeeded);

        stored.FailedAttemptCount = 0;
        await db.SaveChangesAsync();
        var foreignService = CreateService(
            db,
            emailSender,
            new EphemeralDataProtectionProvider());
        var foreign = await foreignService.VerifyAsync("user-4", AccountSecurityAction.ResetPassword, delivery.Code);
        Assert.False(foreign.Succeeded);
    }

    private static AccountSecurityCodeService CreateService(
        ApplicationDbContext db,
        IApplicationEmailSender emailSender,
        IDataProtectionProvider? dataProtectionProvider = null,
        TimeProvider? timeProvider = null) =>
        new(
            db,
            emailSender,
            dataProtectionProvider ?? new EphemeralDataProtectionProvider(),
            new WorkspaceQuotaGate(db),
            timeProvider ?? TimeProvider.System);

    private static AccountSecurityCode CreateStoredCode(
        string userId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? consumedAt) =>
        new()
        {
            UserId = userId,
            Action = AccountSecurityAction.ResetPassword,
            ProtectedCode = [1],
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            ConsumedAt = consumedAt
        };

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("account-security-codes-" + Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class CapturingEmailSender : IApplicationEmailSender
    {
        public List<Delivery> Deliveries { get; } = [];

        public Task SendAccountSecurityCodeAsync(
            string email,
            AccountSecurityAction action,
            string code)
        {
            Deliveries.Add(new Delivery(email, action, code));
            return Task.CompletedTask;
        }
    }

    private sealed record Delivery(string Email, AccountSecurityAction Action, string Code);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }
}
