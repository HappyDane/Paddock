using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using Paddock.App.ViewModels;
using Paddock.Core.Services;

namespace Paddock.App.Views;

/// <summary>
/// The auto-organize rules. Opened from the tray, from Settings, or from a
/// paddock's menu (new custom rules then fill that paddock). Only one is ever
/// open; asking again brings it forward.
/// </summary>
public partial class AutoOrganizeWindow : Window
{
    private static AutoOrganizeWindow? _open;

    private readonly AutoOrganizeViewModel _viewModel;

    private AutoOrganizeWindow(string? paddockId)
    {
        InitializeComponent();

        _viewModel = new AutoOrganizeViewModel((App)Application.Current, paddockId);
        DataContext = _viewModel;

        foreach (var preset in AutoOrganizeViewModel.Presets)
        {
            var item = new MenuItem { Header = preset.Label, Tag = preset, ToolTip = $"Creates a \"{preset.PaddockTitle}\" paddock if you have none" };
            item.Click += Preset_Click;
            AddRuleMenu.Items.Add(item);
        }

        AddRuleMenu.Items.Add(new Separator());
        var custom = new MenuItem { Header = "Custom rule", FontWeight = FontWeights.SemiBold };
        custom.Click += (_, _) => _viewModel.AddCustom();
        AddRuleMenu.Items.Add(custom);

        // Paddocks may have been created, renamed or closed meanwhile.
        Activated += (_, _) => _viewModel.RefreshPaddocks();
        Closed += (_, _) =>
        {
            _viewModel.SaveNow();
            _open = null;
        };
    }

    /// <summary>Opens the window (or brings it forward), optionally for one paddock.</summary>
    public static void ShowFor(string? paddockId)
    {
        if (_open is { } existing)
        {
            if (paddockId is not null)
                existing._viewModel.DefaultTargetId = paddockId;

            existing.Activate();
            return;
        }

        _open = new AutoOrganizeWindow(paddockId);
        _open.Show();
        _open.Activate();
    }

    private static RuleViewModel? RuleOf(object sender) => (sender as FrameworkElement)?.DataContext as RuleViewModel;

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        AddRuleMenu.PlacementTarget = AddRuleButton;
        AddRuleMenu.Placement = PlacementMode.Top;
        AddRuleMenu.IsOpen = true;
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: RulePreset preset })
            _viewModel.AddPreset(preset);
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (RuleOf(sender) is { } rule)
            _viewModel.Move(rule, -1);
    }

    private void Down_Click(object sender, RoutedEventArgs e)
    {
        if (RuleOf(sender) is { } rule)
            _viewModel.Move(rule, +1);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (RuleOf(sender) is { } rule)
            _viewModel.Remove(rule);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (RuleOf(sender) is not { } rule)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Items that come from this folder",
            InitialDirectory = System.IO.Directory.Exists(rule.Folder) ? rule.Folder : string.Empty,
        };

        if (dialog.ShowDialog(this) == true)
            rule.Folder = dialog.FolderName;
    }

    private async void OrganizeNow_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveNow();

        if (((App)Application.Current).AutoOrganize is not { } organizer)
            return;

        try
        {
            IsEnabled = false;
            await organizer.OrganizeDesktopNowAsync(this);
        }
        catch (Exception ex)
        {
            Log.Error("Organizing the desktop failed.", ex);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
