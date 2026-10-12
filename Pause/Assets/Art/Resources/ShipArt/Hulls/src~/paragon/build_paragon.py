#!/usr/bin/env python3
"""Builds the Paragon (the premium "cheat ship") hull sheet, skins and exhaust.

    python3 build_paragon.py            # hull + skins + exhaust strips + data.json
    python3 build_paragon.py --preview DIR

Paragon's art is NOT a vector design like the other hulls: it is one HD
neon pixel-art ship (ship_alone.png, from the icon work) that this script
splits and rebuilds in the same flipbook format every hull uses
(Scripts/Ship/ShipHullArt.cs): 256 px cells, 9 columns (idle 0-5, bank L/R,
hit) x 3 damage rows. Everything is deterministic (seeded), nothing is hand
painted:

  * hull      the source with its three baked flames cut off at the nozzle
              lips (a dark lip is added so the engines read as closed),
              downscaled to 243 px wide, centred in the cell
  * poses     bob (idle dy), running-light blink, canopy glint, bank warp
              (the same warp ShipDamageTable.Posed uses, so emitter points
              stay on paint), hit flash
  * damage    states 1 and 2 are scripted from the rest art: scorch, blown
              plating (dark cavity with frame lines and exposed wiring),
              cracks, torn edges, a cracked canopy. Always INSIDE the intact
              alpha (rows 1-2 share row 0's exact alpha, like every hull)
  * skins     hue-shifted recolours of the finished sheet (alpha identical)
  * exhaust   the long centre flame of the source, quantised to the five
              exhaust bands, 7 flipbook frames (+ smear) for cruise and boost,
              pasted into ShipArt/Exhaust/exhaust_atlas.png (free band at
              x=1050..1750, y=504)

It writes data.json (rect, nozzles, emitters, colours) which the C# rows in
ShipHullArt / ShipNozzles / ShipDamageTable / ShipExhaustStyle were copied
from. Palette: neon blue / cyan; the only red is the hull's own armour.
"""
import colorsys
import io
import json
import math
import os
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.normpath(os.path.join(HERE, "..", "..", "..", ".."))   # Art/Resources
HULLS = os.path.join(RES, "ShipArt", "Hulls")
SKINS_DIR = os.path.join(RES, "ShipArt", "Skins")
ATLAS = os.path.join(RES, "ShipArt", "Exhaust", "exhaust_atlas.png")
SRC = os.path.join(HERE, "ship_alone.png")

KEY = "Paragon"
CELL = 256
COLS, STATES = 9, 3
TARGET_W = 243
INK = (10, 11, 30)
BONE = (244, 234, 212)

# idle drawings: (dy in canvas units, lights, glint) -- ShipDamageTable.idleDy
IDLE = [(0, True, None), (-2, True, None), (-2, False, None), (0, True, .15), (0, False, .75), (2, True, None)]

# ------------------------------------------------------------------ hull --


def load_hull():
    """The source with the baked flames removed, cropped to its bbox."""
    im = Image.open(SRC).convert("RGBA")
    a = np.array(im)
    h, w = a.shape[:2]
    ys, xs = np.mgrid[0:h, 0:w]
    side = (xs < 150) | (xs > 585)
    center = (xs >= 262) & (xs <= 473)
    cut = (side & (ys >= 552)) | (center & (ys >= 540)) | (ys >= 600)
    a[cut] = 0
    # a dark lip just inside each nozzle's cut so the engine reads as closed
    for x0, x1, y in ((30, 100, 552), (637, 700, 552), (276, 459, 540)):
        for yy in range(y - 6, y):
            for xx in range(x0, x1):
                if a[yy, xx, 3] > 200:
                    a[yy, xx, :3] = INK
    out = Image.fromarray(a)
    return out.crop(out.getbbox())


def downscale(img, width):
    """Premultiplied Lanczos downscale (no dark fringe), alpha cleaned."""
    scale = width / img.width
    size = (width, round(img.height * scale))
    arr = np.array(img).astype(np.float32)
    al = arr[..., 3:4] / 255.0
    pre = np.concatenate([arr[..., :3] * al, arr[..., 3:4]], axis=2)
    chans = [Image.fromarray(pre[..., i]).resize(size, Image.LANCZOS) for i in range(4)]
    pre = np.stack([np.array(c) for c in chans], axis=2)
    alpha = np.clip(pre[..., 3], 0, 255)
    alpha[alpha < 10] = 0
    safe = np.maximum(alpha, 1)[..., None] / 255.0
    rgb = np.clip(pre[..., :3] / safe, 0, 255)
    out = np.dstack([rgb, alpha]).astype(np.uint8)
    out[out[..., 3] == 0] = 0
    return Image.fromarray(out), scale


class Hull:
    def __init__(self):
        full = load_hull()
        self.src_size = full.size
        self.img, self.scale = downscale(full, TARGET_W)
        w, h = self.img.size
        self.ox = (CELL - w) // 2
        self.oy = (CELL - h) // 2
        # rest cell
        self.rest = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        self.rest.alpha_composite(self.img, (self.ox, self.oy))
        self.alpha = np.array(self.rest)[..., 3]

    def to_cell(self, sx, sy):
        """Source (full-res crop) pixel -> rest-cell pixel."""
        return sx * self.scale + self.ox, sy * self.scale + self.oy


# -------------------------------------------------------------- damage ----


def rng_for(state):
    return random.Random(0x9A7A60 + state)


def noisy_blob(draw, cx, cy, rx, ry, rng, fill, n=22, jag=.35):
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        k = 1 + rng.uniform(-jag, jag)
        pts.append((cx + math.cos(t) * rx * k, cy + math.sin(t) * ry * k))
    draw.polygon(pts, fill=fill)


def jag_line(draw, pts, rng, fill, width, wobble=1.6, step=7):
    out = []
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        n = max(2, int(math.hypot(x1 - x0, y1 - y0) / step))
        for i in range(n):
            t = i / n
            out.append((x0 + (x1 - x0) * t + rng.uniform(-wobble, wobble),
                        y0 + (y1 - y0) * t + rng.uniform(-wobble, wobble)))
    out.append(pts[-1])
    draw.line(out, fill=fill, width=width, joint="curve")


# damage design in SOURCE pixel coordinates of the hull crop (735 x ~597)
DESIGN = {
    1: dict(
        scorch=[(105, 420, 70, 55), (555, 470, 60, 50), (366, 520, 80, 28)],
        panel=[(158, 372), (205, 340), (258, 352), (270, 392), (236, 420), (178, 412)],
        cracks=[[(418, 250), (445, 300), (430, 340), (462, 390)], [(300, 285), (282, 335), (300, 370)]],
        sparks=[(205, 372), (232, 392), (190, 396)],
    ),
    2: dict(
        scorch=[(640, 420, 80, 70), (540, 330, 60, 60), (230, 330, 60, 50)],
        panel=[(500, 400), (548, 372), (590, 395), (582, 450), (530, 470), (498, 440)],
        cracks=[[(640, 280), (655, 340), (625, 390), (650, 450), (632, 500)],
                [(335, 140), (352, 188), (340, 230), (362, 262)],
                [(110, 330), (140, 380), (118, 440)]],
        bites=[[(690, 440), (735, 400), (735, 520), (700, 500), (712, 470)],
               [(0, 430), (30, 455), (12, 500), (0, 520)]],
        canopy=[[(345, 160), (372, 200), (350, 240), (380, 290)], [(395, 170), (372, 215)]],
        embers=[(560, 520), (590, 500), (546, 540)],
    ),
}


def damage_layer(hull, state):
    """RGBA overlay of the cumulative damage up to `state` (cell sized)."""
    S = 4   # supersample
    layer = Image.new("RGBA", (CELL * S, CELL * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    tf = lambda p: tuple(v * S for v in hull.to_cell(*p))
    for st in range(1, state + 1):
        rng = rng_for(st)
        design = DESIGN[st]
        for cx, cy, rx, ry in design["scorch"]:
            x, y = tf((cx, cy))
            for k, a in ((1.0, 110), (.68, 150), (.4, 190)):
                noisy_blob(d, x, y, rx * hull.scale * S * k, ry * hull.scale * S * k, rng, (14, 10, 24, a))
        # blown plating: dark cavity, frame ribs, exposed wiring
        poly = [tf(p) for p in design["panel"]]
        d.polygon(poly, fill=(8, 9, 24, 255))
        d.line(poly + [poly[0]], fill=(60, 80, 140, 255), width=2 * S // 2 + 2, joint="curve")
        cx = sum(p[0] for p in poly) / len(poly)
        cy = sum(p[1] for p in poly) / len(poly)
        for i in (0, 2, 4):
            d.line([(cx, cy), poly[i]], fill=(34, 46, 100, 255), width=S + 1)
        for k in range(5):
            px = cx + rng.uniform(-14, 14) * S / 2
            py = cy + rng.uniform(-10, 10) * S / 2
            d.line([(px, py), (px + rng.uniform(-9, 9) * S / 2, py + rng.uniform(-9, 9) * S / 2)],
                   fill=[(255, 180, 60, 255), (110, 242, 238, 255)][k % 2], width=S)
        for pts in design["cracks"]:
            p = [tf(q) for q in pts]
            jag_line(d, [(x - S * .6, y) for x, y in p], rng, (4, 5, 16, 255), S * 2 + 2)   # dark groove
            jag_line(d, [(x + S * .8, y) for x, y in p], rng, (150, 200, 255, 230), S)  # lit lip
        for bite in design.get("bites", []):
            d.polygon([tf(q) for q in bite], fill=(6, 6, 18, 255))
            d.line([tf(q) for q in bite], fill=(255, 140, 50, 200), width=S // 2 + 1)
        for pts in design.get("canopy", []):
            p = [tf(q) for q in pts]
            jag_line(d, p, rng, (244, 234, 212, 255), S, wobble=1.2)
            jag_line(d, [(x + S, y) for x, y in p], rng, (10, 14, 40, 255), max(1, S // 2), wobble=1.2)
        for ex, ey in design.get("embers", []):
            x, y = tf((ex, ey))
            d.ellipse([x - 3 * S, y - 3 * S, x + 3 * S, y + 3 * S], fill=(255, 140, 40, 220))
            d.ellipse([x - 1.5 * S, y - 1.5 * S, x + 1.5 * S, y + 1.5 * S], fill=(255, 226, 150, 255))
        for sx, sy in design.get("sparks", []):
            x, y = tf((sx, sy))
            d.ellipse([x - 2 * S, y - 2 * S, x + 2 * S, y + 2 * S], fill=(255, 226, 150, 255))
    layer = layer.resize((CELL, CELL), Image.LANCZOS)
    arr = np.array(layer).astype(np.float32)
    arr[..., 3] *= (hull.alpha > 0)           # inside the outline only
    return Image.fromarray(arr.astype(np.uint8))


def with_damage(hull):
    cells = [hull.rest]
    for st in (1, 2):
        c = hull.rest.copy()
        c.alpha_composite(damage_layer(hull, st))
        cells.append(c)
    # exact intact alpha (alpha_composite of an in-mask overlay keeps it, but
    # make it a hard guarantee)
    out = []
    for c in cells:
        arr = np.array(c)
        arr[..., 3] = hull.alpha
        out.append(Image.fromarray(arr))
    return out


# ---------------------------------------------------------------- poses ---


def light_mask(arr):
    r, g, b, a = [arr[..., i].astype(int) for i in range(4)]
    return (a > 200) & (r > 205) & (g > 190) & (b > 170)


def canopy_mask(hull):
    arr = np.array(hull.rest)
    r, g, b, a = [arr[..., i].astype(int) for i in range(4)]
    x0, y0 = hull.to_cell(322, 135)
    x1, y1 = hull.to_cell(418, 310)
    m = np.zeros(a.shape, bool)
    m[int(y0):int(y1), int(x0):int(x1)] = True
    return m & (a > 200) & (b > 150) & (r < 130)


def warp(img, dy_px, bank):
    """Inverse-map warp: ShipDamageTable.Posed exactly (u = px / 2)."""
    arr = np.array(img)
    out = np.zeros_like(arr)
    ys, xs = np.mgrid[0:CELL, 0:CELL]
    if bank:
        dd = xs - 128 - 3.0 * bank
        near = (dd < 0) == (bank < 0)
        k = np.where(near, .80, .95)
        sx = 128 + dd / k
        d_u = np.abs(dd / k) / 2.0
        shift_px = 2.0 * np.where(near, 1.2, -.6) * d_u / 60.0
        sy = ys - shift_px - dy_px
    else:
        sx = xs.astype(float)
        sy = ys - dy_px
    sxi = np.rint(sx).astype(int)
    syi = np.rint(sy).astype(int)
    ok = (sxi >= 0) & (sxi < CELL) & (syi >= 0) & (syi < CELL)
    out[ok] = arr[syi[ok], sxi[ok]]
    return Image.fromarray(out)


def pose(hull, base, dy_u=0, bank=0, lights=True, glint=None, flash=False):
    arr = np.array(base).astype(np.float32)
    if not lights:
        m = light_mask(arr.astype(np.uint8))
        arr[m, :3] = arr[m, :3] * .25 + np.array([160, 30, 44]) * .75
    if glint is not None and hull is not None:
        cm = canopy_mask(hull)
        ys, xs = np.nonzero(cm)
        if len(xs):
            span = xs.max() - xs.min() + (ys.max() - ys.min())
            pos = xs.min() + glint * span
            band = np.abs((xs - ys * .6) - (pos - ys.min() * .6)) < 4
            sel = (ys[band], xs[band])
            arr[sel[0], sel[1], :3] = arr[sel[0], sel[1], :3] * .3 + np.array(BONE) * .7
    img = Image.fromarray(arr.astype(np.uint8))
    img = warp(img, dy_u * 2.0, bank)
    if flash:
        a = np.array(img).astype(np.float32)
        a[..., :3] = a[..., :3] * .2 + np.array(BONE) * .8
        img = Image.fromarray(a.astype(np.uint8))
    return img


def build_sheet(hull):
    states = with_damage(hull)
    sheet = Image.new("RGBA", (CELL * COLS, CELL * STATES), (0, 0, 0, 0))
    for s, base in enumerate(states):
        # damage rows are posed from their own base; the canopy/light masks
        # come from the intact art
        for c in range(COLS):
            if c < 6:
                dy, lights, glint = IDLE[c]
                im = pose(hull, base, dy, 0, lights, glint)
            elif c == 6:
                im = pose(hull, base, 0, -1)
            elif c == 7:
                im = pose(hull, base, 0, 1)
            else:
                im = pose(hull, base, flash=True)
            sheet.alpha_composite(im, (c * CELL, s * CELL))
    # rows 1-2 share row 0's alpha exactly
    arr = np.array(sheet)
    a0 = arr[:CELL, :, 3]
    for s in (1, 2):
        arr[s * CELL:(s + 1) * CELL, :, 3] = a0
        arr[s * CELL:(s + 1) * CELL][a0 == 0] = 0
    return Image.fromarray(arr)


# ------------------------------------------------- Codex-drawn hull sheets --
# The hull art itself is drawn by Codex (codex/hull_state{0,1,2}.png: three
# 3 x 3 sheets of 1024 px cells on flat #00FF00, see codex/J1 brief). This
# script only keys, fits and assembles them: cells 0-5 (idle) and 8 (hit
# flash) are Codex's drawings; the bank poses are Codex's rest drawing put
# through the same bank warp ShipDamageTable.Posed describes (Codex's own
# banks barely differed from the rest pose). Damage rows take the intact
# row's exact alpha, like every hull.
CODEX = os.path.join(HERE, "codex")


def codex_cells(path):
    im = Image.open(path).convert("RGB")
    n = im.width // 3
    return [im.crop((c * n, r * n, (c + 1) * n, (r + 1) * n)) for r in range(3) for c in range(3)], n


def key_green(img):
    a = np.array(img).astype(int)
    dist = np.sqrt((a[..., 0]) ** 2 + (a[..., 1] - 255) ** 2 + (a[..., 2]) ** 2)
    alpha = np.where(dist < 110, 0, 255).astype(np.uint8)
    rgb = a.copy()
    # despill: no green fringe survives on the kept edge pixels
    rgb[..., 1] = np.minimum(rgb[..., 1], np.maximum(rgb[..., 0], rgb[..., 2]) + 40)
    out = np.dstack([np.clip(rgb, 0, 255).astype(np.uint8), alpha])
    out[alpha == 0] = 0
    return Image.fromarray(out)


def fit_cells(cells, n, scale, anchor):
    """Resize every cell by `scale` and place it so `anchor` (cell0's bbox
    centre, in source pixels) lands on the middle of a 256 px cell."""
    out = []
    for c in cells:
        k = key_green(c)
        big, _ = downscale_rgba(k, scale)
        cell = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        cell.alpha_composite(big, (int(round(CELL / 2 - anchor[0] * scale)), int(round(CELL / 2 - anchor[1] * scale))))
        out.append(cell)
    return out


def downscale_rgba(img, scale):
    size = (max(1, round(img.width * scale)), max(1, round(img.height * scale)))
    arr = np.array(img).astype(np.float32)
    al = arr[..., 3:4] / 255.0
    pre = np.concatenate([arr[..., :3] * al, arr[..., 3:4]], axis=2)
    chans = [Image.fromarray(pre[..., i]).resize(size, Image.LANCZOS) for i in range(4)]
    pre = np.stack([np.array(c).astype(np.float32) for c in chans], axis=2)
    alpha = np.clip(pre[..., 3], 0, 255)
    alpha[alpha < 40] = 0
    safe = np.maximum(alpha, 1)[..., None] / 255.0
    rgb = np.clip(pre[..., :3] / safe, 0, 255)
    out = np.dstack([rgb, alpha]).astype(np.uint8)
    out[out[..., 3] == 0] = 0
    return Image.fromarray(out), scale


def codex_sheet():
    rows = []
    scale = anchor = None
    for st in range(3):
        cells, n = codex_cells(os.path.join(CODEX, f"hull_state{st}.png"))
        k0 = np.array(key_green(cells[0]))[..., 3]
        ys, xs = np.nonzero(k0)
        bw = xs.max() - xs.min() + 1
        if scale is None:                       # the intact sheet sets the fit
            scale = TARGET_W / bw
            anchor = ((xs.min() + xs.max()) / 2, (ys.min() + ys.max()) / 2)
        rows.append(fit_cells(cells, n, scale, anchor))
    # intact alpha per column; damaged rows copy it exactly
    sheet = Image.new("RGBA", (CELL * COLS, CELL * STATES), (0, 0, 0, 0))
    rest_states = []
    for st in range(3):
        cells = rows[st]
        rest = cells[0]
        pieces = []
        for c in range(COLS):
            if c < 6 or c == 8:
                pieces.append(cells[c])
            elif c == 6:
                pieces.append(warp(rest, 0, -1))
            else:
                pieces.append(warp(rest, 0, 1))
        rest_states.append(pieces)
    for st in range(3):
        for c in range(COLS):
            base = np.array(rest_states[0][c])
            cur = np.array(rest_states[st][c])
            if st:
                m0 = base[..., 3] > 0
                fill = m0 & (cur[..., 3] == 0)
                cur[fill] = base[fill]
                cur[~m0] = 0
            sheet.alpha_composite(Image.fromarray(cur), (c * CELL, st * CELL))
    return sheet, rest_states[0][0]


# ---------------------------------------------------------------- skins ---

SKINS = [
    # name, kind, description, (hue h 0-255 PIL, sat mult, val mult)
    dict(name="Aurum", mode="gold"),
    dict(name="Umbra", mode="violet"),
    dict(name="Spectre", mode="ice"),
    dict(name="Viridian", mode="green"),
]


def recolour(sheet, mode):
    arr = np.array(sheet)
    rgb = Image.fromarray(arr[..., :3]).convert("HSV")
    hsv = np.array(rgb).astype(np.float32)
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    # the armour's red: hue within ~25 deg of red, saturated, not dark
    hd = np.minimum(h, 255 - h)            # distance from red (0/255)
    red = (hd < 18) & (s > 110) & (v > 70) & (arr[..., 3] > 0)
    if mode == "gold":
        newh = 28 + np.where(h < 128, h, h - 255) * .35
        news = np.clip(s * 1.0, 0, 255)
        newv = np.clip(v * 1.08 + 10, 0, 255)
    elif mode == "violet":
        newh = 192 + np.where(h < 128, h, h - 255) * .35
        news = np.clip(s * .9, 0, 255)
        newv = np.clip(v * 1.0 + 6, 0, 255)
    elif mode == "green":
        newh = 88 + np.where(h < 128, h, h - 255) * .3
        news = np.clip(s * .95, 0, 255)
        newv = np.clip(v * 1.0 + 8, 0, 255)
    else:  # ice: bone-silver armour
        newh = 140 + 0 * h
        news = np.clip(s * .12, 0, 255)
        newv = np.clip(v * 1.18 + 36, 0, 255)
    hsv[..., 0] = np.where(red, newh, h)
    hsv[..., 1] = np.where(red, news, s)
    hsv[..., 2] = np.where(red, newv, v)
    out = Image.fromarray(np.clip(hsv, 0, 255).astype(np.uint8), "HSV").convert("RGB")
    res = np.dstack([np.array(out), arr[..., 3]])
    res[res[..., 3] == 0] = 0
    return Image.fromarray(res)


def skin_primary(sheet_rest_rgba, recoloured):
    """Hull base / shadow / highlight of a skin (median of the recoloured
    armour pixels), for ShipSkins' table (hex)."""
    a0 = np.array(sheet_rest_rgba)
    a1 = np.array(recoloured)
    m = (a0[..., 3] > 0) & (np.abs(a0[..., :3].astype(int) - a1[..., :3].astype(int)).sum(axis=2) > 30)
    px = a1[m][:, :3].astype(float)
    lum = px @ np.array([.3, .59, .11])
    order = np.argsort(lum)
    n = len(px)
    pick = lambda q: tuple(int(v) for v in px[order[int(n * q)]])
    hx = lambda c: "#%02X%02X%02X" % c
    return hx(pick(.5)), hx(pick(.12)), hx(pick(.92))


# -------------------------------------------------------------- exhaust ---

BANDS = {  # ExhaustStyle row for Paragon (outer, mid, core, dark, accent)
    "outer": (0x2E, 0x9B, 0xE6), "mid": (0x6E, 0xF2, 0xEE), "core": (0xF4, 0xEA, 0xD4),
    "dark": (0x17, 0x4C, 0x8C), "accent": (0x9A, 0xB0, 0xFF),
}
FRAME_W, FRAME_H = 48, 192
HEAD_Y = 4
PLUME_X, BOOST_X, STRIDE, ATLAS_Y_TOP = 1050, 1400, 50, 1472 - 504 - 192
TICKS = [2, 2, 1, 2, 2, 2, 2]


def flame_source():
    im = Image.open(SRC).convert("RGBA").crop((262, 536, 474, 1238))
    return im


def quantise(img):
    """RGBA -> band index map (0 outer,1 mid,2 core,3 dark,4 accent), -1 empty."""
    a = np.array(img).astype(int)
    r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    idx = np.full(al.shape, -1)
    solid = al > 90
    lum = .3 * r + .59 * g + .11 * b
    core = solid & (lum > 205) & (np.minimum(np.minimum(r, g), b) > 150)
    mid = solid & ~core & (g > 175) & (b > 190)
    outer = solid & ~core & ~mid & (b > 170) & (g > 90) & (r < g)
    dark = solid & ~core & ~mid & ~outer
    idx[dark] = 3
    idx[outer] = 0
    idx[mid] = 1
    idx[core] = 2
    return idx


def flame_frames(length, width, boost):
    src = flame_source()
    # keep the aspect the source flame has, fitted to the frame
    sc = Image.new("RGBA", src.size)
    sc.alpha_composite(src)
    base = sc.resize((width, length), Image.BOX)
    idx0 = quantise(base)
    frames = []
    rng = random.Random(7)
    n = len(TICKS)
    for f in range(n):
        smear = TICKS[f] == 1
        L = length
        W = width
        stretch = 1.0 + (0.18 if smear else [0, .04, 0, -.04, .03, -.02, .02][f])
        Ls = min(FRAME_H - HEAD_Y - 1, int(L * stretch))
        Ws = int(W * (.8 if smear else 1.0))
        tmp = Image.fromarray(((idx0 + 1) * 40).astype(np.uint8)).resize((Ws, Ls), Image.NEAREST)
        idx = np.rint(np.array(tmp) / 40.0).astype(int) - 1
        # shimmer: horizontal row shift
        out = np.full((FRAME_H, FRAME_W), -1)
        for y in range(Ls):
            amp = 0 if y < 12 else (1 if y < 80 else 2)
            sh = int(round(amp * math.sin(y * .33 + f * 1.7))) if not smear else 0
            x0 = (FRAME_W - Ws) // 2 + sh
            row = idx[y]
            for x in range(Ws):
                if row[x] >= 0 and 0 <= x0 + x < FRAME_W:
                    out[HEAD_Y + y, x0 + x] = row[x]
        if smear:
            out[out == 0] = 1          # hot, narrow, bright
        # tip sparks in the accent colour
        if not smear:
            for _ in range(3):
                y = HEAD_Y + int(Ls * rng.uniform(.55, 1.0))
                row = np.nonzero(out[min(y, FRAME_H - 1)] >= 0)[0]
                if len(row):
                    x = row[rng.randrange(len(row))]
                    out[min(y, FRAME_H - 1), x] = 4
        frames.append(out)
    return frames


def paint(idx):
    cols = [BANDS[k] for k in ("outer", "mid", "core", "dark", "accent")]
    img = np.zeros((FRAME_H, FRAME_W, 4), np.uint8)
    for i, c in enumerate(cols):
        img[idx == i] = (*c, 255)
    return Image.fromarray(img)


def codex_flame_frames(path):
    """Codex's flame strip (7 cells, 1:4, flat green) -> 7 band-index maps at
    48 x 192, snapped to the five exhaust bands so the skin remap shader
    (ExhaustRemap) finds its colours exactly."""
    im = Image.open(path).convert("RGB")
    n = 7
    w = im.width // n
    cols = np.array([BANDS[k] for k in ("outer", "mid", "core", "dark", "accent")], float)
    out = []
    for i in range(n):
        cell = im.crop((i * w, 0, (i + 1) * w, im.height))
        keyed = key_green(cell)
        arr = np.array(keyed.resize((FRAME_W, FRAME_H), Image.BOX)).astype(float)
        idx = np.full((FRAME_H, FRAME_W), -1)
        solid = arr[..., 3] > 110
        d = ((arr[..., None, :3] - cols[None, None]) ** 2).sum(-1)
        idx[solid] = d.argmin(-1)[solid]
        out.append(idx)
    return out


def write_exhaust():
    atlas = Image.open(ATLAS).convert("RGBA")
    for x0, length, boost, cf in ((PLUME_X, 150, False, "flame_cruise.png"), (BOOST_X, 186, True, "flame_boost.png")):
        cpath = os.path.join(CODEX, cf)
        frames = codex_flame_frames(cpath) if os.path.exists(cpath) and "--no-codex" not in sys.argv \
            else flame_frames(length, 44, boost)
        for f, idx in enumerate(frames):
            cell = paint(idx)
            # clear then paste (replace, so a rerun is idempotent)
            box = (x0 + f * STRIDE, ATLAS_Y_TOP, x0 + f * STRIDE + FRAME_W, ATLAS_Y_TOP + FRAME_H)
            atlas.paste(Image.new("RGBA", (FRAME_W, FRAME_H), (0, 0, 0, 0)), box)
            atlas.alpha_composite(cell, (box[0], box[1]))
    atlas.save(ATLAS, optimize=True)


# ------------------------------------------------------------------ main --


def measure(hull):
    """Rect (in the cell, union of the art over all poses), nozzles, emitters."""
    return None


def main():
    hull = Hull()
    if os.path.exists(os.path.join(CODEX, "hull_state0.png")) and "--no-codex" not in sys.argv:
        sheet, rest0 = codex_sheet()
        hull.rest = rest0
        hull.alpha = np.array(rest0)[..., 3]
        # Codex's hull, fitted: map source-pixel coordinates of the reference
        # layout (the PIL rest placement) onto it for nozzles / emitters
        print("hull from Codex sheets")
    else:
        sheet = build_sheet(hull)
    os.makedirs(HULLS, exist_ok=True)
    sheet.save(os.path.join(HULLS, KEY + ".png"), optimize=True)

    # sprite rect: union over every cell, padded 1 px
    arr = np.array(sheet)[..., 3]
    x0 = y0 = 10 ** 6
    x1 = y1 = -1
    for s in range(STATES):
        for c in range(COLS):
            ys, xs = np.nonzero(arr[s * CELL:(s + 1) * CELL, c * CELL:(c + 1) * CELL])
            x0, y0 = min(x0, int(xs.min())), min(y0, int(ys.min()))
            x1, y1 = max(x1, int(xs.max()) + 1), max(y1, int(ys.max()) + 1)
    x0, y0, x1, y1 = max(0, x0 - 1), max(0, y0 - 1), min(CELL, x1 + 1), min(CELL, y1 + 1)
    w, h = x1 - x0, y1 - y0
    world_w = .34                      # the "old sprite" width every hull is normalised from
    ppu = w / world_w
    rest_alpha = hull.alpha

    # nozzles: source (x, bottom y of the nozzle) -> rect pixels, snapped just
    # inside the lip on painted pixels
    nozzle_src = [(367.5, 540, 1.5), (65, 552, .55), (670, 552, .55)]
    nozzles = []
    for sx, sy, k in nozzle_src:
        cx, cy = hull.to_cell(sx, sy)
        px = int(round(cx))
        py = int(cy) - 3
        while rest_alpha[py, px] < 200:
            py -= 1
        nozzles.append(((px + .5 - x0), (y1 - (py + .5)), k))

    # damage emitters (canvas units = cell px / 2, y down)
    def u(sx, sy):
        cx, cy = hull.to_cell(sx, sy)
        return round(cx / 2, 1), round(cy / 2, 1)

    E = []
    for kind, p, st in (("sparks", (205, 372), 1), ("smoke", (367, 520), 1), ("smolder", (206, 374), 1),
                        ("arc", (665, 430), 2), ("flame", (545, 500), 2), ("leak", (300, 470), 2),
                        ("smolder", (664, 430), 2), ("smolder", (545, 498), 2), ("smolder", (560, 330), 2),
                        ("smolder", (366, 200), 2), ("smolder", (120, 400), 2)):
        E.append((st, kind, *u(*p)))
    # every emitter must sit on paint in every pose it is drawn in: nudge one
    # that doesn't to the nearest point that does (eroded by 2 px), then assert
    import itertools

    def on_paint(st, uu, vv, margin=2):
        for c, s_ in itertools.product(range(COLS), range(st, STATES)):
            dyu = IDLE[c][0] if c < 6 else 0
            bank = -1 if c == 6 else (1 if c == 7 else 0)
            x, y = uu, vv
            if bank:
                d = x - 64
                near = (d < 0) == (bank < 0)
                k = .80 if near else .95
                x = 64 + d * k + 1.5 * bank
                y += (1.2 if near else -.6) * abs(d) / 60
            y += dyu
            px, py = int(c * CELL + x * 2), int(s_ * CELL + y * 2)
            if arr[py - margin:py + margin + 1, px - margin:px + margin + 1].min() <= 200:
                return False
        return True

    settled = []
    for st, kind, uu, vv in E:
        best = None
        for r in range(0, 16):
            for dx, dy in itertools.product(range(-r, r + 1), repeat=2):
                if max(abs(dx), abs(dy)) != r:
                    continue
                if on_paint(st, uu + dx, vv + dy):
                    best = (round(uu + dx, 1), round(vv + dy, 1))
                    break
            if best:
                break
        assert best, (kind, uu, vv)
        settled.append((st, kind, *best))
    E = settled

    data = dict(rect=[x0, CELL - y1, w, h], ppu=round(ppu, 4),
                nozzles=[(round(a, 1), round(b, 1), k) for a, b, k in nozzles], emitters=E)

    # skins
    os.makedirs(SKINS_DIR, exist_ok=True)
    rest = sheet.crop((0, 0, CELL, CELL))
    skins = []
    for sk in SKINS:
        rc = recolour(sheet, sk["mode"])
        assert (np.array(rc)[..., 3] == np.array(sheet)[..., 3]).all()
        buf = io.BytesIO()
        rc.save(buf, "PNG", optimize=True)
        with open(os.path.join(SKINS_DIR, f"{KEY}_{sk['name']}.bytes"), "wb") as fh:
            fh.write(buf.getvalue())
        hue, shadow, high = skin_primary(rest, rc.crop((0, 0, CELL, CELL)))
        skins.append(dict(name=sk["name"], hue=hue, shadow=shadow, highlight=high))
    data["skins"] = skins
    stock_hue = skin_primary(rest, recolour(rest, "gold"))   # unused, informational
    data["stockHue"] = ["#D8232C", "#86121F", "#FF5B45"]

    write_exhaust()
    with open(os.path.join(HERE, "data.json"), "w") as fh:
        json.dump(data, fh, indent=1)
    print(json.dumps(data, indent=1))

    if "--preview" in sys.argv:
        d = sys.argv[sys.argv.index("--preview") + 1]
        os.makedirs(d, exist_ok=True)
        bg = Image.new("RGBA", sheet.size, (14, 20, 36, 255))
        bg.alpha_composite(sheet)
        bg.save(os.path.join(d, "paragon_sheet.png"))
        row = Image.new("RGBA", (CELL * 4, CELL), (14, 20, 36, 255))
        row.alpha_composite(sheet.crop((0, 0, CELL, CELL)), (0, 0))
        for i, sk in enumerate(SKINS):
            row.alpha_composite(recolour(sheet, sk["mode"]).crop((0, 0, CELL, CELL)), ((i + 1) * CELL, 0))
        row.save(os.path.join(d, "paragon_skins.png"))


if __name__ == "__main__":
    main()
