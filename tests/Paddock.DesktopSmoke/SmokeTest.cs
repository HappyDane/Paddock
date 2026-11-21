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
    private readonly List<string> _images = new();
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
        Step("Appearance… changes colour, icon size and opacity live, and Reset undoes them", ChangeAppearance);
        Step("Clicking a paddock does not lift it above an application", ClickStaysBelowApps);
        Step("Show Desktop (Win+D) keeps the paddock visible", ShowDesktopKeepsPaddock);
        Step("Dragging an icon out puts it back on the desktop", DragIconOut);
        Step("An all-users desktop shortcut can be grouped", GroupSharedItem);
        Step("Icons and layout survive a restart", Restart);
        Step("Removing the paddock returns its icons to the desktop", RemovePaddock);
        Step("A plain right-click on the desktop still opens Explorer's menu", RightClickStillOpensDesktopMenu);
        Step("Right-dragging on empty desktop offers to create a paddock there", RightDragCreatesPaddock);
        Step("Clicking elsewhere withdraws the offer", ClickingElsewhereWithdrawsOffer);
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

    private void ChangeAppearance()
    {
        var rect = PaddockRect;
        Native.RightClick(new Point(rect.Right - 70, rect.Bottom - 60));

        var menuItem = WaitFor(() => FindMenuItem("Appearance…"), "the paddock menu", TimeSpan.FromSeconds(5));
        Thread.Sleep(300);
        KeepImage("menu", Union(rect, MenuBounds()));
        ((InvokePattern)menuItem.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

        var blue = WaitFor(() => FindAppElement("Blue", ControlType.Button), "the Appearance panel", TimeSpan.FromSeconds(5));
        Thread.Sleep(300);
        var panel = PanelBounds(blue);
        KeepImage("appearance-default", Union(rect, panel));

        ClickElement(blue);
        WaitUntil(() => StyleSetting("backgroundColor") == "#1E4F8A", "the Blue colour to be saved",
            onTimeout: () => $"backgroundColor is {StyleSetting("backgroundColor")}");

        ClickElement(WaitFor(() => FindAppElement("Large", ControlType.RadioButton), "the Large icon size", TimeSpan.FromSeconds(3)));
        WaitUntil(() => FirstIconWidth() >= 98, "the icons to grow to Large", onTimeout: () => $"an icon is {FirstIconWidth():0} wide");
        WaitUntil(() => StyleSetting("iconSize") == "large", "the icon size to be saved");

        var slider = WaitFor(() => FindAppElement("Opacity", ControlType.Slider), "the opacity slider", TimeSpan.FromSeconds(3));
        ((RangeValuePattern)slider.GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(0.5);
        WaitUntil(() => StyleSetting("opacity") == "0.5", "the opacity to be saved", onTimeout: () => $"opacity is {StyleSetting("opacity")}");

        Thread.Sleep(400);
        KeepImage("appearance-changed", Union(PaddockRect, PanelBounds(blue)));

        ClickElement(WaitFor(() => FindAppElement("Reset", ControlType.Button), "the Reset button", TimeSpan.FromSeconds(3)));
        WaitUntil(() => StyleSetting("backgroundColor") == "#1C1C1E" && StyleSetting("iconSize") == "medium" && StyleSetting("opacity") == "0.88",
            "Reset to restore the standard look",
            onTimeout: () => $"style is {StyleSetting("backgroundColor")}, {StyleSetting("iconSize")}, {StyleSetting("opacity")}");
        WaitUntil(() => FirstIconWidth() is > 70 and < 90, "the icons to return to Medium");

        // Clicking away closes the panel, as with any popover.
        Native.Click(BareSpot(300, 620));
        WaitUntil(() => FindAppElement("Blue", ControlType.Button) is null, "the Appearance panel to close");
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

        InvokeMenu("Close Paddock");
        WaitUntil(() => FindPaddockWindow() == IntPtr.Zero, "the paddock window to close");

        foreach (var name in inside)
            WaitUntil(() => File.Exists(Path.Combine(_desktop, name!)), $"'{name}' to return to the desktop");

        Check(!Directory.Exists(PaddockFolder), "The paddock's folder was left behind.");
        Check(JsonNode.Parse(File.ReadAllText(SettingsFile))?["corrals"]?["default"]?["paddocks"]?.AsArray().Count == 0,
            "settings.json still lists the paddock.");
    }

    private void RightClickStillOpensDesktopMenu()
    {
        MinimiseOtherWindows();
        var point = BareSpot(700, 520);

        var explorer = Process.GetProcessesByName("explorer").Select(p => (uint)p.Id).ToHashSet();
        var before = Native.TopLevelWindows().Where(Native.IsWindowVisible).ToHashSet();

        Native.RightClick(point);

        var menu = IntPtr.Zero;
        WaitUntil(() => (menu = Native.TopLevelWindows().FirstOrDefault(h =>
                Native.IsWindowVisible(h) && !before.Contains(h) && explorer.Contains(Native.ProcessOf(h)))) != IntPtr.Zero,
            "Explorer's desktop menu to open", TimeSpan.FromSeconds(5));

        Info($"Explorer opened {Describe(menu)}.");
        Screenshot("desktop-menu");
        Check(FindMenuItem("Create paddock here") is null, "Paddock offered a paddock for a plain right-click.");

        // Close it again. (Windows 11's menu lives in a XAML host window that can
        // stay "visible" once the menu itself is gone, so this is best effort.)
        Native.Press(Native.VK_ESCAPE);
        Thread.Sleep(400);
        Native.Click(BareSpot(300, 620));
        Thread.Sleep(400);
    }

    private void RightDragCreatesPaddock()
    {
        MinimiseOtherWindows();

        // Draw it around one desktop icon: a new paddock takes in what it covers.
        var alpha = IconBounds("Smoke Alpha");
        Info($"'Smoke Alpha' is drawn at {alpha}.");

        var left = Math.Max(2, (int)alpha.Left - 4);
        var top = Math.Max(2, (int)alpha.Top - 6);
        var right = left + 160; // paddocks are at least 140 wide
        var bottom = (int)alpha.Bottom + 6;

        var start = FirstBareSpot(new Point(right, bottom), new Point(right, top));
        var end = start.Y == bottom ? new Point(left, top) : new Point(left, bottom);

        var before = PaddockCount();
        var outside = DesktopIcons()
            .Where(i => i.Current.Name.StartsWith("Smoke ", StringComparison.Ordinal))
            .Where(i => !Centre(i.Current.BoundingRectangle).IsInside(left, top, right, bottom))
            .Select(i => i.Current.Name)
            .ToList();

        Native.Drag(start, end, rightButton: true);

        WaitUntil(() => FindMenuItem("Create paddock here") is not null, "the 'Create paddock here' menu",
            TimeSpan.FromSeconds(5), onTimeout: DescribeAppWindows);
        var item = FindMenuItem("Create paddock here")!;
        Screenshot("create-offer");

        var bounds = item.Current.BoundingRectangle;
        Native.Click(new Point((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2)));

        var created = IntPtr.Zero;
        WaitUntil(() => (created = FindAppWindow("New Paddock")) != IntPtr.Zero, "the new paddock to appear");

        Native.GetWindowRect(created, out var rect);
        Info($"New paddock at {rect}.");
        Check(Math.Abs(rect.Left - left) <= 4 && Math.Abs(rect.Top - top) <= 4
              && Math.Abs(rect.Right - right) <= 4 && Math.Abs(rect.Bottom - bottom) <= 4,
            $"The paddock {rect} does not match the drawn area ({left},{top})-({right},{bottom}).");
        Check(PaddockCount() == before + 1, "settings.json does not list the new paddock.");

        // The icon it was drawn over moves in; icons outside it stay put.
        var folder = Path.Combine(Store, NewestPaddockId());
        WaitUntil(() => File.Exists(Path.Combine(folder, "Smoke Alpha.txt")) && !File.Exists(Path.Combine(_desktop, "Smoke Alpha.txt")),
            "the icon under the new paddock to move into it");
        Check(outside.All(name => DesktopIcon(name) is not null || File.Exists(Path.Combine(_desktop, name + ".txt"))),
            $"Icons outside the new paddock were taken too (folder holds {string.Join(", ", Directory.EnumerateFiles(folder).Select(Path.GetFileName))}).");

        // It then opens ready to be named. Only type once it really has the
        // keyboard: keys sent to the desktop would open whatever icon is selected.
        WaitUntil(() => Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT) == created,
            "the new paddock to take the keyboard for naming", TimeSpan.FromSeconds(5));

        foreach (var key in "DRAWN")
            Native.Press((byte)key);
        Native.Press(Native.VK_RETURN);

        WaitUntil(() => string.Equals(Native.TitleOf(created), "drawn", StringComparison.OrdinalIgnoreCase),
            "the new paddock to take the typed name", onTimeout: () => $"it is titled \"{Native.TitleOf(created)}\"");
        Screenshot("created");
    }

    private static System.Windows.Rect IconBounds(string name)
        => (DesktopIcon(name) ?? throw new SmokeFailure($"'{name}' is not on the desktop.")).Current.BoundingRectangle;

    private static (double X, double Y) Centre(System.Windows.Rect r) => (r.Left + r.Width / 2, r.Top + r.Height / 2);

    private static Point FirstBareSpot(params Point[] candidates)
    {
        foreach (var candidate in candidates)
        {
            try
            {
                return BareSpot(candidate.X, candidate.Y);
            }
            catch (SmokeFailure)
            {
                // Try the next one.
            }
        }

        throw new SmokeFailure($"None of {string.Join(", ", candidates)} is bare desktop.");
    }

    private string NewestPaddockId()
        => JsonNode.Parse(File.ReadAllText(SettingsFile))?["corrals"]?["default"]?["paddocks"]?.AsArray()
               .LastOrDefault()?["id"]?.GetValue<string>()
           ?? throw new SmokeFailure("settings.json lists no paddock.");

    private void ClickingElsewhereWithdrawsOffer()
    {
        MinimiseOtherWindows();
        var start = BareSpot(560, 460);
        var end = new Point(820, 640);
        var before = PaddockCount();

        Native.Drag(start, end, rightButton: true);
        WaitUntil(() => FindMenuItem("Create paddock here") is not null, "the 'Create paddock here' menu",
            TimeSpan.FromSeconds(5), onTimeout: DescribeAppWindows);

        Native.Click(BareSpot(300, 620));

        WaitUntil(() => FindMenuItem("Create paddock here") is null, "the offer to be withdrawn");
        Thread.Sleep(800);
        Check(PaddockCount() == before, "A paddock was created anyway.");
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

    /// <summary>Paddock's visible windows, for a failure message.</summary>
    private string DescribeAppWindows()
    {
        if (_app is null || _app.HasExited)
            return "Paddock is not running";

        var windows = Native.TopLevelWindows()
            .Where(h => Native.ProcessOf(h) == (uint)_app.Id && Native.IsWindowVisible(h))
            .Select(h =>
            {
                Native.GetWindowRect(h, out var r);
                return $"{Native.ClassOf(h)} \"{Native.TitleOf(h)}\" {r}";
            });

        return "Paddock's visible windows: " + string.Join("; ", windows)
               + $". Foreground: {Describe(Native.GetForegroundWindow())}";
    }

    private string? StyleSetting(string name)
    {
        var json = JsonNode.Parse(File.ReadAllText(SettingsFile));
        var paddock = json?["corrals"]?["default"]?["paddocks"]?.AsArray()
            .FirstOrDefault(p => p?["id"]?.GetValue<string>() == PaddockId);
        return paddock?["style"]?[name]?.ToString();
    }

    private double FirstIconWidth()
    {
        try
        {
            var item = AutomationElement.FromHandle(_paddock).FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
            return item?.Current.BoundingRectangle.Width ?? 0;
        }
        catch (ElementNotAvailableException)
        {
            return 0;
        }
    }

    private static void ClickElement(AutomationElement element)
    {
        var bounds = element.Current.BoundingRectangle;
        Native.Click(new Point((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2)));
    }

    /// <summary>The open context menu's window, for a picture of it.</summary>
    private Rectangle MenuBounds()
    {
        var menu = AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, _app!.Id),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu)));
        return menu is null ? Rectangle.Empty : ToRectangle(menu.Current.BoundingRectangle);
    }

    /// <summary>The Appearance panel's window, found from one of its swatches.</summary>
    private static Rectangle PanelBounds(AutomationElement inside)
    {
        var walker = TreeWalker.ControlViewWalker;
        var element = inside;
        while (walker.GetParent(element) is { } parent && parent != AutomationElement.RootElement)
            element = parent;
        return ToRectangle(element.Current.BoundingRectangle);
    }

    private static Rectangle ToRectangle(System.Windows.Rect r)
        => r.IsEmpty ? Rectangle.Empty : new Rectangle((int)r.Left, (int)r.Top, (int)r.Width, (int)r.Height);

    private static Rectangle Union(Native.RECT a, Rectangle b)
    {
        var first = new Rectangle(a.Left, a.Top, a.Width, a.Height);
        var both = b.IsEmpty ? first : Rectangle.Union(first, b);
        both.Inflate(12, 12);
        return both;
    }

    /// <summary>
    /// With PADDOCK_SMOKE_LOG_IMAGES=1, keeps a small JPEG of part of the
    /// screen to print as base64 at the end of the run — for reviewing the look
    /// from places that can read the log but not download the artifacts.
    /// </summary>
    private void KeepImage(string name, Rectangle region)
    {
        if (Environment.GetEnvironmentVariable("PADDOCK_SMOKE_LOG_IMAGES") != "1" || region.IsEmpty)
            return;

        try
        {
            region.Intersect(SystemInformation.VirtualScreen);
            using var shot = new Bitmap(region.Width, region.Height);
            using (var g = Graphics.FromImage(shot))
                g.CopyFromScreen(region.Location, Point.Empty, region.Size);

            var scale = Math.Min(1.0, 360.0 / region.Width);
            using var small = new Bitmap(shot, new Size((int)(region.Width * scale), (int)(region.Height * scale)));
            using var stream = new MemoryStream();
            var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 60L);
            small.Save(stream, jpeg, parameters);
            _images.Add($"IMAGE {name} {Convert.ToBase64String(stream.ToArray())}");
        }
        catch (Exception ex)
        {
            Info($"(image '{name}' failed: {ex.Message})");
        }
    }

    private int PaddockCount()
        => JsonNode.Parse(File.ReadAllText(SettingsFile))?["corrals"]?["default"]?["paddocks"]?.AsArray().Count ?? 0;

    private IntPtr FindAppWindow(string title)
        => _app is null || _app.HasExited
            ? IntPtr.Zero
            : Native.TopLevelWindows().FirstOrDefault(hwnd =>
                Native.ProcessOf(hwnd) == (uint)_app.Id && Native.IsWindowVisible(hwnd) && Native.TitleOf(hwnd) == title);

    /// <summary>A point of bare desktop: the icon view itself, with no icon or window over it.</summary>
    private static Point BareSpot(int x, int y)
    {
        var point = new Point(x, y);
        var hit = Native.WindowFromPoint(new Native.POINT { X = x, Y = y });
        Check(Native.ClassOf(hit) == "SysListView32", $"{point} is not bare desktop ({Describe(Native.RootWindowAt(point))}).");
        Check(!DesktopIcons().Any(i => i.Current.BoundingRectangle.Contains(x, y)), $"{point} is on a desktop icon.");
        return point;
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

    private AutomationElement? FindMenuItem(string name) => FindAppElement(name, ControlType.MenuItem);

    /// <summary>An on-screen element of Paddock's, in any of its windows (menus and popovers included).</summary>
    private AutomationElement? FindAppElement(string name, ControlType type)
    {
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, type),
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

        foreach (var image in _images)
            Console.WriteLine(image);

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

internal static class PointExtensions
{
    internal static bool IsInside(this (double X, double Y) point, int left, int top, int right, int bottom)
        => point.X >= left && point.X < right && point.Y >= top && point.Y < bottom;
}
