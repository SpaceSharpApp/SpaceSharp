# The Avalonia front end: what is ported, what is not

`SpaceSharp.Avalonia` is the cross-platform SpaceSharp: the same map and the same core, drawn with Skia through Avalonia, for Windows, macOS and Linux first and Android and iOS after. It shares `SpaceSharp.Core` with the Windows app (`SpaceSharp`, WPF), which stays the Windows release until this one matches it. The two apps read the same `settings.json`, so the map looks the same in both.

Run it with `dotnet run --project SpaceSharp.Avalonia` on any of the three desktops. The tests workflow builds it on all three.

## Ported

| Area | State |
|---|---|
| The map (`Controls/TreemapControl*.cs`) | Full port: layout cache, grouping, chain merging, cushions, grid, title bars, labels with halo, hover and selection frames, wheel zoom, drag pan, animated focus. Same code as the WPF control apart from the drawing surface (two layers with `Render`) and the pointer events. |
| Palettes (`Util/Palette.cs`) | The fifteen built-in palettes, both themes, change colors, file categories; custom palettes from the same JSON files as the Windows app (`Util/CustomPalettes.cs`) and the palette editor (`PaletteEditorWindow.cs`, with the generator, the checks and the live preview). |
| Scanning | The folder walk, from `SpaceSharp.Core/Scanning/FolderWalker.cs` (new; the Windows app's `DiskScanner` minus the Win32 and MFT parts). Size on disk, compressed files and hard links use Win32 on Windows (`Util/PlatformFiles.cs`) and 4 KB clusters elsewhere. On Windows, with Settings › Scanning › fast scan on and administrator rights, whole NTFS drives are read from the file table (`Services/MftScanner.cs`, the same scanner as the WPF app); without the rights an elevated helper process reads it after a UAC prompt (`Services/ElevatedScan.cs`, the same pipe protocol as the WPF app); declining falls back to the walk, and the access-denied bar offers Restart as administrator. |
| Main window | Toolbar (scan folder, rescan, up, home, side panel, zoom), path row, drives list with capacity bars, scan card with the three stages, size, rate, elapsed and the current path, status line, Color by, Palette, dark/light toggle, the hover tooltip on the map, the protected-folders bar. Keys: Backspace, Home, + and −, F5, Esc, Alt+Enter. Command line (a path, a .sscan, --compare), one instance per session (a second launch hands its arguments over), crash reports with the GitHub form prefilled. |
| Settings window | Ported (`SettingsWindow.axaml.cs`): the five tabs, every card and row, the live map preview on the Treemap page, the grouped language list, the light compass, presets, color swatches, the Leave-out chips, the keep-scans slider, Reset. The Updates row, the Windows section (the Explorer menu, on Windows only), the chrome looks for both themes, the custom-palette buttons. Changes apply to the main window at once, as in WPF. |
| Right-click menu | Zoom to folder, up, whole map, Open, Show in Explorer/Finder/file manager, Properties (Windows), Copy path and name, Show only this file type, Select everything in this folder, Rescan, Move to trash. Keys: Enter, Alt+Enter, Ctrl+C, Delete, Esc. |
| Trash (`Util/Trash.cs`) | Windows: the Recycle Bin through the same shell call as the WPF app. macOS: the Finder (so Put Back works). Linux: `gio trash`. The map, the lists and the drives update after the move, with the confirmation from Settings › Safety. |
| Filter | Both faces: the drawer over the right of the map (the default) and the rail under the path, with the same grammar (`FileFilter` and `FilterSpec` moved to Core); the Filter button counts the conditions, matches are counted, everything else is dimmed on the map, Select matches, quick filters, Esc clears or closes, Ctrl+F focuses. |
| Largest items | Files, Folders and Types in the side panel under the drives; a row zooms to the item or sets the filter to the type. |
| Insights view | All seven cards and the tile strip: by type with the donut, safe to clear with the checkboxes and Recycle checked (and Empty Recycle Bin on Windows), when changed with the timeline and the age bands, largest folders, largest files, since last scan, extensions. B toggles, Map and Insights buttons on the toolbar. |
| Inspect window | Ctrl+I or the menu: header, the three context lines, callouts, the two donuts with legends, where the space is, Copy details, Show in file manager. Breadcrumbs and folder rows fly the map there. |
| Saved scans and compare | Every scan is saved and compared with the previous one of the same place; the start screen lists recent scans as tiles; Open (Ctrl+O), Save As (Ctrl+S), Compare with a saved scan, Stop comparing; Color by change and the Changes list appear while there is a baseline; old saves are pruned per Settings › Scanning. |
| Export to CSV | Largest files, this folder, filter matches. |
| About and Shortcuts | About (F1 or the toolbar) laid out as the Windows app's: the version row with Check for updates when installed, What's new, the list rows, Crash logs, Copy version info, the bug form prefilled; the Shortcuts window. |
| Updates | Velopack, as the Windows app: `Services/Updater.cs` and `UpdateWindow.cs` (the prompt and What's new), the check at startup and in About and Settings. Active once this app ships through a Velopack setup; a portable copy never updates itself. |
| Title bar | On Windows the caption is painted in the theme's colors (`TitleBarTheme`), the same as the WPF app; other platforms keep their own. |
| Icons | The Lucide set from the Windows app (`Themes/Icons.axaml`, `Controls/Icon.cs`), on the toolbar. |
| Languages | All eighteen, from Core's resources, with the windows mirrored for right-to-left languages (`TitleBarTheme.Attach`, which every window goes through); the map itself stays left-to-right, as in WPF. |

## Not yet

- **Windows packaging**: the Windows download is still the WPF app. macOS (Apple Silicon and Intel) and Linux are packed by `.github/workflows/release-avalonia.yml` with Velopack (channels `osx-arm64`, `osx-x64`, `linux-x64`), run by hand after the Windows release is drafted; the macOS packages are unsigned until there is an Apple Developer account (the workflow marks where signing and notarization go). Switching Windows to this app means a `win-x64` channel from `SpaceSharp.Avalonia` in `release.ps1` in place of the WPF publishes.
- **Android and iOS heads** on the same code, once the desktop app is the release.
- The Windows-only extras that have no equivalent elsewhere (the Explorer menu, Properties, the elevated helper) are ported for Windows and hidden on the other platforms.

## Rules

- Nothing in this project may reference `System.Windows`. Anything that would work the same on a Mac goes in `SpaceSharp.Core`, not here.
- Platform calls go behind an interface in Core (`IPlatformFiles` is the first) with the Windows answer here under `[SupportedOSPlatform("windows")]` and a plain fallback.
- Keep file names and namespaces the same as the Windows app's, so a diff between the two front ends stays readable.
