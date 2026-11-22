using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Paddock.Core.Services;
using Drawing = System.Drawing;

namespace Paddock.App.Helpers;

/// <summary>
/// A picture of every monitor at one moment, in physical pixels, for the
/// eyedropper. Taking it <em>before</em> the eyedropper appears is what lets
/// the eyedropper show the screen exactly as it was without being in it.
/// </summary>
internal sealed class ScreenSnapshot
{
    private readonly byte[] _pixels;
    private readonly int _stride;

    private ScreenSnapshot(BitmapSource image, byte[] pixels, int stride)
    {
        Image = image;
        _pixels = pixels;
        _stride = stride;
    }

    /// <summary>The picture, frozen, at 96 DPI so one image unit is one pixel.</summary>
    public BitmapSource Image { get; }

    public int Width => Image.PixelWidth;

    public int Height => Image.PixelHeight;

    /// <summary>Captures the whole virtual screen, or returns null if Windows will not allow it.</summary>
    public static ScreenSnapshot? Capture()
    {
        try
        {
            var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            using var bitmap = new Drawing.Bitmap(bounds.Width, bounds.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (var graphics = Drawing.Graphics.FromImage(bitmap))
            {
                // CaptureBlt includes layered windows — paddocks among them.
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size,
                    Drawing.CopyPixelOperation.SourceCopy | Drawing.CopyPixelOperation.CaptureBlt);
            }

            var data = bitmap.LockBits(
                new Drawing.Rectangle(0, 0, bounds.Width, bounds.Height),
                Drawing.Imaging.ImageLockMode.ReadOnly,
                Drawing.Imaging.PixelFormat.Format32bppArgb);

            byte[] pixels;
            int stride;

            try
            {
                stride = data.Stride;
                pixels = new byte[stride * bounds.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            // The screen has no transparency; make sure the copy has none either.
            for (var i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;

            var image = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            image.Freeze();
            return new ScreenSnapshot(image, pixels, stride);
        }
        catch (Exception ex)
        {
            Log.Error("Could not capture the screen for the eyedropper.", ex);
            return null;
        }
    }

    /// <summary>The colour of one pixel (clamped to the picture).</summary>
    public Color ColorAt(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        var i = y * _stride + x * 4;
        return Color.FromRgb(_pixels[i + 2], _pixels[i + 1], _pixels[i]);
    }
}
