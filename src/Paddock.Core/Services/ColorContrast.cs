namespace Paddock.Core.Services;

/// <summary>
/// Picks text that stays readable on a paddock of any colour and opacity, so
/// a light colour never ends up with light labels (or a dark one with dark).
/// </summary>
public static class ColorContrast
{
    /// <summary>
    /// Luminance assumed for whatever shows through a translucent paddock: the
    /// wallpaper is unknown, and most are mid-to-dark.
    /// </summary>
    private const double BackdropLuminance = 0.1;

    /// <summary>
    /// Below this the wallpaper dominates what is behind the text, and light
    /// text (as on the desktop itself) is the safe choice whatever the colour.
    /// </summary>
    private const double MinimumOpacityForDarkText = 0.45;

    /// <summary>
    /// Where white and black text have equal contrast (WCAG contrast ratio):
    /// brighter than this, dark text reads better.
    /// </summary>
    private const double Crossover = 0.179;

    /// <summary>WCAG relative luminance of an sRGB colour, 0 (black) to 1 (white).</summary>
    public static double Luminance(byte red, byte green, byte blue)
        => 0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);

    /// <summary>
    /// True when dark text reads better than light text on this background,
    /// drawn at <paramref name="opacity"/> over a typical wallpaper: only for a
    /// light colour that is opaque enough to carry the text on its own.
    /// </summary>
    public static bool PrefersDarkText(byte red, byte green, byte blue, double opacity)
    {
        var alpha = Math.Clamp(opacity, 0, 1);
        if (alpha < MinimumOpacityForDarkText)
            return false;

        var effective = alpha * Luminance(red, green, blue) + (1 - alpha) * BackdropLuminance;
        return effective > Crossover;
    }

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
