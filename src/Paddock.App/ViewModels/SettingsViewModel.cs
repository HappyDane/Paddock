using System.ComponentModel;
using System.Runtime.CompilerServices;
using Paddock.Core.Services;

namespace Paddock.App.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings;

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
    }

    public bool StartWithWindows
    {
        get => _settings.Settings.StartWithWindows;
        set { _settings.Settings.StartWithWindows = value; _settings.Save(); OnPropertyChanged(); }
    }

    public string GlobalTheme
    {
        get => _settings.Settings.GlobalTheme;
        set { _settings.Settings.GlobalTheme = value; _settings.Save(); OnPropertyChanged(); }
    }

    public bool QuickHideEnabled
    {
        get => _settings.Settings.QuickHideEnabled;
        set { _settings.Settings.QuickHideEnabled = value; _settings.Save(); OnPropertyChanged(); }
    }

    public bool SnapEnabled
    {
        get => _settings.Settings.SnapEnabled;
        set { _settings.Settings.SnapEnabled = value; _settings.Save(); OnPropertyChanged(); }
    }

    public double IntelligentSpacing
    {
        get => _settings.Settings.IntelligentSpacing;
        set { _settings.Settings.IntelligentSpacing = value; _settings.Save(); OnPropertyChanged(); }
    }

    public double DefaultOpacity
    {
        get => _settings.Settings.DefaultOpacity;
        set { _settings.Settings.DefaultOpacity = value; _settings.Save(); OnPropertyChanged(); }
    }

    public double DefaultCornerRadius
    {
        get => _settings.Settings.DefaultCornerRadius;
        set { _settings.Settings.DefaultCornerRadius = value; _settings.Save(); OnPropertyChanged(); }
    }

    public string RollUpExpandMode
    {
        get => _settings.Settings.RollUpExpandMode;
        set { _settings.Settings.RollUpExpandMode = value; _settings.Save(); OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
