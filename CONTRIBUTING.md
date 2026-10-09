# Contributing to SpaceSharp

Everyone taking part is expected to follow the [code of conduct](CODE_OF_CONDUCT.md). UI changes should keep to the notes at the end of [ACCESSIBILITY.md](ACCESSIBILITY.md).

Thanks for your interest. SpaceSharp is a small project, so the process is light.

## Ways to help

- **Translate.** Text lives in `SpaceSharp/Resources/Strings.resx` (English); `Strings.nb.resx` is the Norwegian bokmål translation and a good model. To add a language, copy it to `Strings.<culture>.resx` next to it (`Strings.nb.resx` for Norwegian bokmål, `Strings.de.resx` for German), translate the `<value>` elements, keep the `{0}` placeholders, and build. The language appears in Settings › Language by itself; nothing else has to be registered. `dotnet test` checks that every translation has the same keys and placeholders as the English file. Anything a translation lacks falls back to English. Keep file names, keyboard keys and the filter grammar (`over`, `older than`) in English; only the words around them are translated.
- **Screenshots** for the README, the website and store listings come from `tools/screenshot-mockup.html` (the engine is `docs/spacesharp-mock.js`, shared with the website), which draws the app with generic sample data so no real file names end up online. Open it in a browser, pick theme, palette and a scene (hover card, menu, filter drawer, Inspect, selection, zoomed folder, the panel under the map), and capture the window.
- **Report bugs** and **suggest features** through the [issue forms](https://github.com/ClearanceClarence/SpaceSharp/issues/new/choose). Clear steps and a screenshot go a long way.
- **Add a palette.** Palettes are a name, a theme and twelve colors in `SpaceSharp/Util/Palette.cs`; see the Retro entry for the simplest form. Check it in every color mode, and in the theme it is for.
- **Add file types.** Extensions and their categories live in `BuildExtensionMap()` in the same file.
- **Improve the docs.** The README and CHANGELOG are plain Markdown; fixes to wording or missing steps are welcome.
- **Code.** Anything from an open issue. [DEVELOPING.md](DEVELOPING.md) says how the pieces fit and where to change things. For larger changes, open an issue first so we can agree on the approach before you spend time on it.

## Setting up

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows 10 or 11.
2. Clone the repository and open `SpaceSharp.sln` in JetBrains Rider or Visual Studio, or run `dotnet run --project .\SpaceSharp\SpaceSharp.csproj`.
3. Velopack is the only NuGet dependency and restores automatically.
4. `dotnet test` runs the unit tests in `SpaceSharp.Tests`. They cover the layout, the filter parser, size formatting and the tree operations, and take a few seconds.

## Pull requests

- Branch from `main`, one change per pull request.
- Keep the existing style: file-scoped namespaces, `var` where the type is obvious, XML doc comments on public members, and the formatting in `.editorconfig`.
- Use American English in code, comments and UI text.
- Every UI string a user can see should follow the tone of the rest of the app: short, plain, no jargon.
- New settings go in `AppSettings`, `SettingsWindow` and `MainWindow.ApplySettings()`, and a mention on the README's Settings list if they are user-facing.
- New shortcuts go in `MainWindow.Window_PreviewKeyDown`, `ShortcutsWindow.cs` and the README's shortcut table.
- Add a line to `CHANGELOG.md` under an "Unreleased" heading describing the change from the user's point of view.
- Test on a real drive, not only a small folder. Performance problems show up at a few hundred thousand files.
- If you change `Squarify`, `Grouping`, `DriveInsights`, `FsNode`, `FilterSpec`, `FileFilter`, `SizeFormatter`, `ScanFile`, `ScanCompare` or `NamePatterns`, add or adjust a test in `SpaceSharp.Tests`. Text changes are checked too: a test fails if code or XAML asks for a resource key that `Strings.resx` lacks, or if a translation is missing keys. The test workflow runs them on every pull request; it only tests and never builds release assets.

## Releasing (maintainer)

1. Bump `<Version>` in `SpaceSharp/SpaceSharp.csproj` and move the "Unreleased" changelog entries under a `## <version>` heading.
2. Run `.\installer\release.ps1`. It runs the tests and builds the portable exe, the installers and the update packages into `Releases\`.
3. Commit, tag the commit with the bare version number (`git tag 1.2.1 && git push origin 1.2.1`), and create the GitHub release with the changelog section as its notes. Attach everything in `Releases\` (both channels, portable exes included), or run the script with `-Upload`.
4. Update the winget manifest: `wingetcreate update ClearanceClarence.SpaceSharp --version 1.2.1 --urls <MSI download URL> --submit`.
