using HelpMotivateMe.SaaS.Api.Models;
using HelpMotivateMe.SaaS.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpMotivateMe.SaaS.Api.Services;

public sealed class TenantLifecycleService(
    ControlPlaneDbContext db,
    ITenantRuntime runtime,
    TenantOperationLocks operationLocks,
    ILogger<TenantLifecycleService> logger)
{
    public async Task<Tenant> ProvisionAsync(Tenant tenant, TenantConfiguration configuration,
        CancellationToken cancellationToken)
    {
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);

        using var tenantLock = await operationLocks.AcquireAsync(tenant.Id, cancellationToken);
        try
        {
            tenant.ContainerId = await runtime.CreateAsync(tenant, configuration, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await runtime.StartAsync(tenant, cancellationToken);
            tenant.Status = TenantStatus.Running;
            tenant.LastStartedUtc = DateTimeOffset.UtcNow;
            tenant.LastRequestUtc = tenant.LastStartedUtc.Value;
        }
        catch (Exception exception)
        {
            tenant.Status = TenantStatus.Failed;
            logger.LogError(exception, "Failed to provision tenant {TenantId}", tenant.Id);
            if (!string.IsNullOrWhiteSpace(tenant.ContainerId))
            {
                try
                {
                    await runtime.StopAsync(tenant, CancellationToken.None);
                }
                catch (Exception stopException)
                {
                    logger.LogWarning(stopException, "Failed to stop tenant {TenantId} after provisioning failure", tenant.Id);
                }
            }

            throw;
        }
        finally
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return tenant;
    }

    public async Task<bool> StartAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var tenantLock = await operationLocks.AcquireAsync(tenantId, cancellationToken);
        var tenant = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);
        if (tenant is null) return false;

        var state = await runtime.InspectAsync(tenant, cancellationToken);
        if (!state.Exists) throw new InvalidOperationException("The tenant container no longer exists.");
        if (state.Healthy) return true;

        await runtime.StartAsync(tenant, cancellationToken);
        tenant.Status = TenantStatus.Running;
        tenant.LastStartedUtc = DateTimeOffset.UtcNow;
        tenant.LastRequestUtc = tenant.LastStartedUtc.Value;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> StopAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var tenantLock = await operationLocks.AcquireAsync(tenantId, cancellationToken);
        var tenant = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);
        if (tenant is null) return false;

        var state = await runtime.InspectAsync(tenant, cancellationToken);
        if (state.Running)
        {
            AccumulateUptime(tenant);
            await runtime.StopAsync(tenant, cancellationToken);
        }

        tenant.Status = TenantStatus.Stopped;
        tenant.LastStartedUtc = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RestartAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var tenantLock = await operationLocks.AcquireAsync(tenantId, cancellationToken);
        var tenant = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);
        if (tenant is null) return false;

        var state = await runtime.InspectAsync(tenant, cancellationToken);
        if (!state.Exists) throw new InvalidOperationException("The tenant container no longer exists.");
        if (state.Running)
        {
            AccumulateUptime(tenant);
            await runtime.StopAsync(tenant, cancellationToken);
        }

        await runtime.StartAsync(tenant, cancellationToken);
        tenant.Status = TenantStatus.Running;
        tenant.LastStartedUtc = DateTimeOffset.UtcNow;
        tenant.LastRequestUtc = tenant.LastStartedUtc.Value;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReconfigureAsync(Guid tenantId, TenantSettingsUpdate update,
        CancellationToken cancellationToken)
    {
        using var tenantLock = await operationLocks.AcquireAsync(tenantId, cancellationToken);
        var tenant = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);
        if (tenant is null) return false;

        var current = await runtime.ReadConfigurationAsync(tenant, cancellationToken);
        var aiMode = TenantPlanPolicy.GetAiMode(tenant.Plan, update.EnableAi);
        var apiKey = aiMode switch
        {
            TenantAiMode.Disabled => null,
            TenantAiMode.Managed => null,
            _ when !string.IsNullOrWhiteSpace(update.AiApiKey) => update.AiApiKey.Trim(),
            _ when current.AiMode == TenantAiMode.BringYourOwn => current.AiApiKey,
            _ => null
        };
        if (aiMode == TenantAiMode.BringYourOwn && string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("An AI API key is required when AI is enabled on the Free plan.");

        var configuration = new TenantConfiguration(
            update.Username.Trim(),
            string.IsNullOrEmpty(update.Password) ? current.Password : update.Password,
            aiMode,
            aiMode == TenantAiMode.Disabled ? null : update.AiProvider,
            aiMode == TenantAiMode.Disabled ? null : update.AiModel,
            apiKey);

        if (tenant.Status == TenantStatus.Running) AccumulateUptime(tenant);
        tenant.ContainerId = await runtime.ReplaceAsync(tenant, configuration, cancellationToken);
        tenant.Username = configuration.Username;
        tenant.AiMode = aiMode;
        tenant.AiProvider = configuration.AiProvider;
        tenant.AiModel = configuration.AiModel;
        tenant.HasAiApiKey = aiMode == TenantAiMode.Managed || !string.IsNullOrWhiteSpace(apiKey);
        tenant.Status = TenantStatus.Running;
        tenant.LastStartedUtc = DateTimeOffset.UtcNow;
        tenant.LastRequestUtc = tenant.LastStartedUtc.Value;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TenantBackup?> CreateBackupAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var tenantLock = await operationLocks.AcquireAsync(tenantId, cancellationToken);
        var tenant = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);
        if (tenant is null) return null;
        EnsurePaid(tenant);

        var state = await runtime.InspectAsync(tenant, cancellationToken);
        if (!state.Exists) throw new InvalidOperationException("The tenant container no longer exists.");
        var wasRunning = state.Running;
        if (wasRunning)
        {
            AccumulateUptime(tenant);
            await runtime.StopAsync(tenant, cancellationToken);
            tenant.Status = TenantStatus.Stopped;
            await db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var backupId = Guid.NewGuid();
            var backup = new TenantBackup
            {
                Id = backupId,
                TenantId = tenant.Id,
                FileName = $"{backupId:N}.tar.gz",
                SizeBytes = await runtime.CreateBackupAsync(tenant, backupId, cancellationToken),
                CreatedUtc = DateTimeOffset.UtcNow
            };
            db.Backups.Add(backup);
            await db.SaveChangesAsync(cancellationToken);
            return backup;
        }
        finally
        {
            if (wasRunning)
            {
                await runtime.StartAsync(tenant, CancellationToken.None);
                tenant.Status = TenantStatus.Running;
                tenant.LastStartedUtc = DateTimeOffset.UtcNow;
                tenant.LastRequestUtc = tenant.LastStartedUtc.Value;
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
    }

    public async Task<bool> RestoreBackupAsync(Guid tenantId, Guid backupId,
        CancellationToken cancellationToken)
    {
        using var tenantLock = await operationLocks.AcquireAsync(tenantId, cancellationToken);
        var tenant = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);
        if (tenant is null) return false;
        EnsurePaid(tenant);
        if (!await db.Backups.AnyAsync(item => item.Id == backupId && item.TenantId == tenantId, cancellationToken))
            throw new FileNotFoundException("Backup not found.");

        var state = await runtime.InspectAsync(tenant, cancellationToken);
        if (!state.Exists) throw new InvalidOperationException("The tenant container no longer exists.");
        var wasRunning = state.Running;
        if (wasRunning)
        {
            AccumulateUptime(tenant);
            await runtime.StopAsync(tenant, cancellationToken);
            tenant.Status = TenantStatus.Stopped;
            await db.SaveChangesAsync(cancellationToken);
        }

        await runtime.RestoreBackupAsync(tenant, backupId, cancellationToken);
        if (wasRunning)
        {
            await runtime.StartAsync(tenant, cancellationToken);
            tenant.Status = TenantStatus.Running;
            tenant.LastStartedUtc = DateTimeOffset.UtcNow;
            tenant.LastRequestUtc = tenant.LastStartedUtc.Value;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task StopIfIdleAsync(Guid tenantId, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        using var tenantLock = await operationLocks.AcquireAsync(tenantId, cancellationToken);
        var tenant = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Running || tenant.LastRequestUtc >= cutoff) return;

        var state = await runtime.InspectAsync(tenant, cancellationToken);
        if (state.Running)
        {
            AccumulateUptime(tenant);
            await runtime.StopAsync(tenant, cancellationToken);
        }

        tenant.Status = TenantStatus.Stopped;
        tenant.LastStartedUtc = null;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Stopped idle tenant {TenantId}", tenantId);
    }

    private static void AccumulateUptime(Tenant tenant)
    {
        if (tenant.LastStartedUtc is null) return;

        var elapsed = DateTimeOffset.UtcNow - tenant.LastStartedUtc.Value;
        if (elapsed > TimeSpan.Zero) tenant.TotalUptimeSeconds += (long)elapsed.TotalSeconds;
        tenant.LastStartedUtc = null;
    }

    private static void EnsurePaid(Tenant tenant)
    {
        if (tenant.Plan != TenantPlan.Paid)
            throw new InvalidOperationException("Managed backups are available only on the Paid plan.");
    }
}

public sealed record TenantSettingsUpdate(
    string Username,
    string? Password,
    bool EnableAi,
    string AiProvider,
    string AiModel,
    string? AiApiKey);