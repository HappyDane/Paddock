using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Paddock.App.ViewModels;
using Paddock.Core.Models;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.Views;

public partial class PaddockControl : UserControl
{
    private PaddockViewModel ViewModel => (PaddockViewModel)DataContext;
    private System.ComponentModel.PropertyChangedEventHandler? _vmPropertyChangedHandler;

    // Drag state tracking
    private bool _isDragging;
    private Point _dragOriginPosition;

    // Minimum distance (pixels) before a mouse move counts as a drag
    private const double MinDragDistance = 5;

    // Icon drag state
    private Point _iconDragStart;
    private bool _iconDragStarted;

    public PaddockControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from old ViewModel
        if (e.OldValue is PaddockViewModel oldVm && _vmPropertyChangedHandler is not null)
        {
            oldVm.PropertyChanged -= _vmPropertyChangedHandler;
        }

        // Subscribe to new ViewModel
        if (e.NewValue is PaddockViewModel vm)
        {
            UpdateTitleVisibility(vm.TitleVisibility);
            _vmPropertyChangedHandler = (_, args) =>
            {
                if (args.PropertyName == nameof(PaddockViewModel.IsRenaming))
                    UpdateRenameState(vm.IsRenaming);
            };
            vm.PropertyChanged += _vmPropertyChangedHandler;
        }
    }

    // --- Title ---

    private void UpdateTitleVisibility(TitleVisibility mode)
    {
        TitleText.Visibility = mode switch
        {
            TitleVisibility.Always => Visibility.Visible,
            TitleVisibility.Hover => Visibility.Visible, // controlled by hover animation
            TitleVisibility.Hidden => Visibility.Collapsed,
            _ => Visibility.Visible
        };

        // For "Always" mode, keep title at full opacity even without hover
        if (mode == TitleVisibility.Always)
            TitleText.Opacity = 1.0;
    }

    private void UpdateRenameState(bool isRenaming)
    {
        if (isRenaming)
        {
            TitleText.Visibility = Visibility.Collapsed;
            RenameBox.Visibility = Visibility.Visible;
            RenameBox.SelectAll();
            RenameBox.Focus();
        }
        else
        {
            RenameBox.Visibility = Visibility.Collapsed;
            TitleText.Visibility = ViewModel.TitleVisibility == TitleVisibility.Hidden
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    // --- Title bar interactions ---

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

        _isDragging = false;
        _dragOriginPosition = e.GetPosition(Parent as Canvas ?? this);
        ViewModel.BeginDrag(e.GetPosition(this));
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsMouseCaptured || ViewModel.IsLocked)
            return;

        var canvas = Parent as Canvas;
        if (canvas is null)
            return;

        var mousePos = e.GetPosition(canvas);

        // Only start dragging after the mouse moves beyond the threshold
        if (!_isDragging)
        {
            var delta = mousePos - _dragOriginPosition;
            if (Math.Abs(delta.X) < MinDragDistance && Math.Abs(delta.Y) < MinDragDistance)
                return;
            _isDragging = true;
        }

        var newX = mousePos.X - ViewModel.DragStartOffset.X;
        var newY = mousePos.Y - ViewModel.DragStartOffset.Y;

        // Apply snap-to-edge if enabled
        var app = (App)Application.Current;
        if (app.Settings.Settings.SnapEnabled)
        {
            var snapped = ViewModel.SnapPosition(
                newX, newY,
                app.LayoutEngine,
                app.PaddockManager.GetPaddocks(),
                canvas.ActualWidth,
                canvas.ActualHeight,
                app.Settings.Settings.IntelligentSpacing);
            newX = snapped.X;
            newY = snapped.Y;
        }

        Canvas.SetLeft(this, newX);
        Canvas.SetTop(this, newY);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();

            // Only save position if the user actually dragged
            if (_isDragging)
            {
                var x = Canvas.GetLeft(this);
                var y = Canvas.GetTop(this);
                ViewModel.UpdatePosition(x, y);
            }

            _isDragging = false;
        }
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

    // --- Close ---

    private void CloseButton_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.Remove();
        e.Handled = true;
    }

    // --- Resize ---

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (ViewModel.IsLocked)
            return;

        var newWidth = ActualWidth + e.HorizontalChange;
        var newHeight = ActualHeight + e.VerticalChange;

        if (newWidth >= MinWidth)
            Width = newWidth;
        if (newHeight >= MinHeight)
            Height = newHeight;

        ViewModel.UpdateSize(Width, Height);
    }

    // --- Icon drag & drop ---

    private void IconArea_Drop(object sender, DragEventArgs e)
    {
        ViewModel.HandleIconDrop(e);
    }

    private void IconArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            _iconDragStart = e.GetPosition(element);
            _iconDragStarted = false;
        }
    }

    private void Icon_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: IconViewModel icon })
        {
            icon.Launch();
            e.Handled = true;
        }
    }

    private void Icon_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: IconViewModel icon })
        {
            var helper = new WindowInteropHelper(Window.GetWindow(this));
            var screenPoint = PointToScreen(e.GetPosition(this));
            ShellContextMenu.Show(icon.FullPath, helper.Handle, (int)screenPoint.X, (int)screenPoint.Y);
            e.Handled = true;
        }
    }

    private void Icon_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _iconDragStarted = false;
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not IconViewModel icon)
            return;

        // Require minimum mouse movement before starting a drag,
        // so that double-click to launch still works reliably.
        var currentPos = e.GetPosition(element);
        var delta = currentPos - _iconDragStart;
        if (Math.Abs(delta.X) < MinDragDistance && Math.Abs(delta.Y) < MinDragDistance)
            return;

        if (_iconDragStarted)
            return;

        _iconDragStarted = true;

        var dragData = new IconDragData
        {
            SourcePaddockId = ViewModel.Id,
            DesktopPath = icon.FullPath
        };
        DragDrop.DoDragDrop(element, dragData, DragDropEffects.Move);
    }

    // --- Context menu handlers ---

    private void ContextMenu_Rename(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginRenameTitle();
    }

    private void ContextMenu_Remove(object sender, RoutedEventArgs e)
    {
        ViewModel.Remove();
    }

    private void TitleVisibility_Always(object sender, RoutedEventArgs e)
    {
        ViewModel.TitleVisibility = TitleVisibility.Always;
        UpdateTitleVisibility(TitleVisibility.Always);
    }

    private void TitleVisibility_Hover(object sender, RoutedEventArgs e)
    {
        ViewModel.TitleVisibility = TitleVisibility.Hover;
        UpdateTitleVisibility(TitleVisibility.Hover);
    }

    private void TitleVisibility_Hidden(object sender, RoutedEventArgs e)
    {
        ViewModel.TitleVisibility = TitleVisibility.Hidden;
        UpdateTitleVisibility(TitleVisibility.Hidden);
    }

    private void Sort_Name(object sender, RoutedEventArgs e) => ViewModel.SortBy = SortField.Name;
    private void Sort_Date(object sender, RoutedEventArgs e) => ViewModel.SortBy = SortField.DateModified;
    private void Sort_Type(object sender, RoutedEventArgs e) => ViewModel.SortBy = SortField.FileType;
    private void Sort_Size(object sender, RoutedEventArgs e) => ViewModel.SortBy = SortField.FileSize;

    private void Sort_ToggleDirection(object sender, RoutedEventArgs e)
    {
        ViewModel.SortAscending = !ViewModel.SortAscending;
    }
}
