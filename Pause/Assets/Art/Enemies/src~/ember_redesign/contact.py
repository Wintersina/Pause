#!/usr/bin/env python3
"""Contact sheet of src/*.png (or strips/*.png frame 0 with --strips) on the Ember dark tile colour, scaled to a given factor.
    python3 contact.py out.png [scale=0.3] [--strips]"""
import sys
from pathlib import Path
from PIL import Image
HERE = Path(__file__).resolve().parent
out = sys.argv[1]; args = [a for a in sys.argv[2:] if not a.startswith("--")]
scale = float(args[0]) if args else .3
strips = "--strips" in sys.argv
fs = sorted((HERE / ("strips" if strips else "src")).glob("*.png"))
ims = []
for f in fs:
    im = Image.open(f).convert("RGBA")
    if strips:
        im = im.crop((0, 0, im.height, im.height))
    ims.append(im.resize((max(1, int(im.width * scale)), max(1, int(im.height * scale))), Image.NEAREST if strips else Image.LANCZOS))
cw = max(i.width for i in ims); ch = max(i.height for i in ims); cols = 5
rows = (len(ims) + cols - 1) // cols
sheet = Image.new("RGBA", (cw * cols, ch * rows), (58, 22, 14, 255))
for k, im in enumerate(ims):
    sheet.alpha_composite(im, (cw * (k % cols) + (cw - im.width) // 2, ch * (k // cols) + (ch - im.height) // 2))
sheet.save(out); print([f.stem for f in fs])
