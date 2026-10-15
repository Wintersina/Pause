#!/usr/bin/env python3
"""Slice Codex's seven-pose strips (Snap Sprout, Glacier Golem) into the live
seven-cell enemy strips: 0-3 idle, 4-5 tell, 6 hit.  Nearest-neighbour only,
alpha binarised to drop the generated glow fringe.  Poses keep their position
relative to their slot, so the body does not drift between cells."""
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
RES = HERE.parents[2] / "Resources" / "Enemies"
SPEC = {  # raw file, live key, cell, max extent in cell
    "snap_sprout_strip_raw.png": ("verdant_alien", 192, 172),
    "glacier_golem_strip_raw.png": ("frost_big", 256, 236),
}


def build(raw, key, cell, fit):
    im = Image.open(HERE / raw).convert("RGBA")
    a = np.array(im)
    a[:, :, 3] = np.where(a[:, :, 3] >= 140, 255, 0)
    a[a[:, :, 3] == 0, :3] = 0
    im = Image.fromarray(a, "RGBA")
    slot = im.width / 7
    occ = (a[:, :, 3] > 0).sum(axis=0)
    cuts = [0]
    for i in range(1, 7):
        n = round(i * slot)
        lo, hi = n - 45, n + 45
        cuts.append(lo + int(np.argmin(occ[lo:hi])))
    cuts.append(im.width)
    print("cuts", cuts, [int(occ[c]) for c in cuts[1:-1]])
    slots = [im.crop((cuts[i], 0, cuts[i + 1], im.height)) for i in range(7)]
    boxes = [s.getbbox() for s in slots]
    # union box in slot coordinates
    ux0 = min(b[0] for b in boxes); ux1 = max(b[2] for b in boxes)
    uw = max(b[2] - b[0] for b in boxes)
    uy0 = min(b[1] for b in boxes); uy1 = max(b[3] for b in boxes)
    f = fit / uw if key == "frost_big" else min(fit / uw, fit / (uy1 - uy0))
    out = Image.new("RGBA", (cell * 7, cell))
    f0 = 0.62 if key == "frost_big" else f
    cy = (uy0 + uy1) / 2
    if key == "frost_big":
        cy = float(np.median([(b[1] + b[3]) / 2 for b in boxes]))
    for i, s in enumerate(slots):
        bh = boxes[i][3] - boxes[i][1]
        f = min(f0, (cell - 8) / bh, (cell - 8) / (boxes[i][2] - boxes[i][0]))
        w, h = max(1, round(s.width * f)), max(1, round(s.height * f))
        r = s.resize((w, h), Image.Resampling.NEAREST)
        bx = (boxes[i][0] + boxes[i][2]) / 2
        ox = round(cell / 2 - bx * f) + i * cell
        oy = round(cell / 2 - ((boxes[i][1] + boxes[i][3]) / 2 if bh * f0 > cell - 8 else cy) * f)
        out.alpha_composite(r, (ox, oy))
    # clip any spill into neighbours
    out.save(HERE / f"{key}_strip.png", optimize=True)
    print(key, "scale", round(f, 3), "union", ux1 - ux0, uy1 - uy0)


if __name__ == "__main__":
    for raw, (key, cell, fit) in SPEC.items():
        build(raw, key, cell, fit)
