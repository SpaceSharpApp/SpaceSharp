# Accessibility

This page says plainly what SpaceSharp does and does not do for people who use a screen reader, keyboard only, high contrast, large text, a right-to-left language, or who see color differently. It is written to be checked against the current release and updated when something changes; if you find it wrong, that is a bug worth reporting (see below).

## What works today

**Text size and scaling.** SpaceSharp is a WPF app and follows the Windows display scaling (125 %, 150 %, 200 % and so on) in every window, including the map, the hover card and the dialogs. Inside the map, Settings › Appearance › Label size has five steps from Smallest to Larger, and Settings › Treemap › Font changes the typeface; Outlined labels adds a contrasting outline so text stays readable on any box color. Labels that would not fit are dropped rather than clipped, and the hover card and Inspect show the full name.

**Color.** The Color-blind safe palettes (one dark, one light) use the Okabe and Ito colors, chosen to stay distinguishable with the common kinds of color vision deficiency. Color is never the only carrier of information: every box has a label with its name and size, the legend names each category, Color by change has a legend and names the change in words in the hover card and Inspect ("grew 1.2 GB since the last scan"), and the filter dims non-matches rather than only recoloring them. The palette creator checks every color you pick against the label text and warns when the contrast is too low or two folder colors are too alike, so a home-made palette cannot silently become unreadable. Light and dark themes follow Windows or can be fixed.

**Keyboard.** Most of the app is reachable without a mouse: F5 rescans, Backspace and Home move around the map, Ctrl+F opens the filter, Ctrl+A selects matches, Ctrl+I opens Inspect, Del deletes, Ctrl+S and Ctrl+O save and open scans, Ctrl+, opens Settings, F1 opens About, and S, K, G, L switch styles, colors, density and the side panel. The full list is in About › Shortcuts. Dialogs answer to Enter and Escape and keep focus inside them. Toolbar buttons show an accent outline when they have keyboard focus, so Tab is visible.

**Standard controls.** The side panel, toolbar, filter panel, Insights view, Settings, Inspect, About, the palette creator and all dialogs are built from ordinary WPF controls, so Narrator, NVDA and JAWS read their text, state and roles, and Tab moves through them in a sensible order. Icon-only buttons carry their tooltip as their accessible name, so a screen reader says "Rescan (F5)" rather than "button".

**The numbers without the map.** The Insights view (the button next to Map) is the map rewritten as text: totals, space by file type, what is safe to clear, when files were last changed, the largest folders and files, and what changed since the last scan, as ordinary lists a screen reader walks row by row. Every row is a button that zooms the map to that item. If the map is not usable for you, Insights carries the same information.

**Language and direction.** English, Norwegian bokmål and Arabic; the app follows the Windows language by default and can be set in Settings › Language. For Arabic and other right-to-left languages every window mirrors: panels swap sides, text and numbers align right, menus and tooltips follow. The map, the donut charts and the icons keep their drawn shape, since mirroring a treemap would only move things without making it more readable.

**Motion.** The zoom animation can be turned off in Settings › Map › Animate zoom. Nothing else in the app moves on its own except the progress sweep while a scan runs.

## What does not work yet

**The map itself is not accessible to screen readers.** The treemap is a custom-drawn surface with no UI Automation tree behind it: a screen reader sees one large control and nothing inside it, and arrow keys do not move between boxes. The information in the map is available elsewhere (Insights, the side panel, Inspect for any selected item, the filter's match count), but there is no way yet to walk the map's folders with a keyboard or have them read out. This is the largest gap and the one we most want to close.

**High-contrast themes.** The app uses its own colors rather than the system high-contrast palette, so turning on a Windows high-contrast theme does not restyle SpaceSharp. The dark and light themes have strong contrast on their own, but they are not the user's chosen colors.

**System animation setting.** Animate zoom is an app setting; it does not yet read the Windows "Show animations" preference. Turn Animate zoom off if motion is a problem.

**Hover-only information.** The hover card shows details for the item under the mouse; the same details are reachable with Ctrl+I on a selection, but there is no keyboard way to point at an arbitrary box.

**Sliders and color pickers.** The shading sliders in Settings and the hue, saturation and lightness sliders in the palette creator work with the arrow keys but do not announce their values as they change. The hex field beside each color does, so use that with a screen reader.

**Arabic numerals.** With the app in Arabic, sizes and dates use Eastern Arabic digits because Windows' Arabic locale does; there is no switch yet to keep Western digits.

## Planned

In rough order:

1. A UI Automation peer for the map so each visible box is an element with its name, size and position in the hierarchy, and arrow keys move between siblings, Enter zooms in, Backspace goes up, with a drawn focus indicator like the selection frame.
2. Honoring the Windows "Show animations" setting and high-contrast mode.
3. Spoken values for sliders, and a Western-digits option for Arabic.

If you depend on one of these, say so in an issue; it helps decide what comes first.

## Reporting a problem

Open a bug report through the issue chooser, or use About › Report a bug, and start the title with "Accessibility:" so it is easy to find. Please include which assistive technology you use (and its version), your Windows scaling and theme, what you expected, and what happened. Keyboard-only, low-vision and right-to-left reports are as welcome as screen-reader ones.

If you would rather not report publicly, use the contact on the maintainer's GitHub profile at [github.com/ClearanceClarence](https://github.com/ClearanceClarence).

## For contributors

- New controls must be reachable with Tab and have a name a screen reader can speak. Buttons with only an icon get their name from their tooltip automatically (the `ToolButton` style binds `AutomationProperties.Name` to `ToolTip`), so give every icon button a tooltip; set `AutomationProperties.Name` by hand on anything else whose purpose is only shown by a picture.
- Do not communicate state by color alone; add text, a glyph or a tooltip.
- Keep every string in `Resources/Strings.resx` so screen readers get the user's language, and add the Norwegian and Arabic lines too; a key missing from a translation falls back to English silently, which is easy to miss.
- Anything that draws with coordinates must pin `FlowDirection = LeftToRight` on itself, as `TreemapControl`, `DonutChart` and `Icon` do; everything else should use alignment, not fixed margins, so it mirrors in Arabic. Try it with `"Language": "ar"` in `settings.json`.
- Test at 150 % scaling at least once; layouts that only work at 100 % are a bug. Test with Narrator once if you touched a window; it ships with Windows (Win+Ctrl+Enter).
