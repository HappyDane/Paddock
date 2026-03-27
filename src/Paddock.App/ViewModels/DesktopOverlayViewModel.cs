using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Paddock.App.Views;
using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.App.ViewModels;

public class DesktopOverlayViewModel : INotifyPropertyChanged
{
    private Canvas? _canvas;
    private readonly PaddockManager _paddockManager;
    private bool _isCreatingPaddock;

    public ObservableCollection<PaddockViewModel> Paddocks { get; } = new();

    public DesktopOverlayViewModel()
    {
        var app = (App)Application.Current;
        _paddockManager = app.PaddockManager;
    }

    public void Initialize(Canvas canvas)
    {
        _canvas = canvas;
        ClampPaddocksToScreen();
        LoadPaddocks();
    }

    /// <summary>
    /// Clamp all saved paddock positions to the current screen bounds.
    /// This handles resolution changes, monitor disconnects, etc.
    /// </summary>
    private void ClampPaddocksToScreen()
    {
        if (_canvas is null)
            return;

        var app = (App)Application.Current;
        var models = _paddockManager.GetPaddocks();

        if (models.Count == 0)
            return;

        app.LayoutEngine.ClampToScreen(models, _canvas.ActualWidth, _canvas.ActualHeight);
        _paddockManager.SaveLayout();
    }

    private void LoadPaddocks()
    {
        if (_canvas is null)
            return;

        var models = _paddockManager.GetPaddocks();
        foreach (var model in models)
        {
            AddPaddockToCanvas(model);
        }
    }

    public void BeginCreatePaddock(Point position)
    {
        BeginCreatePaddock(new Rect(position.X, position.Y, 300, 250));
    }

    public void BeginCreatePaddock(Rect area)
    {
        if (_isCreatingPaddock)
            return;

        _isCreatingPaddock = true;

        var model = _paddockManager.CreatePaddock(
            title: "New Paddock",
            x: area.X,
            y: area.Y,
            width: area.Width,
            height: area.Height
        );

        AddPaddockToCanvas(model);
        _isCreatingPaddock = false;
    }

    /// <summary>
    /// Toggle visibility of all paddock controls (QuickHide).
    /// Paddocks with ExcludeFromQuickHide remain visible.
    /// </summary>
    public void ToggleQuickHide()
    {
        if (_canvas is null)
            return;

        // Determine the target state: if any non-excluded paddock is visible, hide all; otherwise show all.
        bool anyVisible = false;
        foreach (var child in _canvas.Children)
        {
            if (child is PaddockControl control
                && control.DataContext is PaddockViewModel vm
                && !vm.ExcludeFromQuickHide
                && control.Visibility == Visibility.Visible)
            {
                anyVisible = true;
                break;
            }
        }

        var targetVisibility = anyVisible ? Visibility.Collapsed : Visibility.Visible;

        foreach (var child in _canvas.Children)
        {
            if (child is PaddockControl control
                && control.DataContext is PaddockViewModel vm
                && !vm.ExcludeFromQuickHide)
            {
                control.Visibility = targetVisibility;
            }
        }
    }

    private void AddPaddockToCanvas(PaddockModel model)
    {
        if (_canvas is null)
            return;

        var vm = new PaddockViewModel(model, _paddockManager);
        Paddocks.Add(vm);

        var control = new PaddockControl
        {
            DataContext = vm,
            Width = model.Width,
            Height = model.Height
        };

        Canvas.SetLeft(control, model.X);
        Canvas.SetTop(control, model.Y);
        _canvas.Children.Add(control);

        vm.RemoveRequested += () =>
        {
            _canvas.Children.Remove(control);
            Paddocks.Remove(vm);
            _paddockManager.DeletePaddock(model.Id);
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
