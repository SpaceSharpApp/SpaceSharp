SpaceSharp deletes files to the **Windows Recycle Bin**, after you confirm, so a mistake is one Restore away. Two things skip the bin, and both say so before they run: **empty folders**, which have nothing in them to restore, and **Delete permanently** on the right-click menu, which exists for the folders Explorer cannot open.

## Ways to delete

- Select boxes on the map and press **Del**, or right-click › **Move to Recycle Bin**.
- Filter, **Ctrl+A** to select every match, **Del**.
- Insights › Safe to clear: tick rows, **Recycle checked**.
- Insights › Safe to clear › Recycle Bin › **Empty** empties the bin through Windows. It asks first and says how much is in it, because that one is final: the bin is what it empties.
- Insights › Duplicates › **Find duplicates…**: tick the copies you do not want, **Move checked to Recycle Bin** ([Duplicates and empty folders](Duplicates-and-Empty-Folders)).
- Insights › Small and empty › **Review empty folders…**: the folders with nothing in them, deleted directly since there is nothing to restore; only the folders that are plainly yours are listed or ticked by default ([Duplicates and empty folders](Duplicates-and-Empty-Folders#empty-folders)).
- Right-click › **Delete permanently**, and Insights › Deep and long › **Delete the repeats**: see below.

Settings › Behavior › Confirm before moving to the Recycle Bin turns the confirmation off if you prefer; the bin is still where things go. One case always asks, setting or not: when something you selected sits inside Windows, Program Files, ProgramData, AppData or a game or build folder. The question then says how many of the items do, and that a program which needs them stops working until they are put back. The Safe to clear row is the exception the other way: its places are caches and temp folders picked by name, so it does not warn about the AppData they live in.

The question lists what goes: a row per item with its name, folder and size, largest first, the three largest shown and the rest behind a "Show the other n" link. Hover a row for the full path.

## What happens

1. Before a file is sent, its size and last-write time are checked against what the scan recorded. A file that changed since the scan, or a path that now names a different file, is left alone and listed in the result so you can scan again and decide. Folders are sent as they are.
2. Windows moves the items to the Recycle Bin, two hundred at a time. A panel in the map's corner counts them off, with a **Stop** button that ends it between batches and keeps what was already moved. Files in protected locations are refused by Windows, not by SpaceSharp; expect an error, not a deletion.
3. The map updates without a rescan: the boxes disappear, their parents shrink, and the free-space block grows. The side panel and Insights follow.

## Deleting what Explorer cannot

A bad extraction or a junction can leave a folder nested inside itself a few hundred levels deep, with a path thousands of characters long. Explorer crashes when you open it, and neither Explorer nor the Recycle Bin can delete it. Since 2.6, SpaceSharp can:

- **Right-click › Delete permanently** deletes the selection for good. It uses long-path names, walks the tree without recursion and removes it bottom up, read-only files included, with junctions removed as links rather than followed. It asks first, the dialog says that it cannot be undone, and if anything selected sits in a program folder it says so and that there is no bin to put it back from. The same corner panel shows progress, and **Stop** ends it between items.
- The **Deep and long** row in Insights recognizes a folder that repeats inside itself, says what repeats and how many times, and offers **Delete the repeats**, which keeps the first copy and removes the nested ones.

Use it for that. For anything a Recycle Bin can hold, use the Recycle Bin.

## What SpaceSharp will not do

- Touch `hiberfil.sys`, `pagefile.sys` or `swapfile.sys`. Safe to clear explains how to free them with `powercfg` and the paging settings instead.
- Delete anything it found on its own. Safe to clear, the duplicate finder and the empty folder list only *suggest*; nothing moves until you tick and press.
- Tick a program's folder for you. The empty folder list hides the folders inside Windows, Program Files, ProgramData, AppData and game folders by default, and warns when you tick one.
- Run as administrator for deleting. The elevated helper exists only to read the file table and exits as soon as it has.

## Good habits

- Look before you empty: **Show** on a Safe to clear row selects its places on the map.
- Duplicates: keep the copy in the place you would look for it, which is not always the first or the newest. The sets show each copy's folder for that reason.
- Browser caches: close the browser first, or pages reload slower for a while.
- Package caches (npm, NuGet, pip and the rest) come back the next time a build needs them; that is their job.
- Windows.old removes itself ten days after a feature update. If it is still there, it is because you went back and forth; it is safe to recycle unless you plan to roll back.
