## What you need

- The [.NET 10 SDK](https://dotnet.microsoft.com/download), on Windows, macOS or Linux.
- An editor: JetBrains Rider or Visual Studio open `SpaceSharp.sln`; anything else works with the command line.
- Git LFS, for the brand source files under `.github/branding` (`git lfs install` once).

## Build, run, test

```powershell
git clone https://github.com/SpaceSharpApp/SpaceSharp.git
cd SpaceSharp
dotnet run --project .\SpaceSharp\SpaceSharp.csproj
dotnet test
```

The tests are what the GitHub workflow runs on every push and pull request; it never builds release assets. They cover the layout, the tree, scanning, the saved-scan format and compare, the filter, Insights, the keyboard layouts, and the strings: a test fails when code or XAML asks for a key `Strings.resx` lacks.

On Windows the file-table scan and the Explorer entry work from a debug build like from a release; the fast scan starts a second copy of the debug exe elevated. On macOS and Linux the app runs with the folder walk and the platform's own trash.

## The three projects

| Project | What it is |
|---|---|
| `SpaceSharp.Core` | Plain `net10.0`, no Avalonia, no Win32: the tree, the layout, scanning, saved scans and compare, the filter, Insights, settings, every string |
| `SpaceSharp` | The app: Avalonia windows and controls, the Windows scanners, the shell, the updater |
| `SpaceSharp.Tests` | xUnit, against Core and the app's platform-neutral parts |

[Architecture](Architecture) explains how they fit and where to add things.

## The pretend PC

`dotnet run --project SpaceSharp -- --mock` starts on a sample drive with generic names, sizes and dates, a comparison from yesterday (grew, shrank, new and gone items), a few empty folders and a few files kept twice (the duplicates scene shows a canned result, since the pretend files have no bytes to hash), five pretend drives in the list, and an in-memory achievement store with four mild ones unlocked, so every screen has something to show without scanning anything or touching your own files. In a running app, **Ctrl+Shift+Alt+M** loads it and **Ctrl+Shift+Alt+U** opens the update prompt with nothing behind it.

## Testing right-to-left and clipped text

`--rtl` starts the app with every window mirrored the way Arabic has it, whatever language is set; **Ctrl+Shift+Alt+R** does the same in a running app and back. Open each screen and dialog in that state: the side panel should sit on the right, text should align right, and nothing should be cut.

**Ctrl+Shift+Alt+L** runs a layout audit: it walks every open window for labels that are wider than the room they were given and have no trimming or wrapping to save them, then says how many it found in the status bar and writes the list, with each label's text and the named controls above it, to `%LocalAppData%\SpaceSharp\logs\layout-audit.txt`. The mirror toggle runs the audit on its own. A clean run says "no clipped text". None of these hotkeys is listed in the app.

**The whole sweep, unattended:**

```powershell
.\tools\layout-audit.ps1                   # every language the app ships
.\tools\layout-audit.ps1 -Languages de,pl  # just those
.\tools\layout-audit.ps1 -Resume            # only the languages without a report yet
```

For each language (English plus one per `Strings.*.resx`, eighteen in all) the app starts with `--audit <report> --lang <code>`, poses every screen and dialog on the pretend PC (the start screen, the map in several states, Insights, Changes, the filter drawer and rail, Inspect, each Settings page, Shortcuts, Achievements, the empty folders and duplicates windows, About, the update prompt, the palette editor, the light theme), left-to-right and mirrored, at 1600×900 and at the minimum 1040×620, audits each state and exits. The report (`audit\layout-audit-<code>.txt`, ignored by git) opens with the distinct findings sorted by how many states they appear in, then every state in full. The script's exit code is the total, so a zero means clean. Run it before a release and after any change to a window's layout or to the strings; a translation that runs long shows up as findings in that language only. A full run takes a couple of minutes per language and needs the desktop to itself only in the sense that the windows must stay on screen; `-Resume` picks up where an interrupted run stopped by skipping every language that already has a report. To redo one language, delete its report or name it with `-Languages`.

## Screenshots

`.\tools\screenshots.ps1` builds Release and poses each scene on the pretend PC (`none`, `drawer`, `insights`, `changes`, `inspect`, `settings`, `menu`, `start`, `about`, `update`, `light`, `duplicates`), capturing the window off the screen into `docs\`. Pass scene names to redo a few. The monitor's scaling sets the pixel size; 125% or 150% looks right on GitHub. `python tools\make-mobile-shots.py` then writes the small WebP copies the website uses.

## A clean build after XAML changes

If the app crashes at start after you renamed or added a control in XAML, delete `SpaceSharp\obj` and build again; the compiled XAML cache can go stale.

## Conventions

File-scoped namespaces, braces on every body, `var` only where the type is apparent on the line, XML doc comments on every public member and every documented method complete (the compiler checks them: `GenerateDocumentationFile` is on, so a missing summary, a wrong parameter name or a broken cref is a build warning), terse conventional comments, the formatting in `.editorconfig`, which builds as warnings, American English in code, comments and UI text. [CODING_STANDARDS.md](https://github.com/SpaceSharpApp/SpaceSharp/blob/main/CODING_STANDARDS.md) has every rule with examples from the code, and [CONTRIBUTING.md](https://github.com/SpaceSharpApp/SpaceSharp/blob/main/CONTRIBUTING.md) says how to send a change.
