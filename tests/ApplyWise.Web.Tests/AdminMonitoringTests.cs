using ApplyWise.Web.Controllers;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class AdminMonitoringTests
{
    [Fact]
    public void Production_admin_options_require_mfa()
    {
        var json = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "ApplyWise.Web", "appsettings.Production.json"));

        Assert.Contains("\"RequireMfa\": true", json, StringComparison.Ordinal);
        Assert.Contains("__SET_OWNER_EMAIL__", json, StringComparison.Ordinal);
        Assert.DoesNotContain("@gmail.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.IsAssignableFrom<IAuthorizationRequirement>(new AdminMfaRequirement());
    }

    [Fact]
    public void Admin_mfa_requires_proof_from_the_current_session()
    {
        var enrolledButPasswordOnly = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "owner")
        ], "Identity.Application"));
        var secondFactorSession = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "owner"),
            new Claim(AdminAccess.AuthenticationMethodClaim, AdminAccess.MfaAuthenticationMethod)
        ], "Identity.Application"));

        Assert.False(AdminAccess.HasMfaSession(enrolledButPasswordOnly));
        Assert.True(AdminAccess.HasMfaSession(secondFactorSession));
    }
    [Fact]
    public void Admin_controller_requires_the_dedicated_policy()
    {
        var authorization = Assert.Single(
            typeof(AdminController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>());

        Assert.Equal(AdminAccess.Policy, authorization.Policy);
    }

    [Theory]
    [InlineData("OWNER@example.test", true)]
    [InlineData("not-owner@example.test", false)]
    [InlineData(null, false)]
    public void Admin_allowlist_matching_is_exact_and_case_insensitive(string? email, bool expected)
    {
        var options = new AdminAccessOptions { Emails = ["owner@example.test"] };

        Assert.Equal(expected, options.Contains(email));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/applications")]
    [InlineData("/resumes")]
    public async Task Mfa_verified_admin_is_redirected_out_of_the_candidate_workspace(string path)
    {
        var context = AdminContext(path, includeMfa: true);
        var nextCalled = false;
        var middleware = CreateAdminOnlyMiddleware(() => nextCalled = true);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/admin", context.Response.Headers.Location);
    }

    [Fact]
    public async Task Admin_without_current_mfa_is_sent_to_owner_security()
    {
        var context = AdminContext("/admin", includeMfa: false);
        var nextCalled = false;
        var middleware = CreateAdminOnlyMiddleware(() => nextCalled = true);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/settings", context.Response.Headers.Location);
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/settings")]
    [InlineData("/Identity/Account/Manage/TwoFactorAuthentication")]
    [InlineData("/Identity/Account/Manage/EnableAuthenticator")]
    [InlineData("/Identity/Account/Manage/GenerateRecoveryCodes")]
    [InlineData("/Identity/Account/Manage/ShowRecoveryCodes")]
    [InlineData("/Identity/Account/Manage/ResetAuthenticator")]
    [InlineData("/Identity/Account/Logout")]
    [InlineData("/css/site.css")]
    public async Task Admin_boundary_allows_console_security_and_assets(string path)
    {
        var context = AdminContext(path, includeMfa: true);
        var nextCalled = false;
        var middleware = CreateAdminOnlyMiddleware(() => nextCalled = true);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/settings/delete-account")]
    [InlineData("/settings/security-code/delete")]
    [InlineData("/Identity/Account/Manage/DeletePersonalData")]
    [InlineData("/Identity/Account/Manage/PersonalData")]
    [InlineData("/Identity/Account/Manage/ChangePassword")]
    [InlineData("/Identity/Account/Manage/Email")]
    [InlineData("/Identity/Account/Manage/ExternalLogins")]
    [InlineData("/Identity/Account/Manage/Disable2fa")]
    public async Task Admin_boundary_rejects_unapproved_account_mutations(string path)
    {
        var context = AdminContext(path, includeMfa: true);
        context.Request.Method = HttpMethods.Post;
        var nextCalled = false;
        var middleware = CreateAdminOnlyMiddleware(() => nextCalled = true);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/settings/security-code/password")]
    [InlineData("/settings/change-password")]
    public async Task Admin_boundary_allows_only_approved_password_mutations(string path)
    {
        var context = AdminContext(path, includeMfa: true);
        context.Request.Method = HttpMethods.Post;
        var nextCalled = false;
        var middleware = CreateAdminOnlyMiddleware(() => nextCalled = true);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Allowlisted_identity_with_a_stale_role_cookie_is_still_contained()
    {
        var context = AdminContext("/applications", includeMfa: true, includeAdminRole: false);
        context.User.Identities.Single().AddClaim(
            new Claim(ClaimTypes.Email, "owner@example.test"));
        var nextCalled = false;
        var middleware = CreateAdminOnlyMiddleware(
            () => nextCalled = true,
            new AdminAccessOptions
            {
                RequireMfa = true,
                Emails = ["owner@example.test"]
            });

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal("/owner/session", context.Response.Headers.Location);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Non_admin_principals_pass_through(bool authenticated)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/applications";
        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "candidate"),
                new Claim(ClaimTypes.Email, "candidate@example.test")
            ], "Identity.Application"));
        }
        var nextCalled = false;
        var middleware = CreateAdminOnlyMiddleware(() => nextCalled = true);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/Identity/Account/Manage/Email", "GET", 302)]
    [InlineData("/Identity/Account/Manage/ChangePassword", "POST", 403)]
    [InlineData("/Identity/Account/Manage/DeletePersonalData", "POST", 403)]
    public async Task Packaged_identity_management_routes_cannot_bypass_custom_controls(
        string path,
        string method,
        int expectedStatus)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "candidate")
            ], "Identity.Application"))
        };
        context.Request.Path = path;
        context.Request.Method = method;
        var nextCalled = false;
        var middleware = CreateAdminOnlyMiddleware(() => nextCalled = true);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        if (expectedStatus == StatusCodes.Status302Found)
        {
            Assert.Equal("/settings", context.Response.Headers.Location);
        }
    }

    [Fact]
    public async Task Authenticator_management_remains_available_for_mfa_enrollment()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "candidate")
            ], "Identity.Application"))
        };
        context.Request.Path = "/Identity/Account/Manage/EnableAuthenticator";
        context.Request.Method = HttpMethods.Get;
        var nextCalled = false;
        var middleware = CreateAdminOnlyMiddleware(() => nextCalled = true);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/Identity/Account/Manage/EnableAuthenticator", "GET", 302)]
    [InlineData("/Identity/Account/Manage/GenerateRecoveryCodes", "POST", 403)]
    [InlineData("/Identity/Account/Manage/ResetAuthenticator", "POST", 403)]
    public async Task Admin_authenticator_management_requires_current_session_mfa(
        string path,
        string method,
        int expectedStatus)
    {
        var context = AdminContext(path, includeMfa: false);
        context.Request.Method = method;
        var nextCalled = false;
        var middleware = CreateAdminOnlyMiddleware(() => nextCalled = true);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        if (expectedStatus == StatusCodes.Status302Found)
        {
            Assert.Equal("/settings", context.Response.Headers.Location);
        }
    }

    [Fact]
    public async Task Admin_workspace_mutation_is_rejected_instead_of_redirected()
    {
        var context = AdminContext("/applications/create", includeMfa: true);
        context.Request.Method = HttpMethods.Post;
        var middleware = CreateAdminOnlyMiddleware(() => { });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    private static AdminOnlyAccountMiddleware CreateAdminOnlyMiddleware(
        Action onNext,
        AdminAccessOptions? options = null) =>
        new(
            _ =>
            {
                onNext();
                return Task.CompletedTask;
            },
            Options.Create(options ?? new AdminAccessOptions { RequireMfa = true }));

    private static DefaultHttpContext AdminContext(
        string path,
        bool includeMfa,
        bool includeAdminRole = true)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "owner")
        };
        if (includeAdminRole)
        {
            claims.Add(new Claim(ClaimTypes.Role, AdminAccess.Role));
        }
        if (includeMfa)
        {
            claims.Add(new Claim(
                AdminAccess.AuthenticationMethodClaim,
                AdminAccess.MfaAuthenticationMethod));
        }

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Identity.Application"))
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        return context;
    }

    [Fact]
    public async Task Login_tracking_updates_bounded_account_activity_without_event_rows()
    {
        const string userId = "tracked-user";
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("admin-monitoring-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        db.Users.Add(new IdentityUser { Id = userId, UserName = "tracked@example.test" });
        db.UserAccountActivities.Add(new UserAccountActivity
        {
            UserId = userId,
            RegisteredAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z")
        });
        await db.SaveChangesAsync();

        var recorder = new ProductEventRecorder(
            db,
            TimeProvider.System,
            Options.Create(new ProductEventRetentionOptions()),
            NullLogger<ProductEventRecorder>.Instance);
        await recorder.RecordLoginAsync(userId, "password");

        var activity = await db.UserAccountActivities.SingleAsync();
        Assert.NotNull(activity.LastLoginAt);
        Assert.NotNull(activity.LastActivityAt);
        Assert.Equal(1, activity.TotalSuccessfulLogins);
        Assert.Equal("password", activity.LastLoginProvider);
        Assert.Empty(await db.ProductEvents.ToListAsync());
    }

    [Fact]
    public async Task Anonymous_failed_logins_are_not_persisted_as_unbounded_rows()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("anonymous-login-events-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        var recorder = new ProductEventRecorder(
            db,
            TimeProvider.System,
            Options.Create(new ProductEventRetentionOptions()),
            NullLogger<ProductEventRecorder>.Instance);

        await recorder.RecordAsync(
            ProductEventNames.LoginFailed,
            "password",
            succeeded: false);

        Assert.Empty(await db.ProductEvents.ToListAsync());
    }

    [Fact]
    public async Task Product_event_recorder_stops_at_the_global_storage_cap()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("product-event-cap-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        var recorder = new ProductEventRecorder(
            db,
            TimeProvider.System,
            Options.Create(new ProductEventRetentionOptions { MaxStoredEvents = 1 }),
            NullLogger<ProductEventRecorder>.Instance);

        await recorder.RecordAsync(ProductEventNames.ResumeUploaded, "test");
        await recorder.RecordAsync(ProductEventNames.ApplicationCreated, "test");

        Assert.Single(await db.ProductEvents.ToListAsync());
    }

    [Fact]
    public async Task Admin_dashboard_returns_aggregate_counts_without_document_contents()
    {
        const string userId = "admin-view-user";
        const string ownerId = "owner-user";
        const string adminRoleId = "admin-role";
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("admin-dashboard-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        var now = DateTimeOffset.UtcNow;
        db.Users.AddRange(new IdentityUser
        {
            Id = userId,
            UserName = "candidate@example.test",
            Email = "candidate@example.test",
            EmailConfirmed = true
        }, new IdentityUser
        {
            Id = ownerId,
            UserName = "owner@example.test",
            Email = "owner@example.test",
            NormalizedEmail = "OWNER@EXAMPLE.TEST",
            EmailConfirmed = true
        });
        db.Roles.Add(new IdentityRole
        {
            Id = adminRoleId,
            Name = AdminAccess.Role,
            NormalizedName = AdminAccess.Role.ToUpperInvariant()
        });
        db.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = ownerId,
            RoleId = adminRoleId
        });
        db.CareerProfiles.Add(new CareerProfile
        {
            UserId = userId,
            FullName = "Candidate Example",
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now,
            OnboardingCompletedAt = now.AddDays(-1)
        });
        db.UserAccountActivities.Add(new UserAccountActivity
        {
            UserId = userId,
            RegisteredAt = now.AddDays(-2),
            LastActivityAt = now,
            LastLoginAt = now,
            TotalSuccessfulLogins = 2
        });
        db.UserAccountActivities.Add(new UserAccountActivity
        {
            UserId = ownerId,
            RegisteredAt = now.AddDays(-2),
            LastActivityAt = now,
            LastLoginAt = now,
            TotalSuccessfulLogins = 5
        });
        db.ProductEvents.Add(new ProductEvent
        {
            UserId = userId,
            Name = ProductEventNames.ResumeAnalysisCompleted,
            Source = "savedapplication",
            Succeeded = true,
            OccurredAt = now
        });
        db.ProductEvents.Add(new ProductEvent
        {
            UserId = ownerId,
            Name = ProductEventNames.ResumeAnalysisCompleted,
            Source = "owner",
            Succeeded = true,
            OccurredAt = now
        });
        await db.SaveChangesAsync();

        var service = new AdminDashboardService(
            db,
            TimeProvider.System,
            Options.Create(new AdminAccessOptions { Emails = ["owner@example.test"] }));
        var result = await service.LoadAsync(30);

        Assert.Equal(1, result.TotalUsers);
        Assert.Equal(1, result.ConfirmedUsers);
        Assert.Equal(1, result.ActiveUsersInRange);
        Assert.Equal(1, result.CompletedOnboardingUsers);
        Assert.Equal("candidate@example.test", Assert.Single(result.Users).Email);
        Assert.Equal(1, Assert.Single(result.FeatureUsage).Count);
        Assert.DoesNotContain(result.Users, user => user.Email == "owner@example.test");
        Assert.DoesNotContain(
            typeof(AdminUserRowViewModel).GetProperties(),
            property => property.Name.Contains("Text", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains("Description", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Admin_dashboard_searches_email_and_display_name_case_insensitively()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("admin-search-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        var now = DateTimeOffset.UtcNow;
        db.Users.AddRange(
            new IdentityUser
            {
                Id = "alice",
                UserName = "alice@example.test",
                Email = "alice@example.test",
                NormalizedEmail = "ALICE@EXAMPLE.TEST"
            },
            new IdentityUser
            {
                Id = "bob",
                UserName = "bob@example.test",
                Email = "bob@example.test",
                NormalizedEmail = "BOB@EXAMPLE.TEST"
            },
            new IdentityUser
            {
                Id = "owner",
                UserName = "owner@example.test",
                Email = "owner@example.test",
                NormalizedEmail = "OWNER@EXAMPLE.TEST"
            });
        db.CareerProfiles.AddRange(
            new CareerProfile
            {
                UserId = "alice",
                FullName = "Alice Designer",
                CreatedAt = now,
                UpdatedAt = now
            },
            new CareerProfile
            {
                UserId = "bob",
                FullName = "Bob Engineer",
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var service = new AdminDashboardService(
            db,
            TimeProvider.System,
            Options.Create(new AdminAccessOptions { Emails = ["owner@example.test"] }));
        var byName = await service.LoadAsync(30, "  dEsIgNeR  ", 1);
        var byEmail = await service.LoadAsync(30, "ALICE@EXAMPLE.TEST", 1);

        Assert.Equal("dEsIgNeR", byName.Search);
        Assert.Equal(2, byName.TotalUsers);
        Assert.Equal(1, byName.TotalMatchingUsers);
        Assert.Equal("alice", Assert.Single(byName.Users).UserId);
        Assert.Equal("alice", Assert.Single(byEmail.Users).UserId);
        Assert.DoesNotContain(byName.Users, user => user.UserId == "owner");
    }

    [Fact]
    public async Task Admin_dashboard_pages_users_and_clamps_to_the_last_valid_page()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("admin-pagination-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        var now = DateTimeOffset.UtcNow;
        for (var index = 1; index <= 27; index++)
        {
            var userId = $"candidate-{index:00}";
            db.Users.Add(new IdentityUser
            {
                Id = userId,
                UserName = $"{userId}@example.test",
                Email = $"{userId}@example.test",
                NormalizedEmail = $"{userId}@example.test".ToUpperInvariant()
            });
            db.UserAccountActivities.Add(new UserAccountActivity
            {
                UserId = userId,
                RegisteredAt = now.AddMinutes(-index)
            });
        }

        db.Users.Add(new IdentityUser
        {
            Id = "owner",
            UserName = "owner@example.test",
            Email = "owner@example.test",
            NormalizedEmail = "OWNER@EXAMPLE.TEST"
        });
        await db.SaveChangesAsync();

        var service = new AdminDashboardService(
            db,
            TimeProvider.System,
            Options.Create(new AdminAccessOptions { Emails = ["owner@example.test"] }));
        var result = await service.LoadAsync(30, null, 999);

        Assert.Equal(27, result.TotalUsers);
        Assert.Equal(27, result.TotalMatchingUsers);
        Assert.Equal(2, result.TotalUserPages);
        Assert.Equal(2, result.UserPage);
        Assert.Equal(25, result.UserPageSize);
        Assert.Equal(2, result.Users.Count);
        Assert.True(result.HasPreviousUserPage);
        Assert.False(result.HasNextUserPage);
        Assert.DoesNotContain(result.Users, user => user.UserId == "owner");
    }

    [Fact]
    public async Task Sql_dashboard_aggregates_execute_on_the_release_schema_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "APPLYWISE_SQL_INTEGRATION_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var service = new AdminDashboardService(
            db,
            TimeProvider.System,
            Options.Create(new AdminAccessOptions()));

        var result = await service.LoadAsync(7, null, 1);

        Assert.Equal(7, result.DailyActivity.Count);
    }
}
