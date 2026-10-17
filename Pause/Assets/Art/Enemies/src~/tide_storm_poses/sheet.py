"""Before/after contact sheets: python sheet.py <world> [scale] [srcdir] -> before_after_<world>.png (rows: orig strip, new strip)."""
import sys
from pathlib import Path
from PIL import Image, ImageDraw
sys.path.insert(0, str(Path(__file__).parent))
from spec import SHIPS
H = Path(__file__).parent
world = sys.argv[1]
sc = int(sys.argv[2]) if len(sys.argv) > 2 else 1
src = H / (sys.argv[3] if len(sys.argv) > 3 else "build")
rows = []
for n, cell, *_ in SHIPS:
    if not n.startswith(world):
        continue
    for lab, p in (("before", H / "orig" / f"{n}.png"), ("after", src / f"{n}.png")):
        im = Image.open(p).convert("RGBA")
        im = im.resize((int(im.width * 192 / im.height) * sc, 192 * sc), Image.NEAREST)
        rows.append((f"{n} {lab}", im))
W = max(r[1].width for r in rows)
sh = Image.new("RGBA", (W + 4, sum(r[1].height + 2 for r in rows)), (28, 30, 40, 255))
y = 0
d = ImageDraw.Draw(sh)
for lab, im in rows:
    sh.alpha_composite(im, (0, y))
    d.text((3, y + 2), lab, fill=(255, 255, 255, 255))
    y += im.height + 2
out = H / f"before_after_{world}.png"
sh.convert("RGB").save(out)
print(out, sh.size)
