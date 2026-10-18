#!/usr/bin/env python3
"""
SpaceSharp brand assets: the "Core" mark.

An isometric amber block with a cube carved out of its front corner. Three lit
faces (pale top, brand amber left, deep amber right), three graphite faces
inside the void. The mark itself has no background tile: the silhouette is the
icon on the README, the landing page and inside the app. The Windows icon
(taskbar, Explorer, the title bar) sits on the rounded Graphite tile the old
nine-cell icon used, so it reads as an app icon next to other apps' tiles.

One definition here produces every brand file in the repository:

  SpaceSharp/Assets/SpaceSharp.svg        master mark (deep carve)
  SpaceSharp/Assets/SpaceSharp.ico        Windows icon on the tile: 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 (40 and 96 are what the taskbar asks for at 125% and 150% scaling)
  SpaceSharp/Assets/SpaceSharp-256.png    the mark the app shows in its own UI
  docs/icon.png, docs/icon-512.png        landing page favicon and store icon (the bare mark)
  docs/app-icon-256.png, -512.png         the Windows icon on its tile, as PNG, for listings that want the app icon
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
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

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

# The tile behind the Windows icon: the Panel tone on rounded corners (about a fifth of the side), the same
# tile the nine-cell icon had. The mark fills three quarters of it so the amber stays bold at 16 px.
TILE_COLOR = PANEL
TILE_RADIUS = 0.22
TILE_MARK = 0.78

def tile_image(px: int, interior=INTERIOR_DARK) -> Image.Image:
    S = 4 if px < 128 else 2                      # draw the corners oversampled, then shrink
    big = px * S
    tile = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    ImageDraw.Draw(tile).rounded_rectangle([0, 0, big - 1, big - 1], radius=round(big * TILE_RADIUS), fill=TILE_COLOR + (255,))
    tile = tile.resize((px, px), Image.LANCZOS)
    m = max(8, round(px * TILE_MARK))
    if m % 2 != px % 2:
        m += 1                                    # keep the mark centered on whole pixels
    tile.alpha_composite(mark_image(m, interior), ((px - m) // 2, (px - m) // 2))
    return tile

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
    lock = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 120" width="640" height="120" role="img" aria-label="SpaceSharp">'
            f'<style>text{{font-family:"Bricolage Grotesque","Segoe UI",sans-serif;letter-spacing:-0.035em}}</style>'
            f'<g transform="translate(10,10)">{mark_body()}</g>'
            f'<text x="128" y="88" font-weight="800" font-size="76" fill="{hexs(OFFWHITE)}">Space<tspan fill="{hexs(AMBER)}">Sharp</tspan></text></svg>')
    write("docs/wordmark.svg", lock)

def make_pngs():
    write("SpaceSharp/Assets/SpaceSharp-256.png", png_bytes(mark_image(256)))
    write("docs/icon.png", png_bytes(mark_image(256)))
    write("docs/icon-512.png", png_bytes(mark_image(512)))
    write("docs/app-icon-256.png", png_bytes(tile_image(256)))   # the Windows icon on its tile, for store listings
    write("docs/app-icon-512.png", png_bytes(tile_image(512)))

def make_ico(rel: str, sizes=(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)):
    frames = [tile_image(px) for px in sizes]
    buf = io.BytesIO()
    frames[-1].save(buf, "ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    write(rel, buf.getvalue())

def version_pill(d: ImageDraw.ImageDraw, x: int, y: int, px: int, label: str, ring: int = 0) -> int:
    """An amber pill with the version, sized to go next to a wordmark of px. A ring in the ground color
    separates it from whatever it sits on. Returns the end x."""
    fpx = int(px * 0.46)
    f = font(800, fpx)
    pad = int(px * 0.32)
    h = int(px * 0.72)
    wdt = int(d.textlength(label, font=f)) + 2 * pad
    if ring:
        d.rounded_rectangle([x - ring, y - ring, x + wdt + ring, y + h + ring], radius=h // 2 + ring, fill=GRAPHITE)
    d.rounded_rectangle([x, y, x + wdt, y + h], radius=h // 2, fill=AMBER)
    bbox = f.getbbox(label)
    d.text((x + pad, y + (h - (bbox[3] - bbox[1])) // 2 - bbox[1]), label, font=f, fill=(0x1C, 0x15, 0x00))
    return x + wdt

def version_label() -> str:
    """The full <Version> from SpaceSharp.csproj, so the badge follows every release."""
    import re
    text = (ROOT / "SpaceSharp" / "SpaceSharp.csproj").read_text(encoding="utf-8")
    m = re.search(r"<Version>([\d.]+)</Version>", text)
    return m.group(1) if m else "2.0.0"

VERSION_LABEL = version_label()

def version_tag(d: ImageDraw.ImageDraw, x: int, y: int, px: int, label: str, fill=AMBER, ink=(0x1C, 0x15, 0x00), outline=None) -> int:
    """A small rounded tag with the version. Returns the end x."""
    f = font(700, px)
    pad = int(px * 0.6)
    h = int(px * 1.7)
    wdt = int(d.textlength(label, font=f)) + 2 * pad
    d.rounded_rectangle([x, y, x + wdt, y + h], radius=int(px * 0.35), fill=fill, outline=outline, width=2 if outline else 0)
    bbox = f.getbbox(label)
    d.text((x + pad, y + (h - (bbox[3] - bbox[1])) // 2 - bbox[1]), label, font=f, fill=ink)
    return x + wdt

def make_header(w=1280, h=360, scale=2):
    """README header: the lockup on a Panel card with an amber accent bar, drawn at 2x for high-DPI screens."""
    S = scale
    im = Image.new("RGBA", (w * S, h * S), GRAPHITE + (255,))
    d = ImageDraw.Draw(im)
    stroke = (0x30, 0x36, 0x3D)
    cx0, cy0, cx1, cy1 = 110 * S, 60 * S, (w - 110) * S, (h - 60) * S
    radius = 24 * S
    d.rounded_rectangle([cx0, cy0, cx1, cy1], radius=radius, fill=PANEL, outline=stroke, width=2 * S)
    # the accent bar follows the card's corners: a rounded rectangle of the same radius, clipped to its first 10 px
    bar = Image.new("RGBA", im.size, (0, 0, 0, 0))
    ImageDraw.Draw(bar).rounded_rectangle([cx0, cy0, cx0 + 2 * radius, cy1], radius=radius, fill=AMBER + (255,))
    clip = Image.new("L", im.size, 0)
    ImageDraw.Draw(clip).rectangle([cx0, cy0, cx0 + 10 * S, cy1], fill=255)
    bar.putalpha(ImageChops.multiply(bar.getchannel("A"), clip))
    im.alpha_composite(bar)
    paste_mark(im, 150 * S, (cx0 + 60 * S, cy0 + 45 * S))
    px = 76 * S
    x = cx0 + 250 * S
    end = wordmark(d, x, cy0 + 48 * S, px)
    version_tag(d, end + 22 * S, cy0 + 60 * S, 22 * S, VERSION_LABEL)
    tag = font(500, 25 * S)
    d.text((x, cy0 + 150 * S), "See where your disk space went.", font=tag, fill=MUTED)
    hint = font(500, 18 * S)
    d.text((x, cy0 + 190 * S), "A disk space treemap for Windows, macOS and Linux", font=hint, fill=HINT)
    write("docs/header.png", png_bytes(im))

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
