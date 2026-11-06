The interface is in eighteen languages. English (`Strings.resx`) and Norwegian Bokmål (`Strings.nb.resx`) are written by hand; the other sixteen started as machine translations and are marked so in the app's language list. A native speaker fixing even a few of them is the most useful small contribution there is.

## Where the strings are

`SpaceSharp.Core/Resources/Strings*.resx`, one file per language, one `<data name="Key">` entry per string. The key is the same in every file; the English file is the reference.

```xml
<data name="Changes_Gone" xml:space="preserve"><value>Gone</value></data>
```

Placeholders like `{0}` and `{1:N0}` are filled in by the app and must stay; move them around freely to suit the language. `{1:N0}` is a number with thousands separators.

## Fixing a translation

1. Find the key in `Strings.resx` to see the English and its context (the key names say where it is used: `Settings_`, `Insights_`, `Changes_`, `Key_`).
2. Change the `<value>` in `Strings.<culture>.resx`. Keep the tone of the rest of the app: short, plain, no jargon, and the app's own names for things (Insights, Safe to clear, Changes).
3. Keep every `{0}`-style placeholder the English has; the app fills them in, and a missing one shows as a gap. A key missing from a translation falls back to English rather than breaking anything.
4. Run `dotnet test`, then send a pull request. Say which language you speak natively; that is the review.

Each translation file has a `Meta_MachineTranslated` entry; `true` is what makes Settings › General mark the language as machine-made. When a language has been checked through by a native speaker, set it to `false` in the same pull request and the note comes off.

## Adding a language

1. Copy `Strings.resx` to `Strings.<culture>.resx` with the culture code Windows uses (`pt-BR`, `cs`, `fi`, …).
2. Translate the values. There are about a thousand; the machine-translated files are an acceptable starting point if you would rather correct than write.
3. Set `Meta_MachineTranslated` in the new file to `true` or `false` as the case may be.
4. `dotnet test`, then a pull request. The app discovers translation files by their culture code, so the language appears in Settings › General on its own, sorted among the reviewed or the machine-made ones.

Right-to-left languages work: the window mirrors itself when the culture is right-to-left. To check your translation in that state, start the app with `--rtl`, or press Ctrl+Shift+Alt+R in a running one, and Ctrl+Shift+Alt+L to list any text that no longer fits ([Building from source](Building-from-Source#testing-right-to-left-and-clipped-text)). A translation that is much longer than the English will show up there.

## What not to translate

- The filter's grammar (`over`, `older than`, `videos`): it is parsed, not shown, and English in every language. The drawer writes it for people.
- Keys, placeholders and the `SpaceSharp` name.
- File and folder names the app found on the disk, obviously, and Windows' own names such as Recycle Bin where the language has an established term; use that term.
