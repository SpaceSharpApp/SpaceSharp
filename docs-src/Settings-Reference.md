<p align="center"><img src="images/shot-settings.png" width="900" alt="Settings, the Treemap page"></p>

**Ctrl+,** or the gear in the toolbar. Settings are saved as they change to `%AppData%\SpaceSharp\settings.json`; **Reset** at the bottom puts everything back to the defaults listed here.

## General

<p align="center"><img src="images/settings-general.png" width="900" alt="Settings, General"></p>

| Setting | What it does | Default |
|---|---|---|
| App language | The interface language; "Same as Windows" follows the system. Takes effect after a restart. Machine translations are marked as such. | Same as Windows |
| "Scan with SpaceSharp" in Explorer | Adds an entry to the right-click menu of folders, drives and the folder background. For this Windows user only; no administrator rights. | Off |
| Command line | Not a setting: the ways to start SpaceSharp from a shortcut or script ([Command line](Command-Line)). | |

## Behavior

<p align="center"><img src="images/settings-behavior.png" width="900" alt="Settings, Behavior"></p>

| Setting | What it does | Default |
|---|---|---|
| Check for updates on startup | Looks for a new release on GitHub a few seconds after start and offers to install it. Installed copies only; the portable exe does not update itself. | On |
| Draw the map as the scan goes | During a folder walk the map fills in as folders are placed, and what is drawn becomes the finished map. Off keeps the last map until the new one is ready. File-table scans arrive whole either way. | On |
| Keep saved scans for | Automatic saves older than this are deleted at startup: 7 days, 30 days, 3 months, 6 months, 1 year or forever. **Delete all saved scans** sits under it, asks with a table of what goes, and closes a comparison whose scan went with them. | 3 months |
| Animate zoom | Fly into folders instead of jumping. | On |
| Confirm before moving to the Recycle Bin | Ask before deleting. Items inside Windows, program, app and game folders always ask, setting or not. | On |
| Hide unsafe empty folders | The empty folder list shows only folders that are plainly yours; the ones inside Windows, Program Files, ProgramData, AppData and game or build folders, which programs often expect to exist, are left out. Off lists them unticked, with a warning when you tick one ([Duplicates and empty folders](Duplicates-and-Empty-Folders#empty-folders)). | On |

## Appearance

<p align="center"><img src="images/settings-appearance.png" width="900" alt="Settings, Appearance"></p>

| Setting | What it does | Default |
|---|---|---|
| Theme | Match Windows, Light or Dark. The theme button in the toolbar toggles it too. A fourth choice, Gold, appears once every achievement is unlocked: a dark, gilt look with its own map palette. | Match Windows |
| Dark look | The window, panel and menu colors in the dark theme: Graphite, Slate, Mocha, Midnight, GitHub Dark or Carbon. | Graphite |
| Light look | The same for the light theme: Paper, Linen, Mist, Sand, GitHub Light or Snow. | Paper |
| Dim behind windows | How much the map darkens behind Settings, Inspect, About and the update prompt; 0 turns it off. | 45% |

## Treemap

A sample map at the top of the page follows every change.

**Colors**

| Setting | What it does | Default |
|---|---|---|
| Palette | The palette for the current theme; each theme remembers its own. The toolbar's picker is the same choice. | Graphite (dark), Pastel (light) |
| Color by | Top folder, Folder depth or File type ([Reading the map](Reading-the-Map#colors)). | Folder depth |
| Use any palette on either theme | Off: picking a palette made for the other theme switches to that theme. On: the palette is used as it is. | Off |
| Custom palettes | New palette…, Reload palettes, and the palettes folder ([Palettes and looks](Palettes-and-Looks)). | |

**Shading**

| Setting | What it does | Default |
|---|---|---|
| Cushion shading | Light-to-dark shading on every file, the way SpaceMonger drew them. Off gives flat colors. | Off |
| Presets | Soft, Classic, Deep: a starting point for the sliders. | Soft |
| Depth | How pronounced the shading is. | |
| Spread | How far the light reaches across a file before the shadow takes over. | |
| Tone | Lighter or darker files overall. | |
| Fade inside folders | How much shading nested files keep; low calms deep levels. | |
| Light from | Which side the light comes from; the middle is straight ahead. | Straight ahead |
| Reset shading | Shading, grid and highlight back to how they ship. | |

**Text and title bars**

<p align="center"><img src="images/settings-text.png" width="900" alt="Settings, Treemap, Text and title bars"></p>

| Setting | What it does | Default |
|---|---|---|
| Label size | Smallest to Larger; title bars shrink and grow to match. | Normal |
| Font | The typeface for every label on the map. | Segoe UI Variable Text |
| Folder names, File names and sizes | Text colors; Auto picks dark or light per box. | Auto |
| Outlined labels | A thin contrasting outline around every label, for readability on any color. | Off |
| Title bar padding | Space above and below a folder name. | |
| Title bar tint | How far the bar is shaded from the folder's own color; 0 is the same color. | 0 |
| Files, Folders | What a file box and a folder title show: center names, sizes, file counts. | Sizes on; counts in folder titles on |

**Grid and highlight**

| Setting | What it does | Default |
|---|---|---|
| Grid lines | A line between every box. | Off |
| Grid line width | How thick, when on: 1 to 3. | 1 |
| Grid color | Dark suits most palettes; light works on the darkest. | |
| Highlight frame | The frame around selected boxes. | |

**Layout**

| Setting | What it does | Default |
|---|---|---|
| Density | How many small items are drawn before they are grouped into one "312 files" box: Sparse, Normal, Dense, Maximum, Everything (no grouping). | Dense |
| Bias | Whether the layout favors wide boxes or tall ones; Equal is the plain squarified layout. | Equal |
| Padding | Extra space around every box. | 0 |

## Map

<p align="center"><img src="images/settings-map.png" width="900" alt="Settings, Map"></p>

| Setting | What it does | Default |
|---|---|---|
| Size measure | File size, or Size on disk (whole clusters, compressed size for compressed, sparse and cloud files, like Explorer's "Size on disk"). | File size |
| Show free space | A gray block for the unused space in a whole-drive scan. | Off |
| Merge single-folder chains | Draw folders that only contain one folder as one box with a combined title. | On |
| Filter layout | Drawer over the map, or Rail under the path. | Drawer |
| Show side panel | The drives and largest items panel. Shortcut: L. | On |

## Scanning

<p align="center"><img src="images/settings-scanning.png" width="900" alt="Settings, Scanning"></p>

Changes here take effect on the next scan (F5).

| Setting | What it does | Default |
|---|---|---|
| Leave out | Names to skip, with everything inside them. Matched against file and folder names, not paths; `*` and `?` are wildcards. Suggestions are offered. | Empty |
| Include hidden and system files | Off leaves out files and folders with the Hidden or System attribute, such as pagefile.sys. | On |
| Count hard links once | Reads every file's link count so data with several names (WinSxS) is counted once, the way the file-table scan always does, so two scans of the same drive agree. Slower on large drives. | On |

A scan's method and these three settings together decide whether two scans can be compared ([Changes](Changes#how-comparisons-happen)).
