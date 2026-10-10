"""Writes docs/m/*.webp: the README screenshots at 1200 px wide, for the phone-sized landing page, which shows a
gallery of pictures instead of the live preview. Run after tools/screenshots.js has refreshed docs/shot-*.png.
Needs Pillow with WebP support (pip install pillow)."""
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
DOCS = HERE.parent / "docs"
OUT = DOCS / "m"
WIDTH = 1200

OUT.mkdir(exist_ok=True)
for src in sorted(DOCS.glob("*.png")):
    if not (src.name.startswith("shot-") or src.name == "screenshot.png"):
        continue
    im = Image.open(src).convert("RGB")
    im = im.resize((WIDTH, round(im.height * WIDTH / im.width)), Image.LANCZOS)
    dst = OUT / (src.stem + ".webp")
    im.save(dst, "WEBP", quality=82, method=6)
    print(f"  docs/m/{dst.name}  {dst.stat().st_size // 1024} KB")
