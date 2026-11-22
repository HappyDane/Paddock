using System.Globalization;

namespace Paddock.Core.Services;

/// <summary>The <c>#RRGGBB</c> colours settings.json stores.</summary>
public static class HexColor
{
    /// <summary>Reads <c>#RRGGBB</c> or <c>#AARRGGBB</c> (the alpha is ignored).</summary>
    public static bool TryParse(string? text, out byte red, out byte green, out byte blue)
    {
        red = green = blue = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 8)
            hex = hex[2..];

        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;

        red = (byte)(value >> 16);
        green = (byte)(value >> 8);
        blue = (byte)value;
        return true;
    }

    public static string Format(byte red, byte green, byte blue) => $"#{red:X2}{green:X2}{blue:X2}";
}
