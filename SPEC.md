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
│ TrayIcon    │ ProfileManager│ WallpaperService   │
│ EditOverlay │ SnapEngine    │ StartupManager     │
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

### Phase 1 — MVP: Basic Paddocks

The minimum viable product that makes the app usable.

#### P1.1 Desktop Integration
- [ ] Embed a transparent WPF overlay window as a child of the desktop (`Progman` / `WorkerW`)
- [ ] Intercept and forward mouse events to the desktop when not interacting with paddocks
- [ ] Run on Windows startup (optional, configurable)
- [ ] System tray icon with context menu (Settings, Exit, Show/Hide paddocks)

#### P1.2 Creating & Managing Paddocks
- [ ] **Create**: Right-click desktop → "New Paddock" → draw a rectangle to define the zone
- [ ] **Resize**: Drag edges/corners to resize
- [ ] **Move**: Drag title bar area to reposition
- [ ] **Delete**: Right-click paddock → "Remove Paddock"
- [ ] **Rename**: Double-click title to edit the paddock label
- [ ] Each paddock has a title bar (top) and a scrollable icon area

#### P1.3 Icon Management
- [ ] Drag desktop icons into a paddock to organize them
- [ ] Drag icons out of a paddock back to the desktop
- [ ] Drag icons between paddocks
- [ ] Auto-arrange icons within a paddock (grid layout)
- [ ] Double-click an icon inside a paddock to launch it (same as desktop)
- [ ] Right-click icon shows the standard Windows shell context menu

#### P1.4 Persistence
- [ ] Save paddock positions, sizes, and contained icons to `settings.json`
- [ ] Restore layout on application start
- [ ] Handle desktop resolution changes gracefully (reflow/clamp paddocks)

#### P1.5 Basic Styling
- [ ] Configurable background color per paddock
- [ ] Configurable background opacity/transparency (0-100%)
- [ ] Rounded corners (configurable radius)
- [ ] Configurable title font color
- [ ] Light and dark default themes

---

### Phase 2 — Polish & Power Features

#### P2.1 Advanced Styling
- [ ] Blur/acrylic background effect (Windows 10/11 Mica/Acrylic)
- [ ] Border color and thickness
- [ ] Custom CSS-like theme files (JSON-based theme definitions)
- [ ] Per-paddock or global theme application
- [ ] Shadow/glow effects on paddock edges
- [ ] Title bar position options (top, bottom, hidden)

#### P2.2 Layout & Snapping
- [ ] Snap paddocks to screen edges and to each other
- [ ] Grid-based positioning (optional snap-to-grid)
- [ ] Auto-layout presets: 2-column, 3-column, sidebar, quadrant
- [ ] Lock paddock positions (prevent accidental moves)

#### P2.3 Profiles (Corrals)
- [ ] Save/load named layout profiles
- [ ] Switch between profiles via tray menu or hotkey
- [ ] Auto-switch profile based on display configuration (e.g., docked vs. laptop)

#### P2.4 Quick Hide
- [ ] Double-click empty desktop to hide/show all paddocks (like Fences)
- [ ] Fade animation on show/hide
- [ ] Configurable hotkey for toggle

#### P2.5 Auto-Organization Rules
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

#### P3.2 Widgets (Stretch Goal)
- [ ] Clock widget paddock
- [ ] Quick-notes paddock (sticky note)
- [ ] System monitor paddock (CPU/RAM)

#### P3.3 Import/Export
- [ ] Export layout + theme as a `.paddock` file for sharing
- [ ] Import community themes/layouts

#### P3.4 Multi-Monitor
- [ ] Independent paddock layouts per monitor
- [ ] Paddocks can span monitors or be constrained to one

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
  "activeCorral": "default",
  "corrals": {
    "default": {
      "paddocks": [
        {
          "id": "a1b2c3",
          "title": "Projects",
          "x": 100,
          "y": 200,
          "width": 400,
          "height": 300,
          "style": {
            "backgroundColor": "#1e1e2e",
            "opacity": 0.85,
            "cornerRadius": 12,
            "titleColor": "#cdd6f4",
            "borderColor": "#45475a",
            "borderThickness": 1
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
│   │   ├── App.xaml                   # Application definition
│   │   ├── App.xaml.cs
│   │   ├── Views/
│   │   │   ├── DesktopOverlay.xaml    # Main transparent overlay window
│   │   │   ├── PaddockControl.xaml    # Individual paddock user control
│   │   │   ├── SettingsWindow.xaml    # Settings dialog
│   │   │   └── EditOverlay.xaml       # Overlay for creating/resizing paddocks
│   │   ├── ViewModels/
│   │   │   ├── DesktopOverlayVM.cs
│   │   │   ├── PaddockViewModel.cs
│   │   │   └── SettingsViewModel.cs
│   │   ├── Converters/                # WPF value converters
│   │   └── Resources/
│   │       ├── Themes/
│   │       │   ├── Light.xaml
│   │       │   └── Dark.xaml
│   │       └── Icons/
│   │
│   ├── Paddock.Core/                  # Core logic (class library)
│   │   ├── Models/
│   │   │   ├── PaddockModel.cs
│   │   │   ├── CorralModel.cs
│   │   │   ├── PaddockStyle.cs
│   │   │   ├── IconEntry.cs
│   │   │   └── AutoSortRule.cs
│   │   ├── Services/
│   │   │   ├── PaddockManager.cs      # CRUD operations for paddocks
│   │   │   ├── LayoutEngine.cs        # Positioning, snapping, grid
│   │   │   ├── ProfileManager.cs      # Save/load corrals
│   │   │   └── SettingsService.cs     # Read/write settings.json
│   │   └── Events/
│   │       └── PaddockEvents.cs       # Event definitions
│   │
│   ├── Paddock.Shell/                 # Windows shell integration (class library)
│   │   ├── DesktopIconService.cs      # Read/move/monitor desktop icons
│   │   ├── ShellHookService.cs        # Hook into shell events
│   │   ├── WallpaperService.cs        # Detect wallpaper changes
│   │   ├── StartupManager.cs          # Manage run-on-startup
│   │   └── NativeMethods.cs           # P/Invoke declarations
│   │
│   └── Paddock.Installer/            # WiX installer project
│       └── Product.wxs
│
├── tests/
│   ├── Paddock.Core.Tests/
│   └── Paddock.Shell.Tests/
│
├── assets/
│   ├── logo.png
│   └── tray-icon.ico
│
├── Paddock.sln                        # Solution file
├── SPEC.md                            # This file
├── README.md
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

### 3. Click-Through Behavior
- The overlay must be click-through on empty areas (pass events to desktop)
- Only intercept clicks on paddock regions
- Use `WS_EX_TRANSPARENT` on the overlay, but selectively handle hit-testing

### 4. Performance
- Desktop icon monitoring should use file system watchers, not polling
- Minimize redraws — only update changed paddocks
- Keep memory footprint low (target < 50MB RAM)

---

## MVP Milestones

| #  | Milestone                        | Description                                    |
|----|----------------------------------|------------------------------------------------|
| M1 | **Transparent Overlay**          | WPF window embedded on desktop, click-through  |
| M2 | **Static Paddock Rendering**     | Render a hardcoded paddock with styling         |
| M3 | **Paddock CRUD**                 | Create, move, resize, delete paddocks           |
| M4 | **Icon Drag & Drop**            | Move desktop icons into/out of paddocks         |
| M5 | **Persistence**                  | Save/restore layout across restarts             |
| M6 | **System Tray & Startup**        | Tray icon, run on startup, settings UI          |
| M7 | **Styling Controls**             | Per-paddock opacity, color, corner radius       |
| M8 | **Installer**                    | Distributable MSI/MSIX package                  |

---

## Non-Goals (Out of Scope)

- Cross-platform support (Windows only)
- Mobile/tablet support
- Cloud sync of layouts
- App store distribution (initially)
- Replacing the Windows taskbar or Start menu
