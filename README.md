# Paddock

A free, open-source desktop icon organizer for Windows — inspired by Stardock Fences.

Create styled zones on your desktop to group and organize icons. Clean, minimal, and fast.

---

## Features

**Zones** — Create resizable zones ("paddocks") to group your desktop icons. Drag to move, resize from corners, right-click to configure.

**Styling** — Per-zone transparency, background color, and corner radius. Apple-inspired dark and light themes with soft shadows and hover-reveal controls.

**Sorting** — Sort icons within any zone by name, date modified, file type, or file size. Ascending or descending, persisted per zone.

**Titles** — Name your zones. Set title visibility to always visible, hover-only, or hidden. Double-click to rename inline.

**Roll-Up** — Double-click a zone's title bar to collapse it to just the title. Configurable expand trigger: click or hover.

**Drag & Drop** — Drag icons between zones, drag files from Explorer into zones, or drag icons back to the desktop.

**Quick Hide** — Double-click the desktop to hide all zones instantly. Mark specific zones as "always visible" to exclude them.

**Layout Snapshots** — Save your zone layout as a named profile. Switch between profiles, export as `.paddock` files, share with others.

**Intelligent Spacing** — Zones auto-maintain consistent gaps between each other and screen edges. Configurable spacing distance.

**Shell Integration** — Right-click any icon for the native Windows context menu (Open, Open With, Properties, etc.). Real file icons extracted via Shell32.

**Startup** — Optionally runs on Windows startup. Embeds directly into the desktop shell.

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

Settings are stored at `%AppData%/Paddock/settings.json` and include:

- Theme (dark/light)
- Default opacity and corner radius for new zones
- Quick Hide toggle and hotkey
- Snap-to-edge and intelligent spacing
- Roll-up expand mode (click/hover)
- Layout profiles (corrals)
- Auto-sort rules

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
