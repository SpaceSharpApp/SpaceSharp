<p align="center">
  <img src="docs/header.png" width="800" alt="SpaceSharp: see where your disk space went">
</p>

<p align="center">
  <b>See where your disk space went.</b><br>
  A fast, zoomable treemap of your drives for Windows, in the spirit of <a href="https://www.stardock.com/products/spacemonger/">SpaceMonger</a>.
</p>

<p align="center">
  <a href="https://github.com/ClearanceClarence/SpaceSharp/releases/latest"><img src="https://img.shields.io/github/v/release/ClearanceClarence/SpaceSharp?label=version&color=F5B82E" alt="Latest version"></a>
  <a href="https://github.com/ClearanceClarence/SpaceSharp/actions/workflows/tests.yml"><img src="https://img.shields.io/github/actions/workflow/status/ClearanceClarence/SpaceSharp/tests.yml?branch=main&label=tests&color=6FC2B0" alt="Test status"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-F5B82E" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-74A6CC" alt="Windows 10 and 11">
  <img src="https://img.shields.io/badge/.NET-10-8C84D6" alt=".NET 10">
</p>

<p align="center">
  <a href="https://github.com/ClearanceClarence/SpaceSharp/releases/latest/download/SpaceSharp-win-x64.exe"><img src="https://img.shields.io/badge/Download-SpaceSharp--win--x64.exe-F5B82E?style=for-the-badge" alt="Download SpaceSharp-win-x64.exe"></a>
  <a href="https://clearanceclarence.github.io/SpaceSharp/"><img src="https://img.shields.io/badge/Website-clearanceclarence.github.io%2FSpaceSharp-21262D?style=for-the-badge" alt="Website"></a>
</p>

<p align="center">
  <img src="docs/screenshot.png" alt="SpaceSharp showing a drive as a treemap, with sample data">
  <br>
  <sub>Every file and folder is a box sized by the space it uses. Sample data.</sub>
</p>

Scan a drive, and the whole thing fills the window: folders are frames with a title bar, files are boxes inside them, and the biggest boxes are where your space went. Zoom into any folder, filter in plain words, see what changed since last time, and send what you do not need to the Recycle Bin without leaving the map.

One portable `.exe`, or an installer that updates itself. Free and open source.

## What it does

| | |
|---|---|
| **Scans a whole drive in seconds** | Reads the NTFS file table directly (asks for administrator rights once); a parallel folder walk covers everything else. Hidden and system files included, junctions skipped. |
| **One map, shaded your way** | Folders are frames, files are cushioned boxes. Shading depth, light direction, title bars, text colors and grid are sliders in Settings with a live preview. |
| **Filter in plain words** | `videos over 500MB older than 1 year` dims everything else and keeps the layout, so you see where the matches are. A drawer with checkboxes and sliders writes the same filter for you. |
| **Three views under the map** | Space by file type, places that are usually safe to clear (Windows.old, caches, the Recycle Bin, old Downloads) with one-click actions, and a histogram of when files were last changed. |
| **Remembers every scan** | Opens with yesterday's map on screen. Color by change shows what grew, shrank or appeared since last time. |
| **Inspect anything** | Size and share, where the space is inside, donuts by type and by age, and a callout when one thing holds most of a folder. |
| **Fifteen palettes** | Eight for the dark theme, seven for the light one, twelve hues each, plus your own as JSON. |
| **Cleans up safely** | Everything goes to the Recycle Bin and the map updates without a rescan. |

<table align="center">
  <tr>
    <td align="center" width="50%"><a href="docs/shot-filter.png"><img src="docs/shot-filter.png" alt="The filter drawer with matches lit and everything else dimmed"></a><br><sub><b>Filter.</b> Check types, set size and age, and the map dims to the matches.</sub></td>
    <td align="center" width="50%"><a href="docs/shot-change.png"><img src="docs/shot-change.png" alt="Color by change: what grew, shrank or appeared since the last scan"></a><br><sub><b>What changed.</b> Grew warm, shrank cool, new in amber.</sub></td>
  </tr>
  <tr>
    <td align="center"><a href="docs/shot-inspect.png"><img src="docs/shot-inspect.png" alt="The Inspect window for a folder"></a><br><sub><b>Inspect.</b> Where the space is, by type and by age.</sub></td>
    <td align="center"><a href="docs/shot-settings.png"><img src="docs/shot-settings.png" alt="Settings, Treemap page with the preview map"></a><br><sub><b>Shading.</b> Every slider shows in the preview as you drag it.</sub></td>
  </tr>
</table>

## Download

For 64-bit Windows 10 and 11 (Windows on ARM runs them through x64 emulation). Get them from the [latest release](https://github.com/ClearanceClarence/SpaceSharp/releases/latest).

| File | Best for |
|---|---|
| **SpaceSharp-win-x64.exe** | One portable file. Nothing to install; you download new versions yourself. |
| **SpaceSharp-win-x64-Setup.exe** | One-click install to your profile, no admin rights, updates itself. `--installto "D:\Apps\SpaceSharp"` picks another folder. |
| **SpaceSharp-win-x64.msi** | A regular installer where you choose the folder and whether it is for you or everyone. Updates itself. |

32-bit Windows gets the same three with **x86** in the name: **SpaceSharp-win-x86.exe**, **SpaceSharp-win-x86-Setup.exe** and **SpaceSharp-win-x86.msi**. They update from their own channel, so an x86 install never pulls an x64 package. A 32-bit process has less memory to work with, so a drive with several million files is better scanned with the x64 build.

> [!NOTE]
> The exe is not code-signed, so SmartScreen may show "Windows protected your PC" the first time. Click **More info**, then **Run anyway**.

## Using it

1. Click a drive in the **Drives** list, or **Scan folder** for one folder. Windows asks for administrator rights for a drive scan; without them, protected folders are skipped and marked.
2. The biggest boxes are the biggest space users. Hover for the path and size, double-click a folder to zoom in, Backspace or the mouse back button to go up.
3. Right-click a box for Inspect, Open, Show in Explorer, Properties, copy, filter to its type, select its folder, or Recycle Bin.
4. Press **B** for the panel under the map, **Ctrl+F** for the filter, **L** for the side panel with your drives and the largest files, and the gear or **Ctrl+,** for Settings.

### The filter

The drawer (Ctrl+F) has file types as rows with a share bar, size and age controls, Files or Folders, and presets such as Large files and Untouched for 2 years. Its name box also takes the full syntax, and anything you type becomes checked rows and set controls. Settings › Map › Filter layout switches to a one-row rail of dropdown pills instead.

| You can type | Meaning |
|---|---|
| `*.mp4 *.mkv`, `.iso`, `report` | Name patterns (OR-ed), an extension, or text the name contains |
| `videos`, `photos`, `music`, `archives`, `programs`, `documents`, `code`, `type:video,audio` | File type |
| `>1GB`, `over 500 MB`, `at least 100MB` | Minimum size (a bare number means MB) |
| `<10MB`, `under 1 GB` | Maximum size |
| `older than 2 years`, `not modified in 6 months`, `unused for 1 year` | Not changed for that long |
| `newer than 30 days`, `last 2 months` | Changed recently |
| `files`, `folders`, `is:file`, `is:folder` | Only files or only folders |

Terms combine. **Select matches** (Ctrl+A) selects all of them; Del sends them to the Recycle Bin in one go.

### Settings

Everything applies at once and is saved. The pages:

- **General**: language, update check, confirm before the Recycle Bin, "Scan with SpaceSharp" in Explorer's right-click menu, and the command-line options.
- **Appearance**: theme (match Windows, light, dark).
- **Treemap**: a preview map of sample data pinned at the top, then Colors (palette per theme, Color by, custom palettes), Shading (cushion on or off, presets, Depth, Spread, Tone, Fade, Light from), Text and title bars (label size, font, folder and file text colors, outlined labels, title bar padding and tint), Grid and highlight, and Layout (density, bias, padding).
- **Map**: size measure (file size or size on disk), free space, merge single-folder chains, hover details, filter layout, side panel, bottom panel, animated zoom.
- **Scanning**: fast NTFS scan, names to leave out, reopen the last scan, how long saved scans are kept, hidden and system files, hard links counted once.

**Color by** is Top folder (one hue per top-level folder, lighter at each level), Depth (one color per level, like SpaceMonger), File type (with a legend), or Change (since the last scan). **Palettes**, dark: Graphite, Ocean, Sunset, Forest, Vivid, Aurora, Monochrome, Color-blind safe; light: Pastel, Paper, Candy, Nordic, Earth, Retro, Color-blind safe (light). Your own go in `%LocalAppData%\SpaceSharp\palettes` as JSON; Settings › Custom palettes › Open folder writes an example and a README there.

```json
{ "name": "Mine", "theme": "dark", "folders": ["#5B8DEF", "#49B86B", "#E8C547", "#E8864A"] }
```

### Command line

| | |
|---|---|
| `SpaceSharp.exe D:\` | Scan a drive or folder right away |
| `SpaceSharp.exe scan.sscan` | Open a saved scan |
| `SpaceSharp.exe --compare old.sscan D:\` | Scan `D:\` and compare it with a saved scan |
| `SpaceSharp.exe --help` | All options |

Only one SpaceSharp runs at a time; starting it again hands the request to the open window.

### Keyboard and mouse

| Input | Action |
|---|---|
| Double-click, Enter | Zoom to a folder |
| Mouse wheel, `+` / `−` | Zoom in or out |
| Left or middle drag | Pan |
| Backspace, mouse back button | Up one folder |
| Home, Ctrl+0 | Whole map |
| F5 | Rescan |
| Ctrl+F | Filter |
| Ctrl+A | Select every file matching the filter |
| Del | Move the selection to the Recycle Bin |
| Ctrl+I | Inspect |
| Ctrl+C | Copy the selected paths |
| Ctrl+S, Ctrl+O | Save or open a scan file |
| Alt+Enter | Windows Properties |
| Ctrl+click, Shift+click | Select several |
| K | Next color mode |
| B | Panel under the map |
| L | Side panel |
| G | Density: Everything (no grouping) and back |
| Ctrl+, | Settings |
| Esc | Cancel a scan, close the filter |
| F1 | About and shortcuts |

### Where things are stored

| What | Where |
|---|---|
| Settings | `%AppData%\SpaceSharp\settings.json` |
| Saved scans | `%LocalAppData%\SpaceSharp\scans\*.sscan` |
| Custom palettes | `%LocalAppData%\SpaceSharp\palettes\*.json` |
| The installed app | `%LocalAppData%\SpaceSharp\` |

Nothing leaves the machine except the update check against GitHub Releases, which only installed copies make.

## Building

Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download). Open `SpaceSharp.sln` in Rider or Visual Studio, or:

```powershell
dotnet run --project .\SpaceSharp\SpaceSharp.csproj
dotnet test
```

The only NuGet dependency is Velopack, for the installer and updates. `SpaceSharp.Tests` covers what does not need a screen: layout and grouping, the filter parser, the panel's numbers, formatting, and the tree operations behind delete and free space. Every push runs them on GitHub Actions; releases are built locally with `installer\release.ps1`, which runs the tests, publishes the portable exe, packs the installer and MSI, and can upload the lot with `-Upload`. Portable, Portable-x86 and Small (needs the .NET Desktop Runtime) exes come from the publish profiles in `SpaceSharp/Properties/PublishProfiles`; the script builds the x64 and x86 channels side by side (`-NoX86` skips the second).

How the scanner, layout, renderer, filter and updater work, and where to add a palette, a file type or a translation, is in [DEVELOPING.md](DEVELOPING.md).

## Known limitations

- Windows only; it is WPF.
- Size on disk is an estimate for files stored inside the MFT and leaves out alternate data streams.
- Hard links count once per name unless "Count hard links once" is on, so `WinSxS` can look bigger than it is.
- Junctions and symbolic links are not followed; the target counts where it lives.
- Online-only cloud files show their full size in file-size mode; size on disk shows the local footprint.

## Feedback

[Report a bug](https://github.com/ClearanceClarence/SpaceSharp/issues/new?template=bug_report.yml) (About › Report a bug fills in your version and Windows details), [suggest a feature](https://github.com/ClearanceClarence/SpaceSharp/issues/new?template=feature_request.yml), or open a [blank issue](https://github.com/ClearanceClarence/SpaceSharp/issues/new). Code, palettes and translations: [CONTRIBUTING.md](CONTRIBUTING.md). Screen readers, keyboard and low vision: [ACCESSIBILITY.md](ACCESSIBILITY.md). Releases: [CHANGELOG.md](CHANGELOG.md).

## Credits and license

Squarified treemaps by Bruls, Huizing and van Wijk (2000). Inspired by [SpaceMonger](https://www.stardock.com/products/spacemonger/). Interface icons are Segoe Fluent Icons, built into Windows. The color-blind safe palette is Okabe and Ito's.

[MIT License](LICENSE). Made by ClearanceClarence.
