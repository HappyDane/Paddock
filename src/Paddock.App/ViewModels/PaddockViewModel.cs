using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.App.ViewModels;

public class PaddockViewModel : INotifyPropertyChanged
{
    private readonly PaddockModel _model;
    private readonly PaddockManager _manager;
    private Point _dragStartOffset;

    public event Action? RemoveRequested;

    public PaddockViewModel(PaddockModel model, PaddockManager manager)
    {
        _model = model;
        _manager = manager;

        // Load icons from model
        foreach (var iconEntry in model.Icons)
        {
            Icons.Add(new IconViewModel(iconEntry));
        }
    }

    public string Id => _model.Id;

    public string Title
    {
        get => _model.Title;
        set
        {
            _model.Title = value;
            OnPropertyChanged();
            _manager.SaveLayout();
        }
    }

    public bool IsLocked
    {
        get => _model.Locked;
        set
        {
            _model.Locked = value;
            OnPropertyChanged();
        }
    }

    public double Opacity => _model.Style.Opacity;

    public CornerRadius CornerRadius => new(_model.Style.CornerRadius);

    public CornerRadius TitleCornerRadius => new(
        _model.Style.CornerRadius, _model.Style.CornerRadius, 0, 0);

    public Brush BackgroundBrush
    {
        get
        {
            var color = (Color)ColorConverter.ConvertFromString(_model.Style.BackgroundColor);
            return new SolidColorBrush(color);
        }
    }

    public Brush TitleColor
    {
        get
        {
            var color = (Color)ColorConverter.ConvertFromString(_model.Style.TitleColor);
            return new SolidColorBrush(color);
        }
    }

    public new Brush BorderBrush
    {
        get
        {
            var color = (Color)ColorConverter.ConvertFromString(_model.Style.BorderColor);
            return new SolidColorBrush(color);
        }
    }

    public Thickness BorderThickness => new(_model.Style.BorderThickness);

    public ObservableCollection<IconViewModel> Icons { get; } = new();

    public void BeginDrag(Point mouseOffset)
    {
        _dragStartOffset = mouseOffset;
    }

    public void UpdateSize(double width, double height)
    {
        _model.Width = width;
        _model.Height = height;
        _manager.SaveLayout();
    }

    public void UpdatePosition(double x, double y)
    {
        _model.X = x;
        _model.Y = y;
        _manager.SaveLayout();
    }

    public void BeginRenameTitle()
    {
        // TODO: Show inline text editor for title
    }

    public void HandleIconDrop(DragEventArgs e)
    {
        // TODO: Handle icon drop from desktop or other paddocks
    }

    public void Remove()
    {
        RemoveRequested?.Invoke();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
