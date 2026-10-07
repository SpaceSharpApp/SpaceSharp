#!/usr/bin/env python3
"""
SpaceSharp brand assets: the "Core" mark.

An isometric amber block with a cube carved out of its front corner. Three lit
faces (pale top, brand amber left, deep amber right), three graphite faces
inside the void. No background tile: the silhouette is the icon.

One definition here produces every brand file in the repository:

  SpaceSharp/Assets/SpaceSharp.svg        master mark (deep carve)
  SpaceSharp/Assets/SpaceSharp-small.svg  shallow carve, used for 16 to 24 px
  SpaceSharp/Assets/SpaceSharp.ico        Windows icon: 16, 20, 24, 32, 48, 64, 128, 256
  SpaceSharp/Assets/SpaceSharp-256.png    the mark the app shows in its own UI
  docs/icon.png, docs/icon-512.png        landing page favicon and store icon
  docs/header.png                         README header, 1280x360 on GitHub's #0D1117
  docs/wordmark.svg                       horizontal lockup, mark + wordmark
  installer/banner.bmp                    WiX WixUIBannerBmp, 493x58 (branding at the right edge)
  installer/logo.bmp                      WiX WixUIDialogBmp, 493x312 (branding in the left column)

tools/make-social.py imports this file for the palette, fonts and mark.

Requires: pip install cairosvg pillow
The wordmark is Bricolage Grotesque; the TTF is downloaded from google/fonts on
first run and cached in tools/.fonts/ (ignored by git).
"""
from __future__ import annotations
import io, math, urllib.request
from pathlib import Path

import cairosvg
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent

# ---------------------------------------------------------------- palette
AMBER       = (0xF5, 0xB8, 0x2E)   # brand amber: left face, accents
AMBER_LIGHT = "#FFD166"            # top face
AMBER_DEEP  = "#C98E22"            # right face
GRAPHITE    = (0x0D, 0x11, 0x17)   # GitHub dark page color, app window
PANEL       = (0x16, 0x1B, 0x22)
OFFWHITE    = (0xE6, 0xED, 0xF3)
MUTED       = (0x8B, 0x94, 0x9E)
HINT        = (0x6E, 0x76, 0x81)

INTERIOR_DARK  = ("#30363D", "#161B22", "#21262D")   # inner left wall, inner right wall, floor
INTERIOR_LIGHT = ("#484F58", "#21262D", "#30363D")   # one step lighter, for white grounds

def hexs(rgb) -> str:
    return "#%02X%02X%02X" % rgb[:3]

def blend(a, b, t: float):
    """t of color a over color b (both RGB tuples)."""
    return tuple(round(a[i] * t + b[i] * (1 - t)) for i in range(3))

# ---------------------------------------------------------------- geometry
# Isometric cube with edge L and its front vertex at (CX, CY); a cube of edge l
# is carved out of that corner. The mark is the deep carve, l = 2/3 L.
CX, CY, L = 50.0, 52.0, 42.0
CARVE_DEEP = 28.0
CARVE_MID = 22.0
CARVE_SMALL = 16.0

def carve_for(px: int) -> float:
    """Shallower carve at small sizes so the hole stays a hole, not a dot."""
    if px <= 24:
        return CARVE_SMALL
    if px <= 48:
        return CARVE_MID
    return CARVE_DEEP

def _pt(x, y):
    return f"{x:.2f},{y:.2f}"

def mark_paths(l: float, top: str, left: str, right: str,
               in_l: str, in_r: str, floor: str) -> str:
    h = math.sqrt(3) / 2 * L
    T, TL, TR = _pt(CX, CY - L), _pt(CX - h, CY - L / 2), _pt(CX + h, CY - L / 2)
    C, BL, BR, B = _pt(CX, CY), _pt(CX - h, CY + L / 2), _pt(CX + h, CY + L / 2), _pt(CX, CY + L)
    hl = math.sqrt(3) / 2 * l
    nU, nUR, nLR = _pt(CX, CY - l), _pt(CX + hl, CY - l / 2), _pt(CX + hl, CY + l / 2)
    nD, nLL, nUL = _pt(CX, CY + l), _pt(CX - hl, CY + l / 2), _pt(CX - hl, CY - l / 2)
    P = lambda pts, fill: f'<polygon points="{pts}" fill="{fill}"/>'
    return "".join([
        P(f"{T} {TR} {nUR} {nU} {nUL} {TL}", top),
        P(f"{TL} {nUL} {nLL} {nD} {B} {BL}", left),
        P(f"{TR} {BR} {B} {nD} {nLR} {nUR}", right),
        P(f"{nU} {C} {nLL} {nUL}", in_l),
        P(f"{nU} {nUR} {nLR} {C}", in_r),
        P(f"{C} {nLR} {nD} {nLL}", floor),
    ])

def mark_body(l: float = CARVE_DEEP, interior=INTERIOR_DARK, mono: str | None = None) -> str:
    """The polygons only, for inlining into another SVG."""
    if mono:
        return mark_paths(l, mono, mono, mono, "none", "none", "none")
    return mark_paths(l, AMBER_LIGHT, hexs(AMBER), AMBER_DEEP, *interior)

def mark_svg(l: float = CARVE_DEEP, interior=INTERIOR_DARK, mono: str | None = None,
             size: int | None = None) -> str:
    dim = f' width="{size}" height="{size}"' if size else ""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"{dim} '
            f'role="img" aria-label="SpaceSharp">{mark_body(l, interior, mono)}</svg>')

# ---------------------------------------------------------------- raster helpers
def raster(svg: str, px: int) -> Image.Image:
    png = cairosvg.svg2png(bytestring=svg.encode(), output_width=px, output_height=px)
    return Image.open(io.BytesIO(png)).convert("RGBA")

def mark_image(px: int, interior=INTERIOR_DARK) -> Image.Image:
    return raster(mark_svg(carve_for(px), interior), px)

def paste_mark(im: Image.Image, px: int, xy, tile=None, interior=INTERIOR_DARK):
    """Draw the mark at px on im at xy. `tile` is accepted for compatibility and ignored: the mark has no tile."""
    im.alpha_composite(mark_image(px, interior), (int(xy[0]), int(xy[1])))

# ---------------------------------------------------------------- fonts
FONT_URL = ("https://raw.githubusercontent.com/google/fonts/main/ofl/bricolagegrotesque/"
            "BricolageGrotesque%5Bopsz%2Cwdth%2Cwght%5D.ttf")
FONT_PATH = HERE / ".fonts" / "BricolageGrotesque.ttf"

def ensure_fonts():
    if not FONT_PATH.exists():
        FONT_PATH.parent.mkdir(parents=True, exist_ok=True)
        print("  downloading Bricolage Grotesque")
        urllib.request.urlretrieve(FONT_URL, FONT_PATH)

def font(weight: int, px: int) -> ImageFont.FreeTypeFont:
    ensure_fonts()
    f = ImageFont.truetype(str(FONT_PATH), px)
    f.set_variation_by_axes([96, weight, 100])  # opsz, wght, wdth
    return f

def wordmark(d: ImageDraw.ImageDraw, x: int, y: int, px: int,
             color=OFFWHITE, accent=AMBER, tracking=-0.03) -> int:
    """Draws Space|Sharp with 'Sharp' in amber. Returns the end x."""
    f = font(800, px)
    cur = x
    for word, col in (("Space", color), ("Sharp", accent)):
        for ch in word:
            d.text((cur, y), ch, font=f, fill=col)
            cur += d.textlength(ch, font=f) + tracking * px
    return int(cur)

def wordmark_width(px: int) -> int:
    probe = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
    return wordmark(probe, 0, 0, px)

# ---------------------------------------------------------------- outputs
def write(rel: str, data: bytes | str):
    path = ROOT / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(data, bytes):
        path.write_bytes(data)
    else:
        path.write_text(data, encoding="utf-8", newline="\n")
    print(f"  {rel}")

def png_bytes(im: Image.Image) -> bytes:
    buf = io.BytesIO(); im.save(buf, "PNG", optimize=True); return buf.getvalue()

def make_svgs():
    write("SpaceSharp/Assets/SpaceSharp.svg", mark_svg())
    write("SpaceSharp/Assets/SpaceSharp-small.svg", mark_svg(CARVE_SMALL))
    lock = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 120" width="640" height="120" role="img" aria-label="SpaceSharp">'
            f'<style>text{{font-family:"Bricolage Grotesque","Segoe UI",sans-serif;letter-spacing:-0.035em}}</style>'
            f'<g transform="translate(10,10)">{mark_body()}</g>'
            f'<text x="128" y="88" font-weight="800" font-size="76" fill="{hexs(OFFWHITE)}">Space<tspan fill="{hexs(AMBER)}">Sharp</tspan></text></svg>')
    write("docs/wordmark.svg", lock)

def make_pngs():
    write("SpaceSharp/Assets/SpaceSharp-256.png", png_bytes(mark_image(256)))
    write("docs/icon.png", png_bytes(mark_image(256)))
    write("docs/icon-512.png", png_bytes(mark_image(512)))

def make_ico(rel: str, sizes=(16, 20, 24, 32, 48, 64, 128, 256)):
    frames = [mark_image(px) for px in sizes]
    buf = io.BytesIO()
    frames[-1].save(buf, "ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    write(rel, buf.getvalue())

def make_header(w=1280, h=360):
    im = Image.new("RGBA", (w, h), GRAPHITE + (255,))
    paste_mark(im, 150, ((w - 150) // 2, 44))
    d = ImageDraw.Draw(im)
    wordmark(d, (w - wordmark_width(68)) // 2, 212, 68)
    tag = font(500, 24)
    t = "See where your disk space went."
    d.text(((w - d.textlength(t, font=tag)) // 2, 302), t, font=tag, fill=MUTED)
    write("docs/header.png", png_bytes(im))

def make_installer_bitmaps():
    # WiX banner, 493x58: the text area stays light, branding only in a block at the right edge.
    ban = Image.new("RGB", (493, 58), "#FFFFFF")
    blk = Image.new("RGBA", (84, 58), GRAPHITE + (255,))
    paste_mark(blk, 40, (22, 9))
    ban.paste(blk, (493 - 84, 0), blk)
    buf = io.BytesIO(); ban.save(buf, "BMP"); write("installer/banner.bmp", buf.getvalue())
    # WiX dialog, 493x312: branding in the left column (164 px), the text area to the right stays light.
    dlg = Image.new("RGB", (493, 312), "#FFFFFF")
    col = Image.new("RGBA", (164, 312), GRAPHITE + (255,))
    paste_mark(col, 88, (38, 92))
    d = ImageDraw.Draw(col)
    wordmark(d, (164 - wordmark_width(24)) // 2, 196, 24)
    dlg.paste(col, (0, 0), col)
    buf = io.BytesIO(); dlg.save(buf, "BMP"); write("installer/logo.bmp", buf.getvalue())

def make_splash(w=460, h=250, frames=36):
    """The window Setup.exe and Update.exe show while they work (Velopack's --splashImage). A Graphite card with
    the mark, the wordmark and an amber sweep that keeps moving, so an install that takes a few seconds does not
    look stuck. An animated GIF, which Velopack plays; the first frame also works as a still."""
    def frame(t: float) -> Image.Image:
        im = Image.new("RGBA", (w, h), GRAPHITE + (255,))
        d = ImageDraw.Draw(im)
        d.rectangle([0, 0, w - 1, h - 1], outline=(0x30, 0x36, 0x3D), width=1)
        paste_mark(im, 72, (40, 48))
        wordmark(d, 132, 56, 34)
        tag = font(500, 15)
        d.text((134, 100), "See where your disk space went.", font=tag, fill=MUTED)
        d.text((40, 170), "Setting up", font=font(600, 14), fill=OFFWHITE)   # one splash serves both Setup.exe and Update.exe
        # the track, and a sweep that runs along it and fades at both ends
        x0, x1, y, th = 40, w - 40, 198, 4
        d.rounded_rectangle([x0, y, x1, y + th], radius=th // 2, fill=(0x21, 0x26, 0x2D))
        span = x1 - x0; sw = int(span * 0.28)
        head = int(x0 - sw + (span + sw) * t)
        for i in range(sw):
            k = i / sw
            a = (1 - abs(k * 2 - 1)) ** 1.5           # bright in the middle, fading to both ends
            xx = head + i
            if x0 <= xx <= x1:
                d.line([(xx, y), (xx, y + th)], fill=blend(AMBER, (0x21, 0x26, 0x2D), a))
        return im.convert("P", palette=Image.ADAPTIVE, colors=128)
    seq = [frame(i / frames) for i in range(frames)]
    buf = io.BytesIO()
    seq[0].save(buf, "GIF", save_all=True, append_images=seq[1:], duration=40, loop=0, optimize=False)
    write("installer/splash.gif", buf.getvalue())

if __name__ == "__main__":
    print(f"writing into {ROOT}")
    make_svgs()
    make_pngs()
    make_ico("SpaceSharp/Assets/SpaceSharp.ico")
    make_header()
    make_installer_bitmaps()
    make_splash()
    print("done; run tools/make-social.py for docs/social-preview.png")
