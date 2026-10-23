<!-- Thanks for the pull request. CONTRIBUTING.md has the conventions; this template is the short version. -->

## What this changes

<!-- One or two sentences from the user's point of view. Link the issue if there is one: "Fixes #123". -->

## Why

<!-- The problem it solves or the behavior it corrects. Skip this if the issue says it already. -->

## How it was tested

<!-- Which drives or folders (a real drive with a few hundred thousand files, not only a small folder), both scan methods if scanning is touched, dark and light theme, which settings. -->

## Screenshots

<!-- Before and after, for anything visible. Delete this section otherwise. -->

## Checklist

- [ ] One change per pull request, branched from `main`
- [ ] Builds without warnings and `dotnet test` passes
- [ ] Every new user-visible string is in `Strings.resx` and in all seventeen translation files (machine translations are fine); the tests check for missing keys
- [ ] New settings are in `AppSettings`, `SettingsWindow` and `MainWindow.ApplySettings()`; new shortcuts are in `ShortcutsWindow.cs` and the README's shortcut table
- [ ] Tests added or updated in `SpaceSharp.Tests` when logic in `Layout`, `Models`, `Services`, `Scanning` or `Util` changed
- [ ] `CHANGELOG.md` has a line under an "Unreleased" heading, written for the user (add the heading if the last release already shipped)
- [ ] American English in code, comments and UI text; the file keeps the existing style and `.editorconfig` formatting
