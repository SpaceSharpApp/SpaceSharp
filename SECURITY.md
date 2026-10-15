# Security

## Reporting a vulnerability

If you find a security problem in SpaceSharp, please do not open a public issue. Use GitHub's private reporting: open the repository's **Security** tab and choose **Report a vulnerability**. Include the version (About shows it), how it was installed (portable exe, Setup or MSI), the steps to reproduce, and what an attacker could gain. You will get a reply within a week, and a fix ships as a regular release with a note in the changelog once it is out; you are credited there unless you prefer not to be.

Only the latest release receives fixes. Please check that the problem is still present in it before reporting.

## What SpaceSharp does, so you can judge the risk

**Reading.** It reads file and folder metadata (names, sizes, dates, attributes) on the drives and folders you choose to scan. It never opens file contents. The fast scan reads the NTFS master file table directly, which needs administrator rights.

**Administrator rights.** The main window never runs elevated. For the fast scan it starts a second copy of its own exe with the `runas` verb (Windows shows the UAC prompt), that copy reads the file table, streams the result back over a named pipe whose name contains a random GUID, and exits. If you decline the prompt, the scan falls back to a normal folder walk without elevation. The portable exe, the Setup build and the MSI all behave the same way.

**Writing.** It writes only to places under your own profile:

- `%AppData%\SpaceSharp\settings.json`: settings.
- `%LocalAppData%\SpaceSharp\scans`: the last scan of each drive, so the map reopens without rescanning. These `.sscan` files are gzip-compressed and contain the full paths, sizes and dates of what was scanned, nothing else. Settings controls how long they are kept (the default is 90 days); delete the folder to remove the record.
- `%LocalAppData%\SpaceSharp\palettes`: custom palettes.
- `%LocalAppData%\SpaceSharp\logs`: crash logs (the last twenty), written only when the app hits an unexpected error. They contain the exception, stack traces, the version, the Windows version and the install kind. Nothing is sent anywhere by itself: the crash dialog offers to open a prefilled GitHub issue in your browser, and attaching the log is your choice.
- `HKEY_CURRENT_USER\Software\Classes`: the Explorer right-click entry, only if you turn it on in Settings, and removed when you turn it off. No administrator rights, no effect on other users.

**Deleting.** It deletes only what you select and confirm, and only to the Recycle Bin, never permanently. Files in protected locations are refused by Windows, not by SpaceSharp, so expect an error rather than a silent success there.

**Network.** Installed copies (Setup or MSI) ask `api.github.com` for new releases on startup and download updates from `github.com`; the check can be turned off in Settings › Updates. The portable exe never updates itself and makes no requests. Nothing else talks to the network: there is no telemetry, no analytics, no crash upload, and no data leaves your computer unless you click a link that opens your browser (the website, the issue form, the changelog).

**Single instance.** A second launch hands its command line to the running window over a local named pipe and exits, so `SpaceSharp.exe D:\` from Explorer or a script scans in the open window. The pipe accepts only paths and the flags listed in `SpaceSharp.exe --help`; anything else is ignored.

**Dependencies.** The app has one package dependency, Velopack (the installer and updater). Everything else is .NET and Windows. See THIRD-PARTY.md.

## Downloads and signing

Release builds are produced by `installer/release.ps1` on the developer's PC and code-signed with a Certum certificate (Windows shows the publisher on every download); the commit they come from is tagged with the version. GitHub Actions only runs the tests. Each release lists its assets on the Releases page; download from there or through winget (`ClearanceClarence.SpaceSharp`), not from third-party download sites, which repackage at their own pace.

The downloads are not code-signed yet, so SmartScreen may show "Windows protected your PC" the first time; choose More info, then Run anyway. If you want to verify a download before running it, compare its SHA-256 (`Get-FileHash .\SpaceSharp-win-x64.exe` in PowerShell) with the line for it in `SHA256SUMS.txt`, which every release carries as an asset. Once a code-signing certificate is in place the installers and exes will carry a signature you can check in the file's Properties › Digital Signatures, and this file will say so.

## Scope

In scope: anything that lets SpaceSharp read, delete or change something you did not ask it to, run code you did not intend, escalate privileges beyond the one UAC prompt described above, or reach the network in a way not described here. Out of scope: issues that require an already-compromised machine or administrator account, social engineering, and the behavior of Windows itself (the Recycle Bin, UAC, SmartScreen).
