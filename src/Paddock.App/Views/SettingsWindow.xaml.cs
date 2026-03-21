using System.Windows;
using Paddock.App.ViewModels;

namespace Paddock.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        DataContext = new SettingsViewModel(app.Settings);
    }
}
