using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class ColorContrastTests
{
    [Fact]
    public void Luminance_RunsFromBlackToWhite()
    {
        Assert.Equal(0, ColorContrast.Luminance(0, 0, 0), 3);
        Assert.Equal(1, ColorContrast.Luminance(255, 255, 255), 3);
    }

    [Theory]
    [InlineData(0x1C, 0x1C, 0x1E)] // graphite, the default
    [InlineData(0x1E, 0x4F, 0x8A)] // blue
    [InlineData(0x8A, 0x2F, 0x4D)] // rose
    [InlineData(0x2D, 0x5F, 0x3F)] // green
    public void DarkColours_GetLightText(byte r, byte g, byte b)
        => Assert.False(ColorContrast.PrefersDarkText(r, g, b, 0.88));

    [Theory]
    [InlineData(0xF2, 0xF2, 0xF7)] // frost
    [InlineData(0xE9, 0xE2, 0xD0)] // sand
    [InlineData(0xFF, 0xFF, 0xFF)]
    public void LightColours_GetDarkText(byte r, byte g, byte b)
        => Assert.True(ColorContrast.PrefersDarkText(r, g, b, 0.88));

    [Fact]
    public void ANearlyClearLightPaddock_ShowsTheWallpaper_SoTextGoesLight()
        => Assert.False(ColorContrast.PrefersDarkText(0xF2, 0xF2, 0xF7, 0.1));

    [Fact]
    public void OpacityOutsideTheRange_IsClamped()
    {
        Assert.True(ColorContrast.PrefersDarkText(255, 255, 255, 5));
        Assert.False(ColorContrast.PrefersDarkText(255, 255, 255, -1));
    }
}
