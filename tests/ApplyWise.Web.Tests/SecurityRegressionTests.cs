using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Areas.Identity.Pages.Account;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.ViewModels.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Threading.RateLimiting;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class SecurityRegressionTests
{
    [Fact]
    public void Unknown_account_logins_use_a_bounded_dummy_password_verifier()
    {
        Assert.Contains(
            typeof(ILoginTimingProtector),
            ConstructorParameterTypes(typeof(LoginModel)));
        var password = typeof(LoginModel.InputModel).GetProperty(nameof(LoginModel.InputModel.Password));
        var maximumLength = Assert.Single(password!.GetCustomAttributes(typeof(StringLengthAttribute), inherit: true));
        Assert.Equal(100, Assert.IsType<StringLengthAttribute>(maximumLength).MaximumLength);

        var protector = new LoginTimingProtector(Options.Create(new PasswordHasherOptions
        {
            IterationCount = 1_000
        }));
        protector.VerifyDummyPassword("not-a-real-password");
    }

    [Fact]
    public async Task Anonymous_identity_outcomes_share_a_minimum_response_floor()
    {
        foreach (var pageType in new[]
                 {
                     typeof(LoginModel),
                     typeof(RegisterModel),
                     typeof(RegisterConfirmationModel),
                     typeof(ResetPasswordModel)
                 })
        {
            Assert.Contains(
                typeof(ILoginTimingProtector),
                ConstructorParameterTypes(pageType));
        }

        var protector = new LoginTimingProtector(Options.Create(new PasswordHasherOptions
        {
            IterationCount = 1_000
        }));
        var startedAt = Stopwatch.GetTimestamp();
        await protector.EnforceMinimumResponseTimeAsync(startedAt);
        Assert.True(
            Stopwatch.GetElapsedTime(startedAt) >= TimeSpan.FromMilliseconds(700),
            "The anonymous identity response completed before the configured timing floor.");
    }

    [Fact]
    public void Unconfirmed_login_and_recovery_shortcuts_are_timing_hardened()
    {
        var accountPages = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ApplyWise.Web",
            "Areas",
            "Identity",
            "Pages",
            "Account");
        var login = File.ReadAllText(Path.Combine(accountPages, "Login.cshtml.cs"));
        var reset = File.ReadAllText(Path.Combine(accountPages, "ResetPassword.cshtml.cs"));
        var confirmation = File.ReadAllText(Path.Combine(accountPages, "RegisterConfirmation.cshtml.cs"));

        Assert.Contains("RequireConfirmedAccount", login, StringComparison.Ordinal);
        Assert.Contains("VerifyDummyPassword", login, StringComparison.Ordinal);
        Assert.Contains("EnforceMinimumResponseTimeAsync", login, StringComparison.Ordinal);
        Assert.Contains("EnforceMinimumResponseTimeAsync", reset, StringComparison.Ordinal);
        Assert.Contains("EnforceMinimumResponseTimeAsync", confirmation, StringComparison.Ordinal);
        var credentialFinalization = confirmation.IndexOf(
            "ResetPasswordAsync",
            StringComparison.Ordinal);
        var emailConfirmation = confirmation.IndexOf(
            "ConfirmEmailAsync",
            StringComparison.Ordinal);
        Assert.True(credentialFinalization >= 0);
        Assert.True(emailConfirmation > credentialFinalization);
    }

    [Fact]
    public void Login_and_registration_use_the_account_security_rate_limit()
    {
        AssertRateLimit<LoginModel>("account-security");
        AssertRateLimit<RegisterModel>("account-security");
    }

    [Theory]
    [InlineData("short1A")]
    [InlineData("alllowercase123")]
    [InlineData("ALLUPPERCASE123")]
    [InlineData("NoDigitsAllowed")]
    public void Public_password_inputs_reject_weak_passwords(string password)
    {
        AssertInvalid(new RegisterModel.InputModel
        {
            FullName = "Candidate",
            Email = "candidate@example.test",
            Password = password,
            ConfirmPassword = password
        });
        AssertInvalid(new ResetPasswordModel.InputModel
        {
            Email = "candidate@example.test",
            Code = "123456",
            Password = password,
            ConfirmPassword = password
        });
        AssertInvalid(new RegisterConfirmationModel.InputModel
        {
            Email = "candidate@example.test",
            Code = "123456",
            Password = password,
            ConfirmPassword = password
        });
        AssertInvalid(new ChangePasswordInput
        {
            CurrentPassword = "ExistingPassword1",
            NewPassword = password,
            ConfirmPassword = password,
            Code = "123456"
        });
    }

    [Fact]
    public void Public_password_inputs_accept_the_documented_policy()
    {
        const string password = "StrongPassword123";
        AssertValidPassword(new RegisterModel.InputModel
        {
            FullName = "Candidate",
            Email = "candidate@example.test",
            Password = password,
            ConfirmPassword = password
        });
        AssertValidPassword(new ResetPasswordModel.InputModel
        {
            Email = "candidate@example.test",
            Code = "123456",
            Password = password,
            ConfirmPassword = password
        });
        AssertValidPassword(new RegisterConfirmationModel.InputModel
        {
            Email = "candidate@example.test",
            Code = "123456",
            Password = password,
            ConfirmPassword = password
        });
        AssertValidPassword(new ChangePasswordInput
        {
            CurrentPassword = "ExistingPassword1",
            NewPassword = password,
            ConfirmPassword = password,
            Code = "123456"
        });
    }

    [Fact]
    public void Registration_does_not_collect_optional_demographics()
    {
        var inputProperties = typeof(RegisterModel.InputModel)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Gender", inputProperties);
        Assert.DoesNotContain("DateOfBirth", inputProperties);
    }

    [Fact]
    public void Password_validation_message_describes_the_enforced_policy()
    {
        var attribute = new StrongPasswordAttribute();

        Assert.Equal(PasswordRequirements.UserFacingSummary, attribute.ErrorMessage);
        Assert.Contains(PasswordRequirements.MinimumLength.ToString(), attribute.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(PasswordRequirements.RequiredUniqueCharacters.ToString(), attribute.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("uppercase", attribute.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lowercase", attribute.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("number", attribute.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Anonymous_recovery_initiators_do_not_resolve_identity_in_the_request()
    {
        Assert.DoesNotContain(
            typeof(Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>),
            ConstructorParameterTypes(typeof(ForgotPasswordModel)));
        Assert.DoesNotContain(
            typeof(Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>),
            ConstructorParameterTypes(typeof(ResendEmailConfirmationModel)));
        Assert.Contains(
            typeof(IAccountSecurityRequestQueue),
            ConstructorParameterTypes(typeof(ForgotPasswordModel)));
        Assert.Contains(
            typeof(IAccountSecurityRequestQueue),
            ConstructorParameterTypes(typeof(ResendEmailConfirmationModel)));
    }

    [Fact]
    public void Production_sql_transport_is_hardened_even_when_the_host_profile_is_not()
    {
        const string configured =
            "Server=sql.example.test;Database=ApplyWise;User ID=applywise_app;Password=test-only;" +
            "Encrypt=False;TrustServerCertificate=True";

        var hardened = new SqlConnectionStringBuilder(
            ProductionSqlConnectionSecurity.Harden(configured));

        Assert.Equal(SqlConnectionEncryptOption.Mandatory, hardened.Encrypt);
        Assert.False(hardened.TrustServerCertificate);
        Assert.Equal("applywise_app", hardened.UserID);
    }

    [Fact]
    public void Production_sql_transport_rejects_the_sa_login()
    {
        const string configured =
            "Server=sql.example.test;Database=ApplyWise;User ID=sa;Password=test-only";

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSqlConnectionSecurity.Harden(configured));

        Assert.Contains("must not use the sa login", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_sql_transport_allows_the_scoped_monsterasp_certificate_policy()
    {
        const string configured =
            "Server=db59734.databaseasp.net;Database=ApplyWise;User ID=applywise_app;" +
            "Password=test-only;Encrypt=False;TrustServerCertificate=False";
        var options = new ProductionSqlConnectionSecurityOptions
        {
            AllowMonsterAspManagedCertificate = true,
            MonsterAspHost = "db59734.databaseasp.net"
        };

        var hardened = new SqlConnectionStringBuilder(
            ProductionSqlConnectionSecurity.Harden(configured, options));

        Assert.Equal(SqlConnectionEncryptOption.Mandatory, hardened.Encrypt);
        Assert.True(hardened.TrustServerCertificate);
        Assert.Equal("db59734.databaseasp.net", hardened.DataSource);
    }

    [Theory]
    [InlineData("sql.example.test", "sql.example.test", "exact MonsterASP database hostname")]
    [InlineData("db59734.databaseasp.net", "db59735.databaseasp.net", "does not match")]
    public void Production_sql_transport_rejects_an_unscoped_certificate_policy(
        string configuredHost,
        string allowedHost,
        string expectedMessage)
    {
        var configured =
            $"Server={configuredHost};Database=ApplyWise;User ID=applywise_app;Password=test-only";
        var options = new ProductionSqlConnectionSecurityOptions
        {
            AllowMonsterAspManagedCertificate = true,
            MonsterAspHost = allowedHost
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSqlConnectionSecurity.Harden(configured, options));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Forwarded_transport_is_normalized_before_hsts_and_https_redirection()
    {
        var program = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ApplyWise.Web",
            "Program.cs"));
        var forwardedHeaders = program.IndexOf("app.UseForwardedHeaders();", StringComparison.Ordinal);
        var hsts = program.IndexOf("app.UseHsts();", StringComparison.Ordinal);
        var httpsRedirection = program.IndexOf("app.UseHttpsRedirection();", StringComparison.Ordinal);

        Assert.True(forwardedHeaders >= 0);
        Assert.True(hsts > forwardedHeaders);
        Assert.True(httpsRedirection > forwardedHeaders);
        Assert.Contains("options.HttpsPort = publicOriginUri!.Port", program, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("candidate@example.test?bcc=attacker@example.test")]
    [InlineData("candidate@example.test&body=hidden")]
    [InlineData("candidate@example.test#fragment")]
    [InlineData("candidate@example.test\r\nBcc:attacker@example.test")]
    public void Admin_reply_links_encode_the_complete_stored_address(string address)
    {
        var link = MailtoLinkBuilder.Build(address, "Re: Support request");

        Assert.StartsWith("mailto:", link, StringComparison.Ordinal);
        Assert.DoesNotContain("?bcc=", link, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("&body=", link, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#fragment", link, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\r", link, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", link, StringComparison.Ordinal);
        Assert.EndsWith("?subject=Re%3A%20Support%20request", link, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_reply_subject_strips_decoded_header_line_breaks()
    {
        var link = MailtoLinkBuilder.Build(
            "candidate@example.test",
            "Re: Help\r\nBcc: attacker@example.test");

        Assert.DoesNotContain("%0D", link, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%0A", link, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "mailto:candidate%40example.test?subject=Re%3A%20Help%20%20Bcc%3A%20attacker%40example.test",
            link);
    }

    [Theory]
    [InlineData("candidate@example.test?bcc=attacker@example.test")]
    [InlineData("candidate@example.test&body=hidden")]
    [InlineData("candidate@example.test#fragment")]
    [InlineData("candidate@example.test\r\nBcc:attacker@example.test")]
    [InlineData("Candidate <candidate@example.test>")]
    [InlineData("first@example.test,second@example.test")]
    public void Contact_mailbox_normalization_rejects_non_mailbox_syntax(string address)
    {
        Assert.False(MailboxAddressNormalizer.TryNormalize(address, out _));
    }

    [Fact]
    public void Contact_mailbox_normalization_accepts_one_plain_address()
    {
        Assert.True(MailboxAddressNormalizer.TryNormalize(
            " candidate@example.test ",
            out var normalized));
        Assert.Equal("candidate@example.test", normalized);
    }

    [Fact]
    public void Account_security_page_reads_are_not_counted_as_security_attempts()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            RequestRateLimitPartitions.CreateAccountSecurity);
        var context = CreateHttpContext(HttpMethods.Get, "/Identity/Account/Login");

        for (var request = 0; request < 100; request++)
        {
            using var lease = limiter.AttemptAcquire(context);
            Assert.True(lease.IsAcquired);
        }
    }

    [Fact]
    public void Account_security_submissions_remain_rate_limited()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            RequestRateLimitPartitions.CreateAccountSecurity);
        var context = CreateHttpContext(HttpMethods.Post, "/Identity/Account/Login");

        for (var request = 0; request < 8; request++)
        {
            using var lease = limiter.AttemptAcquire(context);
            Assert.True(lease.IsAcquired);
        }

        using var rejectedLease = limiter.AttemptAcquire(context);
        Assert.False(rejectedLease.IsAcquired);
    }

    [Fact]
    public void External_login_starts_use_a_separate_bounded_rate_limit()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            RequestRateLimitPartitions.CreateAccountSecurity);
        var passwordContext = CreateHttpContext(HttpMethods.Post, "/Identity/Account/Login");
        var externalLoginContext = CreateHttpContext(HttpMethods.Post, "/Identity/Account/Login");
        externalLoginContext.Request.QueryString = new QueryString("?handler=ExternalLogin");

        for (var request = 0; request < 8; request++)
        {
            using var lease = limiter.AttemptAcquire(passwordContext);
            Assert.True(lease.IsAcquired);
        }

        using (var rejectedPasswordLease = limiter.AttemptAcquire(passwordContext))
        {
            Assert.False(rejectedPasswordLease.IsAcquired);
        }

        for (var request = 0; request < 20; request++)
        {
            using var lease = limiter.AttemptAcquire(externalLoginContext);
            Assert.True(lease.IsAcquired);
        }

        using var rejectedExternalLoginLease = limiter.AttemptAcquire(externalLoginContext);
        Assert.False(rejectedExternalLoginLease.IsAcquired);
    }

    [Theory]
    [InlineData("/css/site.css")]
    [InlineData("/js/site.js")]
    public void Infrastructure_reads_do_not_consume_the_global_request_budget(string path)
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => RequestRateLimitPartitions.CreateGlobal(context, permitLimit: 1));
        var context = CreateHttpContext(HttpMethods.Get, path);

        for (var request = 0; request < 100; request++)
        {
            using var lease = limiter.AttemptAcquire(context);
            Assert.True(lease.IsAcquired);
        }
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/Dashboard/Index/expensive.css")]
    public void Dynamic_and_dependency_backed_reads_consume_the_global_budget(string path)
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => RequestRateLimitPartitions.CreateGlobal(context, permitLimit: 1));
        var context = CreateHttpContext(HttpMethods.Get, path);

        using var first = limiter.AttemptAcquire(context);
        using var rejected = limiter.AttemptAcquire(context);

        Assert.True(first.IsAcquired);
        Assert.False(rejected.IsAcquired);
    }

    [Fact]
    public void Dynamic_pages_remain_globally_rate_limited()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => RequestRateLimitPartitions.CreateGlobal(context, permitLimit: 2));
        var context = CreateHttpContext(HttpMethods.Get, "/Dashboard");

        using var firstLease = limiter.AttemptAcquire(context);
        using var secondLease = limiter.AttemptAcquire(context);
        using var rejectedLease = limiter.AttemptAcquire(context);

        Assert.True(firstLease.IsAcquired);
        Assert.True(secondLease.IsAcquired);
        Assert.False(rejectedLease.IsAcquired);
    }

    [Fact]
    public void Gmail_auto_add_preference_is_authenticated_post_with_antiforgery()
    {
        var controllerType =
            typeof(ApplyWise.Web.Controllers.ApplicationImportsController);
        var action = controllerType.GetMethod(
            nameof(ApplyWise.Web.Controllers.ApplicationImportsController.UpdateAutoAddPreference));

        Assert.NotNull(action);
        Assert.NotEmpty(controllerType.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute),
            inherit: true));
        Assert.NotEmpty(action.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute),
            inherit: true));
        Assert.NotEmpty(action.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute),
            inherit: true));
        Assert.Empty(action.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute),
            inherit: true));
    }

    private static void AssertRateLimit<TPage>(string policyName)
    {
        var attribute = Assert.IsType<EnableRateLimitingAttribute>(
            typeof(TPage).GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true).Single());
        Assert.Equal(policyName, attribute.PolicyName);
    }

    private static Type[] ConstructorParameterTypes(Type type) =>
        type.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();

    private static DefaultHttpContext CreateHttpContext(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");
        return context;
    }

    private static void AssertInvalid(object model) =>
        Assert.NotEmpty(Validate(model));

    private static void AssertValidPassword(object model) =>
        Assert.DoesNotContain(
            Validate(model),
            result => result.MemberNames.Any(name =>
                name.EndsWith("Password", StringComparison.Ordinal)));

    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ApplyWise.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ApplyWise repository root.");
    }
}
