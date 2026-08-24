using ApplyWise.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.AccountSecurity;

public interface IAccountSecurityCodeRetention
{
    Task<int> DeleteObsoleteBatchAsync(CancellationToken cancellationToken = default);
}

public sealed class AccountSecurityCodeRetention(
    ApplicationDbContext db,
    TimeProvider timeProvider) : IAccountSecurityCodeRetention
{
    public const int MaximumBatchSize = 500;
    private static readonly TimeSpan RetentionAfterExpiration = TimeSpan.FromHours(1);

    public async Task<int> DeleteObsoleteBatchAsync(
        CancellationToken cancellationToken = default)
    {
        var cutoff = timeProvider.GetUtcNow().Subtract(RetentionAfterExpiration);
        var obsolete = await db.AccountSecurityCodes
            .Where(code => code.ExpiresAt <= cutoff)
            .OrderBy(code => code.ExpiresAt)
            .ThenBy(code => code.Id)
            .Take(MaximumBatchSize)
            .ToListAsync(cancellationToken);
        if (obsolete.Count == 0)
        {
            return 0;
        }

        db.AccountSecurityCodes.RemoveRange(obsolete);
        await db.SaveChangesAsync(cancellationToken);
        return obsolete.Count;
    }
}

public sealed class AccountSecurityCodeCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<AccountSecurityCodeCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = 0;
                do
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    removed = await scope.ServiceProvider
                        .GetRequiredService<IAccountSecurityCodeRetention>()
                        .DeleteObsoleteBatchAsync(stoppingToken);
                }
                while (removed >= AccountSecurityCodeRetention.MaximumBatchSize
                       && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "The account-security-code retention cleanup failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
