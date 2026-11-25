# Changelog

What changed in each SpaceSharp release from 2.9 on, newest first. Releases 2.0.0 through 2.8.0 are in [CHANGELOG-ARCHIVE.md](CHANGELOG-ARCHIVE.md); the 1.x releases are on the [releases page](https://github.com/SpaceSharpApp/SpaceSharp/releases). A section marked unreleased is what the next version will say; the marker comes off when it ships, since the release script and the in-app What's new read the `## version` heading.

## 2.11.0 (unreleased)

### Supporter edition

SpaceSharp is free and complete, and stays that way. If you want to say thanks, a supporter key is a one-time contribution of your choosing: it puts "Supporter edition" under the version in About, thanks you by name, and, if you ask for it, adds your name to the Credits window. It unlocks no feature, because there is none held back.

About › Supporter edition is where a key is pasted. The key is a short signed note with the name you chose, the order and the date, and it is checked on your PC against a public key built into the app, so entering it sends nothing anywhere and it works offline and forever. Line breaks an email put into the key do not matter. Remove the key from this PC takes it out of the settings again; Reset to defaults leaves it alone.

### Fixes

A condition typed into the filter's name box goes away again when you delete it from the text. Typing `videos over 1GB` and then changing it to `over 500mb` kept Video picked, so only videos over 500 MB showed. Now the text takes back what its earlier wording set (a type, a size, an age, files or folders only), and leaves alone what you set on the tiles and scales yourself.

## 2.10.0

### Scanning

The scanning card can be dragged. Take it by any part that is not the Stop button and a dashed outline on a near-solid plate appears in each corner of the map, so it reads over any map colors; the one the card is over lights up in the accent color. Let go there and the card stays in that corner, for this scan and the next ones, the same as choosing it under Settings › Behavior › While scanning. Let go anywhere else and it returns to where it was. A card still held when the scan finishes goes away with the scan and keeps its old corner.

The "No live drawing for this scan" note moved from a banner across the top of the map into the scanning card, under the figures. It no longer covers the map or sits under the marks that show while the card is dragged.

The "Opening the scan…" card looks the part. The screen behind it dims and cannot be clicked while the scan is read, where the card used to float over the list with rows showing through around it, and the card says which scan it is opening: the drive or folder, its size and when it was scanned. On the start screen, the line under the last scan wraps where it was cut short with dots.

### Saved scans and the start screen

A saved scan can be deleted from the start screen. Each one has a bin button, and a right-click menu with Open the map and Delete this saved scan. The question says which drive or folder, when the scan was made, what it takes on disk and whether the earlier scan kept for comparing goes with it. The files are deleted for good; nothing on the drive itself is touched. Settings › Behavior still has Delete all saved scans.

Settings › Behavior › While scanning is tidier. Keep saved scans for is a dropdown like the row above it instead of a slider with its value floating over it, and Delete all saved scans has its own row, Saved scans, which says how many scans are kept and what they take on disk. The button is off when there are none.

### The side panel

The drive tiles in the side panel are the SpaceSharp cube now, in the color of the drive's kind, and the small cube inside grows with how full the drive is, a hint beside the bar, which stays as the exact reading. From 85% used both turn orange and from 92% red, and the free figure turns with them. Optical drives are teal where they were orange, so orange and red only ever mean a drive is nearly full. The start screen draws the same tile beside the last scan and the earlier ones, where it had rings. The drive letter moved in front of the label.

In the side panel, the scroll bar of the drive list and of the largest items no longer covers the right end of the rows: the rows move in by the bar's width while there is something to scroll. The used and free figures under each drive are a size smaller so both fit, and the used figure wraps in a very narrow panel where it used to be cut short. The panel starts a little wider, 360 where it was 300, and can no longer be dragged narrower than 340, where the drive rows used to be squeezed until words broke in the middle. Before a scan the drive list takes the whole panel, as it was meant to; it was held to the height saved for it and scrolled although the space under it was empty.

### The minimap

The minimap can show more than the top folders. Settings › Behavior › Minimap levels picks 1, 2 or 3 levels: at 2 and 3 the folders inside each top folder are drawn inside it, as on the map. The setting says what it costs: more levels make the blocks smaller and the minimap harder to read. 1 level stays the default. Names in the minimap, under it, turns the folder names on the blocks off for a picture of blocks alone.

### Export, Duplicates and Empty folders

The Export window is laid out again. The format, the columns, the row limit and the row options are one strip across the top, where they used to fill a scrolling column on the left: the format is a three-part control, the columns sit behind a button that says how many are picked, and the switches are check boxes. Under the strip the preview is a real table, with a header that stays in place, numbers lined up on the right and sideways scrolling for wide tables, where it used to be the raw text of the file. The suggested file name no longer contains characters a file name cannot hold, so exporting from the top of a drive suggests "C 2026-11-16" and not "C: 2026-11-16". Picking "Everything under" a whole drive no longer freezes the window: the rows are collected and the file size is counted in the background, and the table shows the first 100 rows however many there are.

The Duplicates window is laid out again, in two sides. The sets are a list on the left, each with its name, its copies and what it would free; the picked set's copies are on the right, each with its folder written out in full where it used to be cut short, its age, an Open folder link and whether it stays or goes. Three figures beside the heading give the sets, the extra copies and what could go. "Filter the list" narrows the list to a file name or a folder, and "Hide program folders" leaves out the sets you can rarely act on. A copy in a program folder is named quietly now; the red is kept for the warning that appears once you tick one. Each set has its own "Keep the newest" and "Keep the oldest", and the two links for every set now act on the listed sets only.

The Empty folders window is laid out again. The folders are grouped under the folder they sit in, so the long path is written once, in full, and the names under it, where every row used to repeat a path cut in the middle. A group's tick ticks or clears the whole group. Figures beside the heading give the folders listed, the ones nested inside them and the ones left out in program folders, where one run-on line said all three. "Filter the list" narrows the list to part of a path, the note that the delete is direct sits above the buttons with a warning mark, and a right-click on a heading or a row opens the folder or shows it in Explorer. "Select all" now ticks only the folders that are listed.

Turning off Hide unsafe empty folders now asks first, twice. The first question says which folders will be listed and that deleting them has broken apps; the second says the delete skips the Recycle Bin and that Select all ticks every one of them. Answer no to either and the switch goes back on.

### Fixes

Colors that the app picks in code now follow the theme. Several places asked for a theme color in a way that never found it and drew a stand-in instead: grey for dim text and lines, a fixed orange or green for the accent, white for the labels on the filter's size and age scales. In the dark theme the stand-ins were close enough to pass. In the light theme the scale labels were white on a light background, and the accent in Insights, Statistics, Changes and the palette editor did not match the rest of the window. All of them now use the theme's own colors.

The filter stays with the map. Its drawer or rail used to remain open over Insights, Statistics and Changes, where it covered part of the page. It now puts itself away when you leave the map and comes back when you return, with the filter still applied. Pressing Filter or Ctrl+F from another view takes you to the map and opens it.

Amber text is readable in the light theme. A picked tile in the filter, the active half of a segmented switch, a hovered text link and the scan percentage were amber on a pale background; they now use the darker amber that links already use there. "No filter: everything is shown" is a plain dim remark instead of bold accent text.

Before the first scan, Insights could be picked on the start screen although it had nothing to show. Until a map is loaded, the map is now the only view that can be picked, it is the one marked, and B and the view shortcuts stay on it.

A text box that has the focus keeps its fill and takes an amber edge. It used to turn black with a blue edge, in every window that has one.

On the start screen, an earlier scan of a drive that is not plugged in says "Not connected" beside its file count. It used to say "Folder", which it is not.

A picked end of a two- or three-part switch (Space and Count in Statistics, the format in Export, the palette editor's switch) no longer pokes its amber fill out past the rounded corner.

In the Achievements window, every name under "Still out there" is on one line. The panel is now as wide as its longest name needs, in any language, where a long name such as "The Long Way Round" used to break in two.

The icon in the title bar is the SpaceSharp mark again. At title bar size it was a simplified drawing that read as an amber square with a dark hexagon in it. The small sizes now show the real mark, the hexagon with the cube in its hollow, drawn to fill the space; the 32 px size gained the cube it was missing.

### Under the hood

A tidy-up of the code with no change in behavior beyond the one noted below.

The rule for which errors count as "the file system said no" was written out at some sixty catch sites in a dozen variations; it now has one name, `IsFileSystemFailure`, with `IsLaunchFailure` beside it for a program or link Windows could not open, and each catch says only what it adds. Theme brushes are read through one helper, `ThemeBrush`, so the lookup cannot be done the wrong way again. The repository address is defined once in `AppInfo` instead of in five files.

Text sizes in the windows built in code are named. The 277 hand-typed sizes are now twelve names in `FontSizes`, with the same values as before, so the scale can be seen and retuned in one place. The shadow under floating cards and the green of the Free tile are defined once in `Chrome`. The five Save dialogs go through one helper.

Settings save themselves. The app writes the settings file every few seconds when something changed and again at exit, so a change no longer depends on its call site remembering to save; 26 scattered save calls are gone, and the two restarts save explicitly because the new instance reads the file as it starts.

Tasks that nobody waits for report their failures at once. They used to keep an exception until the garbage collector found the task, which could be minutes later; they now go through `Detach`, which sends it to the crash report straight away. This is the one change you may notice: a background failure that was silent for a while now shows the crash dialog immediately.

Four of the longest methods are split by step. The keyboard handler is a table of shortcuts, one line each. The right-click menu is built by a method per part. A scan is started, shown and torn down by named steps. The statistics pass keeps its totals in a small class of its own instead of in a dozen local arrays.

The views have left the main window. Insights, Statistics and Changes are each a control of their own, and the filter's rail and drawer are a class of their own. Each knows the window only through a small interface naming what it needs: the scan, the comparison, and the few things only the window can do, such as showing something on the map or deleting. The main window went from about 11,500 lines to about 6,650, and what is left is the window itself: scanning, saved scans, the right-click menu, the drives panel and moving between views.

What the Statistics view decided on its own moved into Core with tests: the size class labels and units, the filter for each age band, signed sizes and counts, and the rows of the extension export. The small parts the views share, such as swatches, bars and clickable rows, are in one place.

The test workflow now builds with warnings as errors, so a style or analyzer warning cannot land on main. The coding standards name each of these rules under "Fix the class, not the case".

## 2.9.0

### Statistics and Insights

SpaceSharp has a new view, Statistics, that shows the scan as a report. It lays out the files by size class, kind and age, lists the largest files and folders, and ranks the biggest extensions, with an export of all of them. Every figure opens the map filtered to what it counts, and when you have compared two scans every figure carries its change. A picker at the top narrows the whole page to one folder.

Insights and Statistics no longer say the same things twice. Statistics is what is on the drive; Insights is what you can do about it. The Insights tiles now add up what could be freed, from what is safe to clear to duplicates and files that can be rebuilt, and its rows come in the order of how much they usually give back. The lists of largest files, types and ages moved to Statistics, where they belong.

### Finding your way

Zoom in and a minimap appears in a corner of the map. It shows the scan's biggest folders as labeled blocks with a box where you are; click to jump there, drag to move the view, right-click to hide it. If you would rather have nothing over the map, Settings › Behavior › Minimap swaps it for two thin markers along the edges, moves it to another corner, or turns it off.

The window title now names the folder on the map and follows you as you move. Saving the map as a picture has moved to the path row beside Filter, as a camera with the word Snapshot, next to the map it captures.

### Shading

Cushion shading is gone, and five looks take its place. Edges puts a lit rim on every box, Pillow makes files bright in the middle and darker toward the edges, Sheen lets light fall from the top of each file, Layers lifts files off their folder with a small shadow, and Side light lays one even slope across each file. You pick one in Settings › Treemap › Shading, and each look has two or three settings of its own that it keeps when you try another.

A fresh install starts with a light Sheen. If you are upgrading, the map keeps what it had: a flat map stays flat, and a map with cushion shading comes up in Side light, the look nearest to it.

### Scanning

The live map now starts out close to the finished one. It goes by the last scan of the same drive, so folders are drawn near their final size from the first moment and fill in as they are read, instead of trading places every time one outgrows another. It is redrawn in one step, with icons and labels as the finished map has them, and nothing shifts when the scan ends. Zooming and panning are lighter as well.

Scan again and compare now repeats the saved scan. On the start page it scans the drive the way that scan was made: by the file table or folder by folder, with hidden files, hard links and exclusions as they were then, whatever Settings say today. Two scans made differently cannot be compared, so this is what makes the comparison dependable. When the scan is done it opens Changes.

A few smaller things make scanning calmer. Opening a saved scan shows an "Opening the scan…" card where the window used to look frozen. The scanning card stays in the corner you choose under Settings › Behavior › While scanning, its clock counts in tenths of a second, and Stop takes you back to the start page. The notice about protected folders floats over the map with a close button, so the map no longer jumps when a scan finishes.

### Duplicates

The duplicate finder is easier to reach and much faster. Ctrl+D opens it from anywhere, a click on the Duplicates tile in Insights does the same, and Find duplicates in this folder on a folder's right-click menu searches that folder alone. The search itself reads far less: files of the same size are told apart by samples from their start, middle and end, and big files are compared side by side and dropped the moment they differ, so only true copies are read to the end.

### Palettes

The palette editor is rebuilt around decisions instead of hex fields. You set a hue, saturation and lightness for the whole palette, pick one of five schemes from sample cards, and a readability check tells you when a label is hard to read and fixes it on request. Random and From a picture give you a starting point, the preview is live and clickable, and single colors are still there under Advanced. The same generator also runs in a browser at [palette.spacesharp.app](https://palette.spacesharp.app/), with nothing to install.

### The map

Labels in the Modern style grow with their box. A file that fills the window is no longer named in small print. Title bars keep the folder name at the left and the size and file count at the right, which stops a long name from pushing the facts off the bar. Uniform is the default layout for a fresh install; if you chose Physical, it stays. Right-clicking free space gives a short menu with only what applies, and says how much is free.

### Windows

Achievements is a journal now. What you unlocked runs down a dated line, newest first, beside a panel of the ones still out there, and a click on a locked one shows its hint. What's new reads as a page instead of one block of text. About is tidier, with rows that light up, an arrow on the ones that open the browser and the number of crash logs at a glance. The Shortcuts list lines up, and F1 is listed as what it does: About SpaceSharp.

There are smaller touches too. The text on the amber buttons is centered, scrollbars are a bare thumb with no track, the theme button shows what a click gives you, and the Explorer entry "Scan with SpaceSharp" shows the app icon. This release is built on .NET 10.0.15; the installers and the portable exe carry the runtime, so there is nothing to install.
