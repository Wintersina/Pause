#!/usr/bin/env python3
"""skin_preview.py SRC_DIR QUANT_DIR OUT_DIR name1 name2 ...  -> <name>_zoom.png, _zoom_bright.png
Panels: original | quantized | abs diff x6, 3x nearest zoom of the busiest 256x256 crop (dark / bright bg)."""
import sys, os
import numpy as np
from PIL import Image
src, q, out = sys.argv[1:4]
os.makedirs(out, exist_ok=True)
for name in sys.argv[4:]:
    a = Image.open(os.path.join(src, name + ".bytes")).convert("RGBA")
    b = Image.open(os.path.join(q, name + ".bytes")).convert("RGBA")
    A = np.array(a); B = np.array(b)
    # busiest crop: most unique colours in a 256x256 window (grid search)
    best, pos = -1, (0, 0)
    for y in range(0, A.shape[0] - 255, 64):
        for x in range(0, A.shape[1] - 255, 64):
            c = A[y:y+256, x:x+256]
            s = len(np.unique(c.reshape(-1, 4), axis=0)) if (c[..., 3] > 0).mean() > 0.1 else 0
            if s > best: best, pos = s, (x, y)
    x, y = pos
    box = (x, y, x + 256, y + 256)
    for tag, bg in (("zoom", (16, 16, 24)), ("zoom_bright", (235, 235, 240))):
        panels = []
        for im in (a, b):
            base = Image.new("RGBA", (256, 256), bg + (255,))
            base.alpha_composite(im.crop(box)); panels.append(base)
        d = np.abs(np.array(panels[0]).astype(int) - np.array(panels[1]).astype(int))[..., :3]
        diff = Image.fromarray(np.clip(d * 6, 0, 255).astype(np.uint8)).convert("RGBA")
        panels.append(diff)
        W = Image.new("RGBA", (256 * 3 * 3 + 20, 256 * 3), (60, 60, 60, 255))
        for i, p in enumerate(panels):
            W.paste(p.resize((768, 768), Image.NEAREST), (i * 778, 0))
        W.convert("RGB").save(os.path.join(out, f"{name}_{tag}.png"))
    print(name, box)
