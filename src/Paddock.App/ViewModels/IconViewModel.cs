using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Paddock.Core.Models;
using Paddock.Shell;

namespace Paddock.App.ViewModels;

public class IconViewModel : INotifyPropertyChanged
{
    private readonly IconEntry _entry;
    private readonly IconExtractor _iconExtractor;

    public IconViewModel(IconEntry entry)
        : this(entry, ((App)Application.Current).IconExtractor)
    {
    }

    public IconViewModel(IconEntry entry, IconExtractor iconExtractor)
    {
        _entry = entry;
        _iconExtractor = iconExtractor;
    }

    public string DisplayName => Path.GetFileNameWithoutExtension(_entry.DesktopPath);

    public string FullPath => _entry.DesktopPath;

    public ImageSource? IconImage
    {
        get
        {
            return _iconExtractor.GetIcon(_entry.DesktopPath);
        }
    }

    public void Launch()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _entry.DesktopPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch {_entry.DesktopPath}: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
