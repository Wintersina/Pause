#!/usr/bin/env python3
"""Paragon-standard enemy redraw: one approved PixelLab drawing -> the 7-cell flipbook, installed.

    python3 build.py <key> <approved.png> [--no-install]

The approved drawing (80 px PixelLab canvas, style refs Paragon + Bastion, see prompts.json) is:
  * trimmed and scaled with NEAREST so its longest edge matches the live strip's frame 0 exactly
    (EnemyRosterTest holds the silhouette within 15%, and the collider stays as it is), centred in the cell;
  * animated without repainting:
      0-3 idle   a 1 px bob and a flicker on the glow/flame pixels (the world's signal hue)
      4-5 tell   the glow ramps hot and throws spark crosses off the signal pixels
      6   hit    a white flash over the hull plus a few chips
Hard alpha everywhere (0 or 255). The old strip is kept in old/<key>.png the first time.
Prints the muzzle check: the shot point (EnemyBehaviours muzzle x TierScale below centre) must sit on or just
ahead of the hull's front (the bottom of the drawing, the nose faces the player).
Then run the world's build_<world>_enemy_death.py <key> to rebuild the death strip from the new idle cell 3.
"""
import json, shutil, sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]                                   # .../Assets/Art
LIVE = ART / "Resources/Enemies"
OLD = HERE / "old"; OUT = HERE / "strips"; KEYS = HERE / "keys"; ANIM = HERE / "anim"
FRAMES = 7
TIER = 1.2
FRAME_WORLD = {"fighter": .9 * TIER, "chaser": .95 * TIER, "alien": .67 * TIER, "big": 1.3 * TIER, "rock": .66 * TIER}
FRAME_SCALE = {"ember_alien": 1.36}
# The world's cue hue (WorldCue.Hue: the enemy shots' colour); the art's glow is drawn in it, so the band finds the glow.
CUE = {"space": 318, "frost": 195, "verdant": 135, "ember": 32, "tide": 188, "storm": 258, "desert": 45}
SIGNAL_HUE = {w: (h - 30, h + 30) for w, h in CUE.items()}
SPARK = {"space": (255, 120, 220, 255), "frost": (190, 235, 255, 255), "verdant": (190, 255, 170, 255), "ember": (255, 200, 90, 255),
         "tide": (150, 255, 240, 255), "storm": (205, 180, 255, 255), "desert": (255, 240, 190, 255)}
BEHAVIOURS = HERE.parents[3] / "Scripts/Gameplay/Enemies/EnemyBehaviours.cs"


def muzzle_of(key):
    """EnemyBehaviours: B("key", ...) ... .Muzzle(x) before the next B( -- default .3 u."""
    import re
    s = BEHAVIOURS.read_text()
    i = s.find(f'B("{key}"')
    if i < 0: return .3
    j = s.find('B("', i + 3); seg = s[i:j if j > 0 else None]
    m = re.search(r"\.Muzzle\(([-\d.]+)f?\)", seg)
    return float(m.group(1)) if m else .3


def close_cut(im):
    """A drawing that runs off its canvas has a flat sliced edge: pad it and close that edge with the outline colour."""
    a = np.array(im.convert("RGBA"))
    op = a[:, :, 3] > 128
    if not op.any(): return im
    ink = a[op][np.argmin(a[op][:, :3].astype(int).sum(1))]
    a = np.pad(a, ((2, 2), (2, 2), (0, 0)))
    op = a[:, :, 3] > 128; H, W = op.shape
    for side in ("top", "bottom", "left", "right"):
        if side == "top" and op[2].any(): a[1][op[2]] = ink
        if side == "bottom" and op[H - 3].any(): a[H - 2][op[H - 3]] = ink
        if side == "left" and op[:, 2].any(): a[:, 1][op[:, 2]] = ink
        if side == "right" and op[:, W - 3].any(): a[:, W - 2][op[:, W - 3]] = ink
    return Image.fromarray(a, "RGBA")


def role(key):
    return next((r for r in ("fighter", "chaser", "alien", "big", "rock") if f"_{r}" in key), "fighter")


def trim(im):
    a = np.asarray(im)[:, :, 3]
    ys, xs = np.nonzero(a > 128)
    return im.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))


def hard(im):
    a = np.array(im); a[:, :, 3] = np.where(a[:, :, 3] > 128, 255, 0); return Image.fromarray(a, "RGBA")


def key_cell(src, old0, cell, others=()):
    """Frame 0 (and any animation frames drawn on the same canvas) keyed, scaled and placed with ONE transform taken from
    frame 0, so the body stays registered from frame to frame and only what the animation moves (wings, rotors, flames) moves."""
    srcs = [close_cut(x.convert("RGBA")) for x in (src,) + tuple(others)]
    a0 = np.asarray(hard(srcs[0]))[:, :, 3] > 128
    ys, xs = np.nonzero(a0)
    x0, y0, x1, y1 = xs.min(), ys.min(), xs.max() + 1, ys.max() + 1
    f = max(trim(old0).size) / max(x1 - x0, y1 - y0)
    out = []
    for s in srcs:
        h = hard(s)
        if h.size != srcs[0].size: h = h.resize(srcs[0].size, Image.NEAREST)
        big = h.resize((max(1, round(h.width * f)), max(1, round(h.height * f))), Image.NEAREST)
        c = Image.new("RGBA", (cell, cell))
        ox = round(cell / 2 - (x0 + x1) / 2 * f); oy = round(cell / 2 - (y0 + y1) / 2 * f)
        c.alpha_composite(big, (ox, oy)) if ox >= 0 and oy >= 0 else c.paste(big, (ox, oy), big)
        out.append(c)
    return out if others else out[0]


def signal_mask(im, world):
    a = np.asarray(im).astype(np.float32)
    hsv = np.asarray(im.convert("RGB").convert("HSV")).astype(np.float32)
    h, s, v = hsv[..., 0] * 360 / 255, hsv[..., 1] / 255, hsv[..., 2] / 255
    lo, hi = SIGNAL_HUE[world]
    return (a[..., 3] > 0) & (h >= lo) & (h <= hi) & (s > .45) & (v > .6)


def brighten(im, mask, k):
    a = np.asarray(im).astype(np.float32)
    rgb = a[..., :3]
    rgb[mask] = np.clip(rgb[mask] * (1 + k) + 255 * max(0, k) * .25, 0, 255)
    a[..., :3] = rgb
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def shift(im, dy):
    c = Image.new("RGBA", im.size); c.alpha_composite(im, (0, dy)); return c


def sparks(im, mask, color, n, seed):
    rng = np.random.default_rng(seed)
    out = im.copy(); px = out.load(); W, H = im.size
    ys, xs = np.nonzero(mask)
    if not len(xs): return out
    for i in rng.choice(len(xs), size=min(n, len(xs)), replace=False):
        x, y = int(xs[i]), int(ys[i])
        L = int(rng.integers(7, 13))
        for t in range(-L, L + 1):
            for (xx, yy) in ((x + t, y), (x, y + t)):
                if 0 <= xx < W and 0 <= yy < H and abs(t) <= L:
                    px[xx, yy] = (255, 255, 255, 255) if abs(t) <= 1 else color
    return out


def flash(im, k, seed):
    a = np.asarray(im).astype(np.float32)
    op = a[..., 3] > 0
    a[op, :3] = a[op, :3] * (1 - k) + 255 * k
    out = Image.fromarray(a.astype(np.uint8), "RGBA").copy()
    rng = np.random.default_rng(seed); px = out.load(); W, H = im.size
    ys, xs = np.nonzero(op)
    cy, cx = ys.mean(), xs.mean()
    for _ in range(9):
        ang = rng.uniform(0, 2 * np.pi); r = rng.uniform(.45, .62) * max(np.ptp(xs), np.ptp(ys))
        x, y = int(cx + r * np.cos(ang)), int(cy + r * np.sin(ang))
        for dx in range(3):
            for dy in range(3):
                if 0 <= x + dx < W and 0 <= y + dy < H: px[x + dx, y + dy] = (200, 196, 190, 255)
    return out


def flappers():
    import re
    src = (HERE.parents[3] / "Scripts/Gameplay/Enemies/EnemyRoster.cs").read_text()
    blk = src[src.index("WingFlapKeys ="):]; blk = blk[:blk.index("};")]
    return set(re.findall(r'"(\w+)",', blk))


def glow_only(base, frame, world):
    """A calm (non-flapping) enemy keeps frame 0's hull pixel for pixel: the animated frame is first aligned to it (best
    integer shift within 6 px), then only its glowing pixels -- flames, lights, core: bright saturated, or in the world's
    cue band -- plus a 2 px rim around them are taken from it."""
    b = np.asarray(base).astype(np.int32); best, bs = None, 1 << 60
    for dy in range(-6, 7):
        for dx in range(-6, 7):
            f = np.roll(np.roll(np.asarray(frame).astype(np.int32), dy, 0), dx, 1)
            d = np.abs((f[..., 3] > 128).astype(int) - (b[..., 3] > 128).astype(int)).sum()
            if d < bs: bs, best = d, f
    f = best.astype(np.uint8)
    def glowing(a):
        im = Image.fromarray(a, "RGBA"); hsv = np.asarray(im.convert("RGB").convert("HSV")).astype(np.float32)
        return (a[..., 3] > 128) & (((hsv[..., 1] > 110) & (hsv[..., 2] > 170)) | signal_mask(im, world))
    m = glowing(np.asarray(base)) | glowing(f)
    from scipy.ndimage import binary_dilation
    m = binary_dilation(m, iterations=2)
    ys, xs = np.nonzero(np.asarray(base)[..., 3] > 32)
    box = np.zeros_like(m); box[ys.min():ys.max() + 1, xs.min():xs.max() + 1] = True
    m &= box                                     # the bounding box never changes (CalmIdleTest: <= 3 %)
    m &= f[..., 3] > 128                         # and never shrinks: flames only recolour, never cut away
    out = np.asarray(base).copy(); out[m] = f[m]
    return Image.fromarray(out, "RGBA")


IDLE_LIMITS = {"ember": (3, 5, 14, 8), "verdant": (3, 8, 60, 24)}   # EmberFramingTest / VerdantFramingTest: drift x/y, bbox change w/h


def idle_ok(idle, world):
    bs = []
    for c in idle:
        ys, xs = np.nonzero(np.asarray(c)[..., 3] > 8); bs.append((xs.min(), ys.min(), xs.max(), ys.max()))
    cx = [(b[0] + b[2]) / 2 for b in bs]; cyy = [(b[1] + b[3]) / 2 for b in bs]; w = [b[2] - b[0] for b in bs]; h = [b[3] - b[1] for b in bs]
    dx, dy, dw, dh = IDLE_LIMITS.get(world, (6, 8, 999, 999))
    return max(cx) - min(cx) <= dx and max(cyy) - min(cyy) <= dy and max(w) - min(w) <= dw and max(h) - min(h) <= dh


def calm(idle, cell):
    """CalmIdleTest's art rule: between idle cells the centroid moves <= 1.5 % of the cell and the bbox changes <= 3 %."""
    cs, bs = [], []
    for c in idle:
        a = np.asarray(c)[:, :, 3] > 32; ys, xs = np.nonzero(a)
        cs.append((xs.mean(), ys.mean())); bs.append((np.ptp(xs) + 1, np.ptp(ys) + 1))
    dc = max(np.hypot(a[0] - b[0], a[1] - b[1]) for a in cs for b in cs)
    db = max(max(abs(a[0] - b[0]), abs(a[1] - b[1])) for a in bs for b in bs)
    return dict(idle_centroid_px=round(float(dc), 2), idle_bbox_px=int(db), calm_ok=bool(dc <= .015 * cell and db <= .03 * cell))


MARGIN = 7   # every test's edge margin is <= 6 px (DesertEmberFrame / Verdant / Frost stills); nothing is ever drawn closer


def clip_margin(im, m=MARGIN):
    a = np.array(im); a[:m] = 0; a[-m:] = 0; a[:, :m] = 0; a[:, -m:] = 0; return Image.fromarray(a, "RGBA")


def sil_change(a, b):
    """DesertEmberFrameTest.Change(silhouetteOnly): share of the union silhouette whose alpha differs."""
    pa, pb = np.asarray(a)[..., 3] > 40, np.asarray(b)[..., 3] > 40
    u = (pa | pb).sum(); return float((pa != pb).sum()) / max(1, u)


def tsfr_diff(a, b):
    """TideStormFramesTest.Diff: the share of the union (alpha > 40) that differs in alpha or colour (> 40), at the best of 9 shifts (+-3 px)."""
    A = np.asarray(a).astype(int); B0 = np.asarray(b).astype(int); h = A.shape[0]; best = 9.0
    for dy in (-3, 0, 3):
        for dx in (-3, 0, 3):
            B = np.zeros_like(B0)
            ys0, ys1 = max(0, -dy), min(h, h - dy); xs0, xs1 = max(0, -dx), min(h, h - dx)
            B[ys0:ys1, xs0:xs1] = B0[ys0 + dy:ys1 + dy, xs0 + dx:xs1 + dx]
            ma, mb = A[..., 3] > 40, B[..., 3] > 40; u = ma | mb
            d = (ma != mb) | (ma & mb & (np.abs(A[..., :3] - B[..., :3]).max(-1) > 40))
            best = min(best, d[u].sum() / max(1, u.sum()))
    return float(best)


def break_runs(im, cell):
    """EnemyRosterTest 'one whole pose': no straight vertical silhouette edge as long as 15 % of the cell (reads as art cut by the frame).
    Every edge run gets a 1 px notch before it reaches the limit -- invisible at play size."""
    a = np.array(im); op = a[..., 3] > 128; lim = int(np.ceil(.15 * cell)) - 6; h, w = op.shape
    for side in (-1, 1):
        for _ in range(3):
            changed = False
            for x in range(w):
                nx = x + side; run = 0
                for y in range(h):
                    out = nx < 0 or nx >= w or a[y, nx, 3] == 0
                    run = run + 1 if (op[y, x] and out) else 0
                    if run >= lim: a[y, x, 3] = 0; op[y, x] = False; run = 0; changed = True
            if not changed: break
    return Image.fromarray(a, "RGBA")


def guns_of(key):
    """Shot origins (x offsets in u) from EnemyBehaviours: Twin gap / shot count; centre when there is no twin."""
    import re
    s = BEHAVIOURS.read_text(); i = s.find(f'B("{key}"')
    if i < 0: return [0.0]
    j = s.find('B("', i + 3); seg = s[i:j if j > 0 else None]
    gap = re.search(r"\.Twin\(([-\d.]+)", seg); shot = re.search(r"\.Shot\(\w+,\s*(\d+)", seg)
    if gap and shot:
        n = int(shot.group(1)); g = float(gap.group(1)); return [(k - (n - 1) / 2) * g for k in (range(n) if n <= 2 else (0, n - 1))]
    return [0.0]


def flare(base, pts, r, color, world):
    """A charging muzzle flare: an opaque glow disc in the world's colour, a white-hot core and four short rays, at each gun."""
    out = base.copy(); px = out.load(); W, H = out.size
    core = (255, 255, 255, 255); edge = tuple(color[:3]) + (255,)
    for (cx, cy) in pts:
        for y in range(int(cy - 2 * r), int(cy + 2 * r) + 1):
            for x in range(int(cx - 2 * r), int(cx + 2 * r) + 1):
                if not (0 <= x < W and 0 <= y < H): continue
                d = ((x - cx) ** 2 + (y - cy) ** 2) ** .5
                ray = (abs(x - cx) <= 1 and abs(y - cy) <= 2 * r) or (abs(y - cy) <= 1 and abs(x - cx) <= 2 * r)
                if d <= r * .45: px[x, y] = core
                elif d <= r or ray: px[x, y] = edge if d > r * .7 or ray else (255, 245, 230, 255)
    return out


def tell_pair(key, base, cell, world, idle=()):
    """Tell warm-up and peak: a muzzle flare at the real gun points that grows until the silhouette changes >= 7.5 % (warm-up)
    and >= 13 % (peak; and >= 5 % from the warm-up) -- the DesertEmberFrameTest poses -- with the centroid kept within 7 px."""
    r_ = role(key); fw = FRAME_WORLD[r_] * FRAME_SCALE.get(key, 1); pu = cell / fw
    mz = muzzle_of(key) * TIER
    a = np.asarray(base)[..., 3] > 128; ys, xs = np.nonzero(a)
    y = cell / 2 + mz * pu if mz > .05 else ys.max() - 4          # a ring / centre attack flares at the hull's front
    y = min(y, cell - MARGIN - 14)
    pts = [(cell / 2 + g * TIER * pu, y) for g in guns_of(key)]
    col = SPARK[world]
    def cy(im):
        ys = np.nonzero(np.asarray(im)[..., 3] > 8)[0]; return ys.mean()
    ics = [cy(c) for c in idle] or [cy(base)]
    lo, hi = max(ics) - 7.5, min(ics) + 7.5            # all seven cells' centroids within 8 px (DesertEmberFrameTest)
    def make(target, floor=None):
        best, bscore = None, 1e9
        for r in range(5, int(cell * .16)):
            for dy in range(0, 7):          # the ship rears back as it charges: keeps the centroid on the pivot
                f = clip_margin(flare(shift(base, -dy), [(x, yy - dy) for x, yy in pts], r, col, world))
                c = min(sil_change(base, f), tsfr_diff(base, f) - .01); yc = cy(f); drift = max(0, lo - yc, yc - hi)
                ok = c >= target and (floor is None or min(sil_change(floor, f), tsfr_diff(floor, f)) >= .05) and drift == 0
                if ok: return f
                score = max(0, target - c) * 100 + drift
                if score < bscore: best, bscore = f, score
        return best
    warm = make(.075); peak = make(.13, warm)
    return warm, peak


def recoil(base, cell):
    """The hit cell: the hull knocked UP a few px (a real silhouette change >= 7 %), white-flashed INSIDE its dark outline (no pale rim),
    with grey chips kept inside the cell margin."""
    ys0 = np.nonzero(np.asarray(base)[..., 3] > 8)[0].mean()
    for dy in range(3, 9):
        moved = shift(base, -dy)
        if sil_change(base, moved) >= .07 or dy >= 6: break      # <= 6 px: the seven cells keep one pivot (<= 8 px with the tell)
    a = np.asarray(moved).astype(np.float32); op = a[..., 3] > 128
    from scipy.ndimage import binary_erosion
    inner = binary_erosion(op, structure=np.ones((3, 3)), iterations=3)   # RimCheck's band is the 5x5 neighbourhood
    a[inner, :3] = a[inner, :3] * .45 + 255 * .55
    out = Image.fromarray(a.astype(np.uint8), "RGBA").copy(); px = out.load()
    rng = np.random.default_rng(6); ys, xs = np.nonzero(op); cy, cx = ys.mean(), xs.mean()
    for _ in range(9):
        ang = rng.uniform(0, 2 * np.pi); rr = rng.uniform(.42, .55) * max(np.ptp(xs), np.ptp(ys))
        x, y = int(cx + rr * np.cos(ang)), int(cy + rr * np.sin(ang))
        for ddx in range(3):
            for ddy in range(3):
                if xs.min() <= x + ddx <= xs.max() and MARGIN <= y + ddy < cell - MARGIN: px[x + ddx, y + ddy] = (92, 88, 84, 255)   # inside the hull's width: the bbox centre stays
    return clip_margin(out)


def build(key, src_path, install=True):
    world = key.split("_")[0]
    live = LIVE / f"{key}.png"
    OLD.mkdir(exist_ok=True); OUT.mkdir(exist_ok=True); KEYS.mkdir(exist_ok=True)
    if not (OLD / f"{key}.png").exists(): shutil.copy(live, OLD / f"{key}.png")
    old = Image.open(OLD / f"{key}.png").convert("RGBA")
    cell = old.height
    adir = ANIM / key
    afr = [Image.open(adir / f"f_{i}.png") for i in range(1, 4)] if (adir / "f_3.png").exists() else []
    if afr:   # real idle animation (anim.py: PixelLab animate-with-text from the approved drawing): f_0 is the drawing itself
        cells = key_cell(Image.open(src_path), old.crop((0, 0, cell, cell)), cell, afr)
        k = cells[0]
    else:
        k = key_cell(Image.open(src_path), old.crop((0, 0, cell, cell)), cell)
    k.save(KEYS / f"{key}.png")
    m = signal_mask(k, world)
    frames = []
    if afr and key in flappers() and not idle_ok(cells, world):
        print(f"{key}: wing beat exceeds {world}'s idle framing limits; idle falls back to flames and lights", file=sys.stderr)
    if afr and (key not in flappers() or not idle_ok(cells, world)):
        cells = [cells[0]] + [glow_only(cells[0], c, world) for c in cells[1:]]
    if afr:
        for c, glow in zip(cells, (0, .45, .22, .9)):     # every idle pair differs >= 1 % (FrameStills / DesertEmberFrame)
            frames.append(brighten(c, signal_mask(c, world), glow))
    else:
        for i, (dy, glow) in enumerate(((0, 0), (-1, .18), (-2, .05), (-1, .28))):
            frames.append(shift(brighten(k, m, glow), dy))
    # TideStormFramesTest: every idle pair >= .5 % apart even after a +-3 px re-alignment -- widen the pulsing lights until it holds
    from scipy.ndimage import binary_dilation as _dil
    for grow in range(1, 6):
        if role(key) == "rock" or min(tsfr_diff(frames[a], frames[b]) for a in range(4) for b in range(a + 1, 4)) >= .007: break
        src = cells if afr else [k] * 4
        frames = [frames[0]] + [brighten(c, _dil(signal_mask(c, world), iterations=grow * 2), min(1, g * (1 + .3 * grow)))
                                for c, g in zip(src[1:], (.45, .22, .9))]
    if role(key) == "rock":
        # rocks never attack: four genuinely different idle cells (FrameStillsTest: >= 1 % between every pair), a glow tell, a recoil hit
        frames = [shift(brighten(k, m, g), dy) for dy, g in ((0, 0), (-1, .2), (-2, .05), (-1, .35))]
        frames[3] = Image.fromarray(np.roll(np.asarray(frames[3]), 1, axis=1))
        from scipy.ndimage import binary_erosion
        inner = m & binary_erosion(np.asarray(k)[..., 3] > 40, iterations=3)    # no pale rim (FrameStillsTest.RimCheck: 2 px band < 6 %)
        frames += [brighten(k, inner, .6), brighten(k, inner, 1.0)]
    else:
        frames += list(tell_pair(key, brighten(k, m, .6), cell, world, frames[:4]))
    frames.append(recoil(k, cell))
    frames = [break_runs(clip_margin(f), cell) for f in frames]
    strip = Image.new("RGBA", (cell * FRAMES, cell))
    for i, f in enumerate(frames): strip.alpha_composite(hard(f), (i * cell, 0))
    strip.save(OUT / f"{key}.png", optimize=True)
    if install: shutil.copy(OUT / f"{key}.png", live)
    # checks
    r = role(key); fw = FRAME_WORLD[r] * FRAME_SCALE.get(key, 1)
    edge_old, edge_new = max(trim(old.crop((0, 0, cell, cell))).size), max(trim(k).size)
    a = np.asarray(k)[:, :, 3] > 128; ys, xs = np.nonzero(a)
    front = (ys.max() + 1 - cell / 2) / cell * fw                      # world u below centre
    muzzle = muzzle_of(key) * TIER
    reds = [(0xD8, 0x23, 0x2C), (0x86, 0x12, 0x1F), (0xFF, 0x5B, 0x45)]
    px = np.asarray(strip)[np.asarray(strip)[:, :, 3] > 128][:, :3].astype(int)
    red = sum(((np.abs(px - np.array(c)).sum(1) <= 24)).sum() for c in reds) / max(1, len(px))
    rep = dict(key=key, edge_old=edge_old, edge_new=edge_new, front_u=round(front, 3), muzzle_u=round(muzzle, 3),
               muzzle_ok=bool(front - .25 <= muzzle <= front + .08), signal_px=int(m.sum()), player_red=round(float(red), 4),
               animated=bool(afr), **calm(frames[:4], cell))
    print(json.dumps(rep))
    return rep


if __name__ == "__main__":
    build(sys.argv[1], sys.argv[2], "--no-install" not in sys.argv)
