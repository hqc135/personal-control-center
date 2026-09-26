using ControlCenter.Windows;
using Xunit;
namespace ControlCenter.Tests;
public class SingleInstanceTests
{
    [Fact]
    public void SecondInstanceNotifiesPrimaryWithoutStartingAnotherOwner()
    {
        // Keep acquisition/disposal on one dedicated thread: Windows mutex ownership is thread-bound.
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var name = "PCC.Test." + Guid.NewGuid().ToString("N");
                using var first = new SingleInstanceHost(name);
                Assert.True(first.IsPrimary);
                using var received = new ManualResetEventSlim();
                first.Listen(() => received.Set());
                using var second = new SingleInstanceHost(name);
                Assert.False(second.IsPrimary);
                second.NotifyAsync().GetAwaiter().GetResult();
                Assert.True(received.Wait(TimeSpan.FromSeconds(4)));
                Assert.Throws<InvalidOperationException>(() => first.Listen(() => { }));
            }
            catch (Exception ex) { error = ex; }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)));
        Assert.Null(error);
    }
    [Fact]
    public void DisposeReleasesOwnershipAndIsIdempotent()
    {
        var name = "PCC.Test." + Guid.NewGuid().ToString("N");
        var host = new SingleInstanceHost(name);
        host.Dispose(); host.Dispose();
        using var next = new SingleInstanceHost(name);
        Assert.True(next.IsPrimary);
    }
}
