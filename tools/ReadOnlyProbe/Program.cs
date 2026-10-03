using System.Text.Json;
using ControlCenter.Windows;
using ControlCenter.Windows.Audio;
using ControlCenter.Windows.Power;
if (args.Length == 1 && args[0] is "--desktop" or "--desktop-repeat")
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    await using var audio = new CoreAudioService();
    await using var power = new PowerSchemeService();
    await using var desktop = new DesktopFeatures(audio);
    try
    {
        var a = await audio.ReadAsync(timeout.Token).WaitAsync(timeout.Token);
        var p = await power.ReadAsync(timeout.Token).WaitAsync(timeout.Token);
        var d = await desktop.ReadAsync(timeout.Token).WaitAsync(timeout.Token);
        if (args[0] == "--desktop-repeat")
        {
            // This opt-in regression check expects a supported display on the test machine.
            bool passed = d.Displays.Length > 0 && d.Warnings.Length == 0;
            var samples = new List<object> { new { Count = d.Displays.Length, Displays = d.Displays.Select(x => new { x.Name, x.Percent }), d.Warnings } };
            for (int i = 0; i < 2; i++)
            {
                d = await desktop.ReadAsync(timeout.Token).WaitAsync(timeout.Token);
                passed &= d.Displays.Length > 0 && d.Warnings.Length == 0;
                samples.Add(new { Count = d.Displays.Length, Displays = d.Displays.Select(x => new { x.Name, x.Percent }), d.Warnings });
            }
            Console.WriteLine(JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        Console.WriteLine(JsonSerializer.Serialize(new {
            Mode = "Read only; no writes", ObservedAt = DateTimeOffset.Now,
            Audio = new { Devices = a.Devices.Select(x => x.Name), Volume = a.Level?.Volume, Muted = a.Level?.Muted },
            Power = new { Schemes = p.Schemes.Select(x => x.Name), p.OnAcPower },
            d.Battery, Microphone = d.Microphone is null ? null : new { d.Microphone.Name, d.Microphone.Muted },
            Displays = d.Displays.Select(x => new { x.Name, x.Percent }),
            Media = d.Media.Select(x => new { x.Playing, x.CanToggle, x.CanPrevious, x.CanNext }),
            NetworkPresent = !string.IsNullOrWhiteSpace(d.Network) && d.Network != "未连接",
            d.Bluetooth, d.Warnings
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
}
if (args.Length != 0) { Console.Error.WriteLine("Usage: ReadOnlyProbe [--desktop|--desktop-repeat]"); return 2; }
var result = ReadOnlyProbe.Run();
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return result.AudioHResult < 0 || result.PowerError != 0 ? 1 : 0;
