namespace HelpMotivateMe.SaaS.Api.Models;

public sealed class Tenant
{
    public Guid Id { get; init; }
    public required string OwnerEmail { get; init; }
    public required string Subdomain { get; init; }
    public required string AppName { get; set; }
    public required string Username { get; set; }
    public required string VolumeName { get; init; }
    public string? ContainerId { get; set; }
    public TenantStatus Status { get; set; }
    public TenantPlan Plan { get; set; } = TenantPlan.Free;
    public TenantAiMode AiMode { get; set; } = TenantAiMode.Disabled;
    public string? AiProvider { get; set; }
    public string? AiModel { get; set; }
    public bool HasAiApiKey { get; set; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset? LastStartedUtc { get; set; }
    public DateTimeOffset LastRequestUtc { get; set; }
    public DateTimeOffset? LastLoginUtc { get; set; }
    public long TotalUptimeSeconds { get; set; }
}

public enum TenantStatus
{
    Provisioning,
    Running,
    Stopped,
    Failed
}

public enum TenantPlan
{
    Free,
    Paid
}

public enum TenantAiMode
{
    Disabled,
    BringYourOwn,
    Managed
}

public sealed record TenantConfiguration(
    string AppName,
    string Username,
    string Password,
    TenantAiMode AiMode,
    string? AiProvider,
    string? AiModel,
    string? AiApiKey);