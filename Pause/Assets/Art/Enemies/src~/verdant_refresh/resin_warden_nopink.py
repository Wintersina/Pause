#!/usr/bin/env python3
"""Resin Warden pink-free pass: every pink/magenta/hot-pink light (hue 300-360, S >= .3, V >= .35) becomes a lime-white world light.
Brightness is kept (V), saturation eased to the lime-white family (#E9FFB8, hue ~72), alpha and the cell grid untouched.
Reads verdant_old/elite/, writes Resources/Elites/Verdant and Enemies/Elite/Verdant.   python3 resin_warden_nopink.py"""
from pathlib import Path
import colorsys
import numpy as np
from PIL import Image
HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]
OLD = HERE.parent / "verdant_old" / "elite"
for name in ("verdant_elite_resin_warden", "verdant_elite_resin_warden_death"):
    a = np.array(Image.open(OLD / f"{name}.png").convert("RGBA"))
    f = a[..., :3].astype(float) / 255
    mx = f.max(-1); mn = f.min(-1); d = mx - mn
    s = np.where(mx > 0, d / np.where(mx > 0, mx, 1), 0)
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    h = np.zeros_like(mx); m = d > 1e-6
    h = np.where(m & (mx == r), ((g - b) / np.where(m, d, 1)) % 6, h)
    h = np.where(m & (mx == g) & (mx != r), (b - r) / np.where(m, d, 1) + 2, h)
    h = np.where(m & (mx == b) & (mx != r) & (mx != g), (r - g) / np.where(m, d, 1) + 4, h)
    hue = (h * 60) % 360
    pink = (a[..., 3] > 0) & (hue >= 300) & (hue < 360) & (s >= .3) & (mx >= .35)
    out = a.copy()
    core = np.array([233, 255, 184], float) / 255
    # luminous lime-white at the pixel's own brightness (brightest pinks become the pale core, darker ones the leaf-lime rim)
    v = mx[pink][:, None]
    t = np.clip((v - .35) / .65, 0, 1)
    lime = np.array([150, 214, 70], float) / 255
    col = (lime[None] * (1 - t) + core[None] * t) * np.clip(v / .85 + .15, 0, 1.0)
    out[pink, :3] = np.clip(col * 255, 0, 255).astype(np.uint8)
    for dest in (ART / "Resources/Elites/Verdant", ART / "Enemies/Elite/Verdant"):
        if not (dest / f"{name}.png").exists(): continue
        Image.fromarray(out).save(dest / f"{name}.png", optimize=True)
    print(name, "recoloured", int(pink.sum()), "px")
