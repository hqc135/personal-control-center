using System.IO.Compression;
using System.Security.Cryptography;
using ControlCenter.Core;
using ControlCenter.Windows;
using Xunit;
namespace ControlCenter.Tests;
public sealed class DesktopFeatureTests
{
    [Fact]
    public void PreferencesRejectInvalidGeometryAndUnknownCards()
    {
        Assert.Throws<InvalidDataException>(() => new FeaturePreferences { Geometry = new(double.NaN, 0, 400, 600) }.Validate());
        Assert.Throws<InvalidDataException>(() => new FeaturePreferences { Favorites = ["unknown"] }.Validate());
        Assert.Throws<InvalidDataException>(() => new SceneDefinition("empty").Validate());
        Assert.Throws<InvalidDataException>(() => new SceneDefinition("bad", AwakeMinutes: 481).Validate());
    }
    [Fact]
    public void PreferencesRoundTripPreservesScenesAndGeometry()
    {
        var root = Temp();
        try
        {
            var store = new FeaturePreferencesStore(root);
            var expected = new FeaturePreferences { Geometry = new(-1000, 50, 460, 720), Favorites = ["media", "audio"], Scenes = [new("工作", Volume: 25)] };
            store.Save(expected); var read = new FeaturePreferencesStore(root).Load();
            Assert.Equal(expected.Geometry, read.Geometry); Assert.Equal(expected.Favorites, read.Favorites); Assert.Equal(expected.Scenes, read.Scenes);
            store.Save(read with { Favorites = [] }); Assert.True(File.Exists(Path.Combine(root, "features.json.bak")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("../escaped.exe")]
    [InlineData("C:/escaped.exe")]
    [InlineData("sub/../../escaped.exe")]
    [InlineData("file.exe:payload")]
    public async Task VerifiedArchiveCannotEscapeVersionDirectory(string malicious)
    {
        var root = Temp();
        try
        {
            var zip = MakeZip(root, malicious); var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
            await Assert.ThrowsAsync<InvalidDataException>(() => new UpdatePackages(Path.Combine(root, "versions")).StageAsync(zip, hash, default));
            Assert.False(File.Exists(Path.Combine(root, "escaped.exe")));
            Assert.Empty(Directory.GetDirectories(Path.Combine(root, "versions")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task WrongHashCreatesNoInstalledVersion()
    {
        var root = Temp();
        try
        {
            var zip = MakeZip(root); var updates = new UpdatePackages(Path.Combine(root, "versions"));
            await Assert.ThrowsAsync<InvalidDataException>(() => updates.StageAsync(zip, new string('0', 64), default));
            Assert.Empty(updates.Versions());
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
            var staged = await updates.StageAsync(zip, hash, default);
            Assert.Equal(hash, File.ReadAllText(Path.Combine(staged, "VERIFIED-SHA256.txt")));
            Assert.Single(updates.Versions());
        }
        finally { Directory.Delete(root, true); }
    }
    private static string Temp() { var path = Path.Combine(Path.GetTempPath(), "PCC-FeatureTests-" + Guid.NewGuid()); Directory.CreateDirectory(path); return path; }
    private static string MakeZip(string root, string? extra = null)
    {
        string file = Path.Combine(root, "app.zip");
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        foreach (var name in new[] { "RELEASE.json", "PersonalControlCenter.exe", "coreclr.dll" }.Concat(extra is null ? [] : new[] { extra }))
        { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(name == "RELEASE.json" ? "{\"Version\":\"0.7.0\"}" : "fake payload, never executed"); }
        return file;
    }
}
