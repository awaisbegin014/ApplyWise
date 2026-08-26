using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Monitoring;

public static class ProductEventNames
{
    public const string AccountRegistered = "account.registered";
    public const string EmailConfirmed = "account.email_confirmed";
    public const string LoginSucceeded = "account.login_succeeded";
    public const string LoginFailed = "account.login_failed";
    public const string OnboardingCompleted = "onboarding.completed";
    public const string ResumeUploaded = "resume.uploaded";
    public const string ResumeAnalysisCompleted = "resume.analysis_completed";
    public const string ApplicationCreated = "application.created";
    public const string InterviewScheduled = "interview.scheduled";
    public const string ScamCheckCompleted = "scam_check.completed";
    public const string AtsAiFeedbackCompleted = "ai.ats_feedback_completed";
    public const string ProUpgradeRequested = "subscription.pro_requested";
    public const string ProUpgradeApproved = "subscription.pro_approved";
}

public interface IProductEventRecorder
{
    Task RecordAsync(
        string name,
        string source,
        string? userId = null,
        bool succeeded = true,
        CancellationToken cancellationToken = default);

    Task RecordLoginAsync(
        string userId,
        string source,
        CancellationToken cancellationToken = default);
}

public sealed class ProductEventRecorder(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<ProductEventRetentionOptions> options,
    ILogger<ProductEventRecorder> logger) : IProductEventRecorder
{
    public Task RecordAsync(
        string name,
        string source,
        string? userId = null,
        bool succeeded = true,
        CancellationToken cancellationToken = default)
    {
        // Anonymous login failures are adversary-controlled and have no safe
        // per-row cardinality bound. Rate-limit/host metrics may observe them,
        // but they must not become durable product-event rows.
        if (!succeeded
            && userId is null
            && name.Equals(ProductEventNames.LoginFailed, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return SaveBestEffortAsync(
            new ProductEvent
            {
                Name = Normalize(name, 64),
                Source = Normalize(source, 32),
                UserId = userId,
                Succeeded = succeeded,
                OccurredAt = timeProvider.GetUtcNow()
            },
            cancellationToken);
    }

    public async Task RecordLoginAsync(
        string userId,
        string source,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        try
        {
            var activity = await dbContext.UserAccountActivities
                .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
            if (activity is null)
            {
                activity = new UserAccountActivity
                {
                    UserId = userId,
                    RegisteredAt = now,
                };
                dbContext.UserAccountActivities.Add(activity);
            }

            activity.LastLoginAt = now;
            activity.LastActivityAt = now;
            activity.LastLoginProvider = Normalize(source, 30);
            activity.TotalSuccessfulLogins++;

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not persist a privacy-safe login event for user {UserId}.", userId);
        }
    }

    private async Task SaveBestEffortAsync(ProductEvent productEvent, CancellationToken cancellationToken)
    {
        try
        {
            if (await dbContext.ProductEvents.CountAsync(cancellationToken)
                >= options.Value.MaxStoredEvents)
            {
                return;
            }

            dbContext.ProductEvents.Add(productEvent);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            dbContext.Entry(productEvent).State = EntityState.Detached;
            logger.LogError(exception, "Could not persist product event {EventName}.", productEvent.Name);
        }
    }

    private static string Normalize(string value, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
