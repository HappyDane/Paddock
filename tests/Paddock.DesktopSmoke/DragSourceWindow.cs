using System.Windows.Forms;

namespace Paddock.DesktopSmoke;

/// <summary>
/// A small window that starts an ordinary file drag the moment it is pressed:
/// the same multi-file drop (CF_HDROP) Explorer hands over when several desktop
/// icons are dragged at once. Explorer's own multi-select cannot be driven the
/// same way on every Windows version (synthesized Ctrl+click, rubber bands and
/// UI Automation each fail on one of them), and the drop is what is under test.
/// </summary>
internal sealed class DragSourceWindow : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _shown = new();
    private Form? _form;

    internal DragSourceWindow(IReadOnlyList<string> files, Rectangle bounds)
    {
        Bounds = bounds;

        _thread = new Thread(() =>
        {
            var form = new Form
            {
                Text = "Smoke Drag Source",
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Bounds = bounds,
                BackColor = Color.SteelBlue,
                ShowInTaskbar = false,
                TopMost = true,
            };

            form.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    form.DoDragDrop(new DataObject(DataFormats.FileDrop, files.ToArray()), DragDropEffects.Copy | DragDropEffects.Move);
            };

            form.Shown += (_, _) => _shown.Set();
            _form = form;
            Application.Run(form);
        })
        {
            IsBackground = true,
            Name = "Smoke drag source",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_shown.Wait(TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("The drag source window did not appear.");

        Thread.Sleep(300);
    }

    internal Rectangle Bounds { get; }

    internal Point Centre => new(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2);

    public void Dispose()
    {
        try
        {
            _form?.BeginInvoke(() => _form.Close());
        }
        catch (InvalidOperationException)
        {
            // Already closed.
        }

        _thread.Join(TimeSpan.FromSeconds(5));
        _shown.Dispose();
    }
}
