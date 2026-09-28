namespace HelpMotivateMe.SaaS.Api.Services;

public sealed class TenantStartWorker(
    TenantStartQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<TenantStartWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var tenantId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var lifecycle = scope.ServiceProvider.GetRequiredService<TenantLifecycleService>();
                await lifecycle.StartAsync(tenantId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to wake tenant {TenantId}", tenantId);
            }
        }
    }
}