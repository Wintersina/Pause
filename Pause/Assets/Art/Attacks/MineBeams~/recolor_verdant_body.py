"""Verdant's dormant/armed BODY (the Verdant row, y 594..891, of rail_mines_neon.png) still had hot-pink head / thorn tips (user, 2026-10-13).
Every pink-family pixel inside that row goes to the Verdant cue green-white (recolor_nopink.remap, same mask as MineCue.IsPink); every pixel
outside the row (Space, Frost, Ember) is copied untouched, so the atlas keeps its size, rects and pivots.
usage: python3 recolor_verdant_body.py <pink original rail_mines_neon.png> <dest.png>
The pink original is archived as ONLY the Verdant row (verdant_body/pink_original/rail_mines_neon_verdant_row.png, 1254 x 298; the full
atlas is 2.1 MiB, over jj's snapshot cap). If <src> is that 298-high row, it is remapped and spliced into the existing <dest.png> at y 594.
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).parent))
from recolor_nopink import remap
ROW_Y0, ROW_Y1 = 594, 892
a = np.array(Image.open(sys.argv[1]).convert("RGBA"))
if a.shape[0] == ROW_Y1 - ROW_Y0:
    out = np.array(Image.open(sys.argv[2]).convert("RGBA")); src = a
else:
    out = a.copy(); src = a[ROW_Y0:ROW_Y1]
row, n = remap(src, "verdant")
out[ROW_Y0:ROW_Y1] = row
Image.fromarray(out, "RGBA").save(sys.argv[2], optimize=True)
print("recoloured px:", n)
