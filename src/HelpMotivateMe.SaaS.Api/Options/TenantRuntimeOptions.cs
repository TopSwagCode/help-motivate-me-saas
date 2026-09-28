using System.ComponentModel.DataAnnotations;

namespace HelpMotivateMe.SaaS.Api.Options;

public sealed class TenantRuntimeOptions
{
    public const string SectionName = "TenantRuntime";

    [Required]
    public string DockerEndpoint { get; init; } = "unix:///var/run/docker.sock";

    [Required]
    public string Image { get; init; } = "help-motivate-me:saas-local";

    [Required]
    public string Network { get; init; } = "help-motivate-me-saas-proxy";

    [Required]
    public string BaseDomain { get; init; } = "localhost";

    [Range(1, 65535)]
    public int PublicPort { get; init; } = 8088;

    [Range(10, 300)]
    public int StartTimeoutSeconds { get; init; } = 90;

    [Range(1, 1440)]
    public int IdleTimeoutMinutes { get; init; } = 30;

    [Range(5, 3600)]
    public int IdleSweepSeconds { get; init; } = 60;

    public string ManagedAiApiKey { get; init; } = string.Empty;

    public string ManagedAiProvider { get; init; } = "OpenAI";

    public string ManagedAiModel { get; init; } = "gpt-4.1-mini";

    [Required]
    public string ControlPlaneVolumeName { get; init; } = "help-motivate-me-saas_control-plane-data";

    [Required]
    public string BackupRootPath { get; init; } = "/data/backups";
}