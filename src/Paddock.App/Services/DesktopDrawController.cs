using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Paddock.App.Helpers;
using Paddock.App.Views;
using Paddock.Core.Models;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.Services;

/// <summary>
/// Turns a right-drag on the bare desktop into a new paddock: shows the
/// outline while it is drawn, then a small menu at the cursor offering to
/// create a paddock of exactly that size.
///
/// <see cref="DesktopGestureService"/> raises its events on its hook thread;
/// everything here runs on the UI thread.
/// </summary>
internal sealed class DesktopDrawController : IDisposable
{
    private readonly App _app;
    private readonly DesktopGestureService _gestures = new();
    private readonly object _gate = new();

    private DrawRectangleWindow? _outline;
    private ContextMenu? _menu;

    // Outline updates arrive with every mouse move; only the latest matters.
    private LayoutBounds _latest;
    private bool _updateQueued;

    internal DesktopDrawController(App app)
    {
        _app = app;

        _gestures.DrawingChanged += OnDrawingChanged;
        _gestures.DrawCompleted += bounds => _app.Dispatcher.BeginInvoke(() => OfferToCreate(ToDips(bounds)));
        _gestures.DrawCancelled += () => _app.Dispatcher.BeginInvoke(() =>
        {
            Log.Info("Desktop right-drag cancelled.");
            HideOutlineUnlessOffering();
        });
        _gestures.ButtonPressed += (x, y) => _app.Dispatcher.BeginInvoke(() => DismissIfOutside(x, y));
    }

    internal void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            _gestures.Start();
            return;
        }

        _gestures.Stop();
        CloseMenu();
        HideOutline();
    }

    public void Dispose()
    {
        _gestures.Dispose();
        CloseMenu();
        _outline?.Close();
        _outline = null;
    }

    private void OnDrawingChanged(LayoutBounds bounds)
    {
        lock (_gate)
        {
            _latest = bounds;
            if (_updateQueued)
                return;

            _updateQueued = true;
        }

        _app.Dispatcher.BeginInvoke(() =>
        {
            LayoutBounds latest;
            lock (_gate)
            {
                latest = _latest;
                _updateQueued = false;
            }

            // Starting a new rectangle withdraws any offer still open.
            CloseMenu();
            ShowOutline(ToDips(latest));
        });
    }

    private void OfferToCreate(Rect area)
    {
        ShowOutline(area);
        CloseMenu();

        var cursor = ScreenHelper.GetCursorPosition();
        Log.Info($"Desktop right-drag drew {area.X:0},{area.Y:0} {area.Width:0}x{area.Height:0}: " +
                 $"offering a paddock (menu at {cursor.X:0},{cursor.Y:0}).");

        var create = new MenuItem { Header = "Create paddock here", FontWeight = FontWeights.SemiBold };
        create.Click += (_, _) => _app.Host?.CreatePaddock(area, beginRename: true, collectIconsUnderneath: true);

        var menu = new ContextMenu
        {
            Placement = PlacementMode.AbsolutePoint,
            HorizontalOffset = cursor.X,
            VerticalOffset = cursor.Y,
            StaysOpen = false,
        };

        menu.Items.Add(create);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Cancel" });

        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menu, menu))
                _menu = null;

            Log.Info("Paddock offer closed.");
            HideOutline();
        };

        // No SetForegroundWindow here: when Paddock is already active the popup
        // holds mouse capture, and changing activation would make it lose
        // capture and close at once. A click elsewhere is caught by the mouse
        // hook instead (DismissIfOutside), active or not.
        _menu = menu;
        menu.IsOpen = true;
    }

    /// <summary>A click anywhere outside the open menu withdraws the offer.</summary>
    private void DismissIfOutside(int x, int y)
    {
        var menu = _menu;
        if (menu is null || !menu.IsOpen || !menu.IsLoaded)
            return;

        var scale = ScreenHelper.DipScale;
        var topLeft = menu.PointToScreen(new Point(0, 0));
        var bounds = new Rect(topLeft.X, topLeft.Y, menu.ActualWidth * scale, menu.ActualHeight * scale);

        if (!bounds.Contains(x, y))
        {
            Log.Info("Paddock offer withdrawn: the user clicked elsewhere.");
            CloseMenu();
        }
    }

    private void CloseMenu()
    {
        if (_menu is { } menu)
        {
            _menu = null;
            menu.IsOpen = false;
        }
    }

    private void ShowOutline(Rect area)
    {
        _outline ??= new DrawRectangleWindow();
        _outline.ShowAt(area);
    }

    private void HideOutline() => _outline?.Hide();

    private void HideOutlineUnlessOffering()
    {
        if (_menu is null)
            HideOutline();
    }

    /// <summary>Hook coordinates are physical pixels; windows are placed in DIPs.</summary>
    private static Rect ToDips(LayoutBounds pixels)
    {
        var scale = ScreenHelper.DipScale;
        return new Rect(pixels.Left / scale, pixels.Top / scale, pixels.Width / scale, pixels.Height / scale);
    }
}
