using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Paddock.App.Helpers;
using Paddock.App.ViewModels;
using Paddock.App.Views;
using Paddock.Core.Models;

namespace Paddock.App.Services;

/// <summary>
/// Owns the live paddock windows: creates them from the saved layout, keeps them
/// pinned at desktop level, and keeps them in step with what is on disk.
/// </summary>
internal sealed class PaddockHost : IDisposable
{
    /// <summary>
    /// How often the desktop-level z-order is re-asserted. WM_WINDOWPOSCHANGING
    /// covers our own windows, but this also recovers if Explorer restarts (the
    /// desktop window we anchor to gets a new handle) or another app reshuffles
    /// the z-order behind our back.
    /// </summary>
    private static readonly TimeSpan ZOrderInterval = TimeSpan.FromSeconds(3);

    private readonly App _app;
    private readonly List<PaddockWindow> _windows = new();
    private readonly DispatcherTimer _zOrderTimer;

    private bool _paddocksHidden;
    private bool _disposed;

    internal PaddockHost(App app)
    {
        _app = app;

        _zOrderTimer = new DispatcherTimer { Interval = ZOrderInterval };
        _zOrderTimer.Tick += (_, _) => PinAllToDesktop();
    }

    internal int PaddockCount => _windows.Count;

    /// <summary>Restores the saved layout and starts watching the environment.</summary>
    internal void Start()
    {
        _app.PaddockManager.ReconcilePaddocks();
        ClampAllToMonitors();

        foreach (var model in _app.PaddockManager.GetPaddocks().ToList())
        {
            CreateWindow(model);
        }

        _zOrderTimer.Start();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _app.DesktopIconService.WatchStore(OnStoreChanged);
    }

    /// <summary>Asks the user to draw an area, then creates a paddock there.</summary>
    internal void CreatePaddockInteractive()
    {
        var area = DrawAreaWindow.PickArea();
        if (area is null)
            return;

        CreatePaddock(area.Value);
    }

    internal void CreatePaddock(Rect screenArea)
    {
        var model = _app.PaddockManager.CreatePaddock(
            title: "New Paddock",
            x: screenArea.X,
            y: screenArea.Y,
            width: screenArea.Width,
            height: screenArea.Height);

        _app.LayoutEngine.ClampToBounds(model, ScreenHelper.GetWorkAreaFor(model));
        _app.PaddockManager.SaveLayout();

        // A new paddock should be visible even if paddocks were hidden.
        if (_paddocksHidden)
            SetPaddocksVisible(true);

        var window = CreateWindow(model);
        window.PinToDesktop();
    }

    internal void RemovePaddock(PaddockWindow window)
    {
        _windows.Remove(window);
        var paddockId = window.ViewModel.Id;

        window.Close();

        // Deleting the paddock moves its items back onto the desktop.
        _app.PaddockManager.DeletePaddock(paddockId);
        _app.DesktopIconService.NotifyShellOfDesktopChange();
    }

    // --- Visibility ---

    internal void ToggleQuickHide() => SetPaddocksVisible(_paddocksHidden);

    internal void SetPaddocksVisible(bool visible)
    {
        _paddocksHidden = !visible;

        foreach (var window in _windows)
        {
            if (!visible && window.ViewModel.ExcludeFromQuickHide)
                continue;

            if (visible)
            {
                window.Show();
                window.PinToDesktop();
            }
            else
            {
                window.Hide();
            }
        }
    }

    // --- Contents ---

    /// <summary>
    /// Moves every item in every paddock back to the desktop. The escape hatch:
    /// after this, nothing of the user's is left inside Paddock's folders.
    /// </summary>
    internal int RestoreAllIconsToDesktop()
    {
        var manager = _app.PaddockManager;
        var restored = 0;

        foreach (var paddock in manager.GetPaddocks().ToList())
        {
            foreach (var icon in paddock.Icons.ToList())
            {
                if (manager.EjectIcon(paddock.Id, icon.DesktopPath) is not null)
                    restored++;
            }
        }

        RefreshAll();
        _app.DesktopIconService.NotifyShellOfDesktopChange();
        return restored;
    }

    internal void RefreshAll()
    {
        foreach (var window in _windows)
        {
            window.ViewModel.RefreshIcons();
        }
    }

    internal void RefreshPaddock(string paddockId)
        => FindWindow(paddockId)?.ViewModel.RefreshIcons();

    /// <summary>
    /// Re-syncs every paddock with its folder, redrawing only if something
    /// actually changed — the store watcher also fires for our own moves, and
    /// rebuilding the icon lists needlessly would reset scroll positions.
    /// </summary>
    internal void RefreshFromDisk()
    {
        if (_app.PaddockManager.ReconcilePaddocks())
            RefreshAll();
    }

    // --- Lookup ---

    internal PaddockWindow? FindWindow(string paddockId)
        => _windows.FirstOrDefault(w => w.ViewModel.Id == paddockId);

    /// <summary>The visible paddock under a screen point (DIPs), if any.</summary>
    internal PaddockWindow? FindWindowAt(Point screenPoint)
        => _windows.FirstOrDefault(w => w.IsVisible && w.ScreenBounds.Contains(screenPoint));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _zOrderTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        foreach (var window in _windows.ToList())
        {
            window.Close();
        }

        _windows.Clear();
    }

    private PaddockWindow CreateWindow(PaddockModel model)
    {
        var viewModel = new PaddockViewModel(model, _app.PaddockManager);
        var window = new PaddockWindow(viewModel, _app.ShellHookService);

        viewModel.RemoveRequested += () => RemovePaddock(window);

        _windows.Add(window);

        if (!_paddocksHidden || viewModel.ExcludeFromQuickHide)
            window.Show();

        return window;
    }

    private void PinAllToDesktop()
    {
        foreach (var window in _windows)
        {
            if (window.IsVisible)
                window.PinToDesktop();
        }
    }

    /// <summary>
    /// Keeps every paddock fully on a monitor — the layout is saved in screen
    /// coordinates, so a resolution change or an unplugged display could
    /// otherwise leave one parked off-screen.
    /// </summary>
    private void ClampAllToMonitors()
    {
        var models = _app.PaddockManager.GetPaddocks();
        if (models.Count == 0)
            return;

        var moved = false;

        foreach (var model in models)
        {
            var before = (model.X, model.Y, model.Width, model.Height);
            _app.LayoutEngine.ClampToBounds(model, ScreenHelper.GetWorkAreaFor(model));

            if (before != (model.X, model.Y, model.Width, model.Height))
                moved = true;
        }

        if (moved)
            _app.PaddockManager.SaveLayout();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _app.Dispatcher.InvokeAsync(() =>
        {
            ClampAllToMonitors();

            foreach (var window in _windows)
            {
                window.ApplyModelBounds();
            }

            PinAllToDesktop();
        });
    }

    private void OnStoreChanged()
    {
        // Raised on a watcher thread — hop to the UI before touching windows.
        _app.Dispatcher.InvokeAsync(RefreshFromDisk);
    }
}
