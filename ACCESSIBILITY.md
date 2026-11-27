# Accessibility

This page says plainly what SpaceSharp does and does not do for people who use a screen reader, keyboard only, high contrast, large text, a right-to-left language, or who see color differently. It describes version 2.11.0. It is written to be checked against the current release and updated when something changes; if you find it wrong, that is a bug worth reporting (see below).

## The Accessibility page in Settings

Settings (Ctrl+,) has a page of its own for this. Everything on it takes effect at once, in every open window, and is remembered.

| Setting | What it does | Default |
|---|---|---|
| Text size | Draws the whole interface at 100 %, 110 %, 125 % or 150 %, text, icons and controls together, on top of the system's display scaling. | 100 % |
| Reading font | Sets the interface and the map's labels in Atkinson Hyperlegible, a font made for low vision with letters that are hard to mistake for each other. | Default (Inter) |
| Reduce motion | Leaves out the zoom between folders and the flash that marks an item on the map. | Off |
| Stronger contrast | Brightens the dim text and the lines between rows, on every look. | Off |
| Color vision | Adjusts every palette for red-weak (protan), green-weak (deutan) or blue-weak (tritan) vision. | Off |
| Outlined labels | A thin contrasting outline around every label on the map. The same switch as under Treemap › Text and title bars. | Off |

The sections below say what each of these reaches and where it stops.

## What works today

**Text size and scaling.** SpaceSharp follows the system display scaling (125 %, 150 %, 200 % and so on, and Retina on a Mac) in every window, including the map, the hover card and the dialogs. Text size on the Accessibility page multiplies that: at 150 % everything in a window is drawn half again as large, the map and its labels included. Dialogs grow with their content, up to the size of the screen they are on, and scroll past that; the main window keeps the size you gave it, so its content has less room and the toolbar folds its labels away sooner. For the map alone, Settings › Treemap › Text and title bars › Label size has five steps from Smallest to Larger and Font changes the labels' typeface. Labels that would not fit are dropped rather than clipped, and the hover card and Inspect show the full name.

**Reading font.** Atkinson Hyperlegible replaces Inter in every window, in right-click menus, dropdown menus, flyouts and tooltips, and on the map, where it takes the place of the Font setting while it is chosen. The font is bundled with the app, so nothing needs installing.

**Contrast.** The dark and light looks are drawn with strong contrast between text and surface. Stronger contrast goes further for the two things that are deliberately quiet: secondary text (hints, column headings, the lines under a setting's name) moves a good half of the way toward the full text color, and the rules between rows and around cards move toward it too. It works on every look, the custom ones included. On the map, Outlined labels puts a contrasting outline around each label so text stays readable on any box color, and the label color itself is picked per box, dark on light boxes and light on dark ones.

**Color vision.** Color vision adjusts every palette, the built-in ones and your own, for the kind of vision you choose. Each palette is treated as a whole: the colors that kind of vision runs together are moved apart, mostly by making one lighter and another darker, and the closest pair in a palette is never left closer than it was, so a palette keeps its character instead of being swapped for a fixed safe set. The adjustment covers folder colors, file colors and the file-type colors, everywhere they are drawn: the map, the legend, Insights, Statistics, the Tree view's bars and Inspect. It replaces the two Color-blind safe palettes of earlier versions; a settings file that had one of them chosen goes back to the theme's default palette with Color vision set to green-weak, the most common kind.

**Color is never the only carrier of information.** Every box has a label with its name and size, the legend names each category, Color by change has a legend and names the change in words in the hover card and Inspect ("grew 1.2 GB since the last scan"), and the filter dims non-matches rather than only recoloring them. The palette creator checks every color you pick against the label text and warns when the contrast is below 4.5 to 1 or two folder colors are too alike, so a home-made palette cannot silently become unreadable. Light and dark themes follow Windows or can be fixed.

**Keyboard.** Most of the app is reachable without a mouse. F5 rescans, Backspace and Home move around the map, + and − zoom, Ctrl+F opens the filter, Ctrl+A selects the matches, Ctrl+I opens Inspect, Enter opens the selected item, Del moves the selection to the Recycle Bin, Ctrl+C copies its paths, Ctrl+D finds duplicates, Ctrl+S and Ctrl+O save and open scans, Ctrl+W closes the scan, Ctrl+, opens Settings and F1 opens About. Single letters switch things on the map: B goes through the views (Map, Tree, Insights, Statistics, Changes), K the color modes, M the Classic and Modern styles, U the Physical and Uniform maps, G free space, L the side panel and T opens Achievements. The full list is in About › Keyboard shortcuts, drawn on your own keyboard layout. Dialogs answer to Enter and Escape and keep focus inside them. Buttons, rows and segmented choices show an accent outline when they have keyboard focus, so Tab is visible.

**The scan without the map.** Four views beside the map show the same scan as text and tables built from ordinary controls, so the map is never the only way to the numbers:

- **Tree** is the scan as a folder table: name, share of the parent, size, files, folders and modified date. Up and Down move between rows, Right opens a folder in place, Left closes it or goes to its parent, and Enter opens a folder or goes to a file on the map.
- **Insights** reads the drive for you: totals, space by file type, what is safe to clear, when files were last changed, the largest folders and files. Every row is a button that goes to that item.
- **Statistics** lays the figures out as a report for the whole scan or one folder.
- **Changes** compares the scan with the previous one as a folder table, with the same Right, Left and Enter keys as the Tree.

Inspect (Ctrl+I) gives everything known about the selected item or items as text: where it is, its size and size on disk, its share of the folder and the drive, what kinds of files and what ages are inside, and what changed since the last scan.

**Standard controls.** The side panel, toolbar, filter panel, these views, Settings, Inspect, About, the palette creator and all dialogs are built from ordinary Avalonia controls, which expose UI Automation on Windows, VoiceOver on macOS and AT-SPI on Linux, so a screen reader can read their text, state and roles, and Tab moves through them in order.

**Language and direction.** The interface comes in eighteen languages. English and Norwegian Bokmål are written by hand; Arabic, Chinese (Simplified), Danish, Dutch, French, German, Italian, Japanese, Korean, Polish, Portuguese, Russian, Spanish, Swedish, Turkish and Ukrainian are machine translations, and Settings › General › Language says so. The app follows the system language by default. For Arabic every window mirrors: panels swap sides, text and numbers align right, menus and tooltips follow. The map, the minimap, the donut charts and the icons keep their drawn shape, since mirroring a treemap would only move things without making it more readable.

**Motion.** Reduce motion leaves out the two things that move on the map: the zoom between folders, whatever Settings › Behavior › Animate zoom says, and the flash that marks an item sent to the map from another view; the item is still selected and framed. Nothing else in the app moves on its own except the progress sweep while a scan runs.

## What does not work yet

**The map itself is not accessible to screen readers.** The treemap is a custom-drawn surface with no UI Automation tree behind it: a screen reader sees one large control and nothing inside it, and arrow keys do not move between boxes. The information in the map is available elsewhere (the Tree view above all, Insights, Statistics, the side panel, Inspect for any selected item, the filter's match count), but there is no way yet to walk the map's boxes with a keyboard or have them read out. This is the largest gap and the one we most want to close.

**Names for icon-only buttons.** Toolbar and row buttons that show only an icon have a tooltip but, with one exception, no accessible name of their own, so a screen reader may announce some of them as just "button", with the tooltip as help text at best. Buttons with a text label are read normally.

**Menus and tooltips at a larger text size.** Text size scales the windows. Right-click menus, dropdown lists, flyouts and tooltips open as separate popups: they take the reading font but stay at their normal size.

**Charts and the reading font.** The numbers drawn inside the donut charts and the minimap's labels stay in the default font when a reading font is chosen. The legends and tables beside them follow it.

**The reading font and other scripts.** Atkinson Hyperlegible covers Latin letters. In Arabic, Chinese, Japanese, Korean, Russian and Ukrainian the text falls back to the system's font.

**Color vision in the palette creator.** The creator shows and saves your colors as you picked them, not as adjusted, and its "too alike" warning judges them with typical color vision. The adjusted result shows on the map behind it.

**High-contrast themes.** The app uses its own colors rather than the system high-contrast palette, so turning on a Windows high-contrast theme does not restyle SpaceSharp. Stronger contrast strengthens the app's own looks, but they are still not the colors you chose in Windows.

**System preferences are not read.** Reduce motion and Text size are app settings. The app does not yet follow the Windows "Show animations" switch or the Windows text size slider (it does follow display scaling); set them on the Accessibility page.

**Hover-only information.** The hover card shows details for the item under the mouse; the same details are reachable with Ctrl+I on a selection or from a row in the Tree view, but there is no keyboard way to point at an arbitrary box on the map.

**Sliders and color pickers.** The shading sliders in Settings and the hue, saturation and lightness sliders in the palette creator work with the arrow keys but do not announce their values as they change. The hex field beside each color does, so use that with a screen reader.

**Arabic numerals.** With the app in Arabic, sizes and dates use Eastern Arabic digits because the system's Arabic locale does; there is no switch yet to keep Western digits.

## Planned

In rough order:

1. A UI Automation peer for the map so each visible box is an element with its name, size and position in the hierarchy, and arrow keys move between siblings, Enter zooms in, Backspace goes up, with a drawn focus indicator like the selection frame.
2. An accessible name on every icon-only button, taken from its tooltip.
3. Text size for menus and tooltips, and the reading font inside the charts.
4. Honoring the Windows "Show animations" setting, text size and high-contrast mode.
5. Spoken values for sliders, and a Western-digits option for Arabic.

If you depend on one of these, say so in an issue; it helps decide what comes first.

## Reporting a problem

Open a bug report through the issue chooser, or use About › Report a bug, and start the title with "Accessibility:" so it is easy to find. Please include which assistive technology you use (and its version), your display scaling and theme, what is set on Settings › Accessibility, what you expected, and what happened. Keyboard-only, low-vision and right-to-left reports are as welcome as screen-reader ones.

If you would rather not report publicly, use the contact on the maintainer's GitHub profile at [github.com/ClearanceClarence](https://github.com/ClearanceClarence).

## For contributors

- New controls must be reachable with Tab and have a name a screen reader can speak. Give every icon-only button a tooltip and set `AutomationProperties.Name` on it; nothing does that for you yet.
- Do not communicate state by color alone; add text, a glyph or a tooltip.
- Draw with the palette's colors as `ColorScheme` hands them out, not with raw hex values, so the Color vision adjustment reaches what you draw. Take text and line colors from the theme's brushes (`Text`, `TextDim`, `Stroke`), so Stronger contrast reaches them.
- Call `TitleBarTheme.Attach` on every new window; that is also what makes it follow Text size and the reading font. A window that sets its own height from its content must multiply by `Accessibility.Scale`, as `InspectWindow` does.
- Do not set a font family on a control unless it must differ; text that draws itself should take the font from `TextElement.GetFontFamily`, not `FontFamily.Default`.
- Anything new that moves on its own must stop when `AppSettings.ReduceMotion` is on.
- Keep every string in `Resources/Strings.resx` so screen readers get the user's language. The tests require every key in every language file, so add the line to each: Norwegian by hand, the others as machine translations.
- Anything that draws with coordinates must pin `FlowDirection = LeftToRight` on itself, as `TreemapControl`, `DonutChart`, `Minimap` and `Icon` do; everything else should use alignment, not fixed margins, so it mirrors in Arabic. Try it with `"Language": "ar"` in `settings.json`.
- Test at 150 % display scaling and at Text size 150 % at least once; layouts that only work at 100 % are a bug. Test with Narrator once if you touched a window; it ships with Windows (Win+Ctrl+Enter).
