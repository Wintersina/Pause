#!/usr/bin/env python3
"""Image inputs for the Codex re-render of the six Ember elites: the CURRENT 192 px flight cell (cell 3 of the legacy strip, taken from git
history so the script stays repeatable after the strips are replaced) enlarged 4x with nearest-neighbour onto a flat #00FF40 key, 1024 px.
  python3 make_inputs.py            writes inputs/<name>_in.png and prints each cell-3 bbox (the registration target, 192 px cell px)
"""
import subprocess, io, json
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[5]
NAMES = "sunstoke coalrunner brass_vulture kilnback ash_wraith cauterizer".split()
LEGACY = "a814cee2"        # master before this branch: the 192 px strips


def legacy(name):
    data = subprocess.check_output(["git", "show", f"{LEGACY}:Pause/Assets/Art/Resources/Elites/Ember/ember_elite_{name}.png"], cwd=REPO)
    return Image.open(io.BytesIO(data)).convert("RGBA")


if __name__ == "__main__":
    info = {}
    (HERE / "inputs").mkdir(exist_ok=True)
    for n in NAMES:
        strip = legacy(n)
        c3 = strip.crop((576, 0, 768, 192))
        c3.save(HERE / "inputs" / f"{n}_cell3_192.png")
        bb = c3.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox()
        info[n] = bb
        big = c3.resize((768, 768), Image.Resampling.NEAREST)
        key = Image.new("RGBA", (1024, 1024), (0, 255, 64, 255))
        key.alpha_composite(big, (128, 128))
        key.convert("RGB").save(HERE / "inputs" / f"{n}_in.png")
        print(n, bb)
    json.dump(info, open(HERE / "inputs" / "legacy_bbox.json", "w"))
