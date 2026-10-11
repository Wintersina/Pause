#!/usr/bin/env python3
"""Ember fire pass: hard-stepped pixel-art flame tongues (deep red-orange rim, orange body, yellow-orange core, a white-hot root) drawn
BEHIND the real hull pixels of selected units, flickering across the 7 cells (four different idle drawings, a bigger tell, the biggest
action, a guttering hit). Hull pixels are never touched; flames sit at anchors found on the hull's real silhouette.
Palette (hue): rim (222,84,22) ~18, body (255,128,32) ~26, core (255,184,64) ~36 small, root (255,236,180) tiny. No pink, no red band.

  python3 fire.py            # enemies: reads Resources/Enemies (already bordered) -> overwrites them;  elites: out/elites -> out/elites_glow
"""
import math, sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]
RIM, BODY, CORE, ROOT = (222, 84, 22), (255, 128, 32), (255, 184, 64), (255, 236, 180)
PHASE = [0.0, 1.7, 3.3, 4.9, 2.4, 0.8, 5.5]
LEN = [1.0, .84, 1.12, .92, 1.30, 1.65, .42]          # idle x4, tell, action, hit
EDGE = 8                                              # keep every flame pixel this far inside the cell


def tongue(canvas, ax, ay, direction, length, width, ph, seed):
    """One flame: from (ax, ay) outward (direction -1 = up the screen, +1 = down), `length` px, base `width` px."""
    rng = np.random.default_rng(seed)
    S = canvas.shape[0]
    L = int(round(length))
    for t in range(L + 1):
        u = t / max(L, 1)
        sway = (1.6 + 1.4 * u) * math.sin(ph * 1.3 + t * .42) * u
        flick = 1 + .22 * math.sin(ph * 2.1 + t * .9)
        hw = width / 2.0 * (1 - u) ** .85 * flick
        y = ay + direction * t
        if not (EDGE <= y < S - EDGE):
            continue
        for layer, (col, k, umax) in enumerate(((RIM, 1.0, 1.0), (BODY, .66, .88), (CORE, .36, .66), (ROOT, .16, .30))):
            if u > umax:
                continue
            h = max(hw * k, .6 if layer < 2 else 0)
            if h <= 0:
                continue
            x0, x1 = int(round(ax + sway - h)), int(round(ax + sway + h))
            for x in range(x0, x1 + 1):
                if EDGE <= x < S - EDGE:
                    canvas[int(y), x] = (*col, 255)
    # embers: a few detached 1-2 px sparks beyond the tip
    for i in range(3):
        d = L + 2 + int(rng.integers(0, 7)) + int(3 * math.sin(ph + i))
        sx = int(round(ax + rng.normal(0, width * .45) + 2 * math.sin(ph * 1.7 + i * 2)))
        sy = int(ay + direction * d)
        if EDGE <= sx < S - EDGE and EDGE <= sy < S - EDGE and (i + int(ph * 3)) % 2 == 0:
            canvas[sy, sx] = (*(CORE if i % 2 else BODY), 255)


def edge_y(alpha, x, direction):
    """First opaque row from the outside along column x (direction -1: topmost, +1: bottommost); None when empty."""
    col = np.nonzero(alpha[:, x] > 128)[0]
    if len(col) == 0:
        return None
    return int(col.min() if direction < 0 else col.max())


def flames_for(cell, specs, k, ph, seed):
    """specs: list of (fx, direction, length, width) with fx the column as a fraction of the hull's bbox."""
    S = cell.shape[0]
    a = cell[..., 3]
    ys, xs = np.nonzero(a > 128)
    x0, x1 = xs.min(), xs.max()
    lay = np.zeros((S, S, 4), np.uint8)
    for i, (fx, d, length, width) in enumerate(specs):
        x = int(round(x0 + (x1 - x0) * fx))
        y = edge_y(a, min(max(x, 0), S - 1), d)
        if y is None:
            continue
        tongue(lay, x, y + (2 if d < 0 else -2), d, length * 1.15 * k, width * 1.35, ph + i * 1.9, seed + i * 17)
    return lay


def overlay(strip, specs, seed=1):
    S = strip.height
    out = Image.new("RGBA", strip.size)
    for c in range(strip.width // S):
        cell = strip.crop((c * S, 0, c * S + S, S))
        lay = Image.fromarray(flames_for(np.asarray(cell), specs, LEN[c], PHASE[c], seed + c * 101), "RGBA")
        lay.alpha_composite(cell)
        out.alpha_composite(lay, (c * S, 0))
    return out


# enemies face DOWN, so their exhaust / tails are at the top (direction -1); a maw / jaw fire goes down (+1)
ENEMY_FIRE = {
    "ember_alien":     [(.50, -1, 16, 7), (.27, -1, 10, 5), (.73, -1, 10, 5)],           # a living flame: crest and shoulder licks
    "ember_chaser":    [(.50, -1, 15, 6)],                                              # the stack burns
    "ember_big":       [(.30, -1, 15, 6), (.50, -1, 19, 8), (.70, -1, 15, 6), (.50, +1, 13, 7)],   # stacks + a furnace maw
    "ember_fighter_1": [(.38, -1, 15, 5), (.62, -1, 15, 5)],                            # twin thrusters
    "ember_fighter_3": [(.50, -1, 14, 5), (.20, -1, 8, 4), (.80, -1, 8, 4)],            # flame fins
    "ember_fighter_4": [(.50, -1, 17, 7)],                                              # the furnace stack
}
# elites fly nose UP: exhaust leaves the nozzles downward
def elite_specs(key, nozzles):
    return nozzles


def overlay_points(strip, points, seed=1):
    """Flames at explicit points (elite nozzles): (x, y, direction, length, width) per cell k-scaled; hull composited over them."""
    S = strip.height
    out = Image.new("RGBA", strip.size)
    for c in range(strip.width // S):
        cell = strip.crop((c * S, 0, c * S + S, S))
        lay = np.zeros((S, S, 4), np.uint8)
        for i, (x, y, d, length, width) in enumerate(points):
            tongue(lay, x, y - 3 * d, d, length * LEN[c], width, PHASE[c] + i * 1.9, seed + c * 101 + i * 17)
        im = Image.fromarray(lay, "RGBA")
        im.alpha_composite(cell)
        out.alpha_composite(im, (c * S, 0))
    return out


def elites():
    global EDGE
    import json
    EDGE = 14
    src, dst = HERE / "out" / "elites", HERE / "out" / "elites_glow"
    dst.mkdir(parents=True, exist_ok=True)
    for f in sorted(src.glob("ember_elite_*.png")):
        d = json.load(open(ART / "Resources" / "Elites" / "Defs" / f"{f.stem}.json"))
        pts = [(n["x"], n["y"], +1, 26, 10) for n in d["nozzles"]]
        # a muzzle-side flare for the shooters would clutter: exhaust only
        overlay_points(Image.open(f).convert("RGBA"), pts, seed=sum(map(ord, f.stem))).save(dst / f.name)
        print("fire", f.name)
    EDGE = 8


def main():
    sys.path.insert(0, str(HERE))
    elites()
    for key, specs in ENEMY_FIRE.items():
        p = ART / "Resources" / "Enemies" / f"{key}.png"
        Image.open(p).convert("RGBA")
        out = overlay(Image.open(p).convert("RGBA"), specs, seed=sum(map(ord, key)))
        out.save(p)
        print("fire", key)


if __name__ == "__main__":
    main()
