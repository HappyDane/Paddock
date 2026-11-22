using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Paddock.App.ViewModels;
using Paddock.Core.Services;

namespace Paddock.App.Views;

/// <summary>
/// A paddock's Appearance popover: colour (or one picked off the screen),
/// solid or frosted glass, opacity, icon tint, icon size, font, title. Bound
/// to the paddock's view model, so every change shows immediately.
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

    private void TintSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ColorSwatch swatch } && ViewModel is { } viewModel)
            viewModel.IconTint = swatch.Hex;
    }

    private void NoTint_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            viewModel.IconTint = null;
    }

    /// <summary>
    /// Picks a colour off the screen. The panel gets out of the way first —
    /// it would otherwise be in the picture, over the wallpaper — and comes
    /// back with the colour applied.
    /// </summary>
    private void Eyedropper_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EyedropperChoice choice } || ViewModel is not { } viewModel)
            return;

        var popup = Parent as Popup;
        if (popup is not null)
            popup.IsOpen = false;

        PickColor(viewModel, choice.Target, popup);
    }

    private async void PickColor(PaddockViewModel viewModel, ColorTarget target, Popup? popup)
    {
        try
        {
            // Long enough for the panel's fade-out to finish before the screen is captured.
            await Task.Delay(300);

            if (EyedropperWindow.PickColor() is { } colour)
                viewModel.ApplyPickedColor(target, colour);
        }
        catch (Exception ex)
        {
            Log.Error("The eyedropper failed.", ex);
        }
        finally
        {
            if (popup is not null)
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () => popup.IsOpen = true);
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => ViewModel?.ResetAppearance();

    private void Close()
    {
        if (Parent is Popup popup)
            popup.IsOpen = false;
    }
}
