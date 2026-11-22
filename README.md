# Paddock

A free, open-source desktop icon organizer for Windows — inspired by Stardock Fences.

Create styled zones on your desktop to group and organize icons. Clean, minimal, and fast.

---

## Features

**Zones** — Create resizable zones ("paddocks") to group your desktop icons. Hold the right mouse button on an empty part of the desktop, drag out an area and choose **Create paddock here** — then just type its name. (Or use the tray icon → **New Paddock...**.) Drag the title strip to move, drag the bottom-right grip to resize, right-click for everything else — including **Close Paddock**, which puts its icons back on the desktop.

**Appearance** — Right-click a zone → **Appearance…** for a small panel beside it: a row of soft colours, an opacity slider, icon size (small / medium / large), font (system, compact, serif, mono) and when to show the title. Every change shows immediately; **Reset** goes back to the standard look. Text picks light or dark by itself so labels stay readable on any colour, and transparency applies to the panel only, so icons and labels stay crisp. Menus and tooltips follow the dark or light theme.

**Frosted glass** — Set a zone's background to **Frosted glass** and the wallpaper shows through it, blurred, under the zone's colour — the Windows 11 look. The opacity slider then sets how strong the tint over the glass is. Settings can make it the default for new zones.

**Icon tint & eyedropper** — Give every icon in a zone one hue (blue, teal, mono, …) while each keeps its own light and shade, with a strength slider. The eyedropper at the end of each row of colours picks any colour straight off your screen — click a spot on the wallpaper and the zone (or its icons) takes that colour. Arrow keys nudge it one pixel at a time; Esc cancels.

**Auto-Organize** — Rules put new desktop items into zones by themselves: by file type (`.pdf, .docx`), kind (images, installers, archives…), name (`Screenshot*` or a regular expression), where it came from (downloaded from the internet, or moved out of your Downloads folder), or — for shortcuts — the kind of app it starts (games, creative tools, development, office, browsers, chat, media). Rules run top to bottom, first match wins, and a catch-all can take whatever is left. Ready-made rules (*Installers → Installers*, *PDFs → Documents*, *Images → Screenshots*, *Anything from Downloads → Downloads*, *Game shortcuts → Games*, …) create their zone if you don't have it. **Organize Desktop Now** applies the rules to what is already there, after showing what will move. Open it from the tray, Settings, or a zone's menu → **Auto-Organize…**. An item you drag out of a zone always stays where you put it, and nothing is moved while it is still downloading or being renamed.

**Sorting** — Sort icons within any zone by name, date modified, file type, or file size. Ascending or descending, persisted per zone.

**Titles** — Name your zones. Set title visibility to always visible, hover-only, or hidden. Right-click → Rename edits the title in place.

**Roll-Up** — Double-click a zone's title bar to collapse it to just the title strip. The state is remembered across restarts.

**Drag & Drop** — Drag desktop icons into a zone (one at a time or a whole selection) and they leave the desktop. Drag them between zones, or back out onto the desktop where you drop them.

**Quick Hide** — Double-click an empty spot on the desktop to hide all zones *and* the desktop's own icons; double-click again to bring them back. The tray icon (double-click it) and a hotkey (`Ctrl+F12` by default) do the same. Mark specific zones as "always visible" to exclude them, or turn off hiding the desktop icons in Settings. If Paddock ever stops while they are hidden, the desktop icons come back the next time it starts.

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
- **While Paddock is closed, icons in zones are not on the desktop** — they wait
  in `Desktop\.Paddock` and are back the moment Paddock starts (it starts with
  Windows by default). Tray → **Exit** says so and offers to put every icon back
  on the desktop first; tray → **Open Icons Folder** shows where they are.

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
- Quick Hide toggle and hotkey; double-click desktop to hide; hide the desktop icons too
- Frosted glass for new zones
- Draw on Desktop (right-drag to create a zone; on by default)
- Snap-to-edge and intelligent spacing
- Roll-up expand mode (click/hover)
- Layout profiles (corrals)
- Auto-organize rules and their on/off switch

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
- **Auto-Organize watches your own desktop only.** Shortcuts an installer puts on
  the all-users desktop are not routed (that would mean a permission prompt out of
  nowhere); drag those in by hand. Items that arrived while Paddock was closed
  wait until you choose **Organize Desktop Now**, which likewise covers only your
  own desktop.
- **Frosted glass corners** are cut by a window region, which has no
  anti-aliasing, so the rounded edge of a frosted zone is a touch crisper than a
  solid one's.
- **App categories for shortcuts** come from a built-in list of well-known
  programs and game libraries (Steam, Epic, GOG, Battle.net, Adobe, JetBrains,
  Office, …). An app it does not know is not categorised; a name or folder rule
  can still route it.

---

## Roadmap

See [ROADMAP.md](ROADMAP.md) for the full plan.

**v1.0** — Core organizer (zones, sorting, styling, drag-drop, snapshots, installer)

**v2.0** — Tabbed zone groups, search across zones, wallpaper-aware theming, drag-to-action zones

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
