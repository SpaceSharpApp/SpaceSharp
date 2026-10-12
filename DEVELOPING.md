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
The filter text is parsed into conditions (`FilterSpec`). The whole tree is evaluated once per change; files match on their own and a folder stays lit when anything inside it matches, so the path to every hit stays visible. Non-matching boxes are blended toward the background without changing the layout. The hidden filter box is the single source of truth: the drawer and the rail both read and write its text, so anything that sets a filter works with either face. `SetFilterMatches` can dim the map to any set of nodes without touching the real filter (used by Show in Insights).

**The Insights view** (`Services/DriveInsights.cs`, `MainWindow.Insights.cs`)
`DriveInsights` is pure and tested: bytes by file type with the top extensions, bytes by month last written, the oldest large files, where each extension mostly lives, what changed against the baseline, and the cleanup suggestions, which are decided from names alone (`CachePatterns`). `MainWindow.Insights.cs` builds the tiles and cards from those results in the map's place (`InsightsView` over the `Treemap`, toggled by `ApplyView`) and refreshes them after a scan, a delete or a load. Every row calls back into the map: `ShowOnMap` selects and zooms, `FilterAndShowMap` sets the filter text and switches the view.

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
├── MainWindow.Insights.cs        The Insights view in the map's place
├── AboutWindow, InspectWindow, SettingsWindow, UpdateWindow, ShortcutsWindow
├── Controls/
│   ├── TreemapControl*.cs        fields, camera, layout, render, input
│   ├── DonutChart.cs             the donuts in Inspect
│   └── MapEnums.cs               ColorMode, TreemapItem
├── Layout/                       Squarify.cs, Grouping.cs
├── Models/FsNode.cs              the file and folder tree
├── Services/                     scanners, filter, insights, top lists, compare, CSV, updater, shell
├── Util/                         settings, palettes, custom palettes, color text, themes, strings, command line
├── Resources/Strings*.resx       UI text: English, Norwegian bokmål, Arabic
├── Themes/                       Styles.xaml, Dark.xaml, Light.xaml
├── Assets/                       the mark (SVG), the Windows icon, the 256 px PNG
└── Properties/PublishProfiles/   Portable, Small, Velopack
SpaceSharp.Tests/                 xUnit: layout, grouping, filter, insights, color text, formatting, tree ops, localization
docs/                             README images, spacesharp-mock.js (the app drawn in a browser), screenshots, icon, social preview; index.html redirects to spacesharp.app
tools/make-assets.py              every icon, header and installer image from one definition of the mark
tools/make-social.py              docs/social-preview.png, the message laid out as a treemap in amber
tools/make-icons.py               SpaceSharp/Themes/Icons.xaml, the Lucide icons as path geometry
tools/screenshot-mockup.html      controls around docs/spacesharp-mock.js, for screenshots
tools/screenshots.js              renders the README screenshots from it with headless Chromium
tools/make-mobile-shots.py        docs/m/*.webp (not committed), the screenshots at 1200 px for the website's phone gallery
installer/                        wizard pages, dialog images, release.ps1, brand-msi.ps1
```

## Where to change things

**Add a built-in palette.** Add an entry to `BuildSchemes()` in `Util/Palette.cs` with `FromList(name, theme, colors)`: twelve folder colors as `#RRGGBB`. File colors and file-type colors are derived unless you pass them. Users make their own in the palette editor (`PaletteEditorWindow`, over a `PaletteDraft`: the pure model with the derivations, the generator, the checks and the JSON) or as JSON by hand; `CustomPalettes` reads and writes the files.

**Add a file type.** Extensions and their categories are in `BuildExtensionMap()` in `Util/Palette.cs`. The cleanup patterns the panel uses are `CachePatterns` in `Services/DriveInsights.cs`.

**Change theme colors.** Edit `Themes/Dark.xaml` or `Themes/Light.xaml`. Both must define the same keys.

**Add a setting.** A property with a default in `Util/AppSettings.cs` (and in `ResetToDefaults`), a row in `SettingsWindow.Build()`, and its effect in `MainWindow.ApplySettings()` or `ApplyLook()` if it changes the map. Map settings also show in the Settings preview, which runs through the same `ApplyLook`.

**Add or change an icon.** The icons are Lucide, as path geometry in `SpaceSharp/Themes/Icons.xaml`, which `tools/make-icons.py` generates: add a line to its `ICONS` table (app name, Lucide name) or a path to `CUSTOM`, run it, then use `{StaticResource Icon.Name}` on a `ToolButton`'s `Tag` or a `controls:Icon`'s `Glyph` in XAML, or `Icons.Get("Name")` / `Icons.Make("Name", size, "TextDim")` in code. `Controls/Icon.cs` draws any of them at any size with the stroke scaled along. The mockup's copies live in `docs/spacesharp-mock.js`; the drive pictures are picked by `Services/DriveKinds.cs`.

**Add a shortcut.** `MainWindow.Window_PreviewKeyDown`, the list in `ShortcutsWindow.cs`, and the README's table.

**Add UI text.** Every string is a key in `Resources/Strings.resx` with a Norwegian twin in `Strings.nb.resx`. `LocalizationTests` fails the build if a key used in code or XAML is missing. A new language is one more `Strings.<culture>.resx`; it appears in the language list on its own.

**The icon.** The mark is an isometric amber block with a cube carved out of its front corner. Everything is generated from `tools/make-assets.py` (Python 3 with Pillow and cairosvg): the SVGs, the Windows icon on its rounded Graphite tile, the 256 px PNG the app shows, the header, the wordmark and the installer images (the MSI's banner and dialog art, "Facets", is a list of triangles in that file). Change the geometry or colors there, not in the files.

**Screenshots.** `docs/spacesharp-mock.js` draws the window with sample data so no real file names end up online; the website (spacesharp.app, kept outside this repository) embeds it as the live preview and `tools/screenshot-mockup.html` wraps it in controls. Open the tool in a browser, pick a theme, palette and scene, and capture the window, or run `node tools/screenshots.js` (needs Playwright) to regenerate every PNG the README uses, then `python tools/make-mobile-shots.py` for the small WebP copies the website shows on phones (written to `docs/m/`, which is ignored by git; copy them and the PNGs into the site when they change). When the app's UI changes, the engine has to follow: it is a drawing, not a build of the app.

**Right-to-left.** `AppLanguages.ApplyDirection(window)` sets a window's `FlowDirection` to RightToLeft when the culture reads right to left; `TitleBarTheme.Attach` calls it, and every window goes through that, so a new window needs nothing beyond the `Attach` call it has anyway. Menus and tooltips inherit the direction from their window. (Overriding the property metadata instead fails: `Window`'s static constructor overrides it too.) `TreemapControl`, `DonutChart` and `Icon` pin themselves to LeftToRight because they draw with coordinates. When adding a control that draws with a DrawingContext or positions things by x, do the same, and prefer `HorizontalAlignment`/`TextAlignment` over fixed margins for anything that should swap sides. To try it without a translation, put `"Language": "ar"` in `%AppData%\SpaceSharp\settings.json` (the dropdown only lists languages that have a resx): the strings fall back to English, mirrored.

**Crash reporting.** `Util/CrashLog.cs` writes unexpected errors to `%LocalAppData%\SpaceSharp\logs` and builds the prefilled GitHub issue link; `App.xaml.cs` hooks the dispatcher, the AppDomain and unobserved tasks. To try it, press Ctrl+Alt+Shift+F12 in the main window (an exception on the UI thread) or Ctrl+Alt+Shift+F11 (one in a background task, which reaches the dialog when the finalizer notices it, so it can take a moment). Neither is in the shortcut list.

**Name, version and author** come from `SpaceSharp.csproj` and appear in About and in the exe's properties. Tag releases with the bare version number; the update window reads the changelog at that tag.

**Releasing.** Finish the changelog section (take "(unreleased)" off its heading), make sure `<Version>` matches and the version badge at the top of the README says the same (it is a static badge; the live one timed out behind GitHub's image proxy), run `python tools/make-assets.py` if the major.minor changed so the header badge follows, commit, then `git tag 2.1.0 && git push origin main --tags`. The tag starts `.github/workflows/release.yml`, which checks that the tag, the version and the changelog heading agree, then runs `installer/release.ps1` in three stages: Build (tests, the four publishes), Pack (vpk, the MSIs, the 1.x bridge files) and Upload (a draft release). A signed release is made from your own PC, where the certificate is: `.\installer\release.ps1 -Sign <thumbprint> -Upload` builds, signs the app exes, lets vpk sign everything inside the packages plus Setup.exe and Update.exe, signs the branded MSIs, checks every signature, and drafts the release; the tag workflow still runs the tests and a dry build. The thumbprint is the certificate's SHA-1 in certmgr.msc, and the certificate has to be present in the user store while the script runs (SimplySign Desktop signed in, or the card inserted). From the Actions tab the workflow can be run by hand as a dry run: it builds everything but creates no release. A broken run reaches nobody: installed copies only see published releases, and the draft is yours to delete; fix, delete the tag, tag again.
