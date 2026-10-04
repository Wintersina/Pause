"""Per-skin damage designs, batch A: NeonComet, VoltViper, SolarFang,
CrimsonHalo, IonLancer -- every non-stock skin gets its own damage story
(damage.SKIN_D; the stock skin keeps damage.D).

Loaded by damage.py's per-skin module block while damage.py is still being
imported, so nothing here imports damage at module level (it is imported
lazily, at draw time, when it is complete).

Looks: the stock damage kinds draw soot in the hull's own shadow tone, which
all but vanishes on a dark skin (Night: soot on navy). These skins use a
small kit of extra feature styles drawn in tones picked per skin to contrast
with that skin's palette (pale ash and ember rims on dark hulls, black slag
with glowing cracks on orange ones, bullet holes and dark soot on bone):

    scar      burn mark: discoloured halo, ash body, ember ring, dark core
    bare      paint stripped to bright bare metal, seams and rivets
    plates    skin plates gone: bright exposed frame lattice over the dark
    gouge     long scrape (a path): parallel bright-metal furrows, hot head
    bolt      lightning burn (a path): charred track with an electric core
    pocks     a cluster of bullet holes, each trailing a soot streak
    blisters  heat-bubbled paint, some bubbles popped to the ash
    slag      melted crust with glowing cracks and drips
    ring      corona burn: dark disc, hot ring, ash eye
    slash     a narrow cut clean through, hot rim
    breach    a hole blown clean through (the wreck's hole, plus a hot halo)

Each is carried through damage.attach as a "tear" whose extra field is a
_Style (attach passes extra fields through, scales the polygon by GROW and
asserts it sits on its form), and drawn by a wrapper around
hullkit.damage_draw that handles only _Style features and passes everything
else on. A wrapper around hullkit.render remembers which skin is being drawn
(by the table's "seed", always "A:<ship>/<skin>") so the last-life wreck's
generated soot and char halos are drawn in the same per-skin tones; a wrapper
around hullkit.damage_holes keeps stripes off the new features. All three
wrappers chain to whatever was there before and only act on batch-A seeds.
Everything is still clipped to its form, so the alpha never changes.

The game's damage FX emitters are per ship (ShipDamageTable.cs), not per
skin, so each design also leaves a small mark (in the skin's style) under
every emitter point of the ship that its own features do not already cover
(EMITTERS below, design coordinates of the stock table), so smoke and sparks
never come out of a pristine panel.
"""
import math
import random
import zlib

import hullkit as _hk
from hullkit import *  # noqa: F401,F403
from ships import BUILDERS

SKIN_D = {}
CONCEPTS = {}   # (ship, skin) -> (damaged, critical) one-liners

STEEL, STEEL_SH, STEEL_HI = "#5A6A88", "#262D44", "#A3B4CC"
VOID = "#0E1424"


# --------------------------------------------------------------- looks --
class Look:
    def __init__(self, halo, ash, core, hot, metal, metal_sh, spark, fleck=INK):
        self.halo, self.ash, self.core, self.hot = halo, ash, core, hot
        self.metal, self.metal_sh, self.spark, self.fleck = metal, metal_sh, spark, fleck


LOOKS = {
    # dark indigo hull: pale ash, ember rims, bright frame
    "night": Look(STEEL, STEEL_HI, GUN_SH, AMBER, BONE, STEEL_HI, CYAN),
    # orange hull: black slag and char, bone-hot cores
    "heat": Look(SODIUM_SH, GUN, INK, BONE, STEEL_HI, STEEL, AMBER),
    # teal hull: dark char, amber embers, bone metal
    "teal": Look(TEAL_SH, GUN_SH, INK, AMBER, STEEL_HI, STEEL, AMBER),
    # off-white hull: dark soot, hot orange
    "bone": Look("#B9AE98", GUN, INK, SODIUM, STEEL, STEEL_SH, SODIUM),
    # red / crimson hulls: dark char, amber embers, pale metal
    "red": Look(None, GUN_SH, INK, AMBER, STEEL_HI, STEEL, AMBER),
    # azure hull: storm burns, electric bone core
    "azure": Look(None, GUN_SH, INK, AMBER, BONE, STEEL_HI, BONE),
    # cobalt hull: pale ash, sodium rims
    "cobalt": Look(None, STEEL_HI, GUN_SH, SODIUM, BONE, STEEL_HI, CYAN),
}


class _Style:
    """Marks a feature as one of this module's styles (see the docstring)."""
    def __init__(self, kind, **kw):
        self.kind = kind
        self.kw = kw

    def __repr__(self):
        return f"_Style({self.kind})"


PATHS = ("gouge", "bolt")


# ----------------------------------------------------------- authoring --
_FORMS = {}


def _forms(key):
    if key not in _FORMS:
        ship = BUILDERS[key]()
        out = []
        for fm in ship.forms:
            xs = [p[0] for p in fm.outline]
            c = (min(xs) + max(xs)) / 2
            side = "" if abs(c - 64) < 2 else (".L" if c < 64 else ".R")
            out.append((fm.name + side, fm.outline))
        _FORMS[key] = (out, ship.nozzles, ship.canopy)
    return _FORMS[key]


def _inside(pts, x, y):
    c = False
    j = len(pts) - 1
    for i in range(len(pts)):
        xi, yi = pts[i]
        xj, yj = pts[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi + 1e-9) + xi:
            c = not c
        j = i
    return c


def blob(cx, cy, rx, ry=None, n=9, rough=.22, rot=0.0):
    """A rough closed blob (design coords), deterministic for its centre."""
    ry = rx if ry is None else ry
    rng = random.Random(int(cx * 131 + cy * 17 + rx * 7))
    a0 = rng.uniform(0, math.tau)
    ca, sa = math.cos(rot), math.sin(rot)
    out = []
    for i in range(n):
        a = a0 + math.tau * i / n
        k = 1 + rng.uniform(-rough, rough)
        x, y = math.cos(a) * rx * k, math.sin(a) * ry * k
        out.append((cx + x * ca - y * sa, cy + x * sa + y * ca))
    return out


def F(kind, ref, pts, **kw):
    return ("tear", ref, [(float(x), float(y)) for x, y in pts], _Style(kind, **kw))


def B(kind, ref, cx, cy, rx, ry=None, rot=0.0, **kw):
    """A blob-shaped feature of style `kind`; rot in degrees."""
    return F(kind, ref, blob(cx, cy, rx, ry, rot=math.radians(rot)), **kw)


# per-ship FX emitter points (design coords) of the stock table, per state
EMITTERS = {
    "NeonComet": {1: [(32, 81), (52, 108)],
                  2: [(108, 92), (76, 106), (70, 92), (83, 90), (55, 98), (75, 57), (53, 58)]},
    "VoltViper": {1: [(76, 56), (52, 96)],
                  2: [(13, 64), (76, 96), (70, 84), (97, 48), (70, 14), (47, 73), (78, 80)]},
    "SolarFang": {1: [(46, 85), (64, 100)],
                  2: [(70, 70), (104, 102), (84, 96), (83, 57), (53, 41), (79, 100), (24, 90)]},
    "CrimsonHalo": {1: [(34, 80), (61, 106)],
                    2: [(86, 40), (106, 90), (66, 100), (80, 72), (49, 67), (46, 107)]},
    "IonLancer": {1: [(32, 90), (54, 104)],
                  2: [(92, 84), (74, 104), (64, 90), (112, 103), (50, 88), (76, 64), (47, 67), (22, 101)]},
}
COVER_R = 6.5   # an emitter this close to a feature's body counts as covered


def _near(ft, x, y):
    pts = ft[2] if ft[0] in ("tear", "panel", "bend", "crack") else None
    if ft[0] == "scorch":
        return math.hypot(ft[2] - x, ft[3] - y) <= ft[4] * 1.3 + COVER_R
    if pts is None:
        return False
    if ft[0] in ("tear", "panel") and not (isinstance(ft[-1], _Style) and ft[-1].kind in PATHS):
        if _inside(pts, x, y):
            return True
    return min(math.hypot(px - x, py - y) for px, py in pts) <= COVER_R


def _cover(key, state, feats, prev, mark):
    forms, nozzles, canopy = _forms(key)
    out = []
    for x, y in EMITTERS[key][state]:
        if any(_near(ft, x, y) for ft in prev + feats + out):
            continue
        # keep nozzles clear: slide the mark up off the mouth
        for nx, ny, w, _ in nozzles:
            if abs(x - nx) <= max(w, 4) + 4 and ny - 10 <= y <= ny + 4:
                y = ny - 10
        ref = None
        for name, o in forms:
            if _inside(o, x, y):
                ref = name     # the topmost form there
        assert ref, (key, state, x, y)
        out.append(B(mark, ref, x, y, 3.0, 2.6, small=True))
    return out


def design(key, skin, look, d1, d2, c1, c2, mark="scar"):
    """Registers skin `skin` of ship `key`: d1 / d2 are the damaged and
    critical features (critical adds to damaged), c1 / c2 the concepts."""
    f1 = list(d1) + _cover(key, 1, list(d1), [], mark)
    f2 = list(d2) + _cover(key, 2, list(d2), f1, mark)
    seed = f"A:{key}/{skin}"
    SKIN_D[(key, skin)] = {1: (f1, None), 2: (f2, None), "seed": seed}
    _SEED_LOOK[seed] = look
    CONCEPTS[(key, skin)] = (c1, c2)


_SEED_LOOK = {}


# ------------------------------------------------------------ drawing --
_CTX = [None]   # the look of the batch-A skin being rendered, else None


def _L(hue):
    L = LOOKS[_CTX[0]] if _CTX[0] else LOOKS["red"]
    if L.halo is None:
        L = Look(hue[1], L.ash, L.core, L.hot, L.metal, L.metal_sh, L.spark, L.fleck)
    return L


def _c(pts):
    return sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts)


def _sc(pts, k, c=None):
    cx, cy = c or _c(pts)
    return [(cx + (x - cx) * k, cy + (y - cy) * k) for x, y in pts]


def _bb(pts):
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return min(xs), min(ys), max(xs), max(ys)


def _clip(pts_w, body, tag):
    cid = "ac%08x" % (zlib.crc32((tag + repr([(round(x, 2), round(y, 2)) for x, y in pts_w])).encode()))
    d = "M" + " L".join(f"{f(x)},{f(y)}" for x, y in pts_w) + " Z"
    return f'<clipPath id="{cid}"><path d="{d}"/></clipPath><g clip-path="url(#{cid})">{body}</g>'


def _jag(pts, seed, amp=1.0):
    out = []
    k = seed
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        out.append(a)
        dx, dy = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dx, dy) or 1
        k = (k * 1103515245 + 12345) % (2 ** 31)
        s = ((k % 1000) / 1000.0 - 0.5) * 2 * amp
        out.append((a[0] + dx * .5 - dy / ln * s, a[1] + dy * .5 + dx / ln * s))
    return out


def _spots(pts, rng, n, margin=1.0, sep=3.2):
    """n points inside polygon pts, spread apart."""
    x0, y0, x1, y1 = _bb(pts)
    cx, cy = _c(pts)
    out = []
    for _ in range(400):
        if len(out) >= n:
            break
        x, y = rng.uniform(x0, x1), rng.uniform(y0, y1)
        inner = _sc(pts, max(.3, 1 - margin / max(1, (x1 - x0) / 2)), (cx, cy))
        if _inside(inner, x, y) and all(math.hypot(x - a, y - b) >= sep for a, b in out):
            out.append((x, y))
    return out


def draw_style(ft, hue, W, seed):
    st = ft[-1]
    kind, kw = st.kind, st.kw
    pts = ft[2]
    L = _L(hue)
    rng = random.Random(zlib.crc32(repr([(round(x, 1), round(y, 1)) for x, y in pts]).encode()))
    s = ""
    cx, cy = _c(pts)
    x0, y0, x1, y1 = _bb(pts)
    w, h = x1 - x0, y1 - y0
    if kind == "scar":
        s += poly(W(_sc(pts, 1.3)), L.halo)
        s += poly(W(pts), L.ash)
        body = ""
        for x, y in _spots(pts, rng, 3 if kw.get("small") else 6, 0.5, 1.6):
            body += poly(W(ngon(x, y, rng.uniform(.6, 1.2), 4, rng.uniform(0, 90))), L.fleck)
        s += _clip(W(pts), body, "scar")
        s += inkpoly(W(_sc(pts, .78)), 1.0, L.hot)
        s += poly(W(_sc(pts, .42)), L.core)
    elif kind == "bare":
        j = _jag(pts, rng.randrange(9999), 1.1)
        s += poly(W(_sc(j, 1.16)), hue[1])
        s += poly(W(j), L.metal_sh)
        s += _clip(W(j), poly(W([(x - 1.3, y - 1.3) for x, y in j]), L.metal), "bare")
        body = ""
        for t in (.33, .7):
            yy = y0 + h * t
            body += line(W([(x0 - 2, yy), (x1 + 2, yy - w * .08)]), .9, L.metal_sh)
            for k in range(int(w / 3.2) + 1):
                xx = x0 + 1.6 + k * 3.2
                body += poly(W(ngon(xx, yy - (xx - x0) * .08 + 1.2, .45, 4)), INK)
        s += _clip(W(j), body, "bares")
        s += inkpoly(W(j), 1.3)
        s += line(W(_sc(j, 1.1)[:4]), 1.2, hue[2])
    elif kind == "plates":
        j = _jag(pts, rng.randrange(9999), .9)
        s += poly(W(_sc(j, 1.14)), hue[1])
        s += poly(W(j), GUN_SH)
        body = ""
        step = kw.get("step", 4.2)
        k = 0
        x = x0 + step * .5
        while x < x1:
            body += line(W([(x, y0 - 1), (x - h * .15, y1 + 1)]), 2.8, INK) + line(W([(x, y0 - 1), (x - h * .15, y1 + 1)]), 1.5, L.metal)
            x += step
        y = y0 + step * .7
        while y < y1:
            body += line(W([(x0 - 1, y), (x1 + 1, y)]), 2.4, INK) + line(W([(x0 - 1, y), (x1 + 1, y)]), 1.2, L.metal_sh)
            y += step * 1.3
            k += 1
        # a glowing node and one plate still hanging on, askew
        gx, gy = cx + w * .15, cy - h * .1
        body += poly(W(star(gx, gy, 2.0, .9, 4, rot=10)), L.spark)
        px, py = x0 + w * .25, y0 + h * .7
        plate = [(px - 2.6, py - 1.8), (px + 2.2, py - 2.6), (px + 2.8, py + 1.6), (px - 2.0, py + 2.4)]
        body += poly(W(plate), hue[0]) + inkpoly(W(plate), .9)
        s += _clip(W(j), body, "plates")
        s += inkpoly(W(j), 1.5)
        s += inkpoly(W(_sc(j, .9)), .8, L.hot)
    elif kind == "gouge":
        a, b = pts[0], pts[-1]
        dx, dy = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dx, dy) or 1
        nx, ny = -dy / ln, dx / ln
        n = kw.get("n", 3)
        sp = kw.get("sp", 3.0)
        rough = [pts[0]]
        for p0, p1 in zip(pts, pts[1:]):
            for t in (.33, .66, 1.0):
                q = (p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t)
                rough.append(q if t == 1.0 else (q[0] + nx * rng.uniform(-.9, .9), q[1] + ny * rng.uniform(-.9, .9)))
        pts = rough
        for i in range(n):
            o = (i - (n - 1) / 2) * sp
            tl = 1 - .18 * abs(i - (n - 1) / 2)
            path = [(x + nx * o, y + ny * o) for x, y in pts]
            path = path[:1] + [(path[0][0] + (p[0] - path[0][0]) * tl, path[0][1] + (p[1] - path[0][1]) * tl) for p in path[1:]]
            s += line(W(path), 4.4, INK)
            s += line(W(path), 2.6, L.metal_sh)
            s += line(W([(x - .5, y - .5) for x, y in path]), 1.2, L.metal)
            for q in path[2::3]:
                s += poly(W(ngon(q[0], q[1], .9, 4, 45)), L.hot)
        hx, hy = pts[-1]
        s += poly(W(star(hx, hy, 2.6, 1.0, 5, rot=seed * 11)), L.hot) + poly(W(ngon(hx, hy, .8, 4)), BONE)
    elif kind == "bolt":
        path = [pts[0]]
        for i in range(1, len(pts)):
            a, b = pts[i - 1], pts[i]
            m = ((a[0] + b[0]) / 2 + rng.uniform(-1.6, 1.6), (a[1] + b[1]) / 2 + rng.uniform(-1.6, 1.6))
            path += [m, b]
        branches = []
        for i in (2, len(path) // 2 + 1):
            if i < len(path) - 1:
                bx, by = path[i]
                a = rng.uniform(0, math.tau)
                p1 = (bx + math.cos(a) * 3.5, by + math.sin(a) * 3.5)
                p2 = (p1[0] + math.cos(a + .6) * 3.0, p1[1] + math.sin(a + .6) * 3.0)
                branches.append([path[i], p1, p2])
        for p in [path] + branches:
            wd = 1.0 if p is path else .65
            s += line(W(p), 5.2 * wd, L.halo or hue[1])
        for p in [path] + branches:
            wd = 1.0 if p is path else .65
            s += line(W(p), 3.0 * wd, INK) + line(W(p), 1.5 * wd, L.spark)
        s += line(W(path), .6, BONE)
        sx, sy = pts[0]
        s += poly(W(star(sx, sy, 3.4, 1.2, 6, rot=seed * 7)), L.spark) + poly(W(ngon(sx, sy, 1.1, 4)), BONE)
    elif kind == "pocks":
        sp = _spots(pts, rng, kw.get("n", 5), 1.0, 4.8)
        for x, y in sp:
            streak = [(x - 1.8, y), (x + 1.8, y), (x + .6, y + kw.get("streak", 8)), (x - .5, y + 6)]
            s += poly(W(streak), L.ash)
        for x, y in sp:
            r = rng.uniform(1.8, 2.5)
            s += poly(W(star(x, y, r * 1.9, r * 1.2, 6, rot=rng.uniform(0, 60))), L.halo)
            s += poly(W(ngon(x, y, r * 1.25, 6)), L.hot)
            s += poly(W(ngon(x, y, r, 6)), VOID) + inkpoly(W(ngon(x, y, r, 6)), .8)
            s += poly(W(ngon(x - r * .35, y - r * .35, .45, 4)), BONE)
    elif kind == "blisters":
        s += poly(W(_sc(pts, 1.15)), L.halo)
        sp = _spots(pts, rng, kw.get("n", 7), 0.6, 2.8)
        for i, (x, y) in enumerate(sp):
            r = rng.uniform(1.4, 2.4)
            s += poly(W(ngon(x + .7, y + .7, r, 7)), hue[1])
            if i % 3 == 0:
                s += poly(W(ngon(x, y, r, 7)), L.ash) + inkpoly(W(ngon(x, y, r, 7)), .9, L.hot)
                s += poly(W(ngon(x, y, r * .45, 5)), L.core)
            else:
                s += poly(W(ngon(x, y, r, 7)), hue[2]) + inkpoly(W(ngon(x, y, r, 7)), .8)
                s += poly(W(ngon(x - r * .3, y - r * .35, r * .3, 4)), BONE)
    elif kind == "slag":
        j = _jag(pts, rng.randrange(9999), 1.0)
        s += poly(W(_sc(j, 1.22)), L.halo)
        # drips running aft
        for t in (.25, .5, .78):
            x = x0 + w * t
            yb = y1 - h * .12
            d = [(x - 1.1, yb - 2), (x + 1.1, yb - 2), (x + .5, yb + rng.uniform(3.5, 6.5)), (x - .5, yb + 3)]
            s += poly(W(d), L.ash) + poly(W(ngon(x, d[2][1] - .8, .7, 4)), AMBER)
        s += poly(W(j), L.ash)
        body = ""
        for k in range(4):
            a = rng.uniform(0, math.pi)
            r = max(w, h) * .55
            p = [(cx - math.cos(a) * r, cy - math.sin(a) * r), (cx + rng.uniform(-1.5, 1.5), cy + rng.uniform(-1.5, 1.5)),
                 (cx + math.cos(a) * r, cy + math.sin(a) * r)]
            body += line(W(p), 1.5, AMBER) + line(W(p), .55, BONE)
        s += _clip(W(_sc(j, .86)), body, "slag")
        s += inkpoly(W(j), 1.2)
        s += poly(W(ngon(cx, cy, 1.5, 6)), BONE)
    elif kind == "ring":
        s += poly(W(_sc(pts, 1.25)), L.halo)
        s += poly(W(pts), L.core)
        s += inkpoly(W(_sc(pts, .82)), 2.2, L.hot)
        s += inkpoly(W(_sc(pts, .82)), .7, BONE)
        s += poly(W(_sc(pts, .4)), L.ash)
        body = ""
        for k in range(8):
            a = math.tau * k / 8 + rng.uniform(-.2, .2)
            body += line(W([(cx + math.cos(a) * w * .25, cy + math.sin(a) * h * .25),
                            (cx + math.cos(a) * w * .62, cy + math.sin(a) * h * .62)]), .8, L.ash)
        s += body
        s += inkpoly(W(pts), 1.0)
    elif kind == "slash":
        s += poly(W(_sc(pts, 1.3)), hue[1])
        s += poly(W(pts), VOID)
        s += inkpoly(W(_sc(pts, .8)), 1.0, L.hot)
        s += line(W([pts[0], _c(pts), pts[len(pts) // 2]]), 1.0, L.hot)
        s += inkpoly(W(pts), 1.4)
        a = pts[0]
        s += poly(W(star(a[0], a[1], 2.2, .9, 4, rot=30)), L.hot)
    elif kind == "breach":
        import damage
        s += poly(W(_sc(pts, 1.42)), L.halo)
        s += inkpoly(W(_sc(pts, 1.32)), 1.0, L.hot)
        s += damage.draw_wreck(("hole", ft[1], pts, kw.get("k", 1)), hue, W, seed)
    else:
        raise ValueError(kind)
    return s


def _wreck_restyle(ft, hue, W, seed):
    """The wreck's generated soot / char, in the skin's look."""
    L = _L(hue)
    if ft[0] == "scorch":
        x, y, r = ft[2], ft[3], ft[4]
        pts = blob(x, y, r * .85, r * .75)
        return draw_style(("tear", ft[1], pts, _Style("scar")), hue, W, seed)
    if ft[0] == "char":
        pts = ft[2]
        rng = random.Random(ft[3] * 31 + 7)
        s = poly(W(pts), L.halo)
        s += poly(W(_sc(pts, .52)), L.ash)
        x0, y0, x1, y1 = _bb(pts)
        for k in range(7):
            fx, fy = rng.uniform(x0, x1), rng.uniform(y0, y1)
            s += poly(W(ngon(fx, fy, rng.uniform(.8, 1.5), 4, rng.uniform(0, 90))), L.hot if k % 3 == 0 else INK)
        return s
    return None


# ------------------------------------------------------------ hooks --
if not getattr(_hk, "_skin_damage_a", False):
    _hk._skin_damage_a = True
    _prev_draw, _prev_holes, _prev_render = _hk.damage_draw, _hk.damage_holes, _hk.render

    def _draw(ft, hue, W, seed):
        if len(ft) > 3 and isinstance(ft[-1], _Style):
            return draw_style(ft, hue, W, seed)
        if _CTX[0] and ft[0] in ("scorch", "char"):
            return _wreck_restyle(ft, hue, W, seed)
        return _prev_draw(ft, hue, W, seed)

    def _holes(ship, state):
        if not any(len(ft) > 3 and isinstance(ft[-1], _Style) for ft in getattr(ship, "damage", {}).get(state, [])):
            return _prev_holes(ship, state)
        out = []
        for ft in ship.damage.get(state, []):
            if len(ft) > 3 and isinstance(ft[-1], _Style):
                if ft[-1].kind in PATHS:
                    p = ft[2]
                    for a, b in zip(p, p[1:]):
                        dx, dy = b[0] - a[0], b[1] - a[1]
                        ln = math.hypot(dx, dy) or 1
                        nx, ny = -dy / ln * 4, dx / ln * 4
                        out.append([(a[0] + nx, a[1] + ny), (b[0] + nx, b[1] + ny), (b[0] - nx, b[1] - ny), (a[0] - nx, a[1] - ny)])
                else:
                    out.append(ft[2])
            elif ft[0] in ("panel", "tear", "hole", "rip", "char"):
                out.append(ft[2])
        return out

    def _render(ship, hue, pose, uid="s", livery=None, trim=None):
        prev = _CTX[0]
        _CTX[0] = _SEED_LOOK.get(getattr(ship, "damage_seed", None))
        try:
            return _prev_render(ship, hue, pose, uid, livery=livery, trim=trim)
        finally:
            _CTX[0] = prev

    _hk.damage_draw, _hk.damage_holes, _hk.render = _draw, _holes, _render


# ============================================================ designs ==
# Design coordinates of ships.py, as in damage.D. Critical (2) adds to
# damaged (1); the generated last-life wreck (holes, bites, cracks, soot,
# char) is seeded per skin and drawn in the skin's look.

# ---------------------------------------------------------- NeonComet --
# stock: left wing panel; critical right wingtip torn
design("NeonComet", "Night", "night",
       [B("plates", "hull", 100, 86, 8, 5.5, rot=-30),
        B("scar", "hull", 86, 74, 4.5, 3.5)],
       [B("breach", "hull", 47, 72, 5.5, 5),
        B("bare", "hull", 84, 63, 3.5, 3),
        B("scar", "pod.R", 76, 100, 3.2, 4)],
       "stealth coat burnt off the right wing: bright frame lattice and pale ash",
       "left wing root blown through, right wing stripped to bare metal, pale ash everywhere")

design("NeonComet", "Sodium", "heat",
       [F("slag", "hull", blob(64, 78, 6, 7)),
        B("blisters", "hull", 48, 66, 5, 4, n=5)],
       [B("breach", "hull", 24, 90, 5, 4.5),
        F("slag", "pod.R", blob(76, 100, 4, 5)),
        B("blisters", "hull", 96, 80, 5, 4, n=5)],
       "overheated: a slag puddle on the spine, paint blistering at the left root",
       "left wingtip melted through, right pod and wing slagged and blistered")

design("NeonComet", "Bone", "bone",
       [B("pocks", "hull", 46, 68, 7, 7, n=6),
        ("crack", "hull", [(38, 74), (34, 80), (30, 79)])],
       [B("pocks", "hull", 96, 82, 8, 6, n=6),
        B("breach", "pod.R", 76, 98, 4, 5),
        B("pocks", "hull", 64, 92, 3, 6, n=3)],
       "shot up: a burst of bullet holes trailing soot across the left wing root",
       "second burst across the right wing, right pod holed, spine stitched")

design("NeonComet", "Kaneda", "red",
       [F("gouge", "hull", [(80, 58), (90, 66), (104, 80)], n=3),
        B("bare", "hull", 92, 70, 4, 3)],
       [B("bare", "hull", 64, 86, 4, 9),
        F("gouge", "pod.L", [(52, 92), (54, 106)], n=2, sp=2.2),
        B("breach", "hull", 106, 90, 4.5, 4)],
       "road rash: the right leading edge scraped down to bare metal",
       "racing stripes peeled off the spine, left pod scraped, right wingtip holed")

# ---------------------------------------------------------- VoltViper --
# stock: hull panel right of the spine; critical left wingtip torn, right wing bent
design("VoltViper", "Night", "night",
       [B("plates", "wing.R", 103, 58, 9, 5, rot=20),
        B("scar", "hull", 50, 64, 4, 5)],
       [B("breach", "hull", 78, 72, 5, 5),
        B("bare", "wing.L", 28, 52, 5, 3.5, rot=-25),
        B("scar", "leg.R", 76, 94, 3, 3.5)],
       "stealth coat burnt off the right wing down to its frame; ash on the belly",
       "belly blown through by the right leg, left wing stripped bare, pale ash spreading")

design("VoltViper", "Sodium", "heat",
       [F("slag", "hull", blob(50, 62, 6, 8)),
        B("blisters", "wing.L", 34, 56, 6, 4, rot=-20, n=6)],
       [B("breach", "wing.R", 104, 60, 5, 4),
        F("slag", "hull", blob(80, 30, 4, 6)),
        B("blisters", "hull", 76, 64, 5, 5, n=5)],
       "overheated: slag running down the left belly, left wing root blistering",
       "right wing melted through, right shoulder slagged, belly blistered")

design("VoltViper", "Bone", "bone",
       [B("pocks", "hull", 62, 62, 12, 6, n=6),
        ("crack", "hull", [(80, 50), (84, 56), (82, 62)])],
       [B("pocks", "wing.L", 22, 58, 9, 5, rot=-20, n=5),
        B("breach", "hull", 48, 30, 4, 5),
        B("pocks", "wing.R", 104, 56, 8, 5, rot=20, n=4)],
       "shot up: a burst of bullet holes stitched across the belly",
       "both wings stitched with holes, left shoulder blown through")

design("VoltViper", "Storm", "azure",
       [F("bolt", "wing.L", [(10, 64), (22, 57), (38, 50)]),
        F("bolt", "hull", [(44, 40), (52, 52), (56, 70)])],
       [F("bolt", "wing.R", [(118, 64), (106, 58), (90, 54)]),
        B("breach", "hull", 76, 60, 5, 5),
        F("bolt", "hull", [(84, 26), (78, 36), (82, 44)])],
       "lightning strike: a burnt bolt track across the left wing into the belly",
       "struck again on the right wing, right flank blown open, shoulder arcing")

# ---------------------------------------------------------- SolarFang --
# stock: left flank panel; critical left fang bent, right tip torn, reactor opened
design("SolarFang", "Night", "night",
       [B("plates", "hull", 80, 56, 6, 8, rot=-30),
        B("scar", "hull", 94, 90, 4, 4)],
       [B("breach", "hull", 28, 94, 5, 4.5),
        B("bare", "hull", 52, 62, 3.5, 5),
        B("scar", "fang.R", 102, 74, 2.5, 4)],
       "stealth coat burnt off the right shoulder down to its frame",
       "left wing blown through, left shoulder stripped bare, ash spreading")

design("SolarFang", "Neon", "teal",
       [F("gouge", "hull", [(76, 66), (90, 80), (104, 96)], n=3),
        B("bare", "hull", 84, 86, 3.5, 3)],
       [F("gouge", "hull", [(48, 52), (38, 70), (26, 90)], n=3),
        B("breach", "hull", 54, 80, 5, 5),
        B("pocks", "hull", 92, 64, 4, 4, n=2)],
       "scraped: deep furrows gouged down the right wing to bright metal",
       "left wing gouged too, left flank blown through")

design("SolarFang", "Bone", "bone",
       [B("pocks", "hull", 52, 58, 6, 8, n=6),
        ("crack", "hull", [(58, 70), (54, 76), (56, 82)])],
       [B("pocks", "hull", 98, 96, 8, 5, n=5),
        B("breach", "hull", 76, 86, 5, 5),
        B("pocks", "hull", 30, 92, 6, 5, n=3)],
       "shot up: a burst of bullet holes across the left shoulder",
       "both wingtips stitched, right of the reactor blown through")

design("SolarFang", "Eclipse", "heat",
       [F("ring", "hull", ngon(64, 72, 8.5, 10)),
        B("scar", "hull", 46, 86, 3.5, 3.5)],
       [F("ring", "hull", ngon(30, 92, 6, 9)),
        F("ring", "hull", ngon(98, 92, 6, 9)),
        B("breach", "hull", 64, 57, 3.5, 3)],
       "eclipse: the reactor overloads into a black corona burn",
       "corona burns blossom on both wings, the spine behind the canopy blown open")

# -------------------------------------------------------- CrimsonHalo --
# stock: left wing panel, left halo cracked; critical right wingtip torn, tail fin bent
design("CrimsonHalo", "Night", "night",
       [B("plates", "wing.R", 92, 76, 8, 5, rot=40),
        B("scar", "hull", 64, 92, 3, 4)],
       [B("breach", "wing.L", 40, 76, 5, 4.5),
        B("bare", "halo.R", 84, 42, 2.5, 6),
        B("scar", "tail.R", 80, 104, 3, 3)],
       "stealth coat burnt off the right wing down to its frame",
       "left wing blown through, right halo stripped bare, ash on the tail")

design("CrimsonHalo", "Sodium", "heat",
       [F("slag", "hull", blob(64, 82, 5, 9)),
        B("blisters", "wing.L", 48, 62, 5, 4, rot=-40, n=5)],
       [B("breach", "wing.R", 98, 84, 5, 4),
        F("slag", "tail.L", blob(48, 104, 4, 4)),
        B("blisters", "halo.L", 44, 40, 2.5, 6, n=3)],
       "overheated: slag running down the spine, left wing root blistering",
       "right wing melted through, left tail slagged, left halo blistered")

design("CrimsonHalo", "Bone", "bone",
       [B("pocks", "hull", 63, 96, 5, 9, n=4),
        B("pocks", "tail.L", 47, 104, 5, 4, n=2)],
       [B("pocks", "wing.R", 98, 80, 8, 5, rot=40, n=5),
        B("breach", "wing.L", 28, 84, 5, 4),
        ("crack", "halo.R", [(83, 30), (87, 36), (84, 42)])],
       "shot up: bullet holes stitched up the spine and left tail",
       "right wing stitched, left wing blown through, right halo cracked")

design("CrimsonHalo", "Seraph", "red",
       [B("bare", "halo.L", 44, 40, 2.6, 7),
        B("bare", "wing.L", 44, 62, 5, 3.5, rot=-40)],
       [B("bare", "halo.R", 84, 40, 2.6, 7),
        B("breach", "wing.R", 92, 78, 5, 4),
        B("bare", "hull", 64, 98, 3, 5)],
       "feathers shed: plating gone from the left halo and wing root, bare metal",
       "right halo stripped too, right wing blown through, spine bare")

# ---------------------------------------------------------- IonLancer --
# stock: left wing panel; critical right canard bent, right wingtip torn, right wing arcing
design("IonLancer", "Night", "night",
       [B("plates", "wing.R", 80, 72, 7, 5, rot=-45),
        B("scar", "hull", 64, 80, 3, 4)],
       [B("breach", "wing.L", 44, 78, 5, 4),
        B("bare", "wing.R", 102, 98, 4, 3),
        B("scar", "canard.L", 52, 39, 2.4, 2)],
       "stealth coat burnt off the right wing root down to its frame",
       "left wing blown through, right wingtip stripped bare, ash on the canard")

design("IonLancer", "Sodium", "heat",
       [F("slag", "hull", blob(64, 84, 5, 8)),
        B("blisters", "wing.L", 46, 72, 5, 4, rot=-45, n=5)],
       [B("breach", "wing.R", 100, 96, 4.5, 3.5),
        F("slag", "pod.R", blob(74, 96, 3, 5)),
        B("blisters", "wing.R", 84, 74, 5, 4, rot=45, n=4)],
       "overheated: slag running down the spine, left wing root blistering",
       "right wingtip melted through, right pod slagged, right root blistering")

design("IonLancer", "Bone", "bone",
       [B("pocks", "wing.R", 100, 94, 8, 5, rot=-30, n=5),
        ("crack", "wing.R", [(86, 80), (92, 84), (90, 90)])],
       [B("pocks", "wing.L", 38, 80, 8, 5, rot=45, n=5),
        B("breach", "hull", 64, 70, 3.5, 6),
        B("pocks", "canard.R", 76, 38, 3, 2, n=1)],
       "shot up: a burst of bullet holes across the right wingtip",
       "left wing stitched, the spine blown through, right canard holed")

design("IonLancer", "Lance", "cobalt",
       [F("slash", "hull", [(64, 61), (66.8, 72), (64, 85), (61.2, 72)]),
        F("gouge", "wing.L", [(48, 66), (40, 75), (33, 83)], n=2)],
       [F("slash", "wing.R", [(78, 62), (86, 66), (100, 92), (94, 90)]),
        B("breach", "wing.L", 42, 84, 4.5, 4),
        F("slash", "canard.R", [(70, 32), (78, 38), (79, 41), (70, 37)])],
       "lanced: a spear-thrust split down the spine, the left wing raked",
       "a second thrust through the right wing, left wing holed, right canard split")
