namespace ControlCenter.Core;
// This type has no Windows dependencies and cannot perform host-system writes.
public sealed class DemoSession
{
    public double Volume { get; private set; } = 60;
    public bool Muted { get; private set; }
    public string Device { get; private set; } = "扬声器";
    public string Power { get; private set; } = "平衡";
    public DateTimeOffset? AwakeUntil { get; private set; }
    public void SetVolume(double value) => Volume = Math.Clamp(value, 0, 100);
    public void ToggleMute() => Muted = !Muted;
    public void SetDevice(string value) => Device = value;
    public void SetPower(string value) => Power = value;
    public void StartAwake(int minutes, DateTimeOffset now)
    {
        if (minutes is < 1 or > 480) throw new ArgumentOutOfRangeException(nameof(minutes));
        AwakeUntil = now.AddMinutes(minutes);
    }
    public void StopAwake() => AwakeUntil = null;
    public string AwakeLabel(DateTimeOffset now)
    {
        if (AwakeUntil is null) return "未开启";
        if (now >= AwakeUntil) { AwakeUntil = null; return "未开启"; }
        return $"剩余 {Math.Ceiling((AwakeUntil.Value - now).TotalMinutes)} 分钟";
    }
}

