#!/usr/bin/env python3
"""Before/after sheets (old on top, new below) on the Storm sky colour. usage: make_sheets.py <outdir> [--old <dir with old_<name>.png>]
Old strips come from git (HEAD of the base commit) unless --old is given."""
import sys, subprocess, io
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
RES = HERE.parents[2] / "Resources/Elites/Storm"
NAMES = "skyhook_harrier refinery_tender rod_dancer anvil_cloud static_bastion".split()
BG = (22, 16, 52, 255)
BASE = sys.argv[2] if len(sys.argv) > 2 else "origin/master"
out = Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)


def old(n, death=False):
    p = f"Pause/Assets/Art/Resources/Elites/Storm/storm_elite_{n}{'_death' if death else ''}.png"
    data = subprocess.run(["git", "show", f"{BASE}:{p}"], capture_output=True, check=True, cwd=HERE.parents[5]).stdout
    return Image.open(io.BytesIO(data)).convert("RGBA")


def new(n, death=False):
    return Image.open(RES / f"storm_elite_{n}{'_death' if death else ''}.png").convert("RGBA")


def on_bg(im, scale=1):
    b = Image.new("RGBA", im.size, BG); b.alpha_composite(im)
    if scale != 1:
        b = b.resize((b.width * scale, b.height * scale), Image.Resampling.NEAREST)
    return b


def sheet(name, scale, cells, death=False):
    rows = []
    for n in NAMES:
        for im in (old(n, death), new(n, death)):
            rows.append(im.crop((cells[0] * 384, 0, (cells[-1] + 1) * 384, 384)))
    W = rows[0].width
    s = Image.new("RGBA", (W, 384 * len(rows)), BG)
    for i, r in enumerate(rows):
        s.alpha_composite(r, (0, i * 384))
    s = s.resize((s.width * scale // 2, s.height * scale // 2), Image.Resampling.NEAREST) if scale != 2 else s
    s.convert("RGB").save(out / name)


# 1x: the game draws elites ~1.5-2.5 world units; this is the strips at native cell size (2 cells wide per ship pair)
sheet("storm_elites_strips_1x.png", 2, [0, 1, 2, 3, 4, 5, 6])
# hover cell, old | new side by side per ship, 3x on a 1x cell crop
hov = Image.new("RGBA", (384 * 2 * 5, 384), BG)
for i, n in enumerate(NAMES):
    hov.alpha_composite(old(n).crop((3 * 384, 0, 4 * 384, 384)), (i * 768, 0))
    hov.alpha_composite(new(n).crop((3 * 384, 0, 4 * 384, 384)), (i * 768 + 384, 0))
bg = Image.new("RGBA", hov.size, BG); bg.alpha_composite(hov)
bg.convert("RGB").save(out / "storm_elites_hover_before_after_1x.png")
for n in NAMES:   # 3x: the hover cell's art area (40..344), old left, new right
    box = (1152 + 40 - 1152, 0, 344, 384)
    o = old(n).crop((3 * 384 + 40, 0, 3 * 384 + 344, 384)); w = new(n).crop((3 * 384 + 40, 0, 3 * 384 + 344, 384))
    r = Image.new("RGBA", (304 * 2 + 20, 384), BG); r.alpha_composite(o, (0, 0)); r.alpha_composite(w, (324, 0))
    r.resize((r.width * 3, r.height * 3), Image.Resampling.NEAREST).convert("RGB").save(out / f"storm_elite_{n}_before_after_hover_3x.png")
sheet("storm_elites_death_before_after.png", 2, [0, 1, 2], True)
print("ok")
