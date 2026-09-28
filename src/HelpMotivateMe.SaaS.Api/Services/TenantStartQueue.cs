using System.Threading.Channels;

namespace HelpMotivateMe.SaaS.Api.Services;

public sealed class TenantStartQueue
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();

    public bool Enqueue(Guid tenantId) => _queue.Writer.TryWrite(tenantId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _queue.Reader.ReadAllAsync(cancellationToken);
}