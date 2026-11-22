using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>
/// The file side of auto-organize: rules only ever <em>move</em> desktop items,
/// and the store says when it puts something back, so that is never re-routed.
/// </summary>
public class AutoOrganizeStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _desktop;
    private readonly IconStore _store;
    private readonly PaddockManager _manager;

    public AutoOrganizeStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paddock_auto_{Guid.NewGuid():N}");
        _desktop = Path.Combine(_root, "Desktop");
        Directory.CreateDirectory(_desktop);

        _store = new IconStore(_desktop, Path.Combine(_desktop, ".Paddock"));
        _manager = new PaddockManager(new SettingsService(Path.Combine(_root, "settings.json")), _store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string DesktopFile(string name)
    {
        var path = Path.Combine(_desktop, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void MoveDesktopItems_MovesDesktopItemsIn()
    {
        var docs = _manager.CreatePaddock("Docs", 0, 0, 300, 250);
        var file = DesktopFile("a.pdf");

        var added = _manager.MoveDesktopItemsIntoPaddock(docs.Id, [file]);

        var entry = Assert.Single(added);
        Assert.True(entry.Managed);
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(entry.DesktopPath));
    }

    [Fact]
    public void MoveDesktopItems_NeverReferencesWhatItCannotMove()
    {
        var docs = _manager.CreatePaddock("Docs", 0, 0, 300, 250);
        var elsewhere = Path.Combine(_root, "Elsewhere");
        Directory.CreateDirectory(elsewhere);
        var outside = Path.Combine(elsewhere, "b.pdf");
        File.WriteAllText(outside, "x");

        var added = _manager.MoveDesktopItemsIntoPaddock(docs.Id, [outside, Path.Combine(_desktop, "missing.pdf")]);

        Assert.Empty(added);
        Assert.Empty(docs.Icons);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void PlacedOnDesktop_IsRaisedForEveryItemPutBack()
    {
        var docs = _manager.CreatePaddock("Docs", 0, 0, 300, 250);
        _manager.MoveDesktopItemsIntoPaddock(docs.Id, [DesktopFile("a.pdf"), DesktopFile("b.pdf")]);

        var placed = new List<string>();
        _store.PlacedOnDesktop += placed.Add;

        _manager.DeletePaddock(docs.Id);

        Assert.Equal(
            new[] { Path.Combine(_desktop, "a.pdf"), Path.Combine(_desktop, "b.pdf") }.Order(),
            placed.Order());
    }

    [Fact]
    public void NewPaddocks_TakeTheFrostedGlassDefault()
    {
        var settingsPath = Path.Combine(_root, "frosted.json");
        var settings = new SettingsService(settingsPath);
        settings.Settings.DefaultFrostedGlass = true;

        var paddock = new PaddockManager(settings).CreatePaddock("Glass", 0, 0, 300, 250);

        Assert.True(paddock.Style.FrostedGlass);
    }

    [Fact]
    public void FindFreeSpot_AvoidsExistingPaddocksAndStartsTopRight()
    {
        var engine = new LayoutEngine();
        var bounds = new LayoutBounds(0, 0, 1000, 800);

        var first = engine.FindFreeSpot([], bounds, 300, 250, gap: 10);
        Assert.Equal((690.0, 10.0), first);

        var taken = new PaddockModel { X = 690, Y = 10, Width = 300, Height = 250 };
        var second = engine.FindFreeSpot([taken], bounds, 300, 250, gap: 10)!.Value;

        var overlaps = second.X < taken.X + taken.Width + 10 && second.X + 300 > taken.X - 10
                       && second.Y < taken.Y + taken.Height + 10 && second.Y + 250 > taken.Y - 10;
        Assert.False(overlaps, $"{second} overlaps the existing paddock.");
        Assert.True(second.X + 300 <= 990 && second.Y + 250 <= 790);
    }

    [Fact]
    public void FindFreeSpot_SaysWhenThereIsNoRoom()
    {
        var full = new PaddockModel { X = 0, Y = 0, Width = 400, Height = 300 };
        Assert.Null(new LayoutEngine().FindFreeSpot([full], new LayoutBounds(0, 0, 400, 300), 300, 250));
    }
}
