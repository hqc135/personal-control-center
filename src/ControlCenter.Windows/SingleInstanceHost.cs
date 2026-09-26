using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
namespace ControlCenter.Windows;
public sealed class SingleInstanceHost : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource lifetime = new();
    private Task? listener;
    private bool disposed;
    public bool IsPrimary { get; }
    public SingleInstanceHost(string product = "PCC")
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("无法确定当前用户。");
        pipeName = $"{product}.{sid}.{Process.GetCurrentProcess().SessionId}";
        mutex = new Mutex(true, @"Local\" + pipeName, out var created);
        IsPrimary = created;
    }
    public void Listen(Action show)
    {
        if (!IsPrimary || listener is not null || disposed) throw new InvalidOperationException();
        listener = Task.Run(async () =>
        {
            while (!lifetime.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(lifetime.Token);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    byte[] message = new byte[10];
                    await pipe.ReadExactlyAsync(message, deadline.Token);
                    if (Encoding.ASCII.GetString(message) == "ShowPanel\n") show();
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            }
        });
    }
    public async Task NotifyAsync()
    {
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(2000);
        await client.WriteAsync(Encoding.ASCII.GetBytes("ShowPanel\n"));
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
        // The listener owns pending I/O and observes cancellation before disposing its pipe.
        if (listener is null || listener.IsCompleted) lifetime.Dispose();
        else _ = listener.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
    }
}

