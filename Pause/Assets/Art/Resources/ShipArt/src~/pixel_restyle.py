#!/usr/bin/env python3
"""Builds a player ship's whole art set from ONE approved pixel-art drawing (top-down, nose up), by script:

    python3 pixel_restyle.py <Key> [--preview DIR]

Reads   ShipArt/<Key>/src~/pixel/config.json + the drawing it names (e.g. approved.png, 128 or 160 px).
Writes  <Key>.png              hull sheet 2304 x 768 (9 columns x 3 damage rows, 256 px cells)
        <Key>_<Skin>.bytes     the 4 non-stock skins (PNG bytes), recoloured from the new drawing
        <Key>_data.json        rect + ppu, nozzles (+ per-column offsets), emitters, damageNote, painted
        <Key>_rotors.json      (only if config has "rotors")
Then run bake_silhouettes.py <Key> (shield + hitbox) and pixel_check.py <Key> (test rules).
The flame sheet (<Key>_flame.png) is kept: its strips are mounted at the nozzles measured here.

Everything is painted on the DRAWING's own texel grid (damage, lights off, canopy glints, skin recolours,
livery patterns) and only then upscaled nearest-neighbour into the cell, so every frame keeps the drawing's
pixel grain. The scale is the largest that keeps the sprite rect's aspect within ~6% of the ship's
balanced size (ShipArtTest.oldRects) and leaves room for the 4 px idle bob.

Columns (ShipHullArt): 0 key, 1 bob up, 2 bob up lights off, 3 glint 1, 4 glint 2 (lights off), 5 bob down,
6 bank left, 7 bank right (ShipDamageTable.Posed applied as a warp: the side leaned into is squeezed to .80 and
dips, the far side .95, 1.5 u shift; a spinner keeps its idle geometry), 8 hit flash (bone-white silhouette).
Rows: 0 intact, 1 damaged, 2 wreck; the damage is one texel layer per state applied to every column, so it
sits on the same panels in every frame and every skin; alpha is identical in all rows and skins.
"""
import colorsys
import io
import json
import math
import os
import random
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import shiplib  # noqa: E402

CELL, COLS, ROWS = 256, 9, 3
IDLE_DY = [0, -2, -2, 0, 0, 2]                 # ShipDamageTable.idleDy (canvas units, 1 u = 2 px)
OLD_RECTS = {1: (32, 29), 2: (28, 23), 3: (29, 28), 4: (46, 57), 5: (47, 55), 6: (56, 55), 7: (51, 55), 8: (28, 27),
             9: (20, 29), 10: (30, 22), 11: (30, 30), 12: (24, 30), 13: (26, 26), 14: (20, 24), 15: (22, 29), 16: (34, 29)}
ASPECT_SLACK = 1.06                            # rect w/h may run this much wider than the balanced aspect (test: 10%)
BANK_SHADE = 0.12
INK = (6, 8, 20)


# ---------------------------------------------------------------- colour helpers

def hsv_arrays(rgb):
    f = rgb.astype(np.float64) / 255.0
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    mx, mn = f.max(-1), f.min(-1)
    v = mx
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-9), 0)
    d = np.maximum(mx - mn, 1e-9)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60.0
    h = np.where(mx - mn < 1e-9, 0, h)
    return h, s, v


def hex_rgb(h):
    return np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)], np.float64)


def lum(rgb):
    f = rgb.astype(np.float64)
    return 0.299 * f[..., 0] + 0.587 * f[..., 1] + 0.114 * f[..., 2]


def hue_dist(a, b):
    d = np.abs(a - b) % 360
    return np.minimum(d, 360 - d)


# ---------------------------------------------------------------- drawing analysis

def ellipse_mask(shape, el):
    yy, xx = np.mgrid[0:shape[0], 0:shape[1]]
    m = np.zeros(shape, bool)
    for cx, cy, rx, ry in el:
        m |= ((xx + .5 - cx) / rx) ** 2 + ((yy + .5 - cy) / ry) ** 2 <= 1
    return m


def clean(draw):
    """Drop specks (components under 4 texels) and force binary alpha."""
    a = draw[..., 3] >= 128
    lab, n = ndimage.label(a, structure=np.ones((3, 3)))
    sizes = ndimage.sum(a, lab, range(1, n + 1))
    keep = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s >= 4])
    out = draw.copy()
    out[~keep] = 0
    out[keep, 3] = 255
    return out


class Drawing:
    def __init__(self, cfg, img):
        self.cfg = cfg
        self.rgba = clean(img)
        if cfg.get("fill_holes"):
            # enclosed see-through gaps (a halo ring) cannot sit inside one hitbox outline (ShipHitboxTest wants the
            # hull polygon >= 80 % of the body): close them with a dark recessed field instead
            a = self.rgba[..., 3] > 0
            hole = ndimage.binary_fill_holes(a) & ~a
            if cfg["fill_holes"] == "nearest":
                # pinholes in the drawing: take the colour of the nearest painted texel
                _, (iy, ix) = ndimage.distance_transform_edt(~a, return_indices=True)
                self.rgba[hole] = self.rgba[iy[hole], ix[hole]]
                self.rgba[hole, 3] = 255
            else:
                self.rgba[hole] = (*cfg["fill_holes"], 255)
        self.N = self.rgba.shape[0]
        self.sil = self.rgba[..., 3] > 0
        ys, xs = np.nonzero(self.sil)
        self.bbox = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
        self.cx = (xs.min() + xs.max() + 1) / 2.0
        rgb = self.rgba[..., :3]
        h, s, v = hsv_arrays(rgb)
        self.h, self.s, self.v = h, s, v
        self.canopy = ellipse_mask(self.sil.shape, cfg["canopy"]) & self.sil & (v > .35)
        # armour = the hull's identity hue family (the ramp a skin repaints)
        if "armour_hue" in cfg:
            ah = cfg["armour_hue"]
        else:
            sel = self.sil & ~self.canopy & (s > .3) & (v > .25)
            hist, edges = np.histogram(h[sel], bins=36, range=(0, 360))
            ah = (edges[hist.argmax()] + 5)
        self.armour_hue = ah
        tol = cfg.get("armour_tol", 30)
        smin = cfg.get("armour_smin", .22)
        self.armour = self.sil & ~self.canopy & (hue_dist(h, ah) <= tol) & (s >= smin) & (v >= cfg.get("armour_vmin", .12))
        for ex in cfg.get("armour_exclude", []):
            self.armour &= ~ellipse_mask(self.sil.shape, [ex])
        # running lights: small, bright, saturated spots off the armour hue (red / amber / cyan / star sparks)
        lightish = self.sil & ~self.canopy & (v > .7) & (s > .4) & (hue_dist(h, ah) > 35)
        lab, n = ndimage.label(lightish, structure=np.ones((3, 3)))
        sizes = ndimage.sum(lightish, lab, range(1, n + 1))
        self.lights = np.isin(lab, [i + 1 for i, sz in enumerate(sizes) if sz <= cfg.get("light_max", 40)])
        for ex in cfg.get("light_exclude", []):
            self.lights &= ~ellipse_mask(self.sil.shape, [ex])
        self.dist = ndimage.distance_transform_edt(np.pad(self.sil, 1))[1:-1, 1:-1]


# ---------------------------------------------------------------- texel-level edits

def lights_off(rgb, d):
    out = rgb.astype(np.float64)
    out[d.lights] *= .38
    if not d.lights.any():          # nothing blinks: the canopy glow dips instead
        out[d.canopy] *= .78
    return np.clip(out, 0, 255).astype(np.uint8)


def glint(rgb, d, which):
    """A diagonal light sweep across the canopy: glint 1 on its left half, glint 2 (fainter) on the right."""
    out = rgb.astype(np.float64)
    ys, xs = np.nonzero(d.canopy)
    if not len(xs):
        return rgb
    cx, cy = xs.mean(), ys.mean()
    w = max(2.0, (xs.max() - xs.min() + 1) / 2.0)
    yy, xx = np.mgrid[0:d.N, 0:d.N]
    t = (xx + .5 - cx) + (yy + .5 - cy) * .55
    centre = -w * .45 if which == 1 else w * .45
    band = d.canopy & (np.abs(t - centre) <= max(1.0, w * .28))
    k = .7 if which == 1 else .42
    out[band] = out[band] * (1 - k) + np.array([236, 250, 255]) * k
    return np.clip(out, 0, 255).astype(np.uint8)


def recolour(rgb, d, ramp, mask=None):
    """Repaint the armour texels onto a skin ramp (deep, shadow, base, highlight, top) by their luminance
    rank inside the stock armour, so the drawing's shading survives in the new colour."""
    m = d.armour if mask is None else mask
    out = rgb.astype(np.float64).copy()
    L = lum(rgb)
    ref = np.sort(L[d.armour])
    if not len(ref) or not m.any():
        return rgb
    rank = np.searchsorted(ref, L[m]) / float(len(ref))
    stops = np.array([0.0, .2, .6, .92, 1.0])
    cols = np.array(ramp)
    res = np.zeros((rank.size, 3))
    for c in range(3):
        res[:, c] = np.interp(rank, stops, cols[:, c])
    out[m] = res
    return np.clip(out + .5, 0, 255).astype(np.uint8)


def remap_hue(img, spec):
    """config "hue_remap": [[h_lo, h_hi, new_lo, new_hi, s_min], ...]: texels whose hue lies in [h_lo, h_hi]
    (and saturation >= s_min) get their hue mapped linearly onto [new_lo, new_hi]; saturation and value (the
    shading ramp) are kept. Used to pull a glass colour away from the no-pink band (WorldCue.IsPink)."""
    out = img.copy()
    rgb = img[..., :3].astype(np.float64) / 255.0
    h, s, v = hsv_arrays(img[..., :3])
    a = img[..., 3] > 0
    for lo, hi, nlo, nhi, smin in spec:
        m = a & (h >= lo) & (h <= hi) & (s >= smin)
        nh = nlo + (h[m] - lo) * (nhi - nlo) / max(1e-9, hi - lo)
        res = np.array([colorsys.hsv_to_rgb((hh % 360) / 360.0, ss, vv) for hh, ss, vv in zip(nh, s[m], v[m])])
        if len(res):
            out[m, :3] = np.clip(res * 255 + .5, 0, 255).astype(np.uint8)
    return out


def ramp_of(base, shadow, high):
    b, s, h = hex_rgb(base), hex_rgb(shadow), hex_rgb(high)
    deep = s * .45
    return [deep, s, b, b * .45 + h * .55, h]


def livery_mask(d, pattern):
    yy, xx = np.mgrid[0:d.N, 0:d.N].astype(np.float64) + .5
    x0, y0, x1, y1 = d.bbox
    H = y1 - y0
    W = x1 - x0
    cx = d.cx
    if pattern == "split":
        m = xx >= cx
    elif pattern == "racing":
        u = np.abs(xx - cx)
        m = (u >= W * .045) & (u <= W * .11)
    elif pattern == "chevrons":
        t = (yy - y0) - np.abs(xx - cx) * .7
        per = max(8.0, H / 5.0)
        m = ((t % per) < per * .32) & (yy > y0 + H * .2)
    elif pattern == "diagonal":
        t = (xx - x0) + (yy - y0)
        per = max(10.0, H / 4.0)
        m = (t % per) < per * .35
    else:
        m = np.zeros(d.sil.shape, bool)
    return m & d.armour


# ---------------------------------------------------------------- damage (one texel layer per state)

class Damage:
    """Per state: `mul` (soot / scorch multiplier) and `paint` (rgb, mask) laid over any variant of the drawing."""

    def __init__(self, d, seed):
        self.d = d
        rng = random.Random(seed)
        self.rng = rng
        N = d.N
        sc = N / 128.0
        x0, y0, x1, y1 = d.bbox
        W, H = x1 - x0, y1 - y0
        cand = d.sil & ~d.canopy & (d.dist >= 4 * sc)
        if cand.sum() < 50:
            cand = d.sil & ~d.canopy & (d.dist >= 3)
        for nz in d.cfg["nozzles"]:
            cand &= ~ellipse_mask(d.sil.shape, [(nz[0], nz[1], 6 * sc, 6 * sc)])
        yy, xx = np.mgrid[0:N, 0:N]

        def pick(xr, yr, avoid=(), mind=10 * sc):
            m = cand & (xx >= x0 + xr[0] * W) & (xx <= x0 + xr[1] * W) & (yy >= y0 + yr[0] * H) & (yy <= y0 + yr[1] * H)
            for ax, ay in avoid:
                m &= (xx - ax) ** 2 + (yy - ay) ** 2 >= mind ** 2
            if not m.any():
                m = cand.copy()
                for ax, ay in avoid:
                    m &= (xx - ax) ** 2 + (yy - ay) ** 2 >= (mind * .6) ** 2
            ys_, xs_ = np.nonzero(m)
            score = np.minimum(d.dist[ys_, xs_], 5 * sc) + np.array([rng.random() * 3.0 for _ in range(len(xs_))])
            i = int(score.argmax())
            return int(xs_[i]), int(ys_[i])

        sites = d.cfg.get("sites", {})
        self.A = tuple(sites.get("A") or pick((.08, .42), (.38, .82)))
        self.B = tuple(sites.get("B") or pick((.6, .92), (.3, .7), [self.A]))
        self.C = tuple(sites.get("C") or pick((.58, .95), (.55, .92), [self.A, self.B]))
        self.D = tuple(sites.get("D") or pick((.35, .65), (.6, .95), [self.A, self.B, self.C]))
        self.E = tuple(sites.get("E") or pick((.1, .45), (.12, .45), [self.A, self.B, self.C, self.D]))
        self.sc = sc
        self.layers = {1: self._state(1), 2: self._state(2)}

    def _noise(self, seed, cell):
        r = np.random.RandomState(seed)
        n = self.d.N
        small = r.rand(n // cell + 2, n // cell + 2)
        big = np.array(Image.fromarray((small * 255).astype(np.uint8)).resize(((n // cell + 2) * cell,) * 2, Image.BICUBIC))
        return big[:n, :n].astype(np.float64) / 255.0

    def _state(self, st):
        d, sc = self.d, self.sc
        N = d.N
        mul = np.ones((N, N))
        prgb = np.zeros((N, N, 3))
        pm = np.zeros((N, N), bool)
        yy, xx = np.mgrid[0:N, 0:N].astype(np.float64) + .5
        rng = random.Random(hash((st, self.A)) & 0xffff)
        sil = d.sil

        def put(mask, col):
            m = mask & sil
            prgb[m] = col
            pm[m] = True

        def scorch(c, r, k):
            rr = np.sqrt((xx - c[0] - .5) ** 2 + (yy - c[1] - .5) ** 2) / r
            f = np.clip(1 - rr, 0, 1) ** .7
            mul[:] = np.minimum(mul, 1 - k * f)

        def blown(c, r, hot=False):
            jag = self._noise(rng.randrange(1 << 30), 3)
            rr = np.sqrt(((xx - c[0] - .5) / 1.0) ** 2 + ((yy - c[1] - .5) / .8) ** 2) / r
            hole = rr + (jag - .5) * .5 < 1
            rim = (rr + (jag - .5) * .5 < 1.35) & ~hole
            put(hole, (20, 15, 22))
            ribs = hole & ((np.floor(yy) % 3) == 0) & (np.abs(xx - c[0] - .5) < r * .7)
            put(ribs, (70, 64, 76))
            put(rim & (jag > .55), (255, 138, 40) if hot else (96, 82, 78))
            put(rim & (jag <= .55), (40, 32, 34))
            # sparking wires
            for _ in range(3):
                a = rng.uniform(0, 2 * math.pi)
                ln = rng.uniform(.3, .8) * r
                for t in np.linspace(0, ln, 6):
                    px, py = int(c[0] + math.cos(a) * t), int(c[1] + math.sin(a) * t)
                    if 0 <= px < N and 0 <= py < N and sil[py, px]:
                        prgb[py, px] = (255, 214, 92) if t > ln * .6 else (150, 150, 160)
                        pm[py, px] = True
            scorch(c, r * 2.3, .55)

        def crack(c, length, ang):
            x, y = c[0] + .5, c[1] + .5
            for _ in range(int(length)):
                ang += rng.uniform(-.6, .6)
                x += math.cos(ang)
                y += math.sin(ang)
                px, py = int(x), int(y)
                if 0 <= px < N and 0 <= py < N and sil[py, px]:
                    prgb[py, px] = (14, 10, 16)
                    pm[py, px] = True
                    if rng.random() < .25 and px + 1 < N and sil[py, px + 1]:
                        prgb[py, px + 1] = (60, 30, 24)
                        pm[py, px + 1] = True

        def embers(c, r, n):
            for _ in range(n):
                a = rng.uniform(0, 2 * math.pi)
                t = rng.uniform(0, r)
                px, py = int(c[0] + math.cos(a) * t), int(c[1] + math.sin(a) * t)
                if 0 <= px < N and 0 <= py < N and sil[py, px]:
                    prgb[py, px] = rng.choice([(255, 96, 24), (255, 170, 40), (255, 228, 120), (200, 40, 16)])
                    pm[py, px] = True

        # soot patches (state 1 light, state 2 heavy) on the hull, never on the lit canopy in state 1
        soot = self._noise(1234 + st, max(4, int(6 * sc)))
        if st == 1:
            m = sil & ~d.canopy & (soot > .80)
            mul[m] = np.minimum(mul[m], .62)
        else:
            m = sil & (soot > d.cfg.get("wreck_soot", .6))   # lower = more of the hull sooted (small 1x drawings)
            mul[m] = np.minimum(mul[m], d.cfg.get("wreck_mul", .48) + (1 - soot[m]) * .3)
            edge = sil & (d.dist <= 2.2) & (self._noise(77, 3) > .45)
            mul[edge] = np.minimum(mul[edge], .45)

        r = 4.6 * sc
        blown(self.A, r * (1.0 if st == 1 else 1.35), hot=st == 2)
        crack(self.A, 9 * sc, rng.uniform(0, 6.28))
        crack(self.A, 7 * sc, rng.uniform(0, 6.28))
        scorch(self.B, 4.5 * sc, .5)
        crack(self.B, 8 * sc, rng.uniform(0, 6.28))
        if st == 2:
            blown(self.B, r * .9, hot=True)
            blown(self.C, r * 1.1, hot=True)
            blown(self.E, r * .85)
            scorch(self.D, 6.5 * sc, .6)
            embers(self.D, 4.5 * sc, int(26 * sc))
            embers(self.C, 3 * sc, int(12 * sc))
            crack(self.C, 10 * sc, rng.uniform(0, 6.28))
            crack(self.E, 9 * sc, rng.uniform(0, 6.28))
            # cracked canopy
            ys, xs = np.nonzero(d.canopy)
            if len(xs):
                cc = (int(xs.mean()), int(ys.mean()))
                for a in (.6, 2.4, 4.3):
                    crack(cc, max(4, (xs.max() - xs.min()) * .5), a)
        return mul, prgb, pm

    def apply(self, rgb, st):
        if st == 0:
            return rgb
        mul, prgb, pm = self.layers[st]
        out = rgb.astype(np.float64) * mul[..., None]
        out[pm] = prgb[pm]
        return np.clip(out + .5, 0, 255).astype(np.uint8)


# ---------------------------------------------------------------- cell geometry

class Geometry:
    def __init__(self, d, cfg, sid):
        self.d = d
        ow, oh = OLD_RECTS[sid]
        a = ow / oh
        x0, y0, x1, y1 = d.bbox
        bw, bh = x1 - x0, y1 - y0
        self.R = a * (ASPECT_SLACK if a < .97 else 1.0) * cfg.get("aspect_mul", 1.0)
        s = min(cfg.get("max_scale", 2.0), (CELL * self.R - 6) / bw, (CELL - 14) / bh)
        if cfg.get("integer_scale"):
            # whole-number nearest-neighbour factor only (crisp texels); the rect pads out to the balanced aspect
            s = max(1, int(math.floor(min(cfg.get("max_scale", 4), (CELL * min(self.R, 1.0) - 6) / bw, (CELL * min(1.0, 1.0 / self.R) - 14) / bh) + 1e-9)))
        self.s = s
        self.ox = int(round(128 - (x0 + bw / 2.0) * s))
        self.oy = int(round(128 - (y0 + bh / 2.0) * s))
        X = np.arange(CELL)
        self.sx = np.floor((X + .5 - self.ox) / s).astype(int)
        self.sy = np.floor((X + .5 - self.oy) / s).astype(int)
        self.spinner = bool(cfg.get("spinner"))

    def up(self, rgba):
        """drawing (N x N RGBA) -> 256 cell, nearest on the drawing's grid"""
        N = rgba.shape[0]
        okx = (self.sx >= 0) & (self.sx < N)
        oky = (self.sy >= 0) & (self.sy < N)
        out = np.zeros((CELL, CELL, 4), np.uint8)
        sub = rgba[np.clip(self.sy, 0, N - 1)][:, np.clip(self.sx, 0, N - 1)]
        m = oky[:, None] & okx[None, :]
        out[m] = sub[m]
        out[out[..., 3] == 0] = 0
        return out

    def to_cell(self, tx, ty):
        """texel centre -> cell px (x, y top-down)"""
        return self.ox + (tx + .5) * self.s, self.oy + (ty + .5) * self.s

    @staticmethod
    def shift(cell, dy):
        out = np.zeros_like(cell)
        if dy < 0:
            out[:dy] = cell[-dy:]
        elif dy > 0:
            out[dy:] = cell[:-dy]
        else:
            out = cell.copy()
        return out

    def bank(self, cell, b):
        """ShipDamageTable.Posed as an image warp (nearest): output (x, y) <- source."""
        if self.spinner:
            out = cell.copy()
            xs = np.arange(CELL)
            f = 1 - .08 * np.clip((xs - 128.0) * b / 110.0, 0, 1)
            out[..., :3] = np.clip(out[..., :3] * f[None, :, None] + .5, 0, 255).astype(np.uint8)
            out[out[..., 3] == 0] = 0
            return out
        ys, xs = np.mgrid[0:CELL, 0:CELL].astype(np.float64)
        dout = xs + .5 - 128.0 - 3.0 * b
        near = (dout < 0) == (b < 0)
        k = np.where(near, .80, .95)
        d = dout / k
        dv = np.where(near, 1.2, -.6) * np.abs(d) / 60.0      # px (2 * units * |d_u| / 60 = c * |d_px| / 60)
        sx = np.floor(128.0 + d).astype(int)
        sy = np.floor(ys + .5 - dv).astype(int)
        ok = (sx >= 0) & (sx < CELL) & (sy >= 0) & (sy < CELL)
        out = np.zeros_like(cell)
        out[ok] = cell[sy[ok], sx[ok]]
        sh = near & (out[..., 3] > 0)
        rgb = out[..., :3].astype(np.float64)
        rgb[sh] *= (1 - BANK_SHADE)
        out[..., :3] = np.clip(rgb + .5, 0, 255).astype(np.uint8)
        out[out[..., 3] == 0] = 0
        return out

    def posed_point(self, x, y, col):
        """cell px point (top-down) moved like ShipDamageTable.Posed for column `col`"""
        u, v = x / 2.0, y / 2.0
        b = -1 if col == 6 else (1 if col == 7 else 0)
        if self.spinner and b:
            b = 0
        if b:
            dd = u - 64
            near = (dd < 0) == (b < 0)
            k = .8 if near else .95
            u = 64 + dd * k + 1.5 * b
            v += (1.2 if near else -.6) * abs(dd) / 60.0
        dy = IDLE_DY[col] if col < 6 else 0
        return u * 2, (v + dy) * 2


def hit_flash(cell):
    """Bone-white silhouette washed from the drawn frame (ink and a hint of the panel lines survive)."""
    rgb = cell[..., :3]
    hull = cell[..., 3] > 0
    peak = rgb.astype(np.int16).max(axis=2)
    out = np.zeros_like(cell)
    out[hull, :3] = (244, 234, 212)
    out[hull & (peak < 45), :3] = INK
    out[hull & (peak >= 45) & (peak < 85), :3] = (92, 88, 91)
    out[hull & (peak >= 85) & (peak < 120), :3] = (175, 164, 151)
    out[hull & (peak > 235), :3] = (255, 250, 232)
    out[..., 3] = cell[..., 3]
    return out


def build_rows(geo, d, base_rgb, dmg, flash_from=None):
    """3 x 9 cells for one colourway (stock or a skin) from its intact texel colours."""
    a = d.rgba[..., 3:4]
    rows = []
    for st in range(3):
        rgb = dmg.apply(base_rgb, st)
        def tex(x):
            return np.concatenate([x, a], -1)
        key = geo.up(tex(rgb))
        off = geo.up(tex(lights_off(rgb, d)))
        g1 = geo.up(tex(glint(rgb, d, 1)))
        g2 = geo.up(tex(glint(lights_off(rgb, d), d, 2)))
        cells = [key, geo.shift(key, -4), geo.shift(off, -4), g1, g2, geo.shift(key, 4),
                 geo.bank(key, -1), geo.bank(key, 1),
                 hit_flash(key) if flash_from is None else flash_from[st]]
        rows.append(cells)
    return rows


def sheet_of(rows):
    out = np.zeros((CELL * ROWS, CELL * COLS, 4), np.uint8)
    for st in range(ROWS):
        for c in range(COLS):
            out[st * CELL:(st + 1) * CELL, c * CELL:(c + 1) * CELL] = rows[st][c]
    return out


def png_bytes(arr):
    buf = io.BytesIO()
    Image.fromarray(arr, "RGBA").save(buf, "PNG", optimize=True)
    return buf.getvalue()


# ---------------------------------------------------------------- measuring

def measure_rect(sheet, geo):
    al = sheet[..., 3] > 0
    x0 = y0 = 10 ** 6
    x1 = y1 = -1
    for st in range(ROWS):
        for c in range(COLS):
            ys, xs = np.nonzero(al[st * CELL:(st + 1) * CELL, c * CELL:(c + 1) * CELL])
            x0, y0, x1, y1 = min(x0, xs.min()), min(y0, ys.min()), max(x1, xs.max() + 1), max(y1, ys.max() + 1)
    pad = geo.d.cfg.get("rect_pad", 0)              # extra room so the hull stays clear of the rails
    w0, h0 = x1 - x0 + 2 + pad, y1 - y0 + 2 + pad
    R = geo.R
    if w0 / h0 > R:
        w, h = w0, int(math.ceil(w0 / R))
    else:
        w, h = int(math.ceil(h0 * R)), h0
    w, h = min(w, CELL), min(h, CELL)
    cx, cy = (x0 + x1) / 2.0, (y0 + y1) / 2.0
    rx = int(round(cx - w / 2.0))
    ry = int(round(cy - h / 2.0))
    rx = max(0, min(CELL - w, rx))
    ry = max(0, min(CELL - h, ry))
    assert rx <= x0 and rx + w >= x1 and ry <= y0 and ry + h >= y1, "art does not fit the rect"
    return int(rx), int(ry), int(w), int(h)          # top-down


def alpha_at(sheet, st, col, x, y):
    xi, yi = col * CELL + int(x), st * CELL + int(y)
    if not (0 <= xi < sheet.shape[1] and 0 <= yi < sheet.shape[0]):
        return 0
    return sheet[yi, xi, 3]


def emitter_ok(sheet, geo, st, u, v, margin=2):
    for col in range(COLS):
        cc = 0 if (geo.spinner and col in (6, 7)) else col
        px, py = geo.posed_point(u * 2, v * 2, cc)
        for s in range(st, ROWS):
            for dx in (-margin, 0, margin):
                for dy in (-margin, 0, margin):
                    if alpha_at(sheet, s, col, px + dx, py + dy) < 204:
                        return False
    return True


def place_emitters(sheet, geo, d, dmg, kinds):
    """emitters at the damage sites (texel coords), nudged until on paint in every pose"""
    sites = {1: [dmg.A, dmg.B, dmg.A], 2: [dmg.C, dmg.D, dmg.E, dmg.B, dmg.A, dmg.D, dmg.C, dmg.E, dmg.B]}
    order = {"Sparks": 0, "Smoke": 1, "Smolder": 2, "Arc": 0, "Flame": 1, "Leak": 2}
    out = []
    used = []
    per_state = {1: 0, 2: 0}
    for st, kind in kinds:
        lst = sites[st]
        if st == 1:
            base = lst[order[kind]]
        else:
            base = lst[order[kind]] if kind != "Smolder" else lst[min(len(lst) - 1, 3 + per_state[2] % 6)]
            if kind == "Smolder":
                per_state[2] += 1
        best = None
        for r in range(0, 30):
            for dx in range(-r, r + 1):
                for dy in range(-r, r + 1):
                    if max(abs(dx), abs(dy)) != r:
                        continue
                    tx, ty = base[0] + dx * .5, base[1] + dy * .5
                    x, y = geo.to_cell(tx, ty)
                    u, v = round(x / 2.0, 1), round(y / 2.0, 1)
                    if any(abs(u - a) < 1.5 and abs(v - b) < 1.5 and s == st for s, _, a, b in used):
                        continue
                    if emitter_ok(sheet, geo, st, u, v):
                        best = (u, v)
                        break
                if best:
                    break
            if best:
                break
        assert best, (kind, base)
        used.append((st, kind, best[0], best[1]))
        out.append(dict(state=st, kind=kind, u=best[0], v=best[1]))
    return out


def near_opaque(sheet, col, rect, x, y, R):
    rx, ry, w, h = rect      # top-down rect
    # point in sprite-rect px, y bottom-up -> cell top-down
    cx = rx + x
    cy = ry + h - y
    import math as m
    n = int(m.ceil(R))
    for yy in range(int(m.floor(cy)) - n, int(m.floor(cy)) + n + 1):
        for xx in range(int(m.floor(cx)) - n, int(m.floor(cx)) + n + 1):
            if not (rx <= xx < rx + w and ry <= yy < ry + h):
                continue
            if (xx + .5 - cx) ** 2 + (yy + .5 - cy) ** 2 > (R + .75) ** 2:
                continue
            if sheet[yy, col * CELL + xx, 3] >= 128:
                return True
    return False


def measure_nozzles(sheet, geo, d, rect):
    rx, ry, w, h = rect
    rest = sheet[:CELL, :CELL, 3] > 0
    out = []
    for tx, ty, scale in d.cfg["nozzles"]:
        x, y = geo.to_cell(tx, ty)
        xi = int(x)
        yi = int(y)
        while yi + 1 < CELL and rest[yi + 1, xi]:      # walk down to the lip
            yi += 1
        while not rest[yi, xi]:
            yi -= 1
        cy = yi - 1.0                                  # just inside the lip
        cx = xi + .5
        nx, ny = cx - rx, (ry + h) - (cy + .5)
        offs = []
        for col in range(COLS):
            if col == 0 or col == 8:
                offs += [0.0, 0.0]
                continue
            cc = 0 if (geo.spinner and col in (6, 7)) else col
            px, py = geo.posed_point(cx, cy + .5, cc)
            offs += [round(px - cx, 1), round(-(py - (cy + .5)), 1)]
        out.append(dict(x=round(nx, 1), y=round(ny, 1), scale=scale, offsets=offs))
    return out


# ---------------------------------------------------------------- main

def main():
    key = shiplib.resolve(sys.argv[1])
    sid = shiplib.ship_id(key)
    src = os.path.join(shiplib.src_dir(key), "pixel")
    cfg = json.load(open(os.path.join(src, "config.json")))
    img = np.array(Image.open(os.path.join(src, cfg["drawing"])).convert("RGBA"))
    if cfg.get("hue_remap"):
        img = remap_hue(img, cfg["hue_remap"])
    d = Drawing(cfg, img)
    geo = Geometry(d, cfg, sid)
    dmg = Damage(d, cfg.get("seed", sid))
    data = shiplib.load_data(key)
    print("%s: drawing %dpx, bbox %s, scale %.3f, armour hue %.0f, %d armour / %d light / %d canopy texels"
          % (key, d.N, d.bbox, geo.s, d.armour_hue, d.armour.sum(), d.lights.sum(), d.canopy.sum()))

    stock_rgb = d.rgba[..., :3].copy()
    rows = build_rows(geo, d, stock_rgb, dmg)
    sheet = sheet_of(rows)
    for st in (1, 2):
        assert (sheet[st * CELL:(st + 1) * CELL, :, 3] == sheet[:CELL, :, 3]).all()
    Image.fromarray(sheet, "RGBA").save(shiplib.hull_path(key), optimize=True)
    flash = [rows[st][8] for st in range(3)]

    # skins (config "skin_ramps": {name: [deep, shadow, base, light, top]} overrides the ramp built from the data
    # file's three skin colours, e.g. to give a pale skin real shadows; "skin_tints": {name: [[h_lo, h_hi, s_min,
    # "#RRGGBB"], ...]} multiplies non-armour trim of that hue toward a deeper accent so it reads on the new ramp)
    skins = data["skins"]
    for sk in skins[1:]:
        if sk["name"] in cfg.get("skin_ramps", {}):
            rgb = recolour(stock_rgb, d, [hex_rgb(c) for c in cfg["skin_ramps"][sk["name"]]])
        elif sk["kind"] == "Special":
            rgb = recolour(stock_rgb, d, ramp_of(sk["hue"], sk["shadow"], sk["highlight"]))
            lm = livery_mask(d, sk["pattern"])
            rgb = recolour(rgb, d, ramp_of(sk["accent"], sk["accentShadow"], sk["accentHighlight"]), lm)
        else:
            rgb = recolour(stock_rgb, d, ramp_of(sk["hue"], sk["shadow"], sk["highlight"]))
        for lo, hi, smin, col in cfg.get("skin_tints", {}).get(sk["name"], []):
            m = d.sil & ~d.armour & ~d.canopy & ~d.lights & (hue_dist(d.h, (lo + hi) / 2.0) <= (hi - lo) / 2.0) & (d.s >= smin)
            f = rgb[m].astype(np.float64) / 255.0
            rgb[m] = np.clip(f * hex_rgb(col) + .5, 0, 255).astype(np.uint8)
        sk_sheet = sheet_of(build_rows(geo, d, rgb, dmg, flash))
        assert (sk_sheet[..., 3] == sheet[..., 3]).all()
        with open(shiplib.skin_path(key, sk["name"]), "wb") as fh:
            fh.write(png_bytes(sk_sheet))
        print("  skin", sk["name"], sk["kind"], sk.get("pattern", ""))

    rect = measure_rect(sheet, geo)
    rx, ry, w, h = rect
    ow, oh = OLD_RECTS[sid]
    ppu = math.sqrt((w * 100.0 / ow) * (h * 100.0 / oh))
    nozzles = measure_nozzles(sheet, geo, d, rect)
    kinds = [(e["state"], e["kind"]) for e in data["emitters"]]
    emitters = place_emitters(sheet, geo, d, dmg, kinds)
    data["painted"] = True
    data["rect"] = dict(x=rx, y=CELL - (ry + h), w=w, h=h, ppu=round(ppu, 4))
    data["nozzles"] = nozzles
    data["emitters"] = emitters
    data["damageNote"] = cfg.get("damageNote", data.get("damageNote", ""))
    shiplib.save_data(key, json.loads(json.dumps(data, default=lambda o: o.item())))
    if cfg.get("rotors"):
        rot = json.load(open(os.path.join(shiplib.ship_dir(key), key + "_rotors.json")))
        for r_, (tx, ty) in zip(rot["rotors"], cfg["rotors"]):
            x, y = geo.to_cell(tx - .5, ty - .5)
            r_["x"], r_["y"] = round(x, 1), round(y, 1)
            r_["scale"] = cfg.get("rotor_scale", r_["scale"])
        with open(os.path.join(shiplib.ship_dir(key), key + "_rotors.json"), "w") as fh:
            fh.write('{\n  "rotors": [\n' + ",\n".join(
                '    { "x": %s, "y": %s, "scale": %s, "speed_deg_per_s": %s, "direction": %s }'
                % (r_["x"], r_["y"], r_["scale"], r_["speed_deg_per_s"], r_["direction"]) for r_ in rot["rotors"]) + "\n  ]\n}\n")
    print("  rect", data["rect"], "nozzles", [(n["x"], n["y"]) for n in nozzles])
    print("  sites A%s B%s C%s D%s E%s" % (dmg.A, dmg.B, dmg.C, dmg.D, dmg.E))


if __name__ == "__main__":
    main()
