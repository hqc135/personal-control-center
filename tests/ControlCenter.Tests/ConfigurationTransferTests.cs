using System.Text;
using System.Text.Json;
using ControlCenter.Core;
using ControlCenter.Windows;
using Xunit;
namespace ControlCenter.Tests;
public class ConfigurationTransferTests
{
    [Theory] [InlineData("config.json")] [InlineData("trusted-shortcuts.json")] [InlineData("config-20260926.json")]
    public async Task ExportCannotOverwriteRuntimeConfigNames(string name)
        => await Assert.ThrowsAsync<InvalidDataException>(() => ConfigFiles.ExportAsync(name, new()));
    private static AppConfig Source => new()
    {
        Hotkey = new(3, 80), Modules = ["proxy", "audio"],
        Proxy = new(TestUrl: "https://example.com/?token=private", ShortcutId: "app"),
        Shortcuts = [new("app", "Program", "application", @"C:\Private\app.exe", ["secret"]),
            new("dir", "Folder", "folder", @"C:\Private"), new("web", "Site", "url", "https://example.com/?token=private"),
            new("downloads", "Downloads", "knownFolder", "Downloads")],
        Additional = new() { ["secret"] = JsonSerializer.SerializeToElement("private-data") }
    };
    [Fact] public void PortableExportStripsMachineAndNetworkTargetsAndUnknownFields()
    {
        var portable = ConfigurationTransfer.Portable(Source);
        var json = Encoding.UTF8.GetString(ConfigCodec.Encode(portable));
        Assert.Null(portable.Hotkey); Assert.Null(portable.Proxy.TestUrl); Assert.Null(portable.Proxy.ShortcutId);
        Assert.Null(portable.Additional); Assert.Single(portable.Shortcuts); Assert.Equal("knownFolder", portable.Shortcuts[0].Kind);
        Assert.DoesNotContain("Private", json); Assert.DoesNotContain("secret", json); Assert.DoesNotContain("token", json);
        Assert.Equal(new[] { "proxy", "audio" }, portable.Modules);
    }
    [Fact] public void ImportIsPreviewAndRequiresRebindingEvenForFullLocalConfig()
    {
        var source = Source; var preview = ConfigurationTransfer.Preview(ConfigCodec.Encode(source));
        Assert.Equal(3, preview.RemovedLocalEntries); Assert.Null(preview.Config.Hotkey);
        Assert.Equal(4, source.Shortcuts.Length); Assert.NotNull(source.Hotkey);
    }
    [Fact] public void FutureVersionImportFailsBeforeDraftMutation()
        => Assert.Throws<FutureConfigException>(() => ConfigurationTransfer.Preview(Encoding.UTF8.GetBytes("""{"schemaVersion":2}""")));
    [Fact] public void ExistingV1ConfigMigratesWithHotkeyOff()
    {
        var config = ConfigCodec.Decode(Encoding.UTF8.GetBytes("""{"schemaVersion":1,"appearance":{"theme":"dark"}}"""));
        Assert.Null(config.Hotkey); Assert.Equal(5, config.Modules.Length); Assert.Equal("dark", config.Appearance.Theme);
    }
    [Fact] public async Task ExportIsPortableAndImportReadIsBounded()
    {
        string dir = Path.Combine(Path.GetTempPath(), "PCC-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "portable.json"); await ConfigFiles.ExportAsync(path, Source);
            var config = ConfigCodec.Decode(await ConfigFiles.ReadAsync(path)); Assert.Single(config.Shortcuts);
            await File.WriteAllBytesAsync(path, new byte[ConfigCodec.MaxBytes + 1]);
            await Assert.ThrowsAsync<InvalidDataException>(() => ConfigFiles.ReadAsync(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact] public async Task BackupPreviewLeavesCurrentFileUntouchedUntilExplicitSave()
    {
        string dir = Path.Combine(Path.GetTempPath(), "PCC-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new JsonConfigRepository(dir);
            await repository.SaveAsync(new() { Modules = ["proxy"] });
            await repository.SaveAsync(new() { Modules = ["audio"] });
            byte[] before = await File.ReadAllBytesAsync(Path.Combine(dir, "config.json"));
            var backup = Assert.Single(await repository.ListBackupsAsync());
            var draft = await repository.ReadBackupAsync(backup.Id);
            Assert.Equal(new[] { "proxy" }, draft.Modules);
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(dir, "config.json")));
            await repository.SaveAsync(draft);
            Assert.Equal(new[] { "proxy" }, (await repository.LoadAsync()).Config.Modules);
            await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReadBackupAsync("../config.json"));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Theory] [InlineData(0u, 80u)] [InlineData(8u, 80u)] [InlineData(3u, 123u)]
    public void JsonHotkeyCannotBypassValidation(uint modifiers, uint key)
        => Assert.Throws<InvalidDataException>(() => ConfigCodec.Encode(new() { Hotkey = new(modifiers, key) }));
}
