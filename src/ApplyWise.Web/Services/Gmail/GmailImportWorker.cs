namespace ApplyWise.Web.Services.Gmail;

using Microsoft.Extensions.Options;

public sealed class GmailImportWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<GoogleIntegrationOptions> options,
    ILogger<GmailImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsGmailImportConfigured || !options.Value.GmailAutoSyncEnabled)
        {
            logger.LogInformation("Automatic Gmail import is disabled or Google OAuth is not configured.");
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var importer = scope.ServiceProvider.GetRequiredService<IGmailImportService>();
            await importer.SyncStartupConnectionsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The startup Gmail recovery cycle failed.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var importer = scope.ServiceProvider.GetRequiredService<IGmailImportService>();
                await importer.SyncDueConnectionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The automatic Gmail import cycle failed.");
            }
        }
    }
}
