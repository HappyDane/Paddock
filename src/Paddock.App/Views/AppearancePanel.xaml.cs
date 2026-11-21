using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Paddock.App.ViewModels;

namespace Paddock.App.Views;

/// <summary>
/// A paddock's Appearance popover: colour, opacity, icon size, font, title.
/// Bound to the paddock's view model, so every change shows immediately.
/// </summary>
public partial class AppearancePanel : UserControl
{
    public AppearancePanel()
    {
        InitializeComponent();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;

            Close();
            e.Handled = true;
        };
    }

    private PaddockViewModel? ViewModel => DataContext as PaddockViewModel;

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ColorSwatch swatch } && ViewModel is { } viewModel)
            viewModel.BackgroundColor = swatch.Hex;
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => ViewModel?.ResetAppearance();

    private void Close()
    {
        if (Parent is Popup popup)
            popup.IsOpen = false;
    }
}
