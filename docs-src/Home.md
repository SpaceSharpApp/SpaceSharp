<p align="center"><img src="images/screenshot.png" width="820" alt="SpaceSharp showing a drive as a treemap"></p>

**SpaceSharp** is a free, open source disk space analyzer. It scans a drive or a folder and draws it as a zoomable treemap: folders are frames, files are boxes sized by the space they take. These pages are the long version of the [README](https://github.com/SpaceSharpApp/SpaceSharp#readme): every screen, every setting, every shortcut, and the answers to the questions that come up.

**[Download the latest release](https://github.com/SpaceSharpApp/SpaceSharp/releases/latest)** · [Website](https://spacesharp.app/) · [Changelog](https://github.com/SpaceSharpApp/SpaceSharp/blob/main/CHANGELOG.md) · [Report a bug](https://github.com/SpaceSharpApp/SpaceSharp/issues/new/choose)

## Using SpaceSharp

- [Getting started](Getting-Started): install, the first scan, the administrator prompt
- [Reading the map](Reading-the-Map): what the frames, boxes, colors and shading mean
- [Navigating](Navigating): zoom, pan, the path row, the side panel
- [The filter](The-Filter): plain words, the drawer, quick filters, Select matches
- [Insights](Insights): the numbers behind the map, row by row, and Safe to clear
- [Duplicates and empty folders](Duplicates-and-Empty-Folders): files kept twice, folders with nothing in them
- [Changes](Changes): what grew, shrank, appeared and disappeared since the last scan
- [Inspect](Inspect): one item in depth
- [Cleaning up](Cleaning-Up): the Recycle Bin rule, and deleting what Explorer cannot
- [Saved scans and the start screen](Saved-Scans-and-the-Start-Screen)
- [Settings reference](Settings-Reference): every setting, one line each
- [Palettes and looks](Palettes-and-Looks): the 24 palettes, the picker, themes and looks
- [Custom palettes](Custom-Palettes): the palette editor and the JSON format
- [Keyboard shortcuts](Keyboard-Shortcuts)
- [Command line](Command-Line)
- [Languages](Languages)
- [Achievements](Achievements)

## Questions and problems

- [FAQ](FAQ)
- [Troubleshooting](Troubleshooting)
- [Privacy and security](Privacy-and-Security)
- [Suggest a correction](Suggest-a-Correction): found a mistake on these pages

## For contributors

- [Building from source](Building-from-Source)
- [Architecture](Architecture)
- [Translating](Translating)

## New in 2.7

- **Every delete follows the same rules now.** The map's Move to Recycle Bin and Delete permanently run with a progress panel and a Stop button; a selection that touches Windows, Program Files, ProgramData, AppData or a game folder gets a red note that says so and always asks; the duplicates window never ticks a program-folder copy by itself; emptying the Recycle Bin asks with the size ([Cleaning up](Cleaning-Up)).
- **Delete questions you can read**: a row per item with its name, folder and size, largest first, the rest behind "Show the other n".
- **The live map stops flickering**: boxes that were already there no longer fade in again as the walk finds more.
- **Five new achievements**, twenty-six in all, and a **Gold theme** for whoever collects every one ([Achievements](Achievements), [Palettes and looks](Palettes-and-Looks#looks)).
- **Inspect rewritten** as a wider window: a card of sentences about the folder over tables for where the space is, by type, by age and the largest files ([Inspect](Inspect)); the start screen's last-scan card has a strip of the drive's largest folders along its foot ([Saved scans](Saved-Scans-and-the-Start-Screen)).
- Show on a Safe to clear place flashes the selection on the map; rounded corners fixed on the Files / Folders / Types switch and the achievement cards.

## New in 2.6

- **Insights rebuilt** as one column of rows that open one at a time, with eight new rows: games, media, developer leftovers, Downloads, hard links, small and empty, deep and long, duplicates ([Insights](Insights)).
- A **duplicate finder** that compares file contents, and an **empty folder** list that by default only offers folders that are plainly yours ([Duplicates and empty folders](Duplicates-and-Empty-Folders)).
- **Delete permanently** for the folders Explorer crashes on ([Cleaning up](Cleaning-Up#deleting-what-explorer-cannot)).
- **Achievements**, twenty-one of them then, twenty-six since 2.7 ([Achievements](Achievements)).
- A compare that no longer invents change between two scans a minute apart, a start screen that says what to do first, drive tiles tinted by kind, hard links counted once by default, 987 file types.

2.6.1 and 2.6.2 made the empty folder list safe and fast: Settings › Behavior › **Hide unsafe empty folders** (on by default), a direct delete instead of the Recycle Bin, progress with a Stop button, and Select safe. The scanning card's clock now shows hundredths. The [changelog](https://github.com/SpaceSharpApp/SpaceSharp/blob/main/CHANGELOG.md) has all of it.
