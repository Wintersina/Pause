#!/usr/bin/env python3
"""Part 1 of the wreck-row fix: bring back the milder wreck (commit 5d8bd9852) for every cell except the
two bank cells, which keep the clean banks of 2e8b93148 (current files). Pure compositing of existing pixels.

  key_state2.png            <- old, whole file (the bank cells come from hull_redo_state2)
  hull_redo_state2.png      <- old cells 0-2 (lights off / glints), current cells 3-4 (banks) and 5
  skin_<N>_state2.png       <- old cells 0-5 and 8 (key, bobs, glints, flash), current cells 6-7 (banks)
Then run  python3 src~/build_restyle.py IonLancer  to rebuild the sheet and skins.
"""
import io
import os
import subprocess
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
C = os.path.dirname(HERE)
OLD = sys.argv[1] if len(sys.argv) > 1 else "5d8bd9852"
REL = "Pause/Assets/Art/Resources/ShipArt/IonLancer/src~/codex/"
ROOT = subprocess.check_output(["git", "rev-parse", "--show-toplevel"], cwd=C, text=True).strip()


def old(name):
    data = subprocess.check_output(["git", "show", "%s:%s%s" % (OLD, REL, name)], cwd=ROOT)
    return Image.open(io.BytesIO(data)).convert("RGB")


def cur(name):
    return Image.open(os.path.join(C, name)).convert("RGB")


def composite(name, cols, old_cells, size=1024):
    o, c = old(name), cur(name)
    out = c.copy()
    for cell in old_cells:
        r, k = divmod(cell, cols)
        box = (k * size, r * size, (k + 1) * size, (r + 1) * size)
        out.paste(o.crop(box), box[:2])
    out.save(os.path.join(C, name))
    print("composited", name, "old cells", old_cells)


old("key_state2.png").save(os.path.join(C, "key_state2.png"))
print("restored key_state2.png")
composite("hull_redo_state2.png", 3, [0, 1, 2])
for n in ("Night", "Sodium", "Bone", "Lance"):
    composite("skin_%s_state2.png" % n, 3, [0, 1, 2, 3, 4, 5, 8])
