namespace HelpMotivateMe.SaaS.Api.Models;

public sealed class TenantBackup
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public required string FileName { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}