# Paddock - Desktop Icon Organization for Windows

## Vision

Paddock is a free, open-source Windows desktop organizer inspired by Stardock Fences.
It lets users create customizable zones ("paddocks") on their desktop to group and
organize icons — bringing order to cluttered desktops with style.

---

## Core Concepts

| Term       | Description                                                      |
|------------|------------------------------------------------------------------|
| **Paddock** | A rectangular zone on the desktop that contains grouped icons   |
| **Corral**  | A saved layout/profile of multiple paddocks                     |
| **Pasture** | The desktop background area outside of paddocks                 |

---

## Tech Stack

| Layer              | Technology                                  |
|--------------------|---------------------------------------------|
| Language           | C# 12                                       |
| Framework          | WPF (.NET 8)                                |
| Desktop Integration| Win32 API (P/Invoke) + Windows Shell         |
| Installer          | WiX Toolset (MSI) or MSIX                   |
| Settings Storage   | JSON file (`%AppData%/Paddock/settings.json`)|
| Build              | dotnet CLI / MSBuild                         |
| Testing            | xUnit + Moq                                 |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────┐
│                  Paddock.App                     │
│              (WPF Application)                   │
├─────────────┬───────────────┬───────────────────┤
│   UI Layer  │  Core Logic   │  Shell Integration │
│             │               │                    │
│ PaddockView │ PaddockManager│ DesktopIconService │
│ SettingsUI  │ LayoutEngine  │ ShellHookService   │
│ TrayIcon    │ ProfileManager│ IconExtractor      │
│ EditOverlay │ SortEngine    │ StartupManager     │
└─────────────┴───────────────┴───────────────────┘
        │               │               │
        └───────────────┼───────────────┘
                        ▼
              ┌───────────────────┐
              │   Settings Store  │
              │  (JSON on disk)   │
              └───────────────────┘
```

---

## Feature Spec

### Phase 1 — MVP: Core Paddocks

The minimum viable product that makes the app usable.

#### P1.1 Desktop Integration
- [x] Paddocks live at desktop level: above the wallpaper and icons, below every app window
- [x] Empty desktop space is left untouched, so desktop mouse behaviour is unchanged
- [x] Run on Windows startup (optional, configurable)
- [x] System tray icon with context menu (New Paddock, Show/Hide, Restore icons, Settings, Exit)

#### P1.2 Creating & Managing Paddocks
- [x] **Create**: Tray → "New Paddock…" → drag a rectangle on the desktop (Esc cancels)
- [x] **Resize**: Drag the bottom-right grip
- [x] **Move**: Drag the title area, with snapping to monitor edges and neighbours
- [x] **Delete**: Close button (hover-reveal) or right-click → "Remove Paddock"
- [x] **Rename**: Right-click → Rename, inline text editor
- [x] Each paddock has a title area and a scrollable icon grid

#### P1.3 Icon Management
- [x] **Extract & display** actual file/shortcut icons via the shell's 48px image list
- [x] Drag desktop icons into a paddock — the file moves, so the icon leaves the desktop
- [x] Drag icons out of a paddock back to the desktop
- [x] Drag icons between paddocks
- [x] Bulk fill: right-click → "Collect Desktop Items"
- [x] **Sort icons** within a paddock by: Name, Date Modified, File Type, File Size
- [x] Sort order persisted per paddock (ascending/descending)
- [x] Auto-arrange icons in grid layout after sort
- [x] Double-click an icon inside a paddock to launch it
- [x] Right-click icon shows the standard Windows shell context menu
- [ ] Collect the icons that were sitting underneath a newly drawn paddock
- [ ] Free icon placement inside a paddock (currently a reflowing grid)

#### P1.4 Persistence
- [x] Save paddock positions, sizes, and contained icons to `settings.json`
- [x] Restore layout on application start, reconciled against what is on disk
- [x] Handle resolution / monitor changes gracefully (clamp paddocks per monitor)

#### P1.5 Per-Paddock Styling
- [x] Configurable **background color** per paddock
- [x] Configurable **opacity/transparency** per paddock (applied to the panel, not its contents)
- [x] **Rounded corners** (configurable radius per paddock)
- [x] Light and dark default themes, switchable at runtime
- [x] New paddocks inherit `DefaultOpacity` and `DefaultCornerRadius` from settings
- [ ] Colour picker UI (values are settable in `settings.json` today)

#### P1.6 Zone Titles
- [x] **Title visibility mode**: Always visible, Hover-only, Hidden — per paddock
- [x] Inline rename
- [x] Title font inherits from theme (clean, minimal)

---

### Phase 2 — Polish & Power Features

#### P2.1 Roll-Up (Collapse)
- [ ] Double-click title bar to collapse paddock to just its title bar
- [ ] **Expand trigger option**: Click to expand vs. Hover to expand (configurable)
- [ ] Smooth animation on collapse/expand
- [ ] Collapsed state persisted across restarts

#### P2.2 Layout Snapshots (Corrals)
- [ ] **Save** current layout as a named corral (snapshot)
- [ ] **Restore** any saved corral from tray menu or settings
- [ ] **Quick-switch** between corrals via hotkey or tray menu
- [ ] Auto-switch corral based on monitor configuration (docked vs. laptop)
- [ ] Export/import corrals as `.paddock` files

#### P2.3 Quick Hide
- [x] Configurable global hotkey for toggle (`Ctrl+F12` by default), plus tray double-click
- [x] **Per-paddock exclusion**: Mark specific paddocks as "always visible" (excluded from Quick Hide)
- [ ] Fade animation on show/hide
- ~~Double-click empty desktop to toggle~~ — not possible: Paddock keeps no overlay
  over empty desktop space, so those clicks belong to the shell, not to us

#### P2.4 Layout & Snapping
- [ ] Snap paddocks to screen edges and to each other
- [ ] **Intelligent spacing**: Auto-maintain consistent gaps between paddocks and screen edges
- [ ] Grid-based positioning (optional snap-to-grid)
- [ ] Auto-layout presets: 2-column, 3-column, sidebar, quadrant
- [ ] Lock paddock positions (prevent accidental moves)

#### P2.5 Advanced Styling
- [ ] Blur/acrylic background effect (Windows 10/11 Mica/Acrylic)
- [ ] Custom JSON-based theme files
- [ ] Per-paddock or global theme application
- [ ] Shadow intensity control

#### P2.6 Auto-Organization Rules
- [ ] Rule: auto-sort new desktop icons into a specific paddock by file type
  - e.g., `.pdf` → "Documents" paddock, `.exe` → "Apps" paddock
- [ ] Rule: auto-sort by name pattern (regex or glob)
- [ ] "Catch-all" paddock for uncategorized new icons

---

### Phase 3 — Delight & Extras

#### P3.1 Folder Portals
- [ ] A paddock can mirror the contents of a filesystem folder
- [ ] Changes in the folder are reflected live in the paddock
- [ ] Drag files into a folder-portal paddock to move/copy them there

#### P3.2 Peek Mode
- [ ] Hotkey (Win+Space or configurable) brings all paddocks above open windows
- [ ] Release hotkey to send them back behind windows
- [ ] Taskbar icon trigger as alternative

#### P3.3 Import/Export
- [ ] Export layout + theme as a `.paddock` file for sharing
- [ ] Import community themes/layouts

#### P3.4 Multi-Monitor
- [ ] Independent paddock layouts per monitor
- [ ] Per-monitor-configuration tracking (preserves layouts across dock/undock)
- [ ] Paddocks constrained to their monitor

---

## Data Model

### settings.json

```json
{
  "version": 1,
  "startWithWindows": true,
  "globalTheme": "dark",
  "quickHideEnabled": true,
  "quickHideHotkey": "Ctrl+F12",
  "snapEnabled": true,
  "intelligentSpacing": 12,
  "defaultOpacity": 0.88,
  "defaultCornerRadius": 16,
  "rollUpExpandMode": "click",
  "activeCorral": "default",
  "corrals": {
    "default": {
      "name": "Default",
      "paddocks": [
        {
          "id": "a1b2c3",
          "title": "Projects",
          "titleVisibility": "hover",
          "x": 100,
          "y": 200,
          "_comment": "x/y are screen coordinates in DIPs, relative to the primary monitor",
          "width": 400,
          "height": 300,
          "isRolledUp": false,
          "excludeFromQuickHide": false,
          "sortBy": "name",
          "sortAscending": true,
          "style": {
            "backgroundColor": "#1C1C1E",
            "opacity": 0.88,
            "cornerRadius": 16,
            "borderColor": "#26FFFFFF",
            "borderThickness": 1
          },
          "icons": [
            {
              "desktopPath": "C:\\Users\\User\\Desktop\\.Paddock\\a1b2c3\\MyProject.lnk",
              "managed": true,
              "gridPosition": { "row": 0, "col": 0 }
            }
          ],
          "locked": false
        }
      ]
    }
  },
  "autoSortRules": []
}
```

---

## Project Structure

```
Paddock/
├── src/
│   ├── Paddock.App/                   # WPF application (entry point)
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── Services/
│   │   │   └── PaddockHost.cs         # Owns the live paddock windows
│   │   ├── Views/
│   │   │   ├── PaddockWindow.xaml     # One borderless window per paddock
│   │   │   ├── PaddockControl.xaml    # The paddock's contents + interactions
│   │   │   ├── DrawAreaWindow.xaml    # Transient scrim for drawing a new paddock
│   │   │   └── SettingsWindow.xaml    # Settings dialog
│   │   ├── Helpers/
│   │   │   └── ScreenHelper.cs        # Monitor geometry / DIP conversions
│   │   ├── ViewModels/
│   │   │   ├── PaddockViewModel.cs
│   │   │   ├── IconViewModel.cs
│   │   │   └── SettingsViewModel.cs
│   │   └── Resources/
│   │       └── Themes/
│   │           ├── Light.xaml
│   │           └── Dark.xaml
│   │
│   ├── Paddock.Core/                  # Core logic (class library)
│   │   ├── Models/
│   │   │   ├── PaddockModel.cs
│   │   │   ├── CorralModel.cs
│   │   │   ├── PaddockStyle.cs
│   │   │   ├── IconEntry.cs
│   │   │   ├── AppSettings.cs
│   │   │   └── AutoSortRule.cs
│   │   └── Services/
│   │       ├── PaddockManager.cs      # CRUD + icon sorting
│   │       ├── IconStore.cs           # Moves the files behind icons
│   │       ├── LayoutEngine.cs        # Positioning, snapping, intelligent spacing
│   │       ├── ProfileManager.cs      # Save/load/switch corrals
│   │       └── SettingsService.cs     # Read/write settings.json
│   │
│   ├── Paddock.Shell/                 # Windows shell integration (class library)
│   │   ├── DesktopIconService.cs      # Desktop paths, store folder, watcher
│   │   ├── IconExtractor.cs           # Extract icons from files via Shell32
│   │   ├── ShellHookService.cs        # Pins windows at desktop z-order level
│   │   ├── ShellContextMenu.cs        # Native right-click menu for an item
│   │   ├── HotkeyService.cs           # System-wide hotkeys
│   │   ├── StartupManager.cs          # Manage run-on-startup
│   │   └── NativeMethods.cs           # P/Invoke declarations
│   │
│   └── Paddock.Installer/            # WiX installer project
│       └── Product.wxs
│
├── tests/
│   └── Paddock.Core.Tests/
│
├── assets/
│   ├── logo.png
│   └── tray-icon.ico
│
├── Paddock.sln
├── SPEC.md
├── CLAUDE.md
├── LICENSE
└── .gitignore
```

---

## Key Technical Challenges

### 1. Sitting on the Desktop
The `SetParent`-into-`WorkerW` trick is a dead end for this app: a window living
there is painted *behind* the desktop icons and never receives mouse input.

What Paddock does instead — one borderless window per paddock, each:
- kept a normal top-level window (so input, focus and drag-drop all work),
- marked `WS_EX_TOOLWINDOW` (no Alt-Tab or taskbar entry), never `Topmost`,
- pinned in the z-order directly above the window that hosts the desktop icons
  (`Progman`, or the `WorkerW` holding `SHELLDLL_DefView` when the wallpaper is
  animated), by rewriting `hwndInsertAfter` on `WM_WINDOWPOSCHANGING`.

Windows can raise other applications above a paddock, but nothing can raise a
paddock above them. A periodic re-pin recovers from Explorer restarts.

Deliberately, there is **no** full-screen overlay: empty desktop space is the real
desktop, so right-click, rubber-band selection and icon dragging keep working.

### 2. Desktop Icon Manipulation
Reading and writing positions in Explorer's icon list view (`LVM_GETITEMPOSITION`
and friends) requires cross-process memory and breaks under "Auto arrange".
Paddock takes the deterministic route instead:
- An icon leaves the desktop by **moving the file** into the paddock's folder
  under `%USERPROFILE%\Desktop\.Paddock\<id>`, because the shell only draws the
  top level of the Desktop folder.
- Ejecting an item, or deleting a paddock, moves the file back to the desktop.
- `FileSystemWatcher` on the store keeps paddocks in step with changes made in
  Explorer; `SHChangeNotify` nudges the desktop to redraw after a move.

### 3. Icon Extraction
- Use `SHGetFileInfo` with `SHGFI_ICON | SHGFI_LARGEICON` to extract icons
- Convert `HICON` to WPF `ImageSource` via `Imaging.CreateBitmapSourceFromHIcon`
- Cache extracted icons to avoid repeated Shell32 calls

### 4. Click-Through Behavior
Solved by construction: each paddock is its own window, exactly the size of the
paddock, so there is nothing to click through. No `WS_EX_TRANSPARENT`, no
hit-test forwarding. The only full-screen window is the transient scrim shown
while drawing a new paddock.

### 5. Performance
- Desktop icon monitoring should use file system watchers, not polling
- Minimize redraws — only update changed paddocks
- Cache icon images — extract once, reuse across sessions
- Keep memory footprint low (target < 50MB RAM)

---

## MVP Milestones

| #  | Milestone                        | Description                                    |
|----|----------------------------------|------------------------------------------------|
| M1 | **Transparent Overlay**          | WPF window embedded on desktop, click-through  |
| M2 | **Static Paddock Rendering**     | Render a hardcoded paddock with styling         |
| M3 | **Paddock CRUD**                 | Create, move, resize, delete paddocks           |
| M4 | **Icon Display**                 | Extract and show real file icons                |
| M5 | **Icon Drag & Drop**            | Move desktop icons into/out/between paddocks    |
| M6 | **Icon Sorting**                 | Sort by name, date, type, size per paddock      |
| M7 | **Persistence**                  | Save/restore layout across restarts             |
| M8 | **System Tray & Startup**        | Tray icon, run on startup, settings UI          |
| M9 | **Per-Paddock Styling**          | Opacity, color, corner radius per paddock       |
| M10| **Title Controls**               | Show/hide/rename titles per paddock             |
| M11| **Layout Snapshots**             | Save/restore named corrals                      |
| M12| **Installer**                    | Distributable MSI/MSIX package                  |

---

## Non-Goals (Out of Scope)

- Cross-platform support (Windows only)
- Mobile/tablet support
- Cloud sync of layouts
- App store distribution (initially)
- Replacing the Windows taskbar or Start menu
