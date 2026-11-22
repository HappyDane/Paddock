# Paddock - Development Guide

## What is this?
Paddock is a Windows desktop icon organizer (similar to Stardock Fences).
It creates styled zones ("paddocks") on the desktop to group and organize icons.

## Tech Stack
- **C# 12 / .NET 8** — LTS release
- **WPF** — UI framework (Windows-only)
- **Win32 P/Invoke** — Desktop shell integration
- **xUnit + Moq** — Testing
- **MSIX** — Installer (Windows Application Packaging Project)

## Project Structure
```
src/
  Paddock.App/       → WPF app (entry point, Views, ViewModels, Resources)
  Paddock.Core/      → Core logic (Models, Services) — no UI dependencies
  Paddock.Shell/     → Windows shell integration (P/Invoke, COM interop)
  Paddock.Installer/ → MSIX packaging project (Windows Application Packaging)
tests/
  Paddock.Core.Tests/   → Unit tests for core logic
  Paddock.DesktopSmoke/ → End-to-end test of the real exe on a Windows desktop (CI)
```

## Architecture Rules
- **Paddock.Core** must NOT reference WPF or Windows-specific APIs
- **Paddock.Shell** handles all Win32/COM interop, exposes clean C# interfaces
- **Paddock.App** references both Core and Shell, wires everything together
- Follow MVVM pattern: Views (XAML) bind to ViewModels, ViewModels use Services

## How the desktop integration works (read before touching it)
- **One window per paddock.** There is deliberately no full-screen overlay: empty
  desktop space belongs to the shell, so right-click, rubber-band selection and
  icon dragging keep working. The only full-screen windows are transient:
  `DrawAreaWindow` (drawing a new paddock from the tray) and `EyedropperWindow`
  (picking a colour — it shows a snapshot taken just before it opened, so it is
  never in its own picture).
- **Z-order, not parenting.** Do *not* `SetParent` into `WorkerW` — a window there
  renders behind the desktop icons and gets no mouse input. `ShellHookService`
  instead keeps each window *directly above* the window hosting `SHELLDLL_DefView`,
  wherever that window is: Show Desktop (Win+D) raises it above every app (a
  full-screen WorkerW on the Windows 10-era desktop, Progman on 24H2), so
  `HWND_BOTTOM` or "just below the host" hides paddocks then. Remember that
  `SetWindowPos` puts a window *below* its `hWndInsertAfter`. The slot is
  re-asserted from `WM_WINDOWPOSCHANGING` (see `PaddockWindow.WndProc`), on every
  foreground change, and by a periodic re-pin in `PaddockHost`.
- **Icons leave the desktop by moving the file.** `IconStore` moves items into
  `%USERPROFILE%\Desktop\.Paddock\<paddock-id>` and back out again; the shell only
  draws the top level of the Desktop folder. Nothing is deleted, names are never
  overwritten, and only items on the user's own desktop are ever moved — except
  files on the all-users desktop, which are *copied* in and the original removed
  via `ISharedDesktopRemover` (an elevated helper run of Paddock.exe, one
  permission prompt per batch; declining falls back to referencing). While
  Paddock is closed those icons are not on the desktop; tray → Exit says so
  (`ExitPromptWindow`) and offers Restore All first. Logoff never asks.
- **Right-drag on the desktop draws a paddock** without Paddock owning the
  desktop: `DesktopGestureService` runs a low-level mouse hook on its *own*
  thread (a hook on a busy UI thread stalls every mouse event in the session).
  It swallows the right button only when it goes down on bare desktop
  (`DesktopListView.IsBareDesktop`: Explorer's icon view, `LVM_HITTEST` says no
  icon), and replays a plain right-click so Explorer's desktop menu still opens.
  The state machine is `Core/DesktopDrawGesture` (unit-tested). Left-drag stays
  the desktop's rubber-band selection. Keep the hook callback fast.
- **Double-click on empty desktop hides paddocks** — and, by default, the
  desktop's own icons (Explorer's View → Show desktop icons, `WM_COMMAND 0x7402`
  to `SHELLDLL_DefView`). The same hook *watches* left presses (never swallows
  them): `Core/DesktopDoubleClick` applies Windows' double-click rule using only
  a window-class check in the callback; the cross-process "was it on an icon?"
  `LVM_HITTEST` runs afterwards on the hook thread's own message loop. While the
  icons are hidden, `settings.json` says so (`desktopIconsHiddenByPaddock`), and
  the next start shows them again — a crash must never leave a desktop empty.
- **A new paddock collects the icons it was drawn over.** Positions and real
  file paths come from Explorer's own folder view (`DesktopFolderView`:
  IShellWindows → desktop IShellBrowser → IFolderView), never from icon labels,
  which hide extensions. Cross-process COM: call it off the UI thread. Which
  icons count is `Core/DesktopLayout.ItemsUnder` (centre inside; never
  Paddock's own store folder).
- **Auto-organize rules route new desktop items** (`Core/AutoSortRules`, first
  match wins). `Shell/DesktopArrivalWatcher` is one *recursive* watcher on the
  Desktop, which covers `.Paddock` too. A move out of a paddock reaches it as
  "removed from the store, added to the desktop", so what leaves the store is
  remembered and an item the user drags out is **never** sent back; so are
  items Paddock returns (`IconStore.PlacedOnDesktop`) and anything dropped
  during a drag out of a paddock (`ExpectItemsFromPaddock`, copies and new
  shortcuts included). An item taking the name of one that just left the
  desktop is an app *saving* (Office renames a temp file over the original),
  not a new item. Verdicts are kept on the pending item, not re-derived later.
  An item is only judged after 1.5 s of quiet, when it can be opened for
  DELETE (not still downloading/copying), is not an empty placeholder, and
  nobody is renaming an item in a shell view. Rules only ever *move* items on
  the user's own desktop (`PaddockManager.MoveDesktopItemsIntoPaddock`).
- **Drops are read in the OLE callback, applied after it.** And after dragging an
  icon *out*, Paddock never moves the file itself: the drop target (Explorer)
  does, and the store watcher notices. Moving it too races Explorer.
- **Coordinates** in `PaddockModel` are screen coordinates in device-independent
  pixels (WPF `Window.Left`/`Top`). Use `ScreenHelper` to convert from Win32
  pixels and to find the right monitor.

## Build & Run
```bash
dotnet build                          # Build all projects
dotnet run --project src/Paddock.App  # Run the app (Windows only)
dotnet test                           # Run all tests
```

### Testing on a real Windows desktop
`.github/workflows/windows.yml` runs on every push: unit tests and the
single-file `Paddock.exe` (downloadable artifact `Paddock-win-x64`), then
`tests/Paddock.DesktopSmoke` against that exe on Windows Server 2022 and 2025.
The smoke test uses real mouse input and UI Automation to drag desktop icons
in and out, sort, click under an app window, press Win+D, double-click the
desktop to hide and show, pick a wallpaper pixel with the eyedropper, turn on
frosted glass and a tint, let an auto-organize rule route a new file (and not
an Office-style save, nor an item dragged back out), restart and remove a
paddock, checking files, `settings.json`, what is on screen, and `paddock.log`.
Screenshots of every step (and of every failure) are uploaded as artifacts.
Run it locally only on a throwaway machine: it moves desktop files and wipes
Paddock's settings, and refuses to start without `PADDOCK_SMOKE_TEST=1`.
Change anything about windows, z-order or drag-and-drop → make sure it passes.

### Building from a non-Windows agent/CI box
The projects target `net8.0-windows`, so add `-p:EnableWindowsTargeting=true`.
The SDK must include `Sdks/Microsoft.NET.Sdk.WindowsDesktop` (the official
Microsoft SDK build does; Ubuntu's `dotnet-sdk-8.0` package strips it). The app
cannot *run* off Windows, but everything compiles and `Paddock.Core.Tests` runs.

## Publish

### Self-contained single-file exe (primary)
```bash
dotnet publish src/Paddock.App -c Release -p:PublishProfile=win-x64
```
Output: `artifacts/publish/win-x64/Paddock.exe` — a single self-contained executable.

### MSIX package (secondary)
Build the `Paddock.Installer` project in Visual Studio to produce an MSIX package.
Requires the Windows Application Packaging Project tooling. It is deliberately
*not* part of the solution's default build: the dotnet CLI cannot build a
`.wapproj`, and having it there broke every `dotnet build -c Release`.

## Key Patterns
- **Settings**: JSON file at `%AppData%/Paddock/settings.json` (see `AppPaths`).
  Wrap bulk changes in `PaddockManager.DeferSave()` so they cost one write.
- **Diagnostics**: use `Log` (Core), not `Debug.WriteLine` — the latter compiles
  out of Release, which is exactly when you need to know what happened. The log
  is at `%AppData%/Paddock/paddock.log`, reachable from the tray menu.
- **Icons load off-thread**: `IconExtractor.RequestIcon` queues work on a
  dedicated STA thread and hands back a frozen `BitmapSource`. Never call the
  blocking `GetIcon` from the UI thread — resolving a `.lnk` can hit the network.
- **Naming**: PascalCase for public members, _camelCase for private fields
- **Nullability**: Enabled project-wide — no nullable warnings allowed
- **File-scoped namespaces**: Use `namespace Foo;` not `namespace Foo { }`

## Common Tasks

### Adding a new paddock property
1. Add to `PaddockModel` and/or `PaddockStyle` in Core
2. Add to `PaddockViewModel` with proper change notification
3. Bind in `PaddockControl.xaml`
4. Include in serialization (settings.json schema)

### Adding a new Win32 integration
1. Add P/Invoke signature in `Shell/NativeMethods.cs` (it stays `internal`)
2. Create or extend a service in `Shell/` with a clean interface
3. Register the service in `App.xaml.cs`

### Changing how paddocks look
Per-paddock look lives in `PaddockStyle` (colour, opacity, `FrostedGlass`,
`IconTint`/`IconTintStrength`, `IconSize`, `Font`) and is edited in
`Views/AppearancePanel` (a popover opened from the paddock's menu). Frosted
glass is the compositor's blur behind the window (`WindowStyles.SetBlurBehind`)
plus a rounded window region so the blur follows the corners — `PaddockWindow`
re-applies the region on every resize. Icon tint is the W3C "color" blend
(`Core/IconTint`, unit-tested) applied to a copy of each icon bitmap
(`Helpers/TintedIcons`, cached per bitmap). Text colour is derived, never stored: `ColorContrast.PrefersDarkText`
picks light or dark labels for the paddock's colour and opacity. App-wide menu
and tooltip styling is `Resources/Controls.xaml`, coloured by the active theme
dictionary — keep both themes' keys in step. There is deliberately no close
button on a paddock: **Close Paddock** is in its right-click menu.

### Changing anything about a paddock's window
`PaddockHost` creates and owns `PaddockWindow`s; `PaddockControl` handles the
mouse and applies geometry to its host window. Keep `Topmost` false and keep the
`WM_WINDOWPOSCHANGING` hook — without it, clicking a paddock lifts it above the
user's applications, which is the single most annoying thing this app can do.

## Spec
See [SPEC.md](SPEC.md) for full feature spec, data model, and roadmap.
