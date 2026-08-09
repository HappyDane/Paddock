using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class IconStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _desktop;
    private readonly string _store;
    private readonly IconStore _iconStore;

    public IconStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paddock_store_{Guid.NewGuid():N}");
        _desktop = Path.Combine(_root, "Desktop");
        _store = Path.Combine(_desktop, ".Paddock");
        Directory.CreateDirectory(_desktop);
        _iconStore = new IconStore(_desktop, _store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string MakeDesktopFile(string name, string content = "x")
    {
        var path = Path.Combine(_desktop, name);
        File.WriteAllText(path, content);
        return path;
    }

    // --- Moving items into a paddock ---

    [Fact]
    public void MoveIntoPaddock_MovesDesktopFileOffTheDesktop()
    {
        var file = MakeDesktopFile("notes.txt");

        var moved = _iconStore.MoveIntoPaddock(file, "p1");

        Assert.NotNull(moved);
        Assert.True(File.Exists(moved));
        Assert.False(File.Exists(file));
        Assert.Equal(Path.Combine(_store, "p1", "notes.txt"), moved);
    }

    [Fact]
    public void MoveIntoPaddock_MovesFolders()
    {
        var folder = Path.Combine(_desktop, "Project");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "inner.txt"), "x");

        var moved = _iconStore.MoveIntoPaddock(folder, "p1");

        Assert.NotNull(moved);
        Assert.True(Directory.Exists(moved));
        Assert.True(File.Exists(Path.Combine(moved, "inner.txt")));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void MoveIntoPaddock_NeverOverwritesAnExistingName()
    {
        var first = MakeDesktopFile("dup.txt", "first");
        _iconStore.MoveIntoPaddock(first, "p1");

        var second = MakeDesktopFile("dup.txt", "second");
        var moved = _iconStore.MoveIntoPaddock(second, "p1");

        Assert.Equal(Path.Combine(_store, "p1", "dup (2).txt"), moved);
        Assert.Equal("first", File.ReadAllText(Path.Combine(_store, "p1", "dup.txt")));
        Assert.Equal("second", File.ReadAllText(moved!));
    }

    [Fact]
    public void MoveIntoPaddock_LeavesFilesFromOutsideTheDesktopAlone()
    {
        var elsewhere = Path.Combine(_root, "Documents");
        Directory.CreateDirectory(elsewhere);
        var file = Path.Combine(elsewhere, "report.pdf");
        File.WriteAllText(file, "x");

        var moved = _iconStore.MoveIntoPaddock(file, "p1");

        Assert.Null(moved);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void MoveIntoPaddock_IsIdempotentForItemsAlreadyInThePaddock()
    {
        var file = MakeDesktopFile("notes.txt");
        var moved = _iconStore.MoveIntoPaddock(file, "p1")!;

        var again = _iconStore.MoveIntoPaddock(moved, "p1");

        Assert.Equal(moved, again);
        Assert.True(File.Exists(moved));
    }

    [Fact]
    public void MoveIntoPaddock_MovesBetweenPaddocks()
    {
        var file = MakeDesktopFile("notes.txt");
        var inFirst = _iconStore.MoveIntoPaddock(file, "p1")!;

        var inSecond = _iconStore.MoveIntoPaddock(inFirst, "p2");

        Assert.Equal(Path.Combine(_store, "p2", "notes.txt"), inSecond);
        Assert.False(File.Exists(inFirst));
        Assert.True(File.Exists(inSecond));
    }

    [Fact]
    public void MoveIntoPaddock_ReturnsNullForMissingSource()
    {
        var moved = _iconStore.MoveIntoPaddock(Path.Combine(_desktop, "ghost.txt"), "p1");

        Assert.Null(moved);
    }

    // --- Ejecting items ---

    [Fact]
    public void MoveToDesktop_PutsTheItemBackOnTheDesktop()
    {
        var file = MakeDesktopFile("notes.txt");
        var stored = _iconStore.MoveIntoPaddock(file, "p1")!;

        var ejected = _iconStore.MoveToDesktop(stored);

        Assert.Equal(file, ejected);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(stored));
    }

    [Fact]
    public void MoveToDesktop_AvoidsCollisionsOnTheDesktop()
    {
        var file = MakeDesktopFile("notes.txt", "stored");
        var stored = _iconStore.MoveIntoPaddock(file, "p1")!;
        MakeDesktopFile("notes.txt", "newcomer");

        var ejected = _iconStore.MoveToDesktop(stored);

        Assert.Equal(Path.Combine(_desktop, "notes (2).txt"), ejected);
        Assert.Equal("newcomer", File.ReadAllText(Path.Combine(_desktop, "notes.txt")));
        Assert.Equal("stored", File.ReadAllText(ejected!));
    }

    [Fact]
    public void MoveToDesktop_IsANoOpForItemsAlreadyOnTheDesktop()
    {
        var file = MakeDesktopFile("notes.txt");

        var result = _iconStore.MoveToDesktop(file);

        Assert.Equal(file, result);
        Assert.True(File.Exists(file));
    }

    // --- Listing and evacuating ---

    [Fact]
    public void ListPaddockItems_ReturnsStoredItemsAndIgnoresShellMetadata()
    {
        _iconStore.MoveIntoPaddock(MakeDesktopFile("a.txt"), "p1");
        _iconStore.MoveIntoPaddock(MakeDesktopFile("b.txt"), "p1");
        File.WriteAllText(Path.Combine(_store, "p1", "desktop.ini"), "[.ShellClassInfo]");

        var items = _iconStore.ListPaddockItems("p1");

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.EndsWith(".txt", i));
    }

    [Fact]
    public void ListPaddockItems_ReturnsEmptyForUnknownPaddock()
    {
        Assert.Empty(_iconStore.ListPaddockItems("nope"));
    }

    [Fact]
    public void EvacuatePaddock_ReturnsEverythingToTheDesktopAndRemovesTheFolder()
    {
        _iconStore.MoveIntoPaddock(MakeDesktopFile("a.txt"), "p1");
        _iconStore.MoveIntoPaddock(MakeDesktopFile("b.txt"), "p1");

        _iconStore.EvacuatePaddock("p1");

        Assert.True(File.Exists(Path.Combine(_desktop, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_desktop, "b.txt")));
        Assert.False(Directory.Exists(Path.Combine(_store, "p1")));
    }

    // --- Classification ---

    [Fact]
    public void IsOnDesktop_OnlyCountsDirectChildrenOfTheDesktop()
    {
        var file = MakeDesktopFile("notes.txt");
        var stored = _iconStore.MoveIntoPaddock(file, "p1")!;

        Assert.True(_iconStore.IsOnDesktop(Path.Combine(_desktop, "notes.txt")));
        Assert.False(_iconStore.IsOnDesktop(stored));
    }

    [Fact]
    public void IsInStore_DetectsItemsUnderThePaddockFolders()
    {
        var stored = _iconStore.MoveIntoPaddock(MakeDesktopFile("notes.txt"), "p1")!;

        Assert.True(_iconStore.IsInStore(stored));
        Assert.False(_iconStore.IsInStore(Path.Combine(_desktop, "other.txt")));
    }
}
