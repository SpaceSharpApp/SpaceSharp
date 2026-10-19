#!/usr/bin/env python3

"""
Generate the SpaceSharp installer artwork:

    installer/banner.bmp    WiX WixUIBannerBmp, 493x58
    installer/logo.bmp      WiX WixUIDialogBmp, 493x312

Save this script in the repository tools directory.
Requires: pip install cairosvg pillow

The wordmark is Bricolage Grotesque; the TTF is downloaded from google/fonts
on first run and cached in tools/.fonts/ (ignored by git).
"""


from __future__ import annotations

import io
import math
import urllib.request
from pathlib import Path

import cairosvg
from PIL import Image, ImageDraw, ImageFilter, ImageFont
HERE = Path(__file__).resolve().parent
ROOT = HERE.parent


# ---------------------------------------------------------------- palette
AMBER       = (0xF5, 0xB8, 0x2E)   # brand amber: left face, accents
AMBER_LIGHT = "#FFD166"            # top face
AMBER_DEEP  = "#C98E22"            # right face
OFFWHITE    = (0xE6, 0xED, 0xF3)
INTERIOR_DARK  = ("#30363D", "#161B22", "#21262D")   # inner left wall, inner right wall, floor


def hexs(rgb) -> str:
    return "#%02X%02X%02X" % rgb[:3]


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


# ---------------------------------------------------------------- installer art: "Facets"
# A low-poly landscape: triangles lit amber at the top left, falling through bronze into graphite at the bottom,
# the mark floating over a darker pool in the middle. Drawn oversampled so the edges stay crisp in a BMP.
FACETS_DIALOG = [   # (points in a 164x312 column, fill)
    ("0,0 70,0 28,52", "#F5B82E"), ("70,0 118,0 28,52", "#C98E22"), ("118,0 164,0 164,40 96,62", "#7A4A0C"),
    ("118,0 96,62 28,52", "#A6701A"), ("0,0 28,52 0,96", "#D99A1A"), ("0,96 28,52 60,110", "#8F5F14"),
    ("28,52 96,62 60,110", "#5C3E12"), ("96,62 164,40 164,120 120,128", "#3A2A14"), ("60,110 96,62 120,128", "#2B2416"),
    ("0,96 60,110 24,170 0,160", "#4A3410"), ("24,170 60,110 120,128 100,190", "#1E232B"),
    ("120,128 164,120 164,210 100,190", "#161B22"), ("0,160 24,170 100,190 60,250 0,230", "#141920"),
    ("100,190 164,210 164,312 60,250", "#0F1319"), ("0,230 60,250 164,312 0,312", "#0B0E13"),
]
FACETS_BANNER = [   # (points in a 160x58 block, fill)
    ("0,0 40,0 14,30", "#2B2416"), ("40,0 90,0 14,30", "#161B22"), ("0,30 14,30 0,58", "#1E232B"),
    ("14,30 90,0 60,58 0,58", "#0F1319"), ("90,0 130,0 100,34 60,58", "#3A2A14"), ("130,0 160,0 160,20 100,34", "#A6701A"),
    ("160,20 160,58 100,34", "#5C3E12"), ("100,34 160,58 60,58", "#141920"),
]
FACETS_GROUND = (0x0A, 0x0D, 0x12)


def facets(w: int, h: int, polys, scale: int = 4) -> Image.Image:
    """The triangles, drawn at scale and shrunk, on the ground color."""
    big = Image.new("RGBA", (w * scale, h * scale), FACETS_GROUND + (255,))
    d = ImageDraw.Draw(big)
    for pts, fill in polys:
        d.polygon([tuple(float(v) * scale for v in p.split(",")) for p in pts.split()], fill=fill)
    return big.resize((w, h), Image.LANCZOS)


def radial_veil(im: Image.Image, center, rx: int, ry: int, color, strength: float):
    """An elliptical pool of color, strongest at the center, fading to nothing at the edge."""
    w, h = im.size
    mask = Image.new("L", (w * 2, h * 2), 0)
    md = ImageDraw.Draw(mask)
    steps = 48
    for i in range(steps):
        k = 1 - i / steps                      # 1 at the rim, 0 at the center
        a = int(255 * strength * (1 - k) ** 1.4)
        md.ellipse([(center[0] - rx * k) * 2, (center[1] - ry * k) * 2, (center[0] + rx * k) * 2, (center[1] + ry * k) * 2], fill=a)
    mask = mask.filter(ImageFilter.GaussianBlur(6)).resize((w, h), Image.LANCZOS)
    layer = Image.new("RGBA", (w, h), color + (255,))
    layer.putalpha(mask)
    im.alpha_composite(layer)


def mark_with_shadow(im: Image.Image, px: int, xy, blur: int, offset: int, alpha: int):
    m = mark_image(px)
    pad = blur * 3
    sh = Image.new("RGBA", (px + pad * 2, px + pad * 2), (0, 0, 0, 0))
    sh.paste((0, 0, 0, alpha), (pad, pad), m.split()[3])
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    im.alpha_composite(sh, (int(xy[0]) - pad, int(xy[1]) - pad + offset))
    im.alpha_composite(m, (int(xy[0]), int(xy[1])))


def make_installer_bitmaps():
    # WiX banner, 493x58: the text area stays light, the facets in a block at the right edge with the mark.
    ban = Image.new("RGB", (493, 58), "#FFFFFF")
    blk = facets(160, 58, FACETS_BANNER)
    mark_with_shadow(blk, 36, (108, 11), blur=3, offset=3, alpha=150)
    ban.paste(blk, (493 - 160, 0), blk)
    buf = io.BytesIO(); ban.save(buf, "BMP"); write("installer/banner.bmp", buf.getvalue())
    # WiX dialog, 493x312: the facets fill the left column (164 px), the text area to the right stays light.
    dlg = Image.new("RGB", (493, 312), "#FFFFFF")
    col = facets(164, 312, FACETS_DIALOG)
    radial_veil(col, (82, 150), 96, 120, FACETS_GROUND, 0.85)
    mark_with_shadow(col, 92, (36, 92), blur=8, offset=10, alpha=165)
    d = ImageDraw.Draw(col)
    wordmark(d, (164 - wordmark_width(22)) // 2, 206, 22, color=(0xFF, 0xFF, 0xFF))
    tag = font(500, 10); t = "See where your disk space went."
    d.text(((164 - d.textlength(t, font=tag)) // 2, 240), t, font=tag, fill=(0xA8, 0xB1, 0xBA))
    dlg.paste(col, (0, 0), col)
    buf = io.BytesIO(); dlg.save(buf, "BMP"); write("installer/logo.bmp", buf.getvalue())


if __name__ == "__main__":
    make_installer_bitmaps()
