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
  Paddock.Core.Tests/ → Unit tests for core logic
```

## Architecture Rules
- **Paddock.Core** must NOT reference WPF or Windows-specific APIs
- **Paddock.Shell** handles all Win32/COM interop, exposes clean C# interfaces
- **Paddock.App** references both Core and Shell, wires everything together
- Follow MVVM pattern: Views (XAML) bind to ViewModels, ViewModels use Services

## How the desktop integration works (read before touching it)
- **One window per paddock.** There is deliberately no full-screen overlay: empty
  desktop space belongs to the shell, so right-click, rubber-band selection and
  icon dragging keep working. The only full-screen window is `DrawAreaWindow`,
  shown for the moment the user draws a new paddock.
- **Z-order, not parenting.** Do *not* `SetParent` into `WorkerW` — a window there
  renders behind the desktop icons and gets no mouse input. `ShellHookService`
  instead pins each window directly above the window hosting `SHELLDLL_DefView`,
  re-asserting it from `WM_WINDOWPOSCHANGING` (see `PaddockWindow.WndProc`) and
  from a periodic re-pin in `PaddockHost`.
- **Icons leave the desktop by moving the file.** `IconStore` moves items into
  `%USERPROFILE%\Desktop\.Paddock\<paddock-id>` and back out again; the shell only
  draws the top level of the Desktop folder. Nothing is deleted, names are never
  overwritten, and only items on the user's own desktop are ever moved.
- **Coordinates** in `PaddockModel` are screen coordinates in device-independent
  pixels (WPF `Window.Left`/`Top`). Use `ScreenHelper` to convert from Win32
  pixels and to find the right monitor.

## Build & Run
```bash
dotnet build                          # Build all projects
dotnet run --project src/Paddock.App  # Run the app (Windows only)
dotnet test                           # Run all tests
```

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
Requires the Windows Application Packaging Project tooling.

## Key Patterns
- **Settings**: JSON file at `%AppData%/Paddock/settings.json`
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

### Changing anything about a paddock's window
`PaddockHost` creates and owns `PaddockWindow`s; `PaddockControl` handles the
mouse and applies geometry to its host window. Keep `Topmost` false and keep the
`WM_WINDOWPOSCHANGING` hook — without it, clicking a paddock lifts it above the
user's applications, which is the single most annoying thing this app can do.

## Spec
See [SPEC.md](SPEC.md) for full feature spec, data model, and roadmap.
