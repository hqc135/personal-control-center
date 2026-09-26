namespace ControlCenter.Core;
// Shell-only resource lifecycle. Re-registration never reconstructs application services.
public sealed class TrayRegistration(Func<bool> add, Func<bool> setVersion, Action remove) : IDisposable
{
    private bool disposed;
    public bool Available { get; private set; }
    public bool Version4 { get; private set; }
    public bool Register()
    {
        if (disposed) return false;
        Available = add();
        Version4 = Available && setVersion();
        return Available;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; remove(); Available = false; Version4 = false;
    }
}
