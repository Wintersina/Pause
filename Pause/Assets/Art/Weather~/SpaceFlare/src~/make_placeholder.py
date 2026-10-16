#!/usr/bin/env python3
"""PLACEHOLDER solar-flare hazard sheet for Space (procedural; Codex's painted sheet replaces it).

Layout = the standard hazard sheet (docs/world-weather-hazards.md section 12): 3072 x 1536, 6 x 3 cells of 512.
  row 0  flare A (the single wide front, `solar_flare`), a 6-frame loop
  row 1  flare B (the narrower front of the three-band train, `flare_storm`), a 6-frame loop
  row 2  cells 0-1 warning A, cells 2-3 warning B

A cell is a VERTICAL band seen from above, tileable top-to-bottom: the hot leading edge sits at x = 0.78 of the cell
(HazardVisuals.FlareLead), the plasma tail streams behind it to the left, sparks are thrown ahead of it to the right.
The game mirrors the cell when the flare travels left. Usage: make_placeholder.py <out.png>
"""
import math
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

N = 512
XE = int(N * 0.78)
HOT = (255, 247, 220)
GOLD = (255, 200, 82)
ORANGE = (255, 128, 40)
MAGENTA = (255, 77, 200)
VIOLET = (153, 87, 255)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def ramp(u):
    """u 0 (at the leading edge) .. 1 (the tail's end): hot -> gold -> orange -> magenta -> violet."""
    stops = [(0.0, HOT), (0.12, GOLD), (0.35, ORANGE), (0.65, MAGENTA), (1.0, VIOLET)]
    for (u0, c0), (u1, c1) in zip(stops, stops[1:]):
        if u <= u1:
            return lerp(c0, c1, (u - u0) / (u1 - u0))
    return VIOLET


def render(frame, variant, warn):
    rnd = random.Random(1000 + variant * 7)
    f = frame / 6.0
    arcs = 18 if variant == 0 else 24
    layer = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    glow = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    # broad halo columns behind the front (soft violet -> magenta -> orange), alpha falling away from the edge
    halo = np.zeros((N, N, 4), np.float32)
    xs = np.arange(N, dtype=np.float32)[None, :]
    ys = np.arange(N, dtype=np.float32)[:, None]
    behind = np.clip((XE - xs) / 330.0, 0, 1)
    ahead = np.clip((xs - XE) / 70.0, 0, 1)
    ripple = 0.75 + 0.25 * np.sin(2 * np.pi * (ys / N * 4 + f))
    a = np.where(xs <= XE, (1 - behind) ** 2.2, (1 - ahead) ** 2.0) * ripple
    for ch in range(3):
        col = np.zeros_like(a)
        for u, c in ((0.0, HOT), (0.2, GOLD), (0.45, ORANGE), (0.75, MAGENTA), (1.0, VIOLET)):
            pass
        halo[..., ch] = 0
    # colour the halo per column from the ramp
    cols = np.array([ramp(float(np.clip((XE - x) / 330.0, 0, 1))) if x <= XE else GOLD for x in range(N)], np.float32)
    halo[..., :3] = cols[None, :, :]
    halo[..., 3] = a * (0.5 if variant == 0 else 0.42)
    # plasma arcs: undulating streaks trailing the front, hot near it
    for i in range(arcs):
        y0 = (i + rnd.random() * 0.6) / arcs * N
        amp = rnd.uniform(5, 20) * (1.0 if variant == 0 else 1.4)
        m = rnd.choice([1, 2, 3])
        k = rnd.choice([-1, 1]) * rnd.choice([1, 1, 2])
        ph = rnd.random()
        length = rnd.uniform(120, 330) if variant == 0 else rnd.uniform(160, 380)
        w0 = rnd.uniform(1.6, 3.4)
        pts = []
        for s in range(0, int(length), 3):
            u = s / 330.0
            y = y0 + amp * math.sin(2 * math.pi * (m * s / N + ph + k * f))
            pts.append((XE - s, y, u))
        for off in (-N, 0, N):
            for (x1, y1, u1), (x2, y2, u2) in zip(pts, pts[1:]):
                w = max(1, int(round(w0 * (1 - u1) + 0.5)))
                al = int(255 * (1 - u1 * 0.85) * (0.34 if warn else 1.0))
                c = ramp(min(1.0, u1)) + (al,)
                d.line([(x1, y1 + off), (x2, y2 + off)], fill=c, width=w)
                gd.line([(x1, y1 + off), (x2, y2 + off)], fill=c[:3] + (int(al * 0.7),), width=w * 3 + 2)
    if not warn:
        # the white-hot core, wobbling
        for off in (-N, 0, N):
            pts = [(XE + 7 * math.sin(2 * math.pi * (y / N * 3 + f)), y + off) for y in range(-4, N + 5, 3)]
            gd.line(pts, fill=HOT + (230,), width=15)
            d.line(pts, fill=HOT + (255,), width=4)
            d.line([(x + 4, y) for x, y in pts], fill=GOLD + (230,), width=3)
        # sparks ahead of the shock; the loop closes because each moves a whole sixth of its travel per frame
        for i in range(46):
            x0 = rnd.random()
            y = rnd.random() * N
            sp = rnd.choice([1, 2])
            x = XE + 6 + ((x0 + sp * frame / 6.0) % 1.0) * (N - XE - 8)
            r = rnd.choice([1, 1, 2, 2, 3])
            al = int(255 * (1 - (x - XE) / (N - XE)))
            c = (HOT if i % 3 else GOLD) + (al,)
            d.ellipse([x - r, y - r, x + r, y + r], fill=c)
            gd.ellipse([x - r * 2.5, y - r * 2.5, x + r * 2.5, y + r * 2.5], fill=c[:3] + (al // 2,))
    glow = glow.filter(ImageFilter.GaussianBlur(7))
    base = Image.fromarray(np.clip(halo, 0, 255).astype(np.uint8), "RGBA")
    out = Image.alpha_composite(base, glow)
    out = Image.alpha_composite(out, layer.filter(ImageFilter.GaussianBlur(0.6)))
    if warn:  # a faint, thin preview: about a third of the density
        arr = np.array(out)
        arr[..., 3] = (arr[..., 3] * 0.55).astype(np.uint8)
        out = Image.fromarray(arr, "RGBA")
    return out


def main(path):
    sheet = Image.new("RGBA", (6 * N, 3 * N), (0, 0, 0, 0))
    for v in (0, 1):
        for fr in range(6):
            sheet.paste(render(fr, v, False), (fr * N, v * N))
        for fr in range(2):
            sheet.paste(render(fr * 3, v, True), ((2 * v + fr) * N, 2 * N))
    sheet.save(path)
    print("wrote", path, sheet.size)


if __name__ == "__main__":
    main(sys.argv[1])
