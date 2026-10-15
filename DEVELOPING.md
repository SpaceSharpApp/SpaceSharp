# Developing SpaceSharp

How the pieces fit, where things live, and where to add a palette, a file type or a translation. The README covers building and releasing; CONTRIBUTING.md covers how to send changes.

## How it works

**Scanning** (`SpaceSharp.Core/Scanning/FolderWalker.cs`, `Services/MftScanner.cs`, `Services/ElevatedScan.cs`)
On Windows a whole NTFS drive is read from its Master File Table, in process when the app has administrator rights and otherwise by an elevated helper process (a second copy of the app with `--mft-helper`), which streams progress over a pipe and hands the finished tree back as a scan file, so the app itself never restarts. Anything else, a single folder, another file system, a Mac or Linux, or a declined UAC prompt, is walked with `DirectoryInfo.EnumerateFileSystemInfos` on background threads, the top three levels in parallel. Sizes are summed bottom-up and each folder's children are sorted largest first, which the layout depends on. Progress counters are read by the UI a few times per second, so the scan never waits on the UI.

**Sizes** (`Util/PlatformFiles.cs`, behind Core's `IPlatformFiles`)
On Windows, size on disk comes from `GetCompressedFileSizeW` for compressed, sparse, offline and cloud-placeholder files and from the plain length for everything else, rounded up to the volume's cluster size; hard-link detection reads each file's link count and file ID with `GetFileInformationByHandle`, and a file whose ID was already seen stays in the tree but contributes no size. Elsewhere the plain implementation rounds to 4 KB clusters and treats every file as its own.

**Layout** (`Layout/Squarify.cs`, `Layout/Grouping.cs`)
Boxes are placed with the squarified treemap algorithm (Bruls, Huizing and van Wijk). Items are added to a row along the shorter side of the remaining space for as long as that improves the worst aspect ratio in the row. `Grouping` decides which children of a folder get a box of their own and which merge into one "312 files" block: first against a reference window so the large structure of the map is stable, then against the pixels the folder actually has on screen, quantized to powers of two so a smooth zoom does not shuffle the boxes.

**Rendering and zoom** (`Controls/TreemapControl*.cs`, one partial class in five files)
The map is a custom Avalonia control that draws its boxes in two `Render` passes (the map and an overlay) instead of creating a control per box, so tens of thousands of boxes stay fast. Zooming does not scale a picture: the map is laid out again on a virtual canvas that is the window size times the zoom level, and only boxes that intersect the window are laid out and drawn. Boxes snap to whole pixels and each leaves one grid-colored strip on its right and bottom, none where the folder's own line already is. Files get a cushion gradient per depth from the shading settings (`MakeCushion`); folder title bars are a tint of the folder's color. Zooming to a folder is an animated camera move that interpolates the zoom level logarithmically and refines the target every frame, since title bars have a fixed pixel height. Hover and selection outlines live on a separate overlay.

**Filtering** (`Services/FileFilter.cs`, `MainWindow.FilterPanel.cs`)
The filter text is parsed into conditions (`FilterSpec`). The whole tree is evaluated once per change; files match on their own and a folder stays lit when anything inside it matches, so the path to every hit stays visible. Non-matching boxes are blended toward the background without changing the layout. The hidden filter box is the single source of truth: the drawer and the rail both read and write its text, so anything that sets a filter works with either face. `SetFilterMatches` can dim the map to any set of nodes without touching the real filter (used by Show in Insights).

**The Insights view** (`Services/DriveInsights.cs`, `MainWindow.Insights.cs`)
`DriveInsights` is pure and tested: bytes by file type with the top extensions, bytes by month last written, the oldest large files, where each extension mostly lives, what changed against the baseline, and the cleanup suggestions, which are decided from names alone (`CachePatterns`). `MainWindow.Insights.cs` builds the tiles and cards from those results in the map's place (`InsightsView` over the `Treemap`, toggled by `ApplyView`) and refreshes them after a scan, a delete or a load. Every row calls back into the map: `ShowOnMap` selects and zooms, `FilterAndShowMap` sets the filter text and switches the view.

**Themes** (`App.axaml(.cs)`, `Themes/Styles.axaml`)
All control styles are in `Styles.axaml` and reference colors through `DynamicResource`. The dark and light colors are theme dictionaries in `App.axaml`; `App.ApplyTheme` picks the variant (light, dark, or the system's) and lays the chosen look (`UiLooks` in Core) over it, and every open window recolors. On Windows `TitleBarTheme` paints the caption in the same colors. Palettes are per theme (`PaletteTheme`); `AppSettings` keeps one choice for dark and one for light.

**Top lists** (`Services/TopLists.cs`)
The largest files and folders are found with a priority queue in one pass, so the side panel is instant on millions of files.

**Updates** (`Services/Updater.cs`, `UpdateWindow.cs`, `Program.cs`)
Velopack installs per user (Setup.exe) or wherever the user picks (the MSI) on Windows, as a .pkg on macOS and an AppImage on Linux; all update the same way. `Program.Main` runs Velopack's hooks before Avalonia loads. Installed copies check GitHub Releases a few seconds after launch and offer "Install and restart", which downloads a delta package and swaps in the new version. What's new is read from CHANGELOG.md at the new release's tag, so skipped releases are listed too. The portable exe never updates itself.

**Deleting** (`Util/Trash.cs`)
Items go to the Recycle Bin through the Windows shell with undo enabled, to the Finder's Trash on a Mac (so Put Back works) and to the Freedesktop trash through `gio` on Linux. After a delete the node is removed from the tree and its size subtracted from every parent, so no rescan is needed.

## Project structure

```
SpaceSharp.Core/                  the half that knows nothing about a window or an OS (plain net10.0)
├── Models/FsNode.cs              the file and folder tree
├── Layout/                       Squarify.cs, Grouping.cs, LayoutRect.cs (a Rect with no UI framework behind it)
├── Services/                     saved scans (ScanFile), compare, CSV, the filter (FileFilter), insights (DriveInsights), inspect (InspectSummary)
├── Scanning/                     FolderWalker (the System.IO scan every platform has), IPlatformFiles (what it asks the OS)
├── Util/                         strings, languages, sizes, name patterns, command line, app info, settings, the looks
└── Resources/Strings*.resx       UI text: English, Norwegian bokmål and sixteen machine-made translations
SpaceSharp/                       the app (Avalonia, Skia), references SpaceSharp.Core
├── App.axaml(.cs)                startup, the command line, one instance, themes and looks, error reporting
├── MainWindow.axaml(.cs)         toolbar, path row, map, side panel, scan card, status bar, settings application
├── MainWindow.FilterPanel.cs     the filter drawer and rail
├── MainWindow.Insights.cs        the Insights view in the map's place
├── MainWindow.Scans.cs           saved scans, compare, the start screen's tiles, CSV export
├── MainWindow.Tooltip.cs         the hover tooltip on the map
├── AboutWindow, InspectWindow, SettingsWindow, UpdateWindow, ShortcutsWindow, PaletteEditorWindow
├── Controls/
│   ├── TreemapControl*.cs        fields, camera, layout, render, input
│   ├── DonutChart.cs             the donuts in Inspect
│   ├── Icon.cs                   the icon control and the Icons helper
│   └── MapEnums.cs               ColorMode, TreemapItem
├── Services/                     MFT scanner and the elevated helper (Windows), drive kinds, top lists, updater, shell properties, Explorer menu, single instance
├── Util/                         palettes, custom palettes, the palette draft, color text, dialogs, rich text, trash, platform files, title bar, crash log
├── Themes/                       Styles.axaml, Icons.axaml
└── Properties/PublishProfiles/   Portable, Portable-x86, Small, Velopack, Velopack-x86 (Windows; the Mac and Linux builds are plain publishes)
tools/make-mobile-shots.py        docs/m/*.webp (not committed), the screenshots at 1200 px for the website's phone gallery
installer/                        wizard pages, dialog images, release.ps1, sign-test.ps1, brand-msi.ps1
```

## Two projects, and the line between them

`SpaceSharp.Core` targets plain `net10.0` and may not reference Avalonia, Velopack or Win32; the compiler enforces it, since none of those exist for that target. It holds the tree, the layout, scanning, the saved-scan format, the strings and every translation, size formatting, the settings, the looks and the command line. `SpaceSharp` is the app and references Core. The test project references both and runs on Windows, Linux and macOS; the GitHub workflow runs it on Windows only until the Mac and Linux packages ship.

The rule when adding code: if it would work the same on every platform, it goes in Core. Anything that draws, opens a window, talks to the shell, the trash, the registry or the NTFS file table stays in the app, behind an `OperatingSystem.IsWindows()` check or a `[SupportedOSPlatform]` attribute when it is one platform's. Where Core needs something from the platform, it exposes a plain property or interface and the app fills it in: `IPlatformFiles` for sizes and hard links, `AppLanguages.IsRightToLeft` for the window direction.

Until 2.3.0 the Windows app was a separate WPF project; git history has it. Next is mobile: an Android or iOS head means turning `SpaceSharp` into a library with a desktop head beside it, a layout for a phone (one panel at a time), and the Android and iOS workloads; nothing in Core needs to change.

**Add a built-in palette.** Add an entry to `BuildSchemes()` in `Util/Palette.cs` with `FromList(name, theme, colors)`: twelve folder colors as `#RRGGBB`. File colors and file-type colors are derived unless you pass them. Users make their own in the palette editor (`PaletteEditorWindow`, over a `PaletteDraft`: the pure model with the derivations, the generator, the checks and the JSON) or as JSON by hand; `CustomPalettes` reads and writes the files.

**Add a file type.** Extensions and their categories are in `FileCategories` in `SpaceSharp.Core/Util/FileCategory.cs`. The cleanup patterns the panel uses are `CachePatterns` in `Services/DriveInsights.cs`.

**Change theme colors.** The surfaces and text are the looks in `SpaceSharp.Core/Util/UiLooks.cs` (five per theme); the accent, Danger and the Fluent overrides are the theme dictionaries in `App.axaml`. Both dictionaries must define the same keys.

**Add a setting.** A property with a default in `SpaceSharp.Core/Util/AppSettings.cs` (and in `ResetToDefaults`), a row in `SettingsWindow.Build()`, and its effect in `MainWindow.ApplySettings()` or `ApplyLook()` if it changes the map. Map settings also show in the Settings preview, which runs through the same `ApplyLook`.

**Add or change an icon.** The icons are Lucide, as path geometry in `SpaceSharp/Themes/Icons.xaml`, which `tools/make-icons.py` generates: add a line to its `ICONS` table (app name, Lucide name) or a path to `CUSTOM`, run it, then use `{StaticResource Icon.Name}` on a `ToolButton`'s `Tag` or a `controls:Icon`'s `Glyph` in XAML, or `Icons.Get("Name")` / `Icons.Make("Name", size, "TextDim")` in code. `Controls/Icon.cs` draws any of them at any size with the stroke scaled along. The mockup's copies live in `docs/spacesharp-mock.js`; the drive pictures are picked by `Services/DriveKinds.cs`.

**Add a shortcut.** `MainWindow.Window_PreviewKeyDown`, the list in `ShortcutsWindow.cs`, and the README's table.

**Add UI text.** Every string is a key in `SpaceSharp.Core/Resources/Strings.resx` with a Norwegian twin in `Strings.nb.resx`. `LocalizationTests` fails the build if a key used in code or XAML is missing. A new language is one more `Strings.<culture>.resx`; it appears in the language list on its own. Each translation file carries `Meta_MachineTranslated`: `AppLanguages.IsMachineTranslated` reads it, the Settings dropdown groups them under a "Machine translations" heading after the "Translations" group (the same grouped list as the palette picker, sharing the ComboGroupHeader template), and shows a note under the dropdown while such a language is active. Set it to `false` once a native speaker has read the file; English and Norwegian are the only reviewed ones today. Most of the machine-made files were produced in one pass from the English strings with the Windows terminology of each language; `Strings.nb.resx` is the model for tone.

**The icon.** The mark is an isometric amber block with a cube carved out of its front corner. Everything is generated from `tools/make-assets.py` (Python 3 with Pillow and cairosvg): the SVGs, the Windows icon on its rounded Graphite tile, the 256 px PNG the app shows, the header, the wordmark and the installer images (the MSI's banner and dialog art, "Facets", is a list of triangles in that file). Change the geometry or colors there, not in the files.

**Screenshots.** `docs/spacesharp-mock.js` draws the window with sample data so no real file names end up online; the website (spacesharp.app, kept outside this repository) embeds it as the live preview and `tools/screenshot-mockup.html` wraps it in controls. Open the tool in a browser, pick a theme, palette and scene, and capture the window, or run `node tools/screenshots.js` (needs Playwright) to regenerate every PNG the README uses, then `python tools/make-mobile-shots.py` for the small WebP copies the website shows on phones (written to `docs/m/`, which is ignored by git; copy them and the PNGs into the site when they change). When the app's UI changes, the engine has to follow: it is a drawing, not a build of the app.

**Right-to-left.** `AppLanguages.ApplyDirection(window)` sets a window's `FlowDirection` to RightToLeft when the culture reads right to left; `TitleBarTheme.Attach` calls it, and every window goes through that, so a new window needs nothing beyond the `Attach` call it has anyway. Menus and tooltips inherit the direction from their window. (Overriding the property metadata instead fails: `Window`'s static constructor overrides it too.) `TreemapControl`, `DonutChart` and `Icon` pin themselves to LeftToRight because they draw with coordinates. When adding a control that draws with a DrawingContext or positions things by x, do the same, and prefer `HorizontalAlignment`/`TextAlignment` over fixed margins for anything that should swap sides. To try it without a translation, put `"Language": "ar"` in `%AppData%\SpaceSharp\settings.json` (the dropdown only lists languages that have a resx): the strings fall back to English, mirrored.

**Crash reporting.** The publish profiles embed the symbols (`DebugType` embedded), so the stack traces in a log name the file and line. `Util/CrashLog.cs` writes unexpected errors to `%LocalAppData%\SpaceSharp\logs` and builds the prefilled GitHub issue link; `App.xaml.cs` hooks the dispatcher, the AppDomain and unobserved tasks. To try it, press Ctrl+Alt+Shift+F12 in the main window (an exception on the UI thread) or Ctrl+Alt+Shift+F11 (one in a background task, which reaches the dialog when the finalizer notices it, so it can take a moment). Neither is in the shortcut list.

**Name, version and author** come from `SpaceSharp.csproj` and appear in About and in the exe's properties. Tag releases with the bare version number; the update window reads the changelog at that tag.

**Releasing.** Finish the changelog section (take "(unreleased)" off its heading), make sure `<Version>` matches and the version badge at the top of the README says the same (it is a static badge; the live one timed out behind GitHub's image proxy), run `python tools/make-assets.py` if the major.minor changed so the header badge follows, commit and push. Then, on the PC with the certificate (SimplySign Desktop signed in, or the card inserted), with `$env:GITHUB_TOKEN` set: `.\installer\release.ps1 -Sign <thumbprint> -Upload`. It runs `installer/release.ps1` in three stages: Build (tests, the four publishes, the app exes signed), Pack (vpk with everything inside the packages plus Setup.exe and Update.exe signed, the branded MSIs signed, the 1.x bridge files, a check of every signature) and Upload (a draft release). The thumbprint is the certificate's SHA-1 in certmgr.msc. Read the draft on GitHub, press Publish, then `git tag 2.3.0 && git push --tags` so the tag marks the released commit. GitHub Actions only runs the tests; nothing is built there. A broken run reaches nobody: installed copies only see published releases, and the draft is yours to delete.
