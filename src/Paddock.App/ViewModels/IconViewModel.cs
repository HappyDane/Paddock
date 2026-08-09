using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
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

    /// <summary>
    /// Label shown under the icon. Extensions are dropped the way the desktop
    /// does it, but a folder called "My.Stuff" keeps its whole name.
    /// </summary>
    public string DisplayName => Directory.Exists(_entry.DesktopPath)
        ? Path.GetFileName(_entry.DesktopPath)
        : Path.GetFileNameWithoutExtension(_entry.DesktopPath);

    public string FullPath => _entry.DesktopPath;

    /// <summary>True when Paddock moved this item off the desktop.</summary>
    public bool IsManaged => _entry.Managed;

    public ImageSource? IconImage => _iconExtractor.GetIcon(_entry.DesktopPath);

    public void Launch()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _entry.DesktopPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(_entry.DesktopPath) ?? string.Empty
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
