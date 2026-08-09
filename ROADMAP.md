# Paddock Roadmap

## v1.0 — Core Desktop Organizer

The foundation. Everything needed to replace a cluttered desktop with organized zones.

### Done
- [x] Project architecture (Paddock.App / Core / Shell)
- [x] Paddock model with persistence (JSON settings)
- [x] System tray integration (new paddock, show/hide, restore icons, settings)
- [x] Dark and Light themes (Apple-inspired), switchable at runtime
- [x] Icon extraction via the shell's 48px system image list
- [x] Layout engine with snapping and intelligent spacing
- [x] Desktop-level windows: above wallpaper and icons, below every app window
- [x] One window per paddock, so empty desktop space still belongs to the shell
- [x] Draw-to-create paddock (tray → New Paddock, drag a rectangle, Esc cancels)
- [x] Paddock drag-to-move with per-monitor edge and neighbour snapping
- [x] Resize from the bottom-right grip, clamped to the monitor
- [x] Icons really leave the desktop: files move into the paddock's own folder
- [x] Icon drag in / out / between paddocks, plus "Fill from Desktop" by category
- [x] Restore-everything escape hatch (tray → Restore All Icons to Desktop)
- [x] Reconciliation on startup and on external changes (FileSystemWatcher)
- [x] Sort icons by name, date, type, size — persisted per paddock
- [x] Zone title visibility toggle (always / hover / hidden) + inline rename
- [x] Roll-up (collapse to title bar), persisted
- [x] Per-paddock transparency and colour (panel only — labels stay crisp)
- [x] Quick Hide via global hotkey and tray, with per-zone exclusion
- [x] Shell context menu on right-click icon
- [x] Resolution / monitor change handling (clamp per monitor)
- [x] Run-on-startup registry integration
- [x] Layout snapshots (corrals) — save/restore/switch/export/import
- [x] Background icon loading, batched settings writes, cached monitor geometry
- [x] Single-instance guard, crash logging to %AppData%/Paddock/paddock.log
- [x] Corrupt settings.json is preserved rather than silently discarded

### TODO
- [ ] Collect the icons that were sitting underneath a newly drawn paddock
- [ ] Multi-select inside a paddock (drag, launch and delete several at once)
- [ ] Free icon placement inside a paddock (currently a reflowing grid)
- [ ] Colour picker in the settings UI
- [ ] Fade animation on quick hide, smooth roll-up animation
- [ ] Corral management UI (currently model + service only)
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
