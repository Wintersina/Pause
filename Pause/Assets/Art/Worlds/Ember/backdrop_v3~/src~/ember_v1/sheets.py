"""Contact sheet (old | new) and zoom sheet. usage: sheets.py N OLDDIR OUTDIR"""
import sys
from pathlib import Path
from PIL import Image
v, old, out = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
new = Path(__file__).resolve().parents[5] / "Backgrounds/Resources/Worlds/Ember/Backdrop3" / f"v{v}"
def flat(p):
    im = Image.open(p).convert("RGBA"); bg = Image.new("RGBA", im.size, (60, 60, 60, 255)); bg.alpha_composite(im); return bg.convert("RGB")
roles = ["sky", "far", "mid"]
sheet = Image.new("RGB", (6 * 256 + 5 * 6 + 14, 512), (20, 20, 20))
x = 0
for grp, d in (("old", old), ("new", new)):
    for r in roles:
        sheet.paste(flat(d / f"{r}.png").resize((256, 512), Image.Resampling.NEAREST), (x, 0)); x += 262
    x += 14
sheet.save(out / f"ember_v{v}_contact.png")
# zoom: 3 crops (structure=far, ecology=far, play=mid), old over new, 2x
def crop(d, r, box): return flat(d / f"{r}.png").crop(box).resize((384, 384), Image.Resampling.NEAREST)
boxes = [("far", (20, 40, 212, 232)), ("far", (300, 600, 492, 792)), ("mid", (160, 400, 352, 592))]
z = Image.new("RGB", (3 * 390, 2 * 390), (20, 20, 20))
for i, (r, b) in enumerate(boxes):
    z.paste(crop(old, r, b), (i * 390, 0)); z.paste(crop(new, r, b), (i * 390, 390))
z.save(out / f"ember_v{v}_zoom.png")
