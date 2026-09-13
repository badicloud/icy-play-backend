namespace IcyPlay.Infrastructure.Facilities;

/// <summary>
/// One switch that clears every cached catalogue answer at once. The catalogue
/// is cached under several keys — the activity list, and a court list per sport
/// — and an admin who sets a court up means all of them are wrong, not one.
///
/// A singleton, because the scoped service that clears it lives for a single
/// request while the cache outlives them all.
/// </summary>
public sealed class CatalogCacheSignal : IDisposable
{
    private readonly Lock gate = new();

    private CancellationTokenSource source = new();

    /// <summary>Hand this to a cache entry so clearing evicts it with the rest.</summary>
    public CancellationToken Token
    {
        get
        {
            lock (gate)
            {
                return source.Token;
            }
        }
    }

    public void Clear()
    {
        CancellationTokenSource retired;

        lock (gate)
        {
            retired = source;
            source = new CancellationTokenSource();
        }

        // Cancelled after the swap, so an entry created during the cancellation
        // is registered against the new token rather than one already tripped.
        retired.Cancel();
        retired.Dispose();
    }

    public void Dispose()
    {
        lock (gate)
        {
            source.Dispose();
        }
    }
}
