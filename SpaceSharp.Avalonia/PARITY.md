# The Avalonia front end: what is ported, what is not

`SpaceSharp.Avalonia` is the cross-platform SpaceSharp: the same map and the same core, drawn with Skia through Avalonia, for Windows, macOS and Linux first and Android and iOS after. It shares `SpaceSharp.Core` with the Windows app (`SpaceSharp`, WPF), which stays the Windows release until this one matches it. The two apps read the same `settings.json`, so the map looks the same in both.

Run it with `dotnet run --project SpaceSharp.Avalonia` on any of the three desktops. The tests workflow builds it on all three.

## Ported

| Area | State |
|---|---|
| The map (`Controls/TreemapControl*.cs`) | Full port: layout cache, grouping, chain merging, cushions, grid, title bars, labels with halo, hover and selection frames, wheel zoom, drag pan, animated focus. Same code as the WPF control apart from the drawing surface (two layers with `Render`) and the pointer events. |
| Palettes (`Util/Palette.cs`) | The fifteen built-in palettes, both themes, change colors, file categories. |
| Scanning | The folder walk, from `SpaceSharp.Core/Scanning/FolderWalker.cs` (new; the Windows app's `DiskScanner` minus the Win32 and MFT parts). Size on disk, compressed files and hard links use Win32 on Windows (`Util/PlatformFiles.cs`) and 4 KB clusters elsewhere. |
| Main window | Toolbar (scan folder, rescan, up, home, side panel, zoom), path row, drives list with capacity bars, scan card with size, rate, elapsed and the current path, status line, Color by, Palette, dark/light toggle. Keys: Backspace, Home, + and −, F5, Esc. |
| Settings | Read and written through `AppSettings` in Core: every map option the Windows app has applies here. There is no Settings window yet; edit `settings.json` or set the option in the Windows app. |
| Languages | All eighteen, from Core's resources, with right-to-left mirroring through `AppLanguages.IsRightToLeft` (not yet applied to the Avalonia windows). |

## Not yet

In the order they should come, each a commit on its own:

1. **Settings window** (the big one; 840 lines of WPF). Tabs, the treemap options pages with the live preview, the palette picker with group headings, the language dropdown with its machine-translation group and note.
2. **Right-click menu and deletion**: open, show in file manager, properties, copy path, move to the trash (`RecycleBin` is Windows shell code; macOS and Linux need `trash`/`gio trash` or the Freedesktop spec), rescan folder.
3. **Filter**: `FileFilter` is pure code and can move to Core as is; the drawer and the rail are UI.
4. **Insights view**, **Inspect window**, **largest items** in the side panel: `DriveInsights`, `TopLists` and `InspectSummary` are computation over the tree with a few brushes to cut; they belong in Core.
5. **Saved scans and compare**: `ScanFile` and `ScanCompare` are already in Core; the start screen tiles, Open and Save As are UI.
6. **Fast NTFS scan** on Windows: `MftScanner` and `ElevatedScan` behind an `IScanner` so the Windows build of this app is as fast as the WPF one.
7. **About, Shortcuts, Update**: Velopack supports macOS and Linux too, so the updater can come across; the Windows installer stays WPF's until the switch.
8. **Icons**: the Lucide set from `Themes/Icons.xaml` (path geometry, which Avalonia draws the same way); the toolbar uses text glyphs until then.
9. **Custom palettes** and the palette editor.
10. **Explorer integration, single instance, crash reports**: Windows-only or platform-specific; last.

## Rules

- Nothing in this project may reference `System.Windows`. Anything that would work the same on a Mac goes in `SpaceSharp.Core`, not here.
- Platform calls go behind an interface in Core (`IPlatformFiles` is the first) with the Windows answer here under `[SupportedOSPlatform("windows")]` and a plain fallback.
- Keep file names and namespaces the same as the Windows app's, so a diff between the two front ends stays readable.
