using System.Diagnostics;
using System.Text;
using ControlCenter.Core;
using ControlCenter.Windows.Launch;
using Xunit;
namespace ControlCenter.Tests;
internal sealed class FakeTrust : IShortcutTrustStore
{
    private readonly HashSet<string> values = [];
    public bool IsTrusted(ShortcutDefinition value) => values.Contains(ShortcutPolicy.Fingerprint(value));
    public Task ConfirmAsync(ShortcutDefinition value, CancellationToken ct) { values.Add(ShortcutPolicy.Fingerprint(value)); return Task.CompletedTask; }
}
internal sealed class FakePlatform : ILaunchPlatform
{
    public List<ProcessStartInfo> Starts = [];
    public bool Exists = true;
    public bool FileExists(string path) => Exists;
    public bool DirectoryExists(string path) => Exists;
    public string DownloadsPath() => @"C:\Test\Downloads";
    public void Start(ProcessStartInfo info) => Starts.Add(info);
}
public class ShortcutTests
{
    [Theory]
    [InlineData("url", "file:///C:/Windows/system32/cmd.exe")]
    [InlineData("url", "javascript:alert(1)")]
    [InlineData("url", "https://name:password@example.com")]
    [InlineData("folder", "../data")]
    [InlineData("application", "cmd /c whoami")]
    [InlineData("application", @"C:\test.cmd")]
    [InlineData("folder", @"\\server\share")]
    [InlineData("folder", @"C:\file:stream")]
    public void InvalidTargetsRejected(string kind, string target) => Assert.Throws<InvalidDataException>(() => ShortcutPolicy.Validate(new("a", "A", kind, target)));
    [Fact] public async Task ImportedClaimDoesNotGrantApplicationTrust()
    {
        string json = """{"shortcuts":[{"id":"app","label":"App","kind":"application","target":"C:\\Tools\\app.exe","confirmed":true}]}""";
        var config = ConfigCodec.Decode(Encoding.UTF8.GetBytes(json));
        var platform = new FakePlatform();
        var launcher = new ShortcutLauncher(new FakeTrust(), platform);
        var result = await launcher.LaunchAsync(config.Shortcuts[0], CancellationToken.None);
        Assert.Equal(FailureCode.Untrusted, result.Code); Assert.Empty(platform.Starts);
    }
    [Fact] public async Task ArgumentBoundariesArePreservedWithoutShellConcatenation()
    {
        var entry = new ShortcutDefinition("app", "App", "application", @"C:\Tools\app.exe", ["a b", "$(not executed)", "a\"b", "; &"], @"C:\Projects");
        var trust = new FakeTrust(); await trust.ConfirmAsync(entry, CancellationToken.None);
        var platform = new FakePlatform(); var launcher = new ShortcutLauncher(trust, platform);
        Assert.Equal(CommandOutcome.Confirmed, (await launcher.LaunchAsync(entry, CancellationToken.None)).Outcome);
        var start = Assert.Single(platform.Starts);
        Assert.False(start.UseShellExecute); Assert.Equal(entry.Target, start.FileName);
        Assert.Equal(entry.Arguments, start.ArgumentList); Assert.Equal("", start.Arguments);
    }
    [Fact] public async Task ChangingArgumentsInvalidatesPreviousTrust()
    {
        var entry = new ShortcutDefinition("app", "App", "application", @"C:\Tools\app.exe", ["safe"]);
        var trust = new FakeTrust(); await trust.ConfirmAsync(entry, CancellationToken.None);
        Assert.False(trust.IsTrusted(entry with { Arguments = ["changed"] }));
        Assert.False(trust.IsTrusted(entry with { WorkingDirectory = @"C:\Elsewhere" }));
    }
    [Fact] public async Task MissingDirectoryReturnsActionableError()
    {
        var platform = new FakePlatform { Exists = false };
        var result = await new ShortcutLauncher(new FakeTrust(), platform).LaunchAsync(new("folder", "Folder", "folder", @"C:\Missing"), CancellationToken.None);
        Assert.Equal(FailureCode.Unavailable, result.Code); Assert.Empty(platform.Starts);
    }
    [Fact] public async Task DownloadsUsesKnownFolderResolver()
    {
        var platform = new FakePlatform();
        await new ShortcutLauncher(new FakeTrust(), platform).LaunchAsync(new("downloads", "Downloads", "knownFolder", "Downloads"), CancellationToken.None);
        Assert.Equal(@"C:\Test\Downloads", Assert.Single(platform.Starts).FileName);
    }
    [Fact] public async Task SoundSettingsIsASeparateFixedAction()
    {
        var platform = new FakePlatform();
        await new ShortcutLauncher(new FakeTrust(), platform).OpenSoundSettingsAsync(CancellationToken.None);
        Assert.Equal("ms-settings:sound", Assert.Single(platform.Starts).FileName);
    }
    [Fact] public void TooManyOrDuplicateShortcutsAreRejected()
    {
        var entry = new ShortcutDefinition("a", "A", "url", "https://example.com");
        Assert.Throws<InvalidDataException>(() => ConfigCodec.Validate(new() { Shortcuts = [entry, entry] }));
        Assert.Throws<InvalidDataException>(() => ConfigCodec.Validate(new() { Shortcuts = Enumerable.Range(0, 21).Select(i => entry with { Id = i.ToString() }).ToArray() }));
    }
    [Fact] public async Task TrustPersistsSeparatelyAndRequiresExactFingerprint()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PCC.TrustTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new ShortcutDefinition("app", "App", "application", @"C:\Tools\app.exe", ["one"]);
            var first = new ShortcutTrustStore(directory); await first.LoadAsync();
            Assert.False(first.IsTrusted(entry)); await first.ConfirmAsync(entry, CancellationToken.None);
            var second = new ShortcutTrustStore(directory); await second.LoadAsync();
            Assert.True(second.IsTrusted(entry)); Assert.False(second.IsTrusted(entry with { Arguments = ["two"] }));
            Assert.False(File.Exists(Path.Combine(directory, "config.json")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

