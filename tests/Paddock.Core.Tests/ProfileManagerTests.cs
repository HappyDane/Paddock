using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class ProfileManagerTests
{
    private string GetTempSettingsPath()
        => Path.Combine(Path.GetTempPath(), $"paddock_profile_test_{Guid.NewGuid():N}.json");

    private (SettingsService settings, ProfileManager manager) CreateManager()
    {
        var settings = new SettingsService(GetTempSettingsPath());
        var manager = new ProfileManager(settings);
        return (settings, manager);
    }

    private static PaddockModel MakePaddock(string title, double x = 0, double y = 0)
    {
        return new PaddockModel { Title = title, X = x, Y = y };
    }

    // ── SaveCorral ──────────────────────────────────────────────

    [Fact]
    public void SaveCorral_CreatesNewCorralFromActive()
    {
        var (settings, manager) = CreateManager();
        settings.GetActiveCorral().Paddocks.Add(MakePaddock("Widget", 10, 20));

        manager.SaveCorral("snapshot1");

        Assert.True(settings.Settings.Corrals.ContainsKey("snapshot1"));
        var saved = settings.Settings.Corrals["snapshot1"];
        Assert.Single(saved.Paddocks);
        Assert.Equal("Widget", saved.Paddocks[0].Title);
    }

    [Fact]
    public void SaveCorral_OverwritesExistingCorral()
    {
        var (settings, manager) = CreateManager();
        settings.GetActiveCorral().Paddocks.Add(MakePaddock("First"));
        manager.SaveCorral("snap");

        settings.GetActiveCorral().Paddocks.Clear();
        settings.GetActiveCorral().Paddocks.Add(MakePaddock("Second"));
        manager.SaveCorral("snap");

        var saved = settings.Settings.Corrals["snap"];
        Assert.Single(saved.Paddocks);
        Assert.Equal("Second", saved.Paddocks[0].Title);
    }

    [Fact]
    public void SaveCorral_ClonesNotReferences()
    {
        var (settings, manager) = CreateManager();
        settings.GetActiveCorral().Paddocks.Add(MakePaddock("Original"));
        manager.SaveCorral("snap");

        // Mutate the active corral after saving
        settings.GetActiveCorral().Paddocks[0].Title = "Mutated";

        var saved = settings.Settings.Corrals["snap"];
        Assert.Equal("Original", saved.Paddocks[0].Title);
    }

    // ── LoadCorral ──────────────────────────────────────────────

    [Fact]
    public void LoadCorral_SwitchesActiveAndReturnsPaddocks()
    {
        var (settings, manager) = CreateManager();
        settings.Settings.Corrals["work"] = new CorralModel
        {
            Name = "work",
            Paddocks = new List<PaddockModel> { MakePaddock("TaskBoard") }
        };

        var paddocks = manager.LoadCorral("work");

        Assert.Equal("work", settings.Settings.ActiveCorral);
        Assert.Single(paddocks);
        Assert.Equal("TaskBoard", paddocks[0].Title);
    }

    [Fact]
    public void LoadCorral_ThrowsForMissingCorral()
    {
        var (_, manager) = CreateManager();

        Assert.Throws<KeyNotFoundException>(() => manager.LoadCorral("nonexistent"));
    }

    // ── DeleteCorral ────────────────────────────────────────────

    [Fact]
    public void DeleteCorral_RemovesCorral()
    {
        var (settings, manager) = CreateManager();
        settings.Settings.Corrals["extra"] = new CorralModel { Name = "extra" };

        manager.DeleteCorral("extra");

        Assert.False(settings.Settings.Corrals.ContainsKey("extra"));
    }

    [Fact]
    public void DeleteCorral_ThrowsWhenDeletingActiveCorral()
    {
        var (_, manager) = CreateManager();

        Assert.Throws<InvalidOperationException>(() => manager.DeleteCorral("default"));
    }

    [Fact]
    public void DeleteCorral_ThrowsForMissingCorral()
    {
        var (_, manager) = CreateManager();

        Assert.Throws<KeyNotFoundException>(() => manager.DeleteCorral("ghost"));
    }

    // ── GetCorralNames ──────────────────────────────────────────

    [Fact]
    public void GetCorralNames_ReturnsAllNames()
    {
        var (settings, manager) = CreateManager();
        settings.Settings.Corrals["alpha"] = new CorralModel { Name = "alpha" };
        settings.Settings.Corrals["beta"] = new CorralModel { Name = "beta" };

        var names = manager.GetCorralNames();

        Assert.Contains("default", names);
        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
        Assert.Equal(3, names.Count);
    }

    // ── DuplicateCorral ─────────────────────────────────────────

    [Fact]
    public void DuplicateCorral_CreatesIndependentCopy()
    {
        var (settings, manager) = CreateManager();
        settings.GetActiveCorral().Paddocks.Add(MakePaddock("Panel", 5, 10));

        manager.DuplicateCorral("default", "copy");

        Assert.True(settings.Settings.Corrals.ContainsKey("copy"));
        var copy = settings.Settings.Corrals["copy"];
        Assert.Single(copy.Paddocks);
        Assert.Equal("Panel", copy.Paddocks[0].Title);

        // Verify independence
        copy.Paddocks[0].Title = "Changed";
        Assert.Equal("Panel", settings.GetActiveCorral().Paddocks[0].Title);
    }

    [Fact]
    public void DuplicateCorral_ThrowsIfSourceMissing()
    {
        var (_, manager) = CreateManager();

        Assert.Throws<KeyNotFoundException>(() => manager.DuplicateCorral("nope", "copy"));
    }

    [Fact]
    public void DuplicateCorral_ThrowsIfTargetExists()
    {
        var (settings, manager) = CreateManager();
        settings.Settings.Corrals["existing"] = new CorralModel { Name = "existing" };

        Assert.Throws<InvalidOperationException>(() => manager.DuplicateCorral("default", "existing"));
    }

    // ── Export / Import Round-trip ───────────────────────────────

    [Fact]
    public void ExportAndImport_RoundTrip()
    {
        var exportPath = Path.Combine(Path.GetTempPath(), $"paddock_export_{Guid.NewGuid():N}.paddock");

        try
        {
            var (settings1, manager1) = CreateManager();
            settings1.GetActiveCorral().Paddocks.Add(MakePaddock("Exported", 100, 200));
            manager1.ExportCorral("default", exportPath);

            // Import into a fresh manager
            var (settings2, manager2) = CreateManager();
            var importedName = manager2.ImportCorral(exportPath);

            Assert.Equal("default", importedName);
            var imported = settings2.Settings.Corrals[importedName];
            Assert.Single(imported.Paddocks);
            Assert.Equal("Exported", imported.Paddocks[0].Title);
            Assert.Equal(100, imported.Paddocks[0].X);
            Assert.Equal(200, imported.Paddocks[0].Y);
        }
        finally
        {
            if (File.Exists(exportPath))
                File.Delete(exportPath);
        }
    }

    [Fact]
    public void ImportCorral_RenamesOnConflict()
    {
        var exportPath = Path.Combine(Path.GetTempPath(), $"paddock_export_{Guid.NewGuid():N}.paddock");

        try
        {
            var (settings, manager) = CreateManager();
            settings.GetActiveCorral().Paddocks.Add(MakePaddock("Item"));
            manager.ExportCorral("default", exportPath);

            // Import again — "default" already exists so it should rename
            var importedName = manager.ImportCorral(exportPath);

            Assert.Equal("default_1", importedName);
            Assert.True(settings.Settings.Corrals.ContainsKey("default_1"));
        }
        finally
        {
            if (File.Exists(exportPath))
                File.Delete(exportPath);
        }
    }

    [Fact]
    public void ExportCorral_ThrowsForMissingCorral()
    {
        var (_, manager) = CreateManager();

        Assert.Throws<KeyNotFoundException>(() =>
            manager.ExportCorral("nonexistent", "/tmp/test.paddock"));
    }

    [Fact]
    public void ImportCorral_ThrowsForMissingFile()
    {
        var (_, manager) = CreateManager();

        Assert.Throws<FileNotFoundException>(() =>
            manager.ImportCorral("/tmp/does_not_exist.paddock"));
    }
}
