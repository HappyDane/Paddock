using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>
/// The safety nets behind "a paddock's icons are never lost from sight":
/// restarting after a crash, finding folders nothing shows, and putting
/// everything back without Paddock running.
/// </summary>
public class RestartPolicyTests
{
    private static readonly TimeSpan Minutes5 = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(unchecked((int)0xC0000005))] // access violation
    [InlineData(unchecked((int)0xE0434352))] // unhandled .NET exception
    [InlineData(unchecked((int)0x80131623))] // Environment.FailFast
    [InlineData(unchecked((int)0xC00000FD))] // stack overflow
    public void Crashes_AreRestarted(int exitCode)
    {
        var decision = RestartPolicy.Decide(exitCode, Minutes5, restartsSoFar: 0, sessionEnding: false);
        Assert.True(decision.Restart);
        Assert.Equal(1, decision.NextCount);
    }

    [Theory]
    [InlineData(0)]  // tray > Exit
    [InlineData(1)]  // Task Manager, taskkill
    [InlineData(-1)] // Process.Kill, stopping the debugger
    public void DeliberateExits_AreRespected(int exitCode)
        => Assert.False(RestartPolicy.Decide(exitCode, Minutes5, 0, false).Restart);

    [Fact]
    public void NoRestart_WhileWindowsSignsOut()
        => Assert.False(RestartPolicy.Decide(unchecked((int)0xC0000005), Minutes5, 0, sessionEnding: true).Restart);

    [Fact]
    public void ACrashRightAfterStarting_IsNotRestarted()
        => Assert.False(RestartPolicy.Decide(unchecked((int)0xC0000005), TimeSpan.FromSeconds(3), 0, false).Restart);

    [Fact]
    public void RepeatedQuickCrashes_StopBeingRestarted()
    {
        var crash = unchecked((int)0xE0434352);
        var count = 0;

        for (var i = 0; i < RestartPolicy.MaxQuickRestarts; i++)
        {
            var decision = RestartPolicy.Decide(crash, TimeSpan.FromMinutes(1), count, false);
            Assert.True(decision.Restart);
            count = decision.NextCount;
        }

        Assert.False(RestartPolicy.Decide(crash, TimeSpan.FromMinutes(1), count, false).Restart);
    }

    [Fact]
    public void ALongRunBetweenCrashes_StartsTheCountAgain()
    {
        var decision = RestartPolicy.Decide(unchecked((int)0xC0000005), TimeSpan.FromHours(2), RestartPolicy.MaxQuickRestarts, false);
        Assert.True(decision.Restart);
        Assert.Equal(1, decision.NextCount);
    }
}

public class HookWatchdogTests
{
    [Fact]
    public void PointerMovedButTheHookHeardNothing_MeansReinstall()
    {
        var watchdog = new HookWatchdog();
        Assert.False(watchdog.Check(10, 10)); // first look: nothing to compare with
        Assert.True(watchdog.Check(50, 60));
    }

    [Fact]
    public void AHealthyHook_IsLeftAlone()
    {
        var watchdog = new HookWatchdog();
        watchdog.Check(10, 10);
        watchdog.OnHookEvent();
        Assert.False(watchdog.Check(50, 60));
    }

    [Fact]
    public void AStillPointer_SaysNothingEitherWay()
    {
        var watchdog = new HookWatchdog();
        watchdog.Check(10, 10);
        Assert.False(watchdog.Check(10, 10));
    }
}

public class StoreRecoveryTests : IDisposable
{
    private readonly string _root;
    private readonly string _desktop;
    private readonly string _settingsPath;
    private readonly IconStore _store;
    private readonly PaddockManager _manager;

    public StoreRecoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paddock_recover_{Guid.NewGuid():N}");
        _desktop = Path.Combine(_root, "Desktop");
        Directory.CreateDirectory(_desktop);
        _settingsPath = Path.Combine(_root, "settings.json");

        _store = new IconStore(_desktop, Path.Combine(_desktop, ".Paddock"));
        _manager = new PaddockManager(new SettingsService(_settingsPath), _store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string StoreFile(string folder, string name)
    {
        var dir = Path.Combine(_store.StoreRoot, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void FoldersNoPaddockOwns_AreFound_AndEmptyOnesTidiedAway()
    {
        var kept = _manager.CreatePaddock("Kept", 0, 0, 300, 250);
        StoreFile(kept.Id, "mine.txt");
        StoreFile("lost0001", "orphan.txt");
        Directory.CreateDirectory(Path.Combine(_store.StoreRoot, "empty001"));

        Assert.Equal(["lost0001"], _manager.FindOrphanedFolders());
        Assert.False(Directory.Exists(Path.Combine(_store.StoreRoot, "empty001")));
    }

    [Fact]
    public void FoldersOwnedByAnotherLayout_AreNotOrphans()
    {
        var settings = new SettingsService(_settingsPath);
        settings.Settings.Corrals["work"] = new CorralModel { Name = "work", Paddocks = [new PaddockModel { Id = "work0001" }] };
        var manager = new PaddockManager(settings, _store);
        StoreFile("work0001", "report.pdf");

        Assert.Empty(manager.FindOrphanedFolders());
    }

    [Fact]
    public void AnAdoptedFolder_ShowsItsItemsAgain()
    {
        var orphan = StoreFile("lost0001", "orphan.txt");

        var paddock = _manager.AdoptFolder("lost0001", "Recovered", 10, 20, 300, 250);
        _manager.ReconcilePaddocks();

        Assert.Equal("lost0001", paddock.Id);
        var entry = Assert.Single(_manager.GetPaddock("lost0001")!.Icons);
        Assert.Equal(orphan, entry.DesktopPath);
        Assert.True(entry.Managed);
        Assert.Empty(_manager.FindOrphanedFolders());
    }

    [Fact]
    public void UnreadableSettings_LoseNoIcons()
    {
        var paddock = _manager.CreatePaddock("Docs", 0, 0, 300, 250);
        _manager.AddIconToPaddock(paddock.Id, Path.Combine(_desktop, "a.pdf").Also(p => File.WriteAllText(p, "x")));

        File.WriteAllText(_settingsPath, "{ this is not json");
        var fresh = new PaddockManager(new SettingsService(_settingsPath), _store);

        Assert.Equal([paddock.Id], fresh.FindOrphanedFolders());
    }

    [Fact]
    public void RestoreEverything_PutsBackItemsSettingsNeverKnew()
    {
        StoreFile("p1", "a.txt");
        StoreFile("p2", "b.txt");
        File.WriteAllText(Path.Combine(_store.StoreRoot, "loose.txt"), "x");
        _store.EnsureReadme();

        Assert.Equal(3, _store.RestoreEverything());

        Assert.True(File.Exists(Path.Combine(_desktop, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_desktop, "b.txt")));
        Assert.True(File.Exists(Path.Combine(_desktop, "loose.txt")));
        Assert.True(File.Exists(Path.Combine(_store.StoreRoot, IconStore.ReadmeName)), "The note should stay behind.");
        Assert.False(File.Exists(Path.Combine(_desktop, IconStore.ReadmeName)));
    }

    [Fact]
    public void TheReadme_IsNeverAPaddockItemOrAStray()
    {
        _store.EnsureReadme();
        StoreFile("p1", "a.txt");

        Assert.Empty(_store.ListStrayItems());
        Assert.Equal(["p1"], _store.ListPaddockFolders());
        Assert.Contains("--restore-all", File.ReadAllText(Path.Combine(_store.StoreRoot, IconStore.ReadmeName)));
    }
}

internal static class TestExtensions
{
    internal static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
