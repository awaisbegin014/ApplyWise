using System.Net;
using System.Text;
using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Gmail;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class GmailImportServiceTests
{
    private const string UserId = "gmail-sync-candidate";
    private const string BodyMarker = "PRIVATE-BODY-MARKER-7f43";
    private const string AttachmentMarker = "PRIVATE-ATTACHMENT-CONTENT-a912";

    [Fact]
    public async Task Disabled_gmail_release_switch_blocks_manual_sync()
    {
        await using var db = CreateContext();
        var service = CreateService(
            db,
            new GmailApiHandler(new Dictionary<string, string>()),
            new ApplicationEmailParser(),
            gmailImportEnabled: false);

        var result = await service.SyncUserAsync(UserId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not enabled", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.GmailConnections.ToListAsync());
    }

    [Fact]
    public async Task SyncUser_ReportsAutomaticAndReviewCounts_WithoutPersistingMessageContent()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: true);
        var handler = new GmailApiHandler(
            new Dictionary<string, string>
            {
                ["high-confidence"] = CreateMessageJson(
                    "high-confidence",
                    "Your application was sent to Contoso",
                    "jobs-noreply@linkedin.com",
                    $"{BodyMarker}\nYour application for Platform Engineer at Contoso",
                    "Platform Resume.pdf",
                    authenticatedFromDomain: "linkedin.com"),
                ["needs-review"] = CreateMessageJson(
                    "needs-review",
                    "Application received - Data Analyst",
                    "careers@fabrikam.test",
                    $"{BodyMarker}\nThank you for applying to Fabrikam")
            });
        var service = CreateService(
            db,
            handler,
            new ApplicationEmailParser());

        var result = await service.SyncUserAsync(UserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AutomaticallyAddedCount);
        Assert.Equal(1, result.ReviewCount);
        Assert.Equal(0, result.LinkedExistingCount);
        Assert.Equal(2, result.ImportedCount);
        Assert.Contains("1 application was automatically added", result.Message);
        Assert.Contains("1 suggestion was sent to review", result.Message);

        var application = await db.JobApplications.SingleAsync();
        Assert.Equal("Contoso", application.CompanyName);
        Assert.Equal("Platform Engineer", application.JobTitle);
        Assert.Equal(ApplicationStatus.Applied, application.Status);

        var imports = await db.ApplicationImports
            .OrderBy(item => item.ExternalMessageId)
            .ToListAsync();
        Assert.Equal(2, imports.Count);
        Assert.Contains(
            imports,
            item => item.Status == ApplicationImportStatus.AutoAccepted);
        Assert.Contains(
            imports,
            item => item.Status == ApplicationImportStatus.PendingReview);

        var persistedText = string.Join(
            '\n',
            imports.SelectMany(item => new[]
            {
                item.ExternalMessageId,
                item.ExternalThreadId,
                item.EmailSubject,
                item.SenderDomain,
                item.CompanyName,
                item.JobTitle,
                item.JobLocation,
                item.JobUrl,
                item.ResumeFileName
            }).Concat(new[]
            {
                application.CompanyName,
                application.JobTitle,
                application.JobLocation,
                application.JobUrl,
                application.Notes
            }).Where(value => value is not null));
        Assert.DoesNotContain(BodyMarker, persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            AttachmentMarker,
            persistedText,
            StringComparison.Ordinal);
        Assert.Null(typeof(ApplicationImport).GetProperty("EmailBody"));
        Assert.Null(typeof(ApplicationImport).GetProperty("AttachmentContent"));
        Assert.Equal(2, handler.MessageDetailRequestCount);
        Assert.Equal(0, handler.AttachmentRequestCount);
    }

    [Fact]
    public async Task SyncUser_RepeatedMessageIds_CreateNoDuplicatesAndReportNoNewEmails()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: true);
        var handler = new GmailApiHandler(
            new Dictionary<string, string>
            {
                ["message-1"] = CreateMessageJson(
                    "message-1",
                    "Your application was sent to Contoso",
                    "jobs-noreply@linkedin.com",
                    "Your application for Platform Engineer at Contoso.",
                    authenticatedFromDomain: "linkedin.com")
            });
        var service = CreateService(
            db,
            handler,
            new ApplicationEmailParser());

        var first = await service.SyncUserAsync(UserId, CancellationToken.None);
        var second = await service.SyncUserAsync(UserId, CancellationToken.None);

        Assert.Equal(1, first.AutomaticallyAddedCount);
        Assert.Equal(0, second.AutomaticallyAddedCount);
        Assert.Equal(0, second.ReviewCount);
        Assert.Equal(0, second.LinkedExistingCount);
        Assert.Contains("No new application emails", second.Message);
        Assert.Single(await db.ApplicationImports.ToListAsync());
        Assert.Single(await db.JobApplications.ToListAsync());
        Assert.Equal(1, handler.MessageDetailRequestCount);
    }

    [Fact]
    public async Task SyncUser_IndeedApplySubject_IsQueriedAndAutomaticallyAdded()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: true);
        var handler = new GmailApiHandler(
            new Dictionary<string, string>
            {
                ["indeed-apply"] = CreateMessageJson(
                    "indeed-apply",
                    "Indeed Application: Full Stack Software Developer (MERN) – Remote",
                    "indeedapply@indeed.com",
                    "Your application has been sent to Contoso.",
                    authenticatedFromDomain: "indeed.com")
            });
        var service = CreateService(
            db,
            handler,
            new ApplicationEmailParser());

        var result = await service.SyncUserAsync(UserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AutomaticallyAddedCount);
        Assert.Contains(
            "subject:\"Indeed Application:\"",
            handler.LastMessageListQuery,
            StringComparison.Ordinal);
        var application = await db.JobApplications.SingleAsync();
        Assert.Equal("Contoso", application.CompanyName);
        Assert.Equal(
            "Full Stack Software Developer (MERN) – Remote",
            application.JobTitle);
        Assert.Equal(JobSource.Indeed, application.Source);
    }

    [Fact]
    public async Task SyncUser_IncompleteKnownIndeedImport_IsRefreshedAndAutomaticallyAdded()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: true);
        var connectionId = await db.GmailConnections
            .Select(connection => connection.Id)
            .SingleAsync();
        db.ApplicationImports.Add(new ApplicationImport
        {
            UserId = UserId,
            GmailConnectionId = connectionId,
            ExternalMessageId = "indeed-existing",
            ExternalThreadId = "thread-indeed-existing",
            Direction = ApplicationImportDirection.Incoming,
            Status = ApplicationImportStatus.PendingReview,
            Confidence = 87,
            EmailSubject = "Indeed Application: Job Title: IT Intern – Internship",
            SenderDomain = "indeed.com",
            CompanyName = string.Empty,
            JobTitle = "Job Title: IT Intern – Internship",
            Source = JobSource.Indeed,
            AppliedDate = new DateOnly(2026, 8, 3),
            DetectedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var handler = new GmailApiHandler(
            new Dictionary<string, string>
            {
                ["indeed-existing"] = CreateMessageJson(
                    "indeed-existing",
                    "Indeed Application: Job Title: IT Intern – Internship",
                    "Indeed Apply <indeedapply@indeed.com>",
                    "Application submitted. Job Title: IT Intern – Internship. "
                    + "NKC SMC PVT LTD - Karachi. "
                    + "The following items were sent to NKC SMC PVT LTD. Good luck!",
                    authenticatedFromDomain: "indeed.com")
            });
        var service = CreateService(
            db,
            handler,
            new ApplicationEmailParser());

        var result = await service.SyncUserAsync(UserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AutomaticallyAddedCount);
        Assert.Equal(0, result.ReviewCount);
        Assert.Equal(1, handler.MessageDetailRequestCount);
        var application = await db.JobApplications.SingleAsync();
        Assert.Equal("NKC SMC PVT LTD", application.CompanyName);
        Assert.Equal("IT Intern – Internship", application.JobTitle);
        var applicationImport = await db.ApplicationImports.SingleAsync();
        Assert.Equal(ApplicationImportStatus.AutoAccepted, applicationImport.Status);
        Assert.Equal(application.Id, applicationImport.CreatedApplicationId);
    }

    [Fact]
    public async Task StartupSync_RetriesErroredConnectionBeforeItsDelayedNextSync()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: true);
        var connection = await db.GmailConnections.SingleAsync();
        connection.LastErrorCode = "authorization_expired";
        connection.NextSyncAt = DateTimeOffset.UtcNow.AddHours(6);
        await db.SaveChangesAsync();

        var handler = new GmailApiHandler(
            new Dictionary<string, string>
            {
                ["indeed-startup-recovery"] = CreateMessageJson(
                    "indeed-startup-recovery",
                    "Indeed Application: Job Title: IT Intern – Internship",
                    "Indeed Apply <indeedapply@indeed.com>",
                    "Application submitted. Job Title: IT Intern – Internship. "
                    + "NKC SMC PVT LTD - Karachi. "
                    + "The following items were sent to NKC SMC PVT LTD. Good luck!",
                    authenticatedFromDomain: "indeed.com")
            });
        var service = CreateService(
            db,
            handler,
            new ApplicationEmailParser());

        await service.SyncDueConnectionsAsync(CancellationToken.None);
        Assert.Equal(0, handler.MessageDetailRequestCount);
        Assert.Empty(await db.JobApplications.ToListAsync());

        await service.SyncStartupConnectionsAsync(CancellationToken.None);

        Assert.Equal(1, handler.MessageDetailRequestCount);
        var application = await db.JobApplications.SingleAsync();
        Assert.Equal("NKC SMC PVT LTD", application.CompanyName);
        Assert.Equal("IT Intern – Internship", application.JobTitle);
        connection = await db.GmailConnections.SingleAsync();
        Assert.Null(connection.LastErrorCode);
        Assert.NotNull(connection.LastSuccessfulSyncAt);
    }

    [Fact]
    public async Task Revocation_pending_connection_is_never_synchronized()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: true);
        var connection = await db.GmailConnections.SingleAsync();
        connection.LastErrorCode = GmailConnectionStates.RevocationPending;
        connection.NextSyncAt = DateTimeOffset.MinValue;
        await db.SaveChangesAsync();
        var handler = new GmailApiHandler(new Dictionary<string, string>());
        var service = CreateService(db, handler, new ApplicationEmailParser());

        var manual = await service.SyncUserAsync(UserId, CancellationToken.None);
        await service.SyncStartupConnectionsAsync(CancellationToken.None);
        await service.SyncDueConnectionsAsync(CancellationToken.None);

        Assert.False(manual.Succeeded);
        Assert.Equal(0, handler.TotalRequestCount);
        Assert.Equal(GmailConnectionStates.RevocationPending,
            (await db.GmailConnections.SingleAsync()).LastErrorCode);
    }

    [Fact]
    public async Task Revocation_started_during_token_refresh_stops_before_Gmail_data_is_read()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = "gmail-revocation-race-" + Guid.NewGuid().ToString("N");
        await using var syncDb = CreateContext(databaseName, databaseRoot);
        await using var disconnectDb = CreateContext(databaseName, databaseRoot);
        await SeedConnectionAsync(syncDb, autoAdd: true);
        var initial = await syncDb.GmailConnections.SingleAsync();
        initial.LastErrorCode = "authorization_expired";
        await syncDb.SaveChangesAsync();

        var handler = new GmailApiHandler(
            new Dictionary<string, string>(),
            async request =>
            {
                if (!request.RequestUri!.Host.Equals(
                        "oauth2.googleapis.com",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var pending = await disconnectDb.GmailConnections.SingleAsync();
                pending.AutoAddHighConfidenceApplications = false;
                pending.NextSyncAt = DateTimeOffset.MaxValue;
                pending.LastErrorCode = GmailConnectionStates.RevocationPending;
                pending.UpdatedAt = DateTimeOffset.UtcNow;
                await disconnectDb.SaveChangesAsync();
            });
        var service = CreateService(syncDb, handler, new ApplicationEmailParser());

        var result = await service.SyncUserAsync(UserId, CancellationToken.None);

        syncDb.ChangeTracker.Clear();
        var persisted = await syncDb.GmailConnections.SingleAsync();
        Assert.False(result.Succeeded);
        Assert.Equal(GmailConnectionStates.RevocationPending, persisted.LastErrorCode);
        Assert.Equal(DateTimeOffset.MaxValue, persisted.NextSyncAt);
        Assert.Equal(1, handler.TotalRequestCount);
    }

    [Fact]
    public async Task Revocation_started_during_message_listing_stops_before_details_or_persistence()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = "gmail-list-revocation-race-" + Guid.NewGuid().ToString("N");
        await using var syncDb = CreateContext(databaseName, databaseRoot);
        await using var disconnectDb = CreateContext(databaseName, databaseRoot);
        await SeedConnectionAsync(syncDb, autoAdd: true);
        var handler = new GmailApiHandler(
            new Dictionary<string, string>
            {
                ["pending-message"] = CreateMessageJson(
                    "pending-message",
                    "Thank you for applying",
                    "jobs@example.test",
                    "Your application was received.")
            },
            async request =>
            {
                if (!request.RequestUri!.Host.Equals(
                        "gmail.googleapis.com",
                        StringComparison.OrdinalIgnoreCase)
                    || !request.RequestUri.AbsolutePath.EndsWith(
                        "/messages",
                        StringComparison.Ordinal))
                {
                    return;
                }

                var pending = await disconnectDb.GmailConnections.SingleAsync();
                pending.AutoAddHighConfidenceApplications = false;
                pending.NextSyncAt = DateTimeOffset.MaxValue;
                pending.LastErrorCode = GmailConnectionStates.RevocationPending;
                pending.UpdatedAt = DateTimeOffset.UtcNow;
                await disconnectDb.SaveChangesAsync();
            });
        var service = CreateService(syncDb, handler, new ApplicationEmailParser());

        var result = await service.SyncUserAsync(UserId, CancellationToken.None);

        syncDb.ChangeTracker.Clear();
        Assert.False(result.Succeeded);
        Assert.Equal(0, handler.MessageDetailRequestCount);
        Assert.Empty(await syncDb.ApplicationImports.ToListAsync());
        Assert.Empty(await syncDb.JobApplications.ToListAsync());
        Assert.Equal(
            GmailConnectionStates.RevocationPending,
            (await syncDb.GmailConnections.SingleAsync()).LastErrorCode);
    }

    [Fact]
    public async Task RevocationPersistedBeforeCompletionGate_IsNotOverwrittenBySyncState()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: false);
        var stateGate = new RevocationBeforeStateActionGate(db);
        var handler = new GmailApiHandler(new Dictionary<string, string>());
        var service = CreateService(
            db,
            handler,
            new ApplicationEmailParser(),
            quotaGate: stateGate);

        var result = await service.SyncUserAsync(UserId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(stateGate.RevocationInjected);
        db.ChangeTracker.Clear();
        var connection = await db.GmailConnections.SingleAsync();
        Assert.Equal(GmailConnectionStates.RevocationPending, connection.LastErrorCode);
        Assert.Equal(DateTimeOffset.MaxValue, connection.NextSyncAt);
        Assert.Null(connection.LastSuccessfulSyncAt);
    }

    [Fact]
    public async Task ImportQuota_RecheckedInsidePersistenceGate_PreventsAtCapInsert()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: false);
        var quotas = new SequencedImportQuotaService(true, false);
        var handler = new GmailApiHandler(
            new Dictionary<string, string>
            {
                ["at-cap-message"] = CreateMessageJson(
                    "at-cap-message",
                    "Thank you for applying",
                    "jobs@example.test",
                    "Thank you for applying for Software Engineer at Contoso.")
            });
        var service = CreateService(
            db,
            handler,
            new ApplicationEmailParser(),
            quotas);

        var result = await service.SyncUserAsync(UserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, quotas.ApplicationImportChecks);
        Assert.Empty(await db.ApplicationImports.ToListAsync());
        Assert.Empty(await db.JobApplications.ToListAsync());
    }

    [Fact]
    public async Task SyncUser_ParserFailureForOneMessage_DoesNotBlockLaterMessage()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: true);
        var handler = new GmailApiHandler(
            new Dictionary<string, string>
            {
                ["malformed"] = CreateMessageJson(
                    "malformed",
                    "Malformed application",
                    "jobs@example.test",
                    "Malformed parser input."),
                ["valid"] = CreateMessageJson(
                    "valid",
                    "Valid application",
                    "jobs@example.test",
                    "Valid parser input.")
            });
        var service = CreateService(
            db,
            handler,
            new ThrowThenParseApplicationEmailParser());

        var result = await service.SyncUserAsync(UserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AutomaticallyAddedCount);
        Assert.Equal(0, result.ReviewCount);
        Assert.Single(await db.ApplicationImports.ToListAsync());
        Assert.Single(await db.JobApplications.ToListAsync());
        Assert.Equal(
            "valid",
            (await db.ApplicationImports.SingleAsync()).ExternalMessageId);
    }

    private static ApplicationDbContext CreateContext(
        string? databaseName = null,
        InMemoryDatabaseRoot? databaseRoot = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(
                databaseName ?? "gmail-import-service-" + Guid.NewGuid().ToString("N"),
                databaseRoot ?? new InMemoryDatabaseRoot())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedConnectionAsync(
        ApplicationDbContext db,
        bool autoAdd)
    {
        db.Users.Add(new IdentityUser
        {
            Id = UserId,
            UserName = "candidate@example.test",
            NormalizedUserName = "CANDIDATE@EXAMPLE.TEST",
            Email = "candidate@example.test",
            NormalizedEmail = "CANDIDATE@EXAMPLE.TEST"
        });
        var now = DateTimeOffset.UtcNow;
        db.GmailConnections.Add(new GmailConnection
        {
            UserId = UserId,
            EmailAddress = "candidate@gmail.test",
            ProtectedRefreshToken = "protected-refresh-token",
            ConnectedAt = now,
            UpdatedAt = now,
            NextSyncAt = now,
            AutoAddHighConfidenceApplications = autoAdd
        });
        await db.SaveChangesAsync();
    }

    private static GmailImportService CreateService(
        ApplicationDbContext db,
        HttpMessageHandler handler,
        IApplicationEmailParser parser,
        IWorkspaceQuotaService? quotas = null,
        IWorkspaceQuotaGate? quotaGate = null,
        bool gmailImportEnabled = true)
    {
        var processor = new ApplicationImportProcessor(
            db,
            NullLogger<ApplicationImportProcessor>.Instance);
        return new GmailImportService(
            db,
            new StubHttpClientFactory(handler),
            new StubCredentialProtector(),
            parser,
            processor,
            Options.Create(new GoogleIntegrationOptions
            {
                ClientId =
                    "fictional-client.apps.googleusercontent.com",
                ClientSecret = "fictional-client-secret",
                GmailImportEnabled = gmailImportEnabled,
                GmailAutoSyncEnabled = true,
                GmailSyncIntervalMinutes = 15,
                GmailInitialLookbackDays = 30,
                GmailMaxMessagesPerSync = 25
            }),
            NullLogger<GmailImportService>.Instance,
            quotas,
            operationLocks: null,
            quotaGate: quotaGate);
    }

    private static string CreateMessageJson(
        string messageId,
        string subject,
        string from,
        string body,
        string? attachmentFileName = null,
        string? authenticatedFromDomain = null)
    {
        var parts = new List<object>
        {
            new
            {
                mimeType = "text/plain",
                filename = "",
                body = new { data = EncodeBase64Url(body) }
            }
        };
        if (attachmentFileName is not null)
        {
            parts.Add(new
            {
                mimeType = "application/pdf",
                filename = attachmentFileName,
                body = new
                {
                    data = EncodeBase64Url(AttachmentMarker),
                    attachmentId = "attachment-1"
                }
            });
        }

        var headers = new List<object>
        {
            new { name = "Subject", value = subject },
            new { name = "From", value = from },
            new
            {
                name = "To",
                value = "candidate@example.test"
            }
        };
        if (!string.IsNullOrWhiteSpace(authenticatedFromDomain))
        {
            headers.Add(new
            {
                name = "Authentication-Results",
                value = $"mx.google.com; dmarc=pass header.from={authenticatedFromDomain}"
            });
        }

        return JsonSerializer.Serialize(new
        {
            id = messageId,
            threadId = "thread-" + messageId,
            internalDate = new DateTimeOffset(
                    2026,
                    7,
                    28,
                    12,
                    0,
                    0,
                    TimeSpan.Zero)
                .ToUnixTimeMilliseconds()
                .ToString(),
            labelIds = new[] { "INBOX" },
            snippet = body,
            payload = new
            {
                mimeType = "multipart/mixed",
                filename = "",
                headers,
                parts
            }
        });
    }

    private static string EncodeBase64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class StubHttpClientFactory(
        HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler);

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubCredentialProtector : IGmailCredentialProtector
    {
        public string Protect(string refreshToken) => refreshToken;

        public string Unprotect(string protectedRefreshToken) =>
            "fictional-refresh-token";
    }

    private sealed class ThrowThenParseApplicationEmailParser
        : IApplicationEmailParser
    {
        public ApplicationImportSuggestion? Parse(GmailMessageEnvelope message)
        {
            if (message.MessageId == "malformed")
            {
                throw new FormatException("Fictional malformed message.");
            }

            return new ApplicationImportSuggestion(
                ApplicationImportDirection.Incoming,
                JobSource.CompanyWebsite,
                95,
                "Adventure Works",
                "Site Reliability Engineer",
                "Karachi",
                "https://careers.adventure-works.test/jobs/55",
                new DateOnly(2026, 7, 28),
                null,
                "adventure-works.test");
        }
    }

    private sealed class SequencedImportQuotaService(
        params bool[] applicationImportResults) : IWorkspaceQuotaService
    {
        private readonly Queue<bool> _applicationImportResults =
            new(applicationImportResults);

        public int ApplicationImportChecks { get; private set; }

        public Task<bool> CanCreateApplicationImportAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            ApplicationImportChecks++;
            return Task.FromResult(_applicationImportResults.Dequeue());
        }

        public Task<bool> CanCreateApplicationAsync(
            string userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> CanCreateInterviewAsync(
            string userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> CanCreateAnalysisAsync(
            string userId,
            long incomingSnapshotBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> CanCreateAnalysesAsync(
            string userId,
            int incomingCount,
            long incomingSnapshotBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> CanCreateScamCheckAsync(
            string userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RevocationBeforeStateActionGate(
        ApplicationDbContext db) : IWorkspaceQuotaGate
    {
        public bool RevocationInjected { get; private set; }

        public async Task<TResult> RunAsync<TResult>(
            string resource,
            string userId,
            Func<CancellationToken, Task<TResult>> action,
            CancellationToken cancellationToken = default)
        {
            if (resource == WorkspaceQuotaResources.GmailConnections
                && !RevocationInjected)
            {
                var connection = await db.GmailConnections.SingleAsync(
                    item => item.UserId == userId,
                    cancellationToken);
                connection.AutoAddHighConfidenceApplications = false;
                connection.NextSyncAt = DateTimeOffset.MaxValue;
                connection.LastErrorCode = GmailConnectionStates.RevocationPending;
                connection.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                RevocationInjected = true;
            }

            return await action(cancellationToken);
        }
    }

    private sealed class GmailApiHandler(
        IReadOnlyDictionary<string, string> messages,
        Func<HttpRequestMessage, Task>? beforeRequest = null) : HttpMessageHandler
    {
        public int TotalRequestCount { get; private set; }
        public int MessageDetailRequestCount { get; private set; }
        public int AttachmentRequestCount { get; private set; }
        public string LastMessageListQuery { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            TotalRequestCount++;
            if (beforeRequest is not null) await beforeRequest(request);
            var uri = request.RequestUri
                ?? throw new InvalidOperationException("A request URI is required.");
            if (uri.Host.Equals(
                    "oauth2.googleapis.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                return await JsonResponse("""{"access_token":"fictional-access-token"}""");
            }

            if (uri.AbsolutePath.EndsWith(
                    "/messages",
                    StringComparison.Ordinal))
            {
                LastMessageListQuery = ParseQueryParameter(uri.Query, "q");
                var payload = JsonSerializer.Serialize(new
                {
                    messages = messages.Keys.Select(id => new { id }).ToArray()
                });
                return await JsonResponse(payload);
            }

            if (uri.AbsolutePath.Contains(
                    "/attachments/",
                    StringComparison.Ordinal))
            {
                AttachmentRequestCount++;
                return await JsonResponse(
                    JsonSerializer.Serialize(new
                    {
                        data = EncodeBase64Url(AttachmentMarker)
                    }));
            }

            var messageId = uri.Segments.Last().Trim('/');
            if (messages.TryGetValue(messageId, out var message))
            {
                MessageDetailRequestCount++;
                return await JsonResponse(message);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static string ParseQueryParameter(string query, string name)
        {
            foreach (var part in query.TrimStart('?').Split('&'))
            {
                var pieces = part.Split('=', 2);
                if (pieces.Length == 2
                    && Uri.UnescapeDataString(pieces[0]).Equals(
                        name,
                        StringComparison.Ordinal))
                {
                    return Uri.UnescapeDataString(pieces[1].Replace('+', ' '));
                }
            }

            return string.Empty;
        }

        private static Task<HttpResponseMessage> JsonResponse(
            string payload) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    payload,
                    Encoding.UTF8,
                    "application/json")
            });
    }
}
