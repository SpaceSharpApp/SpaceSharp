"""Writes SpaceSharp/Themes/Icons.xaml: every icon the app draws, as WPF path geometry on a 24 x 24 grid.

The icons are Lucide (https://lucide.dev, ISC license), pinned to one release so a rebuild gives the same picture.
Two that Lucide does not have (an SSD, a USB stick) are drawn here in the same style: 24-unit grid, 2-unit round
strokes, nothing finer than survives 14 px. The app draws them through Controls/Icon.cs, which scales the whole
thing, strokes included, to the size it needs.

    python tools/make-icons.py                      # fetches the SVGs from GitHub into tools/.icons/
    python tools/make-icons.py --from node_modules/lucide-static/icons

To add an icon: put a line in ICONS (app name, Lucide name) and run this; then use {StaticResource Icon.Name}
in XAML or Icons.Get("Name") in code."""
import re, sys, pathlib, urllib.request

LUCIDE_TAG = "0.468.0"
RAW = f"https://raw.githubusercontent.com/lucide-icons/lucide/{LUCIDE_TAG}/icons/"
HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent
CACHE = HERE / ".icons"
OUT = ROOT / "SpaceSharp" / "Themes" / "Icons.xaml"

# app name -> Lucide icon name
ICONS = {
    # toolbar and path row
    "ScanFolder": "folder-open", "Refresh": "refresh-cw", "Up": "arrow-up", "Home": "house",
    "SidePanel": "panel-left", "ZoomOut": "zoom-out", "ZoomIn": "zoom-in",
    "Settings": "settings", "Moon": "moon", "Sun": "sun", "Monitor": "monitor", "Info": "info",
    "Filter": "filter", "Close": "x", "Shield": "shield",
    # the scan card's stages: waiting, running, done
    "Circle": "circle", "Loader": "loader-circle",
    # the palette editor
    "Palette": "palette", "Shuffle": "shuffle",
    "ChevronDown": "chevron-down", "ChevronUp": "chevron-up", "ChevronRight": "chevron-right",
    # things in lists
    "Folder": "folder", "File": "file-text", "Selection": "square-check-big",
    # menu
    "Open": "file-output", "Properties": "file-sliders", "Copy": "copy", "Export": "file-output",
    "Delete": "trash-2", "Reset": "rotate-ccw", "Add": "plus",
    # inspect
    "Warning": "triangle-alert", "Share": "chart-pie", "Composition": "layers", "Age": "clock-3",
    # about, update
    "Sync": "refresh-ccw", "Notes": "file-text", "Keyboard": "keyboard", "Mouse": "mouse", "Bug": "bug", "Idea": "lightbulb",
    "Globe": "globe", "Book": "book-open", "External": "external-link", "ArrowRight": "arrow-right",
    # dialogs and control chrome
    "Question": "circle-help", "Error": "circle-x", "Search": "search", "Check": "check",
    # drives
    "Drive": "hard-drive", "Usb": "usb", "Network": "network", "Disc": "disc",
}

# drawn here, same grid and stroke as Lucide, and kept simple enough to read at 14 px: a bolt in a square for a
# solid-state drive (SATA and NVMe alike), a stick with a plug for USB. Spinning disks use Lucide's hard-drive.
CUSTOM = {
    "Ssd": '<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M13 8l-3.5 4.5H14L10.5 17"/>',
    "Treemap": '<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 12h11"/><path d="M14 3v18"/><path d="M14 15h7"/>',
    "UsbStick": '<rect x="2" y="7" width="13" height="10" rx="2"/><path d="M15 10h5v4h-5"/><path d="M17 12h1"/>',
}


def fetch(name: str, local: pathlib.Path | None) -> str:
    if local is not None:
        return (local / f"{name}.svg").read_text(encoding="utf-8")
    CACHE.mkdir(exist_ok=True)
    f = CACHE / f"{name}.svg"
    if not f.exists():
        with urllib.request.urlopen(RAW + f"{name}.svg") as r:
            f.write_bytes(r.read())
    return f.read_text(encoding="utf-8")


def num(s: str) -> str:
    v = float(s)
    return f"{v:g}"


def to_path_data(svg: str) -> str:
    """Turns every shape element in an SVG into one WPF path string (the mini-language is SVG's)."""
    parts = []
    for tag, attrs in re.findall(r"<(path|circle|rect|line|polyline|polygon|ellipse)\b([^>]*)/?>", svg):
        a = dict(re.findall(r'([\w-]+)="([^"]*)"', attrs))
        if tag == "path":
            # Each <path> starts from the origin, so a leading relative "m" is absolute there; joined into one
            # geometry it would continue from the previous shape's last point (Lucide's "x" draws as a slash).
            d = a["d"].strip()
            m = re.match(r"m\s*(-?[\d.]+)[\s,]+(-?[\d.]+)\s*,?\s*", d)
            if m:
                rest = d[m.end():]
                # coordinates that follow a moveto are implicit linetos, relative after "m": keep them so with an "l"
                d = f"M{m.group(1)} {m.group(2)} " + (("l" + rest) if rest and rest[0] in "-0123456789." else rest)
            parts.append(d)
        elif tag == "circle":
            cx, cy, r = (float(a[k]) for k in ("cx", "cy", "r"))
            parts.append(f"M{cx - r:g},{cy:g} A{r:g},{r:g} 0 1 0 {cx + r:g},{cy:g} A{r:g},{r:g} 0 1 0 {cx - r:g},{cy:g} Z")
        elif tag == "ellipse":
            cx, cy, rx, ry = (float(a[k]) for k in ("cx", "cy", "rx", "ry"))
            parts.append(f"M{cx - rx:g},{cy:g} A{rx:g},{ry:g} 0 1 0 {cx + rx:g},{cy:g} A{rx:g},{ry:g} 0 1 0 {cx - rx:g},{cy:g} Z")
        elif tag == "rect":
            x, y, w, h = (float(a.get(k, 0)) for k in ("x", "y", "width", "height"))
            rx = float(a.get("rx", a.get("ry", 0))); ry = float(a.get("ry", rx))
            if rx <= 0:
                parts.append(f"M{x:g},{y:g} h{w:g} v{h:g} h{-w:g} Z")
            else:
                parts.append(f"M{x + rx:g},{y:g} h{w - 2 * rx:g} a{rx:g},{ry:g} 0 0 1 {rx:g},{ry:g} v{h - 2 * ry:g} a{rx:g},{ry:g} 0 0 1 {-rx:g},{ry:g} "
                             f"h{-(w - 2 * rx):g} a{rx:g},{ry:g} 0 0 1 {-rx:g},{-ry:g} v{-(h - 2 * ry):g} a{rx:g},{ry:g} 0 0 1 {rx:g},{-ry:g} Z")
        elif tag == "line":
            parts.append(f"M{num(a['x1'])},{num(a['y1'])} L{num(a['x2'])},{num(a['y2'])}")
        elif tag in ("polyline", "polygon"):
            pts = re.findall(r"-?[\d.]+", a["points"])
            pairs = [f"{num(pts[i])},{num(pts[i + 1])}" for i in range(0, len(pts), 2)]
            parts.append("M" + " L".join(pairs) + (" Z" if tag == "polygon" else ""))
    return " ".join(parts)


def main():
    local = None
    if "--from" in sys.argv:
        local = pathlib.Path(sys.argv[sys.argv.index("--from") + 1])
    lines = [
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        "    <!--",
        "      Generated by tools/make-icons.py; do not edit by hand. Lucide icons (ISC license, https://lucide.dev),",
        f"      release {LUCIDE_TAG}, plus a few drawn in the same style. Each is a 24 x 24 path meant to be stroked",
        "      2 units wide with round caps and joins; Controls/Icon.cs does that at any size.",
        "    -->",
    ]
    for key, name in ICONS.items():
        lines.append(f'    <Geometry x:Key="Icon.{key}">{to_path_data(fetch(name, local))}</Geometry>')
    for key, body in CUSTOM.items():
        lines.append(f'    <Geometry x:Key="Icon.{key}">{to_path_data(body)}</Geometry>')
    lines.append("</ResourceDictionary>")
    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"  {OUT.relative_to(ROOT)}: {len(ICONS) + len(CUSTOM)} icons")


if __name__ == "__main__":
    main()
