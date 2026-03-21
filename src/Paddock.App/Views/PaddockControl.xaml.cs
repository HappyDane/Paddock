using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Paddock.App.ViewModels;

namespace Paddock.App.Views;

public partial class PaddockControl : UserControl
{
    private PaddockViewModel ViewModel => (PaddockViewModel)DataContext;

    public PaddockControl()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.IsLocked)
            return;

        // Start drag move — handled by parent canvas
        ViewModel.BeginDrag(e.GetPosition(this));
    }

    private void Title_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel.BeginRenameTitle();
        e.Handled = true;
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (ViewModel.IsLocked)
            return;

        var newWidth = ActualWidth + e.HorizontalChange;
        var newHeight = ActualHeight + e.VerticalChange;

        if (newWidth >= MinWidth)
            Width = newWidth;
        if (newHeight >= MinHeight)
            Height = newHeight;

        ViewModel.UpdateSize(Width, Height);
    }

    private void IconArea_Drop(object sender, DragEventArgs e)
    {
        ViewModel.HandleIconDrop(e);
    }

    private void IconArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void Icon_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: IconViewModel icon })
        {
            icon.Launch();
        }
    }

    private void Icon_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        if (sender is FrameworkElement element && element.DataContext is IconViewModel icon)
        {
            DragDrop.DoDragDrop(element, icon, DragDropEffects.Move);
        }
    }
}
