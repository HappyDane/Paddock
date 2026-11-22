using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Paddock.App.Helpers;
using Paddock.App.ViewModels;
using Paddock.Core.Models;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.Views;

/// <summary>
/// The visible paddock: title strip, icon grid, and all of the mouse handling
/// for moving, resizing and filling it. Geometry changes are applied to the
/// hosting <see cref="PaddockWindow"/>, since each paddock is its own window.
/// </summary>
public partial class PaddockControl : UserControl
{
    /// <summary>Minimum movement before a mouse-down turns into an icon drag.</summary>
    private const double MinDragDistance = 5;

    private System.ComponentModel.PropertyChangedEventHandler? _vmPropertyChangedHandler;

    // Window-drag state
    private bool _isDraggingWindow;
    private Point _dragGrabOffset;

    // Icon-drag state
    private Point _iconDragStart;
    private bool _iconDragStarted;

    /// <summary>A drag is over the paddock, so the outline must stay up whatever a pulse does.</summary>
    private bool _dragOver;

    private Action? _arrivalHandler;

    public PaddockControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private PaddockViewModel ViewModel => (PaddockViewModel)DataContext;

    private PaddockWindow? HostWindow => Window.GetWindow(this) as PaddockWindow;

    private static App CurrentApp => (App)Application.Current;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PaddockViewModel oldVm && _vmPropertyChangedHandler is not null)
            oldVm.PropertyChanged -= _vmPropertyChangedHandler;

        if (e.OldValue is PaddockViewModel previous && _arrivalHandler is not null)
            previous.ArrivalHighlightRequested -= _arrivalHandler;

        if (e.NewValue is PaddockViewModel vm)
        {
            _arrivalHandler = () => Dispatcher.BeginInvoke(PulseArrival);
            vm.ArrivalHighlightRequested += _arrivalHandler;

            UpdateTitleVisibility(vm.TitleVisibility);
            _vmPropertyChangedHandler = (_, args) =>
            {
                if (args.PropertyName == nameof(PaddockViewModel.IsRenaming))
                    UpdateRenameState(vm.IsRenaming);
                else if (args.PropertyName == nameof(PaddockViewModel.TitleVisibility))
                    UpdateTitleVisibility(vm.TitleVisibility);
            };
            vm.PropertyChanged += _vmPropertyChangedHandler;
        }
    }

    // --- Title ---

    private void UpdateTitleVisibility(TitleVisibility mode)
    {
        TitleText.Visibility = mode == TitleVisibility.Hidden
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (mode == TitleVisibility.Always)
            TitleText.Opacity = 1.0;
    }

    private void UpdateRenameState(bool isRenaming)
    {
        if (isRenaming)
        {
            TitleText.Visibility = Visibility.Collapsed;
            RenameBox.Visibility = Visibility.Visible;

            // Deferred: when rename is started from the context menu, the menu is
            // still closing and would take focus straight back off the text box.
            Dispatcher.InvokeAsync(
                () =>
                {
                    // The window has to be active for the box to take keystrokes.
                    // If Windows will not hand Paddock the foreground, leave the
                    // title as it is rather than show a box nobody can type in.
                    if (HostWindow?.Activate() == false)
                    {
                        ViewModel.CancelRename();
                        return;
                    }

                    RenameBox.Focus();
                    Keyboard.Focus(RenameBox);
                    RenameBox.SelectAll();
                },
                DispatcherPriority.Input);
        }
        else
        {
            RenameBox.Visibility = Visibility.Collapsed;
            UpdateTitleVisibility(ViewModel.TitleVisibility);
        }
    }

    // --- Moving the paddock ---

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ViewModel.ToggleRollUp();
            e.Handled = true;
            return;
        }

        if (ViewModel.IsLocked || ViewModel.IsRenaming)
            return;

        var window = HostWindow;
        if (window is null)
            return;

        var cursor = ScreenHelper.GetCursorPosition();
        _dragGrabOffset = new Point(cursor.X - window.Left, cursor.Y - window.Top);
        _isDraggingWindow = true;
        TitleArea.CaptureMouse();
        e.Handled = true;
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingWindow)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndWindowDrag();
            return;
        }

        var window = HostWindow;
        if (window is null)
            return;

        var cursor = ScreenHelper.GetCursorPosition();
        var targetX = cursor.X - _dragGrabOffset.X;
        var targetY = cursor.Y - _dragGrabOffset.Y;

        var settings = CurrentApp.Settings.Settings;
        if (settings.SnapEnabled)
        {
            // Snap against the monitor the paddock is being dragged onto.
            var bounds = ScreenHelper.GetWorkAreaFor(targetX, targetY, ViewModel.Width, ViewModel.DisplayHeight);
            var snapped = ViewModel.SnapPosition(
                targetX,
                targetY,
                CurrentApp.LayoutEngine,
                CurrentApp.PaddockManager.GetPaddocks(),
                bounds,
                settings.IntelligentSpacing);

            targetX = snapped.X;
            targetY = snapped.Y;
        }

        window.Left = targetX;
        window.Top = targetY;
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingWindow)
            EndWindowDrag();
    }

    private void EndWindowDrag()
    {
        _isDraggingWindow = false;
        TitleArea.ReleaseMouseCapture();

        var window = HostWindow;
        if (window is not null)
            ViewModel.UpdatePosition(window.Left, window.Top);
    }

    // --- Resizing ---

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (ViewModel.IsLocked)
            return;

        var window = HostWindow;
        if (window is null)
            return;

        var bounds = ScreenHelper.GetWorkAreaFor(window.Left, window.Top, window.Width, window.Height);

        var maxWidth = Math.Max(window.MinWidth, bounds.Right - window.Left);
        var maxHeight = Math.Max(window.MinHeight, bounds.Bottom - window.Top);

        window.Width = Math.Clamp(window.Width + e.HorizontalChange, window.MinWidth, maxWidth);
        window.Height = Math.Clamp(window.Height + e.VerticalChange, window.MinHeight, maxHeight);
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (ViewModel.IsLocked)
            return;

        var window = HostWindow;
        if (window is not null)
            ViewModel.UpdateSize(window.Width, window.Height);
    }

    // --- Rename ---

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.CancelRename();
            e.Handled = true;
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsRenaming)
            ViewModel.CommitRename();
    }

    // --- Dropping items in ---

    private void Paddock_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanAccept(e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Paddock_DragEnter(object sender, DragEventArgs e)
    {
        if (!CanAccept(e))
            return;

        _dragOver = true;
        DropHighlight.BeginAnimation(OpacityProperty, null);
        DropHighlight.Visibility = Visibility.Visible;
    }

    private void Paddock_DragLeave(object sender, DragEventArgs e)
    {
        _dragOver = false;
        DropHighlight.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Items arrived by themselves (an auto-organize rule): outline the
    /// paddock for a moment and let it fade, so the user sees where they went.
    /// </summary>
    private void PulseArrival()
    {
        if (_dragOver)
            return;

        var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(1200)))
        {
            BeginTime = TimeSpan.FromMilliseconds(500),
        };

        fade.Completed += (_, _) =>
        {
            if (_dragOver)
                return;

            DropHighlight.BeginAnimation(OpacityProperty, null);
            DropHighlight.Visibility = Visibility.Collapsed;
        };

        DropHighlight.Visibility = Visibility.Visible;
        DropHighlight.BeginAnimation(OpacityProperty, fade);
    }

    private static bool CanAccept(DragEventArgs e)
        => e.Data.GetDataPresent(typeof(IconDragData))
           || e.Data.GetDataPresent(DataFormats.FileDrop);

    private void Paddock_Drop(object sender, DragEventArgs e)
    {
        _dragOver = false;
        DropHighlight.Visibility = Visibility.Collapsed;

        // Read the drop now, act on it once the drag has finished. The drag
        // source (Explorer, for a desktop icon) is blocked until this handler
        // returns, and moving files — perhaps after a permission prompt — is
        // better done once it has been let go.
        var request = ViewModel.ReadDrop(e.Data);

        // We move the files ourselves, so tell the drag source there is nothing
        // left for it to do. Reporting Move here would invite Explorer to
        // delete a source path we are about to empty.
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (request is null)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            var sourcePaddockId = ViewModel.ApplyDrop(request);

            if (sourcePaddockId is not null)
                CurrentApp.Host?.RefreshPaddock(sourcePaddockId);

            CurrentApp.DesktopIconService.NotifyShellOfDesktopChange();
        });
    }

    // --- Icons ---

    private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        // StackPanel has no MouseDoubleClick event, so detect it via ClickCount.
        if (e.ClickCount == 2 && element.DataContext is IconViewModel icon)
        {
            icon.Launch();
            _iconDragStarted = false;
            e.Handled = true;
            return;
        }

        _iconDragStart = e.GetPosition(element);
        _iconDragStarted = false;
    }

    private void Icon_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _iconDragStarted = false;
            return;
        }

        if (_iconDragStarted)
            return;

        if (sender is not FrameworkElement element || element.DataContext is not IconViewModel icon)
            return;

        // Require a little movement first, so double-click-to-launch still works.
        var delta = e.GetPosition(element) - _iconDragStart;
        if (Math.Abs(delta.X) < MinDragDistance && Math.Abs(delta.Y) < MinDragDistance)
            return;

        _iconDragStarted = true;

        var data = new DataObject();
        data.SetData(typeof(IconDragData), new IconDragData
        {
            SourcePaddockId = ViewModel.Id,
            DesktopPath = icon.FullPath
        });

        // Also offer the plain file, so the item can be dropped onto the
        // desktop, into a folder, or into any application that accepts files.
        data.SetData(DataFormats.FileDrop, new[] { icon.FullPath });

        // Only items Paddock moved in may be moved out again. A referenced item
        // lives in the user's own folder somewhere; dragging it out must never
        // relocate that file, so drop targets are only offered a copy or link.
        var allowed = icon.IsManaged
            ? DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link
            : DragDropEffects.Copy | DragDropEffects.Link;

        var result = DragDrop.DoDragDrop(element, data, allowed);

        _iconDragStarted = false;
        HandleIconDragFinished(result);
    }

    /// <summary>
    /// After an icon drag ends, whoever accepted the drop has done the work:
    /// another paddock moves the item itself, and Explorer — the desktop, or a
    /// folder window — moves or copies the file. Paddock must not move it as
    /// well: Explorer finishes its move in the background, so a second move
    /// here would race it for the same file (and an icon dropped into, say, an
    /// e-mail would wrongly be thrown out of the paddock). The store watcher
    /// sees the file leave and updates the paddock; refreshing now just makes
    /// that immediate when the move was quick.
    /// </summary>
    private static void HandleIconDragFinished(DragDropEffects result)
    {
        if (result == DragDropEffects.None)
            return;

        CurrentApp.Host?.RefreshFromDisk();
        CurrentApp.DesktopIconService.NotifyShellOfDesktopChange();
    }

    private void Icon_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: IconViewModel icon } element)
            return;

        // Handled here so the paddock's own context menu does not also open.
        e.Handled = true;

        var window = Window.GetWindow(this);
        var handle = window is null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
        var screenPoint = element.PointToScreen(e.GetPosition(element));

        ShellContextMenu.Show(icon.FullPath, handle, (int)screenPoint.X, (int)screenPoint.Y);

        // The menu may have deleted or renamed the item.
        CurrentApp.Host?.RefreshFromDisk();
    }

    // --- Context menu ---

    private void ContextMenu_Rename(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginRenameTitle();
    }

    /// <summary>
    /// Opens the Appearance popover once the menu has closed; opening it while
    /// the menu still holds the mouse would close it again straight away.
    /// </summary>
    private void ContextMenu_Appearance(object sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => AppearancePopup.IsOpen = true);

    private void ContextMenu_AutoOrganize(object sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => AutoOrganizeWindow.ShowFor(ViewModel.Id));

    private void ContextMenu_Remove(object sender, RoutedEventArgs e)
    {
        ViewModel.Remove();
    }

    private void ContextMenu_ToggleRollUp(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleRollUp();
    }

    /// <summary>
    /// Moves a whole category of desktop items into this paddock. The category
    /// comes from the menu item's Tag, and the count is always confirmed first —
    /// this moves real files.
    /// </summary>
    private void ContextMenu_FillFromDesktop(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag }
            || !Enum.TryParse<DesktopItemCategory>(tag, out var category))
        {
            return;
        }

        var items = DesktopItemFilter.Filter(CurrentApp.DesktopIconService.GetDesktopItems(), category);
        var description = DesktopItemFilter.Describe(category);

        if (items.Count == 0)
        {
            MessageBox.Show(
                $"There are no {description} on the desktop to collect.",
                "Paddock",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            $"Move {items.Count} {description} into \"{ViewModel.Title}\"?\n\n" +
            "They will be moved off the desktop into this paddock's folder, and " +
            "go back to the desktop if you drag them out or remove the paddock.",
            "Paddock",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK)
            return;

        ViewModel.AddIcons(items);
        CurrentApp.DesktopIconService.NotifyShellOfDesktopChange();
    }

    private void Sort_Name(object sender, RoutedEventArgs e) => ViewModel.SortBy = SortField.Name;

    private void Sort_Date(object sender, RoutedEventArgs e) => ViewModel.SortBy = SortField.DateModified;

    private void Sort_Type(object sender, RoutedEventArgs e) => ViewModel.SortBy = SortField.FileType;

    private void Sort_Size(object sender, RoutedEventArgs e) => ViewModel.SortBy = SortField.FileSize;

    private void Sort_ToggleDirection(object sender, RoutedEventArgs e)
        => ViewModel.SortAscending = !ViewModel.SortAscending;
}
