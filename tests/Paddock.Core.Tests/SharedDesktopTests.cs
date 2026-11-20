using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>
/// Items on the desktop shared by all accounts (C:\Users\Public\Desktop) —
/// where most installers put their shortcuts. They are copied into a paddock
/// and the original removed through <see cref="ISharedDesktopRemover"/>, with a
/// fallback to referencing them when removal is refused.
/// </summary>
public class SharedDesktopTests : IDisposable
{
    private readonly string _root;
    private readonly string _desktop;
    private readonly string _shared;
    private readonly string _store;
    private readonly IconStore _iconStore;
    private readonly FakeRemover _remover = new();
    private readonly PaddockManager _manager;

    public SharedDesktopTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paddock_shared_{Guid.NewGuid():N}");
        _desktop = Path.Combine(_root, "Desktop");
        _shared = Path.Combine(_root, "Public", "Desktop");
        _store = Path.Combine(_desktop, ".Paddock");
        Directory.CreateDirectory(_desktop);
        Directory.CreateDirectory(_shared);

        _iconStore = new IconStore(_desktop, _store, _shared);
        _manager = new PaddockManager(new SettingsService(Path.Combine(_root, "settings.json")), _iconStore, _remover);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string MakeFile(string folder, string name, string content = "x")
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void IsOnSharedDesktop_OnlyForFilesDirectlyOnTheSharedDesktop()
    {
        var shortcut = MakeFile(_shared, "Browser.lnk");
        var folder = Directory.CreateDirectory(Path.Combine(_shared, "Tools")).FullName;
        var nested = MakeFile(folder, "inner.lnk");
        var own = MakeFile(_desktop, "Mine.lnk");

        Assert.True(_iconStore.IsOnSharedDesktop(shortcut));
        Assert.False(_iconStore.IsOnSharedDesktop(folder));
        Assert.False(_iconStore.IsOnSharedDesktop(nested));
        Assert.False(_iconStore.IsOnSharedDesktop(own));
    }

    [Fact]
    public void IsOnSharedDesktop_FalseWhenNoSharedDesktopIsKnown()
    {
        var store = new IconStore(_desktop, _store);
        Assert.False(store.IsOnSharedDesktop(MakeFile(_shared, "Browser.lnk")));
    }

    [Fact]
    public void AddIcons_MovesSharedItemsWhenTheOriginalCanBeRemoved()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var shortcut = MakeFile(_shared, "Browser.lnk", "target");

        var entries = _manager.AddIconsToPaddock(paddock.Id, [shortcut]);

        var entry = Assert.Single(entries);
        Assert.True(entry.Managed);
        Assert.Equal(Path.Combine(_store, paddock.Id, "Browser.lnk"), entry.DesktopPath);
        Assert.Equal("target", File.ReadAllText(entry.DesktopPath));
        Assert.False(File.Exists(shortcut));
    }

    [Fact]
    public void AddIcons_AsksToRemoveAllSharedItemsOfADropAtOnce()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var a = MakeFile(_shared, "A.lnk");
        var b = MakeFile(_shared, "B.lnk");
        var own = MakeFile(_desktop, "C.lnk");

        var entries = _manager.AddIconsToPaddock(paddock.Id, [a, own, b]);

        Assert.Equal(3, entries.Count);
        Assert.Equal(1, _remover.Calls);
        Assert.Equal([a, b], _remover.Requested);
        Assert.All(entries, e => Assert.True(e.Managed));
    }

    [Fact]
    public void AddIcons_FallsBackToReferencingWhenRemovalIsRefused()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var shortcut = MakeFile(_shared, "Browser.lnk");
        _remover.Refuse = true;

        var entries = _manager.AddIconsToPaddock(paddock.Id, [shortcut]);

        var entry = Assert.Single(entries);
        Assert.False(entry.Managed);
        Assert.Equal(shortcut, entry.DesktopPath);
        Assert.True(File.Exists(shortcut));

        // No stray copy left behind in the paddock's folder.
        Assert.Empty(_iconStore.ListPaddockItems(paddock.Id));
    }

    [Fact]
    public void AddIcons_FallsBackToReferencingWhenTheRemoverThrows()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var shortcut = MakeFile(_shared, "Browser.lnk");
        _remover.Throw = true;

        var entry = Assert.Single(_manager.AddIconsToPaddock(paddock.Id, [shortcut]));

        Assert.False(entry.Managed);
        Assert.True(File.Exists(shortcut));
        Assert.Empty(_iconStore.ListPaddockItems(paddock.Id));
    }

    [Fact]
    public void AddIcons_WithoutARemoverReferencesSharedItems()
    {
        var manager = new PaddockManager(new SettingsService(Path.Combine(_root, "other.json")), _iconStore);
        var paddock = manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var shortcut = MakeFile(_shared, "Browser.lnk");

        var entry = Assert.Single(manager.AddIconsToPaddock(paddock.Id, [shortcut]));

        Assert.False(entry.Managed);
        Assert.True(File.Exists(shortcut));
        Assert.Empty(_iconStore.ListPaddockItems(paddock.Id));
    }

    [Fact]
    public void AddIcons_NeverOverwritesAnItemAlreadyInThePaddock()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        _manager.AddIconsToPaddock(paddock.Id, [MakeFile(_desktop, "Browser.lnk", "mine")]);
        var shared = MakeFile(_shared, "Browser.lnk", "shared");

        _manager.AddIconsToPaddock(paddock.Id, [shared]);

        var folder = Path.Combine(_store, paddock.Id);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(folder, "Browser.lnk")));
        Assert.Equal("shared", File.ReadAllText(Path.Combine(folder, "Browser (2).lnk")));
        Assert.Equal(2, _manager.GetPaddock(paddock.Id)!.Icons.Count);
    }

    [Fact]
    public void EjectingAMovedSharedItemPutsItOnTheUsersOwnDesktop()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var entry = Assert.Single(_manager.AddIconsToPaddock(paddock.Id, [MakeFile(_shared, "Browser.lnk")]));

        var ejected = _manager.EjectIcon(paddock.Id, entry.DesktopPath);

        Assert.Equal(Path.Combine(_desktop, "Browser.lnk"), ejected);
        Assert.True(File.Exists(ejected));
    }

    [Fact]
    public void DiscardCopy_RefusesPathsOutsideTheStore()
    {
        var own = MakeFile(_desktop, "keep.txt");
        var shared = MakeFile(_shared, "keep.lnk");

        _iconStore.DiscardCopy(own);
        _iconStore.DiscardCopy(shared);

        Assert.True(File.Exists(own));
        Assert.True(File.Exists(shared));
    }

    private sealed class FakeRemover : ISharedDesktopRemover
    {
        public bool Refuse { get; set; }
        public bool Throw { get; set; }
        public int Calls { get; private set; }
        public List<string> Requested { get; } = new();

        public IReadOnlyCollection<string> Remove(IReadOnlyList<string> paths)
        {
            Calls++;
            Requested.AddRange(paths);

            if (Throw)
                throw new InvalidOperationException("boom");

            if (Refuse)
                return [];

            foreach (var path in paths)
                File.Delete(path);

            return paths.ToList();
        }
    }
}
