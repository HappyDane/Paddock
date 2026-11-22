using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>
/// Icon tint: every icon takes on one hue but keeps its own light and shade,
/// and transparent pixels stay exactly as they were.
/// </summary>
public class IconTintTests
{
    private static byte[] Pixel(byte r, byte g, byte b, byte a = 255) => [b, g, r, a];

    private static double Lum(byte[] bgra) => IconTint.Luminosity(bgra[2] / 255.0, bgra[1] / 255.0, bgra[0] / 255.0);

    [Fact]
    public void FullStrength_KeepsEachPixelsLuminosity()
    {
        var pixel = Pixel(200, 40, 40); // a red
        var before = Lum(pixel);

        IconTint.Apply(pixel, 0x0A, 0x84, 0xFF, strength: 1); // toward blue

        Assert.Equal(before, Lum(pixel), 2);
        Assert.True(pixel[0] > pixel[2], $"Expected the pixel to turn blue, got R={pixel[2]} B={pixel[0]}.");
    }

    [Fact]
    public void GreyTint_TurnsIconsMonochrome()
    {
        var pixel = Pixel(30, 160, 90);

        IconTint.Apply(pixel, 128, 128, 128, strength: 1);

        Assert.True(Math.Abs(pixel[0] - pixel[1]) <= 1 && Math.Abs(pixel[1] - pixel[2]) <= 1,
            $"Expected grey, got {pixel[2]},{pixel[1]},{pixel[0]}.");
    }

    [Fact]
    public void BlackAndWhite_StayBlackAndWhite()
    {
        var black = Pixel(0, 0, 0);
        var white = Pixel(255, 255, 255);

        IconTint.Apply(black, 0xFF, 0x37, 0x5F, 1);
        IconTint.Apply(white, 0xFF, 0x37, 0x5F, 1);

        Assert.Equal(Pixel(0, 0, 0), black);
        Assert.Equal(Pixel(255, 255, 255), white);
    }

    [Fact]
    public void ZeroStrength_ChangesNothing()
    {
        var pixel = Pixel(10, 200, 30, 128);
        var copy = pixel.ToArray();

        IconTint.Apply(pixel, 0xFF, 0, 0, strength: 0);

        Assert.Equal(copy, pixel);
    }

    [Fact]
    public void HalfStrength_LandsBetweenOriginalAndFullTint()
    {
        var full = Pixel(40, 200, 40);
        var half = Pixel(40, 200, 40);

        IconTint.Apply(full, 0xBF, 0x5A, 0xF2, 1);
        IconTint.Apply(half, 0xBF, 0x5A, 0xF2, 0.5);

        for (var c = 0; c < 3; c++)
        {
            var low = Math.Min(full[c], (byte)new byte[] { 40, 200, 40 }[c]);
            var high = Math.Max(full[c], (byte)new byte[] { 40, 200, 40 }[c]);
            Assert.InRange(half[c], low, high);
        }
    }

    [Fact]
    public void AlphaIsNeverChanged_AndTransparentPixelsAreSkipped()
    {
        var pixels = new byte[] { 10, 20, 30, 0, 10, 20, 30, 77 };

        IconTint.Apply(pixels, 0xFF, 0x9F, 0x0A, 1);

        Assert.Equal(new byte[] { 10, 20, 30, 0 }, pixels[..4]);
        Assert.Equal(77, pixels[7]);
    }

    [Theory]
    [InlineData("#1E4F8A", 0x1E, 0x4F, 0x8A)]
    [InlineData("1e4f8a", 0x1E, 0x4F, 0x8A)]
    [InlineData("#FF1E4F8A", 0x1E, 0x4F, 0x8A)]
    public void HexColor_ReadsSettingsColours(string text, byte r, byte g, byte b)
    {
        Assert.True(HexColor.TryParse(text, out var red, out var green, out var blue));
        Assert.Equal((r, g, b), (red, green, blue));
        Assert.Equal("#1E4F8A", HexColor.Format(red, green, blue));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("blue")]
    [InlineData("#12345")]
    public void HexColor_RejectsAnythingElse(string? text)
        => Assert.False(HexColor.TryParse(text, out _, out _, out _));
}
