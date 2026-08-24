using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.Services.Admin;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class PendingRegistrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Store_enforces_global_pending_cap_but_allows_existing_email_flow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("pending-registration-" + Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var store = new PendingRegistrationStore(
            db,
            provider.GetRequiredService<UserManager<IdentityUser>>(),
            new WorkspaceQuotaGate(db),
            Options.Create(new PendingRegistrationOptions
            {
                MaxPendingAccounts = 1,
                RetentionDays = 7
            }),
            new FixedTimeProvider(Now));

        var first = await store.TryCreateAsync(
            "first@example.test",
            "StrongPassword123!");
        var capped = await store.TryCreateAsync(
            "second@example.test",
            "StrongPassword123!");
        var existing = await store.TryCreateAsync(
            "first@example.test",
            "DifferentPassword123!");

        Assert.Equal(PendingRegistrationStatus.Created, first.Status);
        Assert.Equal(PendingRegistrationStatus.CapacityReached, capped.Status);
        Assert.Equal(PendingRegistrationStatus.Existing, existing.Status);
        Assert.Single(await db.Users.ToListAsync());
        var activity = Assert.Single(await db.UserAccountActivities.ToListAsync());
        Assert.Equal(first.User!.Id, activity.UserId);
        Assert.Equal(Now, activity.RegisteredAt);
        Assert.Equal(Now, activity.LastActivityAt);
    }

    [Fact]
    public async Task Retention_deletes_only_old_unconfirmed_non_owner_accounts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("pending-retention-" + Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        AddUser(db, "expired", "expired@example.test", confirmed: false, Now.AddDays(-8));
        AddUser(db, "owner", "owner@example.test", confirmed: false, Now.AddDays(-8));
        AddUser(db, "confirmed", "confirmed@example.test", confirmed: true, Now.AddDays(-8));
        AddUser(db, "recent", "recent@example.test", confirmed: false, Now.AddDays(-1));
        AddUser(db, "active-code", "active-code@example.test", confirmed: false, Now.AddDays(-8));
        db.AccountSecurityCodes.Add(new AccountSecurityCode
        {
            UserId = "active-code",
            Action = AccountSecurityAction.ConfirmEmail,
            ProtectedCode = [1, 2, 3],
            CreatedAt = Now.AddMinutes(-1),
            ExpiresAt = Now.AddMinutes(9)
        });
        await db.SaveChangesAsync();
        var retention = new PendingRegistrationRetention(
            db,
            Options.Create(new PendingRegistrationOptions { RetentionDays = 7 }),
            Options.Create(new AdminAccessOptions { Emails = ["owner@example.test"] }),
            new FixedTimeProvider(Now),
            new ApplicationLockProvider(db));

        Assert.Equal(1, await retention.DeleteExpiredBatchAsync());

        var remainingIds = await db.Users
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToListAsync();
        Assert.Equal(["active-code", "confirmed", "owner", "recent"], remainingIds);
    }

    [Fact]
    public async Task Retention_deletes_unconfirmed_crash_orphan_but_preserves_owner_and_confirmed_orphans()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("pending-orphan-retention-" + Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        AddIdentityUser(db, "orphan", "orphan@example.test", confirmed: false);
        AddIdentityUser(db, "owner-orphan", "owner@example.test", confirmed: false);
        AddIdentityUser(db, "confirmed-orphan", "confirmed@example.test", confirmed: true);
        await db.SaveChangesAsync();
        var retention = new PendingRegistrationRetention(
            db,
            Options.Create(new PendingRegistrationOptions { RetentionDays = 7 }),
            Options.Create(new AdminAccessOptions { Emails = ["owner@example.test"] }),
            new FixedTimeProvider(Now),
            new ApplicationLockProvider(db));

        Assert.Equal(1, await retention.DeleteExpiredBatchAsync());

        var remainingIds = await db.Users
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToListAsync();
        Assert.Equal(["confirmed-orphan", "owner-orphan"], remainingIds);
    }

    [Fact]
    public async Task Sql_retention_deletes_unconfirmed_crash_orphan_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "APPLYWISE_SQL_INTEGRATION_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var id = "pending-orphan-" + Guid.NewGuid().ToString("N");
        var email = id + "@example.test";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        AddIdentityUser(db, id, email, confirmed: false);
        await db.SaveChangesAsync();

        try
        {
            var retention = new PendingRegistrationRetention(
                db,
                Options.Create(new PendingRegistrationOptions { RetentionDays = 7 }),
                Options.Create(new AdminAccessOptions()),
                new FixedTimeProvider(Now),
                new ApplicationLockProvider(db));

            Assert.True(await retention.DeleteExpiredBatchAsync() >= 1);
            Assert.False(await db.Users.AsNoTracking().AnyAsync(user => user.Id == id));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static void AddUser(
        ApplicationDbContext db,
        string id,
        string email,
        bool confirmed,
        DateTimeOffset registeredAt)
    {
        AddIdentityUser(db, id, email, confirmed);
        db.UserAccountActivities.Add(new UserAccountActivity
        {
            UserId = id,
            RegisteredAt = registeredAt,
            LastActivityAt = registeredAt
        });
    }

    private static void AddIdentityUser(
        ApplicationDbContext db,
        string id,
        string email,
        bool confirmed)
    {
        db.Users.Add(new IdentityUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = confirmed
        });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
