#!/usr/bin/env python3
"""Cuts the Space set-piece atlases from the neon pixel-art sheets in staging/.

    python3 build_atlas.py            (needs Pillow + numpy)

Reads   ../staging/{anim,fx}_pixel_v1.png   (4x4 sheets of 256 px cells)
Writes  ../../../Resources/Worlds/Space/Backdrop/{anim,fx}.{png,json}

Every cell of a sheet is one *variant* (a different planet, station...), not
a flipbook frame, and the painter did not centre the art in its cell. So each
sprite is lifted out, cleaned and put back centred in its cell, and the
manifest gets a rect that is symmetric about the art: BackdropAtlas pivots a
sprite on the middle of its rect, so a centred rect is what makes a variant
swap not hop and a rotation turn in place.

  hard sprites  (planets, moons, stations)  the background removal left a
                halo of low-alpha pure-blue pixels around them, which bilinear
                filtering and block compression smear into a blue fringe. The
                halo is cut, and the remaining soft edge pixels take their
                colour from the solid art beside them.
  glow sprites  (comets, galaxies, wisps)   keep their glow; only the alpha
                floor is subtracted so the glow fades to nothing inside the
                rect. Galaxies and wisps are centred on their core / mass (they
                spin), everything else on its bounding box.
  star/dot/streak  drawn here (the sheet only has one 256 px sparkle cluster,
                which is dropped): a pinpoint, a four-point glint and a thin
                streak, white so the director can tint them.
  mini_*        most bodies are far away and small on screen, where the 256 px
                cells would shimmer (the atlases have no mipmaps), so stations
                and rocky moons also get a properly filtered small copy.

Manifest rects use Unity's bottom-left origin.
"""
import json
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
STAGING = os.path.join(HERE, "..", "staging")
OUT = os.path.join(HERE, "..", "..", "..", "Resources", "Worlds", "Space", "Backdrop")

CELL = 256
PAD = 2                 # transparent border kept inside every rect
HARD_CUT = 48           # hard sprites: alpha below this is halo
GLOW_FLOOR = 12         # glow sprites: alpha floor subtracted
SOLID = 200             # alpha from which a pixel's colour is trusted

HARD, GLOW, DROP = "hard", "glow", "drop"
BBOX, CORE, MASS = "bbox", "core", "mass"

# (name, kind, centre-on) per cell, row-major from the top-left.
SHEETS = {
    "anim": [("giant_%02d" % i, HARD, BBOX) for i in range(12)] +
            [("rocky_%02d" % i, HARD, BBOX) for i in range(4)],
    "fx": [("ringstation_%02d" % i, HARD, BBOX) for i in range(4)] +
          [("station_%02d" % i, HARD, BBOX) for i in range(4)] +
          [("comet_00", GLOW, BBOX), ("comet_01", GLOW, BBOX),
           ("galaxy0", GLOW, CORE), ("galaxy1", GLOW, CORE),
           ("wisp0", GLOW, MASS), ("wisp1", GLOW, MASS),
           ("moon", HARD, BBOX), (None, DROP, BBOX)],
}

MINIS = [("station", 52), ("ringstation", 52), ("rocky", 48)]    # name, longest side in px
SLOT = 62               # mini grid pitch
PIECES = {}             # cleaned full-size sprites by name, for the minis

N8 = [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)]


def shift(a, dy, dx, fill):
    out = np.full_like(a, fill)
    h, w = a.shape[:2]
    ys, yd = (slice(0, h - dy), slice(dy, h)) if dy >= 0 else (slice(-dy, h), slice(0, h + dy))
    xs, xd = (slice(0, w - dx), slice(dx, w)) if dx >= 0 else (slice(-dx, w), slice(0, w + dx))
    out[yd, xd] = a[ys, xs]
    return out


def dilate(mask):
    out = mask.copy()
    for dy, dx in N8:
        out |= shift(mask, dy, dx, False)
    return out


def flood(labels, passable):
    """Grow each label through `passable` pixels until nothing changes."""
    labels = labels.copy()
    while True:
        changed = False
        for dy, dx in N8:
            cand = shift(labels, dy, dx, -1)
            take = (labels < 0) & passable & (cand >= 0)
            if take.any():
                labels[take] = cand[take]
                changed = True
        if not changed:
            return labels


def owners(alpha, cells):
    """Which cell's art each pixel belongs to. Art spills a few pixels over
    its cell border, so ownership is grown out of each cell's solid middle
    instead of being cut on the grid."""
    labels = np.full(alpha.shape, -1, np.int32)
    inset = 40
    for i in range(len(cells)):
        r, c = divmod(i, 4)
        y0, x0 = r * CELL + inset, c * CELL + inset
        core = alpha[y0:y0 + CELL - 2 * inset, x0:x0 + CELL - 2 * inset] >= 128
        labels[y0:y0 + CELL - 2 * inset, x0:x0 + CELL - 2 * inset][core] = i
    labels = flood(labels, dilate(alpha >= GLOW_FLOOR))
    # Detached glow specks (comet debris, a galaxy's outer stars) stay with
    # the cell they sit in.
    for i, (_, kind, _) in enumerate(cells):
        if kind != GLOW:
            continue
        r, c = divmod(i, 4)
        sub = labels[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]
        sub[(sub < 0) & (alpha[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL] >= GLOW_FLOOR)] = i
    return labels


def clean_hard(rgb, alpha):
    a = alpha.copy()
    a[a < HARD_CUT] = 0
    # Soft pixels that are saturated blue are halo, not art: peel them.
    r, g, b = [rgb[..., k].astype(int) for k in range(3)]
    blue = (b > 130) & (r * 2 < b) & (b - np.maximum(r, g) > 40)
    for _ in range(2):
        edge = (a > 0) & dilate(a == 0)
        a[edge & blue & (a < SOLID)] = 0
    # Keep what hangs together with the solid body (2 px gaps allowed).
    seed = np.where(a >= SOLID, 0, -1).astype(np.int32)
    keep = flood(seed, dilate(a > 0)) >= 0
    a[~keep] = 0
    # Remaining soft edge pixels take the colour of the solid art beside them.
    rgb = rgb.astype(float)
    trusted = a >= SOLID
    for _ in range(3):
        todo = (a > 0) & ~trusted
        if not todo.any():
            break
        acc = np.zeros_like(rgb)
        n = np.zeros(a.shape)
        for dy, dx in N8:
            t = shift(trusted, dy, dx, False)
            acc += shift(rgb, dy, dx, 0) * t[..., None]
            n += t
        fix = todo & (n > 0)
        rgb[fix] = acc[fix] / n[fix][:, None]
        trusted = trusted | fix
    return rgb.round().astype(np.uint8), a


def clean_glow(rgb, alpha):
    a = np.clip((alpha.astype(float) - GLOW_FLOOR) * 255.0 / (255 - GLOW_FLOOR), 0, 255)
    return rgb, a.round().astype(np.uint8)


def centre_of(rgb, a, mode):
    ys, xs = np.nonzero(a)
    if mode == BBOX:
        return (xs.min() + xs.max() + 1) / 2.0, (ys.min() + ys.max() + 1) / 2.0
    if mode == MASS:
        w = a[ys, xs].astype(float)
    else:   # CORE: the brightest few hundred pixels
        lum = rgb[ys, xs].astype(float).sum(1) * a[ys, xs]
        w = (lum >= np.sort(lum)[-400]).astype(float)
    return (xs * w).sum() / w.sum() + 0.5, (ys * w).sum() / w.sum() + 0.5


def bleed(img, rounds=8):
    """Spread colour under fully transparent pixels so filtering at a sprite
    edge blends toward the art, not toward black."""
    rgb = img[..., :3].astype(float)
    known = img[..., 3] > 0
    for _ in range(rounds):
        acc = np.zeros_like(rgb)
        n = np.zeros(known.shape)
        for dy, dx in N8:
            k = shift(known, dy, dx, False)
            acc += shift(rgb, dy, dx, 0) * k[..., None]
            n += k
        fill = ~known & (n > 0)
        rgb[fill] = acc[fill] / n[fill][:, None]
        known = known | fill
    rgb[~known] = 0
    img[..., :3] = rgb.round().astype(np.uint8)


def entry(name, x, top, w, h, sheet_h):
    return {"n": name, "x": int(x), "y": int(sheet_h - top - h), "w": int(w), "h": int(h)}


def build_sheet(sheet, cells):
    src = np.array(Image.open(os.path.join(STAGING, sheet + "_pixel_v1.png")).convert("RGBA"))
    H, W = src.shape[:2]
    alpha = src[..., 3]
    labels = owners(alpha, cells)
    out = np.zeros_like(src)
    sprites = []
    half = CELL // 2 - PAD
    for i, (name, kind, mode) in enumerate(cells):
        if kind == DROP:
            continue
        r, c = divmod(i, 4)
        own = labels == i
        rgb, a = (clean_hard if kind == HARD else clean_glow)(src[..., :3], np.where(own, alpha, 0))
        cx, cy = centre_of(rgb, a, mode)
        cx, cy = int(round(cx)), int(round(cy))
        ys, xs = np.nonzero(a)
        # Half-extents about the centre; art further out than the cell allows
        # is glow fringe and gets faded off below.
        hx = min(half, max(cx - xs.min(), xs.max() + 1 - cx))
        hy = min(half, max(cy - ys.min(), ys.max() + 1 - cy))
        piece = np.zeros((2 * hy, 2 * hx, 4), np.uint8)
        sy0, sx0 = max(0, cy - hy), max(0, cx - hx)
        sy1, sx1 = min(H, cy + hy), min(W, cx + hx)
        dy0, dx0 = sy0 - (cy - hy), sx0 - (cx - hx)
        piece[dy0:dy0 + sy1 - sy0, dx0:dx0 + sx1 - sx0, :3] = rgb[sy0:sy1, sx0:sx1]
        piece[dy0:dy0 + sy1 - sy0, dx0:dx0 + sx1 - sx0, 3] = a[sy0:sy1, sx0:sx1]
        if kind == GLOW:
            # Fade the last few pixels so a clipped glow has no hard edge.
            f = 6
            ramp_y = np.minimum(np.arange(2 * hy), np.arange(2 * hy)[::-1])
            ramp_x = np.minimum(np.arange(2 * hx), np.arange(2 * hx)[::-1])
            k = np.clip(np.minimum.outer(ramp_y, ramp_x) / float(f), 0, 1)
            piece[..., 3] = (piece[..., 3] * k).round().astype(np.uint8)
        ox, oy = c * CELL + CELL // 2 - hx, r * CELL + CELL // 2 - hy
        out[oy:oy + 2 * hy, ox:ox + 2 * hx] = piece
        PIECES[name] = piece
        sprites.append(entry(name, ox - PAD, oy - PAD, 2 * hx + 2 * PAD, 2 * hy + 2 * PAD, H))
    return out, sprites


# ---- generated pinpoints ---------------------------------------------------

PX = 2      # the sheets are painted in ~2 px blocks; match that grain


def blocks(grid):
    """A small alpha grid -> white RGBA sprite at PX scale."""
    a = np.kron(np.array(grid, np.uint8), np.ones((PX, PX), np.uint8))
    s = np.zeros(a.shape + (4,), np.uint8)
    s[..., :3] = 255
    s[..., 3] = a
    return s


def star():
    n, arm = 11, [255, 255, 170, 100, 55, 25]
    g = np.zeros((n, n), int)
    m = n // 2
    for d, v in enumerate(arm):
        for y, x in ((m - d, m), (m + d, m), (m, m - d), (m, m + d)):
            g[y, x] = v
    for y, x in ((m - 1, m - 1), (m - 1, m + 1), (m + 1, m - 1), (m + 1, m + 1)):
        g[y, x] = 70
    return blocks(g)


def dot():
    return blocks([[0, 90, 0], [90, 255, 90], [0, 90, 0]])


def streak():
    # Head at +x: the director points +x along the direction of travel.
    n = 40
    g = np.zeros((3, n), int)
    t = np.arange(n) / float(n - 1)
    g[1] = (255 * t ** 1.6).round()
    g[0, -3:] = g[2, -3:] = [60, 120, 60]
    g[1, -1] = 200
    return blocks(g)


def mini(piece, longest):
    im = Image.fromarray(piece, "RGBA").convert("RGBa")     # premultiplied: no dark edge
    k = longest / float(max(im.size))
    im = im.resize((max(1, round(im.width * k)), max(1, round(im.height * k))), Image.LANCZOS)
    m = np.array(im.convert("RGBA"))
    m[m[..., 3] < 10] = 0
    ys, xs = np.nonzero(m[..., 3])
    return m[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def put(out, sprites, name, s, x, y):
    h, w = s.shape[:2]
    out[y:y + h, x:x + w] = s
    sprites.append(entry(name, x - PAD, y - PAD, w + 2 * PAD, h + 2 * PAD, out.shape[0]))


def add_generated(out, sprites, cell_index):
    r, c = divmod(cell_index, 4)
    x0, y0 = c * CELL, r * CELL
    x = x0 + 10
    for name, s in (("star", star()), ("dot", dot()), ("streak", streak())):
        put(out, sprites, name, s, x, y0 + 10)
        x += s.shape[1] + 16
    y = y0 + 40
    for base, longest in MINIS:
        for i in range(4):
            name = "%s_%02d" % (base, i)
            s = mini(PIECES[name], longest)
            h, w = s.shape[:2]
            put(out, sprites, "mini_" + name, s, x0 + 4 + i * SLOT + (SLOT - w) // 2, y + (SLOT - h) // 2)
        y += SLOT


def main():
    for sheet, cells in SHEETS.items():
        out, sprites = build_sheet(sheet, cells)
        for i, (_, kind, _) in enumerate(cells):
            if kind == DROP:
                add_generated(out, sprites, i)
        bleed(out)
        Image.fromarray(out, "RGBA").save(os.path.join(OUT, sheet + ".png"), optimize=True)
        with open(os.path.join(OUT, sheet + ".json"), "w") as f:
            f.write('{\n  "sprites": [\n')
            f.write(",\n".join("    " + json.dumps(s, separators=(",", ":")) for s in sprites))
            f.write("\n  ]\n}\n")
        print(sheet, len(sprites), "sprites")
        for s in sprites:
            print("  %-16s %4d,%4d  %3dx%3d" % (s["n"], s["x"], s["y"], s["w"], s["h"]))


if __name__ == "__main__":
    main()
