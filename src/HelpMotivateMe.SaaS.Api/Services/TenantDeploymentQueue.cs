using System.Threading.Channels;

namespace HelpMotivateMe.SaaS.Api.Services;

public sealed class TenantDeploymentQueue
{
    private readonly Lock _lock = new();
    private readonly Channel<TenantDeploymentRequest> _queue = Channel.CreateBounded<TenantDeploymentRequest>(1);
    private TenantDeploymentStatus _status = TenantDeploymentStatus.Idle;

    public TenantDeploymentStatus Status
    {
        get
        {
            lock (_lock) return _status;
        }
    }

    public bool TryEnqueue(IReadOnlyList<TenantDeploymentTarget> tenants, string targetImage)
    {
        lock (_lock)
        {
            if (_status.State is TenantDeploymentState.Queued or TenantDeploymentState.Running) return false;

            var request = new TenantDeploymentRequest(tenants, targetImage);
            _status = new TenantDeploymentStatus(TenantDeploymentState.Queued, targetImage, tenants.Count, 0,
                null, null, DateTimeOffset.UtcNow, null);
            if (_queue.Writer.TryWrite(request)) return true;

            _status = TenantDeploymentStatus.Idle;
            return false;
        }
    }

    public IAsyncEnumerable<TenantDeploymentRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _queue.Reader.ReadAllAsync(cancellationToken);

    public void SetRunning(string subdomain)
    {
        lock (_lock) _status = _status with { State = TenantDeploymentState.Running, CurrentTenant = subdomain };
    }

    public void SetProgress(int completed)
    {
        lock (_lock) _status = _status with { Completed = completed };
    }

    public void SetCompleted()
    {
        lock (_lock) _status = _status with
        {
            State = TenantDeploymentState.Completed,
            CurrentTenant = null,
            CompletedUtc = DateTimeOffset.UtcNow
        };
    }

    public void SetFailed(string subdomain, string error)
    {
        lock (_lock) _status = _status with
        {
            State = TenantDeploymentState.Failed,
            CurrentTenant = subdomain,
            Error = error,
            CompletedUtc = DateTimeOffset.UtcNow
        };
    }
}

public enum TenantDeploymentState
{
    Idle,
    Queued,
    Running,
    Completed,
    Failed
}

public sealed record TenantDeploymentTarget(Guid Id, string Subdomain);

public sealed record TenantDeploymentRequest(IReadOnlyList<TenantDeploymentTarget> Tenants, string TargetImage);

public sealed record TenantDeploymentStatus(
    TenantDeploymentState State,
    string? TargetImage,
    int Total,
    int Completed,
    string? CurrentTenant,
    string? Error,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc)
{
    public static TenantDeploymentStatus Idle { get; } =
        new(TenantDeploymentState.Idle, null, 0, 0, null, null, null, null);
}