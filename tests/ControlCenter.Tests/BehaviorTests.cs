using System.Text;
using ControlCenter.Core;
using ControlCenter.Windows;
using Xunit;
namespace ControlCenter.Tests;
public class BehaviorTests
{
    [Fact] public void ReversalRejectsStaleCompletion()
    {
        var state = new PanelTransition();
        var opening = state.Request(true); var closing = state.Request(false); var reopen = state.Request(true);
        Assert.False(state.Complete(opening)); Assert.False(state.Complete(closing));
        Assert.Equal(PanelPhase.Opening, state.Phase);
        Assert.True(state.Complete(reopen)); Assert.Equal(PanelPhase.Open, state.Phase);
        Assert.False(state.Complete(reopen)); Assert.Equal(PanelPhase.Open, state.Phase);
        var close = state.Request(false); Assert.True(state.Complete(close)); Assert.Equal(PanelPhase.Closed, state.Phase);
    }
    [Theory]
    [InlineData(-3000, -500, -1908, 12)]
    [InlineData(100, 1200, -372, 468)]
    public void PlacementClampsToNegativeMonitor(double x, double y, double expectedX, double expectedY)
    {
        var result = PanelPlacement.Clamp(x, y, 360, 600, new(-1920, 0, 1920, 1080));
        Assert.Equal(expectedX, result.X); Assert.Equal(expectedY, result.Y);
    }
    [Fact] public void OversizedPanelDoesNotThrow() => Assert.Equal((12d, 12d), PanelPlacement.Clamp(0, 0, 900, 900, new(0, 0, 100, 100)));
    [Theory] [InlineData(0)] [InlineData(481)] [InlineData(-1)]
    public void DemoRejectsOutOfRangeAwake(int minutes) => Assert.Throws<ArgumentOutOfRangeException>(() => new DemoSession().StartAwake(minutes, DateTimeOffset.UtcNow));
    [Fact] public void DemoAwakeExpiresAndRenewalReplacesDeadline()
    {
        var demo = new DemoSession(); var now = DateTimeOffset.Parse("2026-09-26T00:00:00Z");
        demo.StartAwake(30, now); demo.StartAwake(60, now.AddMinutes(10));
        Assert.Equal(now.AddMinutes(70), demo.AwakeUntil);
        Assert.Equal("未开启", demo.AwakeLabel(now.AddMinutes(70))); Assert.Null(demo.AwakeUntil);
    }
    [Fact] public void DemoVolumeClampsAndMutePreservesVolume()
    {
        var demo = new DemoSession(); demo.SetVolume(110); Assert.Equal(100, demo.Volume);
        demo.ToggleMute(); Assert.True(demo.Muted); Assert.Equal(100, demo.Volume);
        demo.SetVolume(-10); Assert.Equal(0, demo.Volume);
    }
    [Theory]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":999}")]
    public void FutureConfigIsDistinct(string json) => Assert.Throws<FutureConfigException>(() => ConfigCodec.Decode(Encoding.UTF8.GetBytes(json)));
    [Theory]
    [InlineData("{\"modules\":[\"audio\",\"audio\"]}")]
    [InlineData("{\"appearance\":null}")]
    [InlineData("{\"modules\":[\"unknown\"]}")]
    [InlineData("{\"schemaVersion\":0}")]
    public void InvalidConfigRejected(string json) => Assert.Throws<InvalidDataException>(() => ConfigCodec.Decode(Encoding.UTF8.GetBytes(json)));
    [Fact] public void UnknownFieldsAndMissingOptionalFieldsAccepted()
    {
        var config = ConfigCodec.Decode(Encoding.UTF8.GetBytes("{\"extension\":true}"));
        Assert.Equal("system", config.Appearance.Theme); Assert.Equal(5, config.Modules.Length);
    }
    [Fact] public void TooLargeConfigRejected() => Assert.Throws<InvalidDataException>(() => ConfigCodec.Decode(new byte[ConfigCodec.MaxBytes + 1]));
    [Fact] public void SettingsRoundTripPreservesUnimplementedModuleConfiguration()
    {
        var source = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"proxy\":{\"port\":7897},\"appearance\":{\"theme\":\"dark\",\"futureAccent\":\"blue\"}}");
        var decoded = ConfigCodec.Decode(source);
        var updated = decoded with { Appearance = decoded.Appearance with { Theme = "light" } };
        using var document = System.Text.Json.JsonDocument.Parse(ConfigCodec.Encode(updated));
        Assert.Equal(7897, document.RootElement.GetProperty("proxy").GetProperty("port").GetInt32());
        Assert.Equal("blue", document.RootElement.GetProperty("appearance").GetProperty("futureAccent").GetString());
    }
    [Fact] public void OversizedWriteIsRejectedBeforeTouchingDisk()
    {
        var config = new AppConfig { Appearance = new(FontFamily: new string('x', ConfigCodec.MaxBytes)) };
        Assert.Throws<InvalidDataException>(() => ConfigCodec.Encode(config));
    }
}
public sealed class RepositoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PCC.Tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "config.json");
    public RepositoryTests() => Directory.CreateDirectory(directory);
    [Fact] public async Task FutureConfigCannotBeOverwritten()
    {
        const string future = "{\"schemaVersion\":9}";
        await File.WriteAllTextAsync(FilePath, future);
        var repo = new JsonConfigRepository(directory); var loaded = await repo.LoadAsync();
        Assert.True(loaded.ReadOnly);
        await Assert.ThrowsAsync<FutureConfigException>(() => repo.SaveAsync(new()));
        Assert.Equal(future, await File.ReadAllTextAsync(FilePath));
    }
    [Fact] public async Task FutureConfigAppearingAfterLoadIsProtected()
    {
        var repo = new JsonConfigRepository(directory); await repo.LoadAsync();
        await File.WriteAllTextAsync(FilePath, "{\"schemaVersion\":2}");
        await Assert.ThrowsAsync<FutureConfigException>(() => repo.SaveAsync(new()));
    }
    [Fact] public async Task InvalidOriginalPreservedUntilExplicitSave()
    {
        await File.WriteAllTextAsync(FilePath, "broken");
        var result = await new JsonConfigRepository(directory).LoadAsync();
        Assert.NotNull(result.Warning); Assert.Equal("broken", await File.ReadAllTextAsync(FilePath));
    }
    [Fact] public async Task BackupRecoveryAndRotation()
    {
        var repo = new JsonConfigRepository(directory);
        for (int i = 0; i < 8; i++) await repo.SaveAsync(new() { Appearance = new(i % 2 == 0 ? "light" : "dark") });
        Assert.Equal(5, Directory.GetFiles(Path.Combine(directory, "backups")).Length);
        await File.WriteAllTextAsync(FilePath, "{broken");
        var result = await repo.LoadAsync();
        Assert.NotNull(result.Warning); Assert.Equal("light", result.Config.Appearance.Theme);
        Assert.Equal("{broken", await File.ReadAllTextAsync(FilePath));
    }
    [Fact] public async Task ConcurrentSavesLeaveValidAtomicFile()
    {
        var repo = new JsonConfigRepository(directory);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => repo.SaveAsync(new() { Appearance = new(i % 2 == 0 ? "dark" : "light") })));
        Assert.Null((await repo.LoadAsync()).Warning);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
    [Fact] public async Task CancelledSaveLeavesOriginal()
    {
        var repo = new JsonConfigRepository(directory); await repo.SaveAsync(new());
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.SaveAsync(new() { Appearance = new("dark") }, ct.Token));
        Assert.Equal("system", (await repo.LoadAsync()).Config.Appearance.Theme);
    }
    public void Dispose() => Directory.Delete(directory, true); // Exact unique test-owned temporary directory.
}

