namespace Paddock.Core.Services;

/// <summary>
/// Recolours icon pixels toward one hue, so a paddock's icons read as a set.
///
/// This is the W3C "color" blend mode: every pixel keeps its own luminosity —
/// so an icon's light and shade, and with them its detail, survive — and takes
/// the tint's hue and saturation. The result is then mixed with the original
/// by <c>strength</c>. A grey tint (no saturation) turns icons monochrome.
/// Alpha is never changed.
/// </summary>
public static class IconTint
{
    /// <summary>
    /// Tints 32-bit pixels in place. They are in B, G, R, A byte order with
    /// straight (not premultiplied) alpha — WPF's <c>Bgra32</c>.
    /// </summary>
    public static void Apply(Span<byte> bgra, byte red, byte green, byte blue, double strength)
    {
        strength = Math.Clamp(strength, 0, 1);
        if (strength <= 0)
            return;

        var tint = (R: red / 255.0, G: green / 255.0, B: blue / 255.0);

        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] == 0)
                continue;

            var b = bgra[i] / 255.0;
            var g = bgra[i + 1] / 255.0;
            var r = bgra[i + 2] / 255.0;

            var (cr, cg, cb) = SetLuminosity(tint.R, tint.G, tint.B, Luminosity(r, g, b));

            bgra[i] = ToByte(b + (cb - b) * strength);
            bgra[i + 1] = ToByte(g + (cg - g) * strength);
            bgra[i + 2] = ToByte(r + (cr - r) * strength);
        }
    }

    /// <summary>The blend mode's luminosity: a weighted sum of the channels, 0 to 1.</summary>
    public static double Luminosity(double r, double g, double b) => 0.3 * r + 0.59 * g + 0.11 * b;

    /// <summary>The colour with the hue and saturation of (r, g, b) and the given luminosity.</summary>
    private static (double R, double G, double B) SetLuminosity(double r, double g, double b, double luminosity)
    {
        var shift = luminosity - Luminosity(r, g, b);
        return Clip(r + shift, g + shift, b + shift);
    }

    /// <summary>Brings channels back into 0–1 without changing the luminosity.</summary>
    private static (double R, double G, double B) Clip(double r, double g, double b)
    {
        var l = Luminosity(r, g, b);
        var low = Math.Min(r, Math.Min(g, b));
        var high = Math.Max(r, Math.Max(g, b));

        if (low < 0 && l - low > 1e-9)
        {
            r = l + (r - l) * l / (l - low);
            g = l + (g - l) * l / (l - low);
            b = l + (b - l) * l / (l - low);
        }

        if (high > 1 && high - l > 1e-9)
        {
            r = l + (r - l) * (1 - l) / (high - l);
            g = l + (g - l) * (1 - l) / (high - l);
            b = l + (b - l) * (1 - l) / (high - l);
        }

        return (r, g, b);
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}
