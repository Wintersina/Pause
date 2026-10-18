"""Ember enemy death flipbooks: Resources/Enemies/Death/<key>.png (3 square cells, width = 3 x height).

Scripted from each enemy's REAL idle cell 3 (the idle strip is Codex's approved art, never edited), so cell 0 sits on the
live hull pixel for pixel (centroid within 3 px of idle cell 3):
  cell 0  the intact hull + a white-hot sun-white starburst with a pale-yellow edge, a tiny hostile-pink core accent
  cell 1  RUPTURE: the hull cut into chunks that part, dimmed and desaturated, white-blue discharge cracks (jagged arcs),
          chips and hail pellets flying, core flare on top
  cell 2  NO hull: the chunks flung wide and fading, slate vapour wisps, hail pellets, metal chips, sparse white glints
Palette: sun-white #F2F6FF, pale yellow #FFF3A0, cold white-blue arc, slate-cloud smoke. No red, no orange, no violet.
Nearest-neighbour everywhere (no resampling blur), hard-stepped alpha, 7 px clear margin in every cell.

Per-unit anatomy lives in UNITS (fracture count, rotation, arc count, vapour amount, extra flavour).

Run from the repo root:
  python3 Pause/Assets/Art/Enemies/src~/build_ember_enemy_death.py [key ...]
Writes Pause/Assets/Art/Resources/Enemies/Death/<key>.png and a copy + preview.png in src~/ember_death/.
"""
from __future__ import annotations
import math, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parents[5]
IDLE = Path(__import__("os").environ.get("EMBER_IDLE", REPO / "Pause/Assets/Art/Enemies/src~/ember_refresh/strips"))
OUT = Path(__file__).resolve().parent / "ember_refresh_death"
RES = OUT   # staged; install step copies to Resources/Enemies/Death

WHITE = (255, 222, 160)     # white-hot centre, warmed
YELLOW = (255, 176, 64)     # hot orange-yellow core
ARC = (255, 128, 40)        # forge orange (hue ~24): cracks, embers
ARC_DIM = (176, 86, 28)
SLATE = (58, 52, 50)        # ash smoke
SLATE_LT = (112, 102, 96)
TINT = (255, 150, 56)
PINK = (255, 222, 160)      # no pink on Ember: the core accent is the warm white-hot centre
if __import__("os").environ.get("DEATH_PALETTE") == "tide":   # the Tide helicopters: sea-spark blue-white, no fire
    WHITE, YELLOW, ARC, ARC_DIM = (236, 250, 255), (150, 220, 255), (110, 170, 255), (60, 96, 176)
    SLATE, SLATE_LT, PINK = (34, 44, 80), (84, 104, 150), (236, 250, 255)
    TINT = (150, 205, 255)

# key -> (cell side, chunks, spin deg, bolts, vapour puffs, pellets, flavour)
UNITS = {
    "ember_rock_magma":    (192, 16, 40, 5, 1, 14, "shatter"),
    "ember_rock_cinder":   (192, 15, 35, 4, 1, 10, "shatter"),
    "ember_rock_obsidian": (192, 14, 55, 5, 1, 8,  "shatter"),
    "ember_rock_islet":    (192, 12, 30, 5, 2, 8,  "tumble"),
    "ember_alien":         (192, 8,  60, 9, 1, 8,  "burst"),
    "ember_chaser":        (192, 10, 90, 6, 1, 8,  "rotor"),
    "ember_big":           (256, 18, 28, 7, 5, 14, "vent"),
    "ember_fighter_1":     (192, 10, 50, 5, 2, 8,  "snap"),
    "ember_fighter_2":     (192, 11, 50, 6, 2, 8,  "snap"),
    "ember_fighter_3":     (192, 12, 45, 7, 2, 10, "drum"),
    "ember_fighter_4":     (192, 14, 40, 9, 3, 12, "coil"),
    "ember_fighter_5":     (192, 16, 80, 7, 3, 10, "rotor"),   # the twin-rotor gunship (EMBER_IDLE=helicopters/out)
    "tide_fighter_5":      (192, 16, 80, 8, 2, 10, "rotor"),   # DEATH_PALETTE=tide EMBER_IDLE=helicopters/out
    "tide_fighter_6":      (192, 16, 80, 7, 2, 10, "rotor"),
    # elites (flight strip cell 3 is the hull; run with EMBER_IDLE=<elite strips folder>)
}


FIRE_UNITS = {"ember_alien", "ember_chaser", "ember_big", "ember_fighter_1", "ember_fighter_3", "ember_fighter_4",
              "ember_elite_sunstoke", "ember_elite_coalrunner", "ember_elite_brass_vulture", "ember_elite_kilnback",
              "ember_elite_ash_wraith", "ember_elite_cauterizer"}


def fire_under(frame, core, R, rng, n, length, width, phase):
    """Flame tongues rising round the burst (drawn UNDER the chunks): the death of a fire unit is a blaze, not only sparks."""
    sys.path.insert(0, str(Path(__file__).resolve().parent / "ember_recolor"))
    import fire
    S = frame.width
    lay = np.zeros((S, S, 4), np.uint8)
    old = fire.EDGE
    fire.EDGE = 10
    for i in range(n):
        a = rng.uniform(0, math.tau)
        rr = rng.uniform(R * .15, R * .62)
        x = int(core[0] + math.cos(a) * rr)
        y = int(core[1] + math.sin(a) * rr * .8 + R * .25)
        fire.tongue(lay, x, y, -1, length * rng.uniform(.7, 1.25), width * rng.uniform(.8, 1.3), phase + i * 1.3, int(rng.integers(1, 9999)))
    fire.EDGE = old
    f = Image.fromarray(lay, "RGBA")
    f.alpha_composite(frame)
    return f


def cell3(key, side):
    strip = Image.open(IDLE / f"{key}.png").convert("RGBA")
    return strip.crop((3 * side, 0, 4 * side, side))


def hull_info(im):
    a = np.asarray(im)[:, :, 3] > 128
    ys, xs = np.nonzero(a)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    return (int(xs.mean()), int(ys.mean())), (x0, y0, x1, y1)


def blend(im, amount, tint):
    a = np.asarray(im).astype(np.float32).copy()
    solid = a[:, :, 3] > 0
    a[solid, :3] = a[solid, :3] * (1 - amount) + np.array(tint, np.float32) * amount
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def desat(im, amount, dim):
    a = np.asarray(im).astype(np.float32).copy()
    g = a[:, :, :3].mean(axis=2, keepdims=True)
    a[:, :, :3] = (a[:, :, :3] * (1 - amount) + g * amount) * dim
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def disc(d, c, r, fill):
    d.ellipse((c[0] - r, c[1] - r, c[0] + r, c[1] + r), fill=fill)


def bolt(d, c, ang, length, rng, col_core, col_edge, width=2, jag=.28):
    """A jagged lightning polyline from c at angle ang: wide edge pass then 1 px core."""
    pts = [c]
    n = max(3, int(length / 9))
    x, y = c
    for i in range(1, n + 1):
        t = i / n
        a = ang + rng.normal(0, jag)
        step = length / n
        x += math.cos(a) * step
        y += math.sin(a) * step
        pts.append((x, y))
    d.line(pts, fill=col_edge, width=width)
    d.line(pts, fill=col_core, width=1)
    return pts


def vapour(d, c, r, rng, col, alpha):
    """A stepped vapour puff: 3 stacked discs, hard alpha steps."""
    for k, (rr, al) in enumerate(((r, alpha * .45), (r * .72, alpha * .7), (r * .42, alpha))):
        ox, oy = rng.normal(0, r * .08, 2)
        disc(d, (c[0] + ox, c[1] + oy), int(max(2, rr)), (*col, int(al)))


def pellet(d, x, y, s, col):
    d.ellipse((x - s, y - s, x + s, y + s), fill=col)
    d.point((x - s // 2, y - s // 2), fill=WHITE)


def chip(d, x, y, size, col, rng):
    a = rng.random() * math.tau
    ux, uy = math.cos(a), math.sin(a)
    d.polygon(((x + ux * size, y + uy * size), (x - uy * size * .45, y + ux * size * .45),
               (x - ux * size * .7, y - uy * size * .7), (x + uy * size * .45, y - ux * size * .45)), fill=col)


def build(key):
    S, nseed, spin, nbolt, npuff, npel, flavour = UNITS[key]
    base = cell3(key, S)
    (cx, cy), (x0, y0, x1, y1) = hull_info(base)
    core = (cx, cy)
    rng = np.random.default_rng(sum(map(ord, key)) * 7 + 3)
    R = max(x1 - x0, y1 - y0) / 2
    lim = S / 2 - 10                    # keep every bright thing 10 px inside the cell
    arr = np.asarray(base)
    ys, xs = np.nonzero(arr[:, :, 3] > 128)
    pick = rng.choice(len(xs), min(nseed, len(xs)), replace=False)
    seeds = np.stack([xs[pick], ys[pick]], 1).astype(np.float32)
    gy, gx = np.mgrid[0:S, 0:S]
    dist = np.stack([(gx - sx) ** 2 + (gy - sy) ** 2 + rng.normal(0, 16, (S, S)) for sx, sy in seeds])
    lab = dist.argmin(0)
    lab[arr[:, :, 3] <= 128] = -1

    def chunks(push, rot, dim, alpha, hot=0.0):
        layer = Image.new("RGBA", (S, S))
        order = sorted(range(len(seeds)), key=lambda i: -np.hypot(seeds[i][0] - cx, seeds[i][1] - cy))
        for i in order:
            m = lab == i
            if not m.any():
                continue
            part = np.zeros_like(arr)
            part[m] = arr[m]
            px, py = seeds[i]
            vx, vy = px - cx, py - cy
            n = math.hypot(vx, vy) or 1.0
            mv = push * (R * .3 + n)
            ox, oy = vx / n * mv, vy / n * mv
            if flavour in ("rotor",) and i % 3 == 0:
                ox, oy = ox * 1.8, oy * 1.8                # blades fly further
            img = Image.fromarray(part, "RGBA").rotate(float(rng.uniform(-rot, rot)), resample=Image.NEAREST, center=(px, py))
            bb = img.getchannel("A").getbbox()
            if bb:
                ox = max(10 - bb[0], min(S - 10 - bb[2], ox))
                oy = max(10 - bb[1], min(S - 10 - bb[3], oy))
            tmp = Image.new("RGBA", (S, S))
            tmp.alpha_composite(img, (int(round(ox)), int(round(oy))))
            tmp = desat(tmp, .02, dim * 1.2)
            if hot > 0:
                tmp = blend(tmp, hot, WHITE)
            ta = np.asarray(tmp).copy()
            ta[:, :, 3] = (ta[:, :, 3] * alpha).astype(np.uint8)
            layer.alpha_composite(Image.fromarray(ta, "RGBA"))
        return layer

    frames = []

    # ---- cell 0: intact hull on the live pixels, a white-hot starburst
    f = blend(base, .30, TINT)
    g = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(g)
    for r_, al in ((.34, 55), (.24, 100), (.14, 170)):
        disc(gd, core, int(R * r_), (*YELLOW, al))
    disc(gd, core, int(R * .09), (*PINK, 180))
    disc(gd, core, int(R * .06), (*WHITE, 255))
    for i in range(8):
        a = i * math.tau / 8 + .2
        r2 = R * (.62 if i % 2 == 0 else .42)
        gd.line((core[0] + R * .12 * math.cos(a), core[1] + R * .12 * math.sin(a),
                 core[0] + min(r2, lim) * math.cos(a), core[1] + min(r2, lim) * math.sin(a)),
                fill=(*(WHITE if i % 2 == 0 else YELLOW), 235), width=1 if i % 2 == 0 else 2)
    f.alpha_composite(g)
    frames.append(f)

    # ---- cell 1: rupture
    rup = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(rup)
    disc(d, core, int(R * .6), (*ARC_DIM, 60))
    disc(d, core, int(R * .42), (*ARC, 110))
    disc(d, core, int(R * .26), (*YELLOW, 215))
    rup.alpha_composite(chunks(.14, spin * .45, .8, 1.0))
    d = ImageDraw.Draw(rup)
    # discharge cracks: jagged arcs radiating from the core, over the hull
    for i in range(nbolt):
        a = i * math.tau / nbolt + rng.uniform(-.2, .2)
        ln = min(R * rng.uniform(.75, 1.05), lim)
        bolt(d, core, a, ln, rng, WHITE, ARC, width=2)
    if flavour == "burst":      # the cage bursts: a clean ring of short arcs between the long ones
        for i in range(nbolt):
            a = (i + .5) * math.tau / nbolt
            bolt(d, (core[0] + R * .25 * math.cos(a), core[1] + R * .25 * math.sin(a)), a, R * .45, rng, YELLOW, ARC, width=1)
    disc(d, core, int(R * .075), (*PINK, 255))
    disc(d, core, int(R * .05), (*WHITE, 255))
    for _ in range(npel):
        a = rng.random() * math.tau
        rr = rng.uniform(R * .5, min(R * 1.0, lim))
        pellet(d, cx + rr * math.cos(a), cy + rr * math.sin(a), int(rng.integers(1, 3)), (*SLATE_LT, 255))
    for _ in range(9):
        a = rng.random() * math.tau
        rr = rng.uniform(R * .4, min(R * .95, lim))
        chip(d, cx + rr * math.cos(a), cy + rr * math.sin(a), int(rng.integers(2, 5)), (*(WHITE if rng.random() < .5 else ARC), 240), rng)
    if flavour in ("vent", "snap", "drum", "coil") and npuff:
        for k in range(min(npuff, 3)):
            a = rng.random() * math.tau
            vapour(d, (cx + math.cos(a) * R * .55, cy + math.sin(a) * R * .55), R * .18, rng, SLATE_LT, 150)
    frames.append(rup)

    # ---- cell 2: dispersal, no hull
    dis = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(dis)
    for k in range(npuff + 3):
        a = rng.random() * math.tau
        rr = rng.uniform(R * .15, min(R * .85, lim))
        vapour(d, (cx + rr * math.cos(a), cy + rr * math.sin(a)), R * rng.uniform(.14, .26) * (1.5 if flavour == "vent" else 1.0), rng, SLATE if k % 2 else SLATE_LT, 120)
    for rad, al in ((R * .5, 120), (R * .78, 80), (min(R * 1.05, lim), 45)):
        rad = int(rad)
        d.arc((cx - rad, cy - rad, cx + rad, cy + rad), 20, 160, fill=(*ARC, al), width=2)
        d.arc((cx - rad, cy - rad, cx + rad, cy + rad), 200, 340, fill=(*WHITE, al), width=1)
    dis.alpha_composite(chunks(.5, spin, .62, .62))
    d = ImageDraw.Draw(dis)
    for _ in range(max(3, nbolt // 2)):
        a = rng.random() * math.tau
        rr = rng.uniform(R * .2, R * .6)
        bolt(d, (cx + rr * math.cos(a), cy + rr * math.sin(a)), a + rng.normal(0, .8), R * rng.uniform(.18, .32), rng, WHITE, ARC_DIM, width=1)
    for _ in range(npel + 8):
        a = rng.random() * math.tau
        rr = rng.uniform(R * .25, min(R * 1.1, lim))
        x, y = cx + rr * math.cos(a), cy + rr * math.sin(a)
        if rng.random() < .55:
            pellet(d, x, y, int(rng.integers(1, 3)), (*SLATE_LT, int(rng.integers(150, 235))))
        else:
            chip(d, x, y, int(rng.integers(1, 4)), (*(ARC if rng.random() < .5 else WHITE), int(rng.integers(150, 235))), rng)
    for _ in range(6):                  # sparse white glints
        a = rng.random() * math.tau
        rr = rng.uniform(R * .15, min(R * .95, lim))
        x, y = int(cx + rr * math.cos(a)), int(cy + rr * math.sin(a))
        d.line((x - 2, y, x + 2, y), fill=(*WHITE, 230)); d.line((x, y - 2, x, y + 2), fill=(*WHITE, 230))
    disc(d, core, 3, (*PINK, 150))
    disc(d, core, 2, (*WHITE, 230))
    frames.append(dis)

    if key in FIRE_UNITS:
        frames[1] = fire_under(frames[1], core, R, rng, 10, 44, 14, 1.0)
        frames[2] = fire_under(frames[2], core, R, rng, 7, 30, 11, 3.0)
    out = Image.new("RGBA", (S * 3, S))
    for i, fr in enumerate(frames):
        out.alpha_composite(fr, (i * S, 0))
    a = np.asarray(out).copy()
    for i in range(3):
        c = a[:, i * S:(i + 1) * S]
        c[:8, :, 3] = 0; c[-8:, :, 3] = 0; c[:, :8, 3] = 0; c[:, -8:, 3] = 0
    out = Image.fromarray(a, "RGBA")
    # harden alpha: no 1-254 fringe from the soft discs except the intentional steps (snap to 8 levels)
    arr2 = np.asarray(out).copy()
    al = arr2[:, :, 3].astype(np.int32)
    al = np.where(al == 0, 0, np.maximum(32, (al // 32) * 32 + 31)).astype(np.uint8)
    al[arr2[:, :, 3] >= 250] = 255
    arr2[:, :, 3] = al
    out = Image.fromarray(arr2, "RGBA")
    sys.path.insert(0, str(Path(__file__).resolve().parent / "ember_recolor"))
    from recolor import keep_off_red
    out = keep_off_red(out)
    RES.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(exist_ok=True)
    out.save(RES / f"{key}.png", optimize=True)
    out.save(OUT / f"{key}.png", optimize=True)
    return base, out


def preview(rows):
    S = 192
    W = 256 * 8
    sheet = Image.new("RGBA", (W, sum((256 if k == "ember_big" else 192) * 2 for k, _, _ in rows)), (11, 11, 26, 255))
    y = 0
    for k, base, out in rows:
        s = base.height
        sheet.alpha_composite(base.resize((s * 2, s * 2), Image.NEAREST), (0, y))
        sheet.alpha_composite(out.resize((out.width * 2, out.height * 2), Image.NEAREST), (s * 2, y))
        y += s * 2
    sheet.save(OUT / "preview.png")


if __name__ == "__main__":
    keys = sys.argv[1:] or list(UNITS)
    rows = []
    for k in keys:
        b, o = build(k)
        rows.append((k, b, o))
        print("built", k, o.size)
    if len(sys.argv) == 1:
        preview(rows)
