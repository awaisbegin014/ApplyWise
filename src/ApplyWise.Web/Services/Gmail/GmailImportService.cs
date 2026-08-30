using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ApplyWise.Web.Services.Security;

namespace ApplyWise.Web.Services.Gmail;

public sealed record GmailSyncResult(
    bool Succeeded,
    int AutomaticallyAddedCount,
    int ReviewCount,
    int LinkedExistingCount,
    string Message)
{
    public int ImportedCount =>
        AutomaticallyAddedCount + ReviewCount + LinkedExistingCount;
}

public interface IGmailImportService
{
    Task<GmailSyncResult> SyncUserAsync(string userId, CancellationToken cancellationToken);
    Task SyncStartupConnectionsAsync(CancellationToken cancellationToken);
    Task SyncDueConnectionsAsync(CancellationToken cancellationToken);
}

public sealed class GmailImportService(
    ApplicationDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IGmailCredentialProtector credentialProtector,
    IApplicationEmailParser emailParser,
    IApplicationImportProcessor importProcessor,
    IOptions<GoogleIntegrationOptions> options,
    ILogger<GmailImportService> logger,
    IWorkspaceQuotaService? quotas = null,
    IApplicationLockProvider? operationLocks = null,
    IWorkspaceQuotaGate? quotaGate = null) : IGmailImportService
{
    private readonly GoogleIntegrationOptions _options = options.Value;
    private readonly IApplicationLockProvider _operationLocks =
        operationLocks ?? new ApplicationLockProvider(dbContext);
    private readonly IWorkspaceQuotaGate _quotaGate =
        quotaGate ?? new WorkspaceQuotaGate(dbContext);

    public async Task<GmailSyncResult> SyncUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (!_options.IsGmailImportConfigured)
        {
            return new GmailSyncResult(
                false,
                0,
                0,
                0,
                "Gmail import is not enabled on this ApplyWise deployment.");
        }

        var connectionId = await dbContext.GmailConnections
            .Where(connection =>
                connection.UserId == userId
                && connection.LastErrorCode != GmailConnectionStates.RevocationPending)
            .Select(connection => (int?)connection.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (!connectionId.HasValue)
        {
            return new GmailSyncResult(
                false,
                0,
                0,
                0,
                "Connect Gmail before syncing applications.");
        }

        return await SyncConnectionAsync(connectionId.Value, userId, cancellationToken);
    }

    public Task SyncStartupConnectionsAsync(CancellationToken cancellationToken) =>
        SyncConnectionsAsync(includeAllConnections: true, cancellationToken);

    public Task SyncDueConnectionsAsync(CancellationToken cancellationToken) =>
        SyncConnectionsAsync(includeAllConnections: false, cancellationToken);

    private async Task SyncConnectionsAsync(
        bool includeAllConnections,
        CancellationToken cancellationToken)
    {
        if (!_options.IsGmailImportConfigured || !_options.GmailAutoSyncEnabled) return;

        var now = DateTimeOffset.UtcNow;
        var connectionIds = await dbContext.GmailConnections
            .AsNoTracking()
            .Where(connection =>
                connection.LastErrorCode != GmailConnectionStates.RevocationPending
                && (includeAllConnections || connection.NextSyncAt <= now))
            .OrderBy(connection => connection.NextSyncAt)
            .Select(connection => connection.Id)
            .Take(20)
            .ToListAsync(cancellationToken);
        logger.LogDebug(
            "Automatic Gmail {CycleType} sync found {ConnectionCount} eligible connections.",
            includeAllConnections ? "startup" : "scheduled",
            connectionIds.Count);

        foreach (var connectionId in connectionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SyncConnectionAsync(connectionId, expectedUserId: null, cancellationToken);
            dbContext.ChangeTracker.Clear();
        }
    }

    private async Task<GmailSyncResult> SyncConnectionAsync(
        int connectionId,
        string? expectedUserId,
        CancellationToken cancellationToken)
    {
        var connectionOwnerId = expectedUserId;
        if (string.IsNullOrWhiteSpace(connectionOwnerId))
        {
            connectionOwnerId = await dbContext.GmailConnections
                .AsNoTracking()
                .Where(connection => connection.Id == connectionId)
                .Select(connection => connection.UserId)
                .SingleOrDefaultAsync(cancellationToken);
        }
        if (string.IsNullOrWhiteSpace(connectionOwnerId))
        {
            return new GmailSyncResult(
                false,
                0,
                0,
                0,
                "The Gmail connection was not found.");
        }

        var connectionLease = await _operationLocks.TryAcquireAsync(
            $"gmail-user:{connectionOwnerId}",
            TimeSpan.Zero,
            cancellationToken);
        if (connectionLease is null)
        {
            return new GmailSyncResult(
                true,
                0,
                0,
                0,
                "A Gmail sync is already running.");
        }

        await using (connectionLease)
        {
            var connection = await dbContext.GmailConnections
                .SingleOrDefaultAsync(item =>
                    item.Id == connectionId
                    && item.UserId == connectionOwnerId,
                    cancellationToken);
            if (connection is null)
            {
                return new GmailSyncResult(
                    false,
                    0,
                    0,
                    0,
                    "The Gmail connection was not found.");
            }

            if (connection.LastErrorCode == GmailConnectionStates.RevocationPending)
            {
                return RevocationPendingResult();
            }

            var now = DateTimeOffset.UtcNow;
            logger.LogInformation(
                "Gmail sync started for connection {ConnectionId}. Auto-add enabled: {AutoAddEnabled}. Previous next sync: {PreviousNextSyncAt}.",
                connection.Id,
                connection.AutoAddHighConfidenceApplications,
                connection.NextSyncAt);
            connection.LastSyncStartedAt = now;
            connection.NextSyncAt = now.AddMinutes(
                Math.Clamp(_options.GmailSyncIntervalMinutes, 5, 24 * 60));
            await dbContext.SaveChangesAsync(cancellationToken);

            try
            {
                using var syncCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                syncCancellation.CancelAfter(TimeSpan.FromSeconds(_options.GmailSyncTimeoutSeconds));
                var syncToken = syncCancellation.Token;
                dbContext.ChangeTracker.Clear();
                connection = await LoadActiveConnectionAsync(
                    connectionId,
                    connectionOwnerId,
                    syncToken);
                if (connection is null)
                {
                    return RevocationPendingResult();
                }
                var refreshToken = credentialProtector.Unprotect(connection.ProtectedRefreshToken);
                var accessToken = await RefreshAccessTokenAsync(refreshToken, syncToken);
                dbContext.ChangeTracker.Clear();
                connection = await LoadActiveConnectionAsync(
                    connectionId,
                    connectionOwnerId,
                    syncToken);
                if (connection is null)
                {
                    return RevocationPendingResult();
                }
                var importResult = await ImportMessagesAsync(
                    connection,
                    accessToken,
                    syncToken);

                var completionSaved = await UpdateConnectionIfActiveAsync(
                    connectionId,
                    connectionOwnerId,
                    candidate =>
                    {
                        candidate.LastSuccessfulSyncAt = DateTimeOffset.UtcNow;
                        candidate.UpdatedAt = DateTimeOffset.UtcNow;
                        candidate.LastErrorCode = null;
                    },
                    cancellationToken);
                if (!completionSaved)
                {
                    return RevocationPendingResult();
                }
                logger.LogInformation(
                    "Gmail sync completed for connection {ConnectionId}. Automatically added: {AutomaticallyAddedCount}; review: {ReviewCount}; linked: {LinkedExistingCount}.",
                    connectionId,
                    importResult.AutomaticallyAddedCount,
                    importResult.ReviewCount,
                    importResult.LinkedExistingCount);
                return new GmailSyncResult(
                    true,
                    importResult.AutomaticallyAddedCount,
                    importResult.ReviewCount,
                    importResult.LinkedExistingCount,
                    BuildSyncMessage(importResult));
            }
            catch (GmailAuthorizationException exception)
            {
                var failureSaved = await UpdateConnectionIfActiveAsync(
                    connectionId,
                    connectionOwnerId,
                    candidate =>
                    {
                        candidate.LastErrorCode = "authorization_expired";
                        candidate.NextSyncAt = DateTimeOffset.UtcNow.AddHours(6);
                    },
                    cancellationToken);
                logger.LogWarning(
                    "Gmail authorization needs attention for connection {ConnectionId}: {Reason}.",
                    connectionId,
                    exception.Reason);
                return failureSaved
                    ? new GmailSyncResult(
                    false,
                    0,
                    0,
                    0,
                    "Gmail authorization expired. Disconnect and reconnect Gmail to continue.")
                    : RevocationPendingResult();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var failureSaved = await UpdateConnectionIfActiveAsync(
                    connectionId,
                    connectionOwnerId,
                    candidate =>
                    {
                        candidate.LastErrorCode = "sync_failed";
                        candidate.NextSyncAt = DateTimeOffset.UtcNow.AddMinutes(30);
                    },
                    CancellationToken.None);
                logger.LogError(
                    exception,
                    "Gmail sync failed for connection {ConnectionId}.",
                    connectionId);
                return failureSaved
                    ? new GmailSyncResult(
                    false,
                    0,
                    0,
                    0,
                    "Gmail could not be synced right now. ApplyWise will try again.")
                    : RevocationPendingResult();
            }
        }
    }

    private async Task<string> RefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("GoogleOAuth");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            })
        };
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GmailAuthorizationException(
                response.StatusCode == System.Net.HttpStatusCode.BadRequest
                    ? "refresh_rejected"
                    : "token_endpoint_failed");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("access_token", out var accessToken)
            || string.IsNullOrWhiteSpace(accessToken.GetString()))
        {
            throw new GmailAuthorizationException("access_token_missing");
        }

        return accessToken.GetString()!;
    }

    private async Task<GmailImportBatchResult> ImportMessagesAsync(
        GmailConnection connection,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var maxMessages = Math.Clamp(_options.GmailMaxMessagesPerSync, 25, 500);
        var lookbackDays = Math.Clamp(_options.GmailInitialLookbackDays, 1, 90);
        var query =
            $"newer_than:{lookbackDays}d (\"thank you for applying\" OR \"thanks for applying\" OR \"application received\" OR \"received your application\" OR \"application submitted\" OR \"application has been submitted\" OR \"application was sent\" OR \"application has been sent\" OR subject:\"Indeed Application:\" OR ((from:indeed.com OR from:indeedemail.com OR from:indeedmail.com OR from:linkedin.com) subject:application) OR (in:sent has:attachment (filename:pdf OR filename:doc OR filename:docx)))";
        var result = new GmailImportBatchResult();
        var inspectedCount = 0;
        string? pageToken = null;

        if (!await IsConnectionActiveAsync(connection, cancellationToken))
        {
            return result;
        }

        if (connection.AutoAddHighConfidenceApplications)
        {
            var existingEligibleIds = await dbContext.ApplicationImports
                .AsNoTracking()
                .Where(import =>
                    import.GmailConnectionId == connection.Id
                    && import.UserId == connection.UserId
                    && import.Status == ApplicationImportStatus.PendingReview
                    && import.Direction == ApplicationImportDirection.Incoming
                    && import.Confidence >= ApplicationImportPolicy.HighConfidenceThreshold
                    && import.CompanyName != string.Empty
                    && import.JobTitle != string.Empty
                    && import.AppliedDate.HasValue)
                .OrderBy(import => import.DetectedAt)
                .Select(import => import.Id)
                .Take(500)
                .ToListAsync(cancellationToken);
            foreach (var importId in existingEligibleIds)
            {
                if (!await IsConnectionActiveAsync(connection, cancellationToken))
                {
                    return result;
                }
                await ApplyAutomaticOutcomeAsync(
                    importId,
                    connection.UserId,
                    result,
                    countAsReviewWhenNotEligible: false,
                    cancellationToken);
            }
        }

        do
        {
            var page = await ListMessageIdsAsync(
                accessToken,
                query,
                Math.Min(100, maxMessages - inspectedCount),
                pageToken,
                cancellationToken);
            if (!await IsConnectionActiveAsync(connection, cancellationToken))
            {
                return result;
            }
            logger.LogInformation(
                "Gmail search returned {MessageCount} messages for connection {ConnectionId}.",
                page.MessageIds.Count,
                connection.Id);
            if (page.MessageIds.Count == 0) break;
            inspectedCount += page.MessageIds.Count;

            var knownImports = await dbContext.ApplicationImports
                .AsNoTracking()
                .Where(import =>
                    import.GmailConnectionId == connection.Id
                    && page.MessageIds.Contains(import.ExternalMessageId))
                .Select(import => new
                {
                    import.Id,
                    import.ExternalMessageId,
                    Refreshable =
                        import.Status == ApplicationImportStatus.PendingReview
                        && import.Direction == ApplicationImportDirection.Incoming
                        && !import.CreatedApplicationId.HasValue
                        && (import.CompanyName == string.Empty
                            || import.JobTitle == string.Empty
                            || import.Confidence < ApplicationImportPolicy.HighConfidenceThreshold)
                })
                .ToListAsync(cancellationToken);
            var known = knownImports
                .Select(import => import.ExternalMessageId)
                .ToHashSet(StringComparer.Ordinal);
            var refreshableImportIds = knownImports
                .Where(import => import.Refreshable)
                .ToDictionary(
                    import => import.ExternalMessageId,
                    import => import.Id,
                    StringComparer.Ordinal);
            logger.LogInformation(
                "Gmail page for connection {ConnectionId} contained {KnownCount} known messages and {RefreshableCount} refreshable incomplete imports.",
                connection.Id,
                known.Count,
                refreshableImportIds.Count);

            foreach (var messageId in page.MessageIds)
            {
                var isRefresh = refreshableImportIds.TryGetValue(
                    messageId,
                    out var refreshableImportId);
                if (known.Contains(messageId) && !isRefresh) continue;
                if (!await IsConnectionActiveAsync(connection, cancellationToken))
                {
                    return result;
                }
                var message = await GetMessageAsync(accessToken, messageId, cancellationToken);
                if (!await IsConnectionActiveAsync(connection, cancellationToken))
                {
                    return result;
                }
                ApplicationImportSuggestion? suggestion;
                try
                {
                    suggestion = emailParser.Parse(message);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        "Gmail message {MessageId} for connection {ConnectionId} could not be parsed. Failure category: {FailureCategory}.",
                        messageId,
                        connection.Id,
                        exception.GetType().Name);
                    continue;
                }
                if (suggestion is null)
                {
                    logger.LogInformation(
                        "Gmail message {MessageId} for connection {ConnectionId} was not recognized as an application confirmation.",
                        messageId,
                        connection.Id);
                    continue;
                }
                logger.LogInformation(
                    "Gmail message {MessageId} for connection {ConnectionId} parsed with source {Source}, confidence {Confidence}, company present {HasCompany}, title present {HasJobTitle}, refresh {IsRefresh}.",
                    messageId,
                    connection.Id,
                    suggestion.Source,
                    suggestion.Confidence,
                    !string.IsNullOrWhiteSpace(suggestion.CompanyName),
                    !string.IsNullOrWhiteSpace(suggestion.JobTitle),
                    isRefresh);

                if (!await IsConnectionActiveAsync(connection, cancellationToken))
                {
                    return result;
                }

                ApplicationImport applicationImport;
                if (isRefresh)
                {
                    applicationImport = await dbContext.ApplicationImports
                        .SingleOrDefaultAsync(import =>
                            import.Id == refreshableImportId
                            && import.UserId == connection.UserId
                            && import.GmailConnectionId == connection.Id
                            && import.Status == ApplicationImportStatus.PendingReview
                            && !import.CreatedApplicationId.HasValue,
                            cancellationToken)
                        ?? throw new InvalidOperationException(
                            "The incomplete application import was no longer available for refresh.");
                    ApplySuggestion(applicationImport, message, suggestion);
                    if (!await IsConnectionActiveAsync(connection, cancellationToken))
                    {
                        return result;
                    }
                    if (!await SaveChangesIfConnectionActiveAsync(
                            connection,
                            cancellationToken))
                    {
                        dbContext.Entry(applicationImport).State = EntityState.Detached;
                        return result;
                    }
                }
                else
                {
                    if (quotas is not null
                        && !await quotas.CanCreateApplicationImportAsync(
                            connection.UserId,
                            cancellationToken))
                    {
                        logger.LogWarning(
                            "Gmail import quota reached for connection {ConnectionId}; remaining messages were deferred.",
                            connection.Id);
                        break;
                    }

                    if (!await IsConnectionActiveAsync(connection, cancellationToken))
                    {
                        return result;
                    }

                    applicationImport = new ApplicationImport
                    {
                        UserId = connection.UserId,
                        GmailConnectionId = connection.Id,
                        ExternalMessageId = message.MessageId,
                        Status = ApplicationImportStatus.PendingReview,
                        DetectedAt = DateTimeOffset.UtcNow
                    };
                    ApplySuggestion(applicationImport, message, suggestion);
                    dbContext.ApplicationImports.Add(applicationImport);
                    try
                    {
                        if (!await SaveChangesIfConnectionActiveAsync(
                                connection,
                                cancellationToken))
                        {
                            dbContext.Entry(applicationImport).State = EntityState.Detached;
                            return result;
                        }
                    }
                    catch (DbUpdateException exception)
                        when (IsDuplicateMessageViolation(exception))
                    {
                        dbContext.Entry(applicationImport).State = EntityState.Detached;
                        logger.LogInformation(
                            "Gmail message {MessageId} for connection {ConnectionId} was already imported by another sync.",
                            messageId,
                            connection.Id);
                        continue;
                    }
                }

                known.Add(messageId);
                if (!await IsConnectionActiveAsync(connection, cancellationToken))
                {
                    return result;
                }
                await ApplyAutomaticOutcomeAsync(
                    applicationImport.Id,
                    connection.UserId,
                    result,
                    countAsReviewWhenNotEligible: !isRefresh,
                    cancellationToken);
            }

            pageToken = inspectedCount >= maxMessages ? null : page.NextPageToken;
        } while (!string.IsNullOrWhiteSpace(pageToken));

        return result;
    }

    private static void ApplySuggestion(
        ApplicationImport applicationImport,
        GmailMessageEnvelope message,
        ApplicationImportSuggestion suggestion)
    {
        applicationImport.ExternalThreadId = TruncateNullable(message.ThreadId, 200);
        applicationImport.Direction = suggestion.Direction;
        applicationImport.Confidence = suggestion.Confidence;
        applicationImport.EmailSubject = Truncate(message.Subject, 500);
        applicationImport.SenderDomain = TruncateNullable(suggestion.SenderDomain, 255);
        applicationImport.CompanyName = Truncate(
            suggestion.CompanyName ?? string.Empty,
            150);
        applicationImport.JobTitle = Truncate(
            suggestion.JobTitle ?? string.Empty,
            150);
        applicationImport.JobLocation = TruncateNullable(suggestion.JobLocation, 150);
        applicationImport.Source = suggestion.Source;
        applicationImport.JobUrl = TruncateNullable(suggestion.JobUrl, 2048);
        applicationImport.AppliedDate = suggestion.AppliedDate;
        applicationImport.ResumeFileName = TruncateNullable(suggestion.ResumeFileName, 255);
    }

    private async Task ApplyAutomaticOutcomeAsync(
        int importId,
        string userId,
        GmailImportBatchResult result,
        bool countAsReviewWhenNotEligible,
        CancellationToken cancellationToken)
    {
        try
        {
            var processingResult = await importProcessor.TryAutoAcceptAsync(
                importId,
                userId,
                cancellationToken);
            logger.LogInformation(
                "Application import {ImportId} automatic processing completed with outcome {Outcome}.",
                importId,
                processingResult.Outcome);
            switch (processingResult.Outcome)
            {
                case ApplicationImportProcessOutcome.Created:
                    result.AutomaticallyAddedCount++;
                    break;
                case ApplicationImportProcessOutcome.LinkedExisting:
                case ApplicationImportProcessOutcome.AlreadyProcessed:
                    result.LinkedExistingCount++;
                    break;
                default:
                    if (countAsReviewWhenNotEligible)
                    {
                        result.ReviewCount++;
                    }
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            dbContext.ChangeTracker.Clear();
            logger.LogError(
                "Application import {ImportId} for user {UserId} could not be processed automatically. Failure category: {FailureCategory}.",
                importId,
                userId,
                exception.GetType().Name);
            if (countAsReviewWhenNotEligible)
            {
                result.ReviewCount++;
            }
        }
    }

    private async Task<GmailMessagePage> ListMessageIdsAsync(
        string accessToken,
        string query,
        int maxResults,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        var uri = new StringBuilder(
            "https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=")
            .Append(maxResults.ToString(CultureInfo.InvariantCulture))
            .Append("&q=")
            .Append(Uri.EscapeDataString(query));
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            uri.Append("&pageToken=").Append(Uri.EscapeDataString(pageToken));
        }

        using var document = await SendGmailRequestAsync(
            accessToken,
            uri.ToString(),
            cancellationToken);
        var ids = new List<string>();
        if (document.RootElement.TryGetProperty("messages", out var messages))
        {
            foreach (var message in messages.EnumerateArray())
            {
                if (message.TryGetProperty("id", out var id)
                    && !string.IsNullOrWhiteSpace(id.GetString()))
                {
                    ids.Add(id.GetString()!);
                }
            }
        }

        var nextPageToken = document.RootElement.TryGetProperty("nextPageToken", out var token)
            ? token.GetString()
            : null;
        return new GmailMessagePage(ids, nextPageToken);
    }

    private async Task<GmailMessageEnvelope> GetMessageAsync(
        string accessToken,
        string messageId,
        CancellationToken cancellationToken)
    {
        var uri =
            $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}?format=full";
        using var document = await SendGmailRequestAsync(accessToken, uri, cancellationToken);
        var root = document.RootElement;
        var payload = root.GetProperty("payload");
        var headers = ReadHeaders(payload);
        var body = new StringBuilder();
        var attachmentNames = new List<string>();
        ReadPayload(payload, body, attachmentNames);

        var internalDate = root.TryGetProperty("internalDate", out var dateProperty)
            && long.TryParse(dateProperty.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : DateTimeOffset.UtcNow;
        var labels = root.TryGetProperty("labelIds", out var labelIds)
            ? labelIds.EnumerateArray()
                .Select(label => label.GetString())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Cast<string>()
                .ToArray()
            : [];

        return new GmailMessageEnvelope(
            root.GetProperty("id").GetString() ?? messageId,
            root.TryGetProperty("threadId", out var threadId) ? threadId.GetString() : null,
            GetHeader(headers, "Subject"),
            GetHeader(headers, "From"),
            GetHeader(headers, "To"),
            body.ToString(),
            root.TryGetProperty("snippet", out var snippet) ? snippet.GetString() ?? string.Empty : string.Empty,
            labels,
            attachmentNames,
            internalDate,
            GetHeader(headers, "Authentication-Results"));
    }

    private async Task<JsonDocument> SendGmailRequestAsync(
        string accessToken,
        string uri,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("Gmail");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new GmailAuthorizationException("access_token_rejected");
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > _options.GmailMaxResponseBytes)
        {
            throw new InvalidDataException("The Gmail response exceeded the configured byte limit.");
        }
        await response.Content.LoadIntoBufferAsync(
            _options.GmailMaxResponseBytes,
            cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static Dictionary<string, string> ReadHeaders(JsonElement payload)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!payload.TryGetProperty("headers", out var values)) return headers;
        foreach (var item in values.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameProperty)
                ? nameProperty.GetString()
                : null;
            var value = item.TryGetProperty("value", out var valueProperty)
                ? valueProperty.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(name) && value is not null)
            {
                // Gmail prepends its receiving-boundary headers. Keep the first
                // value so a sender-supplied duplicate cannot replace it.
                headers.TryAdd(name, value);
            }
        }

        return headers;
    }

    private static string GetHeader(
        IReadOnlyDictionary<string, string> headers,
        string name) =>
        headers.TryGetValue(name, out var value) ? value : string.Empty;

    private static void ReadPayload(
        JsonElement payload,
        StringBuilder body,
        ICollection<string> attachmentNames)
    {
        if (payload.TryGetProperty("filename", out var filenameProperty)
            && !string.IsNullOrWhiteSpace(filenameProperty.GetString()))
        {
            attachmentNames.Add(filenameProperty.GetString()!);
        }

        var mimeType = payload.TryGetProperty("mimeType", out var mimeTypeProperty)
            ? mimeTypeProperty.GetString() ?? string.Empty
            : string.Empty;
        if ((mimeType.Equals("text/plain", StringComparison.OrdinalIgnoreCase)
             || mimeType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
            && payload.TryGetProperty("body", out var bodyProperty)
            && bodyProperty.TryGetProperty("data", out var dataProperty)
            && body.Length < 200_000)
        {
            var decoded = DecodeBase64Url(dataProperty.GetString());
            var remaining = 200_000 - body.Length;
            body.Append(decoded.Length <= remaining ? decoded : decoded[..remaining]);
            body.AppendLine();
        }

        if (!payload.TryGetProperty("parts", out var parts)) return;
        foreach (var part in parts.EnumerateArray())
        {
            ReadPayload(part, body, attachmentNames);
        }
    }

    private static string DecodeBase64Url(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        const int maximumEncodedCharacters = 300_000;
        if (value.Length > maximumEncodedCharacters)
        {
            value = value[..maximumEncodedCharacters];
        }
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        try
        {
            var bytes = Convert.FromBase64String(normalized);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? TruncateNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength ? value : value[..maxLength];

    private Task<GmailConnection?> LoadActiveConnectionAsync(
        int connectionId,
        string? expectedUserId,
        CancellationToken cancellationToken) =>
        dbContext.GmailConnections.SingleOrDefaultAsync(
            connection =>
                connection.Id == connectionId
                && connection.LastErrorCode != GmailConnectionStates.RevocationPending
                && (expectedUserId == null
                    || connection.UserId == expectedUserId),
            cancellationToken);

    private Task<bool> IsConnectionActiveAsync(
        GmailConnection connection,
        CancellationToken cancellationToken) =>
        dbContext.GmailConnections
            .AsNoTracking()
            .AnyAsync(candidate =>
                candidate.Id == connection.Id
                && candidate.UserId == connection.UserId
                && candidate.LastErrorCode != GmailConnectionStates.RevocationPending,
                cancellationToken);

    private async Task<bool> SaveChangesIfConnectionActiveAsync(
        GmailConnection connection,
        CancellationToken cancellationToken)
    {
        return await _quotaGate.RunAsync(
            WorkspaceQuotaResources.ApplicationImports,
            connection.UserId,
            async gateCancellationToken =>
            {
                var addedImportIsStaged = dbContext.ChangeTracker
                    .Entries<ApplicationImport>()
                    .Any(entry =>
                        entry.State == EntityState.Added
                        && entry.Entity.UserId == connection.UserId);
                if (addedImportIsStaged
                    && quotas is not null
                    && !await quotas.CanCreateApplicationImportAsync(
                        connection.UserId,
                        gateCancellationToken))
                {
                    logger.LogWarning(
                        "Gmail import quota was reached before persistence for connection {ConnectionId}.",
                        connection.Id);
                    return false;
                }

                var active = await dbContext.GmailConnections
                    .AsNoTracking()
                    .AnyAsync(candidate =>
                        candidate.Id == connection.Id
                        && candidate.UserId == connection.UserId
                        && candidate.LastErrorCode != GmailConnectionStates.RevocationPending,
                        gateCancellationToken);
                if (!active) return false;

                await dbContext.SaveChangesAsync(gateCancellationToken);
                return true;
            },
            cancellationToken);
    }

    private Task<bool> UpdateConnectionIfActiveAsync(
        int connectionId,
        string userId,
        Action<GmailConnection> update,
        CancellationToken cancellationToken) =>
        _quotaGate.RunAsync(
            WorkspaceQuotaResources.GmailConnections,
            userId,
            async gateCancellationToken =>
            {
                dbContext.ChangeTracker.Clear();
                var connection = await dbContext.GmailConnections
                    .SingleOrDefaultAsync(candidate =>
                        candidate.Id == connectionId
                        && candidate.UserId == userId
                        && candidate.LastErrorCode
                            != GmailConnectionStates.RevocationPending,
                        gateCancellationToken);
                if (connection is null) return false;

                update(connection);
                await dbContext.SaveChangesAsync(gateCancellationToken);
                return true;
            },
            cancellationToken);

    private static GmailSyncResult RevocationPendingResult() => new(
        false,
        0,
        0,
        0,
        "Gmail syncing is disabled while disconnection is pending.");

    private static bool IsDuplicateMessageViolation(DbUpdateException exception)
    {
        for (var current = exception.InnerException;
             current is not null;
             current = current.InnerException)
        {
            if (current is SqlException sqlException
                && sqlException.Errors.Cast<SqlError>().Any(
                    error => error.Number is 2601 or 2627))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildSyncMessage(GmailImportBatchResult result)
    {
        if (result.AutomaticallyAddedCount == 0
            && result.ReviewCount == 0
            && result.LinkedExistingCount == 0)
        {
            return "Gmail is up to date. No new application emails were found.";
        }

        var messages = new List<string>();
        if (result.AutomaticallyAddedCount > 0)
        {
            messages.Add(
                result.AutomaticallyAddedCount == 1
                    ? "1 application was automatically added."
                    : $"{result.AutomaticallyAddedCount} applications were automatically added.");
        }
        if (result.ReviewCount > 0)
        {
            messages.Add(
                result.ReviewCount == 1
                    ? "1 suggestion was sent to review."
                    : $"{result.ReviewCount} suggestions were sent to review.");
        }
        if (result.LinkedExistingCount > 0)
        {
            messages.Add(
                result.LinkedExistingCount == 1
                    ? "1 email was linked to an existing application."
                    : $"{result.LinkedExistingCount} emails were linked to existing applications.");
        }

        return string.Join(' ', messages);
    }

    private sealed class GmailImportBatchResult
    {
        public int AutomaticallyAddedCount { get; set; }
        public int ReviewCount { get; set; }
        public int LinkedExistingCount { get; set; }
    }

    private sealed record GmailMessagePage(
        IReadOnlyList<string> MessageIds,
        string? NextPageToken);

    private sealed class GmailAuthorizationException(string reason) : Exception(reason)
    {
        public string Reason { get; } = reason;
    }
}
