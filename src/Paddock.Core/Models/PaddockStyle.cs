namespace Paddock.Core.Models;

public class PaddockStyle
{
    /// <summary>The default fill: a dark graphite that suits any wallpaper.</summary>
    public const string DefaultBackgroundColor = "#1C1C1E";

    public string BackgroundColor { get; set; } = DefaultBackgroundColor;
    public double Opacity { get; set; } = 0.88;
    public double CornerRadius { get; set; } = 16;
    public string TitleColor { get; set; } = "#98989D";

    /// <summary>
    /// Hairline edge that separates the paddock from the wallpaper. Paddocks
    /// cannot cast a shadow (each one is exactly its own window, so a shadow
    /// would be clipped), so a faint border does the same job.
    /// </summary>
    public string BorderColor { get; set; } = "#26FFFFFF";

    public double BorderThickness { get; set; } = 1;

    /// <summary>How big the icons in this paddock are drawn.</summary>
    public IconSize IconSize { get; set; } = IconSize.Medium;

    /// <summary>Typeface for the title and icon labels.</summary>
    public PaddockFont Font { get; set; } = PaddockFont.System;
}

public enum IconSize
{
    Small,
    Medium,
    Large,
}

/// <summary>A short, curated list of typefaces every Windows 10/11 machine has.</summary>
public enum PaddockFont
{
    /// <summary>Segoe UI Variable / Segoe UI — the Windows system font.</summary>
    System,

    /// <summary>Bahnschrift — narrow and tidy, fits longer names.</summary>
    Compact,

    /// <summary>Georgia — a calm serif.</summary>
    Serif,

    /// <summary>Cascadia Mono / Consolas — fixed width.</summary>
    Mono,
}
