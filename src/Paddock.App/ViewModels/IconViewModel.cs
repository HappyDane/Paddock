using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Paddock.Core.Models;

namespace Paddock.App.ViewModels;

public class IconViewModel : INotifyPropertyChanged
{
    private readonly IconEntry _entry;

    public IconViewModel(IconEntry entry)
    {
        _entry = entry;
    }

    public string DisplayName => Path.GetFileNameWithoutExtension(_entry.DesktopPath);

    public string FullPath => _entry.DesktopPath;

    public ImageSource? IconImage
    {
        get
        {
            // TODO: Extract icon from file using Shell32
            // For now, return null (will show blank)
            return null;
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
