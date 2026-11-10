## Is SpaceSharp safe? Does it send anything anywhere?

No telemetry, no accounts, no network use except the update check, which only asks GitHub for the latest release and only when you installed with Setup or the MSI. The source is public, and [Privacy and security](Privacy-and-Security) says exactly what the app reads, writes and deletes.

## Why does the fast scan ask for administrator permission?

Reading the NTFS file table means opening the drive itself rather than its folders, and Windows only allows that for administrators. SpaceSharp asks once through the normal Windows prompt, a small elevated helper reads the table and hands the map back, and the helper exits. The window you work in never runs elevated. Say no and the folder walk runs instead, which takes longer and needs no rights.

## Why did my scan not use the fast scan?

The file table scan needs three things: an NTFS drive, a whole drive (not a folder), and the administrator prompt answered yes. Anything else, including external drives formatted exFAT or FAT32, network shares and folders, uses the folder walk. The status bar and the scanning card say which one ran. A file-table scan that hits a table it cannot follow safely also falls back to the walk rather than guess.

## Why is the total different from Explorer?

The map counts file contents. Explorer's used space also includes the file system's own structures, shadow copies, and space reserved by Windows. Hard-linked data such as WinSxS is counted once here (Count hard links once is on by default, and the file-table scan always does it), which Explorer's folder sizes do not do; the Hard links row in Insights says how much that is. Settings › Map › Size measure switches between file size and size on disk; the second matches Explorer's "Size on disk" column.

## What does Safe to clear actually do?

It looks at names only: Windows.old, the Recycle Bin, Temp folders, package caches, browser and shader caches, Windows Update downloads, and Downloads not touched in a year. Show selects them on the map; Recycle sends them to the Recycle Bin after the usual confirmation; Empty empties the bin through Windows; Review selects old downloads so you decide. [Insights](Insights#the-rows) lists every pattern.

## What happens when I delete from the map?

Files go to the Recycle Bin and the map updates to show the space as free. A file that changed since the scan is left alone and listed, so you can scan again first. Protected system folders are refused by Windows. The one exception is Delete permanently on the right-click menu, for the folders Explorer cannot open; it says plainly that it skips the bin. [Cleaning up](Cleaning-Up) has the details.

## Can it find duplicate files?

Yes. The Duplicates row in Insights opens a finder that groups files by size, compares three small samples of each, and only then reads whole files, stopping at the first difference, so it reads little and is quick on most drives. The sets come back biggest waste first; Keep the first of each or Keep the newest of each ticks the rest, and the delete goes to the Recycle Bin. Hard links are the same file and are not counted. [Duplicates and empty folders](Duplicates-and-Empty-Folders).

## Explorer crashes on a folder and cannot delete it. Can SpaceSharp?

Yes. A folder nested inside itself hundreds of levels deep, the kind a bad extraction or a junction leaves behind, has a path too long for Explorer. Right-click it on the map and choose Delete permanently; it uses long-path names, walks the tree without recursion and removes it bottom up, after a warning that it cannot be undone. The Deep and long row in Insights points such folders out and offers to delete the repeats. [Cleaning up](Cleaning-Up#deleting-what-explorer-cannot).

## Where do the saved scans go, and can I turn that off?

`%LocalAppData%\SpaceSharp\scans`, one file per drive or folder plus the previous one for comparison. Settings › Behavior › Keep saved scans for sets how long automatic saves are kept and has Delete all saved scans. The app starts on the start screen, where the last scan is one click away; it never opens a map by itself.

## How much memory does it use?

A tree node per file, so it scales with the file count: a drive with a million files lands in the hundreds of megabytes, more while a scan is being compared with the previous one. The status bar's right-hand meter shows SpaceSharp's own working set against the PC's memory, live. Closing the scan (Ctrl+W) releases it.

## What does the update check send?

One request to `api.github.com` for the latest release, with no identifier of you or your PC beyond what any HTTP request carries. If there is a newer version it downloads the package from `github.com`. Settings › Behavior › Check for updates on startup turns it off. The portable exe never checks.

## What languages does SpaceSharp speak?

Eighteen, following Windows by default: English and Norwegian, and machine translations of Arabic, Chinese (Simplified), Danish, Dutch, French, German, Italian, Japanese, Korean, Polish, Portuguese, Russian, Spanish, Swedish, Turkish and Ukrainian. Right-to-left languages mirror the whole window. [Languages](Languages).

## What does SpaceSharp need to run?

Windows 10 or 11. The portable exe and the installers carry their own .NET runtime, so nothing else needs installing. 32-bit Windows has its own downloads; Windows on ARM runs the 64-bit build through emulation.

## Is there a Mac, Linux or Android version?

Not yet as a download. Since 2.3 the app is built on a cross-platform framework (Avalonia) and runs on Mac and Linux from source; the packaged versions come in a later release, and Android after that, with no date set. The [release page](https://github.com/SpaceSharpApp/SpaceSharp/releases) and the changelog will say when they land.

## Can I compare two drives, or a scan from last month?

Yes. Right-click › Scan file › Compare with a saved scan… takes any `.sscan` file; names are matched folder by folder, so a scan of another drive or of the same drive from any date works. [Changes](Changes).

## Why did SmartScreen warn me?

Every download is code-signed, and Windows shows the publisher. A new certificate still has to earn SmartScreen's reputation through downloads over time; until it has, Windows may show "Windows protected your PC" once. More info › Run anyway. The signature is what to trust, not the warning's absence.
