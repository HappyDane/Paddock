using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class PaddockManagerTests
{
    private PaddockManager CreateManager()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"paddock_test_{Guid.NewGuid():N}.json");
        var settings = new SettingsService(tempPath);
        return new PaddockManager(settings);
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
}
