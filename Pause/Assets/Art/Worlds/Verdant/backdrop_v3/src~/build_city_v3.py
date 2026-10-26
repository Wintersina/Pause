"""Rebuild Verdant v3 (ruined city) sky/far/mid from the Codex repaints in city_v3/ (flow.png is kept).
Greener vegetation, clearer roads and buildings: little haze (sky only), higher mid/far contrast, saturation kept.
Run: /usr/local/bin/python3 build_city_v3.py"""
from pathlib import Path
import sys
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build as B
W, H = B.W, B.H
HERE = Path(__file__).resolve().parent
OUT = Path(__file__).resolve().parents[5] / "Art/Backgrounds/Resources/Worlds/Verdant/Backdrop3/v3"
TARGET = {"sky": .40, "far": .45, "mid": .44}   # old: .415 / .445 / .475 (drawn x .8 by VariantBrightness)
SAT = {"sky": 1.0, "far": 1.3, "mid": 1.25}
CONTRAST = {"sky": .30, "far": .62, "mid": .80}
HAZE = {"sky": .26, "far": .06, "mid": 0.0}
FOG = (.12, .30, .28)


def src(n):
    return np.asarray(Image.open(HERE / "city_v3" / n).convert("RGB").resize((W, H), Image.Resampling.NEAREST)).copy()


def ground(role):
    a, b = src("city_a2.png"), src("city_b2.png")
    w = {"sky": .32, "far": .72, "mid": .16}[role]
    f = (a.astype(np.float32) * (1 - w) + b.astype(np.float32) * w) / 255
    g = f.mean(axis=2, keepdims=True)
    f = g + (f - g) * SAT[role]
    f = f * (1 - HAZE[role]) + np.array(FOG, np.float32) * HAZE[role]
    rgb = np.uint8(np.clip(np.rint(f * 255), 0, 255))
    t, cap = TARGET[role], .66
    rgb = B.palette_and_value(rgb, t, CONTRAST[role], 0, cap, colors=42 if role == "sky" else 56)
    rgba = B.wrap_blend(np.dstack((rgb, np.full((H, W), 255, np.uint8))))
    rgb = B.palette_and_value(rgba[:, :, :3], t, 1.0, 0, cap, colors=56)
    rgba = np.dstack((rgb, np.full((H, W), 255, np.uint8)))
    rgba[:, -1] = rgba[:, 0]; rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


if __name__ == "__main__":
    for role in ("sky", "far", "mid"):
        im = ground(role)
        im.save(OUT / f"{role}.png", optimize=True)
        print(role, im.size, B.stats(im))
