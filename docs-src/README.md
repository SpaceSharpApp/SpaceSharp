# docs-src

The text of [docs.spacesharp.app](https://docs.spacesharp.app/): one Markdown file per page. The site is built from these files and kept outside this repository, like spacesharp.app. This file is not part of the site.

## Editing

Edit the files here. The site itself is built from them and kept outside this repository, like spacesharp.app, and updated with each release. A change to a screen in the app belongs in the same commit as the page that describes it and its picture.

## How the pages fit together

| File | What it is |
|---|---|
| `Home.md` | The front page |
| `_Sidebar.md` | The navigation on every page. **Add a line here when you add a page.** |
| `_Footer.md` | The line at the bottom of every page; says which version the docs describe |
| `Page-Name.md` | One page. The file name is the title with hyphens for spaces; `Settings-Reference.md` shows as "Settings Reference" and is linked as `[Settings reference](Settings-Reference)`. |
| `images/` | Screenshots, referenced as `images/name.png` |

Links between pages use the file name without `.md`: `[The filter](The-Filter)`. Links to a heading add the heading in lowercase with hyphens: `[Changes](Changes#filters)`.

## Conventions

- **One page answers one question.** If a page needs a second "## Overview" halfway down, it is two pages.
- **Describe the app as it is**, in the present tense, in the words the app itself uses (Safe to clear, Changes, Color by). Version history belongs in the changelog; a page may say "since 2.5" when it helps.
- **American English**, plain words, short sentences. Headings in sentence case.
- **Tables for reference, prose for explanation.** The Settings reference and Keyboard shortcuts pages are tables on purpose; the other pages should read as text.
- **No dashes as punctuation**; use a comma, a colon or a new sentence.
- **Screenshots come from the app**, never from a hand-cropped window. `tools/screenshots_docs.ps1` renders every picture these pages use on the pretend PC and writes them straight into `docs-src/images` (`-Out` points elsewhere). Pass scene names to redo a few. The scenes themselves are the `WikiScenes` list in `SpaceSharp/MainWindow.Showcase.cs`; a new picture is a new case there, a line in the script's table, and the `<img>` on the page.

## When a release ships

1. Go through the pages that the release touched; the changelog section lists them by name (Palettes, Scanning, Settings, and so on).
2. Update the version in `_Footer.md`.
3. Replace the "New in" list on `Home.md` with the new release's three or four headline items.
4. Regenerate the pictures whose screens changed with `.\tools\screenshots_docs.ps1` and commit them with the pages. The site updates itself when the commit lands on main.

## The pictures

| File | Scene | Shows |
|---|---|---|
| `screenshot.png`, `shot-start.png`, `shot-filter.png`, `shot-insights.png`, `shot-changes.png`, `shot-inspect.png`, `shot-menu.png`, `shot-settings.png`, `shot-about.png`, `shot-update.png`, `shot-light.png`, `shot-duplicates.png` | the README's scenes | the same pictures the README shows |
| `colorby-top.png`, `colorby-type.png` | `wiki-colorby-top`, `wiki-colorby-type` | the Color by modes |
| `shading.png` | `wiki-shading` | cushion shading at the Deep preset |
| `zoomed.png` | `wiki-zoomed` | the map zoomed three folders down |
| `free-space.png`, `physical.png`, `classic.png` | `wiki-free-space`, `wiki-physical`, `wiki-classic` | the free-space panel, the Physical map, the Classic style |
| `filter-rail.png`, `filtered.png` | `wiki-rail`, `wiki-filtered` | the rail layout; a filtered map without the drawer |
| `palette-picker.png`, `palettes.png`, `palette-editor.png` | `wiki-palette-picker`, `wiki-palettes`, `wiki-palette-editor` | the picker open; the sheet of all 24 palettes (drawn by the app, not a window capture); the palette editor |
| `shortcuts.png` | `wiki-shortcuts` | the Shortcuts window |
| `shot-statistics.png` | `statistics` | the Statistics view of the pretend drive |
| `changes-everything.png` | `wiki-changes-everything` | Changes with Everything shown, sorted by size now, two folders open |
| `inspect-selection.png` | `wiki-inspect-selection` | Inspect for several selected files |
| `achievements.png`, `achievement-toast.png` | `wiki-achievements`, `wiki-achievement-toast` | the Achievements window; a toast over the map |
| `empty-folders.png`, `duplicates.png` | `wiki-empty-folders`, `wiki-duplicates` | the two windows behind the Small and empty and Duplicates rows |
| `export.png` | `wiki-export` | the Export window on Program Files |
| `settings-*.png` | `wiki-settings-*` | one per Settings page |
| `rtl.png` | `wiki-rtl` | the window in Arabic, mirrored (the script passes `--lang ar`) |

The [Settings reference](Settings-Reference), [Keyboard shortcuts](Keyboard-Shortcuts), [Command line](Command-Line), [Palettes and looks](Palettes-and-Looks) and [Custom palettes](Custom-Palettes) pages mirror code (`SettingsWindow.axaml.cs`, `ShortcutsWindow.cs`, `CommandLine.cs`, `Palette.cs`). A pull request that changes one of those should change the page too; the PR template asks.
