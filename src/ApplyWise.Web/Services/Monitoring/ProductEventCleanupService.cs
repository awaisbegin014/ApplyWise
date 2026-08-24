using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Monitoring;

public sealed class ProductEventRetentionOptions
{
    public const string SectionName = "ProductEvents";
    public int RetentionDays { get; set; } = 90;
    public int MaxStoredEvents { get; set; } = 500_000;
}

public sealed class ProductEventCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<ProductEventRetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<ProductEventCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RemoveExpiredEventsAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RemoveExpiredEventsAsync(stoppingToken);
        }
    }

    private async Task RemoveExpiredEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cutoff = timeProvider.GetUtcNow().AddDays(-options.Value.RetentionDays);
            var deleted = await DeleteBatchAsync(
                dbContext.ProductEvents
                    .Where(productEvent => productEvent.OccurredAt < cutoff)
                    .OrderBy(productEvent => productEvent.OccurredAt)
                    .ThenBy(productEvent => productEvent.Id),
                cancellationToken);
            var count = await dbContext.ProductEvents.CountAsync(cancellationToken);
            if (count > options.Value.MaxStoredEvents)
            {
                deleted += await DeleteBatchAsync(
                    dbContext.ProductEvents
                        .OrderBy(productEvent => productEvent.OccurredAt)
                        .ThenBy(productEvent => productEvent.Id),
                    cancellationToken,
                    Math.Min(5_000, count - options.Value.MaxStoredEvents));
            }
            if (deleted > 0)
            {
                logger.LogInformation("Removed {EventCount} expired product events.", deleted);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not remove expired product events.");
        }
    }

    private static Task<int> DeleteBatchAsync(
        IQueryable<ProductEvent> query,
        CancellationToken cancellationToken,
        int take = 5_000) =>
        query.Take(take).ExecuteDeleteAsync(cancellationToken);
}
