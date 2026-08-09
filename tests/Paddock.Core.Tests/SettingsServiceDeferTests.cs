using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>
/// Bulk work (collecting a whole desktop into a paddock) used to rewrite
/// settings.json once per item; these cover the batching that replaced it.
/// </summary>
public class SettingsServiceDeferTests : IDisposable
{
    private readonly string _path;

    public SettingsServiceDeferTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"paddock_defer_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
        try { File.Delete(_path + ".tmp"); } catch { }
        try { File.Delete(_path + ".corrupt"); } catch { }
    }

    [Fact]
    public void DeferSave_WritesNothingUntilTheScopeCloses()
    {
        var service = new SettingsService(_path);

        using (service.DeferSave())
        {
            service.Settings.GlobalTheme = "light";
            service.Save();
            service.Save();

            Assert.False(File.Exists(_path));
        }

        Assert.True(File.Exists(_path));
        Assert.Contains("light", File.ReadAllText(_path));
    }

    [Fact]
    public void DeferSave_WritesNothingWhenNobodyAskedToSave()
    {
        var service = new SettingsService(_path);

        using (service.DeferSave())
        {
        }

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void DeferSave_NestsAndFlushesOnlyOnTheOutermostScope()
    {
        var service = new SettingsService(_path);

        using (service.DeferSave())
        {
            using (service.DeferSave())
            {
                service.Save();
                Assert.False(File.Exists(_path));
            }

            Assert.False(File.Exists(_path));
        }

        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void Save_WritesImmediatelyOutsideAScope()
    {
        var service = new SettingsService(_path);

        service.Save();

        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void Load_KeepsAnUnreadableSettingsFileAside()
    {
        File.WriteAllText(_path, "{ this is not json");

        var service = new SettingsService(_path);

        // Falls back to defaults rather than throwing…
        Assert.Equal("dark", service.Settings.GlobalTheme);
        // …and the unreadable file is preserved instead of being overwritten.
        Assert.True(File.Exists(_path + ".corrupt"));
    }
}
