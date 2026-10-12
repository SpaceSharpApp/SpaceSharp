# Third-party notices

SpaceSharp is MIT licensed (see LICENSE). This file lists everything it ships, builds with, or borrows from that is not its own, with the license each one comes under. Nothing here changes SpaceSharp's license; the notices are reproduced because those licenses ask for it.

## In the app

### Velopack

The installer (`Setup.exe`), the updater (`Update.exe`) and the update check inside the app come from Velopack, https://velopack.io, pulled in as the NuGet package `Velopack` (the only package dependency of the app). The MSI is produced by `vpk`, Velopack's command-line tool, which uses the WiX Toolset under the hood.

MIT License. Copyright (c) 2021 Caelan Sayler and Velopack contributors.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

### Lucide icons

The interface icons in `SpaceSharp/Themes/Icons.xaml` (and the copies in `docs/spacesharp-mock.js`) are from Lucide, https://lucide.dev, release 0.468.0, converted to WPF path geometry by `tools/make-icons.py`. A few drawn in the same style (the SSD and USB stick pictures) are SpaceSharp's own.

ISC License. Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of Feather (MIT). All other copyright (c) for Lucide are held by Lucide Contributors 2022.

Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, provided that the above copyright notice and this permission notice appear in all copies. THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

### .NET and WPF

The app is built on .NET 10 and Windows Presentation Foundation, https://github.com/dotnet/wpf, MIT license, Copyright (c) .NET Foundation and Contributors. The portable and Setup builds carry the runtime inside them (self-contained); the Small build expects the .NET Desktop Runtime to be installed. The interface uses Segoe UI, which is part of Windows; no font files ship with the app.

### Windows APIs

The scanner, the Recycle Bin, the title bar colors and the drive detection call Windows directly (`kernel32.dll`, `shell32.dll`, `dwmapi.dll`, and the registry through `Microsoft.Win32`). These are parts of Windows, used under its license; nothing from them is redistributed.

### Algorithms and data

- The treemap layout is the squarified algorithm from "Squarified Treemaps" by Mark Bruls, Kees Huizing and Jarke J. van Wijk (2000). Algorithms are not copyrightable; the implementation is SpaceSharp's own.
- The color-blind safe palettes use the eight colors proposed by Masataka Okabe and Kei Ito ("Color Universal Design", 2002, https://jfly.uni-koeln.de/color/), which are free to use.
- The Insights view's "safe to clear" places (Windows.old, caches, the Recycle Bin and so on) are a list SpaceSharp maintains; it is not taken from another tool.
- The idea of a zoomable treemap of a drive goes back to Ben Shneiderman's work at the University of Maryland (1991) and, as a Windows app, to SpaceMonger by Sean Werkema, now at https://www.stardock.com/products/spacemonger/. SpaceSharp shares no code with it. SpaceMonger, WizTree and WinDirStat are the property of their respective owners and are mentioned only for comparison.

## Used to build and test, not shipped

- **xUnit** 2.9 (Apache License 2.0), **xunit.runner.visualstudio** (Apache License 2.0) and **Microsoft.NET.Test.Sdk** (MIT): the test project, `SpaceSharp.Tests`.
- **GitHub Actions**: `actions/checkout`, `actions/setup-dotnet` and `actions/upload-artifact` (MIT), in `.github/workflows`.
- **Windows SDK** `signtool.exe` for code signing (Microsoft Software License Terms), called by `installer/release.ps1`.

## Used for the website, the screenshots and the brand files, not shipped

- **Bricolage Grotesque** by Mathieu Triay, https://github.com/ateliertriay/bricolage, SIL Open Font License 1.1. The landing page loads it from Google Fonts; `tools/make-assets.py` downloads the TTF to draw the wordmark into the header, the installer bitmaps and the social preview. The font is not inside the app or the installers.
- **Pillow** (MIT-CMU / HPND license) and **CairoSVG** (LGPL 3.0, used as a library, unmodified): `tools/make-assets.py`, `make-social.py` and `make-mobile-shots.py` render the icon, the images and the WebP screenshots.
- **Playwright** (Apache License 2.0) with its bundled Chromium: `tools/screenshots.js` captures the README screenshots from `docs/spacesharp-mock.js`.
- **Shields.io** badges in the README are images fetched from https://shields.io (CC0 for the service's output).
- The landing page is plain HTML, CSS and JavaScript with no frameworks; it loads nothing but the Google Fonts stylesheet above.

## Trademarks

Windows, .NET and Segoe are trademarks of Microsoft Corporation. GitHub is a trademark of GitHub, Inc. Mentioning them does not imply endorsement.
