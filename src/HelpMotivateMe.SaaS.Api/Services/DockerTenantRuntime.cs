using Docker.DotNet;
using Docker.DotNet.Models;
using HelpMotivateMe.SaaS.Api.Models;
using HelpMotivateMe.SaaS.Api.Options;
using Microsoft.Extensions.Options;

namespace HelpMotivateMe.SaaS.Api.Services;

public sealed class DockerTenantRuntime : ITenantRuntime, IDisposable
{
    private readonly DockerClient _client;
    private readonly TenantRuntimeOptions _options;

    public DockerTenantRuntime(IOptions<TenantRuntimeOptions> options)
    {
        _options = options.Value;
        _client = new DockerClientConfiguration(new Uri(_options.DockerEndpoint)).CreateClient();
    }

    public async Task<string> CreateAsync(Tenant tenant, TenantConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await EnsureVolumeAsync(tenant.VolumeName, tenant.Id, cancellationToken);

        var resourceName = GetResourceName(tenant.Id);
        var host = $"{tenant.Subdomain}.{_options.BaseDomain}";
        var response = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = resourceName,
            Image = _options.Image,
            Env = BuildEnvironment(configuration, _options),
            Healthcheck = new HealthConfig
            {
                Test = ["CMD", "curl", "--fail", "--silent", "http://localhost:8080/health/ready"],
                Interval = TimeSpan.FromSeconds(5),
                Timeout = TimeSpan.FromSeconds(5),
                Retries = 10,
                StartPeriod = 10_000_000_000
            },
            Labels = new Dictionary<string, string>
            {
                ["com.helpmotivateme.managed"] = "true",
                ["com.helpmotivateme.tenant-id"] = tenant.Id.ToString(),
                ["traefik.enable"] = "true",
                ["traefik.docker.network"] = _options.Network,
                [$"traefik.http.routers.{resourceName}.rule"] = $"Host(`{host}`)",
                [$"traefik.http.routers.{resourceName}.entrypoints"] = "web",
                [$"traefik.http.routers.{resourceName}.priority"] = "100",
                [$"traefik.http.routers.{resourceName}.middlewares"] = $"{resourceName}-activity",
                [$"traefik.http.middlewares.{resourceName}-activity.forwardauth.address"] =
                    $"http://control-plane:8080/internal/tenants/{tenant.Id}/activity",
                [$"traefik.http.middlewares.{resourceName}-activity.forwardauth.trustForwardHeader"] = "true",
                [$"traefik.http.services.{resourceName}.loadbalancer.server.port"] = "8080"
            },
            HostConfig = new HostConfig
            {
                Binds = [$"{tenant.VolumeName}:/data"],
                NetworkMode = _options.Network,
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
                Memory = 512 * 1024 * 1024,
                NanoCPUs = 1_000_000_000,
                PidsLimit = 256
            }
        }, cancellationToken);

        return response.ID;
    }

    public async Task<TenantConfiguration> ReadConfigurationAsync(Tenant tenant,
        CancellationToken cancellationToken)
    {
        var container = await _client.Containers.InspectContainerAsync(RequireContainerId(tenant), cancellationToken);
        VerifyOwnership(container.Config.Labels, tenant.Id);
        var environment = container.Config.Env ?? [];

        return new TenantConfiguration(
            NullIfEmpty(GetEnvironmentValue(environment, "Pwa__Name")) ?? tenant.AppName,
            GetEnvironmentValue(environment, "SingleUser__Username"),
            GetEnvironmentValue(environment, "SingleUser__Password"),
            tenant.AiMode,
            NullIfEmpty(GetEnvironmentValue(environment, "AI__Provider")),
            NullIfEmpty(GetEnvironmentValue(environment, "AI__ChatModel")),
            NullIfEmpty(GetEnvironmentValue(environment, "AI__ApiKey")));
    }

    public async Task<string> ReplaceAsync(Tenant tenant, TenantConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var oldContainerId = RequireContainerId(tenant);
        var oldContainer = await _client.Containers.InspectContainerAsync(oldContainerId, cancellationToken);
        VerifyOwnership(oldContainer.Config.Labels, tenant.Id);
        var wasRunning = oldContainer.State.Running;
        var replacementId = string.Empty;
        var oldName = GetResourceName(tenant.Id);
        var temporaryName = $"{oldName}-previous-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        try
        {
            if (wasRunning)
                await _client.Containers.StopContainerAsync(oldContainerId,
                    new ContainerStopParameters { WaitBeforeKillSeconds = 15 }, cancellationToken);

            await _client.Containers.RenameContainerAsync(oldContainerId,
                new ContainerRenameParameters { NewName = temporaryName }, cancellationToken);
            replacementId = await CreateAsync(tenant, configuration, cancellationToken);
            await _client.Containers.StartContainerAsync(replacementId, new ContainerStartParameters(), cancellationToken);
            await WaitUntilHealthyAsync(replacementId, cancellationToken);
            await _client.Containers.RemoveContainerAsync(oldContainerId,
                new ContainerRemoveParameters { Force = true, RemoveVolumes = false }, cancellationToken);
            return replacementId;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(replacementId))
            {
                try
                {
                    await _client.Containers.RemoveContainerAsync(replacementId,
                        new ContainerRemoveParameters { Force = true, RemoveVolumes = false }, CancellationToken.None);
                }
                catch
                {
                    // Preserve the original exception; reconciliation can remove a failed replacement.
                }
            }

            try
            {
                await _client.Containers.RenameContainerAsync(oldContainerId,
                    new ContainerRenameParameters { NewName = oldName }, CancellationToken.None);
                if (wasRunning)
                    await _client.Containers.StartContainerAsync(oldContainerId, new ContainerStartParameters(),
                        CancellationToken.None);
            }
            catch
            {
                // Preserve the replacement failure; the old container and volume remain available for repair.
            }

            throw;
        }
    }

    public async Task StartAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var containerId = RequireContainerId(tenant);
        await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);
        await WaitUntilHealthyAsync(containerId, cancellationToken);
    }

    public async Task<long> CreateBackupAsync(Tenant tenant, Guid backupId,
        CancellationToken cancellationToken)
    {
        var relativePath = GetBackupRelativePath(tenant.Id, backupId);
        await RunBackupHelperAsync(tenant, backupId,
            $"mkdir -p /control-plane/backups/{tenant.Id:N} && tar -czf /control-plane/{relativePath} -C /tenant-data .",
            cancellationToken);

        var file = new FileInfo(Path.Combine(_options.BackupRootPath, tenant.Id.ToString("N"),
            $"{backupId:N}.tar.gz"));
        if (!file.Exists) throw new InvalidOperationException("Backup helper completed without creating an archive.");
        return file.Length;
    }

    public async Task RestoreBackupAsync(Tenant tenant, Guid backupId,
        CancellationToken cancellationToken)
    {
        var file = Path.Combine(_options.BackupRootPath, tenant.Id.ToString("N"), $"{backupId:N}.tar.gz");
        if (!File.Exists(file)) throw new FileNotFoundException("The backup archive no longer exists.", file);

        var relativePath = GetBackupRelativePath(tenant.Id, backupId);
        await RunBackupHelperAsync(tenant, backupId,
            $"find /tenant-data -mindepth 1 -maxdepth 1 -exec rm -rf -- {{}} + && tar -xzf /control-plane/{relativePath} -C /tenant-data",
            cancellationToken);
    }

    public async Task StopAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var containerId = RequireContainerId(tenant);
        await _client.Containers.StopContainerAsync(containerId,
            new ContainerStopParameters { WaitBeforeKillSeconds = 15 }, cancellationToken);
    }

    public async Task<TenantRuntimeState> InspectAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenant.ContainerId)) return new TenantRuntimeState(false, false, false);

        try
        {
            var container = await _client.Containers.InspectContainerAsync(tenant.ContainerId, cancellationToken);
            VerifyOwnership(container.Config.Labels, tenant.Id);
            return new TenantRuntimeState(true, container.State.Running,
                string.Equals(container.State.Health?.Status, "healthy", StringComparison.OrdinalIgnoreCase),
                container.Image);
        }
        catch (DockerContainerNotFoundException)
        {
            return new TenantRuntimeState(false, false, false);
        }
    }

    public void Dispose() => _client.Dispose();

    private async Task EnsureVolumeAsync(string volumeName, Guid tenantId, CancellationToken cancellationToken)
    {
        await _client.Volumes.CreateAsync(new VolumesCreateParameters
        {
            Name = volumeName,
            Labels = new Dictionary<string, string>
            {
                ["com.helpmotivateme.managed"] = "true",
                ["com.helpmotivateme.tenant-id"] = tenantId.ToString()
            }
        }, cancellationToken);
    }

    private async Task WaitUntilHealthyAsync(string containerId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.StartTimeoutSeconds));

        while (true)
        {
            var container = await _client.Containers.InspectContainerAsync(containerId, timeout.Token);
            var health = container.State.Health?.Status;
            if (string.Equals(health, "healthy", StringComparison.OrdinalIgnoreCase)) return;
            if (!container.State.Running || string.Equals(health, "unhealthy", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Tenant failed to start. State: {container.State.Status}; health: {health ?? "none"}.");

            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
        }
    }

    private async Task RunBackupHelperAsync(Tenant tenant, Guid operationId, string command,
        CancellationToken cancellationToken)
    {
        var response = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = $"hmm-backup-{tenant.Id:N}-{operationId:N}",
            Image = _options.Image,
            User = "root",
            Entrypoint = ["/bin/sh", "-c"],
            Cmd = [command],
            Labels = new Dictionary<string, string>
            {
                ["com.helpmotivateme.backup-helper"] = "true",
                ["com.helpmotivateme.tenant-id"] = tenant.Id.ToString()
            },
            HostConfig = new HostConfig
            {
                Binds =
                [
                    $"{tenant.VolumeName}:/tenant-data",
                    $"{_options.ControlPlaneVolumeName}:/control-plane"
                ],
                NetworkMode = "none",
                Memory = 256 * 1024 * 1024,
                NanoCPUs = 500_000_000,
                PidsLimit = 128
            }
        }, cancellationToken);

        try
        {
            await _client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), cancellationToken);
            var result = await _client.Containers.WaitContainerAsync(response.ID, cancellationToken);
            if (result.StatusCode != 0)
                throw new InvalidOperationException($"Backup helper exited with status {result.StatusCode}.");
        }
        finally
        {
            await _client.Containers.RemoveContainerAsync(response.ID,
                new ContainerRemoveParameters { Force = true, RemoveVolumes = false }, CancellationToken.None);
        }
    }

    private static string RequireContainerId(Tenant tenant) =>
        !string.IsNullOrWhiteSpace(tenant.ContainerId)
            ? tenant.ContainerId
            : throw new InvalidOperationException("Tenant has no container ID.");

    private static string GetResourceName(Guid tenantId) => $"hmm-tenant-{tenantId:N}";

    private static string GetBackupRelativePath(Guid tenantId, Guid backupId) =>
        $"backups/{tenantId:N}/{backupId:N}.tar.gz";

    internal static List<string> BuildEnvironment(TenantConfiguration configuration, TenantRuntimeOptions options)
    {
        var apiKey = configuration.AiMode == TenantAiMode.Managed
            ? options.ManagedAiApiKey
            : configuration.AiApiKey ?? string.Empty;
        var provider = configuration.AiMode == TenantAiMode.Managed
            ? options.ManagedAiProvider
            : configuration.AiProvider ?? "OpenAI";
        var model = configuration.AiMode == TenantAiMode.Managed
            ? options.ManagedAiModel
            : configuration.AiModel ?? "gpt-4.1-mini";

        return
        [
            "ASPNETCORE_ENVIRONMENT=Production",
            $"Pwa__Name={configuration.AppName}",
            $"Pwa__ShortName={configuration.AppName}",
            $"SingleUser__Username={configuration.Username}",
            $"SingleUser__Password={configuration.Password}",
            $"SingleUser__DisplayName={configuration.Username}",
            $"AI__Provider={provider}",
            $"AI__ApiKey={apiKey}",
            "AI__BaseUrl=https://api.openai.com/v1/",
            $"AI__ChatModel={model}",
            $"AI__EnableTranscription={configuration.AiMode != TenantAiMode.Disabled}",
            "AI__TranscriptionModel=whisper-1",
            "OpenAI__ApiKey="
        ];
    }

    private static string GetEnvironmentValue(IList<string> environment, string name)
    {
        var prefix = $"{name}=";
        var value = environment.FirstOrDefault(item => item.StartsWith(prefix, StringComparison.Ordinal));
        return value?[prefix.Length..] ?? string.Empty;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void VerifyOwnership(IDictionary<string, string>? labels, Guid tenantId)
    {
        if (labels is null || !labels.TryGetValue("com.helpmotivateme.tenant-id", out var value) ||
            !string.Equals(value, tenantId.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Docker resource ownership does not match the tenant.");
    }
}