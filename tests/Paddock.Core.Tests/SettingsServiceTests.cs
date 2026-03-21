using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class SettingsServiceTests
{
    private string GetTempSettingsPath()
        => Path.Combine(Path.GetTempPath(), $"paddock_test_{Guid.NewGuid():N}.json");

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

        // Reload from same file
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
}
