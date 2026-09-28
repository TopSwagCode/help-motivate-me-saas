using HelpMotivateMe.SaaS.Api.Models;

namespace HelpMotivateMe.SaaS.Api.Services;

public interface ITenantRuntime
{
    Task<string> CreateAsync(Tenant tenant, TenantConfiguration configuration, CancellationToken cancellationToken);
    Task<TenantConfiguration> ReadConfigurationAsync(Tenant tenant, CancellationToken cancellationToken);
    Task<string> ReplaceAsync(Tenant tenant, TenantConfiguration configuration, CancellationToken cancellationToken);
    Task<long> CreateBackupAsync(Tenant tenant, Guid backupId, CancellationToken cancellationToken);
    Task RestoreBackupAsync(Tenant tenant, Guid backupId, CancellationToken cancellationToken);
    Task StartAsync(Tenant tenant, CancellationToken cancellationToken);
    Task StopAsync(Tenant tenant, CancellationToken cancellationToken);
    Task<TenantRuntimeState> InspectAsync(Tenant tenant, CancellationToken cancellationToken);
}

public sealed record TenantRuntimeState(bool Exists, bool Running, bool Healthy);