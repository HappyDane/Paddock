# Paddock Roadmap

## v1.0 — Core Desktop Organizer

The foundation. Everything needed to replace a cluttered desktop with organized zones.

### Done (scaffolded)
- [x] Project architecture (Paddock.App / Core / Shell)
- [x] WPF overlay window with transparent background
- [x] Paddock model with persistence (JSON settings)
- [x] System tray integration
- [x] Dark and Light themes (Apple-inspired)
- [x] Icon extraction via Shell32 (SHGetFileInfo)
- [x] Layout engine with snapping and intelligent spacing

### In Progress
- [ ] Desktop shell embedding (WorkerW technique)
- [ ] Paddock drag-to-move on canvas
- [ ] Icon drag in/out/between paddocks
- [ ] Sort icons by name, date, type, size
- [ ] Zone title visibility toggle (always / hover / hidden)
- [ ] Inline rename
- [ ] Roll-up (collapse to title bar)
- [ ] Per-paddock transparency and color
- [ ] Quick Hide with per-zone exclusion
- [ ] Layout snapshots (corrals) — save/restore/switch
- [ ] Intelligent spacing between zones
- [ ] Run-on-startup registry integration

### TODO
- [ ] Draw-to-create paddock (right-click drag rectangle)
- [ ] Shell context menu on right-click icon
- [ ] Click-through on empty overlay areas
- [ ] Desktop resolution change handling
- [ ] MSI/MSIX installer

---

## v2.0 — Power Features

Polish and productivity. The features that make you never want to go back.

### Tabbed Zone Groups
- [ ] Stack multiple paddocks into a tabbed interface
- [ ] Drag zones onto each other to create tab groups
- [ ] Per-tab color accent
- [ ] Drag tabs to reorder or detach

### Search Across All Zones
- [ ] Global search bar (hotkey-triggered, e.g., Ctrl+Shift+F)
- [ ] Searches file names across all paddocks
- [ ] Highlights matching zone and scrolls to icon
- [ ] Filter by file type

### Wallpaper-Aware Auto-Theming
- [ ] Detect current wallpaper and extract dominant colors
- [ ] Auto-generate a complementary theme (background, text, accents)
- [ ] Update live when wallpaper changes
- [ ] Option to override per-paddock

### Per-Zone Icon Size
- [ ] Small (32px), Medium (44px), Large (64px), Extra Large (96px)
- [ ] Configurable per paddock via context menu
- [ ] Grid reflows automatically when size changes

### Pinned Icon Positions
- [ ] Icons maintain their grid position on paddock resize
- [ ] Reflow only when necessary (paddock too narrow)
- [ ] Manual drag-to-reorder within a paddock

### Advanced Styling
- [ ] Acrylic/blur backgrounds (Windows 11 Mica)
- [ ] Custom JSON theme files
- [ ] Background image per paddock
- [ ] Gradient backgrounds
- [ ] Shadow intensity control

### Auto-Organization Rules
- [ ] Auto-sort new desktop files by extension
- [ ] Auto-sort by name pattern (glob/regex)
- [ ] Catch-all paddock for uncategorized items
- [ ] Rule editor in settings

### Drag-to-Action Zones
Special paddocks that trigger actions when files are dropped:
- [ ] **Compress**: Drop files → creates .zip
- [ ] **Email**: Drop files → opens email compose with attachments
- [ ] **Trash**: Drop files → moves to Recycle Bin
- [ ] **Upload**: Drop files → uploads to configured cloud service
- [ ] **Convert**: Drop images → converts format (PNG→JPG, etc.)
- [ ] Custom action zones (user-defined shell commands)

**Technical scope**: Each action zone is a PaddockModel variant with an `ActionType` property. On drop, the action handler runs the operation and removes the file from the zone. Needs a plugin-like interface for extensibility.

---

## v3.0 — Delight

Features that make Paddock special — not just a Fences clone, but something better.

### Peek Mode
- [ ] Hotkey (Win+Space or configurable) summons all paddocks above open windows
- [ ] Release to send back behind windows
- [ ] Alternative: taskbar tray icon click
- [ ] Smooth fade animation

### Folder Portals
- [ ] Paddock that mirrors a filesystem folder
- [ ] Live-updating via FileSystemWatcher
- [ ] Drop files into portal to move/copy them
- [ ] Browse subfolders within the portal
- [ ] View modes: icons, list, details

### Multi-Monitor
- [ ] Independent paddock layouts per monitor
- [ ] Per-monitor-configuration tracking
- [ ] Preserve layouts across dock/undock cycles
- [ ] Paddocks constrained to their monitor

### Desktop Pages
- [ ] Multiple pages of paddocks (like virtual desktops for icons)
- [ ] Swipe at screen edge to switch pages
- [ ] Page indicator dots
- [ ] Drag paddocks between pages

### Import/Export & Sharing
- [ ] Export layout + theme as `.paddock` file
- [ ] Import community themes/layouts
- [ ] Gallery of community themes (stretch)

---

## Deferred Ideas (Evaluate Later)

These are interesting but not committed. Revisit based on user feedback.

| Idea | Notes |
|------|-------|
| AI-powered auto-categorization | Use local LLM to sort files by content, not just extension |
| Calendar integration | Show upcoming events in a widget paddock |
| Quick notes widget | Sticky note paddock |
| System monitor widget | CPU/RAM/disk in a paddock |
| Context-aware layouts | Auto-switch based on time of day, Wi-Fi, or foreground app |
| File preview on hover | Show thumbnails for images/PDFs without opening |
| Usage heat map | Subtly highlight frequently used icons |
| Auto-archive | Move untouched files to Archive paddock after N days |
| Clipboard history panel | Persistent recent clipboard items |
| Edge-triggered drawers | Zones slide out from screen edges on hover |
