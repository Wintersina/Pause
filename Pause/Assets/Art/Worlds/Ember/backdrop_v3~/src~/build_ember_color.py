"""Rebuild the VISIBLE Ember ground (mid tile, drawn on top of the opaque sky/far) from the Codex colour redraws.

Sources: ember_v1/v{N}_col_a.png (colourful redraw: calm, mid-dark central band, vivid lava and colour in the side
fifths and around the lane). Output: ../../../Backgrounds/Resources/Worlds/Ember/Backdrop3/v{N}/mid.png, 512x1024
RGBA, nearest resize, two-axis wrap blend (same as build.py). sky/far/flow are not touched.
Unlike build_ember_v1.py the grade keeps Codex's contrast and hue (no desaturation, highlights not clipped to .45):
only the p90 value is pinned so the as-drawn tile caps hold. Pink guard as before.
Run: python3 build_ember_color.py 1 2 3 4
"""
from __future__ import annotations
import sys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent))
from build import W, H, wrap_blend
from build_ember_v1 import pink_guard, stats

HERE = Path(__file__).resolve().parent
SRC = HERE / "ember_v1"
RES = HERE.parents[3] / "Backgrounds/Resources/Worlds/Ember/Backdrop3"
# per variant: p90 value target of the art, highlight cap, saturation
CFG = {1: dict(p90=.36, cap=.95, sat=1.05), 2: dict(p90=.40, cap=.95, sat=1.05),
       3: dict(p90=.40, cap=.95, sat=1.05), 4: dict(p90=.32, cap=.95, sat=1.05)}
COLORS = 72


def src(name):
    im = Image.open(SRC / name).convert("RGB")
    wd = min(im.width, im.height // 2)
    x = (im.width - wd) // 2; y = (im.height - 2 * wd) // 2
    return np.asarray(im.crop((x, y, x + wd, y + 2 * wd)).resize((W, H), Image.Resampling.NEAREST)).copy()


def grade(rgb, p90_target, cap, sat):
    f = rgb.astype(np.float32) / 255
    g = f.mean(axis=2, keepdims=True); f = np.clip(g + (f - g) * sat, 0, 1)
    v = f.max(axis=2)
    scale = p90_target / max(float(np.percentile(v, 90)), 1e-3)
    nv = np.clip(v * scale, .04, cap)
    f = f * (nv / np.maximum(v, 1e-3))[..., None]
    idx = Image.fromarray(np.uint8(np.rint(np.clip(f, 0, 1) * 255)), "RGB").quantize(
        colors=COLORS, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
    return pink_guard(np.asarray(idx).copy())


def build(v):
    c = CFG[v]
    rgb = grade(src(f"v{v}_col_a.png"), c["p90"], c["cap"], c["sat"])
    rgba = wrap_blend(np.dstack((rgb, np.full((H, W), 255, np.uint8))), 78)
    rgba[:, :, :3] = grade(rgba[:, :, :3], c["p90"], c["cap"], 1.0)
    rgba[:, -1] = rgba[:, 0]; rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


if __name__ == "__main__":
    for v in [int(x) for x in sys.argv[1:]]:
        im = build(v)
        old = Image.open(RES / f"v{v}" / "mid.png"); assert im.size == old.size == (W, H)
        im.save(RES / f"v{v}" / "mid.png", optimize=True)
        print(v, "mid", im.size, stats(im))
