namespace Paddock.Core.Models;

public class AppSettings
{
    public int Version { get; set; } = 1;
    public bool StartWithWindows { get; set; } = true;
    public string GlobalTheme { get; set; } = "dark";
    public bool QuickHideEnabled { get; set; } = true;
    public string QuickHideHotkey { get; set; } = "Ctrl+F12";

    /// <summary>Double-clicking empty desktop hides (and shows again) the paddocks.</summary>
    public bool DoubleClickToHide { get; set; } = true;

    /// <summary>Hiding the paddocks hides the desktop's own icons too, as Fences does.</summary>
    public bool QuickHideDesktopIcons { get; set; } = true;

    /// <summary>
    /// Set while Paddock has the desktop's icons hidden, so that if it stops
    /// without showing them again (a crash, a kill) the next start does.
    /// </summary>
    public bool DesktopIconsHiddenByPaddock { get; set; }
    public bool SnapEnabled { get; set; } = true;

    /// <summary>Right-drag on empty desktop draws a new paddock.</summary>
    public bool DrawOnDesktop { get; set; } = true;
    public double IntelligentSpacing { get; set; } = 12;
    public double DefaultOpacity { get; set; } = 0.88;
    public double DefaultCornerRadius { get; set; } = 16;

    /// <summary>New paddocks start as frosted glass.</summary>
    public bool DefaultFrostedGlass { get; set; }
    public string RollUpExpandMode { get; set; } = "click";
    public string ActiveCorral { get; set; } = "default";
    public Dictionary<string, CorralModel> Corrals { get; set; } = new()
    {
        ["default"] = new CorralModel()
    };
    /// <summary>Master switch for <see cref="AutoSortRules"/> on newly arriving desktop items.</summary>
    public bool AutoOrganizeEnabled { get; set; } = true;

    /// <summary>
    /// Where new desktop items go, checked top to bottom; the first rule that
    /// matches (and whose paddock still exists) wins.
    /// </summary>
    public List<AutoSortRule> AutoSortRules { get; set; } = new();
}
