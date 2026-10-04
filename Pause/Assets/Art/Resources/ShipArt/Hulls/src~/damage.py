"""Per-ship damage progressions for the hull flipbooks (rows 1 and 2 of every
sheet), and the damage-FX emitter points the game reads (ShipDamageTable.cs).

Every ship has 3 lives (collisionDetection.MAXLIFE), so the sheet's rows map
to the hits taken:

    row 0  intact     no hits, 3 lives left
    row 1  damaged    1 hit,   2 lives left
    row 2  critical   2 hits,  the last life

Each ship's damage is designed for that hull, not a shared overlay: a blown
panel exposing glowing wiring, a torn wingtip, a bent fin or barrel, scorch
and cracks on its real parts, and on the last life the canopy cracks. Row 2
keeps everything row 1 has and adds more (cumulative), so it always reads
worse.

All of it is drawn INSIDE the forms (clipped to the form it sits on, before
that form's outline is re-inked), so every damaged frame has exactly the
intact frame's alpha: the baked shield contour, the hitbox and every skin
stay valid. build.py / build_skins.py assert that.

Coordinates are the design space of ships.py (before build.fit); attach()
maps them through the ship's fit.

Feature kinds, each on one form (ref "hull", "wing.L", "pod.R", ...):
    panel(poly, glow)       a blown-out panel: dark interior, spars, two live
                            wires and an exposed glow (amber or cyan)
    tear(poly)              torn-away skin: peeled rim in the hull shadow,
                            dark structure with spars, jagged ink edge
    bend(poly, crease)      a bent fin / barrel: the bent part drops into the
                            hull shadow, ink crease with a highlight kick
    crack(points)           an ink crack with a branch
    scorch(x, y, r)         a soot star with a burnt core and two hairlines
Emitters (the in-game FX, ShipDamageFx), one point each:
    sparks  broken panel crackle     arc    electrical arc on bare wiring
    smoke   damaged engine smoke     flame  small flame lick
    leak    fuel / coolant droplets in the hull's colour
"""
import math

from hullkit import *  # noqa: F401,F403

# features read at ~110 px on a phone: everything is drawn this much bigger
# than the table's polygons, about its own centre
GROW = 1.3

EMITTER_KINDS = ["sparks", "arc", "smoke", "flame", "leak"]


def mx(pts):
    return [(128 - x, y) for x, y in pts]


# ------------------------------------------------------------- the table --
# key: {1: (features, emitters), 2: (features, emitters)}; row 2 adds to row 1.
# An emitter is (kind, x, y); the first emitter of a state is where the
# debris burst sprays from when that state is reached.
D = {}

D["NeonComet"] = {
    1: ([("panel", "hull", [(25, 78), (34, 73), (39, 83), (31, 89)], "amber"),
         ("scorch", "hull", 98, 86, 6),
         ("crack", "hull", [(41, 66), (46, 72), (44, 78), (49, 84)])],
        [("sparks", 32, 81), ("smoke", 52, 108)]),
    2: ([("tear", "hull", [(102, 88), (112, 86), (117, 93), (111, 98), (104, 97)]),
         ("crack", "hull", [(64, 60), (61, 68), (66, 76), (63, 86)]),
         ("scorch", "pod.R", 74, 104, 4.5)],
        [("arc", 108, 92), ("flame", 76, 106), ("leak", 70, 92)]),
}

D["VoltViper"] = {
    1: ([("panel", "hull", [(70, 50), (81, 51), (82, 61), (72, 63)], "cyan"),
         ("scorch", "wing.L", 28, 64, 5),
         ("crack", "hull", [(46, 30), (50, 38), (47, 44)])],
        [("sparks", 76, 56), ("smoke", 52, 96)]),
    2: ([("tear", "wing.L", [(6, 62), (12, 56), (19, 62), (16, 70), (8, 70)]),
         ("bend", "wing.R", [(110, 52), (124, 62), (122, 72), (112, 70)], [(110, 54), (114, 70)]),
         ("scorch", "leg.R", 76, 92, 4),
         ("crack", "hull", [(58, 66), (64, 72), (60, 82)])],
        [("arc", 13, 64), ("flame", 76, 96), ("leak", 70, 84)]),
}

D["SolarFang"] = {
    1: ([("panel", "hull", [(40, 80), (49, 76), (53, 88), (44, 94)], "amber"),
         ("crack", "hull", [(54, 66), (60, 72), (58, 80), (63, 84)]),
         ("scorch", "hull", 86, 82, 5.5)],
        [("sparks", 46, 85), ("smoke", 64, 100)]),
    2: ([("bend", "fang.L", [(14, 56), (20, 54), (25, 70), (17, 72)], [(16, 70), (24, 68)]),
         ("tear", "hull", [(98, 98), (108, 96), (114, 106), (104, 109), (96, 106)]),
         ("panel", "hull", [(66, 62), (76, 64), (74, 78), (66, 76)], "amber"),
         ("scorch", "hull", 38, 100, 4.5)],
        [("arc", 70, 70), ("flame", 104, 102), ("leak", 84, 96)]),
}

D["CrimsonHalo"] = {
    1: ([("panel", "wing.L", [(27, 79), (38, 72), (42, 81), (33, 87)], "amber"),
         ("crack", "halo.L", [(40, 36), (46, 40), (41, 44)]),
         ("scorch", "hull", 66, 92, 4)],
        [("sparks", 34, 80), ("smoke", 61, 106)]),
    2: ([("tear", "wing.R", [(100, 86), (110, 84), (116, 90), (112, 95), (102, 94)]),
         ("crack", "halo.R", [(83, 30), (87, 36), (84, 40), (88, 46)]),
         ("bend", "tail.R", [(76, 100), (88, 106), (90, 113), (78, 108)], [(78, 102), (86, 110)]),
         ("scorch", "wing.L", 20, 90, 3.5)],
        [("arc", 86, 40), ("flame", 106, 90), ("leak", 66, 100)]),
}

D["IonLancer"] = {
    1: ([("panel", "wing.L", [(24, 90), (34, 82), (40, 90), (30, 99)], "cyan"),
         ("scorch", "hull", 62, 76, 4),
         ("crack", "hull", [(63, 22), (65, 30), (63, 34)])],
        [("sparks", 32, 90), ("smoke", 54, 104)]),
    2: ([("tear", "wing.R", [(106, 100), (114, 98), (118, 106), (110, 108)]),
         ("bend", "canard.R", [(74, 38), (82, 40), (82, 45), (74, 43)], [(76, 38), (77, 44)]),
         ("panel", "wing.R", [(86, 82), (94, 78), (98, 86), (90, 90)], "amber"),
         ("scorch", "pod.R", 74, 100, 3.5)],
        [("arc", 92, 84), ("flame", 74, 104), ("leak", 64, 90)]),
}

D["JadePhantom"] = {
    1: ([("panel", "wing.L", [(16, 52), (28, 54), (30, 64), (18, 64)], "cyan"),
         ("scorch", "wing.R", 100, 66, 5),
         ("crack", "hull", [(62, 56), (66, 64), (62, 72)])],
        [("sparks", 23, 58), ("smoke", 61, 98)]),
    2: ([("tear", "wing.R", [(88, 80), (97, 76), (102, 84), (94, 90), (88, 88)]),
         ("bend", "wing.L", [(6, 28), (16, 42), (10, 46), (5, 40)], [(7, 38), (14, 42)]),
         ("panel", "wing.R", [(104, 48), (114, 46), (116, 56), (106, 58)], "amber"),
         ("scorch", "wing.L", 38, 80, 4)],
        [("arc", 110, 52), ("flame", 94, 82), ("leak", 66, 90)]),
}

D["GoldWarden"] = {
    1: ([("panel", "wing.R", [(80, 54), (92, 52), (96, 64), (84, 68)], "amber"),
         ("scorch", "hull", 61, 60, 4.5),
         ("crack", "wing.L", [(20, 64), (28, 68), (26, 74), (32, 78)])],
        [("sparks", 88, 60), ("smoke", 45, 96)]),
    2: ([("bend", "gun.L", [(21, 38), (29, 38), (29, 48), (21, 50)], [(21, 46), (29, 44)]),
         ("tear", "wing.L", [(14, 74), (22, 72), (26, 80), (18, 84)]),
         ("panel", "hull", [(58, 82), (68, 84), (68, 96), (58, 96)], "cyan"),
         ("scorch", "engine.R", 83, 94, 3.5)],
        [("arc", 63, 90), ("flame", 83, 97), ("leak", 20, 78)]),
}

D["Lightning"] = {
    1: ([("panel", "pod.L", [(37, 50), (47, 52), (47, 64), (37, 62)], "amber"),
         ("scorch", "wing.R", 98, 66, 5),
         ("crack", "hull", [(62, 56), (66, 64), (62, 72)])],
        [("sparks", 42, 57), ("smoke", 42, 98)]),
    2: ([("tear", "wing.R", [(108, 80), (116, 76), (120, 86), (112, 90)]),
         ("bend", "pod.R", [(80, 22), (92, 22), (92, 34), (80, 36)], [(80, 32), (92, 26)]),
         ("panel", "hull", [(59, 30), (65, 32), (66, 44), (60, 46)], "cyan"),
         ("scorch", "pod.R", 86, 88, 3.5)],
        [("arc", 112, 82), ("flame", 86, 98), ("leak", 62, 80)]),
}

D["Ligher"] = {
    1: ([("panel", "hull", [(47, 42), (56, 38), (58, 52), (49, 56)], "amber"),
         ("scorch", "hull", 76, 86, 5),
         ("crack", "cap", [(62, 6), (65, 11), (62, 15)])],
        [("sparks", 52, 47), ("smoke", 60, 104)]),
    2: ([("bend", "fin.R", [(84, 70), (98, 92), (96, 100), (88, 90)], [(86, 78), (96, 96)]),
         ("panel", "hull", [(68, 74), (78, 72), (80, 84), (70, 86)], "cyan"),
         ("crack", "hull", [(52, 70), (56, 80), (52, 90), (56, 96)]),
         ("scorch", "fin.L", 38, 90, 3.5)],
        [("arc", 74, 79), ("flame", 70, 102), ("leak", 92, 92)]),
}

D["Paranoid"] = {
    1: ([("panel", "hull", [(38, 34), (48, 28), (52, 38), (42, 44)], "amber"),
         ("scorch", "hull", 86, 76, 5),
         ("crack", "pod.L", [(10, 50), (16, 56), (12, 62)])],
        [("sparks", 45, 36), ("smoke", 16, 82)]),
    2: ([("tear", "pod.R", [(104, 50), (112, 48), (114, 60), (106, 62)]),
         ("bend", "strut.R", [(88, 46), (104, 44), (104, 54), (88, 56)], [(90, 50), (102, 50)]),
         ("panel", "hull", [(84, 54), (94, 50), (98, 62), (88, 66)], "cyan"),
         ("crack", "hull", [(54, 82), (60, 86), (58, 92)])],
        [("arc", 91, 58), ("flame", 112, 82), ("leak", 64, 86)]),
}

D["Ninja"] = {
    1: ([("panel", "shuriken", [(60, 24), (66, 22), (68, 34), (61, 36)], "amber"),
         ("scorch", "shuriken", 92, 64, 5),
         ("crack", "shuriken", [(40, 62), (32, 66), (24, 63)])],
        [("sparks", 64, 29), ("smoke", 77, 77)]),
    2: ([("tear", "shuriken", [(100, 60), (112, 61), (114, 67), (100, 68)]),
         ("bend", "shuriken", [(58, 100), (70, 100), (66, 116), (62, 116)], [(59, 104), (69, 108)]),
         ("panel", "shuriken", [(30, 60), (40, 58), (42, 68), (30, 68)], "cyan"),
         ("crack", "hub", [(52, 70), (56, 76), (54, 80)])],
        [("arc", 36, 64), ("flame", 64, 98), ("leak", 106, 64)]),
}

D["Saboteur"] = {
    1: ([("panel", "horn.L", [(32, 26), (39, 24), (43, 38), (36, 40)], "amber"),
         ("scorch", "cradle", 86, 84, 4.5),
         ("crack", "core", [(74, 56), (78, 62), (76, 68)])],
        [("sparks", 37, 32), ("smoke", 64, 104)]),
    2: ([("tear", "horn.R", [(90, 6), (98, 4), (101, 14), (93, 16)]),
         ("panel", "cradle", [(40, 74), (48, 78), (46, 88), (38, 84)], "cyan"),
         ("bend", "horn.R", [(82, 40), (90, 40), (96, 50), (86, 54)], [(84, 44), (94, 48)]),
         ("crack", "cradle", [(74, 92), (80, 90), (86, 94)])],
        [("arc", 43, 81), ("flame", 86, 84), ("leak", 94, 12)]),
}

D["UFO"] = {
    1: ([("panel", "disc", [(14, 68), (24, 66), (26, 80), (16, 82)], "amber"),
         ("scorch", "disc", 92, 92, 5.5),
         ("crack", "disc", [(64, 104), (58, 110), (62, 118)])],
        [("sparks", 20, 74), ("smoke", 90, 96)]),
    2: ([("tear", "disc", [(98, 28), (108, 32), (114, 44), (104, 46), (98, 38)]),
         ("panel", "disc", [(36, 96), (46, 100), (42, 110), (32, 106)], "cyan"),
         ("crack", "disc", [(84, 32), (90, 38), (88, 46)]),
         ("scorch", "hub", 72, 74, 3)],
        [("arc", 39, 103), ("flame", 106, 40), ("leak", 70, 108)]),
}

D["Dove"] = {
    1: ([("panel", "hull", [(40, 50), (50, 48), (52, 60), (42, 62)], "cyan"),
         ("scorch", "hull", 82, 78, 5),
         ("crack", "hull", [(60, 66), (64, 72), (61, 80)])],
        [("sparks", 46, 55), ("smoke", 64, 104)]),
    2: ([("tear", "wing.R", [(96, 52), (104, 50), (108, 60), (100, 62)]),
         ("bend", "wing.L", [(16, 64), (24, 66), (30, 62), (20, 56)], [(18, 60), (28, 63)]),
         ("panel", "hull", [(68, 82), (76, 78), (78, 88), (70, 92)], "amber"),
         ("scorch", "tail", 60, 106, 3.5)],
        [("arc", 100, 56), ("flame", 73, 86), ("leak", 54, 84)]),
}

D["Turtle"] = {
    1: ([("panel", "shell", [(40, 60), (50, 58), (52, 70), (42, 72)], "amber"),
         ("crack", "shell", [(70, 42), (76, 48), (72, 54), (78, 58)]),
         ("scorch", "shell", 82, 86, 4.5)],
        [("sparks", 46, 65), ("smoke", 64, 100)]),
    2: ([("panel", "shell", [(58, 74), (70, 74), (72, 86), (58, 86)], "cyan"),
         ("bend", "flipper.R", [(90, 26), (102, 18), (104, 28), (92, 36)], [(94, 24), (98, 32)]),
         ("tear", "shell", [(84, 66), (90, 64), (92, 76), (86, 78)]),
         ("scorch", "shell", 46, 88, 3.5)],
        [("arc", 64, 80), ("flame", 87, 70), ("leak", 50, 94)]),
}

CONCEPTS = {
    "NeonComet":   ("left wing panel blown, wiring sparks; scorched right wing",
                    "right wingtip torn off, spine cracked, right pod burning, canopy cracked"),
    "VoltViper":   ("hull panel blown over the right wing root; left engine smoking",
                    "left wingtip torn, right wing bent, right leg on fire, canopy cracked"),
    "SolarFang":   ("left flank panel blown, reactor cracked; nozzle smoking",
                    "left fang bent, reactor housing open and arcing, right tip torn, canopy cracked"),
    "CrimsonHalo": ("left wing panel blown, left halo cracked; tail smoking",
                    "right wingtip torn, right halo cracked and arcing, right tail fin bent, canopy cracked"),
    "IonLancer":   ("left wing panel blown; left pod smoking",
                    "right canard bent, right wingtip torn, right wing wiring arcing, canopy cracked"),
    "JadePhantom": ("left wing panel blown; spine smoking",
                    "right trailing edge torn, left wingtip bent, right wing wiring arcing, canopy cracked"),
    "GoldWarden":  ("right wing panel blown; left engine smoking",
                    "left gun barrel bent, left wingtip torn, belly panel open and arcing, canopy cracked"),
    "Lightning":   ("left pod panel blown; left pod engine smoking",
                    "right wingtip torn, right pod nose bent, right pod on fire, canopy cracked"),
    "Ligher":      ("hull panel blown, nose cap cracked; nozzle smoking",
                    "right fin bent, second panel open and arcing, leaking, canopy cracked"),
    "Paranoid":    ("disc rim panel blown; left pod smoking",
                    "right pod torn open, right strut bent, disc wiring arcing, canopy cracked"),
    "Ninja":       ("top blade panel blown; hub smoking",
                    "right blade tip torn, bottom blade bent, left blade wiring arcing, canopy cracked"),
    "Saboteur":    ("left horn panel blown, core cracked; tail smoking",
                    "right horn tip torn and bent, cradle panel open and arcing, canopy cracked"),
    "UFO":         ("rim panel blown; underside smoking",
                    "rim torn at upper right and burning, second rim panel arcing, canopy cracked"),
    "Dove":        ("flank panel blown; tail smoking",
                    "right wing torn, left wing bent, belly panel open, canopy cracked"),
    "Turtle":      ("shell plate blown, scute cracked; tail smoking",
                    "rear scute shattered and arcing, front flipper bent, shell torn, canopy cracked"),
}


# ------------------------------------------------------------- attaching --
def form_ref(fm):
    xs = [p[0] for p in fm.outline]
    c = (min(xs) + max(xs)) / 2
    side = "" if abs(c - 64) < 2 else (".L" if c < 64 else ".R")
    return fm.name + side


def attach(ship, m):
    """Maps the ship's damage table through its fit `m` (design -> canvas) and
    stores ship.damage = {state: [features]} (cumulative) and
    ship.emitters = {state: [emitters new at that state]}."""
    table = D[ship.key]
    refs = {}
    for i, fm in enumerate(ship.forms):
        refs.setdefault(form_ref(fm), i)
    ship.damage = {0: []}
    ship.emitters = {0: []}
    acc = []
    for state in (1, 2):
        feats, emits = table[state]
        for ft in feats:
            kind, ref = ft[0], ft[1]
            assert ref in refs, (ship.key, ref, sorted(refs))
            fi = refs[ref]
            if kind in ("panel", "tear"):
                pts = m(_scale(ft[2], GROW))
                acc.append((kind, fi, pts) + tuple(ft[3:]))
            elif kind == "bend":
                acc.append((kind, fi, m(ft[2]), m(ft[3])))
            elif kind == "crack":
                acc.append((kind, fi, m(_scale(ft[2], 1.2))))
            elif kind == "scorch":
                (x, y), = m([(ft[2], ft[3])])
                acc.append((kind, fi, x, y, ft[4] * GROW))
            else:
                raise ValueError(kind)
            # the feature must sit on its form
            o = ship.forms[fi].outline
            cx, cy = _centre(acc[-1])
            assert inside(o, cx, cy), (ship.key, state, ft)
        ship.damage[state] = list(acc)
        out = []
        for kind, x, y in emits:
            assert kind in EMITTER_KINDS, kind
            (u, v), = m([(x, y)])
            assert any(inside(fm.outline, u, v) for fm in ship.forms), (ship.key, state, kind, x, y)
            out.append((kind, u, v))
        ship.emitters[state] = out
    return ship


def _centre(ft):
    kind = ft[0]
    if kind == "scorch":
        return ft[2], ft[3]
    pts = ft[2]
    return sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts)


def inside(pts, x, y):
    c = False
    j = len(pts) - 1
    for i in range(len(pts)):
        xi, yi = pts[i]
        xj, yj = pts[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi + 1e-9) + xi:
            c = not c
        j = i
    return c


# -------------------------------------------------------------- drawing --
def _jag(pts, seed, amp=1.3):
    """Torn edge: every side gets a midpoint knocked in or out."""
    out = []
    k = seed
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        out.append(a)
        dx, dy = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dx, dy) or 1
        for t in (0.35, 0.7):
            k = (k * 1103515245 + 12345) % (2 ** 31)
            s = ((k % 1000) / 1000.0 - 0.5) * 2 * amp
            out.append((a[0] + dx * t - dy / ln * s, a[1] + dy * t + dx / ln * s))
    return out


def _centroid(pts):
    return sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts)


def _scale(pts, k, c=None):
    cx, cy = c or _centroid(pts)
    return [(cx + (x - cx) * k, cy + (y - cy) * k) for x, y in pts]


def draw(ft, hue, W, seed):
    """SVG for one damage feature (already clipped to its form by render)."""
    kind = ft[0]
    s = ""
    if kind == "panel":
        pts, glow = ft[2], ft[3]
        j = _jag(pts, seed)
        cx, cy = _centroid(pts)
        s += poly(W(_scale(j, 1.18)), hue[1])                 # buckled skin round the hole
        s += poly(W(j), GUN_SH)
        # spars across the hole
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        w, h = max(xs) - min(xs), max(ys) - min(ys)
        s += line(W([(cx - w * .4, cy - h * .15), (cx + w * .4, cy - h * .3)]), 1.2, GUN_HI)
        s += line(W([(cx - w * .35, cy + h * .3), (cx + w * .35, cy + h * .15)]), 1.2, GUN_HI)
        # two live wires, inked
        wa = [(cx - w * .45, cy + h * .4), (cx - w * .1, cy + h * .05), (cx + w * .05, cy - h * .2)]
        wb = [(cx + w * .45, cy + h * .35), (cx + w * .15, cy + h * .2), (cx + w * .1, cy - h * .05)]
        s += line(W(wa), 2.6, INK) + line(W(wa), 1.2, RED)
        s += line(W(wb), 2.6, INK) + line(W(wb), 1.2, AMBER)
        # the exposed glow
        col = AMBER if glow == "amber" else CYAN
        r = max(1.6, min(w, h) * .2)
        (gx, gy), = W([(cx + w * .08, cy - h * .12)])
        d = [(gx, gy - r * 1.3), (gx + r, gy), (gx, gy + r * 1.3), (gx - r, gy)]
        s += poly(d, col) + inkpoly(d, 1.1)
        s += poly([(gx, gy - r * .5), (gx + r * .4, gy), (gx, gy + r * .5), (gx - r * .4, gy)], BONE)
        s += inkpoly(W(j), 1.8)
    elif kind == "tear":
        pts = ft[2]
        j = _jag(pts, seed, 1.8)
        cx, cy = _centroid(pts)
        s += poly(W(_scale(j, 1.25)), hue[1])
        s += poly(W(j), GUN_SH)
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
        for t in (.3, .65):
            s += line(W([(x0 + (x1 - x0) * t, y0), (x0 + (x1 - x0) * (t + .1), y1)]), 1.4, GUN)
        s += line(W([(x0, cy), (x1, cy - 1)]), 1.2, GUN_HI)
        s += inkpoly(W(j), 2.0)
        # hot torn metal glowing just inside the edge
        s += inkpoly(W(_scale(j, .78)), 1.1, SODIUM)
        # a curled lip of skin on the upper-left edge
        s += line(W(_scale(j, 1.12)[:3]), 1.2, hue[2])
    elif kind == "bend":
        pts, crease = ft[2], ft[3]
        s += poly(W(pts), hue[1])
        s += line(W([(x + .9, y + .9) for x, y in crease]), 1.2, hue[2])
        s += line(W(crease), 1.6, INK)
    elif kind == "crack":
        pts = ft[2]
        s += line(W([(x + .9, y + .9) for x, y in pts]), 1.1, hue[2])
        s += line(W(pts), 1.8, INK)
        if len(pts) >= 3:
            a, b = pts[1], pts[2]
            br = [a, (a[0] + (b[0] - a[0]) * .3 + 3, a[1] + (b[1] - a[1]) * .3 - 1.5)]
            s += line(W(br), 1.1, INK)
    elif kind == "scorch":
        x, y, r = ft[2], ft[3], ft[4]
        s += poly(W(star(x, y, r * 1.35, r * .9, 7, rot=seed * 17)), hue[1])
        s += poly(W(star(x, y, r, r * .55, 5, rot=seed * 23)), GUN_SH)
        s += poly(W(star(x, y, r * .45, r * .25, 4, rot=seed * 40)), INK)
        s += line(W([(x - r * 1.5, y - r * .5), (x - r * .4, y + r * .1)]), 1.2, INK)
        s += line(W([(x + r * .3, y - r * .4), (x + r * 1.4, y + r * .3)]), 1.2, INK)
    return s


def canopy_crack(canopy, W):
    """Last life: a star crack across the glass, a shattered shadow wedge and
    a bone chip at the impact."""
    xs = [p[0] for p in canopy]
    ys = [p[1] for p in canopy]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    w, h = x1 - x0, y1 - y0
    ix, iy = x0 + w * .62, y0 + h * .42
    s = poly(W([(ix, iy), (x1 + 2, iy - h * .1), (x1 + 2, iy + h * .35)]), TEAL_SH)
    rays = [(-1.0, -0.9), (1.1, -0.5), (1.0, 0.7), (-0.6, 1.1), (-1.2, 0.2)]
    for k, (dx, dy) in enumerate(rays):
        L = max(w, h) * (.55 if k % 2 == 0 else .4)
        mid = (ix + dx * L * .5 + (1 if k % 2 else -1), iy + dy * L * .5)
        s += line(W([(ix, iy), mid, (ix + dx * L, iy + dy * L)]), 1.1, INK)
    s += poly(W(star(ix, iy, 2.2, 1.0, 4, rot=20)), BONE) + inkpoly(W(star(ix, iy, 2.2, 1.0, 4, rot=20)), .8)
    return s


def holes(ship, state):
    """The panel / tear areas of a state: stripes are masked out of them."""
    return [ft[2] for ft in ship.damage.get(state, []) if ft[0] in ("panel", "tear")]
