using System.Collections.Concurrent;

namespace HelpMotivateMe.SaaS.Api.Services;

public sealed class TenantOperationLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenantLock = _locks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));
        await tenantLock.WaitAsync(cancellationToken);
        return new Releaser(tenantLock);
    }

    private sealed class Releaser(SemaphoreSlim tenantLock) : IDisposable
    {
        public void Dispose() => tenantLock.Release();
    }
}