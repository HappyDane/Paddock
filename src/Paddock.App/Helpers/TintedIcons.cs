using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Paddock.Core.Services;

namespace Paddock.App.Helpers;

/// <summary>A paddock's icon tint: the colour, and how far icons move toward it.</summary>
public readonly record struct IconTintSpec(byte R, byte G, byte B, double Strength);

/// <summary>
/// Tinted copies of icon bitmaps (see <see cref="IconTint"/>). Many icons
/// share one bitmap — every .txt file has the same icon — so copies are kept
/// per bitmap and per tint, and forgotten along with the bitmap.
/// </summary>
internal static class TintedIcons
{
    /// <summary>A strength slider drags through many values; a handful of copies per icon is plenty.</summary>
    private const int MaxTintsPerIcon = 8;

    private static readonly ConditionalWeakTable<BitmapSource, Dictionary<IconTintSpec, BitmapSource>> Cache = new();

    /// <summary>The icon as it should be drawn under this tint (frozen, safe on any thread).</summary>
    internal static BitmapSource Get(BitmapSource source, IconTintSpec tint)
    {
        var copies = Cache.GetOrCreateValue(source);

        lock (copies)
        {
            if (copies.TryGetValue(tint, out var cached))
                return cached;
        }

        var tinted = Tint(source, tint);

        lock (copies)
        {
            if (copies.Count >= MaxTintsPerIcon)
                copies.Clear();

            copies[tint] = tinted;
        }

        return tinted;
    }

    private static BitmapSource Tint(BitmapSource source, IconTintSpec tint)
    {
        try
        {
            // Straight alpha, so the blend sees each pixel's real colour.
            BitmapSource bgra = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            var stride = bgra.PixelWidth * 4;
            var pixels = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(pixels, stride, 0);

            IconTint.Apply(pixels, tint.R, tint.G, tint.B, tint.Strength);

            var result = BitmapSource.Create(
                bgra.PixelWidth, bgra.PixelHeight, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not tint an icon: {ex.Message}");
            return source;
        }
    }
}
