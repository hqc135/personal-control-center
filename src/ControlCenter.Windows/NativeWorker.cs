using System.Collections.Concurrent;
using System.Runtime.InteropServices;
namespace ControlCenter.Windows;
internal sealed class NativeWorker : IAsyncDisposable
{
    private readonly BlockingCollection<Action> queue = new();
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action cleanup;
    private int disposed;
    public NativeWorker(string name, Action cleanup)
    {
        this.cleanup = cleanup;
        var thread = new Thread(Run) { IsBackground = true, Name = name };
        thread.SetApartmentState(ApartmentState.MTA); thread.Start();
    }
    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(NativeWorker));
            queue.Add(() =>
            {
                if (ct.IsCancellationRequested) { completion.TrySetCanceled(ct); return; }
                try { completion.TrySetResult(action()); }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
        }
        catch (Exception ex) { completion.TrySetException(ex); }
        return completion.Task;
    }
    private void Run()
    {
        int hr = CoInitializeEx(0, 0);
        try
        {
            foreach (var action in queue.GetConsumingEnumerable()) action();
        }
        finally
        {
            try { cleanup(); finished.TrySetResult(); }
            catch (Exception ex) { finished.TrySetException(ex); }
            finally { if (hr >= 0) CoUninitialize(); queue.Dispose(); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        queue.CompleteAdding();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}

