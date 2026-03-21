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
    private bool _isRenaming;
    private string _renameText = string.Empty;

    public event Action? RemoveRequested;

    public PaddockViewModel(PaddockModel model, PaddockManager manager)
    {
        _model = model;
        _manager = manager;
        _renameText = model.Title;

        RefreshIcons();
    }

    // --- Identity ---
    public string Id => _model.Id;

    // --- Title ---
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

    public TitleVisibility TitleVisibility
    {
        get => _model.TitleVisibility;
        set
        {
            _model.TitleVisibility = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTitleAlwaysVisible));
            OnPropertyChanged(nameof(IsTitleHoverVisible));
            OnPropertyChanged(nameof(IsTitleHidden));
            _manager.SaveLayout();
        }
    }

    public bool IsTitleAlwaysVisible => _model.TitleVisibility == TitleVisibility.Always;
    public bool IsTitleHoverVisible => _model.TitleVisibility == TitleVisibility.Hover;
    public bool IsTitleHidden => _model.TitleVisibility == TitleVisibility.Hidden;

    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            _isRenaming = value;
            OnPropertyChanged();
        }
    }

    public string RenameText
    {
        get => _renameText;
        set
        {
            _renameText = value;
            OnPropertyChanged();
        }
    }

    // --- State ---
    public bool IsLocked
    {
        get => _model.Locked;
        set
        {
            _model.Locked = value;
            OnPropertyChanged();
            _manager.SaveLayout();
        }
    }

    public bool IsRolledUp
    {
        get => _model.IsRolledUp;
        set
        {
            _model.IsRolledUp = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ContentVisibility));
            OnPropertyChanged(nameof(RolledUpHeight));
            _manager.SaveLayout();
        }
    }

    public Visibility ContentVisibility => IsRolledUp ? Visibility.Collapsed : Visibility.Visible;
    public double RolledUpHeight => IsRolledUp ? 32 : _model.Height;

    public bool ExcludeFromQuickHide
    {
        get => _model.ExcludeFromQuickHide;
        set
        {
            _model.ExcludeFromQuickHide = value;
            OnPropertyChanged();
            _manager.SaveLayout();
        }
    }

    // --- Sorting ---
    public SortField SortBy
    {
        get => _model.SortBy;
        set
        {
            _manager.SortPaddockIcons(_model.Id, value, _model.SortAscending);
            OnPropertyChanged();
            RefreshIcons();
        }
    }

    public bool SortAscending
    {
        get => _model.SortAscending;
        set
        {
            _manager.SortPaddockIcons(_model.Id, _model.SortBy, value);
            OnPropertyChanged();
            RefreshIcons();
        }
    }

    // --- Styling ---
    public double Opacity => _model.Style.Opacity;

    public CornerRadius CornerRadius => new(_model.Style.CornerRadius);

    public Brush BackgroundBrush
    {
        get
        {
            var color = (Color)ColorConverter.ConvertFromString(_model.Style.BackgroundColor);
            return new SolidColorBrush(color);
        }
    }

    public string BackgroundColor
    {
        get => _model.Style.BackgroundColor;
        set
        {
            _model.Style.BackgroundColor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundBrush));
            _manager.SaveLayout();
        }
    }

    public double PaddockOpacity
    {
        get => _model.Style.Opacity;
        set
        {
            _model.Style.Opacity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Opacity));
            _manager.SaveLayout();
        }
    }

    public double PaddockCornerRadius
    {
        get => _model.Style.CornerRadius;
        set
        {
            _model.Style.CornerRadius = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CornerRadius));
            _manager.SaveLayout();
        }
    }

    // --- Icons ---
    public ObservableCollection<IconViewModel> Icons { get; } = new();

    public void RefreshIcons()
    {
        Icons.Clear();
        var sorted = PaddockManager.GetSortedIcons(_model);
        foreach (var entry in sorted)
        {
            Icons.Add(new IconViewModel(entry));
        }
    }

    // --- Actions ---
    public void BeginDrag(Point mouseOffset)
    {
        _dragStartOffset = mouseOffset;
    }

    public Point DragStartOffset => _dragStartOffset;

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
        RenameText = Title;
        IsRenaming = true;
    }

    public void CommitRename()
    {
        if (!string.IsNullOrWhiteSpace(RenameText))
        {
            Title = RenameText.Trim();
        }
        IsRenaming = false;
    }

    public void CancelRename()
    {
        RenameText = Title;
        IsRenaming = false;
    }

    public void ToggleRollUp()
    {
        IsRolledUp = !IsRolledUp;
    }

    public void HandleIconDrop(DragEventArgs e)
    {
        // Handle drop from another paddock
        if (e.Data.GetDataPresent(typeof(IconDragData)))
        {
            var data = (IconDragData)e.Data.GetData(typeof(IconDragData))!;
            if (data.SourcePaddockId != Id)
            {
                _manager.MoveIconBetweenPaddocks(data.SourcePaddockId, Id, data.DesktopPath);
                RefreshIcons();
            }
        }
        // Handle drop from desktop (file paths)
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            foreach (var file in files)
            {
                _manager.AddIconToPaddock(Id, file);
            }
            RefreshIcons();
        }
    }

    public void Remove()
    {
        RemoveRequested?.Invoke();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Data object for drag-drop between paddocks.
/// </summary>
[Serializable]
public class IconDragData
{
    public required string SourcePaddockId { get; init; }
    public required string DesktopPath { get; init; }
}
