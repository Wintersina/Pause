#!/usr/bin/env python3
"""Parametric SVG source for every ship's ultimate weapon.

Style: flat 2D cel animation in an 80s anime (Akira, 1988) palette -- deep
indigo night, Kaneda red as the hero colour, sodium orange and amber, teal /
cyan energy accents and only a little magenta. Shapes are flat with thick ink
outlines, one hard shadow tone and one highlight tone, no gradients, no glossy
orbs. Only light sources get a bloom, and that bloom is hard-edged (stepped
flat halos), never a blur.

Each roster ship has one weapon family: a charge indicator that sits in front
of the hull, the homing shot it releases, the muzzle flash, the trail puff
and the impact burst. Every frame is an SVG produced by a function of the ship
and a progress value. All colours come from PALETTES below, so a restyle is an
edit to that table plus a re-render.

    python3 weapons.py                 write ../<Ship>/src~/*.svg, render them
                                       with resvg, pack ../../Resources/Weapons/<Ship>.png
    python3 weapons.py --preview DIR   also write one preview strip per ship
    python3 weapons.py --only A,B      just those ships (by key)

Atlas layout (128px cells, 16 x 3, row 0 at the top):
    row 0  charge 0..15            (charge = i / 15)
    row 1  ready 0..3 | shot 0..5 | impact 0..5
           shot 0..3 is the flight loop, shot 4..5 are smear frames
    row 2  muzzle 0..3 | release 0..3 | trail 0..1
WeaponArt.cs slices the same layout; keep the two in step.
"""
import math
import os
import random
import shutil
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.normpath(os.path.join(HERE, ".."))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Resources", "Weapons"))
CELL = 128
COLS, ROWS = 16, 3
CHARGE_FRAMES, READY_FRAMES, SHOT_FRAMES, IMPACT_FRAMES = 16, 4, 6, 6
MUZZLE_FRAMES, RELEASE_FRAMES, TRAIL_FRAMES = 4, 4, 2

# Hold of each drawing in ticks at 24 fps (docs/art-style.md section 3).
# WeaponArt.cs / FlipbookFx.cs play them back with the same tables.
READY_TICKS = [4, 2, 2, 2]          # held key pose, then the pulse on 2s
SHOT_TICKS = [2, 2, 2, 2, 1, 1]     # flight loop on 2s; smear frames 1 tick
IMPACT_TICKS = [1, 1, 2, 2, 2, 3]
MUZZLE_TICKS = [1, 1, 1, 2]         # pinch, flash, smear, speed lines
RELEASE_TICKS = [2, 1, 1, 3]        # squash, stretch, flash, speed lines
EXPLOSION_TICKS = [1, 1, 1, 3, 2, 2, 2, 2, 3, 3]

# ---------------------------------------------------------------- palette ----
#
# One row per ship. main/shade/hi are the weapon body's three cel tones,
# energy/energy_hi the light it throws (always the bloom colour), spot a rare
# accent. ink and night are shared.
# Names and values follow docs/art-style.md section 1 (art-samples/src/akira.py).
INK = "#140C14"        # every outline: warm near-black, never #000
NIGHT_1 = "#0E1424"    # default backdrop (preview strips)
NIGHT = "#1A1F45"      # INDIGO_0: unlit / empty slots
NIGHT_HI = "#2A2E6B"   # INDIGO_1: empty-slot outlines
BONE = "#F4EAD4"       # highlight kicks and light cores; never #FFF ...
WHITE = BONE
PURE = "#FFFFFF"       # ... except the one-tick impact flash
SMOKE, SMOKE_SHADE = "#3A2A5C", "#1A1F45"   # DUSK / INDIGO_0 smoke cels
GUN, GUN_SH, GUN_HI = "#2C2D40", "#1A1A28", "#5A5C78"

KANEDA, KANEDA_SHADE, KANEDA_HI = "#D8232C", "#86121F", "#FF5B45"   # RED, RED_SH, RED_HI
SODIUM, SODIUM_SHADE, SODIUM_HI = "#F2862B", "#A9481A", "#FFB43C"   # SODIUM, SODIUM_SH, AMBER
AMBER, AMBER_SHADE, AMBER_HI = "#FFB43C", "#A9481A", BONE
CYAN, CYAN_HI = "#6EF2EE", BONE
TEAL, TEAL_HI, TEAL_SH = "#1FB5B9", "#6EF2EE", "#0F5E6A"
MAGENTA = "#FF2E88"    # enemy lights only: never on player weapons

PALETTES = {
    #  ship: (main, shade, hi, energy, energy_hi, spot)
    1:  (KANEDA, KANEDA_SHADE, KANEDA_HI, CYAN, CYAN_HI, AMBER),          # Neon Comet
    2:  (SODIUM, SODIUM_SHADE, SODIUM_HI, TEAL, TEAL_HI, KANEDA),         # Volt Viper
    3:  (AMBER, AMBER_SHADE, AMBER_HI, SODIUM, AMBER_HI, KANEDA),         # Solar Fang
    4:  (KANEDA, KANEDA_SHADE, KANEDA_HI, AMBER, BONE, TEAL),             # Crimson Halo
    5:  (KANEDA, KANEDA_SHADE, KANEDA_HI, CYAN, CYAN_HI, AMBER),          # Ion Lancer
    6:  (SODIUM, SODIUM_SHADE, SODIUM_HI, TEAL, TEAL_HI, KANEDA),         # Jade Phantom
    7:  (AMBER, AMBER_SHADE, AMBER_HI, KANEDA, KANEDA_HI, CYAN),          # Gold Warden
    8:  (SODIUM, SODIUM_SHADE, SODIUM_HI, CYAN, CYAN_HI, AMBER),          # Lightning
    9:  (KANEDA, KANEDA_SHADE, SODIUM, AMBER, AMBER_HI, CYAN),            # Ligher
    10: (KANEDA, KANEDA_SHADE, KANEDA_HI, TEAL, TEAL_HI, KANEDA),         # Paranoid
    11: (BONE, GUN_HI, BONE, KANEDA, KANEDA_HI, CYAN),                    # Ninja
    12: (KANEDA, KANEDA_SHADE, KANEDA_HI, SODIUM, AMBER_HI, CYAN),        # Saboteur
    13: (SODIUM, SODIUM_SHADE, SODIUM_HI, CYAN, CYAN_HI, KANEDA),         # UFO
    14: (BONE, AMBER, BONE, SODIUM, AMBER, CYAN),                         # Dove
    15: (AMBER, AMBER_SHADE, AMBER_HI, TEAL, TEAL_HI, KANEDA),            # Turtle
}

# ---------------------------------------------------------------- helpers ----

def rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def hx(c):
    return "#%02x%02x%02x" % tuple(max(0, min(255, int(round(v)))) for v in c)


def mix(a, b, t):
    a, b = rgb(a), rgb(b)
    t = max(0.0, min(1.0, t))
    return hx(tuple(a[i] + (b[i] - a[i]) * t for i in range(3)))


def step(a, b, t, steps=3):
    """Posterised mix: cel colours change in hard steps, never smoothly."""
    return mix(a, b, math.floor(max(0.0, min(1.0, t)) * steps + 1e-6) / steps)


def clamp01(v):
    return max(0.0, min(1.0, v))


def ease(t):
    t = clamp01(t)
    return 1 - (1 - t) ** 3


def smooth(t):
    t = clamp01(t)
    return t * t * (3 - 2 * t)


def P(cx, cy, r, deg):
    """Point at `deg` clockwise from straight up."""
    a = math.radians(deg)
    return (cx + r * math.sin(a), cy - r * math.cos(a))


def n(v):
    return ("%.2f" % v).rstrip("0").rstrip(".")


def pts(lst):
    return " ".join("%s,%s" % (n(x), n(y)) for x, y in lst)


def arc(cx, cy, r, a0, a1):
    x0, y0 = P(cx, cy, r, a0)
    x1, y1 = P(cx, cy, r, a1)
    large = 1 if (a1 - a0) % 360 > 180 else 0
    return "M%s %s A%s %s 0 %d 1 %s %s" % (n(x0), n(y0), n(r), n(r), large, n(x1), n(y1))


def band(cx, cy, r0, r1, a0, a1, steps=None):
    """A flat ring segment as a polygon (angular, so it can be ink-outlined)."""
    steps = steps or max(2, int(abs(a1 - a0) / 9))
    outer = [P(cx, cy, r1, a0 + (a1 - a0) * i / steps) for i in range(steps + 1)]
    inner = [P(cx, cy, r0, a1 - (a1 - a0) * i / steps) for i in range(steps + 1)]
    return outer + inner


def star(cx, cy, r_out, r_in, points, rot=0.0, sx=1.0, sy=1.0):
    out = []
    for i in range(points * 2):
        r = r_out if i % 2 == 0 else r_in
        x, y = P(0, 0, r, rot + i * 180.0 / points)
        out.append((cx + x * sx, cy + y * sy))
    return out


def burst(rng, cx, cy, r_out, r_in, points, rot=0.0, jitter=0.25, sx=1.0, sy=1.0):
    """An irregular cartoon explosion star."""
    out = []
    for i in range(points * 2):
        base = r_out if i % 2 == 0 else r_in
        r = base * (1 + rng.uniform(-jitter, jitter))
        x, y = P(0, 0, r, rot + i * 180.0 / points + rng.uniform(-6, 6))
        out.append((cx + x * sx, cy + y * sy))
    return out


def hexagon(cx, cy, r, rot=30.0):
    return [P(cx, cy, r, rot + 60 * i) for i in range(6)]


def ngon(cx, cy, r, sides, rot=0.0, sx=1.0, sy=1.0):
    out = []
    for i in range(sides):
        x, y = P(0, 0, r, rot + i * 360.0 / sides)
        out.append((cx + x * sx, cy + y * sy))
    return out


def scale_pts(points, cx, cy, sx, sy=None):
    sy = sx if sy is None else sy
    return [(cx + (x - cx) * sx, cy + (y - cy) * sy) for x, y in points]


def jag(rng, x0, y0, x1, y1, steps, amp):
    out = [(x0, y0)]
    dx, dy = x1 - x0, y1 - y0
    length = math.hypot(dx, dy) or 1
    nx, ny = -dy / length, dx / length
    for i in range(1, steps):
        t = i / steps
        o = rng.uniform(-amp, amp)
        out.append((x0 + dx * t + nx * o, y0 + dy * t + ny * o))
    out.append((x1, y1))
    return out


def centroid(points):
    return (sum(p[0] for p in points) / len(points), sum(p[1] for p in points) / len(points))


class Doc:
    """Collects defs (clip paths) so every helper can mint unique ids."""

    def __init__(self):
        self.defs = []
        self.count = 0

    def uid(self, prefix):
        self.count += 1
        return "%s%d" % (prefix, self.count)


GLOW_FILTER = ('<filter id="glow" filterUnits="userSpaceOnUse" x="-32" y="-32" width="192" height="192">'
               '<feGaussianBlur stdDeviation="3.2"/></filter>')


def svg(doc, body, hold=None):
    head = "<!-- hold: %d ticks @24fps -->" % hold if hold else ""
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128" width="128" height="128">%s'
            '<defs>%s%s</defs>%s</svg>\n' % (head, GLOW_FILTER, "".join(doc.defs), body))


def g(body, op=None, tf=None, clip=None):
    attrs = ""
    if op is not None:
        attrs += ' opacity="%s"' % n(op)
    if tf:
        attrs += ' transform="%s"' % tf
    if clip:
        attrs += ' clip-path="url(#%s)"' % clip
    return "<g%s>%s</g>" % (attrs, body)


def poly(points, fill="none", stroke=None, w=0, op=None, join="round"):
    s = '<polygon points="%s" fill="%s"' % (pts(points), fill)
    if stroke:
        s += ' stroke="%s" stroke-width="%s" stroke-linejoin="%s"' % (stroke, n(w), join)
    if op is not None:
        s += ' opacity="%s"' % n(op)
    return s + "/>"


def line(points, stroke, w, op=None, cap="round"):
    s = '<polyline points="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="%s" stroke-linejoin="round"' % (
        pts(points), stroke, n(w), cap)
    if op is not None:
        s += ' opacity="%s"' % n(op)
    return s + "/>"


def circle(cx, cy, r, fill="none", stroke=None, w=0, op=None):
    s = '<circle cx="%s" cy="%s" r="%s" fill="%s"' % (n(cx), n(cy), n(max(r, 0.01)), fill)
    if stroke:
        s += ' stroke="%s" stroke-width="%s"' % (stroke, n(w))
    if op is not None:
        s += ' opacity="%s"' % n(op)
    return s + "/>"


def path(d, fill="none", stroke=None, w=0, op=None, cap="round"):
    s = '<path d="%s" fill="%s"' % (d, fill)
    if stroke:
        s += ' stroke="%s" stroke-width="%s" stroke-linecap="%s" stroke-linejoin="round"' % (stroke, n(w), cap)
    if op is not None:
        s += ' opacity="%s"' % n(op)
    return s + "/>"


INK_W = 5.0


def cel(doc, points, base, shade=None, hi=None, ink=INK_W, light=-35.0, split=0.18):
    """Flat cel shape: thick ink outline, base fill, one hard shadow on the
    side away from the light (top-left), one highlight sliver toward it."""
    out = poly(points, fill=INK, stroke=INK, w=ink, join="round") if ink else ""
    out += poly(points, fill=base)
    cx, cy = centroid(points)
    span = max(math.hypot(x - cx, y - cy) for x, y in points) or 1
    if shade:
        cid = doc.uid("c")
        # half plane beyond a line through (slightly past) the centroid,
        # perpendicular to the light direction
        lx, ly = P(0, 0, 1, light)
        ox, oy = cx - lx * span * split, cy - ly * span * split
        px, py = -ly, lx
        big = span * 4
        half = [(ox + px * big, oy + py * big), (ox - px * big, oy - py * big),
                (ox - px * big - lx * big, oy - py * big - ly * big),
                (ox + px * big - lx * big, oy + py * big - ly * big)]
        doc.defs.append('<clipPath id="%s"><polygon points="%s"/></clipPath>' % (cid, pts(half)))
        out += g(poly(points, fill=shade), clip=cid)
    if hi:
        hp = scale_pts(points, cx, cy, 0.42)
        lx, ly = P(0, 0, span * 0.32, light)
        hp = [(x + lx, y + ly) for x, y in hp]
        cid = doc.uid("h")
        doc.defs.append('<clipPath id="%s"><polygon points="%s"/></clipPath>' % (cid, pts(points)))
        out += g(poly(hp, fill=hi), clip=cid)
    return out


def inked_line(points, color, w, core=None, ink=INK_W):
    s = line(points, INK, w + ink)
    s += line(points, color, w)
    if core:
        s += line(points, core, max(1.0, w * 0.38))
    return s


def light(points, color, bloom, core=WHITE, rings=2, grow=0.32):
    """A light source (docs/art-style.md 2.7): a blurred copy of its shape
    behind it, the flat light colour, a BONE core. Lights carry no ink.
    rings scales the glow (0 = none)."""
    cx, cy = centroid(points)
    s = ""
    if rings > 0:
        s += g(poly(scale_pts(points, cx, cy, 1 + grow * rings), fill=bloom),
               op=0.35 + 0.12 * min(rings, 2)).replace("<g ", '<g filter="url(#glow)" ', 1)
    s += poly(points, fill=color)
    s += poly(scale_pts(points, cx, cy, 0.5), fill=core)
    return s


def light_circle(cx, cy, r, color, bloom, core=WHITE, rings=2):
    s = ""
    for i in range(rings, 0, -1):
        s += circle(cx, cy, r * (1 + 0.45 * i), fill=bloom, op=0.16 + 0.14 * (rings - i))
    s += circle(cx, cy, r, fill=color, stroke=INK, w=3)
    s += circle(cx - r * 0.18, cy - r * 0.18, r * 0.45, fill=core)
    return s


def speed_lines(rng, cx, cy, r0, r1, count, color, w=3, a0=0, a1=360):
    s = ""
    for i in range(count):
        ang = a0 + (a1 - a0) * (i + rng.uniform(0.1, 0.9)) / count
        ra = r0 + rng.uniform(0, 6)
        rb = r1 - rng.uniform(0, 10)
        s += line([P(cx, cy, ra, ang), P(cx, cy, rb, ang)], color, w, cap="butt")
    return s


def puff(doc, x, y, r, rng, col=SMOKE, shade=SMOKE_SHADE, rim=None):
    """Ink-outlined smoke cel: an angular, chamfered lump (docs/art-style.md:
    smoke as angular DUSK / INDIGO cels, never round puffs), flat two-tone
    with a hard shadow on the lower right."""
    sides = 7
    rot = rng.uniform(0, 360)
    pts_ = [P(x, y, r * rng.uniform(0.72, 1.0), rot + i * 360.0 / sides) for i in range(sides)]
    s = poly(pts_, fill=col, stroke=INK, w=3.2)
    sh = [(x, y)] + [p_ for p_ in pts_ if (p_[0] - x) + (p_[1] - y) > 0]
    if len(sh) >= 3:
        sh = sorted(sh[1:], key=lambda q: math.atan2(q[1] - y, q[0] - x))
        s += poly([(x + r * 0.1, y + r * 0.1)] + sh, fill=shade)
    s += poly(pts_, stroke=INK, w=3.2)
    if rim:  # fire-lit kick on the upper-left edges so smoke reads on the night sky
        lit = [p_ for p_ in pts_ if (p_[0] - x) + (p_[1] - y) < -r * 0.3]
        if len(lit) >= 2:
            lit = sorted(lit, key=lambda q: math.atan2(q[1] - y, q[0] - x))
            s += line(scale_pts(lit, x, y, 0.82), rim, 2.4)
    return s


# ------------------------------------------------------------ ship family ----

class Weapon:
    """One ship's weapon family. Subclasses draw charge/shot/motif; the base
    supplies the shared cartoon muzzle, release and impact timing."""
    key = ""
    flash_points = 8

    def __init__(self, ship):
        self.ship = ship
        (self.main, self.shade, self.hi, self.energy, self.energy_hi, self.spot) = PALETTES[ship]

    def rng(self, kind, frame):
        return random.Random(self.ship * 10007 + kind * 101 + frame)

    def charge(self, doc, c, ready, k):
        raise NotImplementedError

    def shot(self, doc, k):
        raise NotImplementedError

    def smear(self, doc, k):
        """Fast-shot smear: the flight pose stretched along its heading with
        speed lines behind it."""
        rng = self.rng(3, k)
        inner = self.shot(doc, k % 4)
        sy = 1.35 + 0.2 * k
        body = ""
        for i in range(5):
            x = 64 + (i - 2) * 7 + rng.uniform(-2, 2)
            y0 = 70 + rng.uniform(0, 14)
            body += line([(x, y0), (x, y0 + 30 + rng.uniform(0, 20))], self.energy, 3, cap="butt")
            body += line([(x, y0 + 4), (x, y0 + 16)], WHITE, 1.6, cap="butt")
        body += g(inner, tf="translate(64 40) scale(%s %s) translate(-64 -40)" % (n(0.8), n(sy)))
        return body

    # A small emblem of the weapon used as trail puffs and impact debris.
    def motif(self, doc, x, y, s, rot, k):
        return cel(doc, star(x, y, 8 * s, 3 * s, 4, rot), self.energy, None, None, ink=3)

    def muzzle(self, doc, k):
        rng = self.rng(4, k)
        if k == 0:   # anticipation: a tight bright pinch
            return light(star(64, 70, 14, 5, 4, 45), self.energy, self.energy_hi, rings=1)
        if k == 1:   # key flash: big flat star
            s = poly(star(64, 64, 58, 20, self.flash_points, 0), fill=self.main, stroke=INK, w=4)
            s += poly(star(64, 64, 40, 14, self.flash_points, 180.0 / self.flash_points), fill=self.hi)
            s += poly(star(64, 64, 22, 10, self.flash_points, 0), fill=WHITE)
            return s
        if k == 2:   # smear forward
            s = poly(star(64, 50, 54, 14, self.flash_points, 0, sx=0.55, sy=1.0), fill=self.energy, stroke=INK, w=4)
            s += poly(star(64, 50, 30, 8, self.flash_points, 0, sx=0.45, sy=1.0), fill=WHITE)
            s += speed_lines(rng, 64, 64, 30, 60, 10, self.energy_hi, 3)
            return s
        # tail: speed lines only
        return speed_lines(rng, 64, 64, 40, 62, 12, self.energy_hi, 2.4) + \
            poly(star(64, 64, 10, 4, 4, 0), fill=WHITE, op=0.8)

    def release(self, doc, k):
        """The indicator letting go: squash, stretch, flash, speed lines."""
        inner = self.charge(doc, 1.0, True, k)
        if k == 0:
            return g(inner, tf="translate(64 64) scale(1.22 0.78) translate(-64 -64)")
        if k == 1:
            return g(inner, tf="translate(64 64) scale(0.78 1.3) translate(-64 -64)") + \
                poly(star(64, 40, 30, 8, 4, 0, sx=0.6), fill=WHITE, stroke=INK, w=3)
        rng = self.rng(5, k)
        if k == 2:
            s = poly(star(64, 64, 58, 22, self.flash_points, 0), fill=self.main, stroke=INK, w=4)
            s += poly(star(64, 64, 36, 14, self.flash_points, 0), fill=WHITE)
            return s
        return speed_lines(rng, 64, 64, 34, 62, 14, self.energy, 3.2) + \
            poly(ngon(64, 64, 30, 8), stroke=self.energy_hi, w=3, op=0.8)

    def impact(self, doc, k):
        rng = self.rng(7, 0)
        if k == 0:   # 1-tick impact frame: pure white, red outline
            return poly(star(64, 64, 60, 24, self.flash_points + 2, 8), fill=PURE, stroke=KANEDA, w=4)
        if k == 1:   # red/ink key burst
            s = poly(burst(rng, 64, 64, 60, 28, 9, 0), fill=KANEDA, stroke=INK, w=5)
            s += poly(burst(rng, 64, 64, 40, 18, 9, 20), fill=self.hi)
            s += poly(burst(rng, 64, 64, 20, 10, 7, 0), fill=WHITE)
            return s
        p = (k - 2) / 3.0  # 0 .. 1 over the remaining frames
        e = ease(p)
        s = ""
        prng = self.rng(8, 0)
        for i in range(5):
            ang = i * 72 + prng.uniform(-15, 15)
            x, y = P(64, 64, 14 + 26 * e, ang)
            s += puff(doc, x, y, (13 - 2 * i % 3) * (1 - 0.45 * p), random.Random(i * 31 + k))
        if p < 0.67:
            s += poly(burst(rng, 64, 64, 40 * (1 - p), 18 * (1 - p), 8, 10 + k * 4),
                      fill=self.main, stroke=INK, w=4)
            s += poly(burst(rng, 64, 64, 20 * (1 - p), 9 * (1 - p), 6, 0), fill=WHITE)
        drng = self.rng(9, 0)
        for i in range(6):
            ang = i * 60 + drng.uniform(-18, 18)
            x, y = P(64, 64, 24 + 34 * e, ang)
            s += self.motif(doc, x, y, 1.0 - 0.4 * p, ang + k * 30, k)
        return s

    def trail(self, doc, k):
        return self.motif(doc, 64, 64, [2.0, 1.4][k], k * 30, k)


# 1 -- Neon Comet: a light-bike streak racing round an octagonal track --------
class NeonComet(Weapon):
    key = "NeonComet"
    flash_points = 4

    def charge(self, doc, c, ready, k):
        s = poly(ngon(64, 64, 50, 8, 22.5), fill=NIGHT, stroke=INK, w=5)
        s += poly(ngon(64, 64, 34, 8, 22.5), fill=INK)
        for q in range(8):  # track lane marks
            s += line([P(64, 64, 38, q * 45), P(64, 64, 46, q * 45)], NIGHT_HI, 3, cap="butt")
        if c > 0.004:
            a1 = 360 * c
            s += poly(band(64, 64, 37, 47, 0, a1), fill=self.main, stroke=INK, w=3)
            s += poly(band(64, 64, 40, 44, max(0, a1 - 90), a1), fill=self.hi)
            hx_, hy = P(64, 64, 42, a1)
            s += light(star(hx_, hy, 10, 5, 4, a1), self.energy, self.energy_hi)
        sz = 7 + 13 * c
        s += cel(doc, star(64, 64, sz, sz * 0.35, 4, 0), step(self.shade, self.energy, c), None, None, ink=3.5)
        if ready:
            rng = self.rng(1, k)
            s += speed_lines(rng, 64, 64, 52, 62, 8, self.energy_hi, 3, a0=k * 11)
            s += light(star(64, 64, 22 + 4 * (k % 2), 6, 4, 45 * (k % 2)), self.energy, self.energy_hi)
        return s

    def shot(self, doc, k):
        wob = [0, 2, 0, -2][k % 4]
        # the long tail-light streak: flat bands, red outside, white core
        s = poly([(52, 44), (64 + wob, 124), (76, 44)], fill=self.main, stroke=INK, w=4)
        s += poly([(57, 44), (64 + wob * 0.6, 104), (71, 44)], fill=self.hi)
        s += poly([(61, 44), (64 + wob * 0.3, 84), (67, 44)], fill=WHITE)
        s += light(star(64, 38, 18 + (k % 2) * 3, 7, 4, 0), self.energy, self.energy_hi)
        return s

    def motif(self, doc, x, y, s, rot, k):
        return cel(doc, star(x, y, 8 * s, 2.4 * s, 4, 0), self.energy_hi if int(rot) % 2 else self.energy,
                   None, None, ink=3)


# 2 -- Volt Viper: a segmented mechanical snake coiling up with charge ---------
class VoltViper(Weapon):
    key = "VoltViper"
    flash_points = 6

    def spiral(self, steps=22):
        return [(P(64, 66, 10 + 32 * i / steps, 200 + 560 * i / steps), i / steps) for i in range(steps + 1)]

    def charge(self, doc, c, ready, k):
        sp = self.spiral()
        segs = len(sp) - 1
        lit = c * segs
        s = ""
        for i in range(segs):
            (p0, u0), (p1, u1) = sp[i], sp[i + 1]
            w = 4 + 6 * u1
            f = clamp01(lit - i)
            col = self.main if f >= 1 else (self.shade if f > 0 else NIGHT_HI)
            s += line([p0, p1], INK, w + 5, cap="butt")
            s += line([p0, p1], col, w, cap="butt")
            if f >= 1 and i % 2 == 0:
                s += line([p0, p1], self.energy, 2, cap="butt")
        (x0, y0), _ = sp[-2]
        (x1, y1), _ = sp[-1]
        ang = math.degrees(math.atan2(y1 - y0, x1 - x0))
        lit_head = clamp01(lit - (segs - 1))
        hc = self.main if lit_head > 0 else NIGHT_HI
        jaw = 6 + 3 * (k % 2) if ready else 0
        head = [(0, -9), (16, -7), (24, -jaw), (14, 0), (24, jaw), (16, 7), (0, 9), (-4, 0)] if ready else \
            [(0, -9), (16, -7), (24, 0), (16, 7), (0, 9), (-4, 0)]
        hs = cel(doc, head, hc, self.shade if lit_head > 0 else None, None, ink=4)
        eye = self.energy if lit_head > 0 else NIGHT
        hs += poly([(8, -6), (14, -4), (9, -2)], fill=eye) + poly([(8, 6), (14, 4), (9, 2)], fill=eye)
        if ready:
            hs += inked_line([(16, 0), (32, 0), (37, -5)], self.energy, 2, ink=3)
            hs += inked_line([(32, 0), (37, 5)], self.energy, 2, ink=3)
        s += g(hs, tf="translate(%s %s) rotate(%s)" % (n(x1), n(y1), n(ang)))
        if ready:
            rng = self.rng(1, k)
            for j in range(3):
                a0 = rng.uniform(0, 360)
                p0 = P(64, 66, rng.uniform(12, 26), a0)
                p1 = P(64, 66, rng.uniform(46, 56), a0 + rng.uniform(-30, 30))
                s += inked_line(jag(rng, p0[0], p0[1], p1[0], p1[1], 4, 5), self.energy, 2.6, WHITE, ink=3)
        return s

    def shot(self, doc, k):
        rng = self.rng(2, k)
        body = []
        for i in range(9):
            t = i / 8
            y = 116 - 86 * t
            amp = 13 * (1 - t) + 1.5
            body.append((64 + math.sin(t * 8 + k * 1.57) * amp, y))
        s = inked_line(body, self.main, 7, None)
        s += line(body, self.energy, 2.4)
        for i in range(2):
            j = rng.randint(1, 6)
            x, y = body[j]
            s += inked_line(jag(rng, x, y, x + rng.choice([-1, 1]) * rng.uniform(10, 16), y + rng.uniform(0, 8), 3, 3),
                            self.energy, 2, WHITE, ink=3)
        hx_, hy = body[-1]
        head = [(hx_, hy - 14), (hx_ + 8, hy - 2), (hx_ + 5, hy + 6), (hx_ - 5, hy + 6), (hx_ - 8, hy - 2)]
        s += cel(doc, head, self.main, self.shade, self.hi, ink=4)
        s += poly([(hx_ - 4, hy - 3), (hx_ - 1.5, hy - 1), (hx_ - 4, hy + 1)], fill=self.energy)
        s += poly([(hx_ + 4, hy - 3), (hx_ + 1.5, hy - 1), (hx_ + 4, hy + 1)], fill=self.energy)
        return s

    def motif(self, doc, x, y, s, rot, k):
        rng = random.Random(int(x * 13 + y * 7 + k))
        p1 = P(x, y, 10 * s, rot)
        p0 = P(x, y, -6 * s, rot)
        return inked_line(jag(rng, p0[0], p0[1], p1[0], p1[1], 3, 3 * s), self.energy, 2.4 * s, WHITE, ink=3)


# 3 -- Solar Fang: a jagged sun core whose fangs ignite one by one -------------
class SolarFang(Weapon):
    key = "SolarFang"
    flash_points = 12

    def charge(self, doc, c, ready, k):
        rays = 12
        lit = c * rays
        s = ""
        base_r = 22 + 3 * c
        for i in range(rays):
            f = clamp01(lit - i)
            ang = i * 30 + (k * 5 if ready else 0)
            ln = 10 if f <= 0 else 12 + 16 * f + (8 if ready and (i + k) % 2 == 0 else 0)
            fang = [P(64, 64, base_r - 3, ang - 11), P(64, 64, base_r + ln, ang + 4), P(64, 64, base_r - 3, ang + 9)]
            if f <= 0:
                s += poly(fang, fill=NIGHT_HI, stroke=INK, w=3.5)
            else:
                s += cel(doc, fang, self.main if f >= 1 else self.shade, self.shade if f >= 1 else None, None, ink=3.5)
        core_r = 16 + 6 * c + (2 if ready and k % 2 else 0)
        core = burst(random.Random(3), 64, 64, core_r, core_r * 0.82, 10, k * 9)
        if c >= 0.99:
            s += light(core, self.energy, self.energy_hi, core=WHITE)
        else:
            s += cel(doc, core, step(self.spot, self.main, c), self.shade, self.hi if c > 0.5 else None, ink=4)
        if ready:
            s += poly(ngon(64, 64, 58, 12, k * 5), stroke=self.hi, w=2.4, op=0.85)
        return s

    def shot(self, doc, k):
        rng = self.rng(2, k)
        flame = [(48, 54), (54, 80), (58, 72), (64, 120 + rng.uniform(-6, 4)), (70, 72), (74, 82), (80, 54)]
        s = cel(doc, flame, self.spot, self.shade, None, ink=4)
        s += poly([(56, 56), (64, 98 + rng.uniform(-4, 4)), (72, 56)], fill=self.main)
        spikes = burst(random.Random(k), 64, 46, 27 + (k % 2) * 3, 15, 10, k * 9, jitter=0.12)
        s += cel(doc, spikes, self.main, self.shade, None, ink=4)
        s += light(ngon(64, 46, 12, 8, k * 9), self.energy, self.energy_hi)
        return s

    def motif(self, doc, x, y, s, rot, k):
        return cel(doc, [P(x, y, 10 * s, rot), P(x, y, 4 * s, rot + 120), P(x, y, 4 * s, rot - 120)],
                   self.main, self.shade, None, ink=3)


# 4 -- Crimson Halo: a rail capacitor ring, segment by segment -----------------
class CrimsonHalo(Weapon):
    key = "CrimsonHalo"
    flash_points = 8

    def charge(self, doc, c, ready, k):
        segs = 8
        lit = c * segs
        s = ""
        for i in range(segs):
            a0 = i * 45 + 5
            a1 = (i + 1) * 45 - 5
            s += poly(band(64, 64, 38, 52, a0, a1), fill=NIGHT, stroke=INK, w=4)
            f = clamp01(lit - i)
            if f > 0:
                col = WHITE if ready and (i + k) % 4 == 0 else self.main
                s += poly(band(64, 64, 40, 50, a0, a0 + (a1 - a0) * f), fill=col)
                s += poly(band(64, 64, 40, 43, a0, a0 + (a1 - a0) * f), fill=self.shade)
        rail = [(64, 30 - 4 * c), (72, 56), (72, 72), (64, 98 + 4 * c), (56, 72), (56, 56)]
        s += cel(doc, rail, step(NIGHT_HI, self.main, c), self.shade if c > 0 else None, self.hi if c > 0.66 else None)
        s += line([(64, 42), (64, 86)], self.energy, 3, op=1 if c > 0.5 else 0.4, cap="butt")
        if ready:
            for i in range(4):
                x, y = P(64, 64, 45, k * 22.5 + i * 90)
                s += light(star(x, y, 8, 3, 4, 45), self.energy, self.energy_hi, rings=1)
            s += poly(ngon(64, 64, 60, 16, k * 11), stroke=self.energy, w=2.4, op=0.8)
        return s

    def shot(self, doc, k):
        s = poly([(64, 124), (56, 60), (72, 60)], fill=self.main, op=0.55)  # tail-light streak
        s += poly([(64, 120), (60, 60), (68, 60)], fill=self.hi, op=0.8)
        s += cel(doc, [(64, 10), (74, 46), (72, 92), (64, 104), (56, 92), (54, 46)], self.main, self.shade, self.hi)
        s += line([(64, 24), (64, 88)], WHITE, 3, cap="butt")
        for i in range(2):
            t = ((k % 4) / 4.0 + i * 0.5) % 1.0
            y = 34 + 56 * t
            rx = 22 - 8 * t
            s += poly(ngon(64, y, rx, 10, 0, sy=0.3), stroke=INK, w=6)
            s += poly(ngon(64, y, rx, 10, 0, sy=0.3), stroke=self.energy, w=3)
        return s

    def motif(self, doc, x, y, s, rot, k):
        return poly(ngon(x, y, 8 * s, 6, rot), stroke=INK, w=5 * s) + poly(ngon(x, y, 8 * s, 6, rot),
                                                                         stroke=self.energy, w=2.4 * s)


# 5 -- Ion Lancer: beams converge through a lens until it focuses --------------
class IonLancer(Weapon):
    key = "IonLancer"
    flash_points = 4

    def charge(self, doc, c, ready, k):
        lens_y = 84
        fy = 22
        s = ""
        bw = 3 + 3 * c
        for o in (-26, 0, 26):
            x = 64 + o
            end_x = (x + o * 0.5) * (1 - c) + 64 * c
            col = self.energy if c > 0.33 else mix(NIGHT_HI, self.energy, 0.5)
            s += inked_line([(x, 122), (x, lens_y)], self.main, bw, None, ink=3)
            s += inked_line([(x, lens_y), (end_x, fy + (1 - c) * 6)], col, bw, WHITE if c > 0.66 else None, ink=3)
        lens = ngon(64, lens_y, 40, 6, 90, sy=0.3)
        s += cel(doc, lens, self.main, self.shade, self.hi, ink=4.5)
        s += line([(36, lens_y - 2), (60, lens_y - 6)], WHITE, 2.4)
        for i in range(5):
            x = 42 + i * 11
            on = c * 5 >= i + 0.5
            s += poly([(x - 3, lens_y + 14), (x + 3, lens_y + 14), (x, lens_y + 20)],
                      fill=self.energy if on else NIGHT, stroke=INK, w=2)
        if c > 0.05:
            r = 3 + 7 * c * c
            s += light(star(64, fy, r * 1.4, r * 0.6, 4, 45), self.energy, self.energy_hi, rings=1 + int(c * 1.5))
        if ready:
            r = 24 + 4 * (k % 2)
            s += poly(star(64, fy, r, 4, 4, 45 * (k // 2)), fill=WHITE, stroke=INK, w=3)
        return s

    def shot(self, doc, k):
        s = poly([(58, 124), (64, 10), (70, 124)], fill=self.energy, op=0.3)
        s += inked_line([(64, 14), (64, 120)], self.main, 7, None, ink=4)
        s += line([(64, 16), (64, 112)], self.energy, 3.2)
        s += line([(64, 16), (64, 90)], WHITE, 1.6)
        for i in range(3):
            y = 30 + ((k % 4) * 9 + i * 30) % 84
            s += poly([(56, y + 6), (64, y), (72, y + 6), (64, y + 3)], fill=self.energy, stroke=INK, w=2)
        s += light(star(64, 14, 13 + 3 * (k % 2), 4, 4, 0), self.energy, self.energy_hi, rings=1)
        return s

    def motif(self, doc, x, y, s, rot, k):
        a = P(x, y, 10 * s, rot)
        b = P(x, y, -10 * s, rot)
        return inked_line([a, b], self.energy, 3 * s, WHITE, ink=3)


# 6 -- Jade Phantom: a jagged cel energy core with crackling hard arcs --------
class JadePhantom(Weapon):
    key = "JadePhantom"
    flash_points = 7

    def charge(self, doc, c, ready, k):
        rng = self.rng(1, int(round(c * 15)) * 4 + k)
        s = ""
        for i in range(12):  # containment clamps light up around the core
            a0 = i * 30 + 6
            on = clamp01(c * 12 - i) >= 1
            s += poly(band(64, 64, 46, 56, a0, a0 + 18, 2), fill=self.main if on else NIGHT, stroke=INK, w=3.5)
            if on:
                s += poly(band(64, 64, 46, 49, a0, a0 + 18, 2), fill=self.shade)
        r = 12 + 20 * c + (3 if ready and k % 2 else 0)
        core = burst(random.Random(int(c * 15) + k * 3), 64, 64, r, r * 0.7, 7, k * 13, jitter=0.18)
        s += light(core, self.energy, self.energy_hi, core=WHITE if c > 0.5 else self.energy_hi,
                   rings=1 + int(c * 1.99))
        count = 1 + int(c * 3) + (3 if ready else 0)
        for i in range(count):
            ang = rng.uniform(0, 360)
            reach = r * 1.05 if not ready else rng.uniform(r + 8, 54)
            x1, y1 = P(64, 64, reach, ang)
            x0, y0 = P(64, 64, r * 0.3, ang)
            s += inked_line(jag(rng, x0, y0, x1, y1, 4, 4), self.energy_hi, 2.4, None, ink=3)
        return s

    def shot(self, doc, k):
        rng = self.rng(2, k)
        s = ""
        for i in range(3):
            sx = 64 + (i - 1) * 8
            q = [(sx + math.sin(k * 1.57 + j * 0.9 + i) * (2 + j * 1.5), 58 + j * 12) for j in range(6)]
            s += inked_line(q, self.energy if i != 1 else self.main, 4 - (i % 2), None, ink=3)
        core = burst(random.Random(k), 64, 44, 20, 14, 7, k * 13, jitter=0.15)
        s += light(core, self.energy, self.energy_hi)
        for i in range(2):
            x1, y1 = P(64, 44, 28, rng.uniform(0, 360))
            s += inked_line(jag(rng, 64, 44, x1, y1, 3, 3), self.energy_hi, 2, None, ink=3)
        return s

    def motif(self, doc, x, y, s, rot, k):
        return cel(doc, burst(random.Random(int(rot)), x, y, 8 * s, 5 * s, 5, rot, jitter=0.2),
                   self.energy, None, None, ink=3)


# 7 -- Gold Warden: panel-lined missile pods slide out one by one --------------
class GoldWarden(Weapon):
    key = "GoldWarden"
    flash_points = 10

    def missile(self, doc, x, top, ready, k, i):
        s = cel(doc, [(x, top), (x + 7, top + 12), (x + 7, top + 42), (x - 7, top + 42), (x - 7, top + 12)],
                self.main, self.shade, self.hi, ink=4)
        s += poly([(x, top), (x + 7, top + 12), (x - 7, top + 12)], fill=KANEDA, stroke=INK, w=2)
        s += line([(x - 7, top + 24), (x + 7, top + 24)], INK, 2, cap="butt")   # panel lines
        s += line([(x - 7, top + 32), (x + 7, top + 32)], INK, 2, cap="butt")
        s += cel(doc, [(x - 7, top + 34), (x - 12, top + 46), (x - 7, top + 44)], self.shade, None, None, ink=3)
        s += cel(doc, [(x + 7, top + 34), (x + 12, top + 46), (x + 7, top + 44)], self.shade, None, None, ink=3)
        return s

    def charge(self, doc, c, ready, k):
        xs = [32, 54, 74, 96]
        order = [1, 2, 0, 3]  # inner pair first, then the outer pair
        clip = doc.uid("rack")
        doc.defs.append('<clipPath id="%s"><rect x="0" y="0" width="128" height="89"/></clipPath>' % clip)
        missiles = ""
        lights = ""
        for slot, x in enumerate(xs):
            i = order.index(slot)
            f = smooth(clamp01(c * 4 - i))
            top = 92 - 60 * f
            if f > 0:
                missiles += self.missile(doc, x, top, ready, k, i)
        s = g(missiles, clip=clip)
        s += cel(doc, [(16, 88), (112, 88), (108, 110), (20, 110)], NIGHT_HI, NIGHT, None, ink=5)
        s += line([(40, 90), (40, 108)], INK, 2) + line([(64, 90), (64, 108)], INK, 2) + line([(88, 90), (88, 108)], INK, 2)
        for slot, x in enumerate(xs):
            i = order.index(slot)
            on = c * 4 >= i + 1
            blink = ready and (i + k) % 2 == 0
            if on:
                lights += light([(x - 5, 96), (x + 5, 96), (x + 5, 102), (x - 5, 102)],
                                WHITE if blink else self.energy, self.energy, core=WHITE, rings=1)
            else:
                lights += poly([(x - 5, 96), (x + 5, 96), (x + 5, 102), (x - 5, 102)], fill=INK)
        s += lights
        if ready:
            for j in range(2):
                y = 20 - j * 9 + (k % 2) * 3
                s += inked_line([(50, y + 9), (64, y), (78, y + 9)], self.energy if j == k % 2 else self.main, 3.4,
                                None, ink=3)
        return s

    def shot(self, doc, k):
        rng = self.rng(2, k)
        fl = 26 + rng.uniform(-4, 8)
        s = poly([(57, 86), (64, 86 + fl), (71, 86)], fill=self.energy, stroke=INK, w=3)
        s += poly([(60, 86), (64, 86 + fl * 0.6), (68, 86)], fill=WHITE)
        s += puff(doc, 64 + rng.uniform(-4, 4), 116, 7, rng) if k % 2 else ""
        s += g(self.missile(doc, 64, 30, False, k, 0), tf="translate(64 58) scale(1.3) translate(-64 -58)")
        return s

    def motif(self, doc, x, y, s, rot, k):
        return puff(doc, x, y, 6 * s, random.Random(int(rot) + k))


# 8 -- Lightning: rings drop onto a lightning rod --------------------------------
class Lightning(Weapon):
    key = "Lightning"
    flash_points = 6

    def charge(self, doc, c, ready, k):
        rng = self.rng(1, k)
        targets = [94, 70, 46]
        s = ""
        for i, ty in enumerate(targets):  # empty ring slots behind the rod
            s += poly(ngon(64, ty, 30 - 5 * i, 8, 22.5, sy=0.26), stroke=NIGHT_HI, w=3)
        rod = [(64, 10), (72, 58), (67, 114), (61, 114), (56, 58)]
        s += cel(doc, rod, step(NIGHT_HI, self.main, c), self.shade, self.hi if c > 0.6 else None)
        for i, ty in enumerate(targets):
            f = smooth(clamp01(c * 3 - i))
            if f > 0:
                y = ty - 30 * (1 - f)
                ring = ngon(64, y, 30 - 5 * i, 8, 22.5, sy=0.26)
                s += poly(ring, stroke=INK, w=9)
                s += poly(ring, stroke=self.energy if f >= 1 else self.shade, w=4.5)
        if ready:
            for i in range(2):
                side = 1 if (i + k) % 2 else -1
                x0 = 64 + side * (30 - 5 * i)
                x1 = 64 + side * (30 - 5 * (i + 1))
                s += inked_line(jag(rng, x0, targets[i], x1, targets[i + 1], 4, 6), self.energy, 2.6, WHITE, ink=3)
            s += light(star(64, 12, 15 + 3 * (k % 2), 4, 4, 0), self.energy, self.energy_hi)
        return s

    def shot(self, doc, k):
        rng = self.rng(2, k)
        main = [(64, 12)]
        y = 12
        while y < 116:
            y += rng.uniform(10, 16)
            main.append((64 + rng.uniform(-8, 8) * (y / 116), min(y, 120)))
        s = inked_line(main, self.energy, 6, WHITE, ink=5)
        for i in range(2):
            j = rng.randint(2, len(main) - 2)
            bx, by = main[j]
            s += inked_line(jag(rng, bx, by, bx + rng.choice([-1, 1]) * rng.uniform(10, 18), by + rng.uniform(8, 18),
                                3, 3), self.energy, 2.4, None, ink=3)
        s += cel(doc, [(64, 2), (72, 22), (64, 17), (56, 22)], self.main, self.shade, None, ink=3.5)
        return s

    def motif(self, doc, x, y, s, rot, k):
        zig = [P(x, y, 10 * s, rot), P(x, y, 3 * s, rot + 70), P(x, y, 3 * s, rot - 110), P(x, y, -10 * s, rot)]
        return inked_line(zig, self.energy, 2.4 * s, WHITE, ink=3)


# 9 -- Ligher: a pilot light that grows into a roaring blowtorch ---------------
class Ligher(Weapon):
    key = "Ligher"
    flash_points = 9

    def flame_pts(self, base, h, w, jit, sharp=False):
        if sharp:
            return [(64 - w, base), (64 - w * 0.6, base - h * 0.5), (64 + jit, base - h),
                    (64 + w * 0.6, base - h * 0.5), (64 + w, base)]
        return [(64 - w, base), (64 - w * 1.05, base - h * 0.35), (64 - w * 0.5, base - h * 0.62),
                (64 - w * 0.45, base - h * 0.48), (64 + jit, base - h), (64 + w * 0.35, base - h * 0.66),
                (64 + w * 0.55, base - h * 0.72), (64 + w * 1.05, base - h * 0.35), (64 + w, base)]

    def charge(self, doc, c, ready, k):
        rng = self.rng(1, int(round(c * 15)) * 4 + k)
        base = 88
        if ready:
            h = 76 + (k % 2) * 4
            s = cel(doc, self.flame_pts(base, h, 17, rng.uniform(-3, 3), True), self.main, self.shade, None)
            s += poly(self.flame_pts(base, h * 0.8, 10, 0, True), fill=self.spot)
            s += poly(self.flame_pts(base, h * 0.55, 5, 0, True), fill=WHITE)
            s += speed_lines(rng, 64, base - h * 0.5, 22, 36, 6, self.hi, 2.6, a0=-80, a1=80)
        else:
            h = 14 + 60 * c
            w = 8 + 12 * c
            jit = rng.uniform(-4, 4) * (0.4 + c)
            s = cel(doc, self.flame_pts(base, h, w, jit), self.main, self.shade, None)
            s += poly(self.flame_pts(base, h * 0.66, w * 0.6, jit * 0.5), fill=self.hi)
            s += poly(self.flame_pts(base, 8 + 6 * c, w * 0.42, 0, True), fill=self.spot)  # blue base
        # the lighter itself: chimney with vent holes, striker wheel, case
        s += cel(doc, [(46, 86), (82, 86), (82, 98), (46, 98)], GUN_HI, GUN, None, ink=4)
        for i in range(4):
            s += poly(ngon(52 + i * 8, 92, 2.2, 4, 45), fill=INK)
        s += cel(doc, [(42, 98), (86, 98), (86, 118), (42, 118)], self.main, self.shade, self.hi, ink=4.5)
        s += cel(doc, ngon(90, 92, 7, 8, k * 22.5), GUN_HI, INK, None, ink=3.5)
        for i in range(4):  # heat gauge on the case
            on = c * 4 >= i + 0.5
            s += poly([(48 + i * 9, 106), (54 + i * 9, 106), (54 + i * 9, 111), (48 + i * 9, 111)],
                      fill=self.energy if on else INK)
        return s

    def shot(self, doc, k):
        rng = self.rng(2, k)
        tip = 112 + rng.uniform(-6, 8)
        wob = rng.uniform(-6, 6)
        fl = [(64, 18), (80, 38), (80, 56), (72, 74), (70 + wob * 0.4, 94), (64 + wob, tip), (58 + wob * 0.4, 94),
              (56, 74), (48, 56), (48, 38)]
        s = cel(doc, fl, self.main, self.shade, None)
        s += poly(scale_pts(fl, 64, 46, 0.62), fill=self.hi)
        s += poly(ngon(64, 42, 9, 6, k * 10), fill=WHITE)
        return s

    def motif(self, doc, x, y, s, rot, k):
        pts_ = [(x, y - 10 * s), (x + 6 * s, y), (x, y + 6 * s), (x - 6 * s, y)]
        return cel(doc, pts_, self.main, self.shade, None, ink=3)


# 10 -- Paranoid: an eye that wakes up and starts looking around ---------------
class Paranoid(Weapon):
    key = "Paranoid"
    flash_points = 5
    looks = [(-10, -2), (9, -3), (-4, 3), (11, 2), (0, -4), (-12, 1), (6, 4), (-7, -4),
             (12, -1), (2, 3), (-11, -3), (8, 1), (-3, -4), (10, 3), (0, 0), (0, -2)]

    def almond(self, top, bot):
        out = []
        for i in range(13):
            t = i / 12
            out.append((18 + 92 * t, (1 - t) ** 2 * 64 + 2 * (1 - t) * t * top + t * t * 64))
        for i in range(1, 12):
            t = 1 - i / 12
            out.append((18 + 92 * t, (1 - t) ** 2 * 64 + 2 * (1 - t) * t * bot + t * t * 64))
        return out

    def charge(self, doc, c, ready, k):
        o = 0.08 + 0.92 * smooth(c)
        if ready:
            o = 1.1 + 0.06 * (k % 2)
        top = 64 - 40 * o
        bot = 64 + 30 * o
        eye = self.almond(top, bot)
        s = poly(eye, fill=self.main, stroke=INK, w=13)  # thick red lid rim
        s += poly(eye, fill=INK, stroke=INK, w=5)
        clip = doc.uid("eye")
        doc.defs.append('<clipPath id="%s"><polygon points="%s"/></clipPath>' % (clip, pts(eye)))
        dx, dy = self.looks[int(round(c * 15)) % len(self.looks)] if not ready else [(0, 0), (-3, 0), (3, 0), (0, 0)][k]
        inner = poly(eye, fill=BONE)
        inner += poly([(18, 64), (110, 64), (110, 40), (18, 40)], fill=AMBER, op=0.6)  # lid shadow
        rng = self.rng(1, 3)
        for i in range(int(c * 6) + (2 if ready else 0)):
            side = -1 if i % 2 else 1
            inner += line(jag(rng, 64 + side * 46, 64 + rng.uniform(-8, 8), 64 + side * 20, 64 + rng.uniform(-12, 12), 3, 3),
                          KANEDA, 1.6)
        iris = ngon(64 + dx, 64 + dy, 18, 10, 0)
        inner += cel(doc, iris, KANEDA if ready else self.energy, KANEDA_SHADE if ready else TEAL_SH, None, ink=3.5)
        if ready:
            inner += poly(ngon(64 + dx, 64 + dy, 4, 6), fill=INK)
        else:
            inner += poly([(64 + dx, 64 + dy - 13), (68 + dx, 64 + dy), (64 + dx, 64 + dy + 13), (60 + dx, 64 + dy)],
                          fill=INK)
        inner += poly(ngon(57 + dx, 57 + dy, 4, 4, 45), fill=WHITE)
        s += g(inner, clip=clip)
        for i in range(5):  # lashes: one more springs up per fifth of charge
            t = (i + 1) / 6.0
            x = 18 + 92 * t
            y = (1 - t) ** 2 * 64 + 2 * (1 - t) * t * top + t * t * 64 - 5
            ln = 4 + 8 * clamp01(c * 5 - i)
            x1, y1 = P(x, y, ln, (t - 0.5) * 70)
            s += inked_line([(x, y), (x1, y1)], self.main if c * 5 >= i + 0.5 else NIGHT_HI, 2.4, None, ink=3)
        if ready:
            for i, ang in enumerate((-48, 0, 48)):
                s += inked_line([P(64, 64, 50, ang), P(64, 64, 60, ang)], WHITE if (i + k) % 2 else KANEDA, 4,
                                None, ink=3)
        return s

    def shot(self, doc, k):
        s = ""
        for i in range(3):
            q = [(64 + (i - 1) * 7 + math.sin(k * 1.57 + j * 0.9 + i) * (1.5 + j * 1.5), 58 + j * 11) for j in range(6)]
            s += inked_line(q, self.main if i == 1 else self.energy, 4 - i % 2, None, ink=3)
        s += circle(64, 42, 18, fill=self.main, stroke=INK, w=4)
        s += circle(64, 42, 13, fill=BONE)
        look = [(0, -4), (-2, -4), (0, -5), (2, -4)][k % 4]
        s += poly(ngon(64 + look[0], 42 + look[1], 8, 10), fill=KANEDA, stroke=INK, w=2.4)
        s += poly([(64 + look[0], 34 + look[1]), (66.5 + look[0], 42 + look[1]), (64 + look[0], 50 + look[1]),
                   (61.5 + look[0], 42 + look[1])], fill=INK)
        s += poly(ngon(60 + look[0], 38 + look[1], 2.4, 4, 45), fill=WHITE)
        return s

    def motif(self, doc, x, y, s, rot, k):
        return cel(doc, [(x - 7 * s, y), (x, y - 9 * s), (x + 7 * s, y), (x, y + 7 * s)], self.energy, TEAL_SH, None,
                   ink=3)


# 11 -- Ninja: shuriken blades snap out one at a time and spin up --------------
class Ninja(Weapon):
    key = "Ninja"
    flash_points = 4

    def blade(self, ang, f, cx=64, cy=64, scale=1.0):
        tip = (14 + 38 * f) * scale
        return [P(cx, cy, 9 * scale, ang - 42), P(cx, cy, tip * 0.55, ang - 16), P(cx, cy, tip, ang),
                P(cx, cy, tip * 0.5, ang + 12), P(cx, cy, 9 * scale, ang + 42)]

    def charge(self, doc, c, ready, k):
        s = ""
        for i in range(4):
            ang = i * 90 + (k * 11 if ready else 0)
            f = smooth(clamp01(c * 4 - i))
            if f <= 0:
                s += poly(self.blade(ang, 1.0), stroke=NIGHT_HI, w=2.4)
            else:
                s += cel(doc, self.blade(ang, f), self.main, self.shade, None, ink=4.5)
                tip = self.blade(ang, f)
                s += line([tip[1], tip[2]], self.energy if (ready or f >= 1) else self.hi, 2.6)  # red edge
        s += cel(doc, ngon(64, 64, 12, 8, 22.5), NIGHT_HI, INK, None, ink=4)
        s += poly(ngon(64, 64, 4.5, 4, 45), fill=self.energy if c > 0.99 else self.shade)
        if ready:
            rng = self.rng(1, k)
            for i in range(2):
                a0 = k * 30 + i * 180
                s += poly(band(64, 64, 54, 59, a0, a0 + 70), fill=self.energy, stroke=INK, w=2.4)
            gx, gy = P(64, 64, 44, k * 90 + 20)
            s += light(star(gx, gy, 9, 2.4, 4, 0), WHITE, self.energy_hi, rings=1)
        return s

    def shot(self, doc, k):
        rot = (k % 4) * 22.5
        s = ""
        for j in range(2):  # smear wedges of the spinning blades
            for i in range(4):
                s += poly(band(64, 64, 18, 36 - j * 6, i * 90 + rot - 34 - j * 10, i * 90 + rot - 4, 3),
                          fill=self.energy if j == 0 else self.shade, op=0.55 - j * 0.2)
        for i in range(4):
            s += cel(doc, self.blade(i * 90 + rot, 0.8, scale=0.92), self.main, self.shade, None, ink=4)
        s += poly(ngon(64, 64, 8, 8, 22.5), fill=NIGHT_HI, stroke=INK, w=3.5)
        s += poly(ngon(64, 64, 3, 4, 45), fill=self.energy)
        return s

    def motif(self, doc, x, y, s, rot, k):
        a = P(x, y, 11 * s, rot + 45)
        b = P(x, y, -11 * s, rot + 45)
        return inked_line([a, b], self.energy, 3 * s, WHITE, ink=3)


# 12 -- Saboteur: a gatling cluster spinning up through the heat colours -------
class Saboteur(Weapon):
    key = "Saboteur"
    flash_points = 6

    def heat_col(self, h):
        ramp = [GUN_SH, KANEDA, SODIUM, AMBER_HI]
        i = min(3, int(clamp01(h) * 3.999))
        return ramp[i]

    def charge(self, doc, c, ready, k):
        s = cel(doc, ngon(64, 64, 48, 12, 15), GUN, GUN_SH, None, ink=5)
        s += poly(ngon(64, 64, 38, 12, 15), stroke=INK, w=2.4)
        for j in range(6):
            ang = j * 60 + (k * 20 if ready else 0)
            x, y = P(64, 64, 24, ang)
            h = 1.0 if ready else clamp01(c * 6 - j)
            s += poly(ngon(x, y, 11, 8, 22.5), fill=GUN_HI, stroke=INK, w=3.4)
            bore = ngon(x, y, 6.5, 8, 22.5)
            if h > 0:
                col = self.heat_col(h)
                s += light(bore, col, col, core=WHITE if h >= 1 else col, rings=1 if h >= 1 else 0) if h >= 0.75 else \
                    poly(bore, fill=col, stroke=INK, w=2)
            else:
                s += poly(bore, fill=INK)
        s += cel(doc, ngon(64, 64, 9, 6, 0), GUN_HI, GUN, None, ink=3)
        # heat gauge wedge on the housing rim
        s += poly(band(64, 64, 49, 55, -60, 60), fill=INK)
        if c > 0:
            s += poly(band(64, 64, 50, 54, -58, -58 + 116 * c), fill=self.heat_col(c))
        if ready:
            rng = self.rng(1, k)
            s += speed_lines(rng, 64, 64, 52, 62, 10, self.energy_hi, 2.6)
        return s

    def shot(self, doc, k):
        rng = self.rng(2, k)
        s = ""
        for i in range(2):
            s += puff(doc, 64 + rng.uniform(-5, 5), 96 + i * 14, 8 + i * 3, random.Random(k * 7 + i))
        s += poly([(56, 80), (64, 124), (72, 80)], fill=self.energy, op=0.5)
        shell = [(64, 14), (76, 40), (76, 80), (52, 80), (52, 40)]
        s += cel(doc, shell, self.main, self.shade, self.hi)
        s += poly([(64, 14), (76, 40), (52, 40)], fill=self.heat_col(0.8 + 0.2 * (k % 2)), stroke=INK, w=3)
        s += poly([(64, 22), (70, 36), (58, 36)], fill=WHITE)
        s += line([(52, 62), (76, 62)], INK, 3, cap="butt")
        return s

    def motif(self, doc, x, y, s, rot, k):
        shard = [P(x, y, 9 * s, rot), P(x, y, 5 * s, rot + 120), P(x, y, 7 * s, rot + 220)]
        return cel(doc, shard, GUN_HI, INK, None, ink=3) + poly(ngon(x, y, 2 * s, 4), fill=self.energy)


# 13 -- UFO: drones swoop in and lock into formation ----------------------------
class UFO(Weapon):
    key = "UFO"
    flash_points = 8
    slots = [(64, 28), (40, 50), (88, 50), (20, 74), (108, 74)]

    def drone(self, doc, x, y, s, k, ghost=False):
        hull = ngon(x, y, 12 * s, 8, 22.5, sy=0.4)
        dome = [(x - 6 * s, y - 2 * s), (x - 4 * s, y - 7 * s), (x + 4 * s, y - 7 * s), (x + 6 * s, y - 2 * s)]
        if ghost:
            return poly(hull, stroke=NIGHT_HI, w=2.4) + poly(dome, stroke=NIGHT_HI, w=2)
        d = cel(doc, dome, self.energy_hi, self.energy, None, ink=3)
        d += cel(doc, hull, self.main, self.shade, None, ink=3.5)
        for i in range(3):
            on = (i + k) % 3 == 0
            d += poly(ngon(x + (i - 1) * 6 * s, y + 0.5 * s, 1.8 * s, 4, 45), fill=self.energy if on else INK)
        return d

    def charge(self, doc, c, ready, k):
        s = ""
        start = (64, 116)
        drones = ""
        for i, (sx, sy) in enumerate(self.slots):
            f = clamp01(c * 5 - i)
            if f < 1:
                s += self.drone(doc, sx, sy, 1.15, k, ghost=True)
            if f > 0:
                e = ease(f)
                x = start[0] + (sx - start[0]) * e + math.sin(f * math.pi) * (18 if sx < 64 else -18)
                y = start[1] + (sy - start[1]) * e
                drones += self.drone(doc, x, y, 0.8 + 0.35 * e, k + i)
        if ready:
            order = [3, 1, 0, 2, 4]
            for a_, b_ in zip(order, order[1:]):
                s += inked_line([self.slots[a_], self.slots[b_]], self.energy, 2.4, None, ink=3)
            s += light([(64, 4 - (k % 2) * 2), (58, 14), (70, 14)], self.energy, self.energy_hi, rings=1)
        s += drones
        s += cel(doc, ngon(64, 112, 22, 8, 22.5, sy=0.3), NIGHT_HI, NIGHT, None, ink=4)
        if c > 0:
            s += poly(ngon(64, 112, 14 * c, 8, 22.5, sy=0.3), fill=self.energy)
        return s

    def shot(self, doc, k):
        s = poly([(54, 74), (74, 74), (86, 116), (42, 116)], fill=self.energy, op=0.35)
        s += g(self.drone(doc, 64, 60, 2.3, k), tf=None)
        return s

    def motif(self, doc, x, y, s, rot, k):
        return poly(ngon(x, y, 5 * s, 4, rot), fill=self.energy if int(rot) % 2 else self.main, stroke=INK, w=2.4)


# 14 -- Dove: wing feathers fan out one by one around a halo -------------------
class Dove(Weapon):
    key = "Dove"
    flash_points = 6
    angles = [32, -32, 64, -64, 98, -98]

    def feather(self, ang, f, cx=64, cy=70, ln=50, w=10.5):
        L = max(ln * f, 0.5)
        base = P(cx, cy, 8, ang)
        out = [base]
        for i in range(1, 5):  # notched trailing edge, angular
            t = i / 5
            m = P(cx, cy, 8 + L * t, ang)
            out.append(P(m[0], m[1], w * (1 - t * 0.5) * (1.0 if i % 2 else 0.8), ang - 90))
        out.append(P(cx, cy, 8 + L, ang))
        m = P(cx, cy, 8 + L * 0.5, ang)
        out.append(P(m[0], m[1], w * 0.55, ang + 90))
        return out, base, P(cx, cy, 8 + L, ang)

    def charge(self, doc, c, ready, k):
        s = ""
        for i, ang in enumerate(self.angles):
            f = smooth(clamp01(c * 6 - i))
            fp, _, _ = self.feather(ang, 1.0)
            if f <= 0:
                s += poly(fp, stroke=NIGHT_HI, w=2.2)
            else:
                fp, base, tip = self.feather(ang + (math.sin(k * 1.6 + i) * 3 if ready else 0), f)
                s += cel(doc, fp, self.main, self.shade, None, ink=4)
                s += line([base, tip], self.shade, 1.6)
        halo = ngon(64, 76, 16, 10, 0, sy=0.33)
        s += poly(halo, stroke=INK, w=8) + poly(halo, stroke=self.energy if c > 0.99 else self.shade, w=4)
        s += cel(doc, [(64, 56), (71, 66), (64, 72), (57, 66)], step(NIGHT_HI, self.main, c), self.shade, None, ink=3.5)
        if ready:
            rng = self.rng(1, k)
            s += light(ngon(64, 76, 16, 10, 0, sy=0.33), self.energy, self.energy_hi, rings=1)
            for i in range(3):
                x, y = P(64, 66, rng.uniform(34, 52), rng.uniform(-110, 110))
                s += poly(star(x, y, 6, 1.6, 4, 0), fill=self.energy_hi, stroke=INK, w=1.6)
        return s

    def shot(self, doc, k):
        sway = [0, 4, 6, 3][k % 4] - 2
        fp, base, tip = self.feather(sway, 1.0, cx=64, cy=112, ln=88, w=17)
        s = poly([(58, 90), (64, 126), (70, 90)], fill=self.energy, op=0.5)
        s += cel(doc, fp, self.main, self.shade, self.hi)
        s += line([base, tip], self.shade, 2)
        s += light(ngon(tip[0], tip[1] + 4, 6, 4, 45), self.energy, self.energy_hi, rings=1)
        return s

    def motif(self, doc, x, y, s, rot, k):
        fp, _, _ = self.feather(rot, 1.0, cx=x, cy=y, ln=12 * s, w=4 * s)
        return cel(doc, fp, self.main, self.shade, None, ink=2.5)


# 15 -- Turtle: hex shell plates lock in around a core ---------------------------
class Turtle(Weapon):
    key = "Turtle"
    flash_points = 6

    def charge(self, doc, c, ready, k):
        R = 13.5
        cells = [P(64, 64, 26, i * 60) for i in range(6)] + [(64, 64)]
        s = poly(hexagon(64, 64, 52, 30), fill=NIGHT, stroke=INK, w=6)
        s += poly(hexagon(64, 64, 52, 30), stroke=self.main if c > 0.99 else NIGHT_HI, w=3 + (1.5 if ready and k % 2 else 0))
        for i, (x, y) in enumerate(cells):
            f = clamp01(c * 7 - i)
            if f <= 0:
                s += poly(hexagon(x, y, R, 30), fill=INK, stroke=NIGHT_HI, w=2)
            elif i == 6:
                s += light(hexagon(x, y, R * (0.6 + 0.4 * f), 30), self.energy, self.energy_hi,
                           rings=2 if ready else 1)
            else:
                s += cel(doc, hexagon(x, y, R * (0.6 + 0.4 * f), 30), self.main, self.shade,
                         self.hi if f >= 1 else None, ink=3.5)
        return s

    def shot(self, doc, k):
        rot = 30 + (k % 4) * 15
        s = poly([(52, 70), (64, 126), (76, 70)], fill=self.energy, op=0.35)
        s += cel(doc, hexagon(64, 64, 30, rot), self.shade, INK, None, ink=5)
        for i in range(6):
            x, y = P(64, 64, 18, rot - 30 + i * 60)
            s += cel(doc, hexagon(x, y, 8, rot), self.main, self.shade, None, ink=2.5)
        s += light(hexagon(64, 64, 8, rot), self.energy, self.energy_hi, rings=1)
        return s

    def motif(self, doc, x, y, s, rot, k):
        return cel(doc, hexagon(x, y, 7 * s, rot), self.main, self.shade, None, ink=3)


ROSTER = {1: NeonComet, 2: VoltViper, 3: SolarFang, 4: CrimsonHalo, 5: IonLancer, 6: JadePhantom,
          7: GoldWarden, 8: Lightning, 9: Ligher, 10: Paranoid, 11: Ninja, 12: Saboteur, 13: UFO,
          14: Dove, 15: Turtle}

# ------------------------------------------------------- target explosions ----
#
# What a target does when a player weapon destroys it. Three kinds: hostile
# metal (enemy craft, aliens, chasers) in magenta-violet, rock (asteroids) in
# browns with sodium fire, and mines, which go up bigger and hotter. The
# firing weapon's colour is added at runtime through the white overlay
# frames (flash star, shockwave ring), so every hit still reads as "my ship
# did that".
#
# Explosions.png: rows metal / rock / mine, 10 frames each in columns 0-9;
# row 0 columns 10-12 hold the white overlays flash_0, flash_1, ring.
EXPLOSION_FRAMES = 10
EXPLOSIONS = {
    #        main        shade       hi          fire        fire_hi     smoke       smoke_shade debris
    # hostile craft: BRUISE armour, STEEL plates, MAGENTA (enemy light) fire
    "metal": ("#74409A", "#3A1E52", "#A86CD0", MAGENTA, BONE, SMOKE, SMOKE_SHADE, "metal"),
    # rocks: ROCK tones with an AMBER rim kick, SODIUM fire
    "rock":  ("#605878", "#2C2638", AMBER, SODIUM, AMBER, SMOKE, SMOKE_SHADE, "rock"),
    # mines: BRUISE hub, hotter and bigger SODIUM / AMBER fire
    "mine":  ("#74409A", "#3A1E52", "#A86CD0", SODIUM, AMBER, SMOKE, SMOKE_SHADE, "metal"),
}
EXPLOSION_ROWS = ["metal", "rock", "mine"]


def debris_chunk(doc, kind, x, y, s, rot, pal, seed):
    rng = random.Random(seed)
    if kind == "metal":
        # bent hull plate with a stripe of the hull's paint
        pts_ = [P(x, y, 9 * s, rot), P(x, y, 7 * s, rot + 80), P(x, y, 9 * s, rot + 170), P(x, y, 5 * s, rot + 260)]
        out = cel(doc, pts_, "#5A6A88", "#262D44", None, ink=3)
        out += line([P(x, y, 5 * s, rot + 20), P(x, y, 5 * s, rot + 200)], pal[0], 1.8 * s)
        return out
    sides = rng.randint(5, 6)
    pts_ = [P(x, y, (6 + rng.uniform(0, 4)) * s, rot + i * 360.0 / sides) for i in range(sides)]
    return cel(doc, pts_, pal[0], pal[1], pal[2], ink=3)


def explosion(doc, kind, k):
    main, shade, hi, fire, fire_hi, smoke, smoke_shade, dkind = EXPLOSIONS[kind]
    big = 1.1 if kind == "mine" else 1.0
    rng = random.Random(len(kind) * 97 + 5)
    if k == 0:   # anticipation: everything pinches inward for one frame
        s = speed_lines(random.Random(1), 64, 64, 26, 54, 12, WHITE, 3)
        s += poly(star(64, 64, 18, 6, 4, 45), fill=WHITE, stroke=INK, w=3)
        return s
    if k == 1:   # 1-tick impact frame: pure white, red outline
        return poly(star(64, 64, 58 * big, 26, 10, 0), fill=PURE, stroke=KANEDA, w=4)
    if k == 2:   # red flash with a white heart
        s = poly(burst(rng, 64, 64, 58 * big, 30, 9, 12), fill=KANEDA, stroke=INK, w=5)
        s += poly(burst(rng, 64, 64, 30, 14, 7, 0), fill=WHITE)
        return s
    if k == 3:   # the held key pose: a hard anime star burst, squashed wide
        s = poly(burst(rng, 64, 64, 54 * big, 30, 11, 6), fill=fire, stroke=INK, w=5)
        s += poly(burst(rng, 64, 64, 38 * big, 22, 9, 26), fill=fire_hi)
        s += poly(burst(rng, 64, 64, 22, 12, 7, 0), fill=WHITE)
        return g(s, tf="translate(64 64) scale(1.14 0.88) translate(-64 -64)")
    p = (k - 4) / 5.0
    if k == 4:   # springs tall out of the squash
        return g(_explosion_tail(doc, kind, p), tf="translate(64 64) scale(0.92 1.09) translate(-64 -64)")
    return _explosion_tail(doc, kind, p)


def _explosion_tail(doc, kind, p):
    main, shade, hi, fire, fire_hi, smoke, smoke_shade, dkind = EXPLOSIONS[kind]
    big = 1.1 if kind == "mine" else 1.0
    rng = random.Random(len(kind) * 97 + 5)
    k = int(round(p * 5)) + 4
    e = ease(p)
    s = ""
    if p < 0.3:  # the shockwave band, gone after two frames
        s += poly(band(64, 64, 48 + 6 * p, 54 + 4 * p, 0, 359.9, 24), fill=fire_hi, op=0.9)
    # smoke: six ink-outlined puffs that drift out and break up
    prng = random.Random(len(kind) * 13)
    for i in range(6):
        ang = i * 60 + prng.uniform(-18, 18)
        dist = 16 + 26 * e
        r = (21 if i % 2 else 17) * big * (1 - 0.3 * p)
        x, y = P(64, 64, dist, ang)
        if p < 0.55:
            s += puff(None, x, y, r, random.Random(i * 7 + k), smoke, smoke_shade, fire_hi if p < 0.7 else fire)
        else:  # broken into two smaller puffs
            for j in (-1, 1):
                bx, by = P(x, y, r * 0.7 * (0.6 + p), ang + 90 * j)
                s += puff(None, bx, by, r * (1.4 - p) * 0.8, random.Random(i * 11 + j + k), smoke, smoke_shade, fire)
    if p < 0.75:  # fire core shrinking inside the smoke
        f = 1 - p / 0.75
        s += poly(burst(rng, 64, 64, 34 * f * big + 4, 17 * f + 2, 9, k * 15), fill=fire, stroke=INK, w=4)
        s += poly(burst(rng, 64, 64, 19 * f + 2, 9 * f + 1, 7, k * 7), fill=fire_hi)
    # debris chunks flying clear, tumbling
    drng = random.Random(len(kind) * 31)
    for i in range(7):
        ang = i * 51 + drng.uniform(-14, 14)
        x, y = P(64, 64, 22 + 34 * e, ang)
        s += debris_chunk(doc, dkind, x, y, (1.15 - 0.35 * p) * big, ang + k * 47, (main, shade, hi), i * 5 + len(kind))
    if p < 0.25:  # spark streaks on the first frames
        s += speed_lines(random.Random(k), 64, 64, 42, 62, 10, fire_hi, 2.6)
    return s


def overlay(doc, k):
    """Tintable white overlays: flash star (2 frames) and shockwave ring."""
    if k == 0:
        return poly(star(64, 64, 60, 22, 8, 0), fill="#ffffff")
    if k == 1:
        return poly(star(64, 64, 46, 18, 8, 22.5), fill="#ffffff")
    return poly(band(64, 64, 52, 60, 0, 359.9, 32), fill="#ffffff")


def build_explosions(preview_dir=None):
    tmp = tempfile.mkdtemp()
    try:
        src = os.path.join(ART, "Explosions", "src~")
        os.makedirs(src, exist_ok=True)
        for old in os.listdir(src):
            if old.endswith(".svg"):
                os.remove(os.path.join(src, old))
        cells = []
        for row, kind in enumerate(EXPLOSION_ROWS):
            for k in range(EXPLOSION_FRAMES):
                cells.append(("%s_%d" % (kind, k), row, k, frame(explosion, kind, k, hold=EXPLOSION_TICKS[k])))
        for k, name in enumerate(["flash_0", "flash_1", "ring"]):
            cells.append((name, 0, 10 + k, frame(overlay, k)))
        atlas = Image.new("RGBA", (COLS * CELL, ROWS * CELL), (0, 0, 0, 0))
        jobs = []
        for name, row, col, text in cells:
            sp = os.path.join(src, name + ".svg")
            with open(sp, "w") as fh:
                fh.write(text)
            pp = os.path.join(tmp, name + ".png")
            jobs.append((subprocess.Popen(["resvg", "-w", str(CELL), "-h", str(CELL), sp, pp]), name, row, col, pp))
        images = {}
        for proc, name, row, col, pp in jobs:
            if proc.wait() != 0:
                raise SystemExit("resvg failed on explosion %s" % name)
            im = Image.open(pp).convert("RGBA")
            ImageDraw.Draw(im).rectangle([0, 0, CELL - 1, CELL - 1], outline=(0, 0, 0, 0), width=2)
            atlas.paste(im, (col * CELL, row * CELL))
            images[name] = im
        atlas.save(os.path.join(OUT, "Explosions.png"), optimize=True)
        if preview_dir:
            os.makedirs(preview_dir, exist_ok=True)
            W = EXPLOSION_FRAMES * CELL
            bg = Image.new("RGBA", (W, len(EXPLOSION_ROWS) * (CELL + 60)), rgb(NIGHT) + (255,))
            d = ImageDraw.Draw(bg)
            for r, kind in enumerate(EXPLOSION_ROWS):
                y0 = r * (CELL + 60)
                d.rectangle([0, y0 + CELL, W, y0 + CELL + 60], fill=rgb(NIGHT) + (255,))
                for k in range(EXPLOSION_FRAMES):
                    im = images["%s_%d" % (kind, k)]
                    bg.alpha_composite(im, (k * CELL, y0))
                    bg.alpha_composite(im.resize((52, 52), Image.LANCZOS), (k * CELL + 38, y0 + CELL + 4))
                    d.text((k * CELL + 4, y0 + 2), "%s_%d" % (kind, k), fill=(200, 200, 255, 255))
            bg.convert("RGB").save(os.path.join(preview_dir, "explosions.png"))
        print("built Explosions")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


# ------------------------------------------------------------------- build ----

def frame(fn, *args, hold=None):
    doc = Doc()
    return svg(doc, fn(doc, *args), hold)


def ready_pose(w, doc, k):
    """Ready loop: the full-charge drawing, swelling on the even (held) poses
    -- the squash is drawn, not tweened in code."""
    inner = w.charge(doc, 1.0, True, k)
    if k % 2 == 0:
        return g(inner, tf="translate(64 64) scale(1.1) translate(-64 -64)")
    return inner


def frames(w):
    """(name, row, col, svg) for every cell of the ship's atlas."""
    out = []
    for i in range(CHARGE_FRAMES):
        out.append(("charge_%02d" % i, 0, i, frame(w.charge, i / (CHARGE_FRAMES - 1), False, 0)))
    for i in range(READY_FRAMES):
        out.append(("ready_%d" % i, 1, i, frame(lambda d, k: ready_pose(w, d, k), i, hold=READY_TICKS[i])))
    for i in range(SHOT_FRAMES):
        out.append(("shot_%d" % i, 1, 4 + i, frame(w.shot, i, hold=SHOT_TICKS[i]) if i < 4
                    else frame(w.smear, i - 4, hold=SHOT_TICKS[i])))
    for i in range(IMPACT_FRAMES):
        out.append(("impact_%d" % i, 1, 10 + i, frame(w.impact, i, hold=IMPACT_TICKS[i])))
    for i in range(MUZZLE_FRAMES):
        out.append(("muzzle_%d" % i, 2, i, frame(w.muzzle, i, hold=MUZZLE_TICKS[i])))
    for i in range(RELEASE_FRAMES):
        out.append(("release_%d" % i, 2, 4 + i, frame(w.release, i, hold=RELEASE_TICKS[i])))
    for i in range(TRAIL_FRAMES):
        out.append(("trail_%d" % i, 2, 8 + i, frame(w.trail, i)))
    return out


def build(preview_dir=None, only=None):
    os.makedirs(OUT, exist_ok=True)
    tmp = tempfile.mkdtemp()
    try:
        for ship, cls in sorted(ROSTER.items()):
            w = cls(ship)
            if only and w.key not in only:
                continue
            src = os.path.join(ART, w.key, "src~")
            os.makedirs(src, exist_ok=True)
            for old in os.listdir(src):
                if old.endswith(".svg"):
                    os.remove(os.path.join(src, old))
            atlas = Image.new("RGBA", (COLS * CELL, ROWS * CELL), (0, 0, 0, 0))
            cells = {}
            jobs = []
            for name, row, col, text in frames(w):
                sp = os.path.join(src, name + ".svg")
                with open(sp, "w") as fh:
                    fh.write(text)
                pp = os.path.join(tmp, "%s_%s.png" % (w.key, name))
                jobs.append((subprocess.Popen(["resvg", "-w", str(CELL), "-h", str(CELL), sp, pp]), name, row, col, pp))
            for proc, name, row, col, pp in jobs:
                if proc.wait() != 0:
                    raise SystemExit("resvg failed on %s %s" % (w.key, name))
                im = Image.open(pp).convert("RGBA")
                # a clean transparent gutter so bilinear sampling never bleeds
                # a neighbouring frame into this one
                ImageDraw.Draw(im).rectangle([0, 0, CELL - 1, CELL - 1], outline=(0, 0, 0, 0), width=2)
                atlas.paste(im, (col * CELL, row * CELL))
                cells[name] = im
            atlas.save(os.path.join(OUT, w.key + ".png"), optimize=True)
            if preview_dir:
                preview(w, cells, preview_dir)
            print("built", w.key)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def preview(w, cells, out_dir):
    """Charge sequence + ready + release + shot + trail + impact + muzzle on an
    indigo night backdrop, with a second row at roughly in-game size."""
    os.makedirs(out_dir, exist_ok=True)
    order = (["charge_%02d" % i for i in range(0, 16, 3)] + ["charge_15"] +
             ["ready_%d" % i for i in range(4)] + ["release_%d" % i for i in range(4)] +
             ["shot_%d" % i for i in range(6)] + ["trail_0"] +
             ["impact_%d" % i for i in range(6)] + ["muzzle_%d" % i for i in range(4)])
    W = len(order) * CELL
    H = CELL + 60
    bg = Image.new("RGBA", (W, H), rgb(NIGHT) + (255,))
    d = ImageDraw.Draw(bg)
    d.rectangle([0, CELL, W, H], fill=rgb(NIGHT) + (255,))
    for i, name in enumerate(order):
        bg.alpha_composite(cells[name], (i * CELL, 0))
        small = cells[name].resize((48, 48), Image.LANCZOS)
        bg.alpha_composite(small, (i * CELL + 40, CELL + 8))
        d.text((i * CELL + 4, 2), name, fill=(200, 200, 255, 255))
    bg.convert("RGB").save(os.path.join(out_dir, "%02d_%s.png" % (w.ship, w.key)))


if __name__ == "__main__":
    prev = None
    only = None
    args = sys.argv[1:]
    if "--preview" in args:
        prev = args[args.index("--preview") + 1]
    if "--only" in args:
        only = set(args[args.index("--only") + 1].split(","))
    if "--explosions-only" not in args:
        build(prev, only)
    if not only:
        build_explosions(prev)
