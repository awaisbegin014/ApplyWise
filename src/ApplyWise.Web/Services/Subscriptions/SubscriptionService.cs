using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Subscriptions;

public sealed record SubscriptionSnapshot(
    SubscriptionTier Tier,
    bool IsPro,
    DateTimeOffset? ProExpiresAt,
    int AtsAnalysisLimit,
    int AtsAnalysisUsed,
    int AtsAnalysisReserved,
    int ResumeBuildLimit,
    int ResumeBuildUsed,
    DateTimeOffset? UsageWindowStartsAt)
{
    public int AtsAnalysesRemaining => Math.Max(0, AtsAnalysisLimit - AtsAnalysisUsed - AtsAnalysisReserved);
    public int ResumeBuildsRemaining => IsPro ? int.MaxValue : Math.Max(0, ResumeBuildLimit - ResumeBuildUsed);
    public bool HasUnlimitedResumeBuilds => IsPro;
}

public sealed record UpgradeSubmission(
    string PaymentMethod,
    string TransactionReference,
    string PayerName,
    decimal Amount,
    string? UserNote);

public sealed record UpgradeSubmissionResult(bool Succeeded, string? Error, long? RequestId = null);

public sealed record AtsUsageReservation(bool Allowed, SubscriptionSnapshot Snapshot, long? UsageRecordId = null);
public sealed record ResumeBuildEntitlement(bool Allowed, SubscriptionSnapshot Snapshot);

public interface ISubscriptionService
{
    Task<SubscriptionSnapshot> GetSnapshotAsync(string userId, CancellationToken cancellationToken = default);
    Task<AtsUsageReservation> TryReserveAtsAnalysisAsync(string userId, string feature, int promptCharacters, CancellationToken cancellationToken = default);
    Task CompleteAtsAnalysisAsync(long usageRecordId, bool succeeded, int responseCharacters, string? model, CancellationToken cancellationToken = default);
    Task<ResumeBuildEntitlement> TryConsumeResumeBuildAsync(string userId, string? templateId, CancellationToken cancellationToken = default);
    Task<UpgradeSubmissionResult> SubmitUpgradeRequestAsync(string userId, UpgradeSubmission submission, CancellationToken cancellationToken = default);
    Task<bool> ApproveUpgradeRequestAsync(long requestId, string adminUserId, string? note, CancellationToken cancellationToken = default);
    Task<bool> RejectUpgradeRequestAsync(long requestId, string adminUserId, string? note, CancellationToken cancellationToken = default);
}

public sealed class SubscriptionService(
    ApplicationDbContext dbContext,
    IWorkspaceQuotaGate quotaGate,
    IOptions<SubscriptionOptions> options,
    TimeProvider timeProvider) : ISubscriptionService
{
    private const string AtsQuotaResource = "ats-analysis-requests";
    private const string ResumeBuildQuotaResource = "resume-builds";
    private const string UpgradeResource = "pro-upgrade-requests";
    private SubscriptionOptions Settings => options.Value;

    public async Task<SubscriptionSnapshot> GetSnapshotAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var subscription = await dbContext.UserSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var isPro = subscription?.Tier == SubscriptionTier.Pro
            && subscription.ProExpiresAt > now;
        var windowStart = isPro
            ? CurrentProUsageWindowStart(
                subscription!.ProStartedAt ?? now,
                now,
                Settings.ProDurationDays)
            : (DateTimeOffset?)null;
        var query = dbContext.AiUsageRecords
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.Feature.StartsWith("ats-"));
        if (windowStart.HasValue)
        {
            query = query.Where(item => item.ReservedAt >= windowStart.Value);
        }

        var used = await query.CountAsync(
            item => item.Status == AiUsageStatus.Succeeded,
            cancellationToken);
        var reserved = await query.CountAsync(
            item => item.Status == AiUsageStatus.Reserved
                && item.ReservedAt >= now.AddMinutes(-10),
            cancellationToken);
        var resumeBuilds = await dbContext.ResumeBuildUsageRecords
            .AsNoTracking()
            .CountAsync(item => item.UserId == userId, cancellationToken);

        return new SubscriptionSnapshot(
            isPro ? SubscriptionTier.Pro : SubscriptionTier.Free,
            isPro,
            subscription?.ProExpiresAt,
            isPro ? Settings.ProAtsAnalysisLimit : Settings.FreeAtsAnalysisLimit,
            used,
            reserved,
            Settings.FreeResumeBuildLimit,
            resumeBuilds,
            windowStart);
    }

    public Task<AtsUsageReservation> TryReserveAtsAnalysisAsync(
        string userId,
        string feature,
        int promptCharacters,
        CancellationToken cancellationToken = default) =>
        quotaGate.RunAsync(
            AtsQuotaResource,
            userId,
            async operationCancellationToken =>
            {
                var snapshot = await GetSnapshotAsync(userId, operationCancellationToken);
                if (snapshot.AtsAnalysesRemaining <= 0)
                {
                    return new AtsUsageReservation(false, snapshot);
                }

                var record = new AiUsageRecord
                {
                    UserId = userId,
                    Feature = Normalize(feature, 64),
                    Status = AiUsageStatus.Reserved,
                    ReservedAt = timeProvider.GetUtcNow(),
                    PromptCharacters = Math.Max(0, promptCharacters)
                };
                dbContext.AiUsageRecords.Add(record);
                await dbContext.SaveChangesAsync(operationCancellationToken);
                return new AtsUsageReservation(true, snapshot, record.Id);
            },
            cancellationToken);

    public async Task CompleteAtsAnalysisAsync(
        long usageRecordId,
        bool succeeded,
        int responseCharacters,
        string? model,
        CancellationToken cancellationToken = default)
    {
        var record = await dbContext.AiUsageRecords.SingleOrDefaultAsync(
            item => item.Id == usageRecordId,
            cancellationToken);
        if (record is null || record.Status != AiUsageStatus.Reserved)
        {
            return;
        }

        record.Status = succeeded ? AiUsageStatus.Succeeded : AiUsageStatus.Failed;
        record.CompletedAt = timeProvider.GetUtcNow();
        record.ResponseCharacters = Math.Max(0, responseCharacters);
        record.Model = string.IsNullOrWhiteSpace(model) ? null : Normalize(model, 80);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ResumeBuildEntitlement> TryConsumeResumeBuildAsync(
        string userId,
        string? templateId,
        CancellationToken cancellationToken = default) =>
        quotaGate.RunAsync(
            ResumeBuildQuotaResource,
            userId,
            async operationCancellationToken =>
            {
                var snapshot = await GetSnapshotAsync(userId, operationCancellationToken);
                if (snapshot.IsPro)
                {
                    return new ResumeBuildEntitlement(true, snapshot);
                }

                if (snapshot.ResumeBuildsRemaining <= 0)
                {
                    return new ResumeBuildEntitlement(false, snapshot);
                }

                dbContext.ResumeBuildUsageRecords.Add(new ResumeBuildUsageRecord
                {
                    UserId = userId,
                    CreatedAt = timeProvider.GetUtcNow(),
                    TemplateId = NormalizeOptional(templateId, 40)
                });
                await dbContext.SaveChangesAsync(operationCancellationToken);
                return new ResumeBuildEntitlement(true, snapshot with
                {
                    ResumeBuildUsed = snapshot.ResumeBuildUsed + 1
                });
            },
            cancellationToken);

    public Task<UpgradeSubmissionResult> SubmitUpgradeRequestAsync(
        string userId,
        UpgradeSubmission submission,
        CancellationToken cancellationToken = default) =>
        quotaGate.RunAsync(
            UpgradeResource,
            userId,
            async operationCancellationToken =>
            {
                var now = timeProvider.GetUtcNow();
                var existingSubscription = await dbContext.UserSubscriptions
                    .AsNoTracking()
                    .SingleOrDefaultAsync(item => item.UserId == userId, operationCancellationToken);
                if (existingSubscription?.Tier == SubscriptionTier.Pro
                    && existingSubscription.ProExpiresAt > now)
                {
                    return new UpgradeSubmissionResult(false, "Your Pro access is already active.");
                }

                if (await dbContext.ProUpgradeRequests.AnyAsync(
                    item => item.UserId == userId && item.Status == ProUpgradeRequestStatus.Pending,
                    operationCancellationToken))
                {
                    return new UpgradeSubmissionResult(false, "You already have an upgrade request awaiting review.");
                }

                var paymentMethod = Normalize(submission.PaymentMethod, 50);
                var transactionReference = Normalize(submission.TransactionReference, 120);
                if (await dbContext.ProUpgradeRequests.AnyAsync(
                    item => item.PaymentMethod == paymentMethod
                        && item.TransactionReference == transactionReference,
                    operationCancellationToken))
                {
                    return new UpgradeSubmissionResult(false, "That transaction reference has already been submitted.");
                }

                var request = new ProUpgradeRequest
                {
                    UserId = userId,
                    PaymentMethod = paymentMethod,
                    TransactionReference = transactionReference,
                    PayerName = Normalize(submission.PayerName, 120),
                    Amount = submission.Amount,
                    Currency = Normalize(Settings.Currency, 10).ToUpperInvariant(),
                    UserNote = NormalizeOptional(submission.UserNote, 1000),
                    SubmittedAt = now
                };
                dbContext.ProUpgradeRequests.Add(request);
                try
                {
                    await dbContext.SaveChangesAsync(operationCancellationToken);
                }
                catch (DbUpdateException)
                {
                    dbContext.Entry(request).State = EntityState.Detached;
                    return new UpgradeSubmissionResult(
                        false,
                        "This request conflicts with an existing pending request or transaction reference.");
                }
                return new UpgradeSubmissionResult(true, null, request.Id);
            },
            cancellationToken);

    public Task<bool> ApproveUpgradeRequestAsync(
        long requestId,
        string adminUserId,
        string? note,
        CancellationToken cancellationToken = default) =>
        ReviewAsync(requestId, adminUserId, note, approve: true, cancellationToken);

    public Task<bool> RejectUpgradeRequestAsync(
        long requestId,
        string adminUserId,
        string? note,
        CancellationToken cancellationToken = default) =>
        ReviewAsync(requestId, adminUserId, note, approve: false, cancellationToken);

    private async Task<bool> ReviewAsync(
        long requestId,
        string adminUserId,
        string? note,
        bool approve,
        CancellationToken cancellationToken)
    {
        var request = await dbContext.ProUpgradeRequests.SingleOrDefaultAsync(
            item => item.Id == requestId,
            cancellationToken);
        if (request is null || request.Status != ProUpgradeRequestStatus.Pending)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        request.Status = approve
            ? ProUpgradeRequestStatus.Approved
            : ProUpgradeRequestStatus.Rejected;
        request.ReviewedAt = now;
        request.ReviewedByUserId = Normalize(adminUserId, 450);
        request.AdminNote = NormalizeOptional(note, 1000);

        if (approve)
        {
            var subscription = await dbContext.UserSubscriptions.SingleOrDefaultAsync(
                item => item.UserId == request.UserId,
                cancellationToken);
            if (subscription is null)
            {
                subscription = new UserSubscription
                {
                    UserId = request.UserId,
                    UpdatedAt = now
                };
                dbContext.UserSubscriptions.Add(subscription);
            }

            var alreadyActive = subscription.Tier == SubscriptionTier.Pro
                && subscription.ProExpiresAt > now;
            var startsFrom = alreadyActive
                ? subscription.ProExpiresAt.GetValueOrDefault(now)
                : now;
            subscription.Tier = SubscriptionTier.Pro;
            if (!alreadyActive)
            {
                subscription.ProStartedAt = now;
            }
            subscription.ProExpiresAt = startsFrom.AddDays(Settings.ProDurationDays);
            subscription.UpdatedAt = now;
            subscription.ApprovedByUserId = Normalize(adminUserId, 450);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string Normalize(string value, int maxLength)
    {
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeOptional(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value, maxLength);

    private static DateTimeOffset CurrentProUsageWindowStart(
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int durationDays)
    {
        if (startedAt >= now) return startedAt;
        var completedWindows = (long)Math.Floor((now - startedAt).TotalDays / durationDays);
        return startedAt.AddDays(completedWindows * durationDays);
    }
}
