#!/usr/bin/env python3
"""Rail mines, Paragon-standard redraw: build the four state frames (Dormant, Waking, Charging, Burst) of
each approved world's mine from its ONE approved dormant drawing (approved/<world>_mine.png, ~96 px,
clamp on its left), by script, and install them into the mine atlases (RailMineArt.cs):

    Resources/Enemies/Mines/rail_mines_neon.png          1254 x 1254, rows Space / Frost / Verdant / Ember
    Resources/Enemies/Mines/rail_mines_neon_tide.png     1254 x 314,  one row
    Resources/Enemies/Mines/rail_mines_neon_storm.png    1254 x 314,  one row
    Resources/Enemies/Mines/rail_mines_neon_verdant_lit.png  1254 x 298, Verdant's row, core and veins lit

Desert (rail_mines_neon_desert.png, desert_mine_codex.png) is NOT touched (it is being regenerated).

Every state is composed at the drawing's own pixel grid (so the glow is as chunky as the art), then the
whole frame is scaled NEAREST so the dormant drawing is as wide as the old dormant frame's (~255 px at
384 PPU, ~0.66 u). Glow layers (behind and in front of the drawing) get an envelope in atlas space: zero
left of the clamp's outer face (the clamp keeps gripping the rail, nothing reaches past it) and fading
to nothing before the frame's rect edge (no frame is clipped). PNG bytes are overwritten in place, so
the .meta GUIDs stay. The script prints the rects, pivots, ClampReach and core offsets for RailMineArt.cs
and measures them the way the Unity tests do.

    python3 build_mines.py [--sheets DIR] [--dry]
"""
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.abspath(os.path.join(HERE, "..", "..", "..", "..", ".."))
MINES = os.path.join(ASSETS, "Art", "Resources", "Enemies", "Mines")
PPU = 384.0

# world -> drawing, cue hue, core (source px), core radius, target drawn width (the old dormant frame's), emissive rule
W = {
    "space":   dict(cue=318, core=(67.5, 48.5), rc=7.0, width=249, emis=("hue", 265, 335, .35, .35)),
    "frost":   dict(cue=195, core=(64.0, 48.0), rc=6.5, width=257, emis=("hue", 170, 205, .25, .88)),
    "verdant": dict(cue=135, core=(57.5, 48.5), rc=6.5, width=257, emis=("hue", 55, 105, .55, .72)),
    "ember":   dict(cue=32,  core=(64.5, 47.5), rc=7.0, width=258, emis=("hue", 0, 50, .6, .55)),
    "tide":    dict(cue=188, core=(64.0, 47.0), rc=8.0, width=240, emis=("hue", 160, 200, .3, .36)),
    "storm":   dict(cue=258, core=(57.5, 49.0), rc=7.5, width=255, emis=("storm",)),
}
ORDER = ["space", "frost", "verdant", "ember", "tide", "storm"]

# Row bands (unchanged from the old atlases): y, height in each file.
BANDS = {"space": (0, 308), "frost": (308, 286), "verdant": (594, 298), "ember": (892, 362), "tide": (0, 314), "storm": (0, 314)}
FILES = {"space": "rail_mines_neon", "frost": "rail_mines_neon", "verdant": "rail_mines_neon", "ember": "rail_mines_neon",
         "tide": "rail_mines_neon_tide", "storm": "rail_mines_neon_storm"}
ATLAS_W = 1254
MARGIN = 6          # transparent gutter left of the clamp face inside every rect
FADE = 14           # atlas px over which glow fades out toward a rect edge


# ---------------------------------------------------------------------------------------------- colour helpers

def hsv_arr(rgb):
    """rgb float [0,1] HxWx3 -> h deg, s, v"""
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1); mn = rgb.min(-1); d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rr = m & (mx == r); gg = m & (mx == g) & ~rr; bb = m & ~rr & ~gg
    h[rr] = ((g - b)[rr] / d[rr]) % 6
    h[gg] = ((b - r)[gg] / d[gg]) + 2
    h[bb] = ((r - g)[bb] / d[bb]) + 4
    h = h * 60.0
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    return h, s, mx


def hsv(h, s, v):
    import colorsys
    return np.array(colorsys.hsv_to_rgb((h % 360) / 360.0, s, v))


def lerp(a, b, t):
    return a + (b - a) * t


# ---------------------------------------------------------------------------------------------- the drawing

def key_drawing(path):
    """Hard alpha; close any edge the canvas cut off (outline pixel past the border). Returns float RGBA on a
    canvas padded by 2 px."""
    im = np.array(Image.open(path).convert("RGBA")).astype(np.float64) / 255.0
    im[..., 3] = (im[..., 3] >= .5).astype(float)
    im[im[..., 3] == 0, :3] = 0
    h, w = im.shape[:2]
    P = 2
    out = np.zeros((h + 2 * P, w + 2 * P, 4))
    out[P:P + h, P:P + w] = im
    a = im[..., 3] > 0
    # the outline colour: the darkest common colour on the silhouette's rim
    rim = a & ~ndimage.binary_erosion(a, border_value=0)
    lum = im[..., :3].sum(-1)
    rim_px = im[rim & (lum < np.percentile(lum[rim], 30))][:, :3]
    ink = np.append(np.median(rim_px, axis=0), 1.0)
    cut = 0
    for (ys, xs, dy, dx) in [(np.arange(h), 0, 0, -1), (np.arange(h), w - 1, 0, 1)]:
        for y in ys:
            if a[y, xs]:
                out[P + y + dy, P + xs + dx] = ink; cut += 1
    for (y0, dy) in [(0, -1), (h - 1, 1)]:
        for x in range(w):
            if a[y0, x]:
                out[P + y0 + dy, P + x] = ink; cut += 1
    # round the closed edge's ends: an ink pixel next to each new run's end
    return out, P, cut


def emissive_mask(d, rule, core):
    rgb, a = d[..., :3], d[..., 3] > 0
    h, s, v = hsv_arr(rgb)
    if rule[0] == "hue":
        _, lo, hi, smin, vmin = rule
        m = a & (h >= lo) & (h <= hi) & (s >= smin) & (v >= vmin)
    else:   # storm: the violet glints plus the white of the coil and the clamp's lamp
        yy, xx = np.mgrid[0:d.shape[0], 0:d.shape[1]]
        coil = (np.abs(xx - core[0]) <= 10) & (np.abs(yy - core[1]) <= 13)
        lamp = (xx >= 24) & (xx <= 35) & (yy >= 45) & (yy <= 57)   # (canvas px: source + 2)
        m = a & (((h >= 230) & (h <= 290) & (s >= .3) & (v >= .5) & (coil | lamp)) | ((v >= .8) & (s <= .35) & (coil | lamp)))
    return m


# ---------------------------------------------------------------------------------------------- states

def quant(alpha, steps=4):
    return np.round(np.clip(alpha, 0, 1) * steps) / steps


def over(dst, src):
    """straight-alpha src over dst (both HxWx4)"""
    sa = src[..., 3:4]; da = dst[..., 3:4]
    oa = sa + da * (1 - sa)
    oc = np.where(oa > 0, (src[..., :3] * sa + dst[..., :3] * da * (1 - sa)) / np.maximum(oa, 1e-6), 0)
    return np.concatenate([oc, oa], -1)


def build_states(world, cfg, drawing, P, lit=False):
    """Returns per state (back_fx, body, front_fx) at the source grid on a padded canvas, and the canvas origin
    offset (where the drawing's (0,0) sits)."""
    cue = cfg["cue"]
    PADL, PADR, PADV = 4, 70, 40
    H, Wd = drawing.shape[:2]
    canvas = (H + 2 * PADV, Wd + PADL + PADR)
    ox, oy = PADL, PADV
    base = np.zeros(canvas + (4,)); base[oy:oy + H, ox:ox + Wd] = drawing
    cx, cy = cfg["core"][0] + P + ox, cfg["core"][1] + P + oy
    rc = cfg["rc"]
    sil = base[..., 3] > 0
    em = np.zeros(canvas, bool); em[oy:oy + H, ox:ox + Wd] = emissive_mask(drawing, cfg["emis"], (cfg["core"][0] + P, cfg["core"][1] + P))
    yy, xx = np.mgrid[0:canvas[0], 0:canvas[1]].astype(float)
    dx, dy = xx + .5 - cx, yy + .5 - cy
    dist = np.hypot(dx, dy)
    ang = np.arctan2(dy, dx)
    left_face = np.argmax(sil.any(0))           # the clamp's outer face (source px)
    body_r = float(np.percentile(dist[sil & (xx > cx - 2)], 97))   # the body's reach around the core
    out_d = ndimage.distance_transform_edt(~sil)   # distance outside the silhouette

    C = hsv(cue, .85, 1.0); C2 = hsv(cue, .66, 1.0); HOT = hsv(cue, .22, 1.0); WHITE = np.ones(3)
    deep = hsv(cue, .9, .75)
    rng = np.random.default_rng({"space": 1, "frost": 2, "verdant": 3, "ember": 4, "tide": 5, "storm": 6}[world])

    # lit veins (Verdant's lit variant): the vines, any green, glow cue green
    veins = np.zeros(canvas, bool)
    if lit:
        h, s, v = hsv_arr(base[..., :3])
        veins = sil & (h >= 55) & (h <= 135) & (s >= .3) & (v >= .25)

    def light(body, mask, amount, hot_share):
        """push the masked pixels toward the cue glow (brighter, the brightest ones toward HOT)"""
        if not mask.any():
            return body
        h, s, v = hsv_arr(body[..., :3])
        tgt = np.where((v >= np.percentile(v[mask], 100 - hot_share * 100))[..., None], HOT, C2)
        body[mask, :3] = lerp(body[mask, :3], tgt[mask], amount)
        return body

    def core(body, radius, heat):
        """paint the glowing core: white-hot centre, cue rings. heat 0..1"""
        r = dist / radius
        m = r < 1.0
        col = np.where((r < .22 + .2 * heat)[..., None], HOT,
                       np.where((r < .55 + .1 * heat)[..., None], C2, C))
        t = np.clip(1.15 - r, 0, 1)[..., None] * (.75 + .25 * heat)
        t = np.where((r < .62)[..., None], 1.0, t)
        body[m, :3] = lerp(body[m, :3], col[m], t[m])
        body[m, 3] = 1.0
        return body

    def glow(alpha, colour):
        g = np.zeros(canvas + (4,)); g[..., :3] = colour; g[..., 3] = quant(alpha); return g

    def zap(layer, r0, a0, a1, colour, jitter=.7, hot=None):
        """a jagged 1 px arc around the core from angle a0 to a1 (rad) at radius r0"""
        n = int(abs(a1 - a0) * r0 * 1.6) + 2
        pts = []
        for i in range(n):
            a = a0 + (a1 - a0) * i / (n - 1)
            rr = r0 + rng.uniform(-jitter, jitter)
            pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
        img = Image.new("L", (canvas[1], canvas[0]), 0)
        ImageDraw.Draw(img).line(pts, fill=255, width=1)
        m = np.array(img) > 0
        layer[m, :3] = colour; layer[m, 3] = 1.0
        if hot is not None:
            k = m & (rng.random(canvas) < .2)
            layer[k, :3] = hot
        return layer

    states = []
    for state in range(4):
        body = base.copy()
        back = np.zeros(canvas + (4,)); front = np.zeros(canvas + (4,))
        if state == 0:
            if lit:
                body = light(body, veins, .55, .25)
                body = light(body, em, .5, .3)
                body = core(body, rc * .9, .3)
        elif state == 1:   # WAKING: the core and the lugs light up
            body = light(body, em, .6, .35)
            if lit:
                body = light(body, veins, .65, .3)
            body = core(body, rc, .55)
            halo = (out_d > 0) & (out_d <= 2) & ndimage.binary_dilation(em | (dist < rc), iterations=3)
            back = over(back, glow(halo * .5, C))
            front = over(front, glow(np.clip(1 - dist / (rc * 1.9), 0, 1) * .5 * (dist > rc * .95), C))
        elif state == 2:   # CHARGING: brighter core, glow halo, arcs
            body = light(body, em, .85, .5)
            if lit:
                body = light(body, veins, .8, .45)
            body = core(body, rc * 1.3, .8)
            halo = np.clip(1 - out_d / 11.0, 0, 1) * .62 * (out_d > 0)
            back = over(back, glow(halo, C))
            front = over(front, glow(np.clip(1 - dist / (rc * 2.4), 0, 1) * .55 * (dist > rc * 1.25), C))
            # arcs: jagged loops of charge round the body, off the clamp side
            for (r0, a0, a1) in [(body_r + 3, -1.9, -.5), (body_r + 5, .25, 1.7), (body_r + 2.5, -.25, .45),
                                 (body_r + 7, -1.2, -.2), (body_r + 6, .9, 1.95)]:
                front = zap(front, r0, a0, a1, C, hot=HOT)
            # sparks
            for _ in range(14):
                a = rng.uniform(-2.0, 2.0); rr = body_r + rng.uniform(4, 12)
                px, py = int(cx + rr * math.cos(a)), int(cy + rr * math.sin(a))
                if 0 <= py < canvas[0] and 0 <= px < canvas[1]:
                    front[py, px] = np.append(HOT if rng.random() < .4 else C, 1.0)
        else:              # BURST: a big radial flare from the core
            body = light(body, em, 1.0, .7)
            if lit:
                body = light(body, veins, 1.0, .6)
            # back: a wide bloom and the halo
            bloom = np.clip(1 - dist / (body_r + 22), 0, 1) ** .8 * .7
            back = over(back, glow(np.maximum(bloom, np.clip(1 - out_d / 12.0, 0, 1) * .7 * (out_d > 0)), C))
            # front: tint the body toward the glow, the white-hot core, rays, ring
            front = over(front, glow(np.clip(1 - dist / (body_r + 2), 0, 1) ** 1.3 * .55, C2))
            rays = np.zeros(canvas)
            rcol = np.zeros(canvas)
            for k in range(12):
                a = k * math.pi / 6 + math.pi / 12 * (k % 2) * 0
                if math.cos(a) < -.45:
                    continue        # no ray back over the clamp: it keeps gripping the rail, visibly
                long = k % 2 == 0
                L = (body_r + 34) if long else (body_r + 14)
                w0 = 4.2 if long else 2.6
                da = np.angle(np.exp(1j * (ang - a)))
                along = dist * np.cos(da); perp = np.abs(dist * np.sin(da))
                inside = (along > 0) & (perp < w0 * np.clip(1 - along / L, 0, 1) + .35 * (along < L * .2)) & (along < L)
                rays = np.maximum(rays, inside * 1.0)
                rcol = np.maximum(rcol, inside * np.clip(1 - perp / max(w0 * .45, .6), 0, 1) * np.clip(1 - along / (L * .75), 0, 1))
            rl = np.zeros(canvas + (4,))
            rl[..., :3] = np.where((rcol > .3)[..., None], WHITE, np.where((rcol > .05)[..., None], HOT, C))
            rl[..., 3] = rays
            front = over(front, rl)
            ring = (np.abs(dist - (body_r + 9)) < 1.0) & (np.cos(ang * 9) > -.4)
            rg = glow(ring * .75, C2)
            front = over(front, rg)
            cr = np.clip(1 - dist / (rc * 2.6), 0, 1)
            cg = np.zeros(canvas + (4,))
            cg[..., :3] = np.where((cr > .55)[..., None], WHITE, np.where((cr > .3)[..., None], HOT, C2))
            cg[..., 3] = (cr > .12) * 1.0
            front = over(front, cg)
            for _ in range(24):
                a = rng.uniform(-2.2, 2.2); rr = body_r + rng.uniform(6, 26)
                px, py = int(cx + rr * math.cos(a)), int(cy + rr * math.sin(a))
                if 0 <= py < canvas[0] and 0 <= px < canvas[1]:
                    front[py, px] = np.append(HOT if rng.random() < .5 else C, 1.0)
        # the white-hot point: the core's centre is the one brightest spot of the frame (RailMineLaserTest measures the laser's
        # origin as the brightest pixels near the body), everything else is capped a little below it
        for lay in (back, body, front):
            sm = lay[..., :3].sum(-1, keepdims=True)
            lay[..., :3] *= np.where(sm > 2.82, 2.82 / np.maximum(sm, 1e-6), 1.0)
        if state > 0:
            ring = dist <= (1.6 if state == 1 else 2.2)
            front[ring, :3] = hsv(cue, .05, 1.0); front[ring, 3] = 1.0
            front[dist <= 1.2, :3] = WHITE; front[dist <= 1.2, 3] = 1.0
        # nothing of the glow reaches left of the clamp's face
        back[:, :left_face + 1, 3] = 0
        front[:, :left_face, 3] = 0
        states.append((back, body, front))
    return states, (ox + P, oy + P), (cx, cy)


# ---------------------------------------------------------------------------------------------- scale + place

def to_img(arr):
    return Image.fromarray(np.clip(np.round(arr * 255), 0, 255).astype(np.uint8), "RGBA")


def scale_layers(layers, s):
    out = []
    for lay in layers:
        im = to_img(lay)
        w, h = im.size
        out.append(np.array(im.resize((int(round(w * s)), int(round(h * s))), Image.NEAREST)).astype(np.float64) / 255.0)
    return out


def bbox(alpha, thr):
    ys, xs = np.nonzero(alpha > thr)
    return xs.min(), xs.max(), ys.min(), ys.max()


def compose_cell(back, body, front, rect, place):
    """place the scaled layers into a rect-sized cell. place = (dx, dy) of the scaled canvas in the cell."""
    rx, ry, rw, rh = rect
    cell_b = np.zeros((rh, rw, 4)); cell_d = np.zeros((rh, rw, 4)); cell_f = np.zeros((rh, rw, 4))
    dx, dy = place
    H, Wd = body.shape[:2]
    for src, dst in ((back, cell_b), (body, cell_d), (front, cell_f)):
        x0, y0 = max(0, dx), max(0, dy)
        x1, y1 = min(rw, dx + Wd), min(rh, dy + H)
        dst[y0:y1, x0:x1] = src[y0 - dy:y1 - dy, x0 - dx:x1 - dx]
    # the drawing must fit whole
    assert (body[..., 3] > 0).sum() == (cell_d[..., 3] > 0).sum(), ("drawing clipped", rect)
    yy, xx = np.mgrid[0:rh, 0:rw]
    m = np.minimum(np.minimum(xx, rw - 1 - xx), np.minimum(yy, rh - 1 - yy)).astype(float)
    env = np.clip((m - 2) / FADE, 0, 1)
    for f in (cell_b, cell_f):
        f[..., 3] = quant(f[..., 3] * env, 4)
    out = over(over(cell_b, cell_d), cell_f)
    out[out[..., 3] <= 0, :3] = 0
    return out


# ---------------------------------------------------------------------------------------------- measuring (as the tests do)

def measure_cell(cell, piv):
    """cell RGBA float, piv in cell coords. Returns dict of the tests' measures."""
    a = (cell[..., 3] * 255).round()
    rh, rw = a.shape
    px, py = piv
    res = {}
    # RailMineArtTest.ClampOffset: leftmost alpha>=200 within 40 rows of the pivot
    y0, y1 = max(0, int(py) - 40), min(rh, int(py) + 40)
    cols = np.nonzero((a[y0:y1] >= 200).any(0))[0]
    res["clamp200"] = cols.min() - px
    # RailsVettingTest: leftmost alpha>40 anywhere -> reach
    cols = np.nonzero((a > 40).any(0))[0]
    res["reach_px"] = px - cols.min()
    res["opaque40"] = int((a > 40).sum())
    border = np.zeros_like(a, bool); border[0] = border[-1] = True; border[:, 0] = border[:, -1] = True
    res["onBorder40"] = int(((a > 40) & border).sum())
    res["edge128"] = float(((a >= 128) & border).sum() / (2 * rw + 2 * rh))
    xs = np.nonzero((a > 128).any(0))[0]; ys = np.nonzero((a > 128).any(1))[0]
    res["width_u"] = (xs.max() - xs.min() + 1) / PPU
    res["long_u"] = max(xs.max() - xs.min() + 1, ys.max() - ys.min() + 1) / PPU
    # Signature buckets
    rgb = cell[..., :3]
    h, s, v = hsv_arr(rgb)
    lit = (a >= 230) & (s >= .6) & (v >= .6)
    b = {"cyan": lit & (h >= 175) & (h < 200), "blue": lit & (h >= 200) & (h < 245), "mint": lit & (h >= 140) & (h < 175),
         "lime": lit & (h >= 60) & (h < 140), "orange": lit & (h < 45), "violet": lit & (h >= 245) & (h < 335)}
    res["neon"] = {k: int(m.sum()) for k, m in b.items()}
    # HostileGlow.IsPlayerRed share, opaque>40
    op = a > 40
    red = op & (s > .35) & (v > .25) & ((h < 28) | (h > 332))
    res["red"] = float(red.sum() / max(1, op.sum()))
    return res


def core_top25(cell, piv):
    """RailMineLaserTest.MeasuredCorePx's top-25 rule for one frame: brightest r+g+b in x piv+5..+70, y piv-30..+30 (alpha
    ignored, as GetPixel's colour)"""
    px, py = int(piv[0]), int(piv[1])
    win = cell[py - 30:py + 30, px + 5:px + 70]
    lum = (np.round(win[..., :3] * 255) / 255).sum(-1)
    flat = lum.ravel()
    order = np.argsort(-flat, kind="stable")[:25]
    ys, xs = np.unravel_index(order, lum.shape)
    ties = int((flat >= flat[order[-1]] - 1e-9).sum())
    cxp = (xs + px + 5).mean(); cyp = (ys + py - 30).mean()
    return cxp - piv[0], piv[1] - cyp, ties


# ---------------------------------------------------------------------------------------------- main

def main():
    sheets = None
    if "--sheets" in sys.argv:
        sheets = sys.argv[sys.argv.index("--sheets") + 1]
    dry = "--dry" in sys.argv
    built = {}
    for world in ORDER:
        cfg = W[world]
        d, P, cut = key_drawing(os.path.join(HERE, "approved", world + "_mine.png"))
        states, org, corec = build_states(world, cfg, d, P)
        dw = bbox(d[..., 3], .5); drawn_w = dw[1] - dw[0] + 1
        s = cfg["width"] / drawn_w
        sc = [scale_layers(st, s) for st in states]
        lit = None
        if world == "verdant":
            lst, _, _ = build_states(world, cfg, d, P, lit=True)
            lit = [scale_layers(st, s) for st in lst]
        built[world] = dict(states=sc, lit=lit, s=s, cut=cut)
        print(f"{world}: closed {cut} cut edge px, scale {s:.3f}")

    # column cuts per file row: dormant, waking, charging tight round their content, burst gets the rest
    results = {}
    atlases = {}
    cuts = {}
    for world in ORDER:
        b = built[world]
        band_y, band_h = BANDS[world]
        body0 = b["states"][0][1]
        L, R, T, B = bbox(body0[..., 3], .5)
        piv_c = ((L + R + 1) / 2.0, (T + B + 1) / 2.0)     # in scaled-canvas px (pixel edges)
        widths = []
        for c in range(3):
            back, body, front = b["states"][c]
            alpha = np.maximum(np.maximum(back[..., 3], body[..., 3]), front[..., 3])
            l2, r2, _, _ = bbox(alpha, 0)
            widths.append(MARGIN + (r2 - L + 1) + 4)
        widths.append(ATLAS_W - sum(widths))
        cuts[world] = widths
    # Tide's and Storm's one-row files share their cuts (RailMineArtTest: Storm's rects are Tide's)
    shared = [max(cuts["tide"][c], cuts["storm"][c]) for c in range(3)]
    cuts["tide"] = cuts["storm"] = shared + [ATLAS_W - sum(shared)]
    for world in ORDER:
        b = built[world]
        band_y, band_h = BANDS[world]
        body0 = b["states"][0][1]
        L, R, T, B = bbox(body0[..., 3], .5)
        piv_c = ((L + R + 1) / 2.0, (T + B + 1) / 2.0)
        widths = cuts[world]
        results[world] = dict(rects=[], pivots=[], cells=[], litcells=[])
        x = 0
        for c in range(4):
            rect = (x, band_y, widths[c], band_h)
            dx = MARGIN - L
            dy = int(round(band_h / 2.0 - piv_c[1]))
            cell = compose_cell(*b["states"][c], rect, (dx, dy))
            piv = (piv_c[0] + dx, piv_c[1] + dy)      # cell coords
            results[world]["rects"].append(rect)
            results[world]["pivots"].append((piv[0] + x, piv[1] + band_y))
            results[world]["cells"].append((cell, piv))
            if b["lit"] is not None:
                results[world]["litcells"].append((compose_cell(*b["lit"][c], rect, (dx, dy)), piv))
            x += widths[c]

    # ---- write atlases
    for fname, size in [("rail_mines_neon", (1254, 1254)), ("rail_mines_neon_tide", (1254, 314)),
                        ("rail_mines_neon_storm", (1254, 314)), ("rail_mines_neon_verdant_lit", (1254, 298))]:
        atlases[fname] = np.zeros((size[1], size[0], 4))
    for world in ORDER:
        A = atlases[FILES[world]]
        for (cell, _), rect in zip(results[world]["cells"], results[world]["rects"]):
            rx, ry, rw, rh = rect
            A[ry:ry + rh, rx:rx + rw] = cell
        if results[world]["litcells"]:
            for (cell, _), rect in zip(results[world]["litcells"], results[world]["rects"]):
                rx, ry, rw, rh = rect
                atlases["rail_mines_neon_verdant_lit"][0:rh, rx:rx + rw] = cell

    # ---- report + measure
    report = {}
    for world in ORDER:
        r = results[world]
        meas = [measure_cell(cell, piv) for cell, piv in r["cells"]]
        cores = [core_top25(cell, piv) for cell, piv in r["cells"][1:3]]
        core = ((cores[0][0] + cores[1][0]) / 2, (cores[0][1] + cores[1][1]) / 2)
        report[world] = dict(rects=r["rects"], pivots=r["pivots"], core=core, core_ties=[c[2] for c in cores], meas=meas)
        print(f"\n== {world}")
        for c in range(4):
            m = meas[c]
            print(f"  {['dormant','waking','charging','burst'][c]:8s} rect {r['rects'][c]} pivot {r['pivots'][c]} reach {m['reach_px']:.1f}px "
                  f"({m['reach_px']/PPU:.3f}u) clamp200 {m['clamp200']:.1f} opaque40 {m['opaque40']} onBorder {m['onBorder40']} edge {m['edge128']:.3f} "
                  f"w {m['width_u']:.3f}u long {m['long_u']:.3f}u red {m['red']:.3f} neon {m['neon']}")
        print(f"  core px from pivot (x lane-ward, y up): ({core[0]:.1f}, {core[1]:.1f}) ties {report[world]['core_ties']} per-frame {[(round(c[0],1), round(c[1],1)) for c in cores]}")
    if results["verdant"]["litcells"]:
        lit = atlases["rail_mines_neon_verdant_lit"]
        a = lit[..., 3] >= .5
        h, s, v = hsv_arr(lit[..., :3])
        pink = a & (v > .25) & (s * v >= .035) & (h >= 280) & (h < 352)
        green = a & (s > .25) & (v > .5) & (np.abs(((h - 125 + 180) % 360) - 180) <= 25)
        print(f"\nverdant lit: opaque {a.sum()} pink {pink.sum()} green {green.sum()} ({green.sum()/a.sum():.1%})")

    with open(os.path.join(HERE, "mines_measure.json"), "w") as f:
        json.dump({k: dict(rects=[[int(i) for i in r] for r in v["rects"]], pivots=[[float(i) for i in p] for p in v["pivots"]], core=[float(i) for i in v["core"]]) for k, v in report.items()}, f, indent=1)

    if not dry:
        for fname, A in atlases.items():
            to_img(A).save(os.path.join(MINES, fname + ".png"), optimize=True)
            print("wrote", fname)
    if sheets:
        os.makedirs(sheets, exist_ok=True)
        contact(results, sheets)


def contact(results, out):
    """Every world's 4 frames (left-hand, as authored) over a checker, and a mirrored right-hand copy with the pivot (cross) and the
    clamp face (line) marked."""
    Z = 2
    bgc = (34, 36, 46, 255)
    for world in ORDER + (["verdant_lit"] if results["verdant"]["litcells"] else []):
        r = results["verdant" if world == "verdant_lit" else world]
        cells = r["litcells"] if world == "verdant_lit" else r["cells"]
        tw = sum(c.shape[1] for c, _ in cells) + 10 * 5
        th = max(c.shape[0] for c, _ in cells)
        sheet = Image.new("RGBA", ((tw) * Z, (2 * th + 30) * Z), bgc)
        dr = ImageDraw.Draw(sheet)
        x = 10
        for i, (cell, piv) in enumerate(cells):
            im = to_img(cell)
            rw, rh = im.size
            # left-hand
            bgimg = Image.new("RGBA", im.size, (52, 56, 70, 255)); bgimg.alpha_composite(im)
            sheet.paste(bgimg.resize((rw * Z, rh * Z), Image.NEAREST), (x * Z, 0))
            # right-hand: flipX mirrors the sprite about its pivot
            fl = im.transpose(Image.FLIP_LEFT_RIGHT)
            fpx = rw - piv[0]
            bg2 = Image.new("RGBA", im.size, (52, 56, 70, 255)); bg2.alpha_composite(fl)
            oy = (th + 30) * Z
            sheet.paste(bg2.resize((rw * Z, rh * Z), Image.NEAREST), (x * Z, oy))
            a = np.array(im)[..., 3]
            cols = np.nonzero((a > 40).any(0))[0]
            face_fl = rw - cols.min()       # clamp face on the mirrored copy (right side), pixel edge
            for (yo, px, face) in [(0, piv[0], cols.min()), (oy, fpx, face_fl)]:
                X = (x + px) * Z; Y = yo + piv[1] * Z
                dr.line([(X - 10, Y), (X + 10, Y)], fill=(255, 255, 0, 255), width=2)
                dr.line([(X, Y - 10), (X, Y + 10)], fill=(255, 255, 0, 255), width=2)
                FX = (x + face) * Z
                dr.line([(FX, yo), (FX, yo + rh * Z)], fill=(255, 60, 60, 255), width=1)
            dr.rectangle([x * Z, 0, (x + rw) * Z - 1, rh * Z - 1], outline=(90, 90, 110, 255))
            dr.rectangle([x * Z, oy, (x + rw) * Z - 1, oy + rh * Z - 1], outline=(90, 90, 110, 255))
            x += rw + 10
        sheet.save(os.path.join(out, f"mine_{world}.png"))
    print("sheets in", out)


if __name__ == "__main__":
    main()
