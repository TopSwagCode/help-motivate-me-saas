using HelpMotivateMe.SaaS.Api.Models;
using HelpMotivateMe.SaaS.Api.Options;
using HelpMotivateMe.SaaS.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HelpMotivateMe.SaaS.Api.Services;

public sealed class IdleTenantWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TenantRuntimeOptions> options,
    ILogger<IdleTenantWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.IdleSweepSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await StopIdleTenantsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Idle tenant sweep failed");
            }
        }
    }

    private async Task StopIdleTenantsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<TenantLifecycleService>();
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-options.Value.IdleTimeoutMinutes);
        var runningTenants = await db.Tenants.AsNoTracking()
            .Where(tenant => tenant.Status == TenantStatus.Running)
            .Select(tenant => new { tenant.Id, tenant.LastRequestUtc })
            .ToListAsync(cancellationToken);
        var tenantIds = runningTenants
            .Where(tenant => tenant.LastRequestUtc < cutoff)
            .Select(tenant => tenant.Id);

        foreach (var tenantId in tenantIds)
        {
            await lifecycle.StopIfIdleAsync(tenantId, cutoff, cancellationToken);
        }
    }
}