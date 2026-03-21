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

## Build & Run
```bash
dotnet build                          # Build all projects
dotnet run --project src/Paddock.App  # Run the app
dotnet test                           # Run all tests
```

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
1. Add P/Invoke signature in `Shell/NativeMethods.cs`
2. Create or extend a service in `Shell/` with a clean interface
3. Register the service in `App.xaml.cs`

## Spec
See [SPEC.md](SPEC.md) for full feature spec, data model, and roadmap.
