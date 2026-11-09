"""Writes docs/m/*.webp (ignored by git): the README screenshots at 1800 px wide, for the website's phone gallery, which shows a
gallery of pictures instead of the live preview. 1800 px covers a phone at 3x scaling without the grain 1200 px had.
Run after tools/screenshots.ps1 has refreshed docs/shot-*.png. Needs Pillow with WebP support (pip install pillow)."""
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
DOCS = HERE.parent / "docs"
OUT = DOCS / "m"
WIDTH = 1800

OUT.mkdir(exist_ok=True)
for src in sorted(DOCS.glob("*.png")):
    if not (src.name.startswith("shot-") or src.name == "screenshot.png"):
        continue
    im = Image.open(src).convert("RGB")
    im = im.resize((WIDTH, round(im.height * WIDTH / im.width)), Image.LANCZOS)
    dst = OUT / (src.stem + ".webp")
    im.save(dst, "WEBP", quality=88, method=6)
    print(f"  docs/m/{dst.name}  {dst.stat().st_size // 1024} KB")
