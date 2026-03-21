namespace Paddock.Core.Models;

public class AppSettings
{
    public int Version { get; set; } = 1;
    public bool StartWithWindows { get; set; } = true;
    public string GlobalTheme { get; set; } = "dark";
    public bool QuickHideEnabled { get; set; } = true;
    public string QuickHideHotkey { get; set; } = "Ctrl+F12";
    public bool SnapEnabled { get; set; } = true;
    public double DefaultOpacity { get; set; } = 0.88;
    public double DefaultCornerRadius { get; set; } = 16;
    public string ActiveCorral { get; set; } = "default";
    public Dictionary<string, CorralModel> Corrals { get; set; } = new()
    {
        ["default"] = new CorralModel()
    };
    public List<AutoSortRule> AutoSortRules { get; set; } = new();
}
