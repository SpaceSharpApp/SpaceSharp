Two projects and a line between them. `SpaceSharp.Core` targets plain `net10.0` and cannot reference Avalonia, Velopack or Win32 (the compiler enforces it: none of them exist for that target). `SpaceSharp` is the app on top. The rule when adding code: if it would work the same on every platform, it goes in Core; anything that draws, opens a window, talks to the shell, the trash, the registry or the NTFS file table stays in the app.

## The pieces

```
scan ─────▶ FsNode tree ─────▶ Squarify + Grouping ─────▶ TreemapControl (render, zoom, input)
  │              │                                                │
  │              ├──▶ FileFilter (dims the map)                   ├──▶ MainWindow partials: FilterPanel, Insights, InsightRows, Changes, Scans, Achievements, Tooltip, StatusBar, PalettePicker, Showcase
  │              ├──▶ DriveInsights + ExtraInsights (the Insights rows)
  │              ├──▶ DuplicateFinder, EmptyFolders, PathLoops (the tools behind three rows)
  │              ├──▶ ScanCompare (baselines + gone items)
  │              ├──▶ Achievements (the store and the judging)
  │              └──▶ ScanFile (.sscan save and load)
  └── FolderWalker (every platform) / MftScanner + ElevatedScan (Windows, NTFS)
```

**The tree** (`Core/Models/FsNode.cs`). One node per file and folder: name, kind, size, size on disk, file count, last write time, attributes, and after a comparison the baseline sizes. Folders sum their children once in `FinishDirectory`. Memory matters at a million nodes, so fields are compact and the free-space box is a node like any other.

**Scanning.** `FolderWalker` is the System.IO scan every platform has, parallel, with exclusions and hard-link detection behind `IPlatformFiles`. On Windows a whole NTFS drive goes through `MftScanner`, which reads the master file table as one stream, verifies every record, and builds the tree from it; `ElevatedScan` runs it in a second elevated copy of the exe and streams the tree back over a pipe. While a walk runs, `LiveTree` keeps a thread-safe ledger of finished folders and stand-ins so the map can draw itself.

**Layout** (`Core/Layout/Squarify.cs`, `Grouping.cs`). The squarified treemap algorithm (Bruls, Huizing and van Wijk) on a UI-free `LayoutRect`. `Grouping` decides which small children become one "312 files" box according to Density.

**Rendering and zoom** (`Controls/TreemapControl*.cs`). One custom Avalonia control in six partial files (fields, camera, layout, render, input, live). It draws every box itself in two render passes rather than creating a control per box, which is why tens of thousands of boxes stay fast. Zoom re-lays out the visible folder rather than scaling a picture; the camera animates between layouts, and the live map tweens boxes between snapshots by path.

**Filter** (`Core/Services/FileFilter.cs`, `MainWindow.FilterPanel.cs`). Text is parsed into a `FilterSpec`; the tree is evaluated once per change and a folder stays lit when anything inside it matches. The drawer and the rail both write the same text.

**Insights** (`Core/Services/DriveInsights.cs`, `ExtraInsights.cs`, `MainWindow.Insights.cs`, `MainWindow.InsightRows.cs`). Pure, tested functions: bytes by type, by month, the cleanup suggestions (name patterns in `CachePatterns`), change totals, the free-space projection, and in `ExtraInsights` one extra pass over the tree for the games, media, developer, Downloads, hard links, small and empty, and deep and long rows. The window arranges the four tiles and the rows (`Row(...)` in `InsightRows`), remembers the open one in settings, and leaves rows with nothing to show out.

**Duplicates, empty folders and loops** (`Core/Services/DuplicateFinder.cs`, `EmptyFolders.cs`, `Core/Util/PathLoops.cs`, `DuplicatesWindow.cs`, `EmptyFoldersWindow.cs`). `DuplicateFinder.Find` groups by size, hashes the first 16 KB, then whole files, on a few threads with throttled progress; `EmptyFolders.Find` lists the topmost folders with no files below them; `PathLoops.Find` spots a path that repeats inside itself and `Compact` shortens long paths from the middle. The two windows are virtualizing lists (`PickList`) whose ticks live in row models, and both hand their deletions to `MainWindow.RemovedFromDiskAsync` so every view follows. `DeepDelete` (app, Windows) removes a tree with long-path names, without recursion, bottom up.

**Achievements** (`Core/Services/Achievements.cs`, `MainWindow.Achievements.cs`, `AchievementsWindow.cs`). An enum, a small JSON store with an `Unlocked` event, and judging functions fed with `ScanFacts` after a scan and with the comparison after a compare; the window shows the toast queue and the list. The mock PC uses an in-memory store.

**Changes** (`Core/Services/ScanCompare.cs`, `MainWindow.Changes.cs`). `ScanCompare.Apply` walks two trees matching names within each folder, writes baseline sizes onto the current nodes and returns a `ScanComparison` holding what is gone per folder. The view is a virtualized table over that.

**Saved scans** (`Core/Services/ScanFile.cs`). A gzip-compressed binary of the tree with a header (root, time, method, free space). Autosaves live per root, with a `.prev` copy for comparison.

**Strings** (`Core/Resources/Strings*.resx`, `Core/Util/Strings.cs`). Every user-visible string is a key; `LocalizationTests` fails the build when code asks for one the English file lacks, and a key missing from a translation falls back to English. Eighteen files, discovered by culture code at startup.

**Settings and looks** (`Core/Util/AppSettings.cs`, `UiLooks.cs`, `App.axaml`, `Themes/Styles.axaml`). Settings are a JSON file loaded once; every control style references colors through `DynamicResource`, so a theme or look change is a dictionary swap.

## Where to add things

| To add | Touch |
|---|---|
| A setting | A property with a default in `AppSettings` (and `ResetToDefaults`), a row in `SettingsWindow.Build()`, its effect in `MainWindow.ApplySettings()` or `ApplyLook()`, the [Settings reference](Settings-Reference) |
| A shortcut | `MainWindow.OnKeyDown`, the `Uses` list and the legend in `ShortcutsWindow.cs`, the README table, the [Keyboard shortcuts](Keyboard-Shortcuts) page |
| UI text | A key in `Strings.resx` and the same key in all seventeen translation files |
| A built-in palette | `BuildSchemes()` in `Util/Palette.cs` with twelve folder colors and a `PaletteAbout_` string |
| A file type | One line under its `[Category]` in `Core/Resources/FileTypes.txt` (987 extensions; a test rejects duplicates); the filter's words in `FileFilter.TypeWords` |
| A cleanup pattern | `CachePatterns` in `DriveInsights.cs` and a `Cleanup_` string pair |
| An Insights row | A `Build…Row` method in `MainWindow.InsightRows.cs` called from `BuildRows`, its facts computed in `ExtraInsights` with a test, and `Insights_Row…` strings |
| An achievement | A value in the `Achievement` enum, `Ach_<Id>_Name` and `_Hint` strings (a test checks every value has them), and a judging line in `Achievements` |
| An icon | A line in `tools/make-icons.py`'s table (Lucide), which regenerates `Themes/Icons.axaml` |
| A translation | One more `Strings.<culture>.resx` with a `Meta_MachineTranslated` entry; it appears in the language list on its own ([Translating](Translating)) |

[DEVELOPING.md](https://github.com/SpaceSharpApp/SpaceSharp/blob/main/DEVELOPING.md) in the repository goes deeper on each of these and is kept in step with the code.
