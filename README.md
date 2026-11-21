# Paddock

A free, open-source desktop icon organizer for Windows — inspired by Stardock Fences.

Create styled zones on your desktop to group and organize icons. Clean, minimal, and fast.

---

## Features

**Zones** — Create resizable zones ("paddocks") to group your desktop icons. Hold the right mouse button on an empty part of the desktop, drag out an area and choose **Create paddock here** — then just type its name. (Or use the tray icon → **New Paddock...**.) Drag the title strip to move, drag the bottom-right grip to resize, right-click to configure.

**Styling** — Per-zone transparency, background color, and corner radius. Apple-inspired dark and light themes with a hairline edge and hover-reveal controls. Transparency applies to the panel, so icons and labels stay crisp on top of it.

**Sorting** — Sort icons within any zone by name, date modified, file type, or file size. Ascending or descending, persisted per zone.

**Titles** — Name your zones. Set title visibility to always visible, hover-only, or hidden. Right-click → Rename edits the title in place.

**Roll-Up** — Double-click a zone's title bar to collapse it to just the title strip. The state is remembered across restarts.

**Drag & Drop** — Drag desktop icons into a zone (one at a time or a whole selection) and they leave the desktop. Drag them between zones, or back out onto the desktop where you drop them.

**Quick Hide** — Hide and show all zones from the tray icon (double-click it) or with a hotkey (`Ctrl+F12` by default). Mark specific zones as "always visible" to exclude them.

**Layout Snapshots** — Save your zone layout as a named profile. Switch between profiles, export as `.paddock` files, share with others.

**Intelligent Spacing** — Zones auto-maintain consistent gaps between each other and screen edges. Configurable spacing distance.

**Shell Integration** — Right-click any icon for the native Windows context menu (Open, Open With, Properties, etc.). Real file icons extracted via Shell32.

**Lives on the desktop** — Zones sit above the wallpaper and desktop icons but always below every application window: they never cover what you are working on, even when you click them. Show Desktop (Win+D) brings them along with the desktop. The bare desktop keeps behaving normally — right-click, rubber-band selection and icon dragging are untouched, because Paddock puts no overlay there.

**Startup** — Optionally runs on Windows startup, and only ever one copy at a time.

**Stays responsive** — Icons are pulled from the shell on a background thread, so
a zone full of network shortcuts opens instantly instead of freezing; bulk moves
are written to disk once rather than once per file.

---

## How icons work

Windows only draws the *top level* of your Desktop folder, so the only reliable
way to take an icon off the desktop is to move the file. Paddock does exactly
that, and keeps it close to home:

- Each zone owns a folder at `%USERPROFILE%\Desktop\.Paddock\<zone-id>` (hidden,
  so it is not itself a desktop icon).
- Dropping a desktop item into a zone **moves** it there — that is why the icon
  disappears from the desktop.
- Dragging it out onto the desktop puts it back where you drop it; removing the
  zone **moves everything straight back**. Nothing is ever deleted, and no name
  is ever overwritten (`file (2).txt`).
- Shortcuts that Windows shows on *every* account's desktop
  (`C:\Users\Public\Desktop` — where most installers put theirs) are copied into
  the zone and the original is removed. That folder belongs to the whole PC, so
  Windows asks for administrator permission once per drop; say no and the icon
  is only *referenced* in the zone (it stays on the desktop too). Taken out of a
  zone again, such a shortcut lands on your own desktop.
- Items dragged in from somewhere other than the desktop (a folder in Explorer)
  are only *referenced* — Paddock never relocates files from outside your desktop,
  and dragging such an item out only ever copies it.
- Tray menu → **Restore All Icons to Desktop** empties every zone in one go.

Because the files really are on disk, zones survive restarts, and anything you
drop into a zone's folder from Explorer shows up in the zone.

### Getting icons into a zone

Draw a zone over icons that are already on the desktop and they go straight into
it: right-drag from an empty spot so the area covers them, then **Create paddock
here**. (Icons count as covered when their centre is inside the zone.) After
that, drag more icons in, or use the zone's right-click menu → **Fill from
Desktop**, which can move everything or just one kind of thing: shortcuts,
folders, documents, images, media, archives, or everything else. The count is
always confirmed before anything moves.

---

## Quick Start

### Download
Every push builds a ready-to-run `Paddock.exe` (self-contained — no .NET
install needed). Open the latest successful run of the
[**windows** workflow](https://github.com/HappyDane/Paddock/actions/workflows/windows.yml),
download the **Paddock-win-x64** artifact, unzip it and run `Paddock.exe`.
Windows SmartScreen may warn about an unsigned app the first time: *More info →
Run anyway*. Then hold the right mouse button on an empty part of the desktop,
drag out an area and pick **Create paddock here** (or use the tray icon →
**New Paddock...**).

The same run also contains `desktop-smoke-*` artifacts: screenshots of the
end-to-end test driving that exact exe on Windows Server 2022 and 2025.

### Requirements (to build)
- Windows 10 (1903+) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build & Run
```bash
git clone https://github.com/HappyDane/Paddock.git
cd Paddock
dotnet build
dotnet run --project src/Paddock.App
```

### Publish (single exe)
```bash
dotnet publish src/Paddock.App -c Release -p:PublishProfile=win-x64
```
Output: a single self-contained `Paddock.exe` — no .NET runtime required on the target machine.

---

## Project Structure

```
src/
  Paddock.App/       WPF application (Views, ViewModels, Themes)
  Paddock.Core/      Core logic (Models, Services) — no UI dependency
  Paddock.Shell/     Windows shell integration (P/Invoke, COM interop)
  Paddock.Installer/ MSIX packaging project
tests/
  Paddock.Core.Tests/   Unit tests (xUnit)
  Paddock.DesktopSmoke/ End-to-end test of the real exe on a Windows desktop (runs in CI)
```

---

## Configuration

Settings are stored at `%AppData%/Paddock/settings.json` — reachable from the
tray menu via **Open Settings Folder**, which is also where `paddock.log` lives
if you ever need to see what the app thought it was doing. Settings include:

- Theme (dark/light)
- Default opacity and corner radius for new zones
- Quick Hide toggle and hotkey
- Draw on Desktop (right-drag to create a zone; on by default)
- Snap-to-edge and intelligent spacing
- Roll-up expand mode (click/hover)
- Layout profiles (corrals)
- Auto-sort rules

---

## Known limitations

- **All-users desktop items** (`C:\Users\Public\Desktop`) need administrator
  permission to take off the desktop, and taking them off removes them for every
  account on the PC. Folders there are only ever referenced.
- **Zones cover the desktop area they occupy.** Desktop icons behind a zone are
  hidden by it, exactly as a real fence would be.
- **Per-monitor DPI**: Paddock is system-DPI aware, so on a mixed-DPI setup zones
  are sized from the primary monitor's scale factor.
- **No multi-select inside a zone** yet — icons are dragged and launched one at a
  time, and ordering comes from the zone's sort setting rather than free placement.

---

## Roadmap

See [ROADMAP.md](ROADMAP.md) for the full plan.

**v1.0** — Core organizer (zones, sorting, styling, drag-drop, snapshots, installer)

**v2.0** — Tabbed zone groups, search across zones, wallpaper-aware theming, drag-to-action zones, auto-organization rules

**v3.0** — Peek mode, folder portals, multi-monitor, desktop pages

---

## Tech Stack

| | |
|-|-|
| Language | C# 12 / .NET 8 |
| UI | WPF |
| Shell | Win32 P/Invoke + COM |
| Tests | xUnit |
| Installer | MSIX + single-file exe |

---

## License

MIT
