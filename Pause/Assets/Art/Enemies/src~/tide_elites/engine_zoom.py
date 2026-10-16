#!/usr/bin/env python3
"""Engine zoom sheet: the engine area of the hover cell, 4x, one column per ship, one row per strip dir.
  python3 engine_zoom.py <out.png> <dir> [<dir> ...]"""
import sys, json
from pathlib import Path
from PIL import Image
out, dirs = sys.argv[1], [Path(d) for d in sys.argv[2:]]
R = Path(__file__).resolve().parents[3]
NAMES = "riptide bathyscaphe trawler thunderfin pearl_warden".split()
BG = (16, 52, 84, 255)
W, H, Z = 150, 110, 4
sheet = Image.new("RGBA", (W * Z * len(NAMES), H * Z * len(dirs)), BG)
for j, d in enumerate(dirs):
    for i, n in enumerate(NAMES):
        df = json.load(open(R / f"Resources/Elites/Defs/tide_elite_{n}.json"))
        xs = [p["x"] for p in df["nozzles"]]; ys = [p["y"] for p in df["nozzles"]]
        cx, cy = sum(xs) / len(xs), sum(ys) / len(ys)
        x0 = int(min(max(cx - W / 2, 0), 384 - W)); y0 = int(min(max(cy - 40, 0), 384 - H))
        cell = Image.open(d / f"tide_elite_{n}.png").convert("RGBA").crop((3 * 384, 0, 4 * 384, 384))
        bg = Image.new("RGBA", cell.size, BG); bg.alpha_composite(cell)
        crop = bg.crop((x0, y0, x0 + W, y0 + H)).resize((W * Z, H * Z), Image.NEAREST)
        sheet.alpha_composite(crop, (i * W * Z, j * H * Z))
sheet.convert("RGB").save(out)
