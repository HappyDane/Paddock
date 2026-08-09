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
    /// <summary>Height of the title strip, and of a rolled-up paddock.</summary>
    public const double TitleBarHeight = 32;

    private static readonly Color FallbackBackground = Color.FromRgb(0x1C, 0x1C, 0x1E);

    private readonly PaddockModel _model;
    private readonly PaddockManager _manager;
    private bool _isRenaming;
    private string _renameText;

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

    // --- Geometry (screen coordinates, device-independent pixels) ---

    public double X => _model.X;

    public double Y => _model.Y;

    public double Width => _model.Width;

    public double Height => _model.Height;

    /// <summary>Height the window should actually take, honouring roll-up.</summary>
    public double DisplayHeight => IsRolledUp ? TitleBarHeight : _model.Height;

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
            OnPropertyChanged(nameof(DisplayHeight));
            OnPropertyChanged(nameof(EmptyHintVisibility));
            _manager.SaveLayout();
        }
    }

    public Visibility ContentVisibility => IsRolledUp ? Visibility.Collapsed : Visibility.Visible;

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

    /// <summary>
    /// Background including the configured transparency. The alpha lives in the
    /// brush rather than on the element's Opacity so that icons and labels stay
    /// fully opaque on top of a see-through panel.
    /// </summary>
    public Brush BackgroundBrush
    {
        get
        {
            var colour = ParseColour(_model.Style.BackgroundColor, FallbackBackground);
            var alpha = (byte)Math.Clamp(_model.Style.Opacity * 255, 0, 255);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, colour.R, colour.G, colour.B));
            brush.Freeze();
            return brush;
        }
    }

    public Brush BorderBrush
    {
        get
        {
            var brush = new SolidColorBrush(ParseColour(_model.Style.BorderColor, Colors.Transparent));
            brush.Freeze();
            return brush;
        }
    }

    public Thickness BorderThickness => new(_model.Style.BorderThickness);

    public CornerRadius CornerRadius => new(_model.Style.CornerRadius);

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
            OnPropertyChanged(nameof(BackgroundBrush));
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

    /// <summary>Shows the "drag icons here" hint while a paddock is empty.</summary>
    public Visibility EmptyHintVisibility => !IsRolledUp && Icons.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public void RefreshIcons()
    {
        Icons.Clear();
        foreach (var entry in PaddockManager.GetSortedIcons(_model))
        {
            Icons.Add(new IconViewModel(entry));
        }

        OnPropertyChanged(nameof(EmptyHintVisibility));
    }

    /// <summary>
    /// Takes the files described by a drop and puts them in this paddock,
    /// moving desktop items off the desktop. Returns the id of the paddock the
    /// items came from, when they came from another paddock.
    /// </summary>
    public string? HandleDrop(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(IconDragData)))
        {
            if (e.Data.GetData(typeof(IconDragData)) is not IconDragData data || data.SourcePaddockId == Id)
                return null;

            _manager.MoveIconBetweenPaddocks(data.SourcePaddockId, Id, data.DesktopPath);
            RefreshIcons();
            return data.SourcePaddockId;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
                return null;

            foreach (var file in files)
            {
                _manager.AddIconToPaddock(Id, file);
            }

            RefreshIcons();
        }

        return null;
    }

    /// <summary>Adds a single item, moving it off the desktop.</summary>
    public void AddIcon(string path)
    {
        _manager.AddIconToPaddock(Id, path);
        RefreshIcons();
    }

    /// <summary>Adds several items in one pass, refreshing the grid once.</summary>
    public void AddIcons(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            _manager.AddIconToPaddock(Id, path);
        }

        RefreshIcons();
    }

    /// <summary>Puts an item back on the desktop and drops it from this paddock.</summary>
    public void EjectIcon(string path)
    {
        _manager.EjectIcon(Id, path);
        RefreshIcons();
    }

    // --- Geometry updates ---

    public void UpdatePosition(double x, double y)
    {
        _model.X = x;
        _model.Y = y;
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        _manager.SaveLayout();
    }

    public void UpdateSize(double width, double height)
    {
        _model.Width = width;
        _model.Height = height;
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(DisplayHeight));
        _manager.SaveLayout();
    }

    /// <summary>
    /// Applies snapping to a candidate position without committing it, so the
    /// window can follow the cursor while snapping to edges and neighbours.
    /// </summary>
    public (double X, double Y) SnapPosition(
        double x, double y,
        LayoutEngine layoutEngine,
        IReadOnlyList<PaddockModel> allPaddocks,
        LayoutBounds bounds,
        double spacingGap)
    {
        var originalX = _model.X;
        var originalY = _model.Y;

        _model.X = x;
        _model.Y = y;

        var result = layoutEngine.SnapToEdges(_model, allPaddocks, bounds, spacingGap);

        _model.X = originalX;
        _model.Y = originalY;

        return result;
    }

    // --- Commands ---

    public void BeginRenameTitle()
    {
        RenameText = Title;
        IsRenaming = true;
    }

    public void CommitRename()
    {
        if (!string.IsNullOrWhiteSpace(RenameText))
            Title = RenameText.Trim();

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

    public void Remove()
    {
        RemoveRequested?.Invoke();
    }

    private static Color ParseColour(string value, Color fallback)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color colour)
                return colour;
        }
        catch (FormatException)
        {
            // Fall through to the default below.
        }

        return fallback;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Payload for dragging an icon out of a paddock — into another paddock, or out
/// to the desktop.
/// </summary>
[Serializable]
public class IconDragData
{
    public required string SourcePaddockId { get; init; }
    public required string DesktopPath { get; init; }
}
