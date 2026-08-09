using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Paddock.App.ViewModels;
using Paddock.Shell;

namespace Paddock.App.Views;

/// <summary>
/// A single paddock, hosted in its own borderless window.
///
/// Giving every paddock its own window is what makes the desktop behave
/// normally: there is no full-screen overlay swallowing clicks, so the empty
/// parts of the desktop are simply the desktop. The window is pinned to desktop level
/// (above the wallpaper and icons, below every application) by
/// <see cref="ShellHookService"/>, re-asserted on every WM_WINDOWPOSCHANGING so
/// activation can never lift it above other windows.
/// </summary>
public partial class PaddockWindow : Window
{
    private readonly PaddockViewModel _viewModel;
    private readonly ShellHookService _shellHook;
    private HwndSource? _source;

    public PaddockWindow(PaddockViewModel viewModel, ShellHookService shellHook)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _shellHook = shellHook;

        DataContext = viewModel;
        ApplyModelBounds();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public PaddockViewModel ViewModel => _viewModel;

    /// <summary>The paddock's rectangle in screen coordinates (DIPs).</summary>
    public Rect ScreenBounds => new(Left, Top, Width, Height);

    /// <summary>Re-asserts the desktop-level z-order slot.</summary>
    public void PinToDesktop()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            _shellHook.SendToDesktopLevel(handle);
    }

    /// <summary>Moves and resizes the window to match the model.</summary>
    public void ApplyModelBounds()
    {
        Left = _viewModel.X;
        Top = _viewModel.Y;
        Width = _viewModel.Width;
        Height = _viewModel.DisplayHeight;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);

        // Runs before the window is made visible, so WS_EX_TOOLWINDOW is in
        // place before a taskbar button could ever appear. That style — rather
        // than ShowInTaskbar="False" — is what keeps paddocks out of the taskbar
        // and Alt-Tab: WPF implements ShowInTaskbar with a hidden *owner* window,
        // and an owned window is liable to be dragged back up the z-order with
        // its owner, which is exactly what we are trying to prevent.
        _shellHook.SendToDesktopLevel(handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PaddockViewModel.X):
            case nameof(PaddockViewModel.Y):
                Left = _viewModel.X;
                Top = _viewModel.Y;
                break;

            case nameof(PaddockViewModel.Width):
                Width = _viewModel.Width;
                break;

            case nameof(PaddockViewModel.DisplayHeight):
                Height = _viewModel.DisplayHeight;
                break;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == ShellHookService.WindowPosChangingMessage)
        {
            // Let the message continue with a corrected z-order rather than
            // handling it, so WPF still sees the move/resize.
            _shellHook.EnforceDesktopZOrder(lParam);
        }

        return IntPtr.Zero;
    }
}
