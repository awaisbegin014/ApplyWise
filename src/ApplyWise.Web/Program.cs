using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ApplyWise.Web.Data;
using ApplyWise.Web.Services.BestResumePicker;
using ApplyWise.Web.Services.Analytics;
using ApplyWise.Web.Services.JobScamDetection;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.Services.Email;
using ApplyWise.Web.Services.Health;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.Services.Dashboard;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.Services.Gmail;
using ApplyWise.Web.Services.Admin;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Contact;
using ApplyWise.Web.Services.Ai;
using ApplyWise.Web.Services.Subscriptions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO.Compression;
using System.Threading.RateLimiting;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

if (PdfInspectionWorker.TryRun(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);
var isProduction = builder.Environment.IsProduction();
var requireConfirmedAccount = builder.Configuration.GetValue(
    "Identity:RequireConfirmedAccount",
    isProduction);

// The default Windows Event Log provider requires elevated permissions and can
// turn an otherwise harmless development warning into a failed HTTP request.
// Local development should log to the terminal/debug output instead.
if (builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
}

var publicOrigin = builder.Configuration["PublicOrigin"];
var allowedHosts = builder.Configuration["AllowedHosts"];
var resumeStorageRoot = builder.Configuration["ResumeStorage:RootPath"];
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
var dataProtectionCertificatePath = builder.Configuration["DataProtection:CertificatePath"];
var dataProtectionCertificatePassword = builder.Configuration["DataProtection:CertificatePassword"];
var previousDataProtectionCertificates = builder.Configuration
    .GetSection("DataProtection:PreviousCertificates")
    .Get<DataProtectionCertificateReference[]>() ?? [];
var smtpHost = builder.Configuration["Email:Host"];
var smtpFrom = builder.Configuration["Email:From"];
var connectionStringSetting = builder.Configuration.GetConnectionString("DefaultConnection");
var productionSqlTransport = builder.Configuration
    .GetSection(ProductionSqlConnectionSecurityOptions.SectionName)
    .Get<ProductionSqlConnectionSecurityOptions>() ?? new ProductionSqlConnectionSecurityOptions();
var globalPermitLimit = builder.Configuration.GetValue("RateLimiting:GlobalPermitLimit", 240);
if (globalPermitLimit is < 60 or > 5_000)
{
    throw new InvalidOperationException(
        "RateLimiting:GlobalPermitLimit must be between 60 and 5000 requests per client per minute.");
}
var slowRequestThreshold = TimeSpan.FromMilliseconds(Math.Clamp(
    builder.Configuration.GetValue("Performance:SlowRequestThresholdMs", 500),
    100,
    60_000));
var googleIntegration = builder.Configuration
    .GetSection(GoogleIntegrationOptions.SectionName)
    .Get<GoogleIntegrationOptions>() ?? new GoogleIntegrationOptions();
var configuredAdminEmails = builder.Configuration
    .GetSection($"{AdminAccessOptions.SectionName}:Emails")
    .Get<string[]>() ?? [];
var requireAdminMfa = builder.Configuration.GetValue(
    $"{AdminAccessOptions.SectionName}:RequireMfa",
    true);
var configuredKnownProxies = builder.Configuration
    .GetSection("ForwardedHeaders:KnownProxies")
    .Get<string[]>() ?? [];
var humanChallenge = builder.Configuration
    .GetSection(HumanChallengeOptions.SectionName)
    .Get<HumanChallengeOptions>() ?? new HumanChallengeOptions();

static bool IsUnset(string? value) => string.IsNullOrWhiteSpace(value) || value.Contains("__SET_", StringComparison.Ordinal);
static bool IsCanonicalHttpsOrigin(Uri? uri) => uri is not null
    && uri.Scheme == Uri.UriSchemeHttps
    && Uri.CheckHostName(uri.Host) == UriHostNameType.Dns
    && !uri.IsLoopback
    && !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
    && string.IsNullOrEmpty(uri.UserInfo)
    && uri.AbsolutePath == "/"
    && string.IsNullOrEmpty(uri.Query)
    && string.IsNullOrEmpty(uri.Fragment);
static bool IsExactHost(string host) => !string.IsNullOrWhiteSpace(host)
    && !host.Contains('*', StringComparison.Ordinal)
    && !host.StartsWith(".", StringComparison.Ordinal)
    && Uri.CheckHostName(host) != UriHostNameType.Unknown;

var publicOriginUri = Uri.TryCreate(publicOrigin, UriKind.Absolute, out var parsedPublicOrigin)
    ? parsedPublicOrigin
    : null;
var configuredAllowedHosts = allowedHosts?
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? [];

if (isProduction &&
    (!requireConfirmedAccount
     || IsUnset(connectionStringSetting)
     || !IsCanonicalHttpsOrigin(publicOriginUri)
     || IsUnset(allowedHosts)
     || configuredAllowedHosts.Length == 0
     || configuredAllowedHosts.Any(host => !IsExactHost(host))
     || !configuredAllowedHosts.Contains(publicOriginUri!.Host, StringComparer.OrdinalIgnoreCase)
     || IsUnset(smtpHost)
     || IsUnset(smtpFrom)
     || IsUnset(resumeStorageRoot)
     || !Path.IsPathRooted(resumeStorageRoot)
     || IsUnset(dataProtectionKeysPath)
     || !Path.IsPathRooted(dataProtectionKeysPath)
     || IsUnset(dataProtectionCertificatePath)
     || !Path.IsPathRooted(dataProtectionCertificatePath)
     || IsUnset(dataProtectionCertificatePassword)
     || previousDataProtectionCertificates.Any(certificate =>
         IsUnset(certificate.Path)
         || !Path.IsPathRooted(certificate.Path)
         || IsUnset(certificate.Password))
     || !requireAdminMfa
     || configuredAdminEmails.Length == 0
     || configuredAdminEmails.Any(IsUnset)
     || configuredKnownProxies.Length == 0
     || configuredKnownProxies.Any(proxy =>
         IsUnset(proxy) || !System.Net.IPAddress.TryParse(proxy, out _))
     || !humanChallenge.Enabled
     || IsUnset(humanChallenge.SiteKey)
     || IsUnset(humanChallenge.SecretKey)
     || !string.Equals(
         humanChallenge.ExpectedHostname,
         publicOriginUri!.Host,
         StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException(
        "Production requires a non-sa SQL connection string, a canonical HTTPS PublicOrigin represented in exact AllowedHosts, SMTP settings, an administrator email allowlist with MFA enforced, trusted reverse-proxy IPs, configured human verification for the public hostname, and absolute persistent paths for resume storage, Data Protection keys, and its encryption certificate.");
}

if (isProduction)
{
    ProductionPrivatePathSecurity.EnsureOutsideDeploymentRoot(
        builder.Environment.ContentRootPath,
        builder.Environment.WebRootPath,
        resumeStorageRoot!,
        dataProtectionKeysPath!,
        dataProtectionCertificatePath!);
    for (var index = 0; index < previousDataProtectionCertificates.Length; index++)
    {
        ProductionPrivatePathSecurity.EnsureSinglePathOutsideDeploymentRoot(
            builder.Environment.ContentRootPath,
            builder.Environment.WebRootPath,
            $"DataProtection:PreviousCertificates:{index}:Path",
            previousDataProtectionCertificates[index].Path);
    }
    builder.Services.AddHttpsRedirection(options =>
        options.HttpsPort = publicOriginUri!.Port);
}

var resolvedDataProtectionKeysPath = Path.GetFullPath(
    Path.IsPathRooted(dataProtectionKeysPath)
        ? dataProtectionKeysPath
        : Path.Combine(builder.Environment.ContentRootPath, dataProtectionKeysPath ?? Path.Combine("App_Data", "DataProtectionKeys")));
Directory.CreateDirectory(resolvedDataProtectionKeysPath);
builder.Services.AddSingleton(
    new DataProtectionReadinessOptions(resolvedDataProtectionKeysPath));
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(resolvedDataProtectionKeysPath))
    .SetApplicationName("ApplyWise");

if (!string.IsNullOrWhiteSpace(dataProtectionCertificatePath))
{
    var resolvedCertificatePath = Path.GetFullPath(
        Path.IsPathRooted(dataProtectionCertificatePath)
            ? dataProtectionCertificatePath
            : Path.Combine(builder.Environment.ContentRootPath, dataProtectionCertificatePath));
    if (!File.Exists(resolvedCertificatePath))
        throw new InvalidOperationException("The configured Data Protection certificate file was not found.");

    try
    {
        var certificate = Path.GetExtension(resolvedCertificatePath).Equals(".pem", StringComparison.OrdinalIgnoreCase)
            ? X509Certificate2.CreateFromEncryptedPemFile(
                resolvedCertificatePath,
                dataProtectionCertificatePassword,
                resolvedCertificatePath)
            : X509CertificateLoader.LoadPkcs12FromFile(
                resolvedCertificatePath,
                dataProtectionCertificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);
        if (isProduction)
        {
            DataProtectionCertificateSecurity.EnsureProductionReady(
                certificate,
                DateTimeOffset.UtcNow);
        }
        var decryptionCertificates = new List<X509Certificate2> { certificate };
        for (var index = 0; index < previousDataProtectionCertificates.Length; index++)
        {
            var previousReference = previousDataProtectionCertificates[index];
            var resolvedPreviousPath = Path.GetFullPath(previousReference.Path);
            if (!File.Exists(resolvedPreviousPath))
            {
                throw new InvalidOperationException(
                    $"DataProtection:PreviousCertificates:{index}:Path was not found.");
            }

            var previousCertificate = Path.GetExtension(resolvedPreviousPath).Equals(
                ".pem",
                StringComparison.OrdinalIgnoreCase)
                ? X509Certificate2.CreateFromEncryptedPemFile(
                    resolvedPreviousPath,
                    previousReference.Password,
                    resolvedPreviousPath)
                : X509CertificateLoader.LoadPkcs12FromFile(
                    resolvedPreviousPath,
                    previousReference.Password,
                    X509KeyStorageFlags.EphemeralKeySet);
            DataProtectionCertificateSecurity.EnsureCanDecrypt(previousCertificate);
            decryptionCertificates.Add(previousCertificate);
        }

        dataProtectionBuilder
            .ProtectKeysWithCertificate(certificate)
            .UnprotectKeysWithAnyCertificate([.. decryptionCertificates]);
    }
    catch (CryptographicException exception)
    {
        throw new InvalidOperationException("The configured Data Protection certificate could not be loaded.", exception);
    }
}

// Add services to the container.
var connectionString = connectionStringSetting
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
if (isProduction)
{
    connectionString = ProductionSqlConnectionSecurity.Harden(
        connectionString,
        productionSqlTransport);
}

builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlServer =>
        sqlServer.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null)));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = requireConfirmedAccount;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = PasswordRequirements.MinimumLength;
        options.Password.RequiredUniqueChars = PasswordRequirements.RequiredUniqueCharacters;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
if (googleIntegration.IsConfigured)
{
    var authentication = builder.Services.AddAuthentication()
        .AddGoogle(
            GoogleDefaults.AuthenticationScheme,
            "Google",
            options =>
            {
                options.ClientId = googleIntegration.ClientId;
                options.ClientSecret = googleIntegration.ClientSecret;
                options.CallbackPath = "/signin-google";
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect(
                        "/Identity/Account/Login?handler=ExternalLoginCallback&remoteError=oauth");
                    return Task.CompletedTask;
                };
            });
    if (googleIntegration.GmailImportEnabled)
    {
        authentication.AddGoogle(
            GmailAuthenticationDefaults.Scheme,
            GmailAuthenticationDefaults.DisplayName,
            options =>
            {
                options.ClientId = googleIntegration.ClientId;
                options.ClientSecret = googleIntegration.ClientSecret;
                options.CallbackPath = "/signin-google-gmail";
                options.AccessType = "offline";
                options.SaveTokens = true;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Scope.Add("https://www.googleapis.com/auth/gmail.readonly");
                options.Events.OnRedirectToAuthorizationEndpoint = context =>
                {
                    var authorizationUri = QueryHelpers.AddQueryString(
                        context.RedirectUri,
                        new Dictionary<string, string?>
                        {
                            ["prompt"] = "consent",
                            ["include_granted_scopes"] = "true"
                        });
                    context.Response.Redirect(authorizationUri);
                    return Task.CompletedTask;
                };
                options.Events.OnCreatingTicket = context =>
                {
                    context.Identity?.AddClaim(new Claim(
                        GmailAuthenticationDefaults.FlowClaimType,
                        GmailAuthenticationDefaults.FlowClaimValue));
                    return Task.CompletedTask;
                };
                options.Events.OnRemoteFailure = context =>
                {
                    var failureReason = GmailOAuthFailure.Classify(
                        context.Request.Query["error"].ToString(),
                        context.Failure);
                    var oauthLogger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ApplyWise.GmailOAuth");
                    oauthLogger.LogWarning(
                        "Gmail OAuth callback failed with category {FailureCategory} and exception type {ExceptionType}.",
                        failureReason,
                        context.Failure?.GetType().Name ?? "none");
                    context.HandleResponse();
                    context.Response.Redirect(QueryHelpers.AddQueryString(
                        "/connections/gmail/failure",
                        GmailOAuthFailure.QueryParameter,
                        failureReason));
                    return Task.CompletedTask;
                };
            });
    }
}
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = isProduction ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
});
builder.Services.AddControllersWithViews();
var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<DatabaseSchemaHealthCheck>("database_schema", tags: ["ready"])
    .AddCheck<ResumeStorageHealthCheck>("resume_storage", tags: ["ready"])
    .AddCheck<DataProtectionHealthCheck>("data_protection", tags: ["ready"]);
if (isProduction)
{
    healthChecks.AddCheck<AdminOwnerHealthCheck>("admin_owner", tags: ["ready"]);
}
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
    [
        "application/javascript",
        "application/json",
        "image/svg+xml",
        "text/javascript"
    ]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Please wait a moment and try again.",
            cancellationToken);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RequestRateLimitPartitions.CreateGlobal(
            context,
            globalPermitLimit));
    options.AddPolicy("uploads", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy(
        "account-security",
        RequestRateLimitPartitions.CreateAccountSecurity);
    options.AddPolicy("resume-analysis", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.AddPolicy("resume-comparison", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 4, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("gmail-sync", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 6, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.AddPolicy("scam-check", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.AddPolicy("contact", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("health", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("ai-coach", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 8,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("pro-upgrade", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminAccess.Policy, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(AdminAccess.Role)
        .AddRequirements(new AdminMfaRequirement())
        .RequireAssertion(context =>
        {
            var authenticatedEmail = context.User.FindFirstValue(ClaimTypes.Email)
                ?? context.User.Identity?.Name;
            return configuredAdminEmails.Any(email => string.Equals(
                email.Trim(),
                authenticatedEmail,
                StringComparison.OrdinalIgnoreCase));
        }));
});
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<AdminAccessOptions>()
    .Bind(builder.Configuration.GetSection(AdminAccessOptions.SectionName))
    .Validate(options => options.Emails.All(email =>
        new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)),
        "AdminAccess:Emails must contain only valid email addresses.")
    .ValidateOnStart();
builder.Services.AddOptions<ProductEventRetentionOptions>()
    .Bind(builder.Configuration.GetSection(ProductEventRetentionOptions.SectionName))
    .Validate(options => options.RetentionDays is >= 30 and <= 365
            && options.MaxStoredEvents is >= 10_000 and <= 5_000_000,
        "ProductEvents retention must be 30-365 days and capacity 10000-5000000 events.")
    .ValidateOnStart();
builder.Services.AddOptions<WorkspaceQuotaOptions>()
    .Bind(builder.Configuration.GetSection(WorkspaceQuotaOptions.SectionName))
    .Validate(options => options.MaxApplicationsPerUser is >= 100 and <= 10_000
        && options.MaxInterviewsPerUser is >= 100 and <= 10_000
        && options.MaxAnalysesPerUser is >= 100 and <= 20_000
        && options.MaxApplicationImportsPerUser is >= 100 and <= 20_000
        && options.MaxScamChecksPerUser is >= 100 and <= 20_000
        && options.MaxAnalysisSnapshotBytesPerUser is >= 5L * 1024 * 1024 and <= 1024L * 1024 * 1024,
        "Workspace quotas are outside safe bounds.")
    .ValidateOnStart();
builder.Services.AddOptions<ContactMessageStorageOptions>()
    .Bind(builder.Configuration.GetSection(ContactMessageStorageOptions.SectionName))
    .Validate(
        options => options.RetentionDays is >= 30 and <= 730
            && options.MaxStoredMessages is >= 100 and <= 100_000
            && options.ReservedAuthenticatedSlots >= 0
            && options.ReservedAuthenticatedSlots <= options.MaxStoredMessages - 100
            && options.MaxUnreadPerAuthenticatedUser is >= 1 and <= 100,
        "ContactMessages retention must be 30-730 days, storage cap 100-100000 messages, authenticated reserve must leave at least 100 anonymous slots, and per-account unread cap must be 1-100.")
    .ValidateOnStart();
builder.Services.AddOptions<SubscriptionOptions>()
    .Bind(builder.Configuration.GetSection(SubscriptionOptions.SectionName))
    .Validate(options => options.FreeAtsAnalysisLimit is >= 1 and <= 20
            && options.ProAtsAnalysisLimit is >= 10 and <= 10_000
            && options.FreeResumeBuildLimit is >= 1 and <= 20
            && options.ProDurationDays is >= 1 and <= 366
            && options.ProPrice > 0
            && !string.IsNullOrWhiteSpace(options.Currency)
            && options.Currency.Length <= 10
            && !string.IsNullOrWhiteSpace(options.PaymentInstructions),
        "Subscription limits, price, currency, duration, or payment instructions are invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<GeminiOptions>()
    .Bind(builder.Configuration.GetSection(GeminiOptions.SectionName))
    .Validate(options => !options.Enabled || options.IsConfigured,
        "Enabled Gemini integration requires an API key and model.")
    .Validate(options => options.TimeoutSeconds is >= 5 and <= 120
            && options.MaxOutputTokens is >= 256 and <= 4096,
        "Gemini timeout or output-token limits are invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<PendingRegistrationOptions>()
    .Bind(builder.Configuration.GetSection(PendingRegistrationOptions.SectionName))
    .Validate(
        options => options.RetentionDays is >= 1 and <= 30
            && options.MaxPendingAccounts is >= 100 and <= 100_000,
        "PendingRegistrations retention must be 1-30 days and capacity 100-100000 accounts.")
    .ValidateOnStart();
builder.Services.AddOptions<HumanChallengeOptions>()
    .Bind(builder.Configuration.GetSection(HumanChallengeOptions.SectionName))
    .Validate(
        options => !options.Enabled
            || (!string.IsNullOrWhiteSpace(options.SiteKey)
                && !string.IsNullOrWhiteSpace(options.SecretKey)
                && Uri.CheckHostName(options.ExpectedHostname) != UriHostNameType.Unknown),
        "Enabled human verification requires a site key, secret key, and valid expected hostname.")
    .ValidateOnStart();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in configuredKnownProxies)
    {
        if (System.Net.IPAddress.TryParse(proxy, out var address)) options.KnownProxies.Add(address);
    }
});
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .Validate(options => options.Port is > 0 and <= 65535, "Email:Port must be between 1 and 65535.")
    .Validate(
        options => !isProduction || ProductionEmailConfiguration.IsReady(options),
        "Production email requires a valid SMTP host and sender address, TLS, and either both or neither of username/password.")
    .ValidateOnStart();
builder.Services.AddTransient<IEmailSender<IdentityUser>, SmtpEmailSender>();
builder.Services.AddTransient<IApplicationEmailSender, SmtpEmailSender>();
builder.Services.AddHttpClient<IHumanChallengeVerifier, TurnstileHumanChallengeVerifier>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ApplyWise/1.0");
});
builder.Services.AddScoped<IAccountSecurityCodeService, AccountSecurityCodeService>();
builder.Services.AddScoped<IPendingRegistrationStore, PendingRegistrationStore>();
builder.Services.AddScoped<IPendingRegistrationRetention, PendingRegistrationRetention>();
builder.Services.AddScoped<IAccountSecurityCodeRetention, AccountSecurityCodeRetention>();
builder.Services.AddSingleton<ILoginTimingProtector, LoginTimingProtector>();
builder.Services.AddSingleton<AccountSecurityRequestQueue>();
builder.Services.AddSingleton<IAccountSecurityRequestQueue>(
    services => services.GetRequiredService<AccountSecurityRequestQueue>());
builder.Services.AddHostedService(
    services => services.GetRequiredService<AccountSecurityRequestQueue>());
builder.Services.AddHostedService<AccountSecurityCodeCleanupService>();
builder.Services.AddHostedService<PendingRegistrationCleanupService>();
builder.Services.AddScoped<IResumeTextExtractorService, ResumeTextExtractorService>();
builder.Services.AddOptions<SkillTaxonomyOptions>()
    .Bind(builder.Configuration.GetSection("SkillTaxonomy"));
builder.Services.AddSingleton<IResumeTextNormalizer, ResumeTextNormalizer>();
builder.Services.AddSingleton<IResumeSectionDetector, ResumeSectionDetector>();
builder.Services.AddSingleton<ISkillTaxonomyService, SkillTaxonomyService>();
builder.Services.AddSingleton<IJobRequirementExtractor, JobRequirementExtractor>();
builder.Services.AddSingleton<IAtsReadinessScorer, AtsReadinessScorer>();
builder.Services.AddSingleton<IJobMatchScorer, JobMatchScorer>();
builder.Services.AddSingleton<IResumeAnalysisService, ResumeAnalysisService>();
builder.Services.AddScoped<IResumeAnalysisStore, ResumeAnalysisStore>();
builder.Services.AddScoped<IBestResumePickerService, BestResumePickerService>();
builder.Services.AddSingleton<IJobScamDetectorService, JobScamDetectorService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IDashboardReadService, DashboardReadService>();
builder.Services.AddScoped<IProductEventRecorder, ProductEventRecorder>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IAdminUserReportService, AdminUserReportService>();
builder.Services.AddScoped<IAdminContactMessageService, AdminContactMessageService>();
builder.Services.AddScoped<IAdminRoleAssignmentService, AdminRoleAssignmentService>();
builder.Services.AddScoped<IWorkspaceQuotaService, WorkspaceQuotaService>();
builder.Services.AddScoped<IApplicationLockProvider, ApplicationLockProvider>();
builder.Services.AddScoped<IWorkspaceQuotaGate, WorkspaceQuotaGate>();
builder.Services.AddScoped<IContactMessageStore, ContactMessageStore>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IAuthorizationHandler, AdminMfaAuthorizationHandler>();
builder.Services.AddHttpClient<IGeminiAtsAdvisor, GeminiAtsAdvisor>((services, client) =>
{
    var settings = services.GetRequiredService<IOptions<GeminiOptions>>().Value;
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ApplyWise/1.0");
});
builder.Services.AddHostedService<ProductEventCleanupService>();
builder.Services.AddHostedService<ContactMessageCleanupService>();
builder.Services.AddOptions<GoogleIntegrationOptions>()
    .Bind(builder.Configuration.GetSection(GoogleIntegrationOptions.SectionName))
    .Validate(
        options => (string.IsNullOrWhiteSpace(options.ClientId)
                    && string.IsNullOrWhiteSpace(options.ClientSecret))
                   || options.IsConfigured,
        "Google ClientId and ClientSecret must either both be empty or contain a valid OAuth web client configuration.")
    .Validate(
        options => !options.GmailImportEnabled || options.IsConfigured,
        "Gmail import can be enabled only when valid Google OAuth credentials are configured.")
    .Validate(
        options => options.GmailSyncIntervalMinutes is >= 5 and <= 1440
            && options.GmailInitialLookbackDays is >= 1 and <= 90
            && options.GmailMaxMessagesPerSync is >= 25 and <= 500
            && options.GmailSyncTimeoutSeconds is >= 30 and <= 600
            && options.GmailMaxResponseBytes is >= 262_144 and <= 10 * 1024 * 1024,
        "Google Gmail sync limits are outside safe bounds.")
    .ValidateOnStart();
builder.Services.AddHttpClient("GoogleOAuth", client =>
    client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient("Gmail", client =>
    client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<IGmailCredentialProtector, GmailCredentialProtector>();
builder.Services.AddSingleton<IApplicationEmailParser, ApplicationEmailParser>();
builder.Services.AddScoped<IApplicationImportProcessor, ApplicationImportProcessor>();
builder.Services.AddScoped<IGmailImportService, GmailImportService>();
builder.Services.AddHostedService<GmailImportWorker>();
builder.Services.AddOptions<ResumeStorageOptions>()
    .Bind(builder.Configuration.GetSection(ResumeStorageOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath),
        "ResumeStorage:RootPath must be configured.")
    .Validate(options => options.MaxFileSizeBytes is > 0 and <= 10 * 1024 * 1024 && options.MaxFilesPerUser > 0 && options.MaxBytesPerUser >= options.MaxFileSizeBytes && options.ExtractionTimeoutSeconds is >= 5 and <= 120 && options.ParserQueueLimit is >= 1 and <= 100 && options.ParserQueueTimeoutSeconds is >= 1 and <= 60,
        "ResumeStorage limits are outside safe bounds.")
    .ValidateOnStart();
builder.Services.AddSingleton<IResumeStorageService, ResumeStorageService>();
builder.Services.AddSingleton<ResumeFileCleanupService>();
builder.Services.AddSingleton<IResumeFileCleanupScheduler>(
    services => services.GetRequiredService<ResumeFileCleanupService>());
builder.Services.AddHostedService(
    services => services.GetRequiredService<ResumeFileCleanupService>());
builder.Services.AddScoped<IResumeIngestionService, ResumeIngestionService>();

var app = builder.Build();

// Forwarded scheme and client information must be normalized before HSTS,
// HTTPS redirection, authentication, and rate-limit partitioning inspect it.
app.UseForwardedHeaders();

// Pre-compute the dummy password hash so the first unknown-account login has
// the same expensive verification work as later requests.
_ = app.Services.GetRequiredService<ILoginTimingProtector>();

await using (var adminScope = app.Services.CreateAsyncScope())
{
    await AdminRoleSynchronizer.SynchronizeAsync(adminScope.ServiceProvider);
    if (OwnerProvisioningCommand.IsRequested(args))
    {
        Environment.ExitCode = await OwnerProvisioningCommand.RunAsync(
            adminScope.ServiceProvider,
            app.Environment,
            args);
        return;
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

var resourceLockLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("ResourceLockAdmission");
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ResourceLockUnavailableException exception)
        when (!context.Response.HasStarted)
    {
        resourceLockLogger.LogWarning(
            "A request to {Path} was rejected because a protected resource was busy: {Reason}",
            context.Request.Path,
            exception.Message);
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "5";
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(
            "This operation is busy. Wait a few seconds and try again.",
            context.RequestAborted);
    }
});
app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");
if (!app.Environment.IsDevelopment()
    || string.Equals(publicOriginUri?.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
{
    app.UseHttpsRedirection();
}
app.UseResponseCompression();
var performanceLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("RequestPerformance");
app.Use(async (context, next) =>
{
    var startedAt = Stopwatch.GetTimestamp();
    await next();

    var duration = Stopwatch.GetElapsedTime(startedAt);
    if (duration >= slowRequestThreshold)
    {
        performanceLogger.LogWarning(
            "Slow request {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds:F1} ms.",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            duration.TotalMilliseconds);
    }
});
app.Use(async (context, next) =>
{
    var formActionPolicy = googleIntegration.IsConfigured
        ? "form-action 'self' https://accounts.google.com"
        : "form-action 'self'";

    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), geolocation=(), microphone=()");
    context.Response.Headers.TryAdd(
        "Content-Security-Policy",
        $"base-uri 'self'; frame-ancestors 'none'; object-src 'none'; {formActionPolicy}");
    await next();
});
app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<AdminOnlyAccountMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<UserActivityMiddleware>();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
}).RequireRateLimiting("health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
}).RequireRateLimiting("health");
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
}).RequireRateLimiting("health");
app.MapGet("/health/release", (
    HttpContext context,
    ApplicationDbContext dbContext) =>
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        return HealthResponseWriter.Release(app.Environment, dbContext);
    })
    .AllowAnonymous()
    .RequireRateLimiting("health");

app.Run();

public partial class Program { }
