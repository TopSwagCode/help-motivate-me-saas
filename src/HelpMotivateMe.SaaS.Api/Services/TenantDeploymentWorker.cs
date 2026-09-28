namespace HelpMotivateMe.SaaS.Api.Services;

public sealed class TenantDeploymentWorker(
    TenantDeploymentQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<TenantDeploymentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            var completed = 0;
            foreach (var tenant in request.Tenants)
            {
                queue.SetRunning(tenant.Subdomain);
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var lifecycle = scope.ServiceProvider.GetRequiredService<TenantLifecycleService>();
                    await lifecycle.DeployLatestAsync(tenant.Id, stoppingToken);
                    completed++;
                    queue.SetProgress(completed);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Deployment stopped after tenant {TenantId} failed", tenant.Id);
                    queue.SetFailed(tenant.Subdomain, exception.Message);
                    break;
                }
            }

            if (completed == request.Tenants.Count) queue.SetCompleted();
        }
    }
}