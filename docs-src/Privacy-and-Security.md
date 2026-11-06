The short version: SpaceSharp reads metadata about your files, writes a few files under your own profile, deletes files to the Recycle Bin (empty folders and Delete permanently excepted, both after a dialog that says so), and talks to the network only to check for updates. [SECURITY.md](https://github.com/SpaceSharpApp/SpaceSharp/blob/main/SECURITY.md) in the repository is the authoritative statement and says how to report a vulnerability privately.

## Reading

File and folder metadata (names, sizes, dates, attributes) on the drives and folders you choose to scan. The fast scan reads the NTFS master file table, which is the same metadata in one place. File contents are read in one case only: the duplicate finder, when you run it, reads the files that share a size with another to compare them, and keeps only a hash while it works.

## Administrator rights

The main window never runs elevated. For the fast scan it starts a second copy of its own exe through the normal Windows prompt; that copy reads the file table, hands the tree back over a local pipe and exits. Decline the prompt and the folder walk runs with your normal rights.

## Writing

Only under your own profile:

| Where | What |
|---|---|
| `%AppData%\SpaceSharp\settings.json` | Settings |
| `%LocalAppData%\SpaceSharp\scans` | The last scan of each drive or folder and the one before it, as gzip-compressed `.sscan` files: full paths, sizes, dates and attributes, never contents |
| `%LocalAppData%\SpaceSharp\palettes` | Custom palettes |
| `%LocalAppData%\SpaceSharp\achievements.json` | Which achievements you have and when, and the counters a few of them need |
| `%LocalAppData%\SpaceSharp\logs` | Crash logs (the last twenty), written only on an unexpected error: the exception, stack traces, the version, the Windows version and the install kind |
| `HKEY_CURRENT_USER\Software\Classes` | The Explorer right-click entry, only if you turn it on, removed when you turn it off |

## Deleting

Only what you select and confirm, and files to the Recycle Bin. Two exceptions, each after a dialog that says so: empty folders are deleted directly, with a delete that refuses any folder holding a file, and Delete permanently on the right-click menu, for the folders Explorer cannot open, deletes for good. Files in protected locations are refused by Windows. SpaceSharp never deletes anything on its own; Safe to clear, the duplicate finder and the empty folder list suggest, you decide. The duplicate finder is the one feature that reads file contents, and only of the files you ask it to compare; it keeps nothing of them.

## Network

Installed copies ask `api.github.com` for new releases a few seconds after start and download updates from `github.com`; Settings › Behavior turns the check off. The portable exe makes no network requests at all. There is no telemetry, no analytics, no account.

## Single instance

A second launch hands its command line to the running window over a local named pipe and exits. The pipe is per user session.

## Downloads and signing

Release builds are produced by `installer/release.ps1` on the developer's PC and code-signed with a Certum certificate; the commit they come from is tagged with the version. Check a file in Properties › Digital Signatures, or in PowerShell:

```powershell
Get-AuthenticodeSignature .\SpaceSharp-win-x64.exe
```

`SHA256SUMS.txt` on every release lists each file's hash.

## Dependencies

One package: Velopack, for the installer and updater. Everything else is .NET and Windows. [THIRD-PARTY.md](https://github.com/SpaceSharpApp/SpaceSharp/blob/main/THIRD-PARTY.md) lists licenses.

## Reporting a vulnerability

Do not open a public issue. Use the repository's **Security** tab › **Report a vulnerability**. Only the latest release receives fixes.
