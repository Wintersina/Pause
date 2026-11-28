#!/usr/bin/env python3
"""The backdrop tests' pixel rules for the Frost ground tiles, ported to Python (no Unity).

    python3 check_tiles.py [<Resources/Worlds/Frost/Backdrop3>] [--variants 1 2]

Ported (each from Pause/Assets/Editor/Tests/):
  BackdropResolutionTest   block score (k = 2, 3, 4, every grid phase) < .97; identical adjacent pairs <= .85
  WorldBackdropTest        size 896 x 1792 RGBA (the importer's PPU contract); vertical seam (premultiplied mean
                           |bottom row - top row|) <= .02 on every tile; as drawn through BackdropGrade (Frost lift
                           1.95 x layer share, saturation 1.05): value p90 <= .55 (sky / far / mid), sky luminance
                           <= .18, tile luminance <= .24, chroma <= .26; CheckReadability on the composite (sky, far,
                           mid, flow; lane x 20-80%): Ice hull 2.5:1, brightest tone 7:1, lane value p90 <= .55 and
                           < hull value, lane chroma <= .26
  HudButtonsTest           Frost v1-v4 sky / mid / far pooled raw luminance: neon glyph vs mean and darkest 1% >= 4.5:1,
                           vs lightest 1% >= 2:1
  FrostBackdropTest        (estimate) the ground drawn brighter than painted: median value ratio of the composite lane
                           in 1.3 .. 1.9 (the test renders the whole scene; this is the tiles alone)
  ground masks             mask.txt 32 x 64 of W B O C; every Frost piece rule has somewhere to stand
  skill verifier           sky / far / mid fully opaque; flow straight alpha with no solid (255) texel; no saturated
                           red / pink (hue 345-15, s > .5, v > .35)
Exit code = number of failures (the skill-verifier lines are warnings: the shipped old flows already miss them).
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
DEFAULT = HERE.parents[4] / "Backgrounds/Resources/Worlds/Frost/Backdrop3"

BRIGHTNESS, SATURATION = 1.95, 1.05            # FrostTuning
SHARE = {"sky": .55, "far": .8, "mid": 1.0, "flow": 1.0}
SEAM = .02
VALUE_P90, SKY_LUM, TILE_LUM, CHROMA = .55, .18, .24, .26
BLOCK, PAIR = .97, .85
HULL, LIGHT, HULL_HI, BONE = 0x6AAED0, 0x6EF2EE, 0xCDF4F8, 0xF4EAD4   # EnemyPalette Ice / Cyan / IceHi / Bone
NEON = (200, 255, 30)                         # HudSkin.FrostNeon

fails = 0
warns = 0


def warn(what, ok):
    """Skill-verifier guidance (.claude/skills/add-world/scripts/verify_backdrop_tiles.py), not a CI test: reported, not counted."""
    global warns
    print(("PASS  " if ok else "WARN  ") + what)
    if not ok:
        warns += 1


def check(what, ok):
    global fails
    print(("PASS  " if ok else "FAIL  ") + what)
    if not ok:
        fails += 1


def hexc(h):
    return np.array([(h >> 16) & 255, (h >> 8) & 255, h & 255], np.float64) / 255


def read(path):
    """Pixels as Unity's Texture2D.GetPixels gives them: float RGBA, row 0 = the BOTTOM of the image."""
    a = np.asarray(Image.open(path).convert("RGBA"))
    return a[::-1].copy()


# ---- BackdropResolutionTest ---------------------------------------------------------------
def block_score(a, k):
    h, w = a.shape[:2]
    packed = (a[..., 0].astype(np.uint32) << 24) | (a[..., 1].astype(np.uint32) << 16) | (a[..., 2].astype(np.uint32) << 8) | a[..., 3]
    opaque = a[..., 3] > 8
    if opaque.sum() < 300:
        return 0.0
    best = 0.0
    for oy in range(k):
        for ox in range(k):
            ny, nx = (h - oy) // k, (w - ox) // k
            p = packed[oy:oy + ny * k, ox:ox + nx * k].reshape(ny, k, nx, k)
            o = opaque[oy:oy + ny * k, ox:ox + nx * k].reshape(ny, k, nx, k)
            first = p[:, :1, :, :1]
            eq = ((p == first) & o).sum()
            tot = o.sum()
            if tot:
                best = max(best, eq / tot)
    return float(best)


def pair_dup(a):
    packed = (a[..., 0].astype(np.uint32) << 24) | (a[..., 1].astype(np.uint32) << 16) | (a[..., 2].astype(np.uint32) << 8) | a[..., 3]
    op = a[..., 3] > 8
    hx = op[:, 1:] | op[:, :-1]
    hy = op[1:] | op[:-1]
    same = ((packed[:, 1:] == packed[:, :-1]) & hx).sum() + ((packed[1:] == packed[:-1]) & hy).sum()
    tot = hx.sum() + hy.sum()
    return float(same / tot) if tot else 0.0


# ---- WorldBackdropTest / BackdropGrade ----------------------------------------------------
def lift(share):
    return max(1.0, 1.0 + (BRIGHTNESS - 1.0) * share)


def saturation(k):
    if BRIGHTNESS <= 1.0001 or k <= 1:
        return 1.0
    return 1.0 + (SATURATION - 1.0) * min(1.0, max(0.0, (k - 1.0) / (BRIGHTNESS - 1.0)))


def curve(v, k):
    return 1.0 - np.power(np.clip(1.0 - v, 0, 1), k)


def as_drawn(px, layer):
    """BackdropGrade.Apply (px float RGBA 0..1)."""
    k = lift(SHARE[layer])
    s = saturation(k)
    rgb = px[..., :3]
    v = rgb.max(-1)
    kk = np.where(v > 1e-4, curve(v, k) / np.maximum(v, 1e-4), 0)
    rgb = rgb * kk[..., None]
    l = rgb.mean(-1, keepdims=True)
    rgb = np.clip(l + (rgb - l) * s, 0, 1)
    return np.concatenate([rgb, px[..., 3:]], -1)


def seam(px):
    a, b = px[0], px[-1]            # bottom row, top row (Unity order)
    pa = a[:, :3] * a[:, 3:]
    pb = b[:, :3] * b[:, 3:]
    return float((np.abs(pa - pb).sum(-1) + np.abs(a[:, 3] - b[:, 3])).sum() / (px.shape[1] * 4))


def value_p90(px):
    f = px.reshape(-1, 4)[::3]
    f = f[f[:, 3] > .5]
    hist = np.bincount(np.clip((f[:, :3].max(-1) * 255 + .5).astype(int), 0, 255), minlength=256)
    target = int(len(f) * .9)
    acc = np.cumsum(hist)
    return float(np.argmax(acc >= target) / 255)


def measure(px):
    f = px.reshape(-1, 4)[::3]
    f = f[f[:, 3] > .01]
    a = f[:, 3]
    lum = (a * (f[:, :3] @ np.array([.2126, .7152, .0722]))).sum() / a.sum()
    chroma = (a * (f[:, :3].max(-1) - f[:, :3].min(-1))).sum() / a.sum()
    return float(lum), float(chroma)


def linear(c):
    return np.where(c <= .03928, c / 12.92, np.power((c + .055) / 1.055, 2.4))


def rel_lum(c):
    c = np.asarray(c, np.float64)
    l = linear(c)
    return l[..., 0] * .2126 + l[..., 1] * .7152 + l[..., 2] * .0722


def contrast(a, b):
    return (max(a, b) + .05) / (min(a, b) + .05)


def composite(base, v, drawn=True):
    d = base / f"v{v}"
    sky = read(d / "sky.png").astype(np.float64) / 255
    out = (as_drawn(sky, "sky") if drawn else sky)[..., :3].copy()
    for layer in ("far", "mid", "flow"):
        px = read(d / f"{layer}.png").astype(np.float64) / 255
        if drawn:
            px = as_drawn(px, layer)
        a = px[..., 3:]
        out = out + (px[..., :3] - out) * a
    return out


def readability(base, v):
    px = composite(base, v)
    h, w = px.shape[:2]
    lane = px[::2, int(w * .2):int(w * .8):2]
    lum = np.sort(rel_lum(lane).ravel())
    val = np.sort(lane.max(-1).ravel())
    chroma = float((lane.max(-1) - lane.min(-1)).mean())
    lane_lum = lum[len(lum) // 2]
    v90 = val[int(len(val) * .9)]
    body = contrast(float(rel_lum(hexc(HULL))), lane_lum)
    bright = max(float(rel_lum(hexc(LIGHT))), float(rel_lum(hexc(HULL_HI))), float(rel_lum(hexc(BONE))))
    kick = contrast(bright, lane_lum)
    hull_v = hexc(HULL).max()
    check(f"v{v} lane with enemies on top: body {body:.2f}:1 >= 2.5, brightest {kick:.2f}:1 >= 7 (lane median lum {lane_lum:.4f})",
          body >= 2.5 and kick >= 7)
    check(f"v{v} lane value p90 {v90:.2f} <= {VALUE_P90} and < hull {hull_v:.2f}", v90 <= VALUE_P90 and v90 < hull_v)
    check(f"v{v} lane chroma {chroma:.3f} <= {CHROMA}", chroma <= CHROMA)
    plain = composite(base, v, drawn=False)
    pl = plain[:, int(w * .2):int(w * .8)].max(-1)
    dl = px[:, int(w * .2):int(w * .8)].max(-1)
    ratio = float(np.median(dl) / max(np.median(pl), 1e-3))
    check(f"v{v} ground drawn brighter than painted (tiles only, median value {np.median(pl):.3f} -> {np.median(dl):.3f}, x{ratio:.2f} in 1.3..1.9)",
          1.3 <= ratio <= 1.9)


def hud(base):
    lums = []
    for v in range(1, 5):
        for layer in ("sky", "mid", "far"):
            p = base / f"v{v}" / f"{layer}.png"
            if not p.exists():
                continue
            f = read(p).reshape(-1, 4)[::11]
            f = f[f[:, 3] > 200]
            c = f[:, :3] / 255
            lin = np.where(c <= .04045, c / 12.92, np.power((c + .055) / 1.055, 2.4))
            lums.append(lin @ np.array([.2126, .7152, .0722]))
    l = np.sort(np.concatenate(lums))
    n = len(l)
    mean, p1, p99 = l.mean(), l[min(max(int(n * .01), 0), n - 1)], l[min(max(int(n * .99), 0), n - 1)]
    g = np.array(NEON) / 255
    g = float((np.where(g <= .04045, g / 12.92, np.power((g + .055) / 1.055, 2.4)) * [.2126, .7152, .0722]).sum())
    check(f"HUD glyph vs Frost tile mean >= 4.5:1 ({contrast(g, mean):.1f}, mean {mean:.4f})", contrast(g, mean) >= 4.5)
    check(f"HUD glyph vs darkest 1% >= 4.5:1 ({contrast(g, p1):.1f})", contrast(g, p1) >= 4.5)
    check(f"HUD glyph vs lightest 1% >= 2:1 ({contrast(g, p99):.1f}, p99 {p99:.4f})", contrast(g, p99) >= 2)


def hsv_bad_red(a):
    rgb = a[..., :3].astype(np.float64) / 255
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = mx - mn
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    dd = np.where(d > 0, d, 1)
    hue = np.where(mx == r, ((g - b) / dd) % 6, np.where(mx == g, (b - r) / dd + 2, (r - g) / dd + 4)) * 60
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    return int((((hue >= 345) | (hue <= 15)) & (d > 0) & (s > .5) & (mx > .35) & (a[..., 3] > 0)).sum())


def tile(base, v, layer):
    p = base / f"v{v}" / f"{layer}.png"
    im = Image.open(p)
    tag = f"v{v}/{layer}"
    check(f"{tag}: {im.mode} {im.size[0]}x{im.size[1]} == RGBA 896x1792", im.mode == "RGBA" and im.size == (896, 1792))
    a = read(p)
    ks = {k: block_score(a, k) for k in (2, 3, 4)}
    worst = max(ks, key=ks.get)
    check(f"{tag}: block score {ks[worst]:.3f} (k={worst}) < {BLOCK} (not a nearest-neighbour upscale)", ks[worst] < BLOCK)
    pd = pair_dup(a)
    check(f"{tag}: identical adjacent texel pairs {pd:.2f} <= {PAIR}", pd <= PAIR)
    px = a.astype(np.float64) / 255
    s = seam(px)
    check(f"{tag}: vertically seamless (top vs bottom row {s:.4f} <= {SEAM})", s <= SEAM)
    if layer == "flow":
        warn(f"{tag}: flow has no solid texel (max alpha {a[..., 3].max()}), {(a[..., 3] > 0).mean():.3f} covered",
              a[..., 3].max() < 255)
    else:
        check(f"{tag}: fully opaque", (a[..., 3] == 255).all())
    warn(f"{tag}: no saturated red / pink ({hsv_bad_red(a)})", hsv_bad_red(a) == 0)
    if layer == "flow":
        return
    d = as_drawn(px, layer)
    v90 = value_p90(d)
    lum, chroma = measure(d)
    check(f"{tag}: value p90 as drawn {v90:.3f} <= {VALUE_P90}", v90 <= VALUE_P90)
    if layer == "sky":
        check(f"{tag}: sky stays dark as drawn (lum {lum:.3f} <= {SKY_LUM})", lum <= SKY_LUM)
    check(f"{tag}: under the contrast guard as drawn (lum {lum:.3f} <= {TILE_LUM}, chroma {chroma:.3f} <= {CHROMA})",
          lum <= TILE_LUM and chroma <= CHROMA)


# ---- ground masks -------------------------------------------------------------------------
TILE_W_UNITS = 6.0
RULES = {   # FrostTuning: (classes, need, core, near)
    "Floating (icebreaker)": ("W", .8, .26, None),
    "Offshore (rig, platform, rigbay)": ("W", .6, .34, None),
    "Island (refinery, relay, derrick, sites)": ("WO", .72, .34, None),
    "IceRoad (convoy)": ("CO", .72, .26, None),
    "Shore (cliff)": ("OC", .55, .28, "W"),
    "Flats (silo)": ("O", .72, .34, None),
    "Ridge (cave)": ("C", .7, .34, None),
}


def mask_check(base, v):
    p = base / f"v{v}" / "mask.txt"
    rows = p.read_text().splitlines()
    ok = len(rows) == 64 and all(len(r) == 32 and set(r) <= set("WBOC") for r in rows)
    check(f"v{v}/mask.txt is 32 x 64 of W B O C", ok)
    if not ok:
        return
    g = np.array([list(r) for r in rows])
    share = {c: float((g == c).mean()) for c in "WBOC"}
    print("      shares " + " ".join(f"{c} {share[c]:.2f}" for c in "WBOC"))
    # GroundPlanner.Accepts on a grid of candidate spots (tile u across, v down; size 1.6 u of a 6 u wide, 12 u tall tile)
    size = 1.6
    rng = np.random.default_rng(5)
    for name, (on, need, core, near) in RULES.items():
        rx = size * max(core, .2) / TILE_W_UNITS
        ry = rx * .8 / 2                               # in v (tile height = 2 widths)
        hits = 0
        tries = 2000
        for _ in range(tries):
            u = rng.uniform(.15, .85)
            vv = rng.uniform(0, 1)
            n = m = 0
            for j in range(-2, 3):
                for i in range(-2, 3):
                    fx, fy = i * .5, j * .5
                    if fx * fx + fy * fy > 1.05:
                        continue
                    n += 1
                    x = min(max(int((u + fx * rx) * 32), 0), 31)
                    y = int(((vv + fy * ry) % 1) * 64) % 64
                    m += g[y, x] in on
            okp = m / n >= need
            if okp and near:
                okp = False
                for k in range(12):
                    a = k * np.pi / 6
                    x = min(max(int((u + np.cos(a) * rx * 1.6) * 32), 0), 31)
                    y = int(((vv + np.sin(a) * ry * 1.6) % 1) * 64) % 64
                    if g[y, x] in near:
                        okp = True
                        break
            hits += okp
        print(f"      {name}: {hits / tries:.1%} of spots accept")
    floats = share["W"] > .08
    check(f"v{v}: open water (W {share['W']:.2f}) for the floating / offshore pieces and land (O+C {share['O'] + share['C']:.2f})",
          floats and share["O"] + share["C"] > .2)


def main():
    args = [a for a in sys.argv[1:]]
    variants = [1, 2]
    if "--variants" in args:
        i = args.index("--variants")
        variants = [int(x) for x in args[i + 1:]]
        args = args[:i]
    base = Path(args[0]) if args else DEFAULT
    layers = {1: ("sky", "far", "mid", "flow"), 2: ("far", "mid")}
    for v in variants:
        for layer in layers.get(v, ("sky", "far", "mid", "flow")):
            tile(base, v, layer)
        readability(base, v)
        mask_check(base, v)
    hud(base)
    print(f"failures: {fails}  (verifier warnings: {warns})")
    sys.exit(fails)


if __name__ == "__main__":
    main()
