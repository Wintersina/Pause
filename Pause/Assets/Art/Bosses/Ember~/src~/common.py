"""Shared helpers for the Cinder Drake v2 build scripts (384 px cells, nearest-neighbour only)."""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

SRC = Path(__file__).resolve().parent
ROOT = SRC.parent
ART = ROOT.parent
C = 384
# The approved hull key's content box (1254 px canvas); every painted key is a same-canvas edit
# of it, so they all share one crop and one scale into the 384 cell.
KEY_BOX = (49, 122, 1205, 1151)
FIT_W = 344

# Palette (never the player's red; no pink)
ORANGE = (255, 150, 40)
AMBER = (255, 200, 70)
YELLOW = (255, 238, 140)
WHITE = (255, 250, 225)
SULFUR = (240, 240, 80)
EMBER = (214, 96, 24)
INK = (12, 12, 22)


def rgba() -> Image.Image:
    return Image.new("RGBA", (C, C), (0, 0, 0, 0))


def fit_box(box=KEY_BOX):
    w = box[2] - box[0]
    h = box[3] - box[1]
    s = FIT_W / w
    return s, (round(w * s), round(h * s))


def fix_hue(im: Image.Image) -> Image.Image:
    """Keep the Ember palette out of the player's red: any red / magenta texel is shifted to orange."""
    im = im.convert("RGBA")
    hsv = np.asarray(im.convert("RGB").convert("HSV")).copy()
    h, s = hsv[..., 0], hsv[..., 1]
    valid = s > 90
    h[valid & (h < 8)] = 14           # 0..11 deg -> 20 deg orange
    h[valid & (h >= 236)] = 14        # magenta / pink -> orange
    rgb = Image.fromarray(hsv, "HSV").convert("RGB")
    rgb.putalpha(im.getchannel("A"))
    return rgb


def to_cell(im: Image.Image, dx: int = 0, dy: int = 0, scale: float = 1.0) -> Image.Image:
    """A same-canvas edit of the hull key placed in the 384 cell (nearest-neighbour). The WHOLE canvas is
    scaled (never cropped to the hull box), so a flame jet or smoke that leaves the hull box survives."""
    im = fix_hue(im)
    norm = 1254.0 / im.width                     # keys come back 1254 or 1280 px square
    s = FIT_W / (KEY_BOX[2] - KEY_BOX[0]) * scale
    w = max(1, round(im.width * norm * s))
    big = im.resize((w, w), Image.Resampling.NEAREST)
    _, size = fit_box()
    x = (C - round(size[0] * scale)) // 2 + dx - round(KEY_BOX[0] * s)
    y = (C - round(size[1] * scale)) // 2 + dy - round(KEY_BOX[1] * s)
    out = rgba()
    out.alpha_composite(big, (x, y)) if x >= 0 and y >= 0 else out.paste(big.crop((max(0, -x), max(0, -y), w, w)), (max(0, x), max(0, y)))
    a = np.asarray(out).copy()
    a[a[..., 3] < 64] = 0           # imagegen alpha dust
    return Image.fromarray(a, "RGBA")


BAND = (100, 215, 100, 284)    # y0, y1, x0, x1: head and neck, clear of wings, flames and the chest glow


def register_core(ref: Image.Image, im: Image.Image, search: int = 8, scales=(0.97, 1.0, 1.03)):
    """(scale, dx, dy) that best overlays the head / neck alpha of `im` on `ref`. The wings and the fire
    jets move between poses, so only the core band is compared."""
    y0, y1, x0, x1 = BAND
    a = (np.asarray(ref)[y0:y1, x0:x1, 3] > 100).astype(np.float32)
    best = None
    for sc in scales:
        if sc == 1.0:
            scaled = im
        else:
            w = round(C * sc)
            sm = im.resize((w, w), Image.Resampling.NEAREST)
            scaled = rgba()
            off = (C - w) // 2
            scaled.paste(sm.crop((max(0, -off), max(0, -off), min(w, C - off), min(w, C - off))), (max(0, off), max(0, off)))
        al = (np.asarray(scaled)[..., 3] > 100).astype(np.float32)
        for dy in range(-search, search + 1):
            for dx in range(-search, search + 1):
                b = np.roll(np.roll(al, dy, axis=0), dx, axis=1)[y0:y1, x0:x1]
                err = float(np.abs(b - a).sum())
                if best is None or err < best[0]:
                    best = (err, sc, dx, dy)
    return best[1], best[2], best[3]


def apply_reg(im: Image.Image, sc: float, dx: int, dy: int) -> Image.Image:
    if sc != 1.0:
        w = round(C * sc)
        sm = im.resize((w, w), Image.Resampling.NEAREST)
        out = rgba()
        off = (C - w) // 2
        out.paste(sm.crop((max(0, -off), max(0, -off), min(w, C - off), min(w, C - off))), (max(0, off), max(0, off)))
        im = out
    return Image.fromarray(np.roll(np.roll(np.asarray(im), dy, axis=0), dx, axis=1), "RGBA")
