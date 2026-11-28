#!/usr/bin/env python3
"""Frost's SOFT weather sheets: weather.png (+ .json), wx_hazard.png (+ .json), wx_ambient.png (+ .json).

    python3 build_soft_weather.py [--out <Resources/Worlds/Frost/Backdrop3>] [weather] [wx_hazard] [wx_ambient]

Why (2026-11, the user: "all the clouds and weathers are too ugly and pixile and its frames are cutting into each other"):
  * wx_hazard: the PixelLab pick (sources/atlas_wx_hazard.png, 6 x 3 cells of 64 px) does NOT draw on its 64 px grid (the five
    blizzard fronts sit on a ~69 px pitch, the six eddies on ~58 px, row 2 on its own); build_atlases.py scaled the whole pick x8
    and cut it on 512 px cells, so every cell held parts of two drawings: the eddy showed a slice of its neighbour turning with it,
    the front tiles had gaps and doubled bits, and the hard vertical cuts of the strip ends showed in the wall.
  * weather: cut from the pick's 64 px cells with keep_all, so the pick's detached strips (a flat streak above every cloud wisp,
    flecks above the mist) came along; drawn x4 nearest neighbour and then magnified 4 - 7x on screen (bilinear on top of 4 px
    blocks: blurry staircases).
  * wx_ambient: the pick scaled x4 whole; the top row's particles touch the sheet's top edge and the bottom row's its bottom edge
    (flat-cut flakes), and the 180 px drawings are drawn 10 - 25 px tall (minified 7 - 18x with no mip maps: crawling sparkle).

What this builds (no new art: every drawing is the approved pick's, found by its own connected pixels, not by the grid):
  * every drawing redrawn SOFT: premultiplied Lanczos upscale, a gaussian blur that dissolves the pixel staircase, a smooth alpha
    ramp (smoothstep, no stepped levels), the pick's light / shade kept as a smooth ice ramp, no dark pixel outlines.
  * every sheet is packed with TRANSPARENT GUTTERS: a cell's sprite rect is its content area, inset `GUTTER` px on every side, and
    nothing is drawn in the gutter (or, for the horizontally tiling front strips, the gutter holds the strip's own wrap-around
    continuation, so neighbouring tiles join seamlessly and no other cell is ever sampled). Gutters are multiples of 4 (ASTC 4x4
    blocks never straddle two cells' content).
  * animated loops are built from ONE drawing so consecutive frames differ only a little and frame 5 -> frame 0 closes the loop:
    the front "boils" (three soft noise fields breathe in and out on 6 phases), the eddy twists (inner arms lead, outer lag, on 6
    phases); nothing pops.

Sheet layouts (wx_hazard / wx_ambient keep their sizes: WeatherArtTest / HazardArt / WeatherArt address them; weather doubles to
2048, imported uncapped there by WorldBackdropImport.IsFrostSoftWeather; WeatherArtTest.FrostSoftSheets guards the gutters):
  weather.png     2048 x 2048, 4 x 4 cells of 512 px pitch, content 480 px (gutter 16); weather.json names the same 16 frames,
                  rects = the content areas, pixelScale 480 / 256 (so every sprite keeps the old 256 px cell's world size).
  wx_hazard.png   3072 x 1536, 6 x 3 cells of 512 px pitch, content 480 px (gutter 16); wx_hazard.json {"gutter": 16} tells
                  HazardArt to cut the content area (480 px = 1 unit) instead of the whole 512 cell.
  wx_ambient.png  2048 x 512, 8 x 2 cells of 256 px pitch; each particle drawn at ~2.5x the size it is shown (40 - 64 px),
                  centred, with a wide clear margin; wx_ambient.json = each particle's bounds + 2 px.
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
OUT = HERE.parents[4] / "Backgrounds/Resources/Worlds/Frost/Backdrop3"
GUTTER = 16


def src(name):
    return np.asarray(Image.open(HERE / "sources" / f"atlas_{name}.png").convert("RGBA")).astype(np.float64) / 255.0


# ---------------------------------------------------------------- drawing helpers
def drawings(a, dilate=2, min_px=40):
    """The pick's separate drawings: connected groups of opaque pixels (joined across `dilate` px gaps), largest first.
    Each is (y0, y1, x0, x1, mask)."""
    m = a[..., 3] > 0
    lab, n = ndimage.label(ndimage.binary_dilation(m, iterations=dilate), structure=np.ones((3, 3)))
    out = []
    for i, sl in enumerate(ndimage.find_objects(lab), 1):
        if sl is None:
            continue
        mm = (lab == i) & m
        if mm.sum() < min_px:
            continue
        out.append((sl[0].start, sl[0].stop, sl[1].start, sl[1].stop, mm))
    return out


def premul(a):
    p = a.copy()
    p[..., :3] *= p[..., 3:4]
    return p


def unpremul(p):
    a = p.copy()
    al = np.clip(a[..., 3:4], 1e-6, None)
    a[..., :3] = np.where(a[..., 3:4] > 1e-4, a[..., :3] / al, 0)
    return np.clip(a, 0, 1)


def resize(p, w, h):
    """Lanczos resample of a premultiplied float RGBA image (per channel, 32-bit float)."""
    chans = [np.asarray(Image.fromarray(p[..., c].astype(np.float32), "F").resize((w, h), Image.Resampling.LANCZOS))
             for c in range(4)]
    return np.clip(np.stack(chans, -1), 0, 1).astype(np.float64)


def blur(p, sigma, wrap_x=False):
    mode = ("constant", "wrap") if wrap_x else "constant"
    out = np.empty_like(p)
    for c in range(4):
        out[..., c] = ndimage.gaussian_filter(p[..., c], sigma=sigma, mode=mode)
    return out


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def ice(p, peak, lo=.06, hi=.75, shade=(104, 134, 186), light=(214, 232, 252), lift=0.0):
    """Premultiplied soft drawing -> straight RGBA: alpha = smoothstep ramp x peak; colour = the drawing's own light / shade
    (its luminance, normalised) on a smooth ice ramp (no dark pixel outlines: the darkest is a mid slate blue)."""
    s = unpremul(p)
    a = smoothstep(lo, hi, p[..., 3])
    lum = .2126 * s[..., 0] + .7152 * s[..., 1] + .0722 * s[..., 2]
    on = a > .02
    if on.any():
        l0, l1 = np.percentile(lum[on], 5), np.percentile(lum[on], 97)
        t = np.clip((lum - l0) / max(1e-3, l1 - l0), 0, 1)
    else:
        t = lum
    t = np.clip(t * (1 - lift) + lift, 0, 1)
    sh, li = np.array(shade, float) / 255, np.array(light, float) / 255
    rgb = sh * (1 - t[..., None]) + li * t[..., None]
    out = np.dstack((rgb, a * peak))
    out[out[..., 3] <= 0] = 0
    return out


def edge_window(h, w, fade):
    """1 inside, falling smoothly to 0 over `fade` px at every edge: a soft drawing never reaches its content rect's border."""
    y = np.minimum(np.arange(h), np.arange(h)[::-1])[:, None]
    x = np.minimum(np.arange(w), np.arange(w)[::-1])[None, :]
    return smoothstep(0, fade, np.minimum(y, x).astype(float))


def place_centre(piece, H, W):
    out = np.zeros((H, W, 4))
    h, w = piece.shape[:2]
    if h > H or w > W:
        raise ValueError(f"piece {w}x{h} does not fit {W}x{H}")
    y0, x0 = (H - h) // 2, (W - w) // 2
    out[y0:y0 + h, x0:x0 + w] = piece
    return out


def to8(a):
    o = np.clip(np.rint(a * 255), 0, 255).astype(np.uint8)
    o[o[..., 3] == 0, :3] = 0
    return o


def soft_piece(a, mask, box, scale, sigma, extra=0):
    """A drawing (its own pixels only) cut from the pick, padded, upscaled smoothly by `scale` and blurred (premultiplied)."""
    y0, y1, x0, x1 = box
    pad = 6
    cut = np.zeros((y1 - y0 + 2 * pad, x1 - x0 + 2 * pad, 4))
    sub = a[y0:y1, x0:x1].copy()
    sub[~mask[y0:y1, x0:x1]] = 0
    cut[pad:pad + y1 - y0, pad:pad + x1 - x0] = sub
    p = premul(cut)
    W, H = int(round(cut.shape[1] * scale)), int(round(cut.shape[0] * scale))
    p = resize(p, W, H)
    if sigma > 0:
        p = blur(p, sigma)
    return p


def tight_premul(p, thr=1.0 / 255):
    ys, xs = np.nonzero(p[..., 3] > thr)
    return p[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


# ---------------------------------------------------------------- weather (cloud banks, wisps, mist, blizzard sheets, snow)
WEATHER_CELLS = [   # frame -> pick cell (row, col): the old build_atlases.WEATHER table
    ("cloud_bank_00", 0, 0), ("cloud_bank_01", 0, 1), ("cloud_bank_02", 0, 2), ("cloud_bank_03", 0, 3),
    ("cloud_wisp_00", 1, 0), ("cloud_wisp_01", 1, 1), ("cloud_wisp_02", 1, 2), ("cloud_wisp_03", 1, 3),
    ("mist_00", 2, 0), ("mist_01", 2, 1), ("mist_02", 2, 2), ("blizzard_00", 2, 3),
    ("blizzard_01", 3, 0), ("blizzard_02", 3, 1), ("snow_00", 3, 2), ("snow_01", 3, 3),
]
# per family: blur sigma (px at 480), alpha ramp, peak alpha (the old sheet's measured peaks, OLD_TONE: the ceiling's thickening and
# the blizzard's 30 % cover cap are tuned to them), and the ice ramp's lift (mist / snow paler)
WEATHER_LOOK = {
    "cloud_bank": dict(sigma=7.0, lo=.05, hi=.80, peak=72 / 255, lift=.15),
    "cloud_wisp": dict(sigma=5.5, lo=.05, hi=.70, peak=64 / 255, lift=.20),
    "mist":       dict(sigma=9.0, lo=.04, hi=.70, peak=48 / 255, lift=.30),
    "blizzard":   dict(sigma=4.5, lo=.05, hi=.65, peak=52 / 255, lift=.30),
    "snow":       dict(sigma=3.0, lo=.10, hi=.60, peak=88 / 255, lift=.45),
}
OLD_WEATHER_MEAN = np.array([142, 179, 238]) / 255   # the old sheet's ice-blue mean (build_atlases.build_weather)


def clean_cell(a, fam):
    """A 64 px cell of the weather pick with the pick's detached strays removed: the flat streak drawn above every wisp and the
    flecks above the mist (separate drawings in the cell's top band, apart from the main drawing)."""
    if fam not in ("cloud_wisp", "mist"):
        return a
    a = a.copy()
    m = a[..., 3] > 0
    lab, n = ndimage.label(ndimage.binary_dilation(m, iterations=2), structure=np.ones((3, 3)))
    lab = lab * m
    sizes = np.bincount(lab.ravel(), minlength=n + 1)
    main = sizes[1:].argmax() + 1
    for i in range(1, n + 1):
        if i == main or sizes[i] == 0:
            continue
        ys, xs = np.nonzero(lab == i)
        if ys.max() < 14 and sizes[i] < sizes[main] * .3:
            a[lab == i] = 0
    return a


def build_weather():
    pick = src("weather")
    P, C, g = 512, 512 - 2 * GUTTER, GUTTER
    S = 2048
    sheet = np.zeros((S, S, 4))
    sprites = []
    for i, (name, r, c) in enumerate(WEATHER_CELLS):
        fam = name.rsplit("_", 1)[0]
        look = WEATHER_LOOK[fam]
        cell = clean_cell(pick[r * 64:(r + 1) * 64, c * 64:(c + 1) * 64], fam)
        scale = C / 64.0                                   # the 64 px pick cell -> the 480 px content area (same framing as before)
        p = premul(cell)
        p = resize(p, C, C)
        p = blur(p, look["sigma"])
        if fam == "blizzard":
            # the sheets streak down-left: smear along the 45 degree streaks (motion), keep them thin across
            k = 9
            acc = np.zeros_like(p)
            for t in range(-k, k + 1):
                acc += ndimage.shift(p, (t * .7, -t * .7, 0), order=1, mode="constant")
            p = acc / (2 * k + 1)
        d = ice(p, look["peak"], lo=look["lo"], hi=look["hi"], lift=look["lift"])
        d[..., 3] *= edge_window(C, C, 18)
        row, col = divmod(i, 4)
        y0, x0 = row * P + g, col * P + g
        sheet[y0:y0 + C, x0:x0 + C] = d
        sprites.append({"n": name, "x": x0, "y": S - (y0 + C), "w": C, "h": C})
    # keep the old sheet's ice-blue tint (the ceiling's look vs the planetfall deck; WorldBackdropTest's luminance cap)
    on = sheet[..., 3] > 4 / 255
    w = sheet[..., 3][on]
    mean = (sheet[..., :3][on] * w[:, None]).sum(0) / w.sum()
    sheet[..., :3] = np.where(on[..., None], np.clip(sheet[..., :3] * (OLD_WEATHER_MEAN / mean), 0, 1), 0)
    out = to8(sheet)
    Image.fromarray(out, "RGBA").save(OUT / "weather.png", optimize=True)
    js = {"sprites": sprites, "pixelScale": C / 256.0, "sheetW": S, "sheetH": S}
    (OUT / "weather.json").write_text(json.dumps(js, indent=2) + "\n")
    return out


# ---------------------------------------------------------------- wx_hazard (blizzard front, whiteout eddy, warnings, gust curls)
def periodic_strip(a, box, mask, overlap):
    """A front drawing (a horizontal strip with hard cut ends) made seamless: its last `overlap` columns cross-fade into its
    first, so the strip repeats with period (width - overlap). Straight RGBA of the pick, float."""
    y0, y1, x0, x1 = box
    s = a[y0:y1, x0:x1].copy()
    s[~mask[y0:y1, x0:x1]] = 0
    s = premul(s)
    w = s.shape[1]
    per = w - overlap
    out = s[:, :per].copy()
    t = smoothstep(0, 1, (np.arange(overlap) + .5) / overlap)[None, :, None]
    out[:, :overlap] = s[:, per:] * (1 - t) + s[:, :overlap] * t
    return out          # premultiplied, periodic in x


def front_tile(pick, C):
    """The blizzard front: the middle of the pick's five front drawings, seamless, smooth, `C` px wide (one unit), its body
    ~0.47 of the unit tall and centred (the old cell's framing), the hard ends gone. Premultiplied."""
    fronts = sorted([d for d in drawings(pick[0:64], dilate=1) if d[3] - d[2] > 40], key=lambda d: d[2])
    y0, y1, x0, x1, m = fronts[len(fronts) // 2]
    # the strip's two ends are hard cuts with a tall lump on them (the pick's frame edge): only its inner run is used
    x0, x1 = x0 + 6, x1 - 6
    strip = periodic_strip(pick[0:64], (y0, y1, x0, x1), m, overlap=14)
    per = strip.shape[1]
    hh = int(round(C * .47))
    # resample with wrap padding so the seam is resampled like the inside
    padn = 8
    wide = np.concatenate([strip[:, -padn:], strip, strip[:, :padn]], 1)
    k = C / per
    up = resize(wide, int(round(wide.shape[1] * k)), hh)
    cut0 = int(round(padn * k))
    up = up[:, cut0:cut0 + C]
    # the strip's top and bottom are straight cuts: a soft vertical envelope (full in the middle 60 %, easing out to the rims)
    yy = (np.arange(hh) + .5) / hh
    env = smoothstep(0, .22, yy) * smoothstep(1, .78, yy)
    up *= env[:, None, None]
    canvas = np.zeros((C, C, 4))
    top = (C - hh) // 2
    canvas[top:top + hh] = up
    canvas = np.stack([ndimage.gaussian_filter(canvas[..., c], sigma=(7, 4), mode=("constant", "wrap")) for c in range(4)], -1)
    return canvas


def boil_frames(base, n=6, amp=.4, seed=7):
    """n frames of one drawing that breathe: three soft noise fields (periodic in x) swell and thin on n phases; frame n == frame 0."""
    rng = np.random.default_rng(seed)
    H, W = base.shape[:2]
    fields = []
    for j in range(3):
        f = rng.standard_normal((H, W))
        f = ndimage.gaussian_filter(f, sigma=(22, 30), mode=("reflect", "wrap"))
        f /= max(1e-6, np.abs(f).max())
        fields.append(f)
    out = []
    for k in range(n):
        mod = 1 + amp * sum(np.sin(2 * np.pi * k / n + 2.1 * j) * fields[j] for j in range(3))
        p = base.copy()
        p *= np.clip(mod, .5, 1.5)[..., None]
        out.append(np.clip(p, 0, 1))
    return out


def eddy_base(pick, D):
    """The whiteout eddy: the cleanest of the pick's six eddy drawings (row 1), smooth, `D` px across. Premultiplied, tight."""
    eddies = sorted([d for d in drawings(pick[64:128], dilate=1) if d[1] - d[0] > 30], key=lambda d: d[2])
    # the one whose drawing is roundest and least clipped by the pick's sheet edge (the middle ones)
    y0, y1, x0, x1, m = eddies[2]
    side = max(y1 - y0, x1 - x0)
    p = soft_piece(pick[64:128], m, (y0, y1, x0, x1), D / side, 4.0)
    return tight_premul(p)


def twist(p, ang_deg, k_phase, n=6):
    """Differential rotation (inner arms lead, rim still), on n phases: angle(r) = ang * sin(2 pi k / n) * (1 - r / R)."""
    H, W = p.shape[:2]
    cy, cx = (H - 1) / 2, (W - 1) / 2
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    dy, dx = yy - cy, xx - cx
    r = np.hypot(dx, dy)
    R = min(H, W) / 2
    th = np.radians(ang_deg) * np.sin(2 * np.pi * k_phase / n) * np.clip(1 - r / R, 0, 1)
    c, s = np.cos(th), np.sin(th)
    sx = cx + c * dx - s * dy
    sy = cy + s * dx + c * dy
    out = np.stack([ndimage.map_coordinates(p[..., ch], [sy, sx], order=3, mode="constant") for ch in range(4)], -1)
    return np.clip(out, 0, 1)


def rotate(p, deg):
    return np.clip(ndimage.rotate(p, deg, axes=(1, 0), reshape=False, order=3, mode="constant"), 0, 1)


def ring(H, W, radius, width):
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    r = np.hypot(yy - (H - 1) / 2, xx - (W - 1) / 2)
    return np.exp(-((r - radius) / width) ** 2)


# the warnings' peak alpha: the old hatched warnings peaked at 72; a soft ghost has no hard edge to read by, so it is a little
# stronger (96) to stay as readable (HazardVisuals still draws it at .35 - .9 of that, rising as the hazard nears)
WARN_PEAK = 96 / 255


def build_wx_hazard():
    pick = src("wx_hazard")
    P, g = 512, GUTTER
    C = P - 2 * g
    sheet = np.zeros((1536, 3072, 4))

    def put(row, col, straight, wrap=False):
        y0, x0 = row * P + g, col * P + g
        sheet[y0:y0 + C, x0:x0 + C] = straight
        if wrap:
            # the gutter carries the strip's own continuation (left gutter = its right end, right gutter = its left end), so a tile's
            # edge texels sample themselves and the tiles placed side by side meet seamlessly; never another cell
            sheet[y0:y0 + C, x0 - g:x0] = straight[:, C - g:]
            sheet[y0:y0 + C, x0 + C:x0 + C + g] = straight[:, :g]

    # row 0: the blizzard front loop (seamless horizontal tiles, boiling on 6 phases); peak alpha 144 as before
    front = front_tile(pick, C)
    hh = int(round(C * .47))
    top = (C - hh) // 2
    yy = np.arange(C).astype(float)
    rim = (smoothstep(top - 10, top + hh * .3, yy) * smoothstep(top + hh + 10, top + hh * .7, yy))[:, None]   # soft top / bottom
    for k, f in enumerate(boil_frames(front)):
        d = ice(f, 144 / 255, lo=.04, hi=.70, shade=(122, 156, 206), light=(232, 244, 255), lift=.25)
        d[..., 3] *= rim
        put(0, k, d, wrap=True)

    # row 1: the whiteout eddy loop (the code turns it continuously; the frames add an inner twist on 6 phases), 0.70 of the unit
    D = int(round(C * .70))
    eddy = place_centre(eddy_base(pick, D), C, C)
    for k in range(6):
        e = twist(eddy, 14, k)
        d = ice(e, 144 / 255, lo=.04, hi=.72, shade=(118, 150, 200), light=(230, 242, 255), lift=.2)
        d[..., 3] *= edge_window(C, C, 12)
        put(1, k, d)

    # row 2: the warnings (WARN_PEAK: faint and readable, a ghost of the body with a brighter leading edge / rim)
    #   cols 0-1: the front's warning (tiles across the top of the view), two phases of the boil
    #   cols 2-3: the eddy's warning (a soft ring where it forms, around a faint eddy), two angles
    #   cols 4-5: the pick's two gust curls (not used by Frost's hazards; drawn soft for completeness)
    yy = np.arange(C)[:, None].astype(float)
    lead = np.exp(-((yy - (top + hh * .76)) / 16.0) ** 2)                 # the wall's leading (lower) edge
    for k in range(2):
        f = boil_frames(front)[k * 3].copy()
        f[..., 3] = np.clip(f[..., 3] * .75 + lead * .55 * (f[..., 3] > .02), 0, 1)
        f[..., :3] = np.clip(f[..., :3] * .75 + lead[..., None] * .55 * (f[..., 3:4] > .02), 0, 1)
        d = ice(f, WARN_PEAK, lo=.04, hi=.6, shade=(150, 184, 226), light=(240, 248, 255), lift=.35)
        d[..., 3] *= rim
        put(2, k, d, wrap=True)
    for k in range(2):
        e = rotate(eddy, -18 * (k + 1)).copy()
        rg = ring(C, C, D * .47, 12.0)
        e[..., 3] = np.clip(e[..., 3] * .7 + rg * .6, 0, 1)
        e[..., :3] = np.clip(e[..., :3] * .7 + rg[..., None] * .6, 0, 1)
        d = ice(e, WARN_PEAK, lo=.04, hi=.6, shade=(150, 184, 226), light=(240, 248, 255), lift=.35)
        d[..., 3] *= edge_window(C, C, 12)
        put(2, 2 + k, d)
    curls = sorted([d for d in drawings(pick[128:192], dilate=1) if d[3] - d[2] > 30], key=lambda d: d[2])[-2:]
    for k, (y0, y1, x0, x1, m) in enumerate(curls):
        side = max(y1 - y0, x1 - x0)
        p = tight_premul(soft_piece(pick[128:192], m, (y0, y1, x0, x1), C * .78 / side, 4.0))
        d = ice(place_centre(p, C, C), 72 / 255, lo=.04, hi=.6, lift=.3)
        d[..., 3] *= edge_window(C, C, 12)
        put(2, 4 + k, d)

    out = to8(sheet)
    Image.fromarray(out, "RGBA").save(OUT / "wx_hazard.png", optimize=True)
    (OUT / "wx_hazard.json").write_text(json.dumps({"gutter": g}) + "\n")
    return out


# ---------------------------------------------------------------- wx_ambient (snowflakes, ice glints, dots)
AMBIENT_SIZE = [64, 48, 40, 64]     # per kind: the drawing's long side (px), ~2.5x its size on a phone (kinds 0-2 are drawn ~25 / 14 / 10 px)
# the pick drew its top row against the sheet's top edge and its bottom row against the bottom edge (flat-cut flakes): the drawings
# that are mirror-symmetric (the six-armed flakes, the diamonds, the stars) are completed by mirroring their whole half; the others
# fade out softly where they were cut
AMBIENT_MIRROR = {0, 1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 14}


def mirror_axis(a, clipped_top):
    """The row a symmetric drawing mirrors about: the row about which its uncut half best matches itself flipped."""
    al = a[..., 3]
    h = al.shape[0]
    best, axis = 1e9, h / 2
    rng = range(h // 3, h) if clipped_top else range(1, 2 * h // 3)
    for cy2 in [2 * x for x in rng] + [2 * x + 1 for x in rng]:
        cy = cy2 / 2
        d = np.arange(1, h)
        lo, hi = np.floor(cy - d).astype(int), np.ceil(cy + d).astype(int)
        ok = (lo >= 0) & (hi < h)
        if ok.sum() < h // 4:
            continue
        lo, hi = lo[ok], hi[ok]
        score = np.abs(al[lo] - al[hi]).mean()
        if score < best:
            best, axis = score, cy
    return axis


def complete(a, clipped_top):
    """Mirror a symmetric drawing cut at its top (or bottom) edge into its whole self (rows beyond the cut come from the other half)."""
    axis = mirror_axis(a, clipped_top)
    h, w = a.shape[:2]
    if clipped_top:
        H = int(np.ceil(2 * (h - 1 - axis))) + 1
        out = np.zeros((H, w, 4)); out[H - h:] = a
    else:
        H = int(np.ceil(2 * axis)) + 1
        out = np.zeros((H, w, 4)); out[:h] = a
    flip = out[::-1]
    take = flip[..., 3:4] > out[..., 3:4]
    return np.where(take, flip, out)


def build_wx_ambient():
    pick = src("wx_ambient")
    sheet = np.zeros((512, 2048, 4))
    cells = []
    for r in range(2):
        for c in range(8):
            idx = r * 8 + c
            cell = pick[r * 64:(r + 1) * 64, c * 64:(c + 1) * 64].copy()
            ds = drawings(cell, dilate=3, min_px=6)
            if not ds:
                raise ValueError(f"ambient cell r{r} c{c} is empty")
            # the cell's drawing: every group of it (a flake and its sparkles)
            m = np.zeros(cell.shape[:2], bool)
            for d in ds:
                m |= d[4]
            cell[~m] = 0
            ys, xs = np.nonzero(m)
            a = cell[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
            cut_top, cut_bottom = r == 0 and ys.min() == 0, r == 1 and ys.max() == 63
            if (cut_top or cut_bottom) and idx in AMBIENT_MIRROR:
                a = complete(a, cut_top)
            elif cut_top or cut_bottom:
                fade = smoothstep(0, 5, np.arange(a.shape[0]).astype(float))
                a = a * (fade if cut_top else fade[::-1])[:, None, None]
            # no dark pixel outlines on falling snow: the drawing's darkest values fade out, its lights stay
            lum = .2126 * a[..., 0] + .7152 * a[..., 1] + .0722 * a[..., 2]
            a = a.copy()
            a[..., 3] *= smoothstep(.18, .45, lum)
            a[..., :3] = a[..., :3] * .3 + .6                      # pale ice, the world's tint colours it (WorldWeatherTable)
            kind = idx // 4
            pad = 3
            big = np.zeros((a.shape[0] + 2 * pad, a.shape[1] + 2 * pad, 4)); big[pad:-pad, pad:-pad] = a
            side = max(a.shape[:2])
            k = AMBIENT_SIZE[kind] / side
            p = resize(premul(big), int(round(big.shape[1] * k)), int(round(big.shape[0] * k)))
            p = blur(p, .7)
            glow = blur(p, 3.0)
            p = np.clip(p + glow * .35, 0, 1)                        # a faint soft halo: it reads as light, not as an outlined icon
            d = unpremul(p)
            d[..., 3] = np.clip(d[..., 3] * 1.15, 0, 1) * (128 / 255)
            d[d[..., 3] < 1.5 / 255] = 0
            d = place_centre(tight_premul(d), 256, 256)
            sheet[r * 256:(r + 1) * 256, c * 256:(c + 1) * 256] = d
            ys, xs = np.nonzero(d[..., 3] > 0)
            x0, x1, y0, y1 = xs.min() - 2, xs.max() + 3, ys.min() - 2, ys.max() + 3
            cells.append({"col": c, "row": r, "x": int(c * 256 + x0), "y": int(512 - (r * 256 + y1)),
                          "w": int(x1 - x0), "h": int(y1 - y0)})
    out = to8(sheet)
    Image.fromarray(out, "RGBA").save(OUT / "wx_ambient.png", optimize=True)
    (OUT / "wx_ambient.json").write_text(json.dumps({"cells": cells}, indent=1) + "\n")
    return out


BUILD = {"weather": build_weather, "wx_hazard": build_wx_hazard, "wx_ambient": build_wx_ambient}


def main():
    global OUT
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=str(OUT))
    ap.add_argument("sheets", nargs="*", default=list(BUILD))
    args = ap.parse_args()
    OUT = Path(args.out)
    OUT.mkdir(parents=True, exist_ok=True)
    for n in args.sheets:
        BUILD[n]()
        print("built", n)


if __name__ == "__main__":
    main()
