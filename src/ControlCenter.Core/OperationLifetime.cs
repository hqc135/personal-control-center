namespace ControlCenter.Core;
// UI-thread-owned lifetime for one settings file operation. Closing cancels work and rejects late results.
public sealed class OperationLifetime : IDisposable
{
    private CancellationTokenSource? pending;
    private bool disposed;
    public bool Busy => pending is not null;
    public CancellationToken? TryBegin()
    {
        if (disposed || pending is not null) return null;
        pending = new(); return pending.Token;
    }
    public bool CanApply(CancellationToken token) => !disposed && pending is not null && pending.Token == token && !token.IsCancellationRequested;
    public void Complete(CancellationToken token)
    {
        if (pending is null || pending.Token != token) return;
        pending.Dispose(); pending = null;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; pending?.Cancel();
        // The outstanding operation owns completion/disposal; its token remains readable until then.
    }
}
