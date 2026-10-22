"""Count (and with --fix, close) enclosed transparent pixels in every cell of the VoltViper hull sheet and skins,
using build_restyle.fill_enclosed (the hole fix master's pipeline applies to new sheets).
    python3 fill_holes.py <Resources/ShipArt dir> [--fix]"""
import importlib.util
import io
import os
import sys

import numpy as np
from PIL import Image

art = sys.argv[1]
fix = "--fix" in sys.argv
hulls = os.path.join(art, "Hulls")
spec = importlib.util.spec_from_file_location("br", os.path.join(hulls, "src~", "build_restyle.py"))
br = importlib.util.module_from_spec(spec)
spec.loader.exec_module(br)
CELL = 256
paths = [(os.path.join(hulls, "VoltViper.png"), False)] + [
    (os.path.join(art, "Skins", f"VoltViper_{n}.bytes"), True) for n in ("Night", "Sodium", "Bone", "Storm")]
for p, as_bytes in paths:
    im = Image.open(p)
    im.load()
    sheet = np.array(im.convert("RGBA"))
    total = 0
    for r in range(3):
        for c in range(9):
            cell = Image.fromarray(sheet[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL])
            filled, n = br.fill_enclosed(cell)
            total += n
            if fix and n:
                sheet[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL] = np.array(filled)
    print(os.path.basename(p), "enclosed transparent pixels:", total)
    if fix and total:
        out = Image.fromarray(sheet)
        if as_bytes:
            buf = io.BytesIO()
            out.save(buf, format="PNG", optimize=True)
            open(p, "wb").write(buf.getvalue())
        else:
            out.save(p, optimize=True)
