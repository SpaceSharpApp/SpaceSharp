# Security

## Reporting a vulnerability

If you find a security problem in SpaceSharp, please do not open a public issue. Use GitHub's private reporting: open the repository's **Security** tab and choose **Report a vulnerability**. Include the version (About shows it), how it was installed (portable exe, Setup or MSI), the steps to reproduce, and what an attacker could gain. You will get a reply within a week, and a fix ships as a regular release with a note in the changelog once it is out; you are credited there unless you prefer not to be.

Only the latest release receives fixes. Please check that the problem is still present in it before reporting. Releases are for Windows 10 and 11; the code also builds on macOS and Linux, but no packages ship for them yet, so reports there are welcome as ordinary issues.

## What SpaceSharp does, so you can judge the risk

**Reading.** A scan reads file and folder metadata (names, sizes, dates, attributes) on the drives and folders you choose. It does not open file contents. The fast scan reads the NTFS master file table directly, which needs administrator rights.

Three things read more than metadata, and each only when you start it. **Find duplicates** (Ctrl+D) opens files of equal size and hashes their contents with SHA-256 to tell copies apart; the hashes stay in memory and are gone when the window closes. **Inspect** on a single file asks the Windows shell for that file's icon. The palette creator's **From a picture** reads the one image you pick, to take its colors.

**Administrator rights.** The main window never runs elevated. For the fast scan it starts a second copy of its own exe with the `runas` verb (Windows shows the UAC prompt), that copy reads the file table, streams its progress back over a named pipe whose name contains a random GUID, writes the finished tree to a temporary file in the scans folder for the main window to pick up, and exits. The helper takes its instructions from its command line only and does nothing but that one scan. If you decline the prompt, the scan falls back to a normal folder walk without elevation. The portable exe, the Setup build and the MSI all behave the same way.

**Writing.** It writes only to places under your own profile:

- `%AppData%\SpaceSharp\settings.json`: settings, including the choices on Settings › Accessibility and, if you entered one, your supporter key.
- `%LocalAppData%\SpaceSharp\scans`: the last scan of each drive, so the map reopens without rescanning. These `.sscan` files are gzip-compressed and contain the full paths, sizes and dates of what was scanned, nothing else. Settings › Behavior › Keep saved scans for controls how long they are kept (the default is 3 months); the start screen can delete one, and deleting the folder removes the whole record. A scan you save or export yourself (Ctrl+S, Export) goes where you choose and nowhere else.
- `%LocalAppData%\SpaceSharp\palettes`: custom palettes, with an example file and a short README the first time.
- `%LocalAppData%\SpaceSharp\achievements.json`: which achievements are unlocked and when.
- `%LocalAppData%\SpaceSharp\instance.lock`: an empty file held open while the app runs, so a second launch can find the first.
- `%LocalAppData%\SpaceSharp\logs`: crash logs (the last twenty), written only when the app hits an unexpected error. They contain the exception, stack traces, the version, the Windows version and the install kind. Nothing is sent anywhere by itself: the crash dialog offers to open a prefilled GitHub issue in your browser, and attaching the log is your choice.
- `HKEY_CURRENT_USER\Software\Classes`: the Explorer right-click entry, only if you turn it on in Settings, and removed when you turn it off. No administrator rights, no effect on other users.

**Deleting.** It deletes only what you select and confirm. The default is the Recycle Bin, and before anything is sent each file is checked against the disk: one whose size or date no longer matches the scan is left alone and named in the result. Four things do not go through the bin, and each says so before it runs: **Delete permanently** on the right-click menu, **Delete the repeats** in Insights, **Empty the Recycle Bin**, and removing **empty folders**, which uses the delete that refuses a folder with anything in it. Anything in Windows, program or app folders is counted in the confirmation, and that confirmation cannot be turned off. A junction or symbolic link is removed as itself, never what it points at. Files in protected locations are refused by Windows, not by SpaceSharp, so expect an error rather than a silent success there. DEVELOPING.md has the rules every deleting feature is held to.

**Network.** Installed copies (Setup or MSI) ask GitHub for new releases a few seconds after startup and download updates from `github.com`; the check can be turned off in Settings › Behavior › Check for updates on startup. When an update is offered, its What's new text is read from `raw.githubusercontent.com` (the changelog at the release's tag) and the release's details from `api.github.com`. The portable exe never updates itself and makes no requests. Nothing else talks to the network: there is no telemetry, no analytics, no crash upload, and no data leaves your computer unless you click a link that opens your browser (the website, the docs, the issue form, the supporter page).

**Supporter key.** A key is checked on your own PC against a public key inside the app. Nothing is sent anywhere to validate it, and the key unlocks no feature, so there is nothing to gain by forging one.

**Fonts.** The reading font on Settings › Accessibility is inside the app; nothing is downloaded when you choose it.

**Single instance.** A second launch hands its command line to the running window over a local named pipe and exits, so `SpaceSharp.exe D:\` from Explorer or a script scans in the open window. The pipe accepts only paths and the flags listed in `SpaceSharp.exe --help`; anything else is ignored.

**Dependencies.** The app has five package dependencies: Avalonia (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`), which draws the interface through SkiaSharp, and Velopack (the installer and updater). Everything else is .NET and Windows. Dependabot watches them. See THIRD-PARTY.md.

## Downloads and signing

Release builds are produced by `installer/release.ps1` on the developer's PC and code-signed with a Certum certificate (Windows shows the publisher on every download); the commit they come from is tagged with the version. GitHub Actions only runs the tests. Each release lists its assets on the Releases page; download from there, through the Scoop bucket (`SpaceSharpApp/scoop-bucket`) or the Chocolatey package (`spacesharp`), both of which fetch the same signed files from the release, and not from third-party download sites, which repackage at their own pace.

Every exe, installer and MSI carries that signature; check it in the file's Properties › Digital Signatures, or with `Get-AuthenticodeSignature .\SpaceSharp-win-x64.exe` in PowerShell, which should say Valid. SmartScreen may still show "Windows protected your PC" while a new certificate earns its reputation; choose More info, then Run anyway. To verify a download independently of the signature, compare its SHA-256 (`Get-FileHash .\SpaceSharp-win-x64.exe`) with the line for it in `SHA256SUMS.txt`, which every release carries as an asset.

## Scope

In scope: anything that lets SpaceSharp read, delete or change something you did not ask it to, run code you did not intend, escalate privileges beyond the one UAC prompt described above, or reach the network in a way not described here. Out of scope: issues that require an already-compromised machine or administrator account, social engineering, and the behavior of Windows itself (the Recycle Bin, UAC, SmartScreen).
