"""Spin the rotor of a Paragon-standard helicopter strip (Resources/Enemies/<key>.png, 7 square 192 px cells).

The Paragon redraw (paragon_standard/build.py) painted Salvage Gyro and Tandem Skimmer with their rotor as one fixed
X: the four idle cells only bob and flicker, so the "helicopter: the rotor spins" flappers (EnemyRoster.WingFlapKeys)
never spun -- in flight or in the codex, which plays the same idle cells.

This lifts the X off every cell (the band it covered is filled back from its edges with the most common neighbouring
colour, hard alpha), then lays the SAME painted blades back, sampled 1:1 from the cell's own drawing, at a new angle:

    idle 0-3   0, 22.5, 45, 67.5 deg   (a four-blade cross repeats every 90, so the loop turns smoothly)
    tell 4-5   11.25, 33.75 deg
    hit  6     0 deg

A blade on a diagonal reaches the cell's corner, but one turned towards an edge would run off the cell, so every blade
is cut to the rotor disc that fits the cell (the inner part kept pixel for pixel, the painted tip cap re-attached).
The hub cap is put back on top. Hull, canopy, pods and glow are untouched outside the blade band.

    python3 spin_paragon_rotors.py tide_fighter_5 tide_fighter_6        (keeps <key>.orig.png beside this script)
"""
from __future__ import annotations
import math, sys
from collections import Counter
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
RES = HERE.parents[2] / "Resources/Enemies"
CELL, CELLS = 192, 7
ANGLES = (0.0, 22.5, 45.0, 67.5, 11.25, 33.75, 0.0)
MARGIN = 3          # px kept clear between a blade tip and the cell edge
CAP = 7             # px of painted tip cap re-attached to a shortened blade

# key -> rough hub (cell px) and hub-cap radius
SPECS = {"tide_fighter_5": dict(hub=(96, 85), hubr=7), "tide_fighter_6": dict(hub=(96, 95), hubr=7)}


def bone(c):
    r, g, b, a = (int(x) for x in c)
    return a > 0 and r > 140 and g > 105 and b > 60 and r - b > 25


def fit(cell, hub0):
    """Two diagonals fitted through the bone-coloured blade pixels; returns hub, 4 arm angles (rad), lengths, half-width."""
    H = cell.shape[0]
    ys, xs = np.mgrid[0:H, 0:H]
    isb = np.zeros((H, H), bool)
    for y in range(H):
        for x in range(H):
            isb[y, x] = bone(cell[y, x])
    lines = []
    for base in (45.0, -45.0):
        u = np.array([math.cos(math.radians(base)), math.sin(math.radians(base))])
        dx, dy = xs - hub0[0], ys - hub0[1]
        along, perp = dx * u[0] + dy * u[1], -dx * u[1] + dy * u[0]
        sel = isb & (np.abs(perp) < 10) & (np.abs(along) > 16)
        pts = np.stack([xs[sel], ys[sel]], 1).astype(float)
        c = pts.mean(0)
        w, v = np.linalg.eigh(np.cov((pts - c).T))
        d = v[:, np.argmax(w)]
        lines.append((c, d))
    (c1, d1), (c2, d2) = lines
    t = np.linalg.solve(np.array([d1, -d2]).T, c2 - c1)
    hub = c1 + t[0] * d1
    arms, lens, perps = [], [], []
    for _, d in lines:
        for sgn in (1, -1):
            u = d * sgn
            ang = math.atan2(u[1], u[0])
            dx, dy = xs - hub[0], ys - hub[1]
            along, perp = dx * u[0] + dy * u[1], -dx * u[1] + dy * u[0]
            band = (np.abs(perp) < 8) & (along > 12)
            op = band & (cell[..., 3] > 0) & isb
            arms.append(ang)
            lens.append(float(along[op].max()))
            perps.append(np.abs(perp[op & (along < along[op].max() - CAP)]))
    hw = float(np.percentile(np.concatenate(perps), 97)) + 1.5     # bone core + its dark outline
    return hub, arms, lens, hw


def band_mask(hub, arms, lens, hw, H, pad_w=1.0, pad_l=2.0):
    ys, xs = np.mgrid[0:H, 0:H]
    m = np.zeros((H, H), bool)
    for ang, L in zip(arms, lens):
        u = (math.cos(ang), math.sin(ang))
        dx, dy = xs - hub[0], ys - hub[1]
        along, perp = dx * u[0] + dy * u[1], -dx * u[1] + dy * u[0]
        m |= (np.abs(perp) <= hw + pad_w) & (along >= -1) & (along <= L + pad_l)
    return m


def mode_fill(cell, unknown):
    """Grow the known pixels into the unknown ones, each taking the most common colour among its known 8 neighbours."""
    out = cell.copy()
    unk = unknown.copy()
    H = cell.shape[0]
    while unk.any():
        grow = []
        for y, x in zip(*np.nonzero(unk)):
            cnt = Counter()
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = y + dy, x + dx
                    if (dy or dx) and 0 <= yy < H and 0 <= xx < H and not unk[yy, xx]:
                        cnt[tuple(out[yy, xx])] += 2 if (dx == 0 or dy == 0) else 1
            if cnt:
                grow.append((y, x, cnt.most_common(1)[0][0]))
        if not grow:
            break
        for y, x, c in grow:
            out[y, x] = c
            unk[y, x] = False
    return out


def draw_blades(plate, src, hub, arms, lens, hw, theta, R):
    out = plate.copy()
    H = plate.shape[0]
    th = math.radians(theta)
    for ang, L in zip(arms, lens):
        a2 = ang + th
        u2 = (math.cos(a2), math.sin(a2))
        u0 = (math.cos(ang), math.sin(ang))
        for y in range(H):
            for x in range(H):
                dx, dy = x + .5 - (hub[0] + .5), y + .5 - (hub[1] + .5)
                s, p = dx * u2[0] + dy * u2[1], -dx * u2[1] + dy * u2[0]
                if s < 0 or s > R or abs(p) > hw:
                    continue
                s0 = s if s < R - CAP else L - (R - s)        # inner blade 1:1, the last CAP px from the painted tip
                sx = hub[0] + s0 * u0[0] - p * u0[1]
                sy = hub[1] + s0 * u0[1] + p * u0[0]
                ix, iy = int(round(sx)), int(round(sy))
                if 0 <= ix < H and 0 <= iy < H and src[iy, ix, 3] > 0:
                    out[y, x] = src[iy, ix]
    return out


GLOW_CELLS = (1, 2, 3, 4, 5)


def bone_mask(c):
    r, g, b, a = (c[..., i].astype(int) for i in range(4))
    return (a > 0) & (r > 140) & (g > 105) & (b > 60) & (r - b > 25)


def glow(c):
    """The Tide cue band (cyan / teal light: canopy, flames, spark crosses) and near-white highlights."""
    hsv = np.asarray(Image.fromarray(c, "RGBA").convert("RGB").convert("HSV")).astype(np.float32)
    h, s, v = hsv[..., 0] * 360 / 255, hsv[..., 1] / 255, hsv[..., 2] / 255
    cue = (h >= 150) & (h <= 220) & (s > .3) & (v > .5)
    white = c[..., :3].min(-1) > 215
    return (c[..., 3] > 0) & (cue | white)


def lift(c0, c):
    """How many px cell c is raised against cell 0 (best vertical roll of c onto c0)."""
    best = None
    for dy in range(-6, 7):
        d = (np.abs(np.roll(c, dy, 0)[..., :3].astype(int) - c0[..., :3].astype(int)).sum(-1) > 30).sum()
        if best is None or d < best[0]:
            best = (d, dy)
    return best[1] if best[0] < 3000 else 0


def flash_like(drawn, hit, keep):
    """The hit cell's white wash (estimated from the original hit cell) over the new drawing, plus its loose chips."""
    d, h = drawn.astype(np.float32), hit.astype(np.float32)
    m = keep & (drawn[..., 3] > 0) & (hit[..., 3] > 0)
    k = float(np.median(((h[..., :3] - d[..., :3]) / np.maximum(255 - d[..., :3], 1))[m]))
    k = min(max(k, 0.0), .9)
    out = drawn.copy()
    op = out[..., 3] > 0
    out[op, :3] = np.clip(d[op, :3] * (1 - k) + 255 * k, 0, 255).astype(np.uint8)
    from scipy.ndimage import label
    loose = (hit[..., 3] > 0) & ~op
    lab, n = label(loose)
    for j in range(1, n + 1):
        comp = lab == j
        if comp.sum() <= 12:
            out[comp] = hit[comp]
    print("  hit wash", round(k, 2))
    return out


def run(key):
    spec = SPECS[key]
    path = RES / f"{key}.png"
    keep = HERE / f"{key}.orig.png"
    if not keep.exists():
        Image.open(path).save(keep)
    strip = np.array(Image.open(keep).convert("RGBA"))
    cells = [strip[:, i * CELL:(i + 1) * CELL].copy() for i in range(CELLS)]
    hub, arms, lens, hw = fit(cells[0], spec["hub"])
    # rotor disc: the largest radius that stays inside the cell (minus the blade's half-width and a margin)
    R = min(hub[0], hub[1], CELL - 1 - hub[0], CELL - 1 - hub[1]) - hw - MARGIN
    R = min(R, min(lens))
    print(key, "hub", np.round(hub, 1), "arms", [round(math.degrees(a), 1) for a in arms], "len", [round(l) for l in lens],
          "hw", round(hw, 1), "-> rotor radius", round(R, 1))
    wide = band_mask(hub, arms, lens, hw, CELL, pad_w=3.5, pad_l=8)
    hy, hx = np.mgrid[0:CELL, 0:CELL]
    hubdisc = (hx - hub[0]) ** 2 + (hy - hub[1]) ** 2 <= spec["hubr"] ** 2
    c0 = cells[0]
    plate = mode_fill(c0, wide & ~hubdisc)
    plate[hubdisc] = c0[hubdisc]
    from scipy.ndimage import label
    lab, n = label(plate[..., 3] > 0)                 # stray bits of the old blade tips left floating outside the hull
    for j in range(1, n + 1):
        if (lab == j).sum() < 60:
            plate[lab == j] = 0
    out = []
    for i, c in enumerate(cells):
        dy = lift(c0, c)                     # the tell / hit cells are cell 0 raised a few px
        base = plate.copy()
        if i in GLOW_CELLS:
            g = glow(c) & ~bone_mask(c)
            if dy == 0:
                g &= plate[..., 3] > 0       # an idle cell's own glow, never past the hull's outline
            g = np.roll(g, dy, 0)            # into cell 0's frame
            base[g] = np.roll(c, dy, 0)[g]
        drawn = draw_blades(base, c0, hub, arms, lens, hw, ANGLES[i], R)
        drawn[hubdisc] = base[hubdisc]
        drawn = np.roll(drawn, -dy, 0)
        if i == 6:
            drawn = flash_like(drawn, c, ~np.roll(wide, -dy, 0))
        drawn[..., 3] = np.where(drawn[..., 3] > 128, 255, 0)
        out.append(drawn)
    Image.fromarray(np.concatenate(out, 1), "RGBA").save(path)
    print("wrote", path)


if __name__ == "__main__":
    for k in sys.argv[1:] or list(SPECS):
        run(k)
