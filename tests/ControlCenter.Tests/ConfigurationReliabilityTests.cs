using System.Text;
using ControlCenter.Core;
using ControlCenter.Windows;
using Xunit;
namespace ControlCenter.Tests;
public sealed class ConfigurationReliabilityTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PCC.B6-" + Guid.NewGuid().ToString("N"));
    private string PathToConfig => Path.Combine(directory, "config.json");
    public ConfigurationReliabilityTests() => Directory.CreateDirectory(directory);
    [Fact] public async Task ExternalEditIsPreservedUntilReload()
    {
        var repo = new JsonConfigRepository(directory); await repo.SaveAsync(new()); await repo.LoadAsync();
        var external = ConfigCodec.Encode(new() { Appearance = new("dark") }); await File.WriteAllBytesAsync(PathToConfig, external);
        await Assert.ThrowsAsync<ConfigConflictException>(() => repo.SaveAsync(new() { Modules = [] }));
        Assert.Equal(external, await File.ReadAllBytesAsync(PathToConfig));
        var loaded = await repo.LoadAsync(); await repo.SaveAsync(loaded.Config with { Modules = [] });
        Assert.Equal("dark", (await repo.LoadAsync()).Config.Appearance.Theme);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task ExternalCreationOrDeletionIsAConflict(bool creation)
    {
        var repo = new JsonConfigRepository(directory);
        if (!creation) await repo.SaveAsync(new());
        await repo.LoadAsync();
        if (creation) await File.WriteAllBytesAsync(PathToConfig, ConfigCodec.Encode(new()));
        else File.Delete(PathToConfig);
        await Assert.ThrowsAsync<ConfigConflictException>(() => repo.SaveAsync(new()));
        Assert.Equal(creation, File.Exists(PathToConfig));
    }
    [Fact] public async Task TwoLoadedRepositoriesCannotBothOverwriteSameRevision()
    {
        var first = new JsonConfigRepository(directory); await first.SaveAsync(new());
        var second = new JsonConfigRepository(directory); await first.LoadAsync(); await second.LoadAsync();
        var results = await Task.WhenAll(TrySave(first, "dark"), TrySave(second, "light"));
        Assert.Equal(1, results.Count(x => x));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
    private static async Task<bool> TrySave(JsonConfigRepository repository, string theme)
    {
        try { await repository.SaveAsync(new() { Appearance = new(theme) }); return true; }
        catch (ConfigConflictException) { return false; }
    }
    [Fact] public async Task CancelWhileWriterLockedDoesNotWriteAndReleasesRepositoryGate()
    {
        var repo = new JsonConfigRepository(directory); await repo.SaveAsync(new()); var before = await File.ReadAllBytesAsync(PathToConfig);
        using (var held = new FileStream(Path.Combine(directory, ".config-write.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.SaveAsync(new() { Modules = [] }, cts.Token));
        }
        Assert.Equal(before, await File.ReadAllBytesAsync(PathToConfig));
        await repo.SaveAsync(new() { Modules = [] });
    }
    [Fact] public async Task InvalidFieldsRecoverWithoutCrashingAndOriginalRemainsUntilSave()
    {
        const string invalid = """{"proxy":{"port":0}}""";
        await File.WriteAllTextAsync(PathToConfig, invalid);
        var repo = new JsonConfigRepository(directory); var loaded = await repo.LoadAsync();
        Assert.NotNull(loaded.Warning); Assert.False(loaded.ReadOnly);
        Assert.Equal(invalid, await File.ReadAllTextAsync(PathToConfig));
        await repo.SaveAsync(loaded.Config);
        Assert.Equal(7897, (await repo.LoadAsync()).Config.Proxy.Port);
    }
    [Fact] public async Task OversizeUnreadableConfigIsReadOnlyAndCanRecoverAfterFix()
    {
        await File.WriteAllBytesAsync(PathToConfig, new byte[ConfigCodec.MaxBytes + 1]);
        var repo = new JsonConfigRepository(directory); Assert.True((await repo.LoadAsync()).ReadOnly);
        await Assert.ThrowsAsync<IOException>(() => repo.SaveAsync(new()));
        await File.WriteAllBytesAsync(PathToConfig, ConfigCodec.Encode(new()));
        Assert.False((await repo.LoadAsync()).ReadOnly); await repo.SaveAsync(new());
    }
    [Fact] public async Task FutureReadOnlyCanBeReloadedAfterExternalRepair()
    {
        await File.WriteAllTextAsync(PathToConfig, """{"schemaVersion":2}""");
        var repo = new JsonConfigRepository(directory); Assert.True((await repo.LoadAsync()).ReadOnly);
        await Assert.ThrowsAsync<FutureConfigException>(() => repo.SaveAsync(new()));
        await File.WriteAllBytesAsync(PathToConfig, ConfigCodec.Encode(new()));
        Assert.False((await repo.LoadAsync()).ReadOnly); await repo.SaveAsync(new() { Modules = [] });
    }
    [Fact] public async Task LockedOldBackupIsWarningAfterSuccessfulCommitAndLaterPruned()
    {
        var repo = new JsonConfigRepository(directory);
        for (int i = 0; i < 6; i++) await repo.SaveAsync(new() { Modules = i % 2 == 0 ? [] : ["audio"] });
        string oldest = Directory.GetFiles(Path.Combine(directory, "backups")).Order().First();
        using (var held = new FileStream(oldest, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await repo.SaveAsync(new() { Appearance = new("dark") });
            Assert.Contains("已保存", repo.LastSaveWarning);
            Assert.Equal("dark", ConfigCodec.Decode(await File.ReadAllBytesAsync(PathToConfig)).Appearance.Theme);
        }
        await repo.SaveAsync(new());
        Assert.Null(repo.LastSaveWarning); Assert.Equal(5, Directory.GetFiles(Path.Combine(directory, "backups")).Length);
    }
    [Fact] public async Task RotationDoesNotDeleteUnownedJsonFiles()
    {
        var repo = new JsonConfigRepository(directory); await repo.SaveAsync(new()); await repo.SaveAsync(new());
        string other = Path.Combine(directory, "backups", "000-private.json"); await File.WriteAllTextAsync(other, "keep");
        for (int i = 0; i < 6; i++) await repo.SaveAsync(new());
        Assert.Equal("keep", await File.ReadAllTextAsync(other));
        Assert.DoesNotContain(await repo.ListBackupsAsync(), x => x.Id == "000-private.json");
    }
    [Fact] public void ClosingOperationRejectsLateResultAndPreventsNewWork()
    {
        var lifetime = new OperationLifetime(); var token = lifetime.TryBegin()!.Value;
        Assert.True(lifetime.CanApply(token)); Assert.Null(lifetime.TryBegin());
        lifetime.Dispose(); Assert.True(token.IsCancellationRequested); Assert.False(lifetime.CanApply(token));
        lifetime.Complete(token); Assert.False(lifetime.Busy); Assert.Null(lifetime.TryBegin());
    }
    [Fact] public void OldCompletionCannotClearNewOperation()
    {
        using var lifetime = new OperationLifetime();
        var first = lifetime.TryBegin()!.Value; lifetime.Complete(first);
        var second = lifetime.TryBegin()!.Value;
        lifetime.Complete(first); Assert.True(lifetime.Busy); Assert.True(lifetime.CanApply(second)); Assert.False(lifetime.CanApply(first));
        lifetime.Complete(second);
    }
    public void Dispose() => Directory.Delete(directory, true);
}
