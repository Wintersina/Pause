#!/usr/bin/env python3
"""Build the Cinder Drake v2 body atlas (35 cells, the Iron Kraken's layout) from the painted keys.

    python3 Pause/Assets/Art/Bosses/Ember~/src~/build.py

Cells (BossArt flat indices, 5 per row, 576 px = 384 x 1.5):
   0..3   idle          4 hit          5,6 tell0 a/b    7,8 tell1 a/b    9 fire    10,11 tell2 a/b
  12..16  death 0..4   17,18 retreat   19 portrait
  20..25  idle loop (six wing-beat drawings)
  26..28  tell0 (fire breath)  29..31 tell1 (furnace slugs)  32..34 tell2 (brow laser)

hull_key.png is the approved painting; t<pose>_<stage>.png are painted edits of it (jaw opening,
furnace shutters, brow gem charging). Where a painted pose is missing the build falls back to a
procedural glow on the hull so the atlas is always complete. The idle beat moves the outer wings by
rotating them about the shoulders (nearest-neighbour), so the head, chest and cannons stay registered.
"""
from __future__ import annotations

import math

import numpy as np
from PIL import Image

from common import *

# Measured anchors in the 384 cell (hull_key)
BROW = (192, 126)
FACE = (192, 176)
FURNACE = (192, 225)
SHOULDER = {-1: (96 * K, 150 * K), 1: (288 * K, 150 * K)}


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
        out = (xx - 192 * K) * side
        w = np.clip((out - 84 * K) / (34.0 * K), 0, 1) ** 1.2     # 0 on the body, 1 on the wing
        th = math.radians(deg) * w * side
        dx, dy = xx - px, yy - py
        c, s = np.cos(th), np.sin(th)
        rx = px + dx * c + dy * s          # inverse rotation: sample where the pixel came from
        ry = py - dx * s + dy * c
        m = (out > 84 * K)
        sx = np.where(m, rx, sx)
        sy = np.where(m, ry + lift * K * w, sy)
    sxi = np.clip(np.rint(sx).astype(int), 0, C - 1)
    syi = np.clip(np.rint(sy).astype(int), 0, C - 1)
    return Image.fromarray(a[syi, sxi], "RGBA")


def pulse(im: Image.Image, k: float) -> Image.Image:
    """Breathe the molten glow: brighten the orange / yellow texels by k (-1..1)."""
    a = np.asarray(im).astype(np.float32).copy()
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    hot = (r > 190) & (g > 110) & (b < 150) & (a[..., 3] > 40)
    a[hot, :3] = np.clip(a[hot, :3] * (1 + .10 * k) + np.array([10, 6, 0]) * k, 0, 255)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


IDLE_BEAT = [(0.0, 0, 0.0), (2.6, 1, 0.0), (4.4, 2, 0.0), (2.6, 3, 0.0), (0.0, 4, 0.0), (-2.2, 5, 0.0)]
IDLE_PULSE = [0.0, .35, .8, .35, -.2, -.5]


def idle(i: int) -> Image.Image:
    deg, _, lift = IDLE_BEAT[i]
    return pulse(beat(BASE, deg, lift), IDLE_PULSE[i])


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
    im = idle([0, 2, 4][stage])
    layer = rgba()
    d = SD(layer)
    if pose == 0:        # fire breath: the snout glows, fire in the throat, a jet on the last stage
        glow(layer, FACE, 9 + 8 * k, ORANGE, .8 + .8 * k)
        if stage == 2:
            for i in range(9):
                x = 192 + (i - 4) * 5 + (i % 2) * 2
                d.polygon([(x - 6, 190), (x + 6, 190), (x + 2, 226 + (i % 3) * 10), (x - 2, 226 + (i % 3) * 10)], fill=(*AMBER, 200))
    elif pose == 1:      # furnace slugs: the grate flares, the cannons glow
        glow(layer, FURNACE, 15 + 8 * k, YELLOW, .7 + .9 * k)
        for s in (-1, 1):
            glow(layer, (192 + s * 82, 138), 7 + 4 * k, ORANGE, .5 + k)
        if stage == 2:
            for x, y in [(176, 262), (208, 262), (192, 276)]:
                d.ellipse((x - 6, y - 6, x + 6, y + 6), fill=(*WHITE, 240))
                d.ellipse((x - 10, y - 10, x + 10, y + 10), outline=(*AMBER, 200), width=2)
    else:                # brow laser: the gem blazes, light runs down the horns
        glow(layer, BROW, 8 + 7 * k, SULFUR, .8 + 1.0 * k)
        if stage >= 1:
            for s in (-1, 1):
                d.line([(192 + s * 6, 124), (192 + s * 24, 98), (192 + s * 36, 84)], fill=(*YELLOW, 120 + 70 * stage), width=1 + stage)
        if stage == 2:
            spark(d, *BROW, 20, WHITE)
            d.line([(192, 134), (192, 176)], fill=(*YELLOW, 200), width=3)
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
        for line in ([(186, 110), (180, 130), (190, 150), (182, 170)], [(120, 150), (150, 170), (160, 200), (150, 226)],
                     [(262, 150), (236, 172), (228, 200), (240, 226)]):
            d.line(line, fill=(*ORANGE, 220), width=3)
            d.line(line, fill=(*YELLOW, 240), width=1)
        glow(layer, FURNACE, 14, YELLOW, 1.0)
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
        fireball(layer, 192, 190, max(r, 24))
        return over(out, layer)
    fireball(layer, 192, 190, r, .8 if i == 3 else .45)
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
    glow(layer, BROW, 6, SULFUR, .8)
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
    sheet.save(ROOT / "Ember.png")
    return cells, sheet


if __name__ == "__main__":
    cells, sheet = build_boss()
    print("built", ROOT / "Ember.png", sheet.size)
