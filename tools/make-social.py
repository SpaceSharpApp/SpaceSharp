"""Builds docs/social-preview.png (1280x640): the mark, large, on the left; the wordmark, the version, the tagline
and one line of what it is on the right. Graphite ground, amber accent, no screenshot. Run after make-assets.py
(it imports the palette, the mark and the fonts from there)."""
from pathlib import Path
import importlib.util
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('ma', HERE / 'make-assets.py'); ma = importlib.util.module_from_spec(spec); spec.loader.exec_module(ma)
ma.ensure_fonts()

S = 2; W, H = 1280 * S, 640 * S
GITHUB = (0x0D, 0x11, 0x17)
im = Image.new('RGBA', (W, H), GITHUB + (255,))

# ---- the mark, 400 px, with a soft shadow under it
MARK, MX, MY = 400, 96, 120
mark = ma.mark_image(MARK * S)
shadow = Image.new('RGBA', (W, H), (0, 0, 0, 0))
sil = Image.new('RGBA', mark.size, (0, 0, 0, 0)); sil.paste((0, 0, 0, 140), (0, 0), mark.split()[3])
shadow.paste(sil, (MX * S, (MY + 30) * S), sil)
shadow = shadow.filter(ImageFilter.GaussianBlur(26 * S))
im.alpha_composite(shadow)
im.alpha_composite(mark, (MX * S, MY * S))

# ---- the words
d = ImageDraw.Draw(im)
x = (MX + MARK + 72) * S
y = 172 * S
# the wordmark and the version pill
end = ma.wordmark(d, x, y, 30 * S)
pill = ma.font(800, 15 * S); label = "2.0"
pw = int(d.textlength(label, font=pill)) + 24 * S
px, py = end + 14 * S, y + 6 * S
d.rounded_rectangle([px, py, px + pw, py + 26 * S], radius=13 * S, fill=ma.AMBER)
d.text((px + 12 * S, py + 4 * S), label, font=pill, fill=(0x1C, 0x15, 0x00))

# the tagline, two lines
y += 56 * S
head = ma.font(800, 76 * S)
for line in ("See where your", "disk space went."):
    d.text((x - 3 * S, y), line, font=head, fill=ma.OFFWHITE); y += 74 * S

# one line of what it is, and the address
y += 18 * S
d.text((x, y), "Your drive as a map. Free and open source, for Windows 10 and 11.", font=ma.font(500, 20 * S), fill=ma.MUTED)
y += 50 * S
d.text((x, y), "clearanceclarence.github.io/SpaceSharp", font=ma.font(500, 15 * S), fill=ma.HINT)

out = im.resize((1280, 640), Image.LANCZOS).convert('RGB')
out.save(HERE.parent / 'docs' / 'social-preview.png', optimize=True)
print("  docs/social-preview.png")
