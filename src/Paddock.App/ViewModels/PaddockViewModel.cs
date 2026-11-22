using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Paddock.App.Helpers;
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

    // Frozen brushes, rebuilt only when the style changes.
    private Brush? _backgroundBrush;
    private Brush? _borderBrush;

    // Text that stays readable on whatever colour the paddock is.
    private static readonly Brush LightLabel = Frozen(0xFF, 0xF2, 0xF2, 0xF7);
    private static readonly Brush LightTitle = Frozen(0xC0, 0xFF, 0xFF, 0xFF);
    private static readonly Brush LightHover = Frozen(0x1F, 0xFF, 0xFF, 0xFF);
    private static readonly Brush LightEdge = Frozen(0x26, 0xFF, 0xFF, 0xFF);
    private static readonly Brush DarkLabel = Frozen(0xFF, 0x1C, 0x1C, 0x1E);
    private static readonly Brush DarkTitle = Frozen(0xA6, 0x00, 0x00, 0x00);
    private static readonly Brush DarkHover = Frozen(0x14, 0x00, 0x00, 0x00);
    private static readonly Brush DarkEdge = Frozen(0x1F, 0x00, 0x00, 0x00);

    /// <summary>
    /// Appearance changes arrive with every step of a slider; the layout is
    /// written once they settle rather than dozens of times a second.
    /// </summary>
    private readonly DispatcherTimer _saveSoon;

    /// <summary>
    /// Above this opacity a frosted paddock would show almost none of its
    /// glass, so turning frosted glass on brings the tint down to this.
    /// </summary>
    private const double FrostedOpacityCeiling = 0.75;
    private const double FrostedOpacity = 0.45;

    public event Action? RemoveRequested;

    /// <summary>Items just arrived (an auto-organize rule sent them): show where they went.</summary>
    public event Action? ArrivalHighlightRequested;

    public PaddockViewModel(PaddockModel model, PaddockManager manager)
    {
        _model = model;
        _manager = manager;
        _renameText = model.Title;

        _saveSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveSoon.Tick += (_, _) =>
        {
            _saveSoon.Stop();
            _manager.SaveLayout();
        };

        Swatches = Palette.Select(p => new ColorSwatch(p.Name, p.Hex)).ToList();
        BackgroundPicker = new EyedropperChoice(ColorTarget.Background, "Pick a colour from the screen", "Pick paddock colour");
        ColorChoices = [.. Swatches, BackgroundPicker];

        TintSwatches = TintPalette.Select(p => new ColorSwatch(p.Name, p.Hex, $"{p.Name} icon tint")).ToList();
        NoTint = new NoTintChoice();
        TintPicker = new EyedropperChoice(ColorTarget.IconTint, "Pick a tint from the screen", "Pick icon tint");
        TintChoices = [NoTint, .. TintSwatches, TintPicker];

        UpdateSwatchSelection();

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

    // Two-way for the Appearance panel's segmented control: choosing a segment
    // sets it; un-choosing (the other segment taking over) does nothing.
    public bool IsTitleAlwaysVisible
    {
        get => _model.TitleVisibility == TitleVisibility.Always;
        set { if (value) TitleVisibility = TitleVisibility.Always; }
    }

    public bool IsTitleHoverVisible
    {
        get => _model.TitleVisibility == TitleVisibility.Hover;
        set { if (value) TitleVisibility = TitleVisibility.Hover; }
    }

    public bool IsTitleHidden
    {
        get => _model.TitleVisibility == TitleVisibility.Hidden;
        set { if (value) TitleVisibility = TitleVisibility.Hidden; }
    }

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
            OnPropertyChanged(nameof(RollUpMenuText));
            OnPropertyChanged(nameof(EmptyHintVisibility));
            _manager.SaveLayout();
        }
    }

    public Visibility ContentVisibility => IsRolledUp ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The menu says what will happen, not what the state is.</summary>
    public string RollUpMenuText => IsRolledUp ? "Expand" : "Roll Up";

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
            OnPropertyChanged(nameof(IsSortedByName));
            OnPropertyChanged(nameof(IsSortedByDate));
            OnPropertyChanged(nameof(IsSortedByType));
            OnPropertyChanged(nameof(IsSortedBySize));
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

    // Menu checkmarks for the current sort.
    public bool IsSortedByName => _model.SortBy == SortField.Name;
    public bool IsSortedByDate => _model.SortBy == SortField.DateModified;
    public bool IsSortedByType => _model.SortBy == SortField.FileType;
    public bool IsSortedBySize => _model.SortBy == SortField.FileSize;

    // --- Appearance ---

    /// <summary>
    /// The colours offered in the Appearance panel: muted, so they read as a
    /// tint over the wallpaper rather than a block of paint.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Palette =
    [
        ("Graphite", PaddockStyle.DefaultBackgroundColor),
        ("Slate", "#39404D"),
        ("Blue", "#1E4F8A"),
        ("Teal", "#1B6264"),
        ("Green", "#2D5F3F"),
        ("Amber", "#8A5A16"),
        ("Rose", "#8A2F4D"),
        ("Purple", "#4E3A8A"),
        ("Sand", "#E9E2D0"),
        ("Frost", "#F2F2F7"),
    ];

    public IReadOnlyList<ColorSwatch> Swatches { get; }

    /// <summary>The swatches plus the eyedropper, which shows a picked colour once there is one.</summary>
    public IReadOnlyList<object> ColorChoices { get; }

    public EyedropperChoice BackgroundPicker { get; }

    /// <summary>
    /// Icon tints: vivid, because they colour small icons rather than a large
    /// panel. Mono has no saturation, so it turns icons greyscale.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> TintPalette =
    [
        ("Mono", "#8E8E93"),
        ("Blue", "#0A84FF"),
        ("Teal", "#40C8E0"),
        ("Green", "#30D158"),
        ("Yellow", "#FFD60A"),
        ("Orange", "#FF9F0A"),
        ("Pink", "#FF375F"),
        ("Purple", "#BF5AF2"),
    ];

    public IReadOnlyList<ColorSwatch> TintSwatches { get; }

    public NoTintChoice NoTint { get; }

    public EyedropperChoice TintPicker { get; }

    /// <summary>"No tint", the tint swatches, and the eyedropper.</summary>
    public IReadOnlyList<object> TintChoices { get; }

    // --- Frosted glass ---

    /// <summary>
    /// The wallpaper shows through blurred, under the paddock's colour. The
    /// window applies the effect itself (see <see cref="Views.PaddockWindow"/>).
    /// </summary>
    public bool FrostedGlass
    {
        get => _model.Style.FrostedGlass;
        set
        {
            if (_model.Style.FrostedGlass == value)
                return;

            _model.Style.FrostedGlass = value;

            if (value && _model.Style.Opacity >= FrostedOpacityCeiling)
                _model.Style.Opacity = FrostedOpacity;

            AppearanceChanged();
        }
    }

    public bool IsBackgroundSolid
    {
        get => !FrostedGlass;
        set { if (value) FrostedGlass = false; }
    }

    public bool IsBackgroundFrosted
    {
        get => FrostedGlass;
        set { if (value) FrostedGlass = true; }
    }

    // --- Icon tint ---

    /// <summary>The colour icons take on, as #RRGGBB, or null for none.</summary>
    public string? IconTint
    {
        get => _model.Style.IconTint;
        set
        {
            _model.Style.IconTint = HexColor.TryParse(value, out var r, out var g, out var b)
                ? HexColor.Format(r, g, b)
                : null;
            AppearanceChanged();
        }
    }

    public double IconTintStrength
    {
        get => _model.Style.IconTintStrength;
        set
        {
            // In steps of 5%: each step re-tints every icon, and a slider sends many.
            _model.Style.IconTintStrength = Math.Round(Math.Clamp(value, 0.1, 1) * 20) / 20;
            AppearanceChanged();
        }
    }

    public string IconTintStrengthText => $"{Math.Round(_model.Style.IconTintStrength * 100):0}%";

    public bool HasIconTint => CurrentTint is not null;

    public Visibility TintStrengthVisibility => HasIconTint ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>What icons are drawn with, or null when they keep their own colours.</summary>
    private IconTintSpec? CurrentTint
        => HexColor.TryParse(_model.Style.IconTint, out var r, out var g, out var b)
            ? new IconTintSpec(r, g, b, _model.Style.IconTintStrength)
            : null;

    /// <summary>Applies a colour from the eyedropper to whatever it was picking for.</summary>
    public void ApplyPickedColor(ColorTarget target, Color colour)
    {
        var hex = HexColor.Format(colour.R, colour.G, colour.B);

        if (target == ColorTarget.IconTint)
            IconTint = hex;
        else
            BackgroundColor = hex;
    }

    /// <summary>
    /// Background including the configured transparency. The alpha lives in the
    /// brush rather than on the element's Opacity so that icons and labels stay
    /// fully opaque on top of a see-through panel.
    /// </summary>
    public Brush BackgroundBrush => _backgroundBrush ??= BuildBackgroundBrush();

    /// <summary>The hairline edge: light on dark paddocks, dark on light ones.</summary>
    public Brush BorderBrush => _borderBrush ??= UsesDarkText ? DarkEdge : BuildBorderBrush();

    public Thickness BorderThickness => new(_model.Style.BorderThickness);

    public CornerRadius CornerRadius => new(_model.Style.CornerRadius);

    public string BackgroundColor
    {
        get => _model.Style.BackgroundColor;
        set
        {
            _model.Style.BackgroundColor = value;
            AppearanceChanged();
        }
    }

    public double PaddockOpacity
    {
        get => _model.Style.Opacity;
        set
        {
            _model.Style.Opacity = Math.Clamp(value, 0.05, 1);
            AppearanceChanged();
        }
    }

    public string OpacityText => $"{Math.Round(_model.Style.Opacity * 100):0}%";

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

    public IconSize IconSize
    {
        get => _model.Style.IconSize;
        set
        {
            _model.Style.IconSize = value;
            AppearanceChanged();
        }
    }

    public bool IsIconSmall
    {
        get => IconSize == IconSize.Small;
        set { if (value) IconSize = IconSize.Small; }
    }

    public bool IsIconMedium
    {
        get => IconSize == IconSize.Medium;
        set { if (value) IconSize = IconSize.Medium; }
    }

    public bool IsIconLarge
    {
        get => IconSize == IconSize.Large;
        set { if (value) IconSize = IconSize.Large; }
    }

    // What an icon size means on screen (DIPs).
    public double IconPixelSize => IconSize switch { IconSize.Small => 32, IconSize.Large => 64, _ => 44 };
    public double IconCellWidth => IconSize switch { IconSize.Small => 66, IconSize.Large => 96, _ => 76 };
    public double IconCellHeight => IconSize switch { IconSize.Small => 72, IconSize.Large => 112, _ => 84 };
    public double LabelFontSize => IconSize switch { IconSize.Small => 10, IconSize.Large => 12, _ => 10.5 };
    public double LabelLineHeight => Math.Round(LabelFontSize * 1.33);
    public double LabelMaxHeight => LabelLineHeight * 2;

    public PaddockFont Font
    {
        get => _model.Style.Font;
        set
        {
            _model.Style.Font = value;
            AppearanceChanged();
        }
    }

    public bool IsFontSystem
    {
        get => Font == PaddockFont.System;
        set { if (value) Font = PaddockFont.System; }
    }

    public bool IsFontCompact
    {
        get => Font == PaddockFont.Compact;
        set { if (value) Font = PaddockFont.Compact; }
    }

    public bool IsFontSerif
    {
        get => Font == PaddockFont.Serif;
        set { if (value) Font = PaddockFont.Serif; }
    }

    public bool IsFontMono
    {
        get => Font == PaddockFont.Mono;
        set { if (value) Font = PaddockFont.Mono; }
    }

    public FontFamily TextFontFamily => FontFor(Font);

    public static FontFamily FontFor(PaddockFont font) => font switch
    {
        PaddockFont.Compact => new FontFamily("Bahnschrift, Segoe UI"),
        PaddockFont.Serif => new FontFamily("Georgia, Cambria, Segoe UI"),
        PaddockFont.Mono => new FontFamily("Cascadia Mono, Consolas"),
        _ => new FontFamily("Segoe UI Variable Text, Segoe UI"),
    };

    public Brush LabelBrush => UsesDarkText ? DarkLabel : LightLabel;

    public Brush TitleBrush => UsesDarkText ? DarkTitle : LightTitle;

    public Brush IconHoverBrush => UsesDarkText ? DarkHover : LightHover;

    /// <summary>Puts colour, glass, opacity, tint, icon size, font and title back to the defaults.</summary>
    public void ResetAppearance()
    {
        var defaults = new PaddockStyle();
        _model.Style.BackgroundColor = defaults.BackgroundColor;
        _model.Style.Opacity = defaults.Opacity;
        _model.Style.FrostedGlass = defaults.FrostedGlass;
        _model.Style.IconTint = defaults.IconTint;
        _model.Style.IconTintStrength = defaults.IconTintStrength;
        _model.Style.IconSize = defaults.IconSize;
        _model.Style.Font = defaults.Font;
        AppearanceChanged();
        TitleVisibility = new PaddockModel().TitleVisibility;
    }

    private bool UsesDarkText
    {
        get
        {
            var colour = ParseColour(_model.Style.BackgroundColor, FallbackBackground);
            return ColorContrast.PrefersDarkText(colour.R, colour.G, colour.B, _model.Style.Opacity);
        }
    }

    /// <summary>One appearance setting changed: redraw everything that depends on it.</summary>
    private void AppearanceChanged()
    {
        InvalidateBrushes();
        UpdateSwatchSelection();

        var tint = CurrentTint;
        foreach (var icon in Icons)
            icon.SetTint(tint);

        foreach (var name in AppearanceProperties)
            OnPropertyChanged(name);

        _saveSoon.Stop();
        _saveSoon.Start();
    }

    private static readonly string[] AppearanceProperties =
    [
        nameof(BackgroundColor), nameof(BackgroundBrush), nameof(BorderBrush),
        nameof(PaddockOpacity), nameof(OpacityText),
        nameof(IconSize), nameof(IsIconSmall), nameof(IsIconMedium), nameof(IsIconLarge),
        nameof(IconPixelSize), nameof(IconCellWidth), nameof(IconCellHeight),
        nameof(LabelFontSize), nameof(LabelLineHeight), nameof(LabelMaxHeight),
        nameof(Font), nameof(IsFontSystem), nameof(IsFontCompact), nameof(IsFontSerif), nameof(IsFontMono),
        nameof(TextFontFamily), nameof(LabelBrush), nameof(TitleBrush), nameof(IconHoverBrush),
        nameof(FrostedGlass), nameof(IsBackgroundSolid), nameof(IsBackgroundFrosted),
        nameof(IconTint), nameof(IconTintStrength), nameof(IconTintStrengthText),
        nameof(HasIconTint), nameof(TintStrengthVisibility),
    ];

    private void UpdateSwatchSelection()
    {
        var background = _model.Style.BackgroundColor;
        foreach (var swatch in Swatches)
            swatch.IsSelected = string.Equals(swatch.Hex, background, StringComparison.OrdinalIgnoreCase);

        // A colour that is none of the swatches came from the eyedropper (or settings.json).
        BackgroundPicker.Show(Swatches.Any(s => s.IsSelected) ? null : background);

        var tint = _model.Style.IconTint;
        NoTint.IsSelected = tint is null;
        foreach (var swatch in TintSwatches)
            swatch.IsSelected = string.Equals(swatch.Hex, tint, StringComparison.OrdinalIgnoreCase);

        TintPicker.Show(tint is null || TintSwatches.Any(s => s.IsSelected) ? null : tint);
    }

    // --- Icons ---
    public ObservableCollection<IconViewModel> Icons { get; } = new();

    /// <summary>Shows the "drag icons here" hint while a paddock is empty.</summary>
    public Visibility EmptyHintVisibility => !IsRolledUp && Icons.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public void RefreshIcons()
    {
        var desired = PaddockManager.GetSortedIcons(_model);

        if (MatchesCurrentIcons(desired))
            return;

        ReconcileIcons(desired);

        OnPropertyChanged(nameof(EmptyHintVisibility));
    }

    /// <summary>
    /// Brings the icon list in line with <paramref name="desired"/> using the
    /// fewest possible collection changes: existing view models are kept (so
    /// their icons, cached or still loading, survive), and dropping one file into
    /// a full paddock costs a single insert rather than a full rebuild.
    /// </summary>
    private void ReconcileIcons(List<IconEntry> desired)
    {
        var wanted = new HashSet<string>(
            desired.Select(entry => entry.DesktopPath),
            StringComparer.OrdinalIgnoreCase);

        for (var i = Icons.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Icons[i].FullPath))
                Icons.RemoveAt(i);
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var path = desired[index].DesktopPath;

            if (index < Icons.Count && PathEquals(Icons[index].FullPath, path))
                continue;

            var existing = IndexOfIcon(path, index);

            if (existing >= 0)
                Icons.Move(existing, index);
            else
                Icons.Insert(index, new IconViewModel(desired[index], CurrentTint));
        }

        while (Icons.Count > desired.Count)
        {
            Icons.RemoveAt(Icons.Count - 1);
        }
    }

    private int IndexOfIcon(string path, int startAt)
    {
        for (var i = startAt; i < Icons.Count; i++)
        {
            if (PathEquals(Icons[i].FullPath, path))
                return i;
        }

        return -1;
    }

    private static bool PathEquals(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the grid already shows exactly these items in this order. The
    /// store watcher fires for Paddock's own moves too, so "nothing changed" is
    /// the common case and should cost nothing.
    /// </summary>
    private bool MatchesCurrentIcons(List<IconEntry> desired)
    {
        if (desired.Count != Icons.Count)
            return false;

        for (var i = 0; i < desired.Count; i++)
        {
            if (!PathEquals(desired[i].DesktopPath, Icons[i].FullPath))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Reads what a drop carries. This has to happen inside the drop handler —
    /// the data object belongs to the drag source and is gone once the handler
    /// returns — but acting on it should not: see <see cref="ApplyDrop"/>.
    /// </summary>
    public DropRequest? ReadDrop(IDataObject data)
    {
        if (data.GetDataPresent(typeof(IconDragData)))
        {
            return data.GetData(typeof(IconDragData)) is IconDragData icon && icon.SourcePaddockId != Id
                ? new DropRequest(icon, [])
                : null;
        }

        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            return new DropRequest(null, files);

        return null;
    }

    /// <summary>
    /// Puts dropped items in this paddock, moving desktop items off the
    /// desktop. Returns the id of the paddock the item came from, when it came
    /// from another paddock, so that one can be redrawn too.
    /// </summary>
    public string? ApplyDrop(DropRequest request)
    {
        Log.Info(request.Icon is { } dragged
            ? $"Drop onto \"{Title}\": an icon from paddock {dragged.SourcePaddockId}."
            : $"Drop onto \"{Title}\": {request.Files.Count} item(s).");

        if (request.Icon is { } icon)
        {
            _manager.MoveIconBetweenPaddocks(icon.SourcePaddockId, Id, icon.DesktopPath);
            RefreshIcons();
            return icon.SourcePaddockId;
        }

        AddIcons(request.Files);
        return null;
    }

    /// <summary>
    /// Adds several items in one pass: one settings write, one grid refresh and
    /// at most one permission prompt for the whole batch.
    /// </summary>
    public void AddIcons(IEnumerable<string> paths)
    {
        _manager.AddIconsToPaddock(Id, paths);
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

    /// <summary>Briefly outlines the paddock, so the user sees where new items went.</summary>
    public void HighlightArrival() => ArrivalHighlightRequested?.Invoke();

    private Brush BuildBackgroundBrush()
    {
        var colour = ParseColour(_model.Style.BackgroundColor, FallbackBackground);
        var alpha = (byte)Math.Clamp(_model.Style.Opacity * 255, 0, 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, colour.R, colour.G, colour.B));
        brush.Freeze();
        return brush;
    }

    private Brush BuildBorderBrush()
    {
        var brush = new SolidColorBrush(ParseColour(_model.Style.BorderColor, Colors.Transparent));
        brush.Freeze();
        return brush;
    }

    /// <summary>Forces the cached brushes to be rebuilt on next use.</summary>
    private void InvalidateBrushes()
    {
        _backgroundBrush = null;
        _borderBrush = null;
    }

    private static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
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

/// <summary>One colour in the Appearance panel.</summary>
public sealed class ColorSwatch : INotifyPropertyChanged
{
    private bool _isSelected;

    public ColorSwatch(string name, string hex, string? automationName = null)
    {
        Name = name;
        Hex = hex;
        AutomationName = automationName ?? name;

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        Brush = brush;
    }

    public string Name { get; }

    public string Hex { get; }

    /// <summary>Name for screen readers (and the smoke test): tints say they are tints.</summary>
    public string AutomationName { get; }

    public Brush Brush { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>What a colour from the eyedropper is for.</summary>
public enum ColorTarget
{
    Background,
    IconTint,
}

/// <summary>
/// The eyedropper at the end of a row of swatches. Once it has picked a
/// colour that is none of the swatches, it shows that colour, ringed.
/// </summary>
public sealed class EyedropperChoice : INotifyPropertyChanged
{
    private static readonly Brush LightGlyph = Frozen(Colors.White);
    private static readonly Brush DarkGlyph = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1E));

    private Brush? _fill;
    private Brush? _glyph;

    public EyedropperChoice(ColorTarget target, string toolTip, string automationName)
    {
        Target = target;
        ToolTip = toolTip;
        AutomationName = automationName;
    }

    public ColorTarget Target { get; }

    public string ToolTip { get; }

    public string AutomationName { get; }

    /// <summary>The picked colour, or null while a swatch (or nothing) is chosen.</summary>
    public Brush? Fill => _fill;

    /// <summary>The eyedropper glyph's colour; null to use the theme's text colour.</summary>
    public Brush? Glyph => _glyph;

    public bool IsSelected => _fill is not null;

    internal void Show(string? hex)
    {
        Brush? fill = null;
        Brush? glyph = null;

        if (HexColor.TryParse(hex, out var r, out var g, out var b))
        {
            fill = Frozen(Color.FromRgb(r, g, b));
            glyph = ColorContrast.PrefersDarkText(r, g, b, 1) ? DarkGlyph : LightGlyph;
        }

        _fill = fill;
        _glyph = glyph;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Fill)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Glyph)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
    }

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>"No tint" at the start of the tint swatches.</summary>
public sealed class NoTintChoice : INotifyPropertyChanged
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>What a drop onto a paddock carries: one icon from another paddock, or files.</summary>
public sealed record DropRequest(IconDragData? Icon, IReadOnlyList<string> Files);

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
