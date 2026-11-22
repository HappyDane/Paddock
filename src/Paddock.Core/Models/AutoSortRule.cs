namespace Paddock.Core.Models;

/// <summary>
/// "When a new item arrives on the desktop and it matches this, move it into
/// that paddock." One condition per rule; rules are checked in order and the
/// first match wins, so a specific rule goes above a catch-all.
/// </summary>
public class AutoSortRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public bool Enabled { get; set; } = true;

    public RuleMatchType MatchType { get; set; } = RuleMatchType.Extension;

    /// <summary>What to look for; its meaning depends on <see cref="MatchType"/>.</summary>
    public string Pattern { get; set; } = string.Empty;

    public string TargetPaddockId { get; set; } = string.Empty;
}

/// <summary>What a rule looks at, and what its <see cref="AutoSortRule.Pattern"/> holds.</summary>
public enum RuleMatchType
{
    /// <summary>File extensions, e.g. <c>.exe, .msi</c>.</summary>
    Extension,

    /// <summary>File-name wildcards, e.g. <c>Screenshot*; Scan *</c>.</summary>
    NameGlob,

    /// <summary>A regular expression over the file name.</summary>
    NameRegex,

    /// <summary>A kind of item: a <see cref="Services.DesktopItemCategory"/> name, e.g. <c>Images</c>.</summary>
    Kind,

    /// <summary>A shortcut to a kind of app: an <see cref="Services.AppCategory"/> name, e.g. <c>Games</c>.</summary>
    AppCategory,

    /// <summary>Downloaded from the internet (Windows' "mark of the web"). No pattern.</summary>
    Downloaded,

    /// <summary>Moved or copied onto the desktop from this folder, e.g. Downloads.</summary>
    FromFolder,

    /// <summary>Everything — a catch-all for the bottom of the list. No pattern.</summary>
    Anything,
}
