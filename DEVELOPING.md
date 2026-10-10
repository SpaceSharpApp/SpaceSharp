# Developing SpaceSharp

How the pieces fit, where things live, and where to add a palette, a file type or a translation. The README covers building and releasing; CONTRIBUTING.md covers how to send changes.

## How it works

**Scanning** (`Services/DiskScanner.cs`, `Services/MftScanner.cs`, `Services/ElevatedScan.cs`)
A whole NTFS drive is read from its Master File Table by an elevated helper process, which hands the finished tree back to the app, so the app itself never restarts. Anything else, a single folder, another file system, or a run without administrator rights, is walked with `DirectoryInfo.EnumerateFileSystemInfos` on background threads, the top three levels in parallel. Sizes are summed bottom-up and each folder's children are sorted largest first, which the layout depends on. Progress counters are read by the UI a few times per second, so the scan never waits on the UI.

**Sizes** (`Services/NativeFileInfo.cs`)
Size on disk comes from `GetCompressedFileSizeW` for compressed, sparse, offline and cloud-placeholder files and from the plain length for everything else, rounded up to the volume's cluster size. Hard-link detection reads each file's link count and file ID with `GetFileInformationByHandle`; a file whose ID was already seen stays in the tree but contributes no size.

**Layout** (`Layout/Squarify.cs`, `Layout/Grouping.cs`)
Boxes are placed with the squarified treemap algorithm (Bruls, Huizing and van Wijk). Items are added to a row along the shorter side of the remaining space for as long as that improves the worst aspect ratio in the row. `Grouping` decides which children of a folder get a box of their own and which merge into one "312 files" block: first against a reference window so the large structure of the map is stable, then against the pixels the folder actually has on screen, quantized to powers of two so a smooth zoom does not shuffle the boxes.

**Rendering and zoom** (`Controls/TreemapControl*.cs`, one partial class in five files)
The map is a custom WPF element that draws into `DrawingVisual`s instead of creating a control per box, so tens of thousands of boxes stay fast. Zooming does not scale a picture: the map is laid out again on a virtual canvas that is the window size times the zoom level, and only boxes that intersect the window are laid out and drawn. Boxes snap to whole pixels and each leaves one grid-colored strip on its right and bottom, none where the folder's own line already is. Files get a cushion gradient per depth from the shading settings (`MakeCushion`); folder title bars are a tint of the folder's color. Zooming to a folder is an animated camera move that interpolates the zoom level logarithmically and refines the target every frame, since title bars have a fixed pixel height. Hover and selection outlines live on a separate overlay.

**Filtering** (`Services/FileFilter.cs`, `MainWindow.FilterPanel.cs`)
The filter text is parsed into conditions (`FilterSpec`). The whole tree is evaluated once per change; files match on their own and a folder stays lit when anything inside it matches, so the path to every hit stays visible. Non-matching boxes are blended toward the background without changing the layout. The hidden filter box is the single source of truth: the drawer and the rail both read and write its text, so anything that sets a filter works with either face. "Peek" (hovering a type or a month in the panel) dims the map through `SetFilterMatches` without touching the real filter.

**The panel under the map** (`Services/DriveInsights.cs`, `MainWindow.BottomPanel.cs`)
`DriveInsights` is pure and tested: bytes by file type, bytes by month last written, and the cleanup suggestions, which are decided from names alone (`CachePatterns`). The window builds the three sections from those results and refreshes them after a scan, a delete or a load.

**Themes** (`Util/ThemeManager.cs`, `Themes/`)
All control styles are in `Styles.xaml` and reference colors through `DynamicResource`. Switching themes swaps `Dark.xaml` for `Light.xaml` and every open window recolors. In "Match Windows" mode the Windows app theme is read from the registry and watched for changes. Palettes are per theme (`PaletteTheme`); `AppSettings` keeps one choice for dark and one for light.

**Top lists** (`Services/TopLists.cs`)
The largest files and folders are found with a priority queue in one pass, so the side panel is instant on millions of files.

**Updates** (`Services/Updater.cs`, `UpdateWindow.cs`, `Program.cs`)
Velopack installs per user (Setup.exe) or wherever the user picks (the MSI); both update the same way. `Program.Main` runs Velopack's hooks before WPF loads. Installed copies check GitHub Releases a few seconds after launch and offer "Install and restart", which downloads a delta package and swaps in the new version. What's new is read from CHANGELOG.md at the new release's tag, so skipped releases are listed too. The portable exe never updates itself.

**Deleting** (`Services/RecycleBin.cs`)
Items go to the Recycle Bin through the Windows shell with undo enabled. After a delete the node is removed from the tree and its size subtracted from every parent, so no rescan is needed.

## Project structure

```
SpaceSharp/
├── App.xaml(.cs)                 startup, theme setup, error dialog
├── MainWindow.xaml(.cs)          toolbar, path row, map, status bar, settings application
├── MainWindow.FilterPanel.cs     the filter drawer and rail
├── MainWindow.BottomPanel.cs     By type, Safe to clear, When changed
├── AboutWindow, InspectWindow, SettingsWindow, UpdateWindow, ShortcutsWindow
├── Controls/
│   ├── TreemapControl*.cs        fields, camera, layout, render, input
│   ├── DonutChart.cs             the donuts in Inspect
│   └── MapEnums.cs               ColorMode, TreemapItem
├── Layout/                       Squarify.cs, Grouping.cs
├── Models/FsNode.cs              the file and folder tree
├── Services/                     scanners, filter, insights, top lists, compare, CSV, updater, shell
├── Util/                         settings, palettes, custom palettes, color text, themes, strings, command line
├── Resources/Strings*.resx       UI text, English and Norwegian bokmål
├── Themes/                       Styles.xaml, Dark.xaml, Light.xaml
├── Assets/                       the mark (SVG), the Windows icon, the 256 px PNG
└── Properties/PublishProfiles/   Portable, Small, Velopack
SpaceSharp.Tests/                 xUnit: layout, grouping, filter, insights, color text, formatting, tree ops, localization
docs/                             landing page (GitHub Pages), spacesharp-mock.js (the app drawn in a browser), screenshots, icon, header, social preview
tools/make-assets.py              every icon, header and installer image from one definition of the mark
tools/make-social.py              docs/social-preview.png, the message laid out as a treemap in amber
tools/screenshot-mockup.html      controls around docs/spacesharp-mock.js, for screenshots
tools/screenshots.js              renders the README screenshots from it with headless Chromium
tools/make-mobile-shots.py        docs/m/*.webp, the screenshots at 1200 px for the phone-sized landing page
installer/                        wizard pages, dialog images, release.ps1, brand-msi.ps1
```

## Where to change things

**Add a built-in palette.** Add an entry to `BuildSchemes()` in `Util/Palette.cs` with `FromList(name, theme, colors)`: twelve folder colors as `#RRGGBB`. File colors and file-type colors are derived unless you pass them. Users add their own as JSON instead (see the README).

**Add a file type.** Extensions and their categories are in `BuildExtensionMap()` in `Util/Palette.cs`. The cleanup patterns the panel uses are `CachePatterns` in `Services/DriveInsights.cs`.

**Change theme colors.** Edit `Themes/Dark.xaml` or `Themes/Light.xaml`. Both must define the same keys.

**Add a setting.** A property with a default in `Util/AppSettings.cs` (and in `ResetToDefaults`), a row in `SettingsWindow.Build()`, and its effect in `MainWindow.ApplySettings()` or `ApplyLook()` if it changes the map. Map settings also show in the Settings preview, which runs through the same `ApplyLook`.

**Add a shortcut.** `MainWindow.Window_PreviewKeyDown`, the list in `ShortcutsWindow.cs`, and the README's table.

**Add UI text.** Every string is a key in `Resources/Strings.resx` with a Norwegian twin in `Strings.nb.resx`. `LocalizationTests` fails the build if a key used in code or XAML is missing. A new language is one more `Strings.<culture>.resx`; it appears in the language list on its own.

**The icon.** The mark is an isometric amber block with a cube carved out of its front corner. Everything is generated from `tools/make-assets.py` (Python 3 with Pillow and cairosvg): the SVGs, the Windows icon on its rounded Graphite tile, the 256 px PNG the app shows, the header, the wordmark and the installer images. Change the geometry or colors there, not in the files.

**Screenshots.** `docs/spacesharp-mock.js` draws the window with sample data so no real file names end up online; the landing page embeds it as the live preview and `tools/screenshot-mockup.html` wraps it in controls. Open the tool in a browser, pick a theme, palette and scene, and capture the window, or run `node tools/screenshots.js` (needs Playwright) to regenerate every PNG the README uses, then `python tools/make-mobile-shots.py` for the small WebP copies the landing page shows on phones, where a swipeable gallery of pictures stands in for the live preview. When the app's UI changes, the engine has to follow: it is a drawing, not a build of the app.

**Name, version and author** come from `SpaceSharp.csproj` and appear in About and in the exe's properties. Tag releases with the bare version number; the update window reads the changelog at that tag.
