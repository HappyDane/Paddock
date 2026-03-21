using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string GetTempSettingsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"paddock_test_{Guid.NewGuid():N}.json");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch { }
            try { File.Delete(path + ".tmp"); } catch { }
        }
    }

    [Fact]
    public void New_SettingsService_HasDefaults()
    {
        var service = new SettingsService(GetTempSettingsPath());

        Assert.Equal(1, service.Settings.Version);
        Assert.Equal("dark", service.Settings.GlobalTheme);
        Assert.Equal("default", service.Settings.ActiveCorral);
        Assert.True(service.Settings.QuickHideEnabled);
    }

    [Fact]
    public void Save_And_Load_RoundTrips()
    {
        var path = GetTempSettingsPath();
        var service = new SettingsService(path);

        service.Settings.GlobalTheme = "light";
        service.Settings.QuickHideEnabled = false;
        service.Save();

        var reloaded = new SettingsService(path);

        Assert.Equal("light", reloaded.Settings.GlobalTheme);
        Assert.False(reloaded.Settings.QuickHideEnabled);
    }

    [Fact]
    public void GetActiveCorral_ReturnsDefaultCorral()
    {
        var service = new SettingsService(GetTempSettingsPath());

        var corral = service.GetActiveCorral();

        Assert.NotNull(corral);
        Assert.Empty(corral.Paddocks);
    }

    [Fact]
    public void Corrupted_File_Falls_Back_To_Defaults()
    {
        var path = GetTempSettingsPath();
        File.WriteAllText(path, "NOT VALID JSON {{{{");

        var service = new SettingsService(path);

        Assert.Equal(1, service.Settings.Version);
        Assert.Equal("dark", service.Settings.GlobalTheme);
    }

    [Fact]
    public void Enums_Serialize_As_Strings()
    {
        var path = GetTempSettingsPath();
        var service = new SettingsService(path);

        var paddock = new Paddock.Core.Models.PaddockModel
        {
            TitleVisibility = Paddock.Core.Models.TitleVisibility.Hidden,
            SortBy = Paddock.Core.Models.SortField.DateModified
        };
        service.GetActiveCorral().Paddocks.Add(paddock);
        service.Save();

        var json = File.ReadAllText(path);
        Assert.Contains("\"hidden\"", json);
        Assert.Contains("\"dateModified\"", json);
        Assert.DoesNotContain("\"titleVisibility\": 2", json);
    }
}
