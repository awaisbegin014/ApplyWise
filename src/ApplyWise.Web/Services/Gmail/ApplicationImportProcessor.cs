using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ApplyWise.Web.Services.Security;

namespace ApplyWise.Web.Services.Gmail;

public static class ApplicationImportPolicy
{
    public const int HighConfidenceThreshold = 90;
}

public sealed record ManualApplicationImportData(
    string CompanyName,
    string JobTitle,
    string? JobLocation,
    JobSource Source,
    string? JobUrl,
    DateOnly? AppliedDate);

public enum ApplicationImportProcessOutcome
{
    NotFound,
    NotEligible,
    Created,
    LinkedExisting,
    AlreadyProcessed,
    OwnershipConflict,
    QuotaExceeded
}

public sealed record ApplicationImportProcessResult(
    ApplicationImportProcessOutcome Outcome,
    int? ApplicationId = null,
    string? CompanyName = null,
    string? JobTitle = null);

public interface IApplicationImportProcessor
{
    Task<ApplicationImportProcessResult> TryAutoAcceptAsync(
        int importId,
        string userId,
        CancellationToken cancellationToken);

    Task<ApplicationImportProcessResult> AcceptManuallyAsync(
        int importId,
        string userId,
        ManualApplicationImportData data,
        CancellationToken cancellationToken);
}

public sealed class ApplicationImportProcessor(
    ApplicationDbContext dbContext,
    ILogger<ApplicationImportProcessor> logger,
    IWorkspaceQuotaService? quotas = null,
    IWorkspaceQuotaGate? quotaGate = null) : IApplicationImportProcessor
{
    private readonly IWorkspaceQuotaGate _quotaGate = quotaGate ?? new WorkspaceQuotaGate(dbContext);

    private static readonly HashSet<string> TrackingQueryParameters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "gclid",
            "dclid",
            "fbclid",
            "msclkid",
            "mc_cid",
            "mc_eid"
        };

    public Task<ApplicationImportProcessResult> TryAutoAcceptAsync(
        int importId,
        string userId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            importId,
            userId,
            manualData: null,
            automatically: true,
            cancellationToken);

    public Task<ApplicationImportProcessResult> AcceptManuallyAsync(
        int importId,
        string userId,
        ManualApplicationImportData data,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            importId,
            userId,
            data,
            automatically: false,
            cancellationToken);

    private async Task<ApplicationImportProcessResult> ExecuteAsync(
        int importId,
        string userId,
        ManualApplicationImportData? manualData,
        bool automatically,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _quotaGate.RunAsync(
                WorkspaceQuotaResources.Applications,
                userId,
                operationCancellationToken => ProcessCoreAsync(
                    importId,
                    userId,
                    manualData,
                    automatically,
                    operationCancellationToken),
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            var processedImport = await dbContext.ApplicationImports
                .AsNoTracking()
                .Where(item =>
                    item.Id == importId
                    && item.UserId == userId
                    && item.CreatedApplicationId.HasValue)
                .Select(item => item.CreatedApplicationId)
                .SingleOrDefaultAsync(cancellationToken);
            if (processedImport.HasValue)
            {
                var application = await dbContext.JobApplications
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item =>
                            item.Id == processedImport.Value
                            && item.UserId == userId,
                        cancellationToken);
                if (application is not null)
                {
                    return new(
                        ApplicationImportProcessOutcome.AlreadyProcessed,
                        application.Id,
                        application.CompanyName,
                        application.JobTitle);
                }
            }

            throw;
        }
    }

    private async Task<ApplicationImportProcessResult> ProcessCoreAsync(
        int importId,
        string userId,
        ManualApplicationImportData? manualData,
        bool automatically,
        CancellationToken cancellationToken)
    {
        // Each quota-gate operation starts from the latest database state.
        dbContext.ChangeTracker.Clear();

        var import = await dbContext.ApplicationImports
            .Include(item => item.GmailConnection)
            .SingleOrDefaultAsync(
                item =>
                    item.Id == importId
                    && item.UserId == userId
                    && item.GmailConnection != null
                    && item.GmailConnection.UserId == userId,
                cancellationToken);
        if (import is null)
        {
            return new(ApplicationImportProcessOutcome.NotFound);
        }

        if (import.CreatedApplicationId.HasValue)
        {
            var ownedApplication = await FindOwnedApplicationAsync(
                import.CreatedApplicationId.Value,
                userId,
                cancellationToken);
            if (ownedApplication is not null)
            {
                return new(
                    ApplicationImportProcessOutcome.AlreadyProcessed,
                    ownedApplication.Id,
                    ownedApplication.CompanyName,
                    ownedApplication.JobTitle);
            }

            logger.LogWarning(
                "Application import {ImportId} for user {UserId} contains an invalid application link.",
                import.Id,
                userId);
            return new(ApplicationImportProcessOutcome.OwnershipConflict);
        }

        if (import.Status != ApplicationImportStatus.PendingReview)
        {
            return new(ApplicationImportProcessOutcome.NotEligible);
        }

        if (automatically && !IsAutoAddEligible(import))
        {
            return new(ApplicationImportProcessOutcome.NotEligible);
        }

        var values = automatically
            ? new ImportApplicationValues(
                import.CompanyName.Trim(),
                import.JobTitle.Trim(),
                NullIfWhiteSpace(import.JobLocation),
                import.Source,
                NullIfWhiteSpace(import.JobUrl),
                import.AppliedDate)
            : ToValues(manualData);
        if (values is null
            || string.IsNullOrWhiteSpace(values.CompanyName)
            || string.IsNullOrWhiteSpace(values.JobTitle))
        {
            return new(ApplicationImportProcessOutcome.NotEligible);
        }

        var sameMessageApplication = await FindApplicationFromProcessedMessageAsync(
            import,
            userId,
            cancellationToken);
        var existingApplication = sameMessageApplication
            ?? await FindDuplicateApplicationAsync(
                userId,
                values,
                cancellationToken);

        if (existingApplication is not null)
        {
            CompleteImport(
                import,
                existingApplication.Id,
                automatically
                    ? ApplicationImportStatus.AutoAccepted
                    : ApplicationImportStatus.Accepted);
            await dbContext.SaveChangesAsync(cancellationToken);

            return new(
                ApplicationImportProcessOutcome.LinkedExisting,
                existingApplication.Id,
                existingApplication.CompanyName,
                existingApplication.JobTitle);
        }

        if (quotas is not null
            && !await quotas.CanCreateApplicationAsync(userId, cancellationToken))
        {
            return new(ApplicationImportProcessOutcome.QuotaExceeded);
        }

        var now = DateTimeOffset.UtcNow;
        var application = new JobApplication
        {
            UserId = userId,
            ResumeId = await ResolveResumeIdAsync(
                userId,
                import.ResumeFileName,
                cancellationToken),
            CompanyName = values.CompanyName,
            JobTitle = values.JobTitle,
            JobLocation = values.JobLocation,
            Source = values.Source,
            JobUrl = values.JobUrl,
            Status = ApplicationStatus.Applied,
            AppliedDate = values.AppliedDate,
            Notes = automatically
                ? "Automatically imported by ApplyWise from a Gmail application confirmation. Review and edit any extracted details if needed."
                : "Imported from a connected Gmail account. Verify any details that were not present in the original confirmation.",
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.JobApplications.Add(application);

        // The first save obtains the generated application ID. The quota gate's
        // transaction keeps that insert atomic with the import status/link update.
        await dbContext.SaveChangesAsync(cancellationToken);
        CompleteImport(
            import,
            application.Id,
            automatically
                ? ApplicationImportStatus.AutoAccepted
                : ApplicationImportStatus.Accepted);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new(
            ApplicationImportProcessOutcome.Created,
            application.Id,
            application.CompanyName,
            application.JobTitle);
    }

    private static bool IsAutoAddEligible(ApplicationImport import) =>
        import.GmailConnection is { AutoAddHighConfidenceApplications: true } connection
        && connection.LastErrorCode != GmailConnectionStates.RevocationPending
        && import.Direction == ApplicationImportDirection.Incoming
        && import.Confidence >= ApplicationImportPolicy.HighConfidenceThreshold
        && !string.IsNullOrWhiteSpace(import.CompanyName)
        && !string.IsNullOrWhiteSpace(import.JobTitle)
        && import.AppliedDate.HasValue
        && !import.CreatedApplicationId.HasValue;

    private async Task<JobApplication?> FindApplicationFromProcessedMessageAsync(
        ApplicationImport import,
        string userId,
        CancellationToken cancellationToken)
    {
        var applicationIds = await dbContext.ApplicationImports
            .AsNoTracking()
            .Where(item =>
                item.Id != import.Id
                && item.UserId == userId
                && item.GmailConnectionId == import.GmailConnectionId
                && item.ExternalMessageId == import.ExternalMessageId
                && item.CreatedApplicationId.HasValue)
            .Select(item => item.CreatedApplicationId!.Value)
            .ToListAsync(cancellationToken);

        if (applicationIds.Count == 0) return null;
        return await dbContext.JobApplications
            .FirstOrDefaultAsync(
                application =>
                    application.UserId == userId
                    && applicationIds.Contains(application.Id),
                cancellationToken);
    }

    private async Task<JobApplication?> FindDuplicateApplicationAsync(
        string userId,
        ImportApplicationValues values,
        CancellationToken cancellationToken)
    {
        var canonicalUrl = CanonicalizeJobUrl(values.JobUrl);
        if (canonicalUrl is not null)
        {
            var urlCandidates = await dbContext.JobApplications
                .Where(application =>
                    application.UserId == userId
                    && application.JobUrl != null)
                .ToListAsync(cancellationToken);
            var urlMatch = urlCandidates.FirstOrDefault(application =>
                string.Equals(
                    CanonicalizeJobUrl(application.JobUrl),
                    canonicalUrl,
                    StringComparison.Ordinal));
            if (urlMatch is not null) return urlMatch;
        }

        var normalizedCompany = NormalizeName(values.CompanyName);
        var normalizedTitle = NormalizeName(values.JobTitle);
        var candidates = await dbContext.JobApplications
            .Where(application =>
                application.UserId == userId
                && application.AppliedDate == values.AppliedDate)
            .ToListAsync(cancellationToken);
        return candidates.FirstOrDefault(application =>
            string.Equals(
                NormalizeName(application.CompanyName),
                normalizedCompany,
                StringComparison.Ordinal)
            && string.Equals(
                NormalizeName(application.JobTitle),
                normalizedTitle,
                StringComparison.Ordinal));
    }

    private async Task<int?> ResolveResumeIdAsync(
        string userId,
        string? resumeFileName,
        CancellationToken cancellationToken)
    {
        var resumes = await dbContext.Resumes
            .AsNoTracking()
            .Where(resume => resume.UserId == userId)
            .OrderByDescending(resume => resume.UploadedAt)
            .Select(resume => new
            {
                resume.Id,
                resume.OriginalFileName,
                resume.IsDefault
            })
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(resumeFileName))
        {
            var expectedFileName = resumeFileName.Trim();
            var matchedResume = resumes.FirstOrDefault(resume =>
                string.Equals(
                    resume.OriginalFileName.Trim(),
                    expectedFileName,
                    StringComparison.OrdinalIgnoreCase));
            if (matchedResume is not null) return matchedResume.Id;
        }

        return resumes.FirstOrDefault(resume => resume.IsDefault)?.Id;
    }

    private Task<JobApplication?> FindOwnedApplicationAsync(
        int applicationId,
        string userId,
        CancellationToken cancellationToken) =>
        dbContext.JobApplications.FirstOrDefaultAsync(
            application =>
                application.Id == applicationId
                && application.UserId == userId,
            cancellationToken);

    private static void CompleteImport(
        ApplicationImport import,
        int applicationId,
        ApplicationImportStatus status)
    {
        import.CreatedApplicationId = applicationId;
        import.Status = status;
        import.ReviewedAt = DateTimeOffset.UtcNow;
    }

    private static ImportApplicationValues? ToValues(
        ManualApplicationImportData? data)
    {
        if (data is null) return null;
        var companyName = NullIfWhiteSpace(data.CompanyName);
        var jobTitle = NullIfWhiteSpace(data.JobTitle);
        if (companyName is null || jobTitle is null) return null;
        return new ImportApplicationValues(
            companyName,
            jobTitle,
            NullIfWhiteSpace(data.JobLocation),
            data.Source,
            NullIfWhiteSpace(data.JobUrl),
            data.AppliedDate);
    }

    private static string NormalizeName(string value) =>
        string.Join(
                ' ',
                value.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    private static string? CanonicalizeJobUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps
                && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.IdnHost.ToLowerInvariant(),
            Fragment = string.Empty
        };
        if (uri.IsDefaultPort)
        {
            builder.Port = -1;
        }

        if (builder.Path.Length > 1)
        {
            builder.Path = builder.Path.TrimEnd('/');
        }

        var retainedParameters = QueryHelpers.ParseQuery(uri.Query)
            .Where(pair =>
                !pair.Key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                && !TrackingQueryParameters.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value
                .OrderBy(item => item, StringComparer.Ordinal)
                .Select(item => new KeyValuePair<string, string?>(
                    pair.Key,
                    item)));
        builder.Query = string.Join(
            "&",
            retainedParameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));

        return builder.Uri.AbsoluteUri;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ImportApplicationValues(
        string CompanyName,
        string JobTitle,
        string? JobLocation,
        JobSource Source,
        string? JobUrl,
        DateOnly? AppliedDate);
}
