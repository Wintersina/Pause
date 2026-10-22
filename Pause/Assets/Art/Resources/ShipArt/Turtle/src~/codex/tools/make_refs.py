"""Builds the OLD-Turtle reference images for the Codex briefs (run BEFORE the hull / flame sheet is overwritten).
    python3 make_refs.py <Turtle.png (old)> <Turtle_flame.png (old)> <out dir = Turtle/src~/codex>
Strips of the old flame sheet: Plume y=194 / PlumeBoost y=0 (from the bottom), x=0, 7 frames stride 50, 48x192."""
import sys, os
from PIL import Image

old = Image.open(sys.argv[1]).convert("RGBA")
flame = Image.open(sys.argv[2]).convert("RGBA")
out = sys.argv[3]
for st in range(3):
    cell = old.crop((0, st * 256, 256, (st + 1) * 256)).resize((1024, 1024), Image.NEAREST)
    bg = Image.new("RGBA", (1024, 1024), (0, 255, 0, 255))
    bg.alpha_composite(cell)
    bg.convert("RGB").save(os.path.join(out, f"ref_old_state{st}.png"))
H = flame.height
for name, y in (("ref_old_flame.png", 194), ("ref_old_flame_boost.png", 0)):
    crop = flame.crop((0, H - y - 192, 7 * 50, H - y)).resize((7 * 50 * 3, 192 * 3), Image.NEAREST)
    bg = Image.new("RGBA", crop.size, (0, 255, 0, 255))
    bg.alpha_composite(crop)
    bg.convert("RGB").save(os.path.join(out, name))
print("ok")
