# Paddock

A free, open-source desktop icon organizer for Windows — inspired by Stardock Fences.

Create styled zones on your desktop to group and organize icons. Clean, minimal, and fast.

---

## Features

**Zones** — Create resizable zones ("paddocks") to group your desktop icons. Drag the title strip to move, drag the bottom-right grip to resize, right-click to configure.

**Styling** — Per-zone transparency, background color, and corner radius. Apple-inspired dark and light themes with a hairline edge and hover-reveal controls. Transparency applies to the panel, so icons and labels stay crisp on top of it.

**Sorting** — Sort icons within any zone by name, date modified, file type, or file size. Ascending or descending, persisted per zone.

**Titles** — Name your zones. Set title visibility to always visible, hover-only, or hidden. Right-click → Rename edits the title in place.

**Roll-Up** — Double-click a zone's title bar to collapse it to just the title strip. The state is remembered across restarts.

**Drag & Drop** — Drag desktop icons into a zone and they leave the desktop. Drag them between zones, or back out to the desktop again.

**Quick Hide** — Hide and show all zones from the tray icon (double-click it) or with a hotkey (`Ctrl+F12` by default). Mark specific zones as "always visible" to exclude them.

**Layout Snapshots** — Save your zone layout as a named profile. Switch between profiles, export as `.paddock` files, share with others.

**Intelligent Spacing** — Zones auto-maintain consistent gaps between each other and screen edges. Configurable spacing distance.

**Shell Integration** — Right-click any icon for the native Windows context menu (Open, Open With, Properties, etc.). Real file icons extracted via Shell32.

**Lives on the desktop** — Zones sit above the wallpaper and desktop icons but always below every application window: they never cover what you are working on, and they never steal focus. The bare desktop keeps behaving normally — right-click, rubber-band selection and icon dragging are untouched, because Paddock puts no overlay there.

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
- Dragging it out, or removing the zone, **moves it straight back** to the desktop.
  Nothing is ever deleted, and no name is ever overwritten (`file (2).txt`).
- Items dragged in from somewhere other than the desktop (a folder in Explorer)
  are only *referenced* — Paddock never relocates files from outside your desktop.
- Tray menu → **Restore All Icons to Desktop** empties every zone in one go.

Because the files really are on disk, zones survive restarts, and anything you
drop into a zone's folder from Explorer shows up in the zone.

### Getting icons into a zone

Draw a zone on an empty part of the desktop, then either drag icons in, or use
the zone's right-click menu → **Fill from Desktop**, which can move everything
or just one kind of thing: shortcuts, folders, documents, images, media,
archives, or everything else. The count is always confirmed before anything
moves. Paddock does not yet work out which icons were sitting *underneath* a new
zone — see [Known limitations](#known-limitations).

---

## Quick Start

### Requirements
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
  Paddock.Core.Tests/ Unit tests (xUnit)
```

---

## Configuration

Settings are stored at `%AppData%/Paddock/settings.json` — reachable from the
tray menu via **Open Settings Folder**, which is also where `paddock.log` lives
if you ever need to see what the app thought it was doing. Settings include:

- Theme (dark/light)
- Default opacity and corner radius for new zones
- Quick Hide toggle and hotkey
- Snap-to-edge and intelligent spacing
- Roll-up expand mode (click/hover)
- Layout profiles (corrals)
- Auto-sort rules

---

## Known limitations

- **Icons underneath a new zone are not collected automatically.** Reading the
  desktop's icon positions means poking around inside Explorer's list view from
  another process; until that is in, draw zones on free space and use
  **Fill from Desktop** or drag icons in.
- **All-users desktop items** (`C:\Users\Public\Desktop`) are left alone —
  moving them would change the desktop for every account on the machine. Drag
  one into a zone and it is referenced in place, so it stays on the desktop too.
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
