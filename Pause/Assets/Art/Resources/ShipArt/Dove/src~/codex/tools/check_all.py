#!/usr/bin/env python3
"""Verifies + previews stock and every skin: contract/damage numbers (verify_sheet.py), a stacked dark overview of all
27 cells per sheet (all.png), 2x bank crops, and bank-vs-idle dominant-colour leak check."""
import os
import subprocess
import sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
DOVE = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
OUT = sys.argv[1]
names = [("stock", "Dove.png")] + [(n, f"Dove_{n}.bytes") for n in ("Night", "Sodium", "Bone", "Courier")]
stock = os.path.join(DOVE, "Dove.png")
overview = Image.new("RGB", (2304, 768 * 5))
banks = Image.new("RGB", (1024, 1536 * 5))
for i, (n, f) in enumerate(names):
    p = os.path.join(DOVE, f)
    r = subprocess.run([sys.executable, os.path.join(HERE, "verify_sheet.py"), p, stock], capture_output=True, text=True).stdout
    print("==", n)
    print("\n".join(l for l in r.splitlines() if not l.startswith("col") or "row0" in l and l.split(":")[0] in ("col 0", "col 6", "col 7")))
    im = Image.open(p).convert("RGBA")
    bg = Image.new("RGBA", im.size, (14, 20, 36, 255))
    bg.alpha_composite(im)
    overview.paste(bg.convert("RGB"), (0, 768 * i))
    banks.paste(bg.crop((1536, 0, 2048, 768)).resize((1024, 1536), Image.NEAREST).convert("RGB"), (0, 1536 * i)) if False else None
    bg.crop((1536, 0, 2048, 768)).resize((1024, 1536), Image.NEAREST).convert("RGB").save(os.path.join(OUT, f"banks2x_{n}.png"))
    a = np.array(im)
    for r_ in range(3):
        idle = a[r_ * 256:(r_ + 1) * 256, :256]
        m = idle[..., 3] > 0
        mi = idle[m][:, :3].mean(0)
        for c in (6, 7):
            cell = a[r_ * 256:(r_ + 1) * 256, c * 256:(c + 1) * 256]
            mc = cell[cell[..., 3] > 0][:, :3].mean(0)
            print(f"   row {r_} col {c}: mean colour idle {mi.astype(int)} bank {mc.astype(int)} delta {np.abs(mi - mc).max():.1f}")
overview.save(os.path.join(OUT, "all.png"))
