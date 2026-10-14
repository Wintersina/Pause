"""Per-world review sheet of the HD rebuilds: each ship's before/after row, then its HD 7-cell flight strip and 3-cell death strip
at half size on a dark backdrop.   python3 make_contact_sheets.py [world ...]   ->  /tmp/elite_hd_contact_<world>.png"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw
ELITE = Path(__file__).resolve().parent.parent
for w in (sys.argv[1:] or ["tide", "storm", "desert", "frost", "verdant"]):
    d = ELITE / w.capitalize() / "hd~"
    keys = sorted(p.name[:-len("_before_after.png")] for p in d.glob("*_before_after.png"))
    rows = [Image.open(d / f"{k}_before_after.png").convert("RGB") for k in keys]
    W = max(r.width for r in rows); H = sum(r.height + 200 for r in rows)
    sheet = Image.new("RGB", (W, H), (22, 20, 44)); y = 0
    for k, r in zip(keys, rows):
        sheet.paste(r, (0, y)); y += r.height
        for strip in (Image.open(d / f"{k}.png"), Image.open(d / f"{k}_death.png")):
            half = strip.convert("RGBA").resize((strip.width // 2, strip.height // 2), Image.LANCZOS)
            bg = Image.new("RGBA", half.size, (14, 12, 34, 255)); bg.alpha_composite(half)
            sheet.paste(bg.convert("RGB"), (0 if strip.width > 2000 else 1344 + 20, y if strip.width > 2000 else y - 0)); 
        y += 200
    out = Path("/tmp") / f"elite_hd_contact_{w}.png"   # (not in the repo: 2 MB each); sheet.save(out, optimize=True); print(out)
