# Contributing to SpaceSharp

Everyone taking part is expected to follow the [code of conduct](CODE_OF_CONDUCT.md). UI changes should keep to the notes at the end of [ACCESSIBILITY.md](ACCESSIBILITY.md).

Thanks for your interest. SpaceSharp is a small project, so the process is light.

## Ways to help

- **Translate.** Text lives in `SpaceSharp.Core/Resources/Strings.resx` (English); `Strings.nb.resx` is the Norwegian bokmål translation and a good model. To add a language, copy it to `Strings.<culture>.resx` next to it (`Strings.nb.resx` for Norwegian bokmål, `Strings.de.resx` for German), translate the `<value>` elements, keep the `{0}` placeholders, and build. The language appears in Settings › Language by itself; nothing else has to be registered. `dotnet test` checks that every translation has the same keys and placeholders as the English file, so a new language starts as a full copy. At run time anything a translation lacks falls back to English. Keep file names, keyboard keys and the filter grammar (`over`, `older than`) in English; only the words around them are translated. Every file has a `Meta_MachineTranslated` entry: `true` means the file was machine-made and nobody who speaks the language has read it through, and Settings lists it under that label. The most useful translation contribution is not a new language but reading one of those through, fixing what reads wrong, and setting the entry to `false` in the same pull request.
- **Screenshots** for the README, the website and store listings come from the app itself on a pretend PC with generic sample data, so no real file names end up online: `.\tools\screenshots.ps1` poses and captures every README scene and `.\tools\screenshots_docs.ps1` the docs site's (see DEVELOPING.md), or start the app with `--scene <name>` to look at one. Every scene is posed with the Accessibility settings at their defaults and without a supporter key, so the pictures do not depend on whose PC made them.
- **Report bugs** and **suggest features** through the [issue forms](https://github.com/SpaceSharpApp/SpaceSharp/issues/new/choose). Clear steps and a screenshot go a long way.
- **Add a palette.** Palettes are a name, a theme and twelve colors in `SpaceSharp/Util/Palette.cs`; see the Retro entry for the simplest form. Each theme has twelve built-in palettes and a test holds it there, so a new one replaces one or the test changes with it. It needs a line about it in the strings (`PaletteAbout_<Name>`, in every language file). Check it in every color mode, in the theme it is for, and with each choice of Settings › Accessibility › Color vision. Do not add a palette for color blindness: Color vision adjusts every palette instead.
- **Make the app easier to use.** [ACCESSIBILITY.md](ACCESSIBILITY.md) lists what does not work yet for screen readers, keyboards and low vision, in the order it is planned; any item there is welcome.
- **Add file types.** Extensions and their categories live in `SpaceSharp.Core/Resources/FileTypes.txt`, a text table with one `[Category]` section each; no C# needed.
- **Improve the docs.** The README, the CHANGELOG and the pages of docs.spacesharp.app (`docs-src/`, one Markdown file per page) are plain Markdown; fixes to wording or missing steps are welcome.
- **Code.** Anything from an open issue. [DEVELOPING.md](DEVELOPING.md) says how the pieces fit and where to change things. For larger changes, open an issue first so we can agree on the approach before you spend time on it.

## Setting up

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download). Windows 10 or 11 is what the app is released for and where everything works; it also builds and runs on macOS and Linux, without the fast NTFS scan.
2. Clone the repository and open `SpaceSharp.sln` in JetBrains Rider or Visual Studio, or run `dotnet run --project .\SpaceSharp\SpaceSharp.csproj`.
3. The NuGet packages (Avalonia 12 and Velopack) restore automatically; nothing else needs installing.
4. `dotnet test` runs the unit tests in `SpaceSharp.Tests`, a few hundred of them in a few seconds. They cover everything in `SpaceSharp.Core` (the layout, scanning, scan files, the filter, insights, statistics, the tree table, duplicates, the color vision adjustment, the strings) and the window-free parts of the app (palettes, the NTFS reader against a made-up file table).
5. `dotnet run --project .\SpaceSharp\SpaceSharp.csproj -- --mock` starts on a pretend PC, so you can try a change without scanning anything.

## Pull requests

- Branch from `main`, one change per pull request.
- Keep the existing style: file-scoped namespaces, `var` where the type is obvious, XML doc comments on public members, and the formatting in `.editorconfig`. [CODING_STANDARDS.md](CODING_STANDARDS.md) has the rest, with examples of what to do and what not to do. The test workflow builds with warnings as errors, so a style warning the analyzer raises locally fails the pull request.
- Use American English in code, comments and UI text.
- Every UI string a user can see should follow the tone of the rest of the app: short, plain, no jargon.
- New settings go in `AppSettings` (with their default in `ResetToDefaults`), the page's builder in `SettingsWindow` and `MainWindow.ApplySettings()`, with a row in `docs-src/Settings-Reference.md` if they are user-facing.
- New shortcuts go in the table in `MainWindow.ShortcutFor`, `ShortcutsWindow.cs` and `docs-src/Keyboard-Shortcuts.md`.
- Describe the change in `CHANGELOG.md`, in the section whose heading ends in "(unreleased)", from the user's point of view. The changelog is written as prose under a heading per area, not as a bullet list; add to the paragraph that covers your area or start one.
- A change to a screen is three things together: the code, the page in `docs-src/` that describes it, and its picture, or a note in the pull request that the picture needs redoing.
- New windows call `TitleBarTheme.Attach`, colors come from the theme's brushes and the palette, and anything that moves stops for Reduce motion; the notes at the end of ACCESSIBILITY.md have the rest.
- Test on a real drive, not only a small folder. Performance problems show up at a few hundred thousand files.
- Logic that needs no window goes in `SpaceSharp.Core` with a test in `SpaceSharp.Tests`; if you change anything in Core that has a test file (`Squarify`, `Grouping`, `DriveInsights`, `FsNode`, `FileFilter`, `ScanFile`, `ScanCompare`, `Statistics`, `FolderTree`, `InspectSummary`, `ColorVision` and the rest), add or adjust a test. Text changes are checked too: a test fails if code or XAML asks for a resource key that `Strings.resx` lacks, or if a translation is missing a key or a placeholder. Write a key in full where it is used (`Strings.Get("Inspect_Tile_Rank")`), since the test finds keys by reading the code. The test workflow runs them on every pull request; it only tests and never builds release assets.

## Releasing (maintainer)

Versions are `major.minor.patch`. Fixes and small improvements ship as often as they are ready and bump the patch number (2.4.2, 2.4.3, 2.4.17); there is no upper limit and no reason to hold a fix back for a bigger release. The minor bumps for a release with new features or changed behavior; the major only when the app changes shape (new file format, dropped platform, a different scanner).


1. Finish the pictures: run `tools/screenshots.ps1` and `tools/screenshots_docs.ps1` for the screens that changed. Then bump `<Version>` in `SpaceSharp/SpaceSharp.csproj`, set `<PreviousVersion>` to the release before it, and take "(unreleased)" off the changelog section's heading. Leave the README's version line alone: it names the released version, and `release.ps1` moves it when it uploads the release.
2. Run `.\installer\release.ps1 -Sign <thumbprint> -Upload` on the PC with the code-signing certificate. It runs the tests, builds the portable exes, the installers and the update packages into `Releases\`, signs every file, checks the signatures and creates the GitHub release as a draft with the changelog section as its notes.
3. Read the draft on GitHub and publish it, then tag the released commit with the bare version number (`git tag <version> && git push origin <version>`).
4. The Scoop bucket ([SpaceSharpApp/scoop-bucket](https://github.com/SpaceSharpApp/scoop-bucket)) follows on its own: its Excavator workflow reads the new release and updates the version and hashes in `bucket/spacesharp.json`. If it has not caught up after a day, run the workflow by hand from the bucket's Actions tab.
5. Push the Chocolatey package once the release is published, since it downloads the MSIs from the release: `choco push .\Releases\chocolatey\spacesharp.<version>.nupkg --source https://push.chocolatey.org/`. `release.ps1` writes and packs it with the version and checksums filled in; the source is `installer/chocolatey`. For a release that is already out, `.\installer\pack-chocolatey.ps1 -FromGitHub` makes the package from the published MSIs without building anything. A new version waits for the Chocolatey moderators before `choco install spacesharp` gives it.
