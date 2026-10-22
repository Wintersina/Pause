"""view_cols.py <sheet.png> <out.png> <cols comma list> [rows comma list]: chosen cells 2x over #0e1424."""
import sys
from PIL import Image

sh = Image.open(sys.argv[1]).convert("RGBA")
cols = [int(c) for c in sys.argv[3].split(",")]
rows = [int(c) for c in sys.argv[4].split(",")] if len(sys.argv) > 4 else [0, 1, 2]
out = Image.new("RGBA", (len(cols) * 384, len(rows) * 384), (14, 20, 36, 255))
for j, r in enumerate(rows):
    for i, c in enumerate(cols):
        cell = sh.crop((c * 256, r * 256, c * 256 + 256, r * 256 + 256)).resize((384, 384), Image.NEAREST)
        out.alpha_composite(cell, (i * 384, j * 384))
out.convert("RGB").save(sys.argv[2])
