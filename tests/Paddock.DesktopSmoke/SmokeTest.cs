using System.IO;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using Microsoft.Win32;

namespace Paddock.DesktopSmoke;

/// <summary>
/// Drives the real Paddock.exe through what a user does with it — drag desktop
/// icons in, sort them, click around, Show Desktop, drag one out, restart,
/// remove the paddock — and checks the files on disk and what is actually on
/// screen after every step. Each step is independent enough that one failure
/// does not hide the rest; every failure leaves a screenshot behind.
/// </summary>
internal sealed class SmokeTest
{
    private const string PaddockId = "smoke001";
    private const string PaddockTitle = "Smoke Paddock";

    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(10);

    private readonly string _exe;
    private readonly string _output;
    private readonly string _desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private readonly string _sharedDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    private readonly string _dataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paddock");

    private readonly List<string> _failures = new();
    private Process? _app;
    private IntPtr _paddock;
    private int _shot;

    internal SmokeTest(string exe, string output)
    {
        _exe = exe;
        _output = output;
    }

    private string Store => Path.Combine(_desktop, ".Paddock");
    private string PaddockFolder => Path.Combine(Store, PaddockId);
    private string SettingsFile => Path.Combine(_dataFolder, "settings.json");
    private string LogFile => Path.Combine(_dataFolder, "paddock.log");

    internal int Run()
    {
        Directory.CreateDirectory(_output);
        Describe();

        if (!Step("Prepare the desktop and a saved layout", Prepare)
            || !Step("Paddock starts and shows the saved paddock", StartApp))
        {
            Finish();
            return 1;
        }

        Step("The paddock is visible on the desktop", PaddockIsVisible);
        Step("Dragging a desktop icon in moves it into the paddock", DragOneIconIn);
        Step("Dropping two desktop files at once moves both", DragTwoIconsIn);
        Step("The paddock shows its icons sorted by name", () => ExpectOrder("Smoke Alpha", "Smoke Bravo", "Smoke Charlie"));
        Step("Sort By > File Size reorders the icons", SortBySize);
        Step("Sort By > Date Modified reorders the icons", SortByDate);
        Step("Sort By > Ascending reverses the order", ToggleAscending);
        Step("Clicking a paddock does not lift it above an application", ClickStaysBelowApps);
        Step("Show Desktop (Win+D) keeps the paddock visible", ShowDesktopKeepsPaddock);
        Step("Dragging an icon out puts it back on the desktop", DragIconOut);
        Step("An all-users desktop shortcut can be grouped", GroupSharedItem);
        Step("Icons and layout survive a restart", Restart);
        Step("Removing the paddock returns its icons to the desktop", RemovePaddock);
        Step("Paddock logged no errors", LogHasNoErrors);

        Finish();
        return _failures.Count == 0 ? 0 : 1;
    }

    // --- Steps ---

    private void Prepare()
    {
        foreach (var process in Process.GetProcessesByName("Paddock"))
        {
            process.Kill();
            process.WaitForExit(5000);
        }

        if (Directory.Exists(_dataFolder))
            Directory.Delete(_dataFolder, recursive: true);

        if (Directory.Exists(Store))
            Directory.Delete(Store, recursive: true);

        foreach (var stale in Directory.EnumerateFiles(_desktop, "Smoke *"))
            File.Delete(stale);

        TryDeleteSharedTestFile();
        MinimiseConsoleWindows();

        // Distinct sizes and dates, so every sort order is different.
        MakeDesktopFile("Smoke Alpha.txt", 300, new DateTime(2020, 1, 1));
        MakeDesktopFile("Smoke Bravo.txt", 100, new DateTime(2022, 1, 1));
        MakeDesktopFile("Smoke Charlie.txt", 200, new DateTime(2021, 1, 1));

        Directory.CreateDirectory(_dataFolder);
        File.WriteAllText(SettingsFile, $$"""
            {
              "startWithWindows": false,
              "corrals": {
                "default": {
                  "name": "default",
                  "paddocks": [
                    {
                      "id": "{{PaddockId}}",
                      "title": "{{PaddockTitle}}",
                      "titleVisibility": "always",
                      "x": 600, "y": 60, "width": 380, "height": 300,
                      "sortBy": "name", "sortAscending": true
                    }
                  ]
                }
              }
            }
            """);

        Native.RefreshDesktop(_desktop);
        foreach (var name in new[] { "Smoke Alpha", "Smoke Bravo", "Smoke Charlie" })
            WaitUntil(() => DesktopIcon(name) is not null, $"'{name}' to appear on the desktop");
    }

    private void StartApp()
    {
        _app = Process.Start(new ProcessStartInfo(_exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(_exe)!,
        });

        WaitUntil(() => (_paddock = FindPaddockWindow()) != IntPtr.Zero, "the paddock window", TimeSpan.FromSeconds(30));
        Thread.Sleep(1500);
        Check(!_app!.HasExited, "Paddock exited right after starting.");
        Info($"Paddock window 0x{_paddock.ToInt64():X} at {PaddockRect}.");
        Screenshot("started");
    }

    private void PaddockIsVisible()
    {
        var rect = PaddockRect;
        foreach (var point in new[] { new Point(rect.Left + 30, rect.Top + rect.Height / 2), new Point(rect.Right - 40, rect.Top + 10) })
            ExpectPaddockAt(point);
    }

    private void DragOneIconIn()
    {
        Native.Drag(DesktopIconCentre("Smoke Alpha"), PaddockDropPoint);
        ExpectInPaddock("Smoke Alpha.txt");
        WaitUntil(() => PaddockItems().Contains("Smoke Alpha"), "'Smoke Alpha' to show in the paddock");
    }

    private void DragTwoIconsIn()
    {
        var files = new[] { Path.Combine(_desktop, "Smoke Bravo.txt"), Path.Combine(_desktop, "Smoke Charlie.txt") };

        // Placed on the left, clear of the paddock, below the desktop icon column.
        using var source = new DragSourceWindow(files, new Rectangle(240, 470, 90, 90));
        Native.Drag(source.Centre, PaddockDropPoint);

        ExpectInPaddock("Smoke Bravo.txt");
        ExpectInPaddock("Smoke Charlie.txt");
    }

    private void SortBySize()
    {
        InvokeMenu("Sort By", "File Size");
        ExpectOrderOfPresent("Smoke Bravo", "Smoke Charlie", "Smoke Alpha");
        Check(PaddockSetting("sortBy") == "fileSize", $"settings.json says sortBy={PaddockSetting("sortBy")}.");
    }

    private void SortByDate()
    {
        InvokeMenu("Sort By", "Date Modified");
        ExpectOrderOfPresent("Smoke Alpha", "Smoke Charlie", "Smoke Bravo");
    }

    private void ToggleAscending()
    {
        InvokeMenu("Sort By", "Name");
        ExpectOrderOfPresent("Smoke Alpha", "Smoke Bravo", "Smoke Charlie");

        InvokeMenu("Sort By", "Ascending");
        ExpectOrderOfPresent("Smoke Charlie", "Smoke Bravo", "Smoke Alpha");
        Check(PaddockSetting("sortAscending") == "false", "settings.json still says ascending.");

        InvokeMenu("Sort By", "Ascending");
        ExpectOrderOfPresent("Smoke Alpha", "Smoke Bravo", "Smoke Charlie");
    }

    private void ClickStaysBelowApps()
    {
        var rect = PaddockRect;
        var exposed = new Point(rect.Left + 60, rect.Top + 14);
        var covered = new Point(rect.Left + 60, rect.Top + rect.Height * 3 / 4);

        using var appWindow = Process.Start(new ProcessStartInfo(Environment.ProcessPath!,
            $"{AppWindow.Switch} {rect.Left - 40} {rect.Top + rect.Height / 2} {rect.Width + 80} {rect.Height}")
        {
            UseShellExecute = false,
        })!;

        try
        {
            WaitUntil(() => Native.ProcessOf(Native.RootWindowAt(covered)) == (uint)appWindow.Id,
                "the application window to cover the paddock's lower half");

            Native.Click(exposed);
            Thread.Sleep(800);
            Screenshot("after-click");

            var hit = Native.RootWindowAt(covered);
            Check(Native.ProcessOf(hit) == (uint)appWindow.Id,
                $"After clicking the paddock, its lower half is drawn over the application window (hit {Describe(hit)}).");
            ExpectPaddockAt(exposed);
        }
        finally
        {
            appWindow.Kill();
            appWindow.WaitForExit(5000);
        }
    }

    private void ShowDesktopKeepsPaddock()
    {
        Native.Chord(Native.VK_LWIN, (byte)'D');
        Thread.Sleep(2500);
        Screenshot("win-d");

        try
        {
            var rect = PaddockRect;
            ExpectPaddockAt(new Point(rect.Left + 30, rect.Top + rect.Height / 2));
        }
        finally
        {
            Native.Chord(Native.VK_LWIN, (byte)'D');
            Thread.Sleep(1500);
        }
    }

    private void DragIconOut()
    {
        MinimiseOtherWindows();
        var target = BareDesktopPoint();
        Info($"Dropping on bare desktop at {target}.");

        Native.Drag(PaddockItemCentre("Smoke Alpha"), target);

        var onDesktop = Path.Combine(_desktop, "Smoke Alpha.txt");
        WaitUntil(() => File.Exists(onDesktop) && !File.Exists(Path.Combine(PaddockFolder, "Smoke Alpha.txt")),
            "'Smoke Alpha.txt' to move back to the desktop");
        WaitUntil(() => !PaddockItems().Contains("Smoke Alpha"), "'Smoke Alpha' to leave the paddock");

        // Explorer performed the move; Paddock must not have raced it with a second one.
        Thread.Sleep(1500);
        var duplicates = Directory.EnumerateFiles(_desktop, "Smoke Alpha*").Select(Path.GetFileName).ToList();
        Check(duplicates.Count == 1, $"Desktop now holds {string.Join(", ", duplicates)}.");
        Check(File.ReadAllText(onDesktop).Length == 300, "The file that came back is not the original.");
    }

    private void GroupSharedItem()
    {
        var shared = Path.Combine(_sharedDesktop, "Smoke Shared.txt");

        try
        {
            File.WriteAllText(shared, "shared");
        }
        catch (Exception ex)
        {
            Info($"SKIP: cannot write to the all-users desktop here ({ex.Message}).");
            return;
        }

        Native.RefreshDesktop(_sharedDesktop);
        Native.RefreshDesktop(_desktop);
        WaitUntil(() => DesktopIcon("Smoke Shared") is not null, "'Smoke Shared' to appear on the desktop");

        Native.Drag(DesktopIconCentre("Smoke Shared"), PaddockDropPoint);

        WaitUntil(() => File.Exists(Path.Combine(PaddockFolder, "Smoke Shared.txt")) && !File.Exists(shared),
            "the all-users desktop item to move into the paddock", TimeSpan.FromSeconds(20));
        WaitUntil(() => PaddockItems().Contains("Smoke Shared"), "'Smoke Shared' to show in the paddock");
    }

    private void Restart()
    {
        var before = PaddockItems();
        Check(before.Count > 0, "Nothing in the paddock to carry across a restart.");

        _app!.Kill();
        _app.WaitForExit(5000);
        WaitUntil(() => FindPaddockWindow() == IntPtr.Zero, "the paddock window to close");

        foreach (var name in before)
            Check(File.Exists(Path.Combine(PaddockFolder, name + ".txt")), $"{name} is no longer in the paddock folder.");

        StartApp();
        ExpectOrder(before.ToArray());
    }

    private void RemovePaddock()
    {
        var inside = Directory.EnumerateFiles(PaddockFolder).Select(Path.GetFileName).ToList();
        Check(inside.Count > 0, "Nothing in the paddock to return.");

        InvokeMenu("Remove Paddock (icons return to desktop)");
        WaitUntil(() => FindPaddockWindow() == IntPtr.Zero, "the paddock window to close");

        foreach (var name in inside)
            WaitUntil(() => File.Exists(Path.Combine(_desktop, name!)), $"'{name}' to return to the desktop");

        Check(!Directory.Exists(PaddockFolder), "The paddock's folder was left behind.");
        Check(JsonNode.Parse(File.ReadAllText(SettingsFile))?["corrals"]?["default"]?["paddocks"]?.AsArray().Count == 0,
            "settings.json still lists the paddock.");
    }

    private void LogHasNoErrors()
    {
        Check(File.Exists(LogFile), "No paddock.log was written.");

        var log = File.ReadAllText(LogFile);
        Console.WriteLine("----- paddock.log -----");
        Console.WriteLine(log.TrimEnd());
        Console.WriteLine("-----------------------");

        Check(!log.Contains("[ERROR]"), "paddock.log contains errors (printed above).");
    }

    // --- Paddock under test ---

    private Native.RECT PaddockRect
    {
        get
        {
            Native.GetWindowRect(_paddock, out var rect);
            return rect;
        }
    }

    /// <summary>Empty space in the paddock's icon area, below the first row.</summary>
    private Point PaddockDropPoint => new(PaddockRect.Left + PaddockRect.Width / 2, PaddockRect.Top + 200);

    private IntPtr FindPaddockWindow()
    {
        if (_app is null || _app.HasExited)
            return IntPtr.Zero;

        return Native.TopLevelWindows().FirstOrDefault(hwnd =>
            Native.ProcessOf(hwnd) == (uint)_app.Id
            && Native.IsWindowVisible(hwnd)
            && Native.TitleOf(hwnd) == PaddockTitle);
    }

    /// <summary>The names of the paddock's icons, in the order they are drawn.</summary>
    private List<string> PaddockItems()
    {
        try
        {
            var root = AutomationElement.FromHandle(_paddock);
            var items = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));

            return items.Cast<AutomationElement>()
                .Select(e => e.Current.Name)
                .Where(n => n.StartsWith("Smoke ", StringComparison.Ordinal))
                .ToList();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
        {
            return [];
        }
    }

    private Point PaddockItemCentre(string name)
    {
        var root = AutomationElement.FromHandle(_paddock);
        var item = root.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem),
                new PropertyCondition(AutomationElement.NameProperty, name)))
            ?? throw new SmokeFailure($"'{name}' is not in the paddock (it shows: {string.Join(", ", PaddockItems())}).");

        var bounds = item.Current.BoundingRectangle;
        return new Point((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 3));
    }

    private string? PaddockSetting(string name)
    {
        var json = JsonNode.Parse(File.ReadAllText(SettingsFile));
        var paddocks = json?["corrals"]?["default"]?["paddocks"]?.AsArray();
        return paddocks?.FirstOrDefault(p => p?["id"]?.GetValue<string>() == PaddockId)?[name]?.ToString();
    }

    /// <summary>
    /// Opens the paddock's context menu on empty space and follows a path of
    /// menu items through UI Automation — the same menu a right-click shows.
    /// </summary>
    private void InvokeMenu(params string[] path)
    {
        var rect = PaddockRect;
        Native.RightClick(new Point(rect.Right - 70, rect.Bottom - 60));

        for (var i = 0; i < path.Length; i++)
        {
            var item = WaitFor(() => FindMenuItem(path[i]), $"menu item '{path[i]}'", TimeSpan.FromSeconds(5));

            if (i < path.Length - 1)
            {
                ((ExpandCollapsePattern)item.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                Thread.Sleep(300);
                continue;
            }

            if (item.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
            {
                ((InvokePattern)invoke).Invoke();
            }
            else
            {
                var bounds = item.Current.BoundingRectangle;
                Native.Click(new Point((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2)));
            }
        }

        Thread.Sleep(600);
    }

    private AutomationElement? FindMenuItem(string name)
    {
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
            new PropertyCondition(AutomationElement.NameProperty, name));

        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, _app!.Id));

        foreach (AutomationElement window in windows)
        {
            var item = window.FindFirst(TreeScope.Descendants, condition);
            if (item is not null && !item.Current.IsOffscreen)
                return item;
        }

        return null;
    }

    private void ExpectOrder(params string[] names)
    {
        List<string> shown = [];
        WaitUntil(() => (shown = PaddockItems()).SequenceEqual(names),
            $"the paddock to show [{string.Join(", ", names)}]", onTimeout: () => $"it shows [{string.Join(", ", shown)}]");
    }

    /// <summary>
    /// Like <see cref="ExpectOrder"/>, limited to the items that really are in
    /// the paddock — so a sort is still checked when an earlier step failed to
    /// bring one of them in.
    /// </summary>
    private void ExpectOrderOfPresent(params string[] names)
    {
        var present = names.Where(n => File.Exists(Path.Combine(PaddockFolder, n + ".txt"))).ToArray();
        Check(present.Length >= 2, $"Only {present.Length} test icon(s) in the paddock: nothing to sort.");
        ExpectOrder(present);
    }

    /// <summary>
    /// A point where a drop lands on the bare desktop: the icon view itself,
    /// not an icon, a window, or the paddock.
    /// </summary>
    private Point BareDesktopPoint()
    {
        var icons = DesktopIcons().Select(i => i.Current.BoundingRectangle).ToList();
        var paddock = PaddockRect;

        for (var y = 140; y < 640; y += 40)
        {
            for (var x = 520; x >= 160; x -= 40)
            {
                var point = new Point(x, y);
                var hit = Native.WindowFromPoint(new Native.POINT { X = x, Y = y });

                if (Native.ClassOf(hit) == "SysListView32"
                    && !icons.Any(r => r.Contains(x, y))
                    && (x < paddock.Left - 40 || x > paddock.Right + 40 || y > paddock.Bottom + 40))
                {
                    return point;
                }
            }
        }

        throw new SmokeFailure("Found no bare desktop to drop on.");
    }

    private void ExpectInPaddock(string fileName)
        => WaitUntil(() => File.Exists(Path.Combine(PaddockFolder, fileName)) && !File.Exists(Path.Combine(_desktop, fileName)),
            $"'{fileName}' to move off the desktop into the paddock folder");

    private void ExpectPaddockAt(Point point)
    {
        var hit = Native.RootWindowAt(point);
        Check(hit == _paddock, $"At {point} the user sees {Describe(hit)}, not the paddock.");
    }

    // --- Desktop ---

    private static AutomationElement? DesktopIcon(string name)
        => DesktopIcons().FirstOrDefault(e => e.Current.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));

    private static List<AutomationElement> DesktopIcons()
    {
        var list = DesktopListView();
        if (list == IntPtr.Zero)
            return [];

        try
        {
            return AutomationElement.FromHandle(list)
                .FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>()
                .ToList();
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
    }

    private static Point DesktopIconCentre(string name)
    {
        var icon = DesktopIcon(name) ?? throw new SmokeFailure($"'{name}' is not on the desktop.");
        var bounds = icon.Current.BoundingRectangle;
        return new Point((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 3));
    }

    private static IntPtr DesktopListView()
    {
        foreach (var host in new[] { Native.GetShellWindow() }.Concat(WorkerWindows()))
        {
            var view = Native.FindWindowExW(host, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero)
                return Native.FindWindowExW(view, IntPtr.Zero, "SysListView32", null);
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<IntPtr> WorkerWindows()
    {
        var worker = IntPtr.Zero;
        while ((worker = Native.FindWindowExW(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            yield return worker;
    }

    private void MakeDesktopFile(string name, int size, DateTime modified)
    {
        var path = Path.Combine(_desktop, name);
        File.WriteAllText(path, new string('x', size));
        File.SetLastWriteTime(path, modified);
    }

    private void TryDeleteSharedTestFile()
    {
        try
        {
            var shared = Path.Combine(_sharedDesktop, "Smoke Shared.txt");
            if (File.Exists(shared))
                File.Delete(shared);
        }
        catch
        {
            // Not writable here; the shared-desktop step will say so.
        }
    }

    /// <summary>
    /// Anything else that could sit over the desktop — CI runner images leave
    /// the odd dialog open — goes out of the way, if it can be minimised.
    /// </summary>
    private void MinimiseOtherWindows()
    {
        const long WS_MINIMIZEBOX = 0x00020000;
        var mine = new HashSet<uint> { (uint)Environment.ProcessId, (uint)(_app?.Id ?? 0) };

        foreach (var hwnd in Native.TopLevelWindows())
        {
            if (!Native.IsWindowVisible(hwnd) || mine.Contains(Native.ProcessOf(hwnd)))
                continue;

            if (Native.ClassOf(hwnd) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                continue;

            if ((Native.GetWindowStyle(hwnd) & WS_MINIMIZEBOX) != 0)
                Native.ShowWindow(hwnd, Native.SW_MINIMIZE);
        }

        Thread.Sleep(500);
    }

    /// <summary>The runner's own console window covers most of the screen.</summary>
    private static void MinimiseConsoleWindows()
    {
        foreach (var hwnd in Native.TopLevelWindows())
        {
            var cls = Native.ClassOf(hwnd);
            if (Native.IsWindowVisible(hwnd) && cls is "ConsoleWindowClass" or "CASCADIA_HOSTING_WINDOW_CLASS")
                Native.ShowWindow(hwnd, Native.SW_MINIMIZE);
        }

        Thread.Sleep(800);
    }

    // --- Plumbing ---

    private bool Step(string name, Action action)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            action();
            Console.WriteLine($"PASS  {name}  ({watch.Elapsed.TotalSeconds:0.0}s)");
            return true;
        }
        catch (Exception ex)
        {
            var reason = ex is SmokeFailure ? ex.Message : ex.ToString();
            Console.WriteLine($"FAIL  {name}  ({watch.Elapsed.TotalSeconds:0.0}s)");
            Console.WriteLine($"      {reason.Replace(Environment.NewLine, Environment.NewLine + "      ")}");
            _failures.Add(name);
            Screenshot("FAIL-" + new string(name.Where(char.IsLetterOrDigit).ToArray()));
            return false;
        }
    }

    private void Finish()
    {
        try
        {
            if (_app is { HasExited: false })
                _app.Kill();
        }
        catch
        {
            // Already gone.
        }

        Screenshot("end");
        Console.WriteLine();
        Console.WriteLine(_failures.Count == 0
            ? "All desktop smoke checks passed."
            : $"{_failures.Count} desktop smoke check(s) failed: {string.Join("; ", _failures)}");
    }

    private void Describe()
    {
        var lua = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", null);
        Info($"{Environment.OSVersion}; elevated={Program.IsElevated()}; EnableLUA={lua ?? "?"}");
        Info($"Screen {Screen.PrimaryScreen?.Bounds}; desktop {_desktop}; all-users desktop {_sharedDesktop}");
        Info($"Paddock under test: {_exe}");
    }

    private static string Describe(IntPtr hwnd)
        => hwnd == IntPtr.Zero ? "nothing" : $"{Native.ClassOf(hwnd)} \"{Native.TitleOf(hwnd)}\" (pid {Native.ProcessOf(hwnd)})";

    private void Screenshot(string name)
    {
        try
        {
            var bounds = SystemInformation.VirtualScreen;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

            bitmap.Save(Path.Combine(_output, $"{++_shot:00}-{name}.png"), ImageFormat.Png);
        }
        catch (Exception ex)
        {
            Info($"(screenshot '{name}' failed: {ex.Message})");
        }
    }

    private static void Info(string message) => Console.WriteLine($"      {message}");

    private static void Check(bool condition, string failure)
    {
        if (!condition)
            throw new SmokeFailure(failure);
    }

    private static void WaitUntil(Func<bool> condition, string what, TimeSpan? timeout = null, Func<string>? onTimeout = null)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < (timeout ?? DefaultWait))
        {
            if (condition())
                return;

            Thread.Sleep(200);
        }

        throw new SmokeFailure($"Timed out waiting for {what}" + (onTimeout is null ? "." : $"; {onTimeout()}."));
    }

    private static T WaitFor<T>(Func<T?> find, string what, TimeSpan timeout) where T : class
    {
        T? found = null;
        WaitUntil(() => (found = find()) is not null, what, timeout);
        return found!;
    }

    private sealed class SmokeFailure(string message) : Exception(message);
}
