#!/usr/bin/env python3
"""Validates a Codex strip (work_<name>/B_<name>.png) and installs it as Resources/Weapons/Powers2/<name>.png
(new files get the import settings of Dove_attack.png.meta + a fresh guid)."""
import sys, uuid, shutil, pathlib
from PIL import Image
import numpy as np
here = pathlib.Path(__file__).resolve().parent
dest = here.parent.parent / "Resources" / "Weapons" / "Powers2"
for name in sys.argv[1:]:
    src = here / f"work_{name}" / f"B_{name}.png"
    im = Image.open(src).convert("RGBA")
    assert im.size == (4096, 256), im.size
    a = np.array(im)[:, :, 3]
    # hard alpha: snap any semi-transparent halo
    arr = np.array(im); arr[:, :, 3] = np.where(arr[:, :, 3] >= 128, 255, 0); 
    for i in range(16):
        c = arr[:, i * 256:(i + 1) * 256, 3]
        assert c.any(), f"{name} cell {i} empty"
    Image.fromarray(arr).save(dest / f"{name}.png")
    meta = dest / f"{name}.png.meta"
    if not meta.exists():
        t = (dest / "Dove_attack.png.meta").read_text()
        import re
        t = re.sub(r"guid: [0-9a-f]+", "guid: " + uuid.uuid4().hex, t, count=1)
        meta.write_text(t)
    print("installed", name)
