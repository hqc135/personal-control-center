namespace ControlCenter.Core;
public enum PanelPhase { Closed, Opening, Open, Closing }
public sealed class PanelTransition
{
    public PanelPhase Phase { get; private set; } = PanelPhase.Closed;
    public long Generation { get; private set; }
    public long Request(bool visible) { Phase = visible ? PanelPhase.Opening : PanelPhase.Closing; return ++Generation; }
    public bool Complete(long generation)
    {
        if (generation != Generation || Phase is PanelPhase.Open or PanelPhase.Closed) return false;
        Phase = Phase == PanelPhase.Opening ? PanelPhase.Open : PanelPhase.Closed;
        return true;
    }
}
public readonly record struct ScreenRect(double Left, double Top, double Width, double Height);
public static class PanelPlacement
{
    public static (double X, double Y) Clamp(double x, double y, double width, double height, ScreenRect work, double margin = 12)
        => (Math.Clamp(x, work.Left + margin, Math.Max(work.Left + margin, work.Left + work.Width - width - margin)),
            Math.Clamp(y, work.Top + margin, Math.Max(work.Top + margin, work.Top + work.Height - height - margin)));
}

