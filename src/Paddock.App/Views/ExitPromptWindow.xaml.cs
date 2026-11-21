using System.Windows;

namespace Paddock.App.Views;

/// <summary>What the user wants done with the icons in their paddocks on exit.</summary>
public enum ExitChoice
{
    Cancel,

    /// <summary>Leave the icons in their paddock folders; they return with Paddock.</summary>
    KeepInPaddocks,

    /// <summary>Move every icon back to the desktop first.</summary>
    RestoreToDesktop,
}

/// <summary>
/// Asked on Exit when paddocks hold icons. Paddock takes icons off the desktop
/// by moving their files into its own folder, so while it is closed they are
/// not on the desktop — which looks like they vanished unless you say so.
/// </summary>
public partial class ExitPromptWindow : Window
{
    private ExitPromptWindow(int iconCount, string storeFolder)
    {
        InitializeComponent();

        Message.Text = iconCount == 1
            ? $"1 icon is in a paddock. While Paddock is closed it is not on the desktop; it is kept safe in {storeFolder}."
            : $"{iconCount} icons are in your paddocks. While Paddock is closed they are not on the desktop; they are kept safe in {storeFolder}.";

        Loaded += (_, _) =>
        {
            Activate();
            KeepButton.Focus();
        };
    }

    private ExitChoice Choice { get; set; } = ExitChoice.Cancel;

    /// <summary>Shows the prompt and returns the user's choice (Cancel if closed).</summary>
    public static ExitChoice Ask(int iconCount, string storeFolder)
    {
        var window = new ExitPromptWindow(iconCount, storeFolder);
        window.ShowDialog();
        return window.Choice;
    }

    private void Keep_Click(object sender, RoutedEventArgs e) => Choose(ExitChoice.KeepInPaddocks);

    private void Restore_Click(object sender, RoutedEventArgs e) => Choose(ExitChoice.RestoreToDesktop);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Choose(ExitChoice.Cancel);

    private void Choose(ExitChoice choice)
    {
        Choice = choice;
        Close();
    }
}
