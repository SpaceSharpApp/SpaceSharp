# SpaceSharp 1.x changelog

Every 1.x release, newest first. The current changelog is [CHANGELOG.md](CHANGELOG.md).

## 1.6.1

### New icon
- SpaceSharp has a new mark: an amber block with a cube carved out of its front corner, drawn in isometric with no background tile. It replaces the nine-cell tile everywhere: the window and taskbar icon, the start screen and About window, the installer pages, the README header, the landing page and the social preview. The carve is shallower at small sizes so the icon stays legible at 16 px.
- Setup.exe and Update.exe show a proper splash while they work: a Graphite card with the mark, the wordmark and a moving amber sweep (`installer/splash.gif`, drawn by tools/make-assets.py), instead of the bare icon on white.

### Fixes
- The update prompt's What's new now covers every release you skipped. Someone updating from 1.4.0 to 1.6.0 used to see only the 1.6.0 notes; the window is now titled "What's new since 1.4.0" and lists each release in between, read from the changelog at the new release's tag, with "See all changes" opening the full changelog. The notes packed into the update are still used when the changelog cannot be fetched.
- Installing an update no longer pops up a stock Windows "Installing Update" box with a blue i. The update applies silently: the app closes, the files are swapped, and the new version opens a few seconds later. The update dialog says "Restarting into 1.6.0" once the download is done.
- The installer's welcome and finish pages have their paragraphs back (they are RTF now; the markdown-to-RTF step flattened them), and the finish page says what F1 does correctly: it opens About, where the keyboard shortcuts are; the gear or Ctrl+, opens the settings.


## 1.6.0

### Inspect, redrawn
- The Inspect window (Ctrl+I) is laid out around the question you opened it for. The size is the one big number; the path is a row of breadcrumbs, and clicking one zooms the map there. Under the name, three lines say something about the item instead of restating the map: its share of the parent and where it ranks among its siblings ("48% of Nexus Stuff, the largest of its 6 folders"), what it is made of ("Mostly images (61.2 GB) and archives (6.8 GB), median file 2.3 MB"), and its age and change ("Last changed 3 months ago, 78% untouched for over a year, unchanged since the last scan"). Depth, average file size, "directly inside" and the fact grid are gone.
- **Where the space is** lists the five largest things anywhere inside, not only direct children, each on one line with a bar scaled to the folder: a folder that is almost entirely one child is skipped in favor of that child, so you see `Google\Chrome\User Data` rather than `Google`. Clicking a folder row zooms the map to it. A closing line says how much the rest of the files share.
- **By type** and **By age** are donuts with the legend beside them; hovering a slice or a legend row shows it in the center. Type colors follow the active palette, the age bands (this month, this year, 1 to 3 years, older) run from bright to dim amber. Age is new: it tells you how much of a folder has not been touched in over a year.
- A callout appears when one thing holds most of a folder: "65% of this folder is one thing: AppData at 93.15 GB."
- The hover tooltip on the map is three lines: name and size, then "Folder · 583 files · 25% of Images", then "Mostly images · changed 27 Apr 2026" with the change since the last scan in the Change colors when a comparison is loaded. Folder count, size on disk, share of the drive, the largest child and the right-click hint are gone from the tooltip; Inspect has them. Three of its labels were hardcoded English and are now translated.
- On Windows 11 every window's title bar is painted in the theme's background color with the theme's text, so the caption and the toolbar read as one surface (Graphite #0D1117 in dark, the light grey in light). Windows 10 keeps the dark or light system caption as before.
- The side panel can be resized: drag its right edge to make it wider or narrower, and drag the line between Drives and Largest items to give either list more room. Both sizes are remembered.
- The window is resizable and remembers its size; the header stays while the rest scrolls. Copy details copies the same content as before, in the new order.

### Fast scan without the restart
- The fast NTFS scan no longer needs SpaceSharp itself to run as administrator. When you scan a drive, Windows asks for permission (the UAC prompt), a small elevated helper reads the file table and hands the finished map back to the window you already have, and the helper exits. The window never closes and keeps your zoom, filters and settings. Say no and the normal scan runs; you are not asked again until the next start. The start screen's "Restart as administrator" line is gone.

### Fixes
- Neon is gone. Its dark frames never worked with the single style and its files were either glare or mud; Aurora takes its slot, a saturated mid-tone palette for the dark theme.
- Tiles, Cards and Soft no longer turn a folder of many small files into a field of dots. The gap between boxes and the corner radius now shrink with the box: under about 40 px the gap narrows to a hairline and then disappears, and corners are never rounder than a sixth of the box's shorter side. Cards under 24 px also drop their shadow, which at that size was only a dark halo. The gap is decided per folder from the typical size of its children, so neighbors never get different gaps and the seams stay straight. Large boxes look exactly as before.
- Classic no longer shades folders, only files and groups. A folder is a frame around its children, and shading every frame stacked darkness with each level of nesting, so deep parts of the tree went muddy; the frames are now flat and the files inside keep their cushion. Boxes under 28 px get a lighter cushion.
- Cards and Tiles had drifted into the same look. Cards now sets its folders a step darker and its files a step lighter, so files read as chips lying on a card; Tiles stays flat.
- Classic and Bands title bars separate name, size and count with a dot instead of a dash.

## 1.5.1

### Fixes
- Neon is gone. Its dark frames never worked with the single style and its files were either glare or mud; Aurora takes its slot, a saturated mid-tone palette for the dark theme.
- Boxes never move when you zoom. Which small items are grouped into a "312 files" box is now decided from the data alone, not from how many pixels the folder has on screen, so zooming in no longer lays a folder out again. A group that gets room shows its members inside its own box, laid out flat under one title.
- Classic's shading is softer: a light top-left and a slightly darker bottom-right instead of a white-to-black sweep, which read as stripes when many boxes sat together.

### Changes
- Only one SpaceSharp runs per session. Starting it again, from Explorer, a shortcut or the command line, brings the open window forward and hands it the request (a drive to scan, a saved scan to open), so a second window never appears. Restarting as administrator or for a language switch hands the instance to the new copy.

## 1.5.0

### Treemap options
- A new Treemap section in Settings: **Density** (Sparse to Maximum, how many small items are drawn before grouping), **Bias** (a slider from Horizontal through Equal to Vertical, steering the squarified layout toward wide or tall boxes), **Padding**, **Border** width, label **Font**, and what file boxes and folder titles show (center names, sizes, file counts). All take effect immediately. The separate "Group small items" setting is gone: Density › Everything is the no-grouping choice, and G now switches between your density and Everything.

### Changes
- Sliders are drawn in the app's style: a thin track, amber up to the thumb, a round amber thumb. Destructive buttons that are not the main action (Delete all saved scans) are outlined in red text.
- Settings › Scanning › Keep saved scans for: a slider from a week to forever (default 90 days); older automatic saves are deleted at startup, and a button deletes them all now. Files you saved yourself are never touched.
- The start screen shows your recent saved scans: one click reopens yesterday's map, compared with the scan before it. Scan a folder and Open a saved scan sit under the list, and the administrator offer for the fast scan is one line at the bottom of the map area instead of a card.
- About is a short list instead of a page: version with a Check button, What's new, Keyboard shortcuts, Report a bug, Suggest a feature, Source on GitHub, Credits, each one row. The shortcut reference moved to its own window, grouped into Navigate, Select and act, View and files.
- Leave out is a list of chips instead of a text box: add a name or pattern with Enter, remove one with its ×, and the common ones (node_modules, $Recycle.Bin, *.tmp, .git, System Volume Information) are one click away.
- Settings is split into tabs: General (language, updates, safety, Windows), Appearance, Treemap, Map and Scanning. Same options, one page at a time.
- The update notice is a dialog instead of a bar across the window: install and restart, **What's new**, or later. What's new shows a short version of the release notes (carried inside the update package, with the GitHub release as fallback) and links to the full changelog on GitHub. The About window's Check for updates opens the same dialog.

## 1.4.0

### Translations
- Ready for translation: every piece of user-visible text (about 450 strings across the map, menus, settings, filter panel, Inspect, About, dialogs and status line) now comes from `Resources/Strings.resx`. Settings › Language lists every language that has a `Strings.<culture>.resx` in the build, with "Same as Windows" as the default, and switching restarts the app. Ships in English and Norwegian bokmål; see CONTRIBUTING to add a language. The filter grammar stays English on purpose.
- Norwegian bokmål is the first translation (`Resources/Strings.nb.resx`); Settings › Språk lists it next to "Same as Windows" and English.

### Command line and Explorer
- `SpaceSharp.exe D:\` scans right away, `SpaceSharp.exe scan.sscan` opens a saved scan, `--compare old.sscan D:\` scans and compares, `--help` lists the options.
- Settings › Windows › "Scan with SpaceSharp" in Explorer adds a right-click entry for folders, drives and the folder background (current user only, no administrator rights). The entry is refreshed to point at the running exe on every start while the setting is on.

### Changes
- Every prompt and error (delete confirmation, language switch, reset, failures) uses the app's own dialog in the app's colors and type instead of the Windows message box.
- Tests cover the scan file, comparison, folder splicing, exclusion patterns and the translations: every key used in code or XAML must exist, every translation must match the English keys and placeholders, and Norwegian must load. The GitHub workflow only runs the tests; it never builds, packs or uploads release assets.
- The dark theme moves to Graphite: GitHub's dark greys (#0D1117 base, #161B22 panels, #21262D controls) replace the warmer charcoal, so the app, the README and the repository page share one set of tones and the amber reads richer. The icon tile, installer images, website, screenshot tool and social images follow.
- The README header is redrawn as a quiet abstract treemap on GitHub's page color, with no screenshot.
- The start-screen offer to restart as administrator appears on every run without administrator rights; "Not now" hides it for the run.

## 1.3.0

### Fast NTFS scan
- Whole drives are scanned by reading the NTFS Master File Table directly, the way WizTree does, so a drive with a million files is mapped in a few seconds. Needs administrator rights; on the start screen the app offers to restart elevated every time it runs without administrator rights ("Not now" hides it for this run; the Fast NTFS scan setting turns it off). Single folders, non-NTFS volumes and non-elevated runs use the folder walk as before. Hard links are always counted once on the fast path. Settings › Scanning › Fast NTFS scan turns it off. The status line says "file table" when it was used.

### Saved scans and comparison
- Every drive or folder scan is saved automatically (`%LocalAppData%\SpaceSharp\scans`). The app opens with the last map already on screen; F5 rescans. Ctrl+S saves a scan as a file, Ctrl+O opens one, so a scan of another machine can be looked at anywhere.
- Each new scan is compared with the previous one of the same place, provided both were made the same way (file table or folder walk, administrator or not, hard-link and hidden-file settings); otherwise the status line says why they are not compared, since a folder walk without administrator rights cannot see System Volume Information and counts hard-linked names separately, which would show up as false growth and shrinkage. A new color mode, **Change**, colors what grew in warm shades, what shrank in cool ones, unchanged grey and new items amber, with a legend. The side panel gets a **Changes** tab listing what grew or appeared most, Inspect shows "Since last scan", and the right-click menu can compare with any saved scan or stop comparing.
- **Rescan this folder** in the right-click menu re-walks one folder and splices it into the map; zoom and the rest of the tree stay as they were.
- **Export to CSV** from the right-click menu: largest files, a folder's contents, the filter matches, or the changes since the last scan. A first, plain version: fixed columns (path, name, type, sizes, modified, change), UTF-8, no options yet.
- **Leave out** in Settings › Scanning: names to skip entirely, one wildcard per line (`node_modules`, `$Recycle.Bin`, `*.tmp`), for both the file-table scan and the folder walk.

### Fixes
- Neon is gone. Its dark frames never worked with the single style and its files were either glare or mud; Aurora takes its slot, a saturated mid-tone palette for the dark theme.
- The hover and selection frames follow the map style: rounded corners on Tiles, Cards and Soft, drawn inside the box so they no longer cover the neighbors' labels.

### Changes
- `TreemapControl` is split into partial files by concern (fields, camera, layout, rendering, input) with the enums in `MapEnums.cs`; no behavior change.
- Classic always has its soft shading; the Cushion setting and the C shortcut are gone.
- Custom palettes: drop JSON files into %LocalAppData%\SpaceSharp\palettes (Settings › Appearance › Custom palettes › Open folder writes an Example.json and a README). Reload from Settings or restart; files that can't be read are listed with the reason.
- New website: the hero is the app itself, drawn live in the browser with sample data, with controls for scene, style, color mode, palette and theme. Spec-style feature list, a comparison with WizTree, WinDirStat and SpaceMonger, and a cleaner download section.
- The window title shows the version: "SpaceSharp 1.2.3" (with "(Administrator)" after it when elevated).

## 1.2.2

### Fixes
- Neon is gone. Its dark frames never worked with the single style and its files were either glare or mud; Aurora takes its slot, a saturated mid-tone palette for the dark theme.
- Zooming no longer rearranges the map. Title bars and borders keep a fixed pixel size, so a folder's content area changes shape slightly as you zoom, and the layout could flip a row from horizontal to vertical along the way; the box you were zooming into then jumped somewhere else. Each folder's layout is now computed once and only stretched with the zoom. Small items still ungroup as they get room, but that only affects the folder they are in.

### Changes
- Two more label sizes, Smaller and Smallest, in Settings › Appearance › Label size.
- New color mode, **Top folder**, and it is the default: each top-level folder gets one hue, and everything inside it is that hue, a step lighter at each level, so the hierarchy reads at a glance. "Depth" (a color per level, the SpaceMonger way) and "File type" are still there.
- New **Flat** map style: plain fills, thin lines, no gaps, no shading. It replaces Terraces, whose one-hue-per-branch idea is now the Top folder color mode and works with every style.
- Map styles reworked. Cards: tighter spacing, folders keep their palette color instead of going muddy, lighter shadow. Bands: the bright borders are gone, replaced by a faint dark line, and folder bodies are less washed out. Soft: smaller gaps, corners and title bars, so less space goes to nothing.
- Cushion shading is off by default (C turns it on).
- **Inspect** (Ctrl+I, or from the right-click menu): a window with everything about an item or a selection. Size and size on disk with exact bytes, share of the parent folder and of the drive, file and folder counts, average file size, newest change, and for folders the largest items inside and the space taken by each file type, all with bars. Copy details puts it on the clipboard as text.
- The right-click menu is reorganized and gains Inspect, Properties (the Windows dialog, also Alt+Enter), Copy name, "Show only *.ext files", and "Select everything in this folder".
- The hover card shows the item's share of the whole drive, the largest item inside a folder, and a hint for the menu and Inspect.
- K cycles the color mode, next to S for map style.
- The scanning card is calmer: no logo, the percentage sits on the right of the title, and the current folder is a single quiet line under the progress bar next to the speed and elapsed time.
- The filter panel is tidier: a title bar with a close button, sections separated by lines instead of stacked headings, and a footer that shows the live match count next to proper Clear and Done buttons. Clear is disabled when there is nothing to clear.

## 1.2.1

### Fixes
- Neon is gone. Its dark frames never worked with the single style and its files were either glare or mud; Aurora takes its slot, a saturated mid-tone palette for the dark theme.
- Age filters without a number now work as documented: `modified in the last week`, `older than a year` and `not opened in one month` mean one week, one year and one month. Before, the words fell through to the name search.

### Appearance
- New mark: nine cells on a charcoal tile whose gutters form the `#` in Sharp, with brightness following size. One brand color (amber) instead of four. New app icon at every size, README header, social preview, landing-page logo and installer images, all generated from `tools/make-assets.py`.

### Project
- Unit tests (`SpaceSharp.Tests`, xUnit) for the treemap layout, the filter parser and matcher, size formatting and the tree operations behind delete and free space.
- GitHub Actions builds and runs the tests on every push and pull request.
- Dependabot keeps NuGet packages and workflow actions up to date.
- `installer/release.ps1` builds every release asset locally; `installer/brand-msi.ps1` (now in the repository) brands the MSI and fixes the desktop shortcut description.

## 1.2.0

The theme of 1.2.0 is finding things. Earlier versions showed you the map; this one lets you ask it questions, pick out what you want to remove, and clear it in one go. It also brings six map styles, a redesigned side panel with your drives, and a set of readability options.

### Filter and highlight
- A filter box sits above the map. Everything that doesn't match is blended toward the background; the layout stays put, so you can see *where* the matches are. Folders that contain a match stay lit, so the path to every hit remains visible.
- The status line explains what was understood in plain words, for example "1,204 files · 48 GB: video, over 500 MB, untouched for 2 years", so a misread query is obvious right away.
- **Plain-language terms.** Type it the way you'd say it: `videos over 500MB not touched in 2 years`, `photos larger than 10 MB`, `.iso`, `installer`. Everything you type must match.
  - Name: any text (`report`), a pattern (`*.mp4 *.mkv`, patterns are OR-ed), or an extension (`.iso`).
  - File type: `videos`, `photos`, `music`, `archives`, `programs`, `documents`, `code`, or `type:video,audio`.
  - Minimum size: `>1GB`, `over 500 MB`, `larger than 2 GB`, `at least 100MB`. A bare number means megabytes.
  - Maximum size: `<10MB`, `under 1 GB`, `smaller than 500MB`.
  - Age: `older than 2 years`, `not modified in 6 months`, `unused for 1 year`, `over 3 years old`, `newer than 30 days`, `modified in the last week`, `last 2 months`.
  - Kind: `files`, `folders`, `is:file`, `is:folder`.
  - Filler words such as "and", "with", "that" and "show" are ignored.
- **Filter panel.** The funnel button in the box opens a panel for people who'd rather click: quick filters (Large files, Big videos, Installers & archives, Untouched for 2 years, Big and old, Recently changed), a name field, file type chips, Larger than / Smaller than drop-downs from 1 MB to 50 GB, a Not modified for drop-down from 1 month to 5 years, and a files/folders switch. Every control writes the filter text into the box, and opening the panel reads the current text back, so the two never disagree.
- **Select matches** (Ctrl+A) selects every matching file; Del then sends them all to the Recycle Bin.
- Ctrl+F focuses the box, Esc clears it, Enter applies immediately. Typing is debounced so the map doesn't redraw on every keystroke.
- Files now record their last modified date during the scan (folders take the newest date inside), which the age terms rely on.

### Side panel with drives and largest items
- A panel on the left of the map, open by default, toggled with the panel button in the toolbar or **L**.
- **Drives** replaces the drive drop-down. Every ready drive is listed with its letter and label, free space, a usage bar, "used of total" and the file system (or Removable, Network, Optical). Click a drive to scan it. The list refreshes after scans and deletes so free space stays current.
- **Largest items** has three tabs. Files and Folders show the 200 largest with a size bar under each row; clicking a row selects it and zooms the map to it. Types shows space per file extension with the file-type color; clicking a type puts `*.ext` into the filter box.
- The lists are built with a priority queue in one pass over the tree, so they appear instantly even on drives with millions of files, and they follow the current size measure.
- The "Scan drive" toolbar button is gone; "Scan folder" is now the highlighted button.

### Multi-select and batch delete
- Ctrl+click adds or removes items; Shift+click selects a range of siblings.
- The status bar shows "12 items selected · 4.2 GB · 318 files".
- Del, or the right-click menu, moves everything selected in a single Recycle Bin operation with one confirmation that lists the first eight paths. If a folder and something inside it are both selected, only the folder is sent. Items that fail to delete (for example, a file in use) are reported, and everything that did go is removed from the map.
- Ctrl+C copies all selected paths, one per line.
- Right-clicking an item outside the selection selects just that item; inside the selection keeps it.

### Hover details
- After the mouse rests on a box for about half a second, a card appears beside it with the name, folder, size, size on disk when it differs, file and folder counts, type, last modified date with a relative age such as "3 years ago", share of the current folder, and a note when content couldn't be read.
- Can be turned off in Settings → Map → Hover details.

### Map styles
- Six styles, chosen from the new **Style** drop-down in the toolbar, in Settings, or by pressing **S** to cycle:
  - **Classic**: title bars, 1 px borders, cushion shading (the previous look).
  - **Tiles**: flat colors, 3 px gaps, softly rounded, folder names as small uppercase captions.
  - **Cards**: folders as raised cards with a soft shadow and bold title, files as flat rounded chips.
  - **Bands**: a deep title band with bold light text, lighter folder body, thin light borders, no shading.
  - **Terraces**: each top-level folder takes one palette color and everything inside gets darker with depth. Works with every palette.
  - **Soft**: rounded pastel blocks with gaps and a gentle top-to-bottom sheen.
- Spacing is tuned so the gap between siblings matches the space between a folder's edge and its children.
- Cushion shading and its C shortcut now apply to the Classic style only.

### Readability
- **Label size**: Normal, Large or Larger. Title bars, file labels and the room a folder needs before it gets a title all scale with it.
- **Outlined labels**: a thin outline in the opposite tone around every label, so text stays readable on any color in any style.
- Label text picks dark or light automatically per box; this now covers the darker fills of Terraces and Bands.

### Layout
- **Folder chains merge more often.** A chain like `Steam › steamapps › common` used to merge only when each folder had exactly one child. Now the child needs to hold at least 97% of the folder, so a few stray files no longer break the merge into stacked title bars.
- **Small items are grouped.** Children that would get less than about 30 × 22 pixels are replaced by a single darker box labeled "812 files" with their combined size. Zooming in gives them more room and they appear individually again. Hovering the group explains what it is; double-clicking zooms into its folder. Toggle with **G** or in Settings.

### Scanning dialog
- Redesigned: the app icon and a title such as "Scanning C: Windows · 63%", three large counters (size found, files, folders), a rate line ("12.4k files per second") and elapsed time.
- For whole-drive scans the progress bar is real, based on the drive's used space; for folder scans it sweeps.
- Long paths are shortened in the middle so the drive and the last folders stay visible.

### About window and feedback
- Shortcuts are listed in two columns, and the window is wider.
- New Feedback section: **Report a bug** opens the GitHub bug form with the version, Windows details and install type already filled in; **Suggest a feature** opens the feature form; **GitHub** opens the repository.
- Issue forms for bugs and feature requests live in the repository, so new issues start with the right questions.

### Settings
- New rows: Map style, Label size, Outlined labels, Hover details, Show side panel, Group small items.
- The side panel setting replaces the earlier "Show largest items panel".

### Shortcuts added
| Key | Action |
|---|---|
| Ctrl+F | Filter the map |
| Ctrl+A | Select every file matching the filter |
| Ctrl+click | Add to selection |
| Shift+click | Select a range |
| L | Side panel |
| S | Next map style |
| Ctrl+C | Copy the selected paths (now several) |
| Del | Move the selected items to the Recycle Bin (now several) |

### Icon
- New app icon: a square drive unit with the map as its label and an activity light. A simplified variant is used at 16 to 24 px. The installer images use it too.

### Fixes
- Neon is gone. Its dark frames never worked with the single style and its files were either glare or mud; Aurora takes its slot, a saturated mid-tone palette for the dark theme.
- The drive list turned white while a scan was running. The list is no longer disabled during scans and has its own template, so the system's white disabled look can't appear.
- The filter box placed the caret about 30 px too far right because the padding was applied twice.
- Filter panel chips were pill-shaped; they now use the same 6 px corners as everything else.

## 1.1.1
- Installers: a Windows Installer (`.msi`) with a choice between per-user and per-machine, and a one-click `Setup.exe`, both with automatic updates via Velopack
- Update notice in the app, **Check for updates** in the About window, and a setting for the startup check
- Branded installer pages and images

## 1.1.0
- Size on disk as an alternative measure
- Optional hard-link detection so files with several names are counted once
- Free-space block for whole-drive scans, growing when you delete files
- Notice for protected folders with one-click restart as administrator
- Cushion shading
- Small items grouped into one "N files" box
- Settings window (Ctrl+,) with all options
- Shortcuts: C toggles cushion shading, G toggles grouping

## 1.0.0
- First release
