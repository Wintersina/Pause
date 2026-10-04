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
import random

from hullkit import *  # noqa: F401,F403

# features read at ~110 px on a phone: everything is drawn this much bigger
# than the table's polygons, about its own centre
GROW = 1.3

EMITTER_KINDS = ["sparks", "arc", "smoke", "flame", "leak", "smolder"]


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
        if state == 2:
            chars, extra, smolder = wreck(ship, acc)
            acc = chars + acc + extra
        ship.damage[state] = list(acc)
        out = []
        for kind, x, y in emits:
            assert kind in EMITTER_KINDS, kind
            (u, v), = m([(x, y)])
            assert any(inside(fm.outline, u, v) for fm in ship.forms), (ship.key, state, kind, x, y)
            out.append((kind, u, v))
        # smoke curls out of every blown / torn spot this state opened
        if state == 1:
            smolder = [_inner_point(ship, ft) for ft in acc if ft[0] in ("panel", "tear")]
        out += [("smolder", u, v) for u, v in smolder if u is not None]
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
    else:
        s += draw_wreck(ft, hue, W, seed)
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
    return [ft[2] for ft in ship.damage.get(state, []) if ft[0] in ("panel", "tear", "hole", "rip", "char")]


# ------------------------------------------------------------ the wreck --
# The last life is a wreck, not the damaged hull plus one more dent. On top
# of the hand-placed row-2 features, every ship gets (seeded by its key, laid
# out on its own forms, so no two ships are alike):
#
#   char    the burn spreading round every panel / tear / hole: a charred
#           halo of hull shadow, burnt tone and black flecks
#   hole    a chunk blown clean through: space behind a skeleton of exposed
#           frame ribs and a stringer, a dangling wire, hot torn rim, curled
#           petals of skin
#   rip     a bite torn out of the hull's edge: the same void and frame,
#           opening onto the outline
#   crack   long branching cracks running off the wounds
#   scorch  more soot stars
#
# Everything is still clipped to its form (the alpha never changes). Each
# panel / tear / hole / rip opened on the last life gets a smolder emitter
# (at most MAX_SMOLDER).

VOID = "#0E1424"       # space seen through a hole (the guide palette's space dark)
MAX_SMOLDER = 6
WRECK_N = {"hole": 2, "rip": 2, "crack": 4, "scorch": 4}   # + 1 hole on a big hull


def _seg_dist(px, py, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = dx * dx + dy * dy
    t = 0 if L == 0 else max(0, min(1, ((px - a[0]) * dx + (py - a[1]) * dy) / L))
    return math.hypot(px - a[0] - dx * t, py - a[1] - dy * t)


def edge_dist(pts, x, y):
    return min(_seg_dist(x, y, pts[i], pts[(i + 1) % len(pts)]) for i in range(len(pts)))


def _covered(ship, fi, x, y):
    """Is (x, y) under a form drawn after form fi (a feature there is hidden)?"""
    return any(inside(ship.forms[j].outline, x, y) for j in range(fi + 1, len(ship.forms)))


def _bbox(pts, pad=0):
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return min(xs) - pad, min(ys) - pad, max(xs) + pad, max(ys) + pad


def _in_box(b, x, y):
    return b[0] <= x <= b[2] and b[1] <= y <= b[3]


def _keepout(ship):
    """Canopy, lights and nozzles stay readable: no wreck feature on them."""
    boxes = []
    if ship.canopy:
        boxes.append(_bbox(ship.canopy, 3))
    for x, y, w, _ in ship.nozzles:
        boxes.append((x - max(w, 4) - 4, y - 9, x + max(w, 4) + 4, y + 4))
    for x, y, r, _ in ship.lights:
        boxes.append((x - r * 2.2, y - r * 2.2, x + r * 2.2, y + r * 2.2))
    return boxes


def _point_ok(ship, fi, x, y, margin, keep):
    o = ship.forms[fi].outline
    if not inside(o, x, y) or edge_dist(o, x, y) < margin or _covered(ship, fi, x, y):
        return False
    return not any(_in_box(b, x, y) for b in keep)


def _inner_point(ship, ft, margin=3.2):
    """A point of feature ft well inside its form (an emitter spot), or (None, None)."""
    fi = ft[1]
    cx, cy = _centre(ft)
    o = ship.forms[fi].outline
    fx, fy = _centroid(o)
    for k in (0, .25, .5, .75):
        x, y = cx + (fx - cx) * k, cy + (fy - cy) * k
        if inside(o, x, y) and edge_dist(o, x, y) >= margin and not _covered(ship, fi, x, y):
            return x, y
    return None, None


def _blob(rng, cx, cy, r, n=9, rough=.28, sx=1.0, sy=1.0):
    out = []
    a0 = rng.uniform(0, math.tau)
    for i in range(n):
        a = a0 + math.tau * i / n
        rr = r * (1 + rng.uniform(-rough, rough))
        out.append((cx + math.cos(a) * rr * sx, cy + math.sin(a) * rr * sy))
    return out


def _area(pts):
    n = len(pts)
    return abs(sum(pts[i][0] * pts[(i + 1) % n][1] - pts[(i + 1) % n][0] * pts[i][1] for i in range(n))) / 2


def wreck(ship, acc):
    """The last life's extra features: (chars, extras, smolder points)."""
    rng = random.Random(sum(ord(c) * (i + 1) for i, c in enumerate(ship.key)))
    keep = _keepout(ship)
    row1 = ship.damage.get(1, [])
    taken = [(_centre(ft), 7) for ft in acc]
    # forms worth wrecking, weighted by area
    cand = [(i, _area(fm.outline)) for i, fm in enumerate(ship.forms)
            if fm.tone in ("hull", "gun") and _area(fm.outline) > 120]
    total = sum(a for _, a in cand)

    def pick_form():
        t = rng.uniform(0, total)
        for i, a in cand:
            t -= a
            if t <= 0:
                return i
        return cand[-1][0]

    def free(x, y, r):
        return all(math.hypot(x - c[0], y - c[1]) > r + rr for c, rr in taken)

    def sample(r, margin, tries=500):
        for _ in range(tries):
            fi = pick_form()
            x0, y0, x1, y1 = _bbox(ship.forms[fi].outline)
            x, y = rng.uniform(x0, x1), rng.uniform(y0, y1)
            if _point_ok(ship, fi, x, y, margin, keep) and free(x, y, r):
                return fi, x, y
        return None

    extra, smolder = [], []
    # holes: blown clean through
    for k in range(WRECK_N["hole"] + (total > 6000)):
        r = rng.uniform(7.5, 10.5)
        s = sample(r, 3.5) or sample(r * .7, 3.2)
        if not s:
            continue
        fi, x, y = s
        pts = _blob(rng, x, y, r, 8, .32, rng.uniform(.8, 1.15), rng.uniform(.8, 1.15))
        extra.append(("hole", fi, pts, k))
        taken.append(((x, y), r + 2))
        smolder.append((x, y))
    # rips: bites torn out of the silhouette's edge
    for k in range(WRECK_N["rip"]):
        got = None
        for _ in range(800):
            fi = pick_form()
            o = ship.forms[fi].outline
            i = rng.randrange(len(o))
            a, b = o[i], o[(i + 1) % len(o)]
            ln = math.hypot(b[0] - a[0], b[1] - a[1])
            if ln < 8:
                continue
            t = rng.uniform(.3, .7)
            px, py = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
            tx, ty = (b[0] - a[0]) / ln, (b[1] - a[1]) / ln
            nx, ny = -ty, tx
            if not inside(o, px + nx * 2, py + ny * 2):
                nx, ny = -nx, -ny
            # a real outer edge: nothing else drawn just outside it
            if any(inside(fm.outline, px - nx * 3, py - ny * 3) for fm in ship.forms):
                continue
            d = rng.uniform(7.5, 10.5)
            w = min(ln * .45, rng.uniform(6, 9))
            ex, ey = px + nx * d * .5, py + ny * d * .5
            if any(_in_box(bb, px, py) or _in_box(bb, ex, ey) for bb in keep):
                continue
            if not free(ex, ey, w) or _covered(ship, fi, ex, ey):
                continue
            if not (inside(o, ex, ey) and edge_dist(o, ex, ey) >= 3.2):
                continue
            got = (fi, px, py, tx, ty, nx, ny, d, w, ex, ey)
            break
        if not got:
            continue
        fi, px, py, tx, ty, nx, ny, d, w, ex, ey = got
        pts = [(px - tx * w - nx * 3, py - ty * w - ny * 3)]
        steps = 5
        for j in range(steps + 1):
            u = -1 + 2 * j / steps
            dep = d * (1 - abs(u) ** 1.6) * rng.uniform(.75, 1.1)
            pts.append((px + tx * w * u + nx * dep, py + ty * w * u + ny * dep))
        pts.append((px + tx * w - nx * 3, py + ty * w - ny * 3))
        extra.append(("rip", fi, pts, k))
        taken.append(((ex, ey), w + 2))
        smolder.append((ex, ey))
    # cracks running off the wounds
    wounds = [ft for ft in acc + extra if ft[0] in ("panel", "tear", "hole", "rip")]
    for k in range(WRECK_N["crack"]):
        if not wounds:
            break
        ft = wounds[k % len(wounds)]
        fi = ft[1]
        o = ship.forms[fi].outline
        cx, cy = _centre(ft) if ft[0] != "rip" else _centroid(ft[2])
        a = rng.uniform(0, math.tau)
        pts = [(cx, cy)]
        x, y = cx, cy
        for _ in range(6):
            a += rng.uniform(-.7, .7)
            nx_, ny_ = x + math.cos(a) * 5, y + math.sin(a) * 5
            if not inside(o, nx_, ny_):
                break
            x, y = nx_, ny_
            pts.append((x, y))
        if len(pts) >= 3 and inside(o, *_centroid(pts)):
            extra.append(("crack", fi, pts))
    # soot
    for k in range(WRECK_N["scorch"]):
        r = rng.uniform(4.0, 6.0)
        s = sample(r, 2.0)
        if not s:
            continue
        fi, x, y = s
        extra.append(("scorch", fi, x, y, r))
        taken.append(((x, y), r))
    # the burn spreads round every wound (drawn first, under everything)
    chars = []
    for ft in acc + extra:
        if ft[0] in ("panel", "tear", "hole", "rip"):
            cx, cy = _centroid(ft[2])
            x0, y0, x1, y1 = _bbox(ft[2])
            r = max(x1 - x0, y1 - y0) * .5
            if inside(ship.forms[ft[1]].outline, cx, cy):
                chars.append(("char", ft[1], _blob(rng, cx, cy, r * 1.3 + 2.5, 11, .3), len(chars)))
    # smoke from what this state opened: its hand-placed panels / tears first
    pts = [_inner_point(ship, ft) for ft in acc if ft[0] in ("panel", "tear") and ft not in row1]
    pts += smolder
    pts = [p for p in pts if p[0] is not None][:MAX_SMOLDER]
    return chars, extra, pts


def draw_wreck(ft, hue, W, seed):
    kind = ft[0]
    s = ""
    if kind == "char":
        pts = ft[2]
        s += poly(W(pts), hue[1])
        s += poly(W(_scale(pts, .74)), ROCK_SH)   # burnt black-violet
        rng = random.Random(ft[3] * 31 + 7)
        x0, y0, x1, y1 = _bbox(pts)
        for _ in range(6):
            fx, fy = rng.uniform(x0, x1), rng.uniform(y0, y1)
            s += poly(W(ngon(fx, fy, rng.uniform(.8, 1.6), 4, rng.uniform(0, 90))), INK)
    elif kind in ("hole", "rip"):
        pts = ft[2]
        j = _jag(pts, ft[3] * 5 + 3, 1.4)
        cx, cy = _centroid(pts)
        x0, y0, x1, y1 = _bbox(pts)
        w, h = x1 - x0, y1 - y0
        s += poly(W(_scale(j, 1.3)), GUN_SH)   # buckled, burnt skin
        s += poly(W(j), VOID)
        # exposed frame: two ribs and a stringer, inked
        rng = random.Random(ft[3] * 17 + len(pts))
        tilt = rng.uniform(-.35, .35)
        for t in (.3, .7):
            a = (x0 + w * t + h * tilt * .5, y0 - 1)
            b = (x0 + w * t - h * tilt * .5, y1 + 1)
            s += line(W([a, b]), 2.8, INK) + line(W([a, b]), 1.4, GUN_HI)
        yy = y0 + h * rng.uniform(.4, .6)
        s += line(W([(x0 - 1, yy), (x1 + 1, yy)]), 2.4, INK) + line(W([(x0 - 1, yy), (x1 + 1, yy)]), 1.1, GUN_HI)
        # a dangling wire with a hot tip
        wa = [(cx - w * .2, y0 + h * .2), (cx, cy + h * .1), (cx + w * .12, cy + h * .3)]
        s += line(W(wa), 2.2, INK) + line(W(wa), 1.0, RED)
        (tx, ty), = W([wa[-1]])
        s += poly(star(tx, ty, 1.9, .8, 4, rot=ft[3] * 13), AMBER)
        # hot torn rim and curled petals of skin
        s += inkpoly(W(j), 2.0)
        s += inkpoly(W(_scale(j, .86)), 1.0, SODIUM)
        for k in range(0, len(j), 3):
            p, n = j[k], j[(k + 1) % len(j)]
            q = (cx + (p[0] - cx) * 1.28, cy + (p[1] - cy) * 1.28)
            r = (p[0] + (n[0] - p[0]) * .8, p[1] + (n[1] - p[1]) * .8)
            s += poly(W([p, q, r]), hue[2]) + inkpoly(W([p, q, r]), .9)
    return s
