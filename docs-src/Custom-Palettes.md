<p align="center"><img src="images/palette-editor.png" width="900" alt="The palette editor: hue, scheme and depth on the left, the sample map on the right"></p>

A custom palette is a JSON file in your palettes folder. The **palette editor** writes one for you; a text editor works too. Either way the palette appears in the picker's Custom tab next to the built-in ones, and the map uses it like any other.

## Opening the editor

- The palette picker in the toolbar: **New palette…** in its corner.
- Settings › Treemap › Custom palettes: **New palette**, or **Edit** under a custom palette you already have, or **Start from…** under any built-in one to begin with its colors.

## What is in it

The window is a short list of decisions on the left and a live sample map on the right. A palette is generated from the decisions and then touched up color by color if you want; most palettes never need the second part.

**Name and theme.** What the picker will call it (built-in names are refused), and which theme it is **for**: Dark, Light or Both, which decides which tab lists it.

**Random, From a picture…, Start from.** Random picks a base color and a scheme; press it until something looks right, then adjust. From a picture takes the dominant hues of any image file and makes the twelve folder colors from them, at the lightness you have set. Start from lists every palette, built-in and custom, and copies its colors in.

**Colors.** Hue, Saturation and Lightness sliders move the whole palette at once. **Scheme** decides how the twelve folder hues spread from the base: **One hue** (shades of it), **Neighbors** (hues close to it), **Two-tone** (the hue and its opposite, alternating), **Triad** (three hues a third of the wheel apart) or **Rainbow** (twelve hues around the wheel). Each card shows four of the colors it would make from the current base.

**Depth and files.** **Each level** steps the lightness per depth, from 4% darker to 4% lighter per level, so deep folders can read darker or lighter than the drive. **Files** are drawn the same as their folder, tinted toward white by a slider's amount (the default), or in own colors you set per depth under Advanced.

**The guard.** One line under the controls: "Labels read on every folder color, and no two are too alike", or which folders fail, in which case **Fix them** moves the failing colors in lightness until their labels read and nudges the hue of any pair too alike.

**The preview.** The sample map, drawn with your shading settings, in the Color by mode you pick above it. It follows every change, and a click on any box selects that box's color for editing, which opens Advanced.

**Advanced.** Swatch grids for the twelve folder colors, the twelve file colors when they are your own, and the file-type colors (prefilled from the folders; a type you set stays set when the folders change, one you did not follows them). Click a swatch and the editor under the grids shows it: the hex you can type into, hue, saturation and lightness sliders, which text color labels get on it and the contrast ratio, and who else uses it. **Back to derived** forgets your choice for a type. The row at the bottom has **Copy JSON** (the file's text on the clipboard), **Paste 12 hexes** (the codes on your clipboard become the folder colors, in order) and **Export file…** (the file anywhere you choose, without installing it).

**Save and use** writes the file to your palettes folder and switches the map to it. Saving over an existing custom palette asks first.

## The files

`%LocalAppData%\SpaceSharp\palettes`, one `.json` per palette. **Open folder** in Settings › Treemap › Custom palettes takes you there and writes `Example.json` (commented) and a README the first time. **Reload** picks up files you edited or dropped in without a restart.

```jsonc
{
  "name": "Harbor",
  "theme": "dark",            // "dark", "light" or "any": which tab lists it
  "folders": ["#4F7CAC", "#49B86B", "#E8C547", "#E8864A", "#B565D9", "#4CC3C9", "#E86A8A"],
  "fileTint": 0.45,           // files are the folder colors lifted toward white by this (0 to 0.9) ...
  // "files": [...],          // ... unless you list twelve file colors yourself
  "categories": {             // File type mode; any left out come from the folder colors
    "video": "#E8864A", "images": "#49B86B", "audio": "#B565D9", "archives": "#E8C547",
    "programs": "#5B8DEF", "documents": "#BAC4E2", "code": "#4CC3C9", "gamedata": "#C9A66B",
    "installers": "#D9A066", "diskimages": "#C7C09A", "databases": "#5FA8D3", "fonts": "#B9A0E0",
    "system": "#8FA3B8", "other": "#B8B4AC"
  }
}
```

- One to twelve folder colors, `#RRGGBB`; fewer are cycled to twelve.
- `files`, `fileTint`, `categories` and `theme` are optional. Leave `files` out and `fileTint` applies; leave both out and the tint is 0.45.
- The fourteen type keys are `video`, `images`, `audio`, `archives`, `programs`, `documents`, `code`, `gamedata`, `installers`, `diskimages`, `databases`, `fonts`, `system`, `other`.
- Comments and trailing commas are allowed. A file that cannot be read is listed with its error under Custom palettes rather than skipped silently.
- The palette's line in the picker is its file name.

## Sharing one

The same generator runs in a browser at [palette.spacesharp.app](https://palette.spacesharp.app/), with nothing to install: it makes the same file, and **Copy a link to this palette** gives an address that opens the page with your colors in it.

Send the `.json`, or the **Copy JSON** text. The other person drops it in their palettes folder and presses Reload. A palette worth having in the app itself can be proposed as a pull request: built-in palettes live in `BuildSchemes()` in `SpaceSharp/Util/Palette.cs` with a one-line description ([Architecture](Architecture#where-to-add-things)).
