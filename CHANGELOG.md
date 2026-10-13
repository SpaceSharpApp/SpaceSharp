# Changelog

What changed in each SpaceSharp release, newest first. The 1.x releases are in [CHANGELOG-1.x.md](CHANGELOG-1.x.md). A section marked unreleased is what the next version will say; the marker comes off when it ships, since the release script and the in-app What's new read the `## version` heading.

## 2.2.0

### Sixteen more languages
- SpaceSharp speaks Arabic, Chinese (Simplified), Danish, Dutch, French, German, Italian, Japanese, Korean, Polish, Portuguese, Russian, Spanish, Swedish, Turkish and Ukrainian. All sixteen are machine-made and not yet checked by a native speaker, and the app says so: Settings › Language lists them under a "Machine translations" heading, apart from English and Norwegian, and shows a note under the dropdown while one is in use. Each file carries a `Meta_MachineTranslated` entry that a reviewer flips to `false`; corrections are welcome through CONTRIBUTING.md.
- The windows mirror for a right-to-left language (Arabic, Hebrew, Persian, Urdu): panels swap sides, text and numbers align right, menus and tooltips follow. The map, the donuts and the icons keep their drawn shape.

### Crash reports
- An unexpected error is written to a log under `%LocalAppData%\SpaceSharp\logs` (the whole exception chain with stack traces, the version, Windows and how the app was installed; the last twenty are kept), and the error dialog gains Report on GitHub, which opens the bug form with the version, the system, the install kind and the error already filled in and asks for the log to be attached. Errors on background threads are logged too. About has a Crash logs row that opens the folder.

### Installer
- The MSI dialogs have new art: a low-poly landscape of amber and graphite facets behind the mark in the left column, and a cut of the same in the banner. Drawn by `tools/make-assets.py` like every other brand image.

### Start screen
- The saved scans are tiles instead of rows: a ring shows how full each drive is right now (warm when it is nearly full), with the volume label, the scanned size and when it was scanned; a folder scan shows a folder icon in the ring's place. A line in the corner sums used and total space across the drives. Scan a folder and Open a saved scan are two wide buttons under the tiles.

### Palettes
- GitHub replaces Vivid in the dark theme and GitHub (light) replaces Retro in the light theme: the twelve accent colors of GitHub's Primer dark and light themes, the light set lifted a fifth toward white. A saved choice of Vivid or Retro falls back to the theme default.

### Accessibility
- Icon-only toolbar buttons announce their tooltip as their name to screen readers, and show an accent outline when they have keyboard focus, so Tab is visible.

### Website
- The site is at https://spacesharp.app now, hosted separately; GitHub Pages is off. The README, the issue chooser and the social preview point at the new address.

### Fixes
- The update prompt no longer repeats the two version numbers as pills under "You have 2.0.0"; the line says it once.
- The license page of the MSI installer wrapped the MIT text twice, once at the 80 columns of the file and once at the width of the dialog, so lines broke in odd places. The file is one line per paragraph now and the dialog wraps it once.

## 2.1.0

The panel under the map became a view of its own. Insights takes the map's whole area when you want the numbers, the icons are a consistent set, drives show what kind of disk they are, and there are fourteen file types instead of eight.

### The Insights view
- The panel under the map is gone; its three views moved into a view of their own and grew.
- Two buttons in the toolbar, Map and Insights, and one keeps the other's place: Insights takes the whole area the map had, so the scan can be read in full instead of in a strip. B switches between them, and the choice is remembered. It fills in when a scan finishes or a saved scan opens, and updates after a delete or a cleanup.
- A row of tiles across the top: what was scanned (files, folders, how it was read), free space on the drive with its kind (HDD, SSD, NVMe, USB), how much is safe to clear, how much has been untouched for a year, the biggest folder, and what changed since the last scan.
- **By type** is a donut with the whole scan in the middle and all fourteen types as rows: swatch, name, the three biggest extensions in grey, a share bar, size, files and share, with a sentence above saying where the biggest type mostly lives. Click a row and the map comes back filtered to that type.
- **Largest folders**, **Largest files**, **Since last scan** (what grew, shrank and appeared, with the biggest movers) and **Extensions** (the biggest extensions and the folder that holds most of each) fill the second row. Every row is a click back to the map, zoomed or filtered to it.
- **Safe to clear** lists places that are usually safe to empty, decided from names alone: Windows.old, the Recycle Bin, Temp folders, package caches (npm, pnpm, NuGet, pip, Cargo, Gradle, Composer, Go, Maven), browser caches, shader caches, Windows Update downloads, and Downloads not touched in a year. Each row has Show, which selects the items on the map and zooms there, and an action: **Recycle** sends them to the Recycle Bin after the usual confirmation, **Empty** empties the Recycle Bin through Windows (which asks first) and takes the freed space off the map, **Review** selects old downloads and sets the filter so you decide, and **How to** explains the hibernation and paging files SpaceSharp will not touch. Rows that can be recycled have a checkbox; the footer sums what is checked, says how much would be free afterwards, and one button recycles them all.
- **When changed** is a histogram of bytes by the month they were last written, 36 months, colored in the four age bands Inspect uses (this month, this year, 1 to 3 years, older), with the band totals and file counts as filter buttons under it, and a list of the oldest large files, each a click away on the map.

### File types
- Fourteen file types instead of eight. Game data (.pak, .ba2, .uasset and the like), Installers (.msi, .msix, .appx), Disk images (.iso, .vhdx, .wim), Databases (.db, .sqlite, .mdf), Fonts and System (.sys, .mui, .etl, .dmp) come out of Other, which on a gaming PC used to be half the drive. Color by type, the filter drawer and the filter's type words ("game data", "installers", "iso", "fonts", "system") know the new ones; custom palettes can name a color for each, and a custom palette that names only the old eight keeps working, with the new types taking the file tints.

### Palette creator
- Settings › Treemap › Custom palettes has New palette, and Edit or Start from the current one. The editor shows the twelve folder colors, the file tint (or twelve file colors of your own) and the fourteen file type colors as swatches; click one and set it by hex (any of #RRGGBB, RRGGBB, #RGB) or hue, saturation and lightness sliders. Generate fills the twelve from a base color in four spreads (Rainbow, Neighbors, Two-tone, One hue), and Random rolls a base and a spread for you. The sample drive is drawn beside it in Depth, Top folder or File type mode as you work, and the footer warns about folder colors whose labels would be hard to read and pairs too alike to tell apart. Save writes the JSON file to the palettes folder and switches the map to it; Copy JSON puts the file on the clipboard to share.

### The scan card
- The card shown while a drive is read now shows the scan as three stages, each with its own bar: find the files (or read the file table), build the tree, draw the map. The first fills against the drive's used space, the second sweeps while a file table scan links its records, the third lights while the map is laid out. Under them the three numbers are big and in a row: found so far (of how much), files per second, and elapsed time with an estimate of what is left. The title line says what is being read: used space, the kind of drive, file table or folder walk.

### Icons
- The interface icons are Lucide (https://lucide.dev, ISC license) instead of Windows' Segoe glyph font: one consistent set of 2-pixel round strokes, drawn as path geometry through a small `Icon` control, so they look the same on Windows 10 and 11, scale cleanly at any DPI, and take the color of whatever they sit in. `tools/make-icons.py` writes `Themes/Icons.xaml` from the Lucide release it pins; the landing-page mockup uses the same set.
- Drives show what they are. A fixed disk is asked, without elevation, whether it spins and which bus it is on, so the side panel and the start screen show a hard disk, a solid-state drive, a USB drive, a network share or an optical disc, each with its own picture and the kind named before the file system ("NVMe SSD · NTFS"). The first breadcrumb and the saved-scan list use the same picture.

### Side panel
- The cards reflow with the window: four across on a wide window, three, two or one as it narrows, with the By type card keeping its width where there is room, and the tiles going from six to three to two across. The view scrolls when the cards no longer fit the height.
- The side panel is narrower (300 px instead of 360) now that Insights carries the long lists, and while Insights shows, its Largest items section is hidden and the drives get the whole height.

### Releasing
- Releases are built by GitHub Actions when a version tag is pushed and created as drafts. `installer\release.ps1` runs in stages, and refuses to upload a build from a PC unless told to.

### Website
- The project moved to github.com/SpaceSharpApp/SpaceSharp and the site to spacesharpapp.github.io/SpaceSharp; every link in the app, the README and the page follows, and the update check and What's new read from the new address.
- The landing page works on a phone: the live preview gives way to a gallery of screenshots, the download links step aside (the installers are for a PC), and the comparison table reads as stacked cards.

## 2.0.0

SpaceSharp 2.0 is a new map, a new filter and a new panel under the map. It is a major version because it takes things away: the six map styles are gone in favor of one look you shade yourself, the Neon palette is gone, and every palette was redrawn, so a saved style or palette choice from 1.x does not carry over (a saved Neon falls back to Graphite; the old `MapStyle` setting is ignored). Everything else, your saved scans, custom palettes and settings, is read as before.

### One look, shaded your way
- The map draws one way, the way SpaceMonger drew it: folders are frames with a title bar, files are boxes inside them, and a one-pixel line or a small gap separates every box. The Style box has left the toolbar, and S no longer cycles anything. What used to be a choice between Classic, Flat, Tiles, Cards, Bands and Soft is now a set of sliders, so the look is yours rather than one of six.
- Settings › Treemap has a **Shading** card in its place. **Cushion shading** switches the light-to-dark shading on files on or off; off is flat color and grays out the rest of the card. **Presets** Soft, Classic and Deep are starting points for four sliders: **Depth** (how pronounced the shading is), **Spread** (how far the light reaches before the shadow), **Tone** (lighter or darker files overall; folders keep their color) and **Fade inside folders** (how much shading nested files keep). **Light from** is a square of nine buttons: pick a side or corner, or the middle for light from straight ahead. Reset to defaults puts all of it back.
- A preview map of sample data is pinned above the Treemap page and follows every change, so you see the shading, the title bars, the grid and the text colors as you drag.
- Folder title bars are a shade of the folder's own color rather than a black overlay. On a pastel palette the overlay turned every nested bar into the same muddy strip and Users › AppData › Local stacked into one dark block; now each bar keeps its palette and its level. **Title bar tint** (0 to 40 %) sets how far the bar is shaded from the folder's color, **Title bar padding** (0 to 8 px) the space above and below the name; the bar grows with it.
- **Text and title bars**: the color of folder names and of file names and sizes is Auto (dark or light per box, as before) or a fixed color from swatches or any #RRGGBB. Label size, font, outlined labels, and what file and folder labels show (names, sizes, counts, centered or not) moved here from Appearance and Layout.
- **Grid and highlight**: grid lines on or off, their width (1 to 3 px, the old Border setting) and color from swatches or any #RRGGBB, and the color of the frame around selected boxes, amber by default.
- The grid is one line wide everywhere. Each box used to stroke its own outline on fractional coordinates, so two neighbors drew two lines on different pixels and a child flush against its folder's edge added a third; seams came out two or three pixels dark. Boxes now snap to whole pixels and each leaves exactly one grid strip on its right and bottom, none where the folder's own line already is.
- **Padding** still puts a gap around every box; the gap shows the map background at the top level and the folder's color inside it, scaled down on small boxes so a field of tiny files does not turn into dots.
- Small folders no longer dissolve into slivers. Grouping used to be judged against the whole scan only, so inside any folder smaller than about 70 × 70 px every child counted as "too small" and the map drew all of them a few pixels wide. A folder is now also cut by the pixels it actually has on screen: children big enough to see (about 10 × 10 px) get their own box, the rest merge into one "312 files" block, and when none is big enough the whole folder body is one block. The cut only moves when the folder's area doubles, so a smooth zoom does not shuffle boxes. A merged block that fills its folder has no title bar of its own; the folder's bar names the place.
- Title bars stay down to 30 px wide. The bar is what makes a folder read as a frame; the name is cut short, and left out below about 18 px of room, so a narrow frame shows a plain bar rather than a lone "…". Bars also appear a little earlier in height (24 px of content under the bar, down from a fixed 44 px box).
- Density, bias and padding sit under **Layout** on the same page; Palette and Color by moved from Appearance to a **Colors** card there, next to the custom palettes folder. Appearance keeps the theme.

### The filter, redone
- The filter box and its popup are gone. In their place, by default, a **filter drawer**: a sheet over the right of the map with the file types as rows (checkbox, color, name, a share-of-scan bar and the size), a size range, **Not modified for** as a row of buttons, Show (files and folders, files only, folders only), a name box, the presets (Large files, Big videos, Installers & archives, Untouched for 2 years, Big and old, Recently changed), and a footer with the live match count and size and Select matches. The Filter button in the path row and Ctrl+F open and close it; Esc closes it; Clear all empties it. The button reads "Filter · 3" while three conditions are set.
- The name box still takes the full syntax ("type:video >500MB older than 1 year"), and anything typed there turns into checked rows and set controls, so the drawer teaches the syntax as well.
- Settings › Map › **Filter layout** switches to a **filter rail** under the path instead: one pill per condition (Type, Size, Age, Show), each a small dropdown that shows its value when set and takes an amber edge, a name box, a Try pill with the presets, and the live count with Select matches and Clear on the right. The same Filter button shows and hides it.
- Underneath, nothing changed: both faces read and write the same filter text, so anything that sets a filter (the panel under the map, the right-click menu's "Show only *.mp4 files", the presets, a typed filter) works with either, and Ctrl+A still selects every match.

### A panel under the map
- A new strip under the map with three views of the whole scan side by side. The toolbar button next to the side panel's, or B, shows and hides it; the chevron on its right folds it down to its titles and back; Settings › Map › Show bottom panel sets whether it is there at all. It fills in when a scan finishes or a saved scan opens, and updates after a delete.
- **By type** sums the scan by file type, eight of them, each with its biggest extensions. Hover a type to see those files on the map; click and it becomes a filter, click again to clear it.
- **Safe to clear** lists places that are usually safe to empty, decided from names alone: Windows.old, the Recycle Bin, Temp folders, package caches (npm, pnpm, NuGet, pip, Cargo, Gradle, Composer, Go, Maven), browser caches, shader caches, Windows Update downloads, and Downloads not touched in a year. Each row has Show, which selects the items on the map and zooms there, and an action: **Recycle** sends them to the Recycle Bin after the usual confirmation, **Empty** empties the Recycle Bin through Windows (which asks first) and takes the freed space off the map, **Review** selects old downloads and sets the filter so you decide, and **How to** explains the hibernation and paging files SpaceSharp will not touch. Anything that does not fit in five rows is summed up on an "Also:" line.
- **When changed** is a histogram of bytes by the month they were last written, 36 months, colored in the four age bands Inspect uses (this month, this year, 1 to 3 years, older), with the band totals as filter buttons under it; click one to filter the map to that band, click again to clear. Hover a month to see it on the map, click to filter to it.

### Palettes
- Every palette now has twelve hues, one per top-level folder before any repeats (they had seven, so the eighth folder on a drive wore the first one's color). The toolbar swatches show eight colors instead of six, and the palette list is wide enough for the longest name.
- Palettes are split by theme. The dark theme has Graphite (new, the default), Ocean, Sunset, Forest, Vivid (new), Aurora (new, in place of Neon), Monochrome and Color-blind safe; the light theme has Pastel (the default), Paper (new), Candy (new), Nordic (new), Earth (new), Retro and Color-blind safe (light). The pickers list the current theme's palettes first and the others under their own heading; each theme remembers its own choice, so switching the theme switches the palette too.
- A custom palette's JSON can say `"theme": "dark"` or `"light"`; without it the palette is listed under both. Example.json and the README in the palettes folder show the field.
- Neon is gone. Its dark frames never worked with the single look and its files were either glare or mud; Aurora, a saturated mid-tone palette for the dark theme, takes its slot.

### The right-click menu
- The map's menu is the same list, drawn better. Inspect is a taller first row on the control tone with the clicked item's name, size and kind as its sub-line (or the count and total size of a selection), so the menu says what it is about. Every row has an icon in a fixed column, the shortcuts are smaller and quieter, the rows are 32 px with a rounded hover, the menu has a 10 px radius and a softer shadow, and Move to Recycle Bin sits apart in a footer on the panel tone with a red-tinted hover.

### The app's own colors
- Settings › Appearance has a **Dark look** and a **Light look**: the colors of the window, panels, controls, menus and the map background, five for each theme. Dark: Graphite (the one the app has had), Slate (cooler), Mocha (warmer), Midnight (deeper blue), Carbon (neutral black). Light: Paper (as before), Linen (warm), Mist (cool), Sand (beige), Snow (white). The amber accent stays the same in every look, so the app stays recognizable; each theme remembers its own look and the map's palette is separate from it.

### The update prompt
- The update notice is one narrow column: the mark on its tile, "SpaceSharp 2.0.0 is ready" with your version under it, the jump as two pills (1.6.1 → 2.0.0), a short paragraph led by the release's section headings, then Install and restart, Remind me later, and a "What's new in 2.0" link that opens the full notes. The download progress shows under the pills. Someone two releases behind still gets every release in between in the notes.

### New defaults
- A fresh install now opens with Color by Depth, Dense grouping, a 2 px gap between boxes with the grid off, no cushion shading (the Soft preset is set, ready to switch on), flat title bars (tint 0), black labels in Segoe UI Variable Text, and file counts in folder titles. Settings you have already saved are kept; Reset to defaults lands on this look.

### App icon
- The Windows icon (taskbar, Explorer, title bars) sits on the rounded Graphite tile the 1.x icon had, so it reads as an app icon next to other apps' tiles. The mark itself is unchanged and stands on its own in the README, on the landing page and inside the app. `tools/make-assets.py` draws both.

### 32-bit Windows
- Every release now also ships for 32-bit Windows: SpaceSharp-win-x86.exe, SpaceSharp-win-x86-Setup.exe and SpaceSharp-win-x86.msi, built from the Portable-x86 and Velopack-x86 publish profiles. The x64 files follow the same scheme (SpaceSharp-win-x64.exe, SpaceSharp-win-x64-Setup.exe, SpaceSharp-win-x64.msi); SpaceSharp.exe and SpaceSharp-win-Setup.exe are the old names. Each build is its own update channel, `win-x64` and `win-x86`, so an x86 install never pulls an x64 package, and every release file carries the channel name. 1.x installs were on a channel called `win`; the release also carries a `releases.win.json` that points them at the 2.0 x64 package, after which they are on `win-x64` like everyone else. `installer\release.ps1` builds both channels; `-NoX86` skips the second.
- The tree takes about a third of the memory it did. A node no longer stores its full path (it is rebuilt from the names up the tree when asked for), files share one empty child list instead of each carrying their own, and the comparison baseline is stored without boxing. The 32-bit build needs this most: a 32-bit process can address a few gigabytes at most, and a system drive with a couple of million files used to run it out of memory. When that still happens, the error says so and points to the 64-bit build.

### Keyboard
- B shows and hides the panel under the map. S is gone with the styles. Ctrl+F opens the filter drawer or rail; Esc closes the drawer. The About window's shortcut list follows.

### Fixes
- Inspect's By type and By age donuts were blank when one slice was the whole ring (a folder with one kind of file, or everything changed this year): a full-circle arc starts and ends on the same point and draws nothing.
- The palette list no longer jumps when the mouse crosses the group headers.
- The panel under the map used to keep the previous scan's numbers until you switched tabs; it now refreshes when a scan ends, a saved scan opens, or a delete finishes.
- Brightness used to darken every folder as well as the files; it now only tilts the cushion shading, and the shading can be switched off altogether.

### Under the hood
- Grouping moved into `Layout/Grouping.cs` with tests of its own (the data cutoff at a reference window, the on-screen cutoff quantized to powers of two). The panel's numbers come from `Services/DriveInsights.cs`, pure and tested: bytes by type, bytes by month, the cleanup suggestions and their filters. Color parsing for the new settings is in `Util/ColorText.cs`, tested. The update prompt's version shortening and highlight headings are tested too.
- The changelog is split: this file holds 2.0 onward, `CHANGELOG-1.x.md` every 1.x release. The release script and the in-app What's new read the `## version` sections as before.
- The README was rewritten to be short; the long material (how the scanner, layout, renderer, filter and updater work, the project structure, where to add a palette, file type, setting, shortcut or translation) is in `DEVELOPING.md`.
- The landing page was rebuilt around a live drawing of the 2.0 window. On a phone, where there is nothing to hover and no room for the window, it shows a swipeable gallery of the screenshots instead, the comparison table stacks into one card per feature, and a card above the downloads sends the link to the PC. `docs/spacesharp-mock.js` is one engine shared by the page and `tools/screenshot-mockup.html`; it is a mockup of the app with sample data, not a build of it, and says so in the image. `node tools/screenshots.js` renders every README screenshot from it with headless Chromium; `tools/make-social.py` draws the social preview.
