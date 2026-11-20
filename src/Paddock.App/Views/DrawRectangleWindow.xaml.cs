using System.Windows;
using System.Windows.Interop;
using Paddock.Shell;

namespace Paddock.App.Views;

/// <summary>
/// The outline shown while a paddock is being right-dragged out on the desktop,
/// and while the "Create paddock here" menu is open. Purely visual: the mouse
/// passes straight through it to the desktop underneath.
/// </summary>
public partial class DrawRectangleWindow : Window
{
    public DrawRectangleWindow()
    {
        InitializeComponent();
    }

    /// <summary>Moves the outline to a rectangle in screen DIPs.</summary>
    public void ShowAt(Rect area)
    {
        Left = area.X;
        Top = area.Y;
        Width = Math.Max(1, area.Width);
        Height = Math.Max(1, area.Height);

        SizeText.Text = $"{area.Width:0} × {area.Height:0}";
        SizeLabel.Visibility = area.Width >= 90 && area.Height >= 40 ? Visibility.Visible : Visibility.Collapsed;

        if (!IsVisible)
            Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyles.MakeClickThroughOverlay(new WindowInteropHelper(this).Handle);
    }
}
