"""Per-skin damage designs, batch B: JadePhantom, GoldWarden, Lightning,
Ligher and Paranoid (every non-stock skin). Loaded by damage.py, which
merges SKIN_D into damage.SKIN_D; the stock skins keep damage.D.

Every skin tells its own story -- a different primary wound in a different
place from its ship's stock damage and from its sibling skins -- and is
drawn in tones that read on THAT skin's paint: pale ash, bare steel and hot
metal on the dark skins (Night, Wraith, Regent), black soot, cyan coolant
and molten orange on the light ones (Bone, Sodium, Amber, Neon, Hazard,
Flint, Signal).

On top of damage.py's kinds (panel, tear, bend, crack, scorch) this module
adds skin damage styles, carried as a "panel" whose glow slot is a style
tuple, so damage.attach / wreck / holes treat them like a blown panel
(GROW-scaled, centre checked on its form, clipped to the form, stripes
masked out of it, charred round on the last life):

    burn    a blast burn: a radiating ring in a contrast tone (ash = pale
            ash on dark paint, soot = black, rust = burnt orange-brown),
            a dark crust and a live ember core
    strip   paint blasted off: bare steel with scratch lines, rivets, a
            heat-blued rim and a curled lip of paint
    gash    a molten cut: dark slit with a white-hot seam, glowing rim and
            spatter (tone "steel": a cold, bright-metal rim instead)
    melt    a molten puddle running into drips
    frame   skin peeled off a bay: bright exposed ribs and stringer
    coolant a ruptured coolant line: cyan splash with frost crystals
    pocks   a stitched line of bullet holes with hot rims and dents
    void    a chunk blown clean through (the wreck's "hole", hand-placed)

The drawing hooks hullkit.damage_draw (the renderer's one call into
damage.draw) and only handles features carrying this module's style tag;
everything else goes to damage.draw unchanged, so stock sheets are
pixel-identical (build_skins.py --verify-stock).

Emitters stay the ship's (ShipDamageTable is per ship): every skin keeps
some damage over its ship's damaged-state sparks point (the smolder there
too), and its last-life wounds near the arc / flame / leak points.
"""
import math
import random

import hullkit as _hk
from hullkit import (AMBER, BONE, BRUISE_HI, CYAN, GUN_SH, INK, P, ROCK, ROCK_SH, SODIUM,
                     SODIUM_SH, STEEL, STEEL_HI, STEEL_SH, TEAL_SH, inkpoly, line, poly, star)

TAG = "skin-b"


# ------------------------------------------------------------ authoring --
def S(style, ref, pts, **opt):
    """A skin damage feature of `style` on form `ref` (design space)."""
    return ("panel", ref, P(pts), (TAG, style, tuple(sorted(opt.items()))))


def blob(cx, cy, rx, ry=None, n=9, seed=1, rough=.22):
    rng = random.Random(seed * 7919 + int(cx * 31 + cy * 17))
    ry = rx if ry is None else ry
    a0 = rng.uniform(0, math.tau)
    out = []
    for i in range(n):
        a = a0 + math.tau * i / n
        k = 1 + rng.uniform(-rough, rough)
        out.append((cx + math.cos(a) * rx * k, cy + math.sin(a) * ry * k))
    return out


def slash(x0, y0, x1, y1, w):
    """A thin lens from (x0, y0) to (x1, y1), w wide in the middle."""
    dx, dy = x1 - x0, y1 - y0
    ln = math.hypot(dx, dy) or 1
    nx, ny = -dy / ln * w / 2, dx / ln * w / 2
    return [(x0, y0), (x0 + dx * .3 + nx, y0 + dy * .3 + ny), (x0 + dx * .7 + nx * .8, y0 + dy * .7 + ny * .8),
            (x1, y1), (x0 + dx * .7 - nx, y0 + dy * .7 - ny), (x0 + dx * .3 - nx * .8, y0 + dy * .3 - ny * .8)]


def burn(ref, x, y, r, tone="soot", seed=1, sy=1.0):
    return S("burn", ref, blob(x, y, r, r * sy, 10, seed), tone=tone)


# --------------------------------------------------------------- geometry --
def _c(pts):
    return sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts)


def _sc(pts, k, c=None):
    cx, cy = c or _c(pts)
    return [(cx + (x - cx) * k, cy + (y - cy) * k) for x, y in pts]


def _sh(pts, dx, dy):
    return [(x + dx, y + dy) for x, y in pts]


def _bb(pts):
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return min(xs), min(ys), max(xs), max(ys)


def _jag(pts, seed, amp):
    rng = random.Random(seed)
    out = []
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        out.append(a)
        dx, dy = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dx, dy) or 1
        for t in (.35, .7):
            s = rng.uniform(-amp, amp)
            out.append((a[0] + dx * t - dy / ln * s, a[1] + dy * t + dx / ln * s))
    return out


def _clip(pts, a, b):
    """Segments of the line a-b (extended) that lie inside polygon pts."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    ts = []
    n = len(pts)
    for i in range(n):
        p, q = pts[i], pts[(i + 1) % n]
        ex, ey = q[0] - p[0], q[1] - p[1]
        den = dx * ey - dy * ex
        if abs(den) < 1e-9:
            continue
        t = ((p[0] - a[0]) * ey - (p[1] - a[1]) * ex) / den
        u = ((p[0] - a[0]) * dy - (p[1] - a[1]) * dx) / den
        if 0 <= u < 1:
            ts.append(t)
    ts.sort()
    return [[(a[0] + dx * ts[i], a[1] + dy * ts[i]), (a[0] + dx * ts[i + 1], a[1] + dy * ts[i + 1])]
            for i in range(0, len(ts) - 1, 2)]


def _axis(pts):
    """The polygon's long axis: (centre, unit along, unit across, half length, half width)."""
    cx, cy = _c(pts)
    best = None
    for k in range(18):
        a = math.pi * k / 18
        ux, uy = math.cos(a), math.sin(a)
        proj = [(x - cx) * ux + (y - cy) * uy for x, y in pts]
        L = max(proj) - min(proj)
        if best is None or L > best[0]:
            best = (L, ux, uy)
    L, ux, uy = best
    vx, vy = -uy, ux
    wid = [(x - cx) * vx + (y - cy) * vy for x, y in pts]
    return (cx, cy), (ux, uy), (vx, vy), L / 2, (max(wid) - min(wid)) / 2


# ---------------------------------------------------------------- drawing --
BURN_TONES = {
    # ring (blast streaks), crust, ember
    "ash": (STEEL_HI, ROCK, SODIUM),
    "soot": (ROCK_SH, INK, SODIUM),
    "rust": (SODIUM_SH, ROCK_SH, AMBER),
}


def _burn(pts, o, hue, W, seed):
    ring, crust, ember = BURN_TONES[o.get("tone", "soot")]
    cx, cy = _c(pts)
    x0, y0, x1, y1 = _bb(pts)
    R = max(x1 - x0, y1 - y0) / 2
    j = _jag(pts, seed * 13 + 5, R * .12)
    s = poly(W(star(cx, cy, R * 1.22, R * .78, 9, rot=seed * 29)), ring)
    s += poly(W(_sc(j, .92)), ring)
    s += poly(W(_sc(j, .66)), crust)
    s += inkpoly(W(_sc(j, .66)), .9)
    s += poly(W(star(cx + R * .05, cy - R * .05, R * .36, R * .18, 5, rot=seed * 41)), ember)
    s += poly(W(star(cx + R * .05, cy - R * .05, R * .14, R * .07, 4, rot=seed * 11)), AMBER)
    rng = random.Random(seed * 101 + 3)
    for _ in range(5):
        a = rng.uniform(0, math.tau)
        d = R * rng.uniform(.75, 1.05)
        s += poly(W(star(cx + math.cos(a) * d, cy + math.sin(a) * d, rng.uniform(.6, 1.0), .35, 4,
                         rot=rng.uniform(0, 90))), crust)
    return s


def _strip(pts, o, hue, W, seed):
    j = _jag(pts, seed * 7 + 1, .9)
    x0, y0, x1, y1 = _bb(pts)
    s = poly(W(_sc(j, 1.12)), BRUISE_HI if o.get("blued", True) else SODIUM_SH)   # heat-tinted edge
    s += poly(W(j), STEEL_SH)
    s += poly(W(_sh(_sc(j, .9), -.6, -.6)), STEEL)
    # scratches, raked one way across the bare metal
    ang = o.get("rake", -.55)
    ux, uy = math.cos(ang), math.sin(ang)
    w = max(x1 - x0, y1 - y0)
    inner = _sc(j, .86)
    cx, cy = _c(pts)
    for k in range(-3, 4):
        off = k * w / 7.5
        a = (cx - ux * w - uy * off, cy - uy * w + ux * off)
        b = (cx + ux * w - uy * off, cy + uy * w + ux * off)
        for seg in _clip(inner, a, b):
            s += line(W(seg), .8 if k % 2 else 1.2, STEEL_HI)
    # rivet row
    for seg in _clip(_sc(j, .7), (x0, cy + (y1 - y0) * .18), (x1, cy + (y1 - y0) * .18))[:1]:
        (ax, ay), (bx, by) = seg
        for t in (.2, .5, .8):
            s += poly(W(star(ax + (bx - ax) * t, ay + (by - ay) * t, .9, .5, 4, rot=45)), INK)
    s += inkpoly(W(j), 1.3)
    # a curled lip of paint on the upper left
    s += line(W(_sc(j, 1.1)[:4]), 1.3, hue[2])
    return s


def _gash(pts, o, hue, W, seed):
    (cx, cy), (ux, uy), (vx, vy), hl, hw = _axis(pts)
    j = _jag(pts, seed * 3 + 2, .5)
    cold = o.get("tone") == "steel"
    halo, rim = (STEEL_HI, BONE) if cold else (SODIUM, AMBER)
    s = poly(W(_sc(j, 1.5)), SODIUM_SH if not cold else STEEL)
    s += poly(W(_sc(j, 1.3)), halo)
    s += poly(W(_sc(j, 1.12)), rim)
    s += poly(W(j), INK)
    seam = [(cx - ux * hl * .7, cy - uy * hl * .7), (cx, cy), (cx + ux * hl * .7, cy + uy * hl * .7)]
    s += line(W(seam), .9, BONE if not cold else CYAN)
    s += inkpoly(W(_sc(j, 1.3)), .9)
    rng = random.Random(seed * 37 + 9)
    for _ in range(6):
        t = rng.uniform(-1, 1)
        side = rng.choice((-1, 1))
        d = hw * rng.uniform(1.8, 3.2)
        px, py = cx + ux * hl * t + vx * d * side, cy + uy * hl * t + vy * d * side
        s += poly(W(star(px, py, rng.uniform(.6, 1.1), .35, 4, rot=rng.uniform(0, 90))), rim)
    return s


def _melt(pts, o, hue, W, seed):
    j = _jag(pts, seed * 5 + 4, 1.0)
    cx, cy = _c(pts)
    x0, y0, x1, y1 = _bb(pts)
    R = max(x1 - x0, y1 - y0) / 2
    s = poly(W(_sc(j, 1.22)), SODIUM_SH)
    s += poly(W(j), SODIUM)
    s += poly(W(_sh(_sc(j, .62), -R * .12, -R * .12)), AMBER)
    s += poly(W(_sh(_sc(j, .22), -R * .2, -R * .22)), BONE)
    # drips running aft (down the sprite)
    rng = random.Random(seed * 19 + 1)
    for k in range(3):
        dx = x0 + (x1 - x0) * (.25 + .25 * k) + rng.uniform(-1, 1)
        L = R * rng.uniform(.5, .9)
        top = y1 - R * .25
        d = [(dx - 1.3, top), (dx + 1.3, top), (dx + 1.0, top + L), (dx, top + L + 1.6), (dx - 1.0, top + L)]
        s += poly(W(d), SODIUM) + inkpoly(W(d), .8)
    s += inkpoly(W(j), 1.2)
    s += inkpoly(W(_sc(j, 1.22)), .8, ROCK_SH)
    return s


def _frame(pts, o, hue, W, seed):
    j = _jag(pts, seed * 11 + 6, 1.2)
    x0, y0, x1, y1 = _bb(pts)
    w, h = x1 - x0, y1 - y0
    cx, cy = _c(pts)
    s = poly(W(_sc(j, 1.18)), hue[1])
    s += poly(W(j), GUN_SH)
    inner = _sc(j, .96)
    rng = random.Random(seed * 3 + 1)
    tilt = rng.uniform(-.3, .3)
    for t in (.22, .5, .78):
        a = (x0 + w * t + h * tilt, y0 - 2)
        b = (x0 + w * t - h * tilt, y1 + 2)
        for seg in _clip(inner, a, b):
            s += line(W(seg), 2.8, INK) + line(W(seg), 1.4, STEEL_HI)
    for seg in _clip(inner, (x0 - 2, cy + h * .1), (x1 + 2, cy - h * .05)):
        s += line(W(seg), 2.4, INK) + line(W(seg), 1.1, STEEL_HI)
    # a loose cable with a live end
    wa = [(cx - w * .3, cy - h * .25), (cx - w * .05, cy + h * .05), (cx + w * .05, cy + h * .3)]
    s += line(W(wa), 2.2, INK) + line(W(wa), 1.0, AMBER)
    (tx, ty), = W([wa[-1]])
    s += poly(star(tx, ty, 1.8, .7, 4, rot=seed * 13), BONE)
    s += inkpoly(W(j), 1.8)
    s += inkpoly(W(_sc(j, .9)), .9, SODIUM)
    # peeled petals of paint
    for k in range(0, len(j), 4):
        p, n = j[k], j[(k + 1) % len(j)]
        q = (cx + (p[0] - cx) * 1.3, cy + (p[1] - cy) * 1.3)
        r = (p[0] + (n[0] - p[0]) * .8, p[1] + (n[1] - p[1]) * .8)
        s += poly(W([p, q, r]), hue[2]) + inkpoly(W([p, q, r]), .8)
    return s


def _coolant(pts, o, hue, W, seed):
    cx, cy = _c(pts)
    x0, y0, x1, y1 = _bb(pts)
    R = max(x1 - x0, y1 - y0) / 2
    j = _jag(pts, seed * 17 + 2, R * .1)
    sp = star(cx, cy, R * 1.25, R * .62, 8, rot=seed * 23)
    s = poly(W(sp), CYAN) + inkpoly(W(sp), 1.0, TEAL_SH)
    s += poly(W(_sc(j, .7)), BONE)
    s += inkpoly(W(_sc(j, .7)), .8, TEAL_SH)
    # the burst line: a gunmetal pipe stub, split
    a, b = (cx - R * .5, cy + R * .1), (cx + R * .5, cy - R * .1)
    s += line(W([a, b]), 3.0, INK) + line(W([a, b]), 1.6, STEEL)
    s += poly(W(star(cx, cy, R * .28, R * .12, 4, rot=30)), INK)
    rng = random.Random(seed * 23 + 7)
    for _ in range(7):
        ang = rng.uniform(0, math.tau)
        d = R * rng.uniform(1.0, 1.55)
        px, py = cx + math.cos(ang) * d, cy + math.sin(ang) * d
        r = rng.uniform(.7, 1.2)
        s += poly(W(star(px, py, r * 1.4, r * .6, 4, rot=45)), BONE) + inkpoly(W(star(px, py, r * 1.4, r * .6, 4, rot=45)), .5, TEAL_SH)
    return s


def _pocks(pts, o, hue, W, seed):
    (cx, cy), (ux, uy), (vx, vy), hl, hw = _axis(pts)
    n = o.get("n", 5)
    rim = {"hot": AMBER, "bright": BONE, "steel": STEEL_HI}[o.get("rim", "hot")]
    rng = random.Random(seed * 29 + 5)
    s = ""
    holes = []
    for k in range(n):
        t = -1 + 2 * (k + .5) / n
        wob = rng.uniform(-.6, .6) * hw
        r = rng.uniform(2.0, 2.7)
        holes.append((cx + ux * hl * t * .9 + vx * wob, cy + uy * hl * t * .9 + vy * wob, r))
    for x, y, r in holes:
        s += poly(W(star(x, y, r * 2.1, r * 1.5, 7, rot=rng.uniform(0, 60))), hue[1])   # dent
    for x, y, r in holes:
        s += poly(W(star(x, y, r * 1.45, r * 1.0, 6, rot=rng.uniform(0, 60))), rim)
        s += poly(W(star(x, y, r, r * .7, 5, rot=rng.uniform(0, 60))), INK)
        a = rng.uniform(0, math.tau)
        s += line(W([(x + math.cos(a) * r, y + math.sin(a) * r),
                     (x + math.cos(a) * r * 2.4, y + math.sin(a) * r * 2.4 + 1)]), .9, INK)
    return s


def _void(pts, o, hue, W, seed):
    import damage
    return damage.draw_wreck(("hole", None, pts, seed % 9), hue, W, seed)


STYLES = {"burn": _burn, "strip": _strip, "gash": _gash, "melt": _melt, "frame": _frame,
          "coolant": _coolant, "pocks": _pocks, "void": _void}


def _is_ours(ft):
    return ft[0] == "panel" and len(ft) > 3 and isinstance(ft[3], tuple) and ft[3][:1] == (TAG,)


def _hook():
    base = _hk.damage_draw
    if getattr(base, "_skin_b", False):
        return

    def damage_draw(ft, hue, W, seed):
        if _is_ours(ft):
            _, style, opt = ft[3]
            return STYLES[style](ft[2], dict(opt), hue, W, seed)
        return base(ft, hue, W, seed)

    damage_draw._skin_b = True
    _hk.damage_draw = damage_draw


_hook()


# ---------------------------------------------------------------- designs --
SKIN_D = {}
CONCEPTS = {}


def design(key, skin, concept, row1, row2):
    SKIN_D[(key, skin)] = {1: (row1, None), 2: (row2, None), "seed": f"{key}/{skin}"}
    CONCEPTS[(key, skin)] = concept


# JadePhantom: bat wing either side of a narrow spine (canopy on the spine's
# nose, twin nozzles at its tail). Ship emitters: damaged sparks (23, 58)
# left wing, smoke at the tail; critical arc (110, 52), flame (94, 82),
# leak (66, 90).
design("JadePhantom", "Night",
       ("right wing skin stripped to bare steel; molten gash across the left wing",
        "right leading edge peeled to its frame, molten puddle aft, left wing holed through, ash on the spine"),
       [S("strip", "wing.R", blob(92, 60, 10, 6, seed=1)),
        S("gash", "wing.L", slash(15, 62, 31, 53, 4)),
        ("crack", "wing.R", [(100, 70), (106, 75), (103, 81)])],
       [S("frame", "wing.R", blob(110, 53, 6, 4.5, seed=2)),
        S("melt", "wing.R", blob(92, 79, 5, 3.5, seed=3)),
        S("void", "wing.L", blob(44, 64, 6, 6, seed=4)),
        burn("hull", 64, 86, 3.2, "ash", seed=5),
        burn("hull", 65, 77, 2.6, "ash", seed=6)])

design("JadePhantom", "Sodium",
       ("coolant line burst at the right wing root; soot-black burn on the left wing",
        "right trailing edge torn, left wing raked with bullet holes, soot round the aft fires"),
       [S("coolant", "wing.R", blob(83, 62, 7, 6, seed=1)),
        burn("wing.L", 23, 58, 4.2, "soot", seed=2),
        ("crack", "wing.L", [(36, 70), (42, 76), (40, 82)])],
       [("tear", "wing.R", [(98, 62), (108, 60), (112, 68), (104, 72)]),
        S("pocks", "wing.L", slash(10, 47, 40, 57, 6), n=5, rim="hot"),
        burn("wing.R", 93, 81, 3.6, "soot", seed=3),
        ("crack", "hull", [(65, 76), (62, 83), (65, 90)]),
        burn("hull", 65, 89, 3.0, "soot", seed=4)])

design("JadePhantom", "Bone",
       ("stitched by cannon fire across the left wing; rust burn at the wing root",
        "second burst stitches the right wing, its leading edge bared to the frame, spine slit open"),
       [S("pocks", "wing.L", slash(30, 58, 52, 78, 6), n=6, rim="hot"),
        burn("wing.L", 22, 56, 4.0, "rust", seed=1),
        ("crack", "wing.R", [(96, 52), (102, 58), (100, 66)])],
       [S("pocks", "wing.R", slash(78, 54, 104, 66, 5), n=5, rim="hot"),
        burn("wing.R", 93, 81, 4.2, "soot", seed=2),
        S("gash", "hull", slash(64, 66, 64, 90, 3)),
        S("frame", "wing.R", blob(110, 53, 6, 4.5, seed=3))])

design("JadePhantom", "Wraith",
       ("three molten claw gashes across the right wing; ash burn on the left",
        "left wingtip torn away, molten puddle aft right, left wing holed, bare steel on the trailing edge"),
       [S("gash", "wing.R", slash(92, 44, 113, 57, 4)),
        S("gash", "wing.R", slash(89, 51, 110, 64, 4)),
        S("gash", "wing.R", slash(86, 58, 104, 70, 4)),
        burn("wing.L", 23, 58, 4.0, "ash", seed=1)],
       [("tear", "wing.L", [(6, 30), (12, 35), (14, 43), (6, 45)]),
        S("melt", "wing.R", blob(92, 79, 5, 3.5, seed=2)),
        S("void", "wing.L", blob(46, 56, 6, 6, seed=3)),
        S("strip", "wing.L", blob(26, 72, 6, 5, seed=4)),
        burn("hull", 64, 86, 3.0, "ash", seed=5)])


# GoldWarden: swept wings with a gun barrel on each (drawn over the wing at
# x 21-29 / 99-107), twin engines under the wing roots, a long spine. Ship
# emitters: damaged sparks (88, 60) right wing, smoke at the left engine;
# critical arc (63, 90) spine, flame (83, 97) right engine, leak (20, 78).
design("GoldWarden", "Night",
       ("left wing skin blasted down to bare steel; molten gash over the right wing",
        "right wing molten, belly holed through, left wingtip peeled to the frame, right gun bent, ash on the engine"),
       [S("strip", "wing.L", blob(40, 59, 9, 7, seed=1)),
        S("gash", "wing.R", slash(81, 65, 95, 55, 4)),
        ("crack", "hull", [(60, 60), (64, 66), (61, 72)])],
       [S("melt", "wing.R", blob(93, 71, 5, 4, seed=2)),
        S("void", "hull", blob(64, 86, 5, 6, seed=3)),
        S("frame", "wing.L", blob(19, 77, 5, 4, seed=4)),
        ("bend", "gun.R", [(99, 34), (107, 34), (107, 46), (99, 48)], [(99, 44), (107, 41)]),
        burn("engine.R", 83, 92, 3.0, "ash", seed=5)])

design("GoldWarden", "Neon",
       ("molten slag pooled on the spine behind the canopy; soot burn on the right wing",
        "left wingtip torn, right wing slashed, a burst of rounds down the belly, soot on the engine"),
       [S("melt", "hull", blob(64, 65, 5, 6, seed=1)),
        burn("wing.R", 88, 60, 4.2, "soot", seed=2),
        ("crack", "wing.L", [(36, 50), (42, 56), (40, 62)])],
       [("tear", "wing.L", [(14, 70), (24, 68), (26, 78), (16, 82)]),
        S("gash", "wing.R", slash(78, 50, 96, 45, 4)),
        S("pocks", "hull", slash(64, 78, 64, 97, 4), n=4, rim="hot"),
        burn("engine.R", 83, 92, 3.0, "soot", seed=3),
        burn("wing.R", 92, 75, 3.0, "soot", seed=4)])

design("GoldWarden", "Bone",
       ("cannon stitch straight down the spine; soot burn on the right wing",
        "left leading edge torn, left wing bay peeled open, right gun bent, soot on the engine"),
       [S("pocks", "hull", slash(64, 58, 64, 96, 4), n=5, rim="hot"),
        burn("wing.R", 88, 60, 4.5, "soot", seed=1),
        ("crack", "wing.L", [(44, 64), (38, 70), (40, 76)])],
       [("tear", "wing.L", [(32, 46), (44, 42), (48, 50), (36, 54)]),
        S("frame", "wing.L", blob(40, 64, 6, 5, seed=2)),
        ("bend", "gun.R", [(99, 40), (107, 38), (107, 50), (99, 50)], [(99, 46), (107, 44)]),
        burn("engine.R", 83, 92, 3.2, "soot", seed=3),
        burn("wing.L", 19, 78, 3.4, "soot", seed=4),
        burn("wing.R", 92, 75, 3.0, "soot", seed=5),
        ("crack", "wing.R", [(80, 42), (85, 47), (83, 53)])])

design("GoldWarden", "Regent",
       ("right wing bay peeled open to its ribs under a pale ash burn",
        "left wing coolant line burst, left wingtip holed, right gun bent, ash down the belly and engine"),
       [S("frame", "wing.R", blob(91, 71, 7, 5, seed=1)),
        burn("wing.R", 88, 57, 4.2, "ash", seed=2),
        ("crack", "hull", [(68, 60), (64, 66), (67, 72)])],
       [S("coolant", "wing.L", blob(40, 62, 7, 6, seed=3)),
        S("void", "wing.L", blob(18, 77, 4.5, 4, seed=4)),
        ("bend", "gun.R", [(99, 34), (107, 34), (107, 46), (99, 48)], [(99, 44), (107, 41)]),
        burn("hull", 64, 89, 3.6, "ash", seed=5),
        burn("engine.R", 83, 92, 3.0, "ash", seed=6),
        burn("wing.R", 83, 46, 2.8, "ash", seed=7)])


# Lightning: delta wings behind two long pods (the pods are drawn over the
# wing at x 36-48 / 80-92) and a centre spine; red bolts on the wings. Ship
# emitters: damaged sparks (42, 57) left pod, smoke at its nozzle; critical
# arc (112, 82) right wingtip, flame (86, 98) right pod, leak (62, 80).
design("Lightning", "Night",
       ("right wing bay peeled open to its ribs; molten gash down the left pod",
        "left wing holed, right pod molten, left pod nose bent, bare steel on the spine, ash on the wingtip"),
       [S("frame", "wing.R", blob(105, 62, 7, 6, seed=1)),
        S("gash", "pod.L", slash(42, 47, 42, 67, 3.5)),
        ("crack", "hull", [(66, 54), (62, 60), (65, 66)])],
       [S("void", "wing.L", blob(25, 79, 6, 5, seed=2)),
        S("melt", "pod.R", blob(86, 84, 3.5, 5, seed=3)),
        ("bend", "pod.L", [(36, 22), (48, 22), (48, 34), (36, 36)], [(36, 32), (48, 26)]),
        S("strip", "hull", blob(64, 83, 4, 4, seed=4)),
        burn("wing.R", 111, 81, 3.0, "ash", seed=5),
        burn("wing.R", 97, 50, 3.0, "ash", seed=6)])

design("Lightning", "Sodium",
       ("coolant burst over the left wing's bolt; soot burn on the left pod",
        "right wingtip and wing root opened up, bullet holes down the spine, right pod nose torn, soot aft"),
       [S("coolant", "wing.L", blob(24, 67, 7, 7, seed=1)),
        burn("pod.L", 42, 57, 3.4, "soot", seed=2),
        ("crack", "wing.R", [(100, 50), (106, 56), (104, 62)])],
       [S("frame", "wing.R", blob(110, 78, 5, 5, seed=3)),
        S("pocks", "hull", slash(64, 52, 64, 70, 3), n=3, rim="hot"),
        S("void", "wing.R", blob(98, 52, 5, 5, seed=4)),
        ("tear", "pod.R", [(82, 13), (87, 9), (91, 18), (84, 22)]),
        burn("pod.R", 86, 87, 3.0, "soot", seed=5),
        burn("hull", 63, 80, 3.0, "soot", seed=6),
        burn("wing.L", 31, 46, 3.0, "soot", seed=7),
        burn("wing.L", 29, 83, 3.0, "soot", seed=8)])

design("Lightning", "Bone",
       ("raked by a burst across the right wing; soot burn on the left pod",
        "left wing molten and holed near the root, spine gashed, soot on the right wingtip"),
       [S("pocks", "wing.R", slash(93, 49, 111, 85, 6), n=6, rim="hot"),
        burn("pod.L", 42, 57, 3.6, "soot", seed=1),
        ("crack", "wing.L", [(30, 40), (34, 46), (32, 52)])],
       [S("melt", "wing.L", blob(26, 80, 5, 4, seed=2)),
        S("void", "wing.L", blob(28, 50, 4.5, 4.5, seed=3)),
        S("gash", "hull", slash(63, 56, 65, 84, 3)),
        burn("wing.R", 111, 81, 3.0, "soot", seed=4),
        burn("hull", 64, 12, 2.5, "soot", seed=5)])

design("Lightning", "Hazard",
       ("right pod's paint scraped to bare steel by a glancing hit; soot burn on the left pod",
        "right wing bay peeled open, coolant burst on the spine, left wing holed, soot aft"),
       [S("strip", "pod.R", blob(86, 56, 4.5, 13, seed=7), rake=-1.2),
        burn("pod.L", 42, 57, 3.5, "soot", seed=1),
        ("crack", "wing.R", [(96, 74), (102, 80), (100, 86)])],
       [S("frame", "wing.R", blob(102, 58, 6, 6, seed=2)),
        S("coolant", "hull", blob(64, 61, 4, 5, seed=3)),
        S("void", "wing.L", blob(24, 80, 5, 5, seed=4)),
        burn("pod.R", 86, 87, 3.0, "soot", seed=5),
        burn("wing.R", 111, 81, 3.0, "rust", seed=6),
        burn("hull", 63, 80, 3.0, "soot", seed=7)])


# Ligher: one big teardrop hull with a teal nose cap, two small fins behind
# it, a single nozzle. Ship emitters: damaged sparks (52, 47) upper left,
# smoke at the nozzle; critical arc (74, 79), flame (70, 102), leak (92, 92)
# on the right fin.
design("Ligher", "Night",
       ("upper right flank peeled open to its ribs; molten gash at the upper left",
        "left flank holed through, right flank molten, right fin torn, left fin bent, ash aft"),
       [S("frame", "hull", blob(78, 54, 5, 7, seed=1)),
        S("gash", "hull", slash(46, 53, 56, 41, 3.5)),
        ("crack", "hull", [(76, 86), (72, 92), (75, 98)])],
       [S("void", "hull", blob(53, 74, 6, 6, seed=2)),
        S("melt", "hull", blob(74, 78, 5, 4, seed=3)),
        ("tear", "fin.R", [(88, 86), (96, 90), (96, 98), (90, 96)]),
        ("bend", "fin.L", [(36, 82), (44, 76), (44, 90), (34, 96)], [(38, 84), (42, 92)]),
        burn("hull", 66, 96, 3.0, "ash", seed=4),
        burn("hull", 77, 36, 2.8, "ash", seed=5)])

design("Ligher", "Amber",
       ("coolant line burst at the lower right; soot burn at the upper left",
        "left flank skinned to bare steel, upper right holed, right fin bent, soot over the nozzle and left fin"),
       [S("coolant", "hull", blob(74, 89, 6, 6, seed=1)),
        burn("hull", 52, 47, 4.0, "soot", seed=2),
        ("crack", "hull", [(78, 40), (82, 48), (79, 54)])],
       [S("strip", "hull", blob(48, 72, 5, 8, seed=3)),
        S("void", "hull", blob(80, 57, 5, 5, seed=4)),
        ("bend", "fin.R", [(84, 70), (98, 92), (96, 100), (88, 90)], [(86, 78), (96, 96)]),
        burn("hull", 62, 98, 2.8, "soot", seed=5),
        burn("fin.L", 38, 90, 3.0, "soot", seed=6),
        burn("fin.R", 91, 89, 2.8, "soot", seed=7)])

design("Ligher", "Bone",
       ("cannon stitch down the left flank; rust burn at the upper left",
        "right flank molten, slashed high on the right, tail bay peeled open, left fin torn"),
       [S("pocks", "hull", slash(46, 60, 57, 90, 6), n=5, rim="hot"),
        burn("hull", 53, 46, 4.0, "rust", seed=1),
        ("crack", "hull", [(80, 60), (84, 66), (81, 72)])],
       [S("melt", "hull", blob(75, 78, 5, 4.5, seed=2)),
        S("gash", "hull", slash(84, 58, 77, 40, 3)),
        S("frame", "hull", blob(64, 95, 5, 3.5, seed=3)),
        ("tear", "fin.L", [(32, 90), (40, 86), (42, 94), (34, 98)])])

design("Ligher", "Flint",
       ("pale ash blast across the lower left; bare steel scraped at the upper left",
        "coolant burst on the right flank, upper right holed, right fin bent, ash at the nozzle"),
       [burn("hull", 52, 86, 6.0, "ash", seed=1, sy=1.1),
        S("strip", "hull", blob(52, 47, 4, 5, seed=2)),
        ("crack", "hull", [(70, 64), (76, 70), (73, 76)])],
       [S("coolant", "hull", blob(76, 78, 5, 5, seed=3)),
        S("void", "hull", blob(80, 52, 4.5, 5, seed=4)),
        ("bend", "fin.R", [(84, 70), (98, 92), (96, 100), (88, 90)], [(86, 78), (96, 96)]),
        burn("hull", 70, 97, 2.8, "ash", seed=5)])


# Paranoid: a disc hull (canopy in the middle) between two engine pods on
# struts (the struts barely show). Ship emitters: damaged sparks (45, 36)
# upper left of the disc, smoke at the left pod; critical arc (91, 58),
# flame (112, 82) right pod, leak (64, 86).
design("Paranoid", "Night",
       ("lower right of the disc peeled open to its ribs; molten gash at the upper left",
        "left pod holed through, right rim molten, right pod shot up, ash at the bottom of the disc"),
       [S("frame", "hull", blob(84, 76, 8, 6, seed=1)),
        S("gash", "hull", slash(37, 44, 53, 29, 4.5)),
        ("crack", "pod.R", [(112, 40), (108, 48), (111, 54)])],
       [S("void", "pod.L", blob(16, 62, 5, 6, seed=2)),
        S("melt", "hull", blob(92, 57, 4.5, 5, seed=3)),
        burn("hull", 64, 84, 3.0, "ash", seed=4),
        S("pocks", "pod.R", slash(112, 50, 112, 75, 4), n=4, rim="bright")])

design("Paranoid", "Sodium",
       ("coolant burst at the upper right of the disc; soot burn at the upper left",
        "lower left skinned to bare steel, right pod holed, left pod peeled open, soot on the rim and pod"),
       [S("coolant", "hull", blob(83, 37, 8, 6, seed=1)),
        burn("hull", 45, 36, 4.0, "soot", seed=2),
        ("crack", "hull", [(40, 70), (44, 76), (42, 82)])],
       [S("strip", "hull", blob(42, 67, 5, 6, seed=3)),
        S("void", "pod.R", blob(112, 48, 5, 6, seed=4)),
        S("frame", "pod.L", blob(16, 70, 5, 5, seed=5)),
        burn("hull", 91, 58, 3.5, "soot", seed=6),
        burn("pod.R", 113, 71, 3.0, "soot", seed=7),
        burn("hull", 64, 84, 3.0, "soot", seed=8)])

design("Paranoid", "Bone",
       ("a burst of rounds down the left pod; rust burn at the upper left of the disc",
        "lower right molten, right pod's red cap torn off, right rim slashed, bottom bay open, soot on the pod"),
       [S("pocks", "pod.L", slash(16, 34, 16, 76, 6), n=6, rim="hot"),
        burn("hull", 45, 36, 4.5, "rust", seed=1),
        ("crack", "hull", [(86, 72), (90, 78), (86, 84)])],
       [S("melt", "hull", blob(84, 74, 5, 4.5, seed=2)),
        ("tear", "pod.R", [(106, 28), (116, 26), (118, 36), (108, 38)]),
        S("gash", "hull", slash(94, 50, 90, 65, 3)),
        S("frame", "hull", blob(64, 82, 5, 4, seed=3)),
        burn("pod.R", 111, 58, 3.5, "soot", seed=4),
        burn("pod.R", 114, 72, 3.0, "soot", seed=5)])

design("Paranoid", "Signal",
       ("soot blast scorching the right pod; paint scraped to bare steel at the upper left of the disc",
        "lower left peeled open, upper right molten, right rim holed, soot on both pods"),
       [burn("pod.R", 112, 60, 5.5, "soot", seed=1, sy=1.6),
        S("strip", "hull", blob(45, 37, 5, 5, seed=7)),
        ("crack", "hull", [(64, 74), (60, 80), (64, 84)])],
       [S("frame", "hull", blob(42, 72, 5, 5, seed=2)),
        S("melt", "hull", blob(86, 36, 5, 4, seed=3)),
        S("void", "hull", blob(91, 60, 4, 5, seed=4)),
        burn("pod.L", 16, 70, 3.5, "soot", seed=5),
        burn("pod.R", 114, 33, 3.0, "soot", seed=6),
        burn("hull", 64, 84, 3.0, "soot", seed=7)])
