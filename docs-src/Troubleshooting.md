Start with the [FAQ](FAQ) for the common "why" questions. This page is for things that are broken.

## The scan ran as a folder walk when I expected the fast scan

The file-table scan needs an NTFS drive, a whole-drive scan and a yes to the administrator prompt. If all three were true and it still fell back, the status bar says why: a table the scanner could not follow safely, or the helper could not start. The folder walk gives the same result, slower. Report it with the status bar's message if it keeps happening on one drive.

## A folder shows as "access denied" or is missing

The folder walk cannot read folders Windows protects (`System Volume Information`, other users' profiles, some `ProgramData` folders). A bar above the map lists them after the scan, with a button to restart as administrator, after which the walk can read them. The file-table scan sees everything regardless. Since 2.6 such a folder is marked unreadable rather than shown as empty, so the empty folder list never offers it.

## Explorer crashes on a folder, and nothing can delete it

A folder nested inside itself a few hundred levels deep, usually left by a bad extraction or a junction. Scan the drive, find it on the map or in the Deep and long row of Insights, and use right-click › **Delete permanently** or the row's **Delete the repeats**. Both reach it with long-path names and remove it bottom up. The confirmation says it cannot be undone; that is the point of it. [Cleaning up](Cleaning-Up#deleting-what-explorer-cannot).

## An app stopped working after deleting empty folders

SpaceSharp 2.6.0 and 2.6.1 offered every empty folder on the drive, including the ones Windows Store apps keep their state in (the Snipping Tool's, for one), and an app whose folders were deleted may not start. Since 2.6.2 those folders are not even listed by default (Settings › Behavior › Hide unsafe empty folders).

To put things back: open the **Recycle Bin**, sort by **Date deleted**, select the folders from that day and **Restore** them; 2.6.0 and 2.6.1 sent them there. Failing that, **Settings › Apps › Installed apps ›** the app **› Advanced options › Repair** (or Reset) recreates its folders. Folders removed by 2.6.2 or later went through the direct delete and are not in the bin, but 2.6.2 only offers folders that are plainly yours.

## The map is blank, or a folder is empty

- A filter may be on: the path row's Filter button is lit and the box has text. Esc clears it.
- Settings › Scanning › Leave out may be hiding it; check the list.
- Hidden and system files are left out when Settings › Scanning › Include hidden and system files is off.
- F5 rescans; if the folder is really empty, Explorer will agree.

## The app is slow or uses a lot of memory

A million files is a million tree nodes; that is normal and shows in the status bar's memory meter. Things that make it worse: Density set to Everything (no grouping) on a huge folder, shading at Maximum density, and the hover card over a folder with hundreds of thousands of files. Close the scan with Ctrl+W when you are done with it.

## The map looks wrong after a Windows scaling change

Avalonia handles 125% and 150% scaling, but a scaling change while the app runs can leave a stale layout. Restart the app.

## The app does not open

1. If it shows an **Unexpected error** dialog: press **Report on GitHub**; the bug form opens with the version, your Windows and the error filled in, and the crash log can be attached.
2. If it shows nothing: About (F1) shows how many crash logs there are, as a number at the end of the **Crash logs** row, and a click opens the folder. Or look in `%LocalAppData%\SpaceSharp\logs` for the newest `crash-*.log` and attach it to a [bug report](https://github.com/SpaceSharpApp/SpaceSharp/issues/new?template=bug_report.yml). About › Crash logs opens the folder when the app does start.
3. A corrupt `settings.json` (`%AppData%\SpaceSharp`) is skipped and replaced with defaults, so settings are rarely the cause; a corrupt autosave in `%LocalAppData%\SpaceSharp\scans` is reported in the status bar, not fatal. Deleting either folder is safe if you want a clean start.

## The update prompt keeps appearing, or never does

- Only installed copies (Setup or MSI) update. The portable exe never checks; download the new one.
- Settings › Behavior › Check for updates on startup must be on.
- The check talks to `api.github.com`; a firewall or proxy that blocks it means no prompt. Nothing else is affected.
- A prompt that returns after installing usually means the install was blocked by an antivirus mid-swap. Run the installer from the release page once.

## "Windows protected your PC" on first run

SmartScreen on a certificate that is still earning its reputation. **More info › Run anyway.** The publisher name in the dialog comes from the code signature; check it with the file's Properties › Digital Signatures if you want to be sure.

## Something else

Open a [bug report](https://github.com/SpaceSharpApp/SpaceSharp/issues/new?template=bug_report.yml). The form asks for what matters; About › Copy version info fills in the version and system fields in one paste.
