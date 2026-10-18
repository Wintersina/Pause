"""Violet Squall palette for the Tempest Engine.

The approved Codex repaint of the idle pose (codex_idle_violet_squall.png) is registered onto the
existing 384 px cell (idle_before.png is the pre-repaint pose used as the registration target). Every
other pose is derived from the older gunmetal keys by keeping each pixel's own luminance and taking its
chroma from a smoothed copy of the repaint, so rivets, cracks and damage keep their detail while the
hull, coils, blades and trim pick up the new colours. Bright, electric effect pixels are left alone.
"""
from __future__ import annotations

from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

HERE = Path(__file__).resolve().parent
CELL = 384
SCALE, OFFSET = 0.975, (1, 1)  # fitted by silhouette IoU against idle_before.png


@lru_cache(maxsize=1)
def paint() -> Image.Image:
    c = Image.open(HERE / "codex_idle_violet_squall.png").convert("RGBA")
    size = round(CELL * SCALE)
    c = c.resize((size, size), Image.Resampling.LANCZOS)
    out = Image.new("RGBA", (CELL, CELL))
    out.alpha_composite(c, OFFSET)
    a = np.array(out)
    a[..., 3] = np.where(a[..., 3] > 128, 255, 0)
    a[a[..., 3] == 0, :3] = 0
    return Image.fromarray(a, "RGBA")


def _lum(rgb: np.ndarray) -> np.ndarray:
    return rgb @ np.array([.299, .587, .114])


@lru_cache(maxsize=1)
def _chroma_field():
    """Smoothed repaint colour spread over (and beyond) the silhouette, plus its smoothed luminance."""
    a = np.array(paint()).astype(float)
    m = a[..., 3] > 0
    rgb = a[..., :3] * m[..., None]
    w = m.astype(float)
    # Normalised box blur so edge pixels are not darkened, repeated until the field covers the cell.
    def blur(x, r):
        im = Image.fromarray(np.clip(x, 0, 255).astype(np.uint8)) if x.ndim == 3 else None
        chans = [np.array(Image.fromarray(np.clip(x[..., i], 0, 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(r))).astype(float)
                 for i in range(3)]
        return np.stack(chans, -1)
    wb = np.array(Image.fromarray((w * 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(4))).astype(float) / 255
    rb = blur(rgb, 4)
    field = rb / np.maximum(wb[..., None], 1e-3)
    have = wb > .02
    for r in (10, 24, 60):
        wb2 = np.array(Image.fromarray((have * 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(r))).astype(float) / 255
        rb2 = blur(field * have[..., None], r) / np.maximum(wb2[..., None], 1e-3)
        field = np.where(have[..., None], field, rb2)
        have = have | (wb2 > .02)
    return field, np.maximum(_lum(field), 8)


def recolor(img: Image.Image, keep_effects: bool = True) -> Image.Image:
    """Re-skin a cell-registered gunmetal image (any pose) in the Violet Squall palette."""
    im = img.convert("RGBA")
    a = np.array(im).astype(float)
    field, fl = _chroma_field()
    hsv = np.array(im.convert("RGB").convert("HSV")).astype(float)
    sat, val = hsv[..., 1], hsv[..., 2]
    L = _lum(a[..., :3])
    ratio = np.clip(L / fl, .15, 2.6)
    new = np.clip(field * ratio[..., None], 0, 255)
    # lift near-white highlights toward white instead of clipping to a coloured plateau
    over = np.clip((new.max(-1) - 235) / 40, 0, 1)[..., None]
    new = new * (1 - over * .4) + 255 * over * .4
    use = a[..., 3] > 0
    if keep_effects:
        # electric / explosion pixels: much brighter than the repaint underneath, or strongly yellow
        hue = hsv[..., 0] * 360 / 255
        yellow = (sat > 60) & (hue > 40) & (hue < 75) & (val > 150)
        bright = (L > 170) & (L > fl * 1.7)
        use &= ~(yellow | bright)
    g = new.mean(-1, keepdims=True)
    new = np.clip(g + (new - g) * 1.18, 0, 255)  # a touch more chroma than the smoothed field
    out = a.copy()
    out[use, :3] = new[use]
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA")
