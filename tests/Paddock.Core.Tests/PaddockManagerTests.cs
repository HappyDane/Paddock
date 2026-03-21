using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class PaddockManagerTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private PaddockManager CreateManager()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"paddock_test_{Guid.NewGuid():N}.json");
        _tempFiles.Add(tempPath);
        var settings = new SettingsService(tempPath);
        return new PaddockManager(settings);
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
    public void CreatePaddock_AddsToList()
    {
        var manager = CreateManager();

        var paddock = manager.CreatePaddock("Test", 100, 200, 300, 250);

        Assert.Single(manager.GetPaddocks());
        Assert.Equal("Test", paddock.Title);
        Assert.Equal(100, paddock.X);
        Assert.Equal(200, paddock.Y);
    }

    [Fact]
    public void CreatePaddock_InheritsDefaultOpacityAndCornerRadius()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"paddock_test_{Guid.NewGuid():N}.json");
        var settings = new SettingsService(tempPath);
        settings.Settings.DefaultOpacity = 0.75;
        settings.Settings.DefaultCornerRadius = 20;
        var manager = new PaddockManager(settings);

        var paddock = manager.CreatePaddock("Test", 0, 0, 300, 250);

        Assert.Equal(0.75, paddock.Style.Opacity);
        Assert.Equal(20, paddock.Style.CornerRadius);
    }

    [Fact]
    public void DeletePaddock_RemovesFromList()
    {
        var manager = CreateManager();
        var paddock = manager.CreatePaddock("Test", 0, 0, 300, 250);

        var result = manager.DeletePaddock(paddock.Id);

        Assert.True(result);
        Assert.Empty(manager.GetPaddocks());
    }

    [Fact]
    public void DeletePaddock_ReturnsFalseForUnknownId()
    {
        var manager = CreateManager();

        var result = manager.DeletePaddock("nonexistent");

        Assert.False(result);
    }

    [Fact]
    public void GetPaddock_ReturnsCorrectPaddock()
    {
        var manager = CreateManager();
        var p1 = manager.CreatePaddock("First", 0, 0, 300, 250);
        manager.CreatePaddock("Second", 400, 0, 300, 250);

        var found = manager.GetPaddock(p1.Id);

        Assert.NotNull(found);
        Assert.Equal("First", found.Title);
    }

    [Fact]
    public void AddIconToPaddock_AddsIcon()
    {
        var manager = CreateManager();
        var paddock = manager.CreatePaddock("Test", 0, 0, 300, 250);

        manager.AddIconToPaddock(paddock.Id, @"C:\Users\User\Desktop\test.lnk");

        Assert.Single(paddock.Icons);
        Assert.Equal(@"C:\Users\User\Desktop\test.lnk", paddock.Icons[0].DesktopPath);
    }

    [Fact]
    public void AddIconToPaddock_PreventsDuplicates()
    {
        var manager = CreateManager();
        var paddock = manager.CreatePaddock("Test", 0, 0, 300, 250);

        manager.AddIconToPaddock(paddock.Id, @"C:\Users\User\Desktop\test.lnk");
        manager.AddIconToPaddock(paddock.Id, @"C:\Users\User\Desktop\test.lnk");

        Assert.Single(paddock.Icons);
    }

    [Fact]
    public void RemoveIconFromPaddock_RemovesIcon()
    {
        var manager = CreateManager();
        var paddock = manager.CreatePaddock("Test", 0, 0, 300, 250);
        manager.AddIconToPaddock(paddock.Id, @"C:\Users\User\Desktop\test.lnk");

        manager.RemoveIconFromPaddock(paddock.Id, @"C:\Users\User\Desktop\test.lnk");

        Assert.Empty(paddock.Icons);
    }

    // --- Move between paddocks ---

    [Fact]
    public void MoveIconBetweenPaddocks_MovesIcon()
    {
        var manager = CreateManager();
        var source = manager.CreatePaddock("Source", 0, 0, 300, 250);
        var target = manager.CreatePaddock("Target", 400, 0, 300, 250);
        manager.AddIconToPaddock(source.Id, @"C:\test.lnk");

        manager.MoveIconBetweenPaddocks(source.Id, target.Id, @"C:\test.lnk");

        Assert.Empty(source.Icons);
        Assert.Single(target.Icons);
        Assert.Equal(@"C:\test.lnk", target.Icons[0].DesktopPath);
    }

    [Fact]
    public void MoveIconBetweenPaddocks_PreventsDuplicateInTarget()
    {
        var manager = CreateManager();
        var source = manager.CreatePaddock("Source", 0, 0, 300, 250);
        var target = manager.CreatePaddock("Target", 400, 0, 300, 250);
        manager.AddIconToPaddock(source.Id, @"C:\test.lnk");
        manager.AddIconToPaddock(target.Id, @"C:\test.lnk");

        manager.MoveIconBetweenPaddocks(source.Id, target.Id, @"C:\test.lnk");

        // Source still has it (move blocked), target still has one
        Assert.Single(source.Icons);
        Assert.Single(target.Icons);
    }

    // --- Sorting ---

    [Fact]
    public void SortPaddockIcons_ByName_Ascending()
    {
        var manager = CreateManager();
        var paddock = manager.CreatePaddock("Test", 0, 0, 300, 250);
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\Zebra.txt");
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\Apple.txt");
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\Mango.txt");

        manager.SortPaddockIcons(paddock.Id, SortField.Name, ascending: true);

        Assert.Equal(@"C:\Desktop\Apple.txt", paddock.Icons[0].DesktopPath);
        Assert.Equal(@"C:\Desktop\Mango.txt", paddock.Icons[1].DesktopPath);
        Assert.Equal(@"C:\Desktop\Zebra.txt", paddock.Icons[2].DesktopPath);
    }

    [Fact]
    public void SortPaddockIcons_ByName_Descending()
    {
        var manager = CreateManager();
        var paddock = manager.CreatePaddock("Test", 0, 0, 300, 250);
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\Apple.txt");
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\Zebra.txt");

        manager.SortPaddockIcons(paddock.Id, SortField.Name, ascending: false);

        Assert.Equal(@"C:\Desktop\Zebra.txt", paddock.Icons[0].DesktopPath);
        Assert.Equal(@"C:\Desktop\Apple.txt", paddock.Icons[1].DesktopPath);
    }

    [Fact]
    public void SortPaddockIcons_ByFileType()
    {
        var manager = CreateManager();
        var paddock = manager.CreatePaddock("Test", 0, 0, 300, 250);
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\doc.pdf");
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\app.exe");
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\note.txt");

        manager.SortPaddockIcons(paddock.Id, SortField.FileType, ascending: true);

        Assert.Equal(@"C:\Desktop\app.exe", paddock.Icons[0].DesktopPath);
        Assert.Equal(@"C:\Desktop\doc.pdf", paddock.Icons[1].DesktopPath);
        Assert.Equal(@"C:\Desktop\note.txt", paddock.Icons[2].DesktopPath);
    }

    [Fact]
    public void SortPaddockIcons_PersistsSortSettings()
    {
        var manager = CreateManager();
        var paddock = manager.CreatePaddock("Test", 0, 0, 300, 250);
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\b.txt");
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\a.txt");

        manager.SortPaddockIcons(paddock.Id, SortField.Name, ascending: true);

        Assert.Equal(SortField.Name, paddock.SortBy);
        Assert.True(paddock.SortAscending);
    }

    [Fact]
    public void SortPaddockIcons_ReassignsGridPositions()
    {
        var manager = CreateManager();
        var paddock = manager.CreatePaddock("Test", 0, 0, 300, 250);
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\c.txt");
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\a.txt");
        manager.AddIconToPaddock(paddock.Id, @"C:\Desktop\b.txt");

        manager.SortPaddockIcons(paddock.Id, SortField.Name, ascending: true);

        for (var i = 0; i < paddock.Icons.Count; i++)
        {
            Assert.Equal(i / 4, paddock.Icons[i].GridPosition.Row);
            Assert.Equal(i % 4, paddock.Icons[i].GridPosition.Col);
        }
    }
}
