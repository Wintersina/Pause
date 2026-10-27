"""Rebuild Verdant v2 (swamp delta) sky/far/mid from the Codex repaint swamp_v2/swamp_a.png (flow.png is kept).
Buildings (stilt refineries, tanks, stacks, walkways) drawn clearly: warm stone / rust / brass with lit windows, darker calmer swamp.
Only `mid` is seen in game (opaque, on top); sky / far get the same picture, hazed deeper.
Run: /usr/local/bin/python3 build_swamp_v2.py"""
from pathlib import Path
import sys
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build as B
W, H = B.W, B.H
HERE = Path(__file__).resolve().parent
OUT = Path(__file__).resolve().parents[5] / "Art/Backgrounds/Resources/Worlds/Verdant/Backdrop3/v2"
TARGET = {"sky": .40, "far": .43, "mid": .43}   # drawn x .95 / .8 by VariantBrightness; old .415 / .445 / .475
SAT = {"sky": 1.0, "far": 1.1, "mid": 1.1}
CONTRAST = {"sky": .30, "far": .62, "mid": .80}
HAZE = {"sky": .26, "far": .06, "mid": 0.0}
FOG = (.12, .30, .28)


def ground(role):
    a = np.asarray(Image.open(HERE / "swamp_v2" / "swamp_a.png").convert("RGB").resize((W, H), Image.Resampling.NEAREST)).copy()
    f = a.astype(np.float32) / 255
    g = f.mean(axis=2, keepdims=True)
    f = g + (f - g) * SAT[role]
    f = f * (1 - HAZE[role]) + np.array(FOG, np.float32) * HAZE[role]
    rgb = np.uint8(np.clip(np.rint(f * 255), 0, 255))
    t, cap = TARGET[role], .66
    rgb = B.palette_and_value(rgb, t, CONTRAST[role], 0, cap, colors=42 if role == "sky" else 64)
    rgba = B.wrap_blend(np.dstack((rgb, np.full((H, W), 255, np.uint8))))
    rgb = B.palette_and_value(rgba[:, :, :3], t, 1.0, 0, cap, colors=64)
    rgba = np.dstack((rgb, np.full((H, W), 255, np.uint8)))
    rgba[:, -1] = rgba[:, 0]; rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


if __name__ == "__main__":
    for role in ("sky", "far", "mid"):
        im = ground(role)
        im.save(OUT / f"{role}.png", optimize=True)
        print(role, im.size, B.stats(im))
