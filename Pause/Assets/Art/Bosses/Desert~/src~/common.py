"""Shared helpers for the Dune-Eater v2 build scripts.

Everything is authored in a 384 px REFERENCE space (the Kraken's cell: anchors, glow radii, line widths) and
rendered into cells of C = REF * K pixels, K = 1.5 (576 px). The painted keys are 1254-1280 px, so the cell
holds ~516 px of drawing instead of 344: the same art, 2.25x the pixels, same on-screen size (a cell is one
world unit whatever its pixel size). 576 keeps the 5 x 7 atlas at 2880 x 4032, under the 4096 import cap.
"""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

SRC = Path(__file__).resolve().parent
ROOT = SRC.parent
ART = ROOT.parent
REF = 384
K = 1.5
C = int(REF * K)
# The approved hull key's content box (1254 px canvas, the Dune-Eater master); every painted key is a same-canvas edit
# of it, so they all share one crop and one scale into the cell.
KEY_BOX = (18, 48, 1251, 1184)
FIT_W = 350 * K

# Palette (never the player's red; no pink)
TEAL = (110, 235, 225)
ORANGE = (255, 150, 40)
AMBER = (255, 200, 70)
YELLOW = (255, 238, 140)
WHITE = (255, 250, 225)
SULFUR = (240, 240, 80)
EMBER = (214, 96, 24)
INK = (12, 12, 22)


def rgba() -> Image.Image:
    return Image.new("RGBA", (C, C), (0, 0, 0, 0))


class SD:
    """ImageDraw that takes 384-space coordinates and widths and draws them at C px."""

    def __init__(self, im: Image.Image):
        self.d = ImageDraw.Draw(im, "RGBA")

    @staticmethod
    def _pts(xy):
        xy = list(xy)
        if xy and not isinstance(xy[0], (tuple, list)):
            xy = list(zip(xy[0::2], xy[1::2]))
        return [(x * K, y * K) for x, y in xy]

    def line(self, xy, fill=None, width=1, joint=None):
        self.d.line(self._pts(xy), fill=fill, width=max(1, round(width * K)), joint=joint)

    def ellipse(self, box, fill=None, outline=None, width=1):
        (x0, y0), (x1, y1) = self._pts(box)
        self.d.ellipse((x0, y0, x1, y1), fill=fill, outline=outline, width=max(1, round(width * K)))

    def polygon(self, pts, fill=None, outline=None):
        self.d.polygon(self._pts(pts), fill=fill, outline=outline)

    def rectangle(self, box, fill=None):
        (x0, y0), (x1, y1) = self._pts(box)
        self.d.rectangle((x0, y0, x1, y1), fill=fill)


def fit_box(box=KEY_BOX):
    w = box[2] - box[0]
    h = box[3] - box[1]
    s = FIT_W / w
    return s, (round(w * s), round(h * s))


def fix_hue(im: Image.Image) -> Image.Image:
    """Keep the Desert palette out of the player's red: any red / magenta texel is shifted to orange."""
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
    """A same-canvas edit of the hull key placed in the cell. The WHOLE canvas is scaled (never cropped to
    the hull box), so a flame jet or smoke that leaves the hull box survives. Downscaled with LANCZOS (the
    keys are painted, not pixel art: nearest-neighbour at 2.4x aliased the fine rivet / spar detail)."""
    im = fix_hue(im)
    norm = 1254.0 / im.width                     # keys come back 1254 or 1280 px square
    s = FIT_W / (KEY_BOX[2] - KEY_BOX[0]) * scale
    w = max(1, round(im.width * norm * s))
    big = im.resize((w, w), Image.Resampling.LANCZOS)
    _, size = fit_box()
    x = (C - round(size[0] * scale)) // 2 + dx - round(KEY_BOX[0] * s)
    y = (C - round(size[1] * scale)) // 2 + dy - round(KEY_BOX[1] * s)
    out = rgba()
    out.paste(big.crop((max(0, -x), max(0, -y), min(w, C - x), min(w, C - y))), (max(0, x), max(0, y)))
    a = np.asarray(out).copy()
    a[a[..., 3] < 40] = 0           # imagegen alpha dust
    return Image.fromarray(a, "RGBA")


BAND = tuple(round(v * K) for v in (150, 215, 130, 254))    # y0, y1, x0, x1: the neck plates between the lens and the eye lamps, clear of the maw, lens, turbines and sails


def _scaled(im: Image.Image, sc: float) -> Image.Image:
    if sc == 1.0:
        return im
    w = round(C * sc)
    sm = im.resize((w, w), Image.Resampling.BICUBIC)
    out = rgba()
    off = (C - w) // 2
    out.paste(sm.crop((max(0, -off), max(0, -off), min(w, C - off), min(w, C - off))), (max(0, off), max(0, off)))
    return out


def register_core(ref: Image.Image, im: Image.Image, search: int = 8, scales=(0.97, 1.0, 1.03)):
    """(scale, dx, dy) that best overlays the head / neck alpha of `im` on `ref`. The wings and the fire
    jets move between poses, so only the core band is compared (searched at a quarter of the resolution)."""
    y0, y1, x0, x1 = BAND
    q = 3
    a = (np.asarray(ref)[y0:y1:q, x0:x1:q, 3] > 100).astype(np.float32)
    best = None
    sr = max(1, round(search * K / q)) + 1
    for sc in scales:
        al = (np.asarray(_scaled(im, sc))[..., 3] > 100).astype(np.float32)
        for dy in range(-sr, sr + 1):
            for dx in range(-sr, sr + 1):
                b = np.roll(np.roll(al, dy * q, axis=0), dx * q, axis=1)[y0:y1:q, x0:x1:q]
                err = float(np.abs(b - a).sum())
                if best is None or err < best[0]:
                    best = (err, sc, dx * q, dy * q)
    return best[1], best[2], best[3]


def apply_reg(im: Image.Image, sc: float, dx: int, dy: int) -> Image.Image:
    im = _scaled(im, sc)
    return Image.fromarray(np.roll(np.roll(np.asarray(im), dy, axis=0), dx, axis=1), "RGBA")
