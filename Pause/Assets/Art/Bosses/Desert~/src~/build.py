#!/usr/bin/env python3
"""Build the Dune-Eater v2 body atlas (35 cells, the Iron Kraken's layout) from the painted keys.

    python3 Pause/Assets/Art/Bosses/Desert~/src~/build.py

Cells (BossArt flat indices, 5 per row, 576 px = 384 x 1.5):
   0..3   idle          4 hit          5,6 tell0 a/b    7,8 tell1 a/b    9 fire    10,11 tell2 a/b
  12..16  death 0..4   17,18 retreat   19 portrait
  20..25  idle loop (six painted drawings: sails, turbines, cables and the mine move)
  26..28  tell0 (the maw: grit fan)   29..31 tell1 (the crane boom: mines)   32..34 tell2 (the crown lens: beam)

hull_key.png is the approved painting; i1..i5 (the idle loop), t<pose>_<stage>.png (maw opening, boom lowering, lens
charging) and d1..d4 are painted edits of it. Where a painted drawing is missing the build falls back to a procedural
version on the hull so the atlas is always complete.
"""
from __future__ import annotations

import math

import numpy as np
from PIL import Image

from common import *

# Measured anchors in the 384 cell (hull_key): the crown lens, the maw's throat, the boom's mine, the eye lamps, the turbines
LENS = (190, 93)
MAW = (190, 253)
MINE = (334, 142)
EYES = [(160, 198), (218, 198)]
TURBINES = [(95, 196), (285, 196), (108, 294), (268, 294)]
SHOULDER = {-1: (104 * K, 190 * K), 1: (276 * K, 190 * K)}


def painted(name: str) -> Image.Image | None:
    p = SRC / f"{name}.png"
    if not p.exists():
        return None
    return to_cell(Image.open(p))


BASE = to_cell(Image.open(SRC / "hull_key.png"))


def registered(name: str, scales=(0.97, 1.0, 1.03)) -> Image.Image | None:
    """A painted edit nudged so its body core sits exactly over the hull's."""
    im = painted(name)
    if im is None:
        return None
    sc, dx, dy = register_core(BASE, im, 8, scales)
    return apply_reg(im, sc, dx, dy)


# ------------------------------------------------------------------ idle (wing beat)
def beat(im: Image.Image, deg: float, lift: float) -> Image.Image:
    """Rotate the outer wings about the shoulders by `deg` (nearest-neighbour). Positive raises the tips."""
    a = np.asarray(im)
    yy, xx = np.indices((C, C)).astype(np.float32)
    sx, sy = xx.copy(), yy.copy()
    for side, (px, py) in SHOULDER.items():
        out = (xx - 190 * K) * side
        w = np.clip((out - 100 * K) / (40.0 * K), 0, 1) ** 1.2     # 0 on the body, 1 on the wing
        th = math.radians(deg) * w * side
        dx, dy = xx - px, yy - py
        c, s = np.cos(th), np.sin(th)
        rx = px + dx * c + dy * s          # inverse rotation: sample where the pixel came from
        ry = py - dx * s + dy * c
        m = (out > 100 * K)
        sx = np.where(m, rx, sx)
        sy = np.where(m, ry + lift * K * w, sy)
    sxi = np.clip(np.rint(sx).astype(int), 0, C - 1)
    syi = np.clip(np.rint(sy).astype(int), 0, C - 1)
    return Image.fromarray(a[syi, sxi], "RGBA")


def pulse(im: Image.Image, k: float) -> Image.Image:
    """Breathe the glows: brighten the cyan-white (turbines, lens, throat) and amber (eye lamps, mine studs) texels by k (-1..1)."""
    a = np.asarray(im).astype(np.float32).copy()
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    opaque = a[..., 3] > 40
    cyan = opaque & (g > 190) & (b > 170) & (r < 215)
    amber = opaque & (r > 200) & (g > 130) & (b < 120)
    a[cyan, :3] = np.clip(a[cyan, :3] * (1 + .10 * k) + np.array([6, 10, 10]) * k, 0, 255)
    a[amber, :3] = np.clip(a[amber, :3] * (1 + .10 * k) + np.array([10, 6, 0]) * k, 0, 255)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


IDLE_PULSE = [0.0, .35, .8, .35, -.2, -.5]
IDLE_BEAT = [0.0, 1.4, 2.4, 1.4, 0.0, -1.2]


def idle(i: int) -> Image.Image:
    """Idle drawing i of the loop: painted (i1..i5.png, registered to the hull) or, if missing, a small wing-beat of the hull."""
    if i == 0:
        return BASE
    pa = registered(f"i{i}", (0.98, 1.0, 1.02))
    if pa is not None:
        return pa
    return pulse(beat(BASE, IDLE_BEAT[i], 0.0), IDLE_PULSE[i])


# ------------------------------------------------------------------ fx helpers
def glow(layer: Image.Image, xy, r, color=AMBER, power=1.0):
    d = SD(layer)
    x, y = xy
    for grow, alpha in [(2.2, 14), (1.7, 28), (1.35, 60), (1.0, 120)]:
        rr = r * grow
        d.ellipse((x - rr, y - rr, x + rr, y + rr), fill=(*color, min(255, int(alpha * power))))
    d.ellipse((x - r * .55, y - r * .55, x + r * .55, y + r * .55), fill=(*WHITE, min(255, int(190 * power))))


def spark(d, x, y, size, color=YELLOW, alpha=255):
    d.line([(x - size, y), (x + size, y)], fill=(*color, alpha), width=1)
    d.line([(x, y - size), (x, y + size)], fill=(*color, alpha), width=1)
    h = size * .6
    d.line([(x - h, y - h), (x + h, y + h)], fill=(*ORANGE, alpha), width=1)
    d.line([(x - h, y + h), (x + h, y - h)], fill=(*ORANGE, alpha), width=1)


def over(im: Image.Image, layer: Image.Image) -> Image.Image:
    out = im.copy()
    out.alpha_composite(layer)
    return out


# ------------------------------------------------------------------ tells (painted if present)
def tell_pose(pose: int, stage: int) -> Image.Image:
    name = f"t{pose}_{stage}"
    pa = registered(name)
    if pa is not None:
        return pa
    # procedural fallback: the idle hull plus a growing glow at the firing part
    k = stage / 2.0
    im = idle(0)
    layer = rgba()
    d = SD(layer)
    if pose == 0:        # the maw: the throat blazes sun-gold, sand-slugs at the last stage
        glow(layer, MAW, 12 + 10 * k, AMBER, .8 + .8 * k)
        if stage == 2:
            for i in range(7):
                x = MAW[0] + (i - 3) * 7
                d.polygon([(x - 4, MAW[1] + 8), (x + 4, MAW[1] + 8), (x + 1, MAW[1] + 30 + (i % 3) * 9), (x - 1, MAW[1] + 30 + (i % 3) * 9)], fill=(*AMBER, 200))
    elif pose == 1:      # the crane boom: the mine's studs flare and the winch glows
        glow(layer, MINE, 10 + 6 * k, ORANGE, .7 + .9 * k)
        if stage == 2:
            spark(d, *MINE, 18, WHITE)
    else:                # the crown lens: the glass blazes white-teal
        glow(layer, LENS, 14 + 10 * k, TEAL, .8 + 1.0 * k)
        if stage == 2:
            spark(d, *LENS, 28, WHITE)
            d.line([(LENS[0], LENS[1] + 20), (LENS[0], LENS[1] + 70)], fill=(*WHITE, 200), width=3)
    return over(im, layer)


# ------------------------------------------------------------------ base cells
def hit() -> Image.Image:
    a = np.asarray(idle(0)).astype(np.float32).copy()
    opaque = a[..., 3] > 45
    bright = opaque & (a[..., :3].mean(axis=2) > 30)
    a[bright, :3] = np.minimum(255, a[bright, :3] * .28 + np.array([255, 240, 190]) * .72)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def fireball(layer: Image.Image, cx, cy, r, power=1.0):
    d = SD(layer)
    rng = np.random.default_rng(int(r * 13 + cx))
    for rad, col, al in [(1.0, EMBER, 150), (.82, ORANGE, 200), (.6, AMBER, 230), (.38, YELLOW, 250), (.2, WHITE, 255)]:
        pts = []
        for k in range(18):
            t = k / 18 * 2 * math.pi
            rr = r * rad * (1 + .22 * math.sin(k * 2.7 + rad * 5) + .08 * rng.uniform(-1, 1))
            pts.append((cx + math.cos(t) * rr, cy + math.sin(t) * rr))
        d.polygon(pts, fill=(*col, int(al * power)))


def death_cell(i: int) -> Image.Image:
    keys = ["death_split", "death_overload", "death_peak", "death_shockwave", "death_fade"]
    if i == 0:
        im = idle(0)
        layer = rgba(); d = SD(layer)
        for line in ([(186, 110), (180, 150), (194, 190), (186, 230)], [(110, 190), (140, 200), (160, 232), (150, 270)],
                     [(270, 190), (240, 204), (226, 232), (236, 270)]):
            d.line(line, fill=(*ORANGE, 220), width=3)
            d.line(line, fill=(*YELLOW, 240), width=1)
        glow(layer, MAW, 14, YELLOW, 1.0)
        return over(im, layer)
    pa = painted(keys[i - 1] if i - 1 < len(keys) else keys[-1])
    if pa is not None:
        if i >= 4:
            s = .9 if i == 3 else .7
            w = round(C * s)
            sm = pa.resize((w, w), Image.Resampling.NEAREST)
            a = np.asarray(sm).copy(); a[..., 3] = (a[..., 3] * (.8 if i == 3 else .55)).astype(np.uint8)
            out = rgba(); out.alpha_composite(Image.fromarray(a, "RGBA"), ((C - w) // 2, (C - w) // 2))
            return out
        return pa
    # fallback: a growing fireball over the breaking hull
    base = idle(0)
    layer = rgba()
    r = [0, 60, 110, 150, 120][i]
    if i <= 2:
        out = base.copy(); out = Image.blend(out, rgba(), .0)
        fireball(layer, 190, 190, max(r, 24))
        return over(out, layer)
    fireball(layer, 190, 190, r, .8 if i == 3 else .45)
    return layer


def retreat(stage: int) -> Image.Image:
    im = idle(0)
    a = np.asarray(im).copy()
    a[..., 3] = (a[..., 3].astype(np.float32) * (.85 if stage == 0 else .62)).astype(np.uint8)
    im = Image.fromarray(a, "RGBA")
    layer = rgba(); d = SD(layer)
    for k in range(10):
        x = 43 + k * 33 + ((k * 7 + stage * 9) % 13)
        y0 = 36 + ((k * 23 + stage * 27) % 77)
        y1 = min(358, y0 + 198 + (k % 3) * 22)
        d.line([(x, y0), (x, y1)], fill=(*AMBER, 65 if stage == 0 else 95), width=1 + (k % 3 == 0))
    return over(im, layer)


def portrait() -> Image.Image:
    im = idle(2)
    layer = rgba(); d = SD(layer)
    glow(layer, LENS, 6, TEAL, .8)
    return over(im, layer)


def build_cells() -> list[Image.Image]:
    cells: list[Image.Image] = [None] * 35
    for i in range(4):
        cells[i] = idle(i)
    cells[4] = hit()
    cells[5], cells[6] = tell_pose(0, 0), tell_pose(0, 1)
    cells[7], cells[8] = tell_pose(1, 0), tell_pose(1, 1)
    cells[9] = tell_pose(0, 2)
    cells[10], cells[11] = tell_pose(2, 0), tell_pose(2, 1)
    for i in range(5):
        cells[12 + i] = death_cell(i)
    cells[17], cells[18], cells[19] = retreat(0), retreat(1), portrait()
    for i in range(6):
        cells[20 + i] = idle(i)
    for pose in range(3):
        for st in range(3):
            cells[26 + pose * 3 + st] = tell_pose(pose, st)
    return cells


def build_boss():
    cells = build_cells()
    sheet = Image.new("RGBA", (C * 5, C * 7))
    for i, im in enumerate(cells):
        sheet.alpha_composite(im, ((i % 5) * C, (i // 5) * C))
    sheet.save(ROOT / "Desert.png")
    return cells, sheet


if __name__ == "__main__":
    cells, sheet = build_boss()
    print("built", ROOT / "Desert.png", sheet.size)
