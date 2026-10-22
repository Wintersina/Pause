#!/usr/bin/env python3
"""Install hull_redo_state{1,2}_v2.png as hull_redo_state{1,2}.png. Codex left a flat green halo
behind the canopy in cells 0-2 (inside the ship silhouette): paint those pixels from the key pose
(same position), then report any green left inside the key's silhouette."""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
C = os.path.dirname(HERE)


def is_green(a):
    v = a.astype(np.int16)
    r, g, b = v[..., 0], v[..., 1], v[..., 2]
    return (g > 100) & (g > r * 1.55) & (g > b * 1.4) & (g - r > 65) & (g - b > 65)


for s in (1, 2):
    sheet = np.array(Image.open(os.path.join(C, "hull_redo_state%d_v2.png" % s)).convert("RGB"))
    key = np.array(Image.open(os.path.join(C, "key_state%d.png" % s)).convert("RGB"))
    keymask = ~is_green(key)
    for cell in range(5):
        r, c = divmod(cell, 3)
        sub = sheet[r * 1024:(r + 1) * 1024, c * 1024:(c + 1) * 1024]
        if cell <= 2:
            bad = is_green(sub) & keymask
            # dilate the key mask by 0: copy the key pixels straight in
            sub[bad] = key[bad]
            print("state", s, "cell", cell, "repaired green px:", int(bad.sum()))
    # Codex also left a darker green blob (0,211,0) in the BACKGROUND above the canopy in cells 0-2:
    # snap every green-keyed pixel to the exact key colour.
    sheet[is_green(sheet)] = (0, 255, 0)
    Image.fromarray(sheet).save(os.path.join(C, "hull_redo_state%d.png" % s))
