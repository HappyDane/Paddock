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
- [ ] Embed a transparent WPF overlay window as a child of the desktop (`Progman` / `WorkerW`)
- [ ] Intercept and forward mouse events to the desktop when not interacting with paddocks
- [ ] Run on Windows startup (optional, configurable)
- [ ] System tray icon with context menu (Settings, Exit, Show/Hide paddocks)

#### P1.2 Creating & Managing Paddocks
- [ ] **Create**: Right-click desktop → "New Paddock" → draw a rectangle to define the zone
- [ ] **Resize**: Drag edges/corners to resize
- [ ] **Move**: Drag title area to reposition on canvas
- [ ] **Delete**: Close button (hover-reveal) or right-click → "Remove Paddock"
- [ ] **Rename**: Double-click title to show inline text editor
- [ ] Each paddock has a title area and a scrollable icon grid

#### P1.3 Icon Management
- [ ] **Extract & display** actual file/shortcut icons via Shell32/SHGetFileInfo
- [ ] Drag desktop icons into a paddock to organize them
- [ ] Drag icons out of a paddock back to the desktop
- [ ] Drag icons between paddocks
- [ ] **Sort icons** within a paddock by: Name, Date Modified, File Type, File Size
- [ ] Sort order persisted per paddock (ascending/descending)
- [ ] Auto-arrange icons in grid layout after sort
- [ ] **Pinned positions**: Icons don't scramble on paddock resize (reflow, don't shuffle)
- [ ] Double-click an icon inside a paddock to launch it
- [ ] Right-click icon shows the standard Windows shell context menu

#### P1.4 Persistence
- [ ] Save paddock positions, sizes, and contained icons to `settings.json`
- [ ] Restore layout on application start
- [ ] Handle desktop resolution changes gracefully (reflow/clamp paddocks)

#### P1.5 Per-Paddock Styling
- [ ] Configurable **background color** per paddock (color picker)
- [ ] Configurable **opacity/transparency** per paddock (0-100% slider)
- [ ] **Rounded corners** (configurable radius per paddock)
- [ ] Light and dark default themes
- [ ] New paddocks inherit `DefaultOpacity` and `DefaultCornerRadius` from settings

#### P1.6 Zone Titles
- [ ] **Title visibility mode**: Always visible, Hover-only, Hidden — per paddock
- [ ] Inline rename via double-click
- [ ] Title font inherits from theme (clean, minimal)

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
- [ ] Double-click empty desktop to hide/show all paddocks
- [ ] Fade animation on show/hide
- [ ] Configurable hotkey for toggle
- [ ] **Per-paddock exclusion**: Mark specific paddocks as "always visible" (excluded from Quick Hide)

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
          "width": 400,
          "height": 300,
          "isRolledUp": false,
          "excludeFromQuickHide": false,
          "sortBy": "name",
          "sortAscending": true,
          "style": {
            "backgroundColor": "#1C1C1E",
            "opacity": 0.88,
            "cornerRadius": 16
          },
          "icons": [
            {
              "desktopPath": "C:\\Users\\User\\Desktop\\MyProject.lnk",
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
│   │   ├── Views/
│   │   │   ├── DesktopOverlay.xaml    # Main transparent overlay window
│   │   │   ├── PaddockControl.xaml    # Individual paddock user control
│   │   │   └── SettingsWindow.xaml    # Settings dialog
│   │   ├── ViewModels/
│   │   │   ├── DesktopOverlayViewModel.cs
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
│   │       ├── LayoutEngine.cs        # Positioning, snapping, intelligent spacing
│   │       ├── ProfileManager.cs      # Save/load/switch corrals
│   │       └── SettingsService.cs     # Read/write settings.json
│   │
│   ├── Paddock.Shell/                 # Windows shell integration (class library)
│   │   ├── DesktopIconService.cs      # Read/move/monitor desktop icons
│   │   ├── IconExtractor.cs           # Extract icons from files via Shell32
│   │   ├── ShellHookService.cs        # Embed overlay in desktop shell
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

### 1. Embedding on the Desktop
Windows doesn't make it easy to draw on the desktop. The approach:
- Find the `Progman` window → send `0x052C` message to spawn a `WorkerW`
- Set the WPF overlay as a child of `WorkerW` using `SetParent`
- The overlay must be transparent (`AllowsTransparency=true`, `WindowStyle=None`)

### 2. Desktop Icon Manipulation
- Use `SHGetDesktopFolder` and `IShellFolder` COM interfaces to enumerate desktop items
- Use `Shell32` to get icon positions via `LVM_GETITEMPOSITION`
- Move icons by programmatically setting their position in the ListView
- Monitor for new icons via `FileSystemWatcher` on the Desktop folder

### 3. Icon Extraction
- Use `SHGetFileInfo` with `SHGFI_ICON | SHGFI_LARGEICON` to extract icons
- Convert `HICON` to WPF `ImageSource` via `Imaging.CreateBitmapSourceFromHIcon`
- Cache extracted icons to avoid repeated Shell32 calls

### 4. Click-Through Behavior
- The overlay must be click-through on empty areas (pass events to desktop)
- Only intercept clicks on paddock regions
- Use `WS_EX_TRANSPARENT` on the overlay, but selectively handle hit-testing

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
