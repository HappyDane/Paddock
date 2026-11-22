using System.Windows;
using Paddock.Core.Models;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.Services;

/// <summary>
/// Puts new desktop items into paddocks by the user's rules (Settings →
/// Auto-Organize). <see cref="DesktopArrivalWatcher"/> says what arrived and
/// what is known about it; <see cref="AutoSortRules"/> says where it goes; the
/// move itself happens here, on the UI thread, through <see cref="PaddockHost"/>.
///
/// Items Paddock puts back on the desktop — and items the user drags out of a
/// paddock — are never routed straight back in.
/// </summary>
internal sealed class AutoOrganizeController : IDisposable
{
    private readonly App _app;
    private DesktopArrivalWatcher? _watcher;

    internal AutoOrganizeController(App app)
    {
        _app = app;
        _app.IconStore.PlacedOnDesktop += OnPlacedOnDesktop;
    }

    private AppSettings Settings => _app.Settings.Settings;

    /// <summary>Call whenever the rules or the on/off switch change.</summary>
    internal void Refresh()
    {
        var wanted = Settings.AutoOrganizeEnabled && Settings.AutoSortRules.Any(r => r.Enabled);

        if (wanted && _watcher is null)
        {
            _watcher = _app.DesktopIconService.CreateArrivalWatcher();
            _watcher.ItemsArrived += OnItemsArrived;
            _watcher.Start();
            Log.Info($"Auto-organize: watching the desktop for new items ({Settings.AutoSortRules.Count(r => r.Enabled)} rule(s)).");
        }
        else if (!wanted && _watcher is not null)
        {
            _watcher.Dispose();
            _watcher = null;
            Log.Info("Auto-organize: no longer watching the desktop.");
        }

        _watcher?.SetSourceFolders(AutoSortRules.SourceFolders(Settings.AutoSortRules));
    }

    /// <summary>
    /// An icon is about to be dragged out of a paddock: whatever it drops on
    /// the desktop stays there. Dispose when the drag is over.
    /// </summary>
    internal IDisposable? ExpectItemsFromPaddock() => _watcher?.ExpectItemsFromPaddock();

    /// <summary>
    /// Which rule sends each item where. Items no rule wants are left out, and
    /// so are items a paddock already holds (one it references in place).
    /// </summary>
    internal List<(DesktopItemFacts Item, AutoSortRule Rule)> Plan(IEnumerable<DesktopItemFacts> items)
    {
        var paddocks = _app.PaddockManager.GetPaddocks();
        var ids = paddocks.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var held = paddocks.SelectMany(p => p.Icons).Select(i => i.DesktopPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plan = new List<(DesktopItemFacts, AutoSortRule)>();

        foreach (var item in items)
        {
            if (held.Contains(item.Path))
                continue;

            if (AutoSortRules.FindMatch(Settings.AutoSortRules, item, ids.Contains) is { } rule)
                plan.Add((item, rule));
        }

        return plan;
    }

    /// <summary>Carries out a plan. Returns how many items moved.</summary>
    internal int Apply(IReadOnlyCollection<(DesktopItemFacts Item, AutoSortRule Rule)> plan)
    {
        if (plan.Count == 0 || _app.Host is not { } host)
            return 0;

        var moved = 0;

        using (_app.PaddockManager.DeferSave())
        {
            foreach (var group in plan.GroupBy(p => p.Rule.TargetPaddockId))
            {
                var title = _app.PaddockManager.GetPaddock(group.Key)?.Title ?? group.Key;
                var count = host.MoveIntoPaddock(group.Key, group.Select(p => p.Item.Path).ToList());
                moved += count;

                foreach (var (item, rule) in group)
                    Log.Info($"Auto-organize: \"{System.IO.Path.GetFileName(item.Path)}\" → \"{title}\" ({AutoSortRules.Describe(rule)}).");

                if (count < group.Count())
                    Log.Info($"Auto-organize: {group.Count() - count} item(s) for \"{title}\" could not be moved and stay on the desktop.");
            }
        }

        return moved;
    }

    /// <summary>
    /// Applies the rules to what is on the desktop right now, after saying
    /// what would move where. ("Came from folder" rules cannot apply here:
    /// nobody saw those items arrive.)
    /// </summary>
    internal async Task OrganizeDesktopNowAsync(Window? owner)
    {
        var desktop = _app.DesktopIconService.GetDesktopItems().Where(_app.IconStore.IsOnDesktop).ToList();
        var facts = await Task.Run(() => desktop.Select(path => DesktopItemInspector.Inspect(path)).ToList());
        var plan = Plan(facts);

        if (plan.Count == 0)
        {
            Show(owner, "Nothing on the desktop matches a rule right now.", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var lines = plan
            .GroupBy(p => p.Rule.TargetPaddockId)
            .Select(g => $"  •  {g.Count()} into \"{_app.PaddockManager.GetPaddock(g.Key)?.Title}\"");

        var answer = Show(
            owner,
            $"Move {plan.Count} desktop item(s)?\n\n{string.Join("\n", lines)}\n\n" +
            "They leave the desktop for these paddocks, and come back if you drag them out or close the paddock.",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.OK)
            Apply(plan);
    }

    public void Dispose()
    {
        _app.IconStore.PlacedOnDesktop -= OnPlacedOnDesktop;
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnPlacedOnDesktop(string path) => _watcher?.IgnoreReturned(path);

    private void OnItemsArrived(IReadOnlyList<DesktopItemFacts> items)
        => _app.Dispatcher.BeginInvoke(() =>
        {
            if (_watcher is null || !Settings.AutoOrganizeEnabled)
                return;

            Apply(Plan(items));
        });

    private static MessageBoxResult Show(Window? owner, string text, MessageBoxButton buttons, MessageBoxImage image)
        => owner is null
            ? MessageBox.Show(text, "Paddock", buttons, image)
            : MessageBox.Show(owner, text, "Paddock", buttons, image);
}
