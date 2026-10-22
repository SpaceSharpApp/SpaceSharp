# Branding

The Illustrator sources for the SpaceSharp brand. Everything the app, the installer, the README and the site show is exported from here by hand; nothing is generated.

| Source | Exports |
|---|---|
| `SpaceSharpIcon.ai` | The mark on its own: `SpaceSharp/Assets/SpaceSharp.svg`, `SpaceSharp-256.png`, `docs/icon.png`, `docs/icon-512.png`, `docs/wordmark.svg` (the mark next to the wordmark) |
| `SpaceSharpAppIcon.ai` | The mark on the rounded Graphite tile, for Windows: `SpaceSharp/Assets/SpaceSharp.ico` (16, 20, 24, 32, 40, 48, 64, 96, 128, 256 px), `docs/app-icon-256.png`, `docs/app-icon-512.png` |
| `SpaceSharpAppHeader.ai` | `docs/header.png` (2560 x 720; the README shows it at 800 wide) |
| `SpaceSharpAppSocialPreview.ai` | `docs/social-preview.png` (1280 x 640, GitHub's social preview) |
| `SpaceSharpAppInstallerBanner.ai` | `installer/banner.bmp` (493 x 58) |
| `SpaceSharpAppInstallerLogo.ai` | `installer/logo.bmp` (493 x 312) |

The two installer files must be 24-bit BMP with no alpha and no compression; the MSI dialogs read nothing else.

Colors: amber `#F5B82E` (left face), `#FFD166` (top), `#C98E22` (right); graphite `#0D1117` (page and window), panel `#161B22`; interior walls `#30363D`, `#161B22`, floor `#21262D`. Type is Bricolage Grotesque (SIL OFL).

The `.ai` files are stored in Git LFS (see `.gitattributes`).
