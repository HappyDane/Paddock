namespace Paddock.Core.Models;

public class PaddockStyle
{
    public string BackgroundColor { get; set; } = "#1C1C1E";
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
}
