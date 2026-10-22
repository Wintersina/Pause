"""Per-ship engine exhaust: one themed flipbook per ship, all in one atlas.

    python3 exhaust.py   ->  ../../Exhaust/exhaust_atlas.png
                             + the generated block in Scripts/Ship/ShipExhaustStyle.cs

Every ship's exhaust is coloured from its identity hue (ShipHullArt) and its
weapon family palette (WeaponStyleTable), and shaped on the ship's concept,
so the hull, the ultimate and the exhaust read as one theme. Rules from
docs/art-style.md: flat cel light shapes (no gradients, no grain), glow only
as a blurred copy of the light shape, snappy timing on 2s with a 1-tick
smear frame per loop.

Nozzle ships (13): a plume strip and a boost strip (longer, brighter), each
6-8 frames of 32 x 128 u rendered at 1.5x (48 x 192 px). The flame head is at
y = HEAD; ShipExhaustStyle pivots plume sprites there, so the plume's top
stays on its nozzle (ShipNozzles) while its length changes.

Spinners (Ninja, UFO): a "spin drift" instead of a plume, in two layers of
64 x 64 u frames rendered at 1.875x (120 x 120 px):
  ring  centred on the hull and parented to it, so it turns with the spin
        (blade-arc afterimages / an orbiting ring of light pulses)
  wake  hangs below the hull, world-aligned (curling shuriken cut streaks /
        tractor-beam shimmer + vortex swirl), pivoted at its top
each with a boost variant.

All strips are shelf-packed into one atlas with a 2 px gutter (mipmaps must
not bleed between frames); the packed rects, timing and colours are written
into ShipExhaustStyle.cs between the GENERATED markers.
"""
import math
import os
import random
import re
import subprocess

from PIL import Image

from hullkit import *  # noqa: F401,F403

OUT = os.path.abspath(os.path.join(HERE, "../../Exhaust"))
CS = os.path.abspath(os.path.join(HERE, "../../../../../Scripts/Ship/ShipExhaustStyle.cs"))
BUILD = os.path.join(HERE, "build", "exhaust")

PW, PH, PZOOM, HEAD = 32, 128, 1.5, 3.0      # plume canvas (u), zoom, flame head y
SW, SZOOM, WAKE_HEAD = 64, 1.875, 4.0         # spinner canvas (u, square), zoom, wake head y
ATLAS_W, GUTTER = 2048, 2


# ----------------------------------------------------------------- palette --
class Pal:
    """outer = the ship's hue (the plume's silhouette), mid = the weapon's
    energy (or the hue's highlight where that reads better), core = the hot
    centre, dark = the hue's shadow (smoke, cut marks, ring backs), accent =
    the weapon's second tone (sparks, shock diamonds, tips)."""

    def __init__(self, outer, mid, core, dark, accent):
        self.outer, self.mid, self.core, self.dark, self.accent = outer, mid, core, dark, accent


# Hue tones are ShipHullArt's (hue, shadow, highlight); weapon tones are
# WeaponStyleTable's (main, shade, energy); ExhaustStyleTest cross-checks.
STYLES = [
    # id, key, kind, palette
    (1, "NeonComet", "CometTrail", Pal(RED, SODIUM, BONE, RED_SH, CYAN)),
    (2, "VoltViper", "VoltZigzag", Pal("#2E9BE6", CYAN, BONE, "#174C8C", SODIUM)),
    (3, "SolarFang", "SolarTongues", Pal("#F07A1E", AMBER, BONE, "#9A3E12", SODIUM)),
    (4, "CrimsonHalo", "HaloRings", Pal("#C8285E", AMBER, BONE, "#6E1136", "#FF7AA2")),
    (5, "IonLancer", "IonLance", Pal("#3A5BE0", CYAN, BONE, "#1C2A80", "#9AB0FF")),
    (6, "JadePhantom", "GhostWisp", Pal("#22B07A", "#8EF0C4", BONE, "#0E5A44", TEAL)),
    (7, "GoldWarden", "Afterburner", Pal("#D99A1A", "#FFD36A", BONE, "#7E4E0C", RED)),
    (8, "Lightning", "ForkedLightning", Pal("#EEDC32", CYAN, BONE, "#8E8414", SODIUM)),
    (9, "Ligher", "Blowtorch", Pal("#C8662E", AMBER, BONE, "#6E3014", CYAN)),
    (10, "Paranoid", "PodJets", Pal("#5DBB3A", TEAL, "#B6EE8A", "#2A5E1E", BONE)),
    (11, "Ninja", "SpinBlades", Pal("#4A58C8", "#9FA8F0", BONE, "#232A6E", RED)),
    (12, "Saboteur", "GatlingSmoke", Pal("#B83CC0", SODIUM, BONE, "#5E1A66", DUSK)),
    (13, "UFO", "SpinVortex", Pal("#7A52DC", "#BBA4FF", BONE, "#3A2478", CYAN)),
    (14, "Dove", "FeatherStreaks", Pal("#36C2C2", "#A6F2EE", BONE, "#146A70", AMBER)),
    (15, "Turtle", "HexJet", Pal(TEAL, CYAN, BONE, "#5A3A18", "#E2BE84")),
]

SPINNERS = ("SpinBlades", "SpinVortex")

# Frame tables: (length u, width mult, smear?, flare) per drawing, and the
# hold of each drawing in 24 fps ticks. Every loop has exactly one 1-tick
# smear and otherwise runs on 2s (a 3-tick key hold where the style rests).
FRAMES = {
    "CometTrail": ([(74, 1.00, 0, .6), (92, 1.05, 0, .8), (122, .72, 1, 1.), (104, .92, 0, .9),
                    (80, 1.10, 0, .7), (88, 1.00, 0, .75)], [2, 2, 1, 2, 2, 2]),
    "VoltZigzag": ([(84, 1.0, 0, .7), (98, .9, 0, .9), (120, .7, 1, 1.), (90, 1.05, 0, .8),
                    (104, .95, 0, .85), (80, 1.1, 0, .7), (94, 1.0, 0, .8)], [2, 2, 1, 2, 2, 2, 2]),
    "SolarTongues": ([(80, 1.0, 0, .7), (94, 1.1, 0, .85), (116, .8, 1, 1.), (100, 1.05, 0, .9),
                      (86, 1.15, 0, .75), (76, 1.0, 0, .65), (90, 1.05, 0, .8)], [3, 2, 1, 2, 2, 2, 2]),
    "HaloRings": ([(88, 1.0, 0, .7), (96, 1.0, 0, .8), (118, .8, 1, 1.), (100, 1.0, 0, .85),
                   (92, 1.0, 0, .75), (86, 1.0, 0, .7)], [2, 2, 1, 2, 2, 2]),
    "IonLance": ([(104, 1.0, 0, .7), (110, .95, 0, .8), (124, .75, 1, 1.), (112, 1.0, 0, .9),
                  (106, 1.05, 0, .8), (100, 1.0, 0, .7)], [3, 2, 1, 2, 2, 2]),
    "GhostWisp": ([(84, 1.0, 0, .6), (92, 1.05, 0, .7), (112, .8, 1, .9), (100, 1.0, 0, .75),
                   (88, 1.1, 0, .65), (80, 1.0, 0, .6), (90, .95, 0, .7), (96, 1.0, 0, .7)],
                  [3, 2, 1, 2, 2, 2, 2, 2]),
    "Afterburner": ([(66, 1.0, 0, .7), (74, 1.05, 0, .85), (96, .85, 1, 1.), (78, 1.1, 0, .9),
                     (70, 1.0, 0, .8), (72, 1.05, 0, .8)], [2, 2, 1, 2, 2, 2]),
    "ForkedLightning": ([(90, 1.0, 0, .7), (104, 1.0, 0, .9), (122, .8, 1, 1.), (96, 1.0, 0, .8),
                         (110, 1.0, 0, .85), (84, 1.0, 0, .7), (100, 1.0, 0, .8)], [2, 2, 1, 2, 2, 2, 2]),
    "Blowtorch": ([(92, 1.0, 0, .75), (96, .96, 0, .8), (114, .8, 1, 1.), (98, 1.0, 0, .85),
                   (94, 1.04, 0, .8), (90, 1.0, 0, .75)], [3, 2, 1, 2, 2, 2]),
    "PodJets": ([(70, 1.0, 0, .7), (80, 1.0, 0, .8), (102, .82, 1, 1.), (86, 1.05, 0, .85),
                 (76, 1.0, 0, .75), (72, 1.05, 0, .75)], [2, 2, 1, 2, 2, 2]),
    "GatlingSmoke": ([(80, 1.0, 0, .8), (88, 1.0, 0, .9), (110, .85, 1, 1.), (92, 1.0, 0, .85),
                      (84, 1.0, 0, .8), (96, 1.0, 0, .85), (86, 1.0, 0, .8), (90, 1.0, 0, .85)],
                     [2, 2, 1, 2, 2, 2, 2, 2]),
    "FeatherStreaks": ([(86, 1.0, 0, .65), (96, 1.05, 0, .75), (116, .8, 1, .95), (102, 1.0, 0, .8),
                        (90, 1.1, 0, .7), (84, 1.0, 0, .65), (94, 1.0, 0, .72)], [3, 2, 1, 2, 2, 2, 2]),
    "HexJet": ([(80, 1.0, 0, .7), (88, 1.0, 0, .8), (108, .85, 1, 1.), (94, 1.05, 0, .85),
                (84, 1.0, 0, .75), (78, 1.0, 0, .7), (90, 1.0, 0, .78)], [2, 2, 1, 2, 2, 2, 2]),
    # spinners: (phase deg, reach, smear?, flare). Phases step evenly through
    # one symmetry period (Ninja's 4 blades: 90 deg; UFO's 8 sparks: 45 deg)
    # so the last drawing hands straight back to the first.
    "SpinBlades": ([(0, 1.0, 0, .7), (11.25, 1.08, 0, .8), (22.5, 1.25, 1, 1.), (33.75, 1.05, 0, .85),
                    (45, .95, 0, .8), (56.25, 1.0, 0, .75), (67.5, 1.06, 0, .8), (78.75, .98, 0, .75)],
                   [2, 2, 1, 2, 2, 2, 2, 2]),
    "SpinVortex": ([(0, 1.0, 0, .7), (5.625, 1.05, 0, .8), (11.25, 1.2, 1, 1.), (16.875, 1.05, 0, .85),
                    (22.5, 1.0, 0, .8), (28.125, .95, 0, .75), (33.75, 1.0, 0, .8), (39.375, 1.0, 0, .75)],
                   [2, 2, 1, 2, 2, 2, 2, 2]),
}


def rng(*k):
    """Deterministic per-frame randomness (string seeds are stable across runs)."""
    return random.Random("/".join(map(str, k)))


def op(v):
    return f'opacity="{max(0.0, min(1.0, v)):.2f}"'


def glowpoly(points, colour, a, filt="glowS"):
    return poly(points, colour, f'{op(a)} filter="url(#{filt})"')


def lerp(a, b, t):
    return a + (b - a) * t


def ribbon(cx, top, L, w, n, half=lambda t: 1 - t, off=lambda t, k: 0.0, tip=True):
    """A tapering flame silhouette sampled at n stations: half(t) is the half
    width (x w) at t in 0..1 down the length, off(t, k) shifts station k
    sideways (zig-zags, waves). Straight segments only -> chamfered edges."""
    left, right = [], []
    for k in range(n + 1):
        t = k / n
        y = top + L * t
        hw = w * half(t)
        o = off(t, k)
        left.append((cx - hw + o, y))
        right.append((cx + hw + o, y))
    if tip:
        right[-1] = left[-1] = ((left[-1][0] + right[-1][0]) / 2, top + L)
    return left + list(reversed(right))


def diamond(x, y, rx, ry):
    return [(x, y - ry), (x + rx, y), (x, y + ry), (x - rx, y)]


def speed_lines(cx, top, L, colour, a=.8, spread=11, count=4, w=1.2):
    out = ""
    for k in range(count):
        x = cx + (spread + (k // 2) * 2.5) * (-1 if k % 2 == 0 else 1)
        y0 = top + 12 + k * 8
        out += line([(x, y0), (x, y0 + 0.42 * L + k * 6)], w, colour, op(a))
    return out


def hexagon(x, y, r, rot=0):
    return ngon(x, y, r, 6, rot)


# ------------------------------------------------------------- plume styles --
# Each: (frame row, boost?, palette, frame index) -> (glow-back svg, body svg).
# Boost drawings run longer and hotter: wider core, more light, extra kicks.

def comet(fr, b, p, i):
    """Neon Comet keeps the Akira tail-light: red streak, sodium middle, bone
    core, hard shock diamond; the smear adds cyan speed lines."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    w = 9.0 * wm * (1.1 if b else 1)
    outer = [(cx - w, top), (cx + w, top), (cx + w * .55, top + L * .55), (cx, top + L), (cx - w * .55, top + L * .55)]
    mid = [(cx - w * .62, top), (cx + w * .62, top), (cx + w * .3, top + L * .5), (cx, top + L * .78),
           (cx - w * .3, top + L * .5)]
    cw = .42 if b else .3
    core = [(cx - w * cw, top), (cx + w * cw, top), (cx, top + L * (.58 if b else .45))]
    back = glowpoly(outer, p.outer, (.75 if b else .5) * flare)
    body = poly(outer, p.outer) + poly(mid, p.mid) + poly(core, p.core)
    for j, f0 in enumerate((.22, .5) if b else (.22 if i % 2 == 0 else .3,)):
        dy = top + L * f0
        s = 1.3 if b else 1
        body += poly(diamond(cx, dy, 3 * s, 5 * s), p.core)
    if smear or b:
        body += speed_lines(cx, top, L, p.accent, .8 if smear else .45)
    if smear:
        body += poly([(cx - 2.4, top + 22), (cx + 2.4, top + 22), (cx, top + L + 4)], RED_HI, op(.85))
    return back, body


def volt(fr, b, p, i):
    """Volt Viper: a crackling zig-zag ribbon with a bone bolt down its spine
    and sodium spark ticks jumping off the corners."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    w = 8.5 * wm * (1.12 if b else 1)
    n = 7
    flip = 1 if i % 2 == 0 else -1
    zig = lambda t, k: (2.2 if k % 2 else -2.2) * flip * (0.4 + t) if 0 < k < n else 0
    outer = ribbon(cx, top, L, w, n, half=lambda t: (1 - t) ** .8, off=zig)
    mid = ribbon(cx, top, L * .8, w * .6, n, half=lambda t: (1 - t) ** .8, off=zig)
    bolt = [(cx + ((2.6 if k % 2 else -2.6) * flip * (0.3 + k / n) if 0 < k < n else 0), top + L * .82 * k / n)
            for k in range(n + 1)]
    back = glowpoly(outer, p.mid, (.8 if b else .55) * flare)
    body = poly(outer, p.outer) + poly(mid, p.mid) + line(bolt, 2.6 if b else 1.8, p.core)
    r = rng("volt", i, b)
    for k in range(1, n, 2 if not b else 1):
        x, y = bolt[k]
        d = 1 if x > cx else -1
        L2 = r.uniform(4, 7) * (1.3 if b else 1)
        body += line([(x, y), (x + d * L2, y + 2), (x + d * (L2 - 2), y + 5)], 1.2, p.accent)
    if smear:
        body += speed_lines(cx, top, L, p.mid, .8)
        ghost = [(x - 4 * flip, y + 6) for x, y in bolt]
        body += line(ghost, 1.2, p.core, op(.5))
    return back, body


def solar(fr, b, p, i):
    """Solar Fang: three flaring solar flame tongues -- a long centre fang and
    two side tongues that kick outward and alternate in length."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    w = 9.0 * wm * (1.08 if b else 1)
    alt = 1 if i % 2 == 0 else -1
    body, back = "", ""
    for side in (-1, 1):
        k = .62 + .14 * side * alt
        Ls = L * k
        x0 = cx + side * w * .45
        tongue = [(x0 - 4.5, top), (x0 + 4.5, top), (x0 + side * (4 + 2 * k), top + Ls * .55),
                  (x0 + side * (6 + 3 * k), top + Ls), (x0 + side * 1, top + Ls * .5)]
        if side < 0:
            tongue = [(x0 - 4.5, top), (x0 + 4.5, top), (x0 - 1, top + Ls * .5), (x0 - 6 - 3 * k, top + Ls),
                      (x0 - 4 - 2 * k, top + Ls * .55)]
        back += glowpoly(tongue, p.outer, .5 * flare)
        body += poly(tongue, p.dark if not b else p.outer)
        inner = xf(tongue, cx=x0, cy=top, sx=.55, sy=.7)
        body += poly(inner, p.outer if not b else p.mid)
    main = [(cx - w * .75, top), (cx + w * .75, top), (cx + w * .5, top + L * .35), (cx + w * .22, top + L * .8),
            (cx, top + L), (cx - w * .3, top + L * .62), (cx - w * .55, top + L * .3)]
    mid = xf(main, cx=cx, cy=top, sx=.62, sy=.72)
    core = xf(main, cx=cx, cy=top, sx=.3 if not b else .42, sy=.42 if not b else .55)
    back += glowpoly(main, p.mid, (.75 if b else .55) * flare)
    body += poly(main, p.outer) + poly(mid, p.mid) + poly(core, p.core)
    r = rng("solar", i, b)
    for k in range(3 if not b else 5):      # flare chips breaking off the tongues
        y = top + L * r.uniform(.45, .95)
        x = cx + r.uniform(-9, 9)
        s = r.uniform(1.4, 2.4)
        body += poly([(x, y - s), (x + s * .8, y + s), (x - s * .8, y + s)], p.accent if k % 2 else p.mid)
    if smear:
        body += speed_lines(cx, top, L, p.mid, .7)
    return back, body


def halo(fr, b, p, i):
    """Crimson Halo: a rail-gun exhaust -- a hard narrow spike threaded with
    capacitor halo rings that march down the plume and shrink."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    w = 6.5 * wm * (1.15 if b else 1)
    outer = [(cx - w, top), (cx + w, top), (cx + w * .45, top + L * .6), (cx, top + L), (cx - w * .45, top + L * .6)]
    mid = xf(outer, cx=cx, cy=top, sx=.6, sy=.8)
    core = [(cx - 1.6 * (1.4 if b else 1), top), (cx + 1.6 * (1.4 if b else 1), top), (cx, top + L * .7)]
    back = glowpoly(outer, p.outer, (.75 if b else .5) * flare)
    body = poly(outer, p.outer) + poly(mid, p.mid) + poly(core, p.core)
    phase = (i % 3) / 3.0
    count = 4 if b else 3
    for k in range(count):
        t = (k + phase) / (count + .3)
        y = top + 8 + t * L * .8
        rx = (10.5 if b else 9.5) * (1 - t * .65)
        ry = rx * .32
        ring = ngon(cx, y, rx, 8, 22.5, sy=ry / rx)
        body += inkpoly(ring, 2.2 if b else 1.7, p.mid if k % 2 == 0 else p.accent)
    if smear:
        body += speed_lines(cx, top, L, p.accent, .8, spread=9)
    return back, body


def ion(fr, b, p, i):
    """Ion Lancer: a dead-straight ion lance -- a narrow chamfered beam with
    bone pulse nodes riding down it."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    w = 5.2 * wm * (1.2 if b else 1)
    outer = [(cx - w, top), (cx + w, top), (cx + w * .8, top + L * .8), (cx, top + L), (cx - w * .8, top + L * .8)]
    mid = [(cx - w * .55, top), (cx + w * .55, top), (cx + w * .4, top + L * .78), (cx, top + L * .92),
           (cx - w * .4, top + L * .78)]
    back = glowpoly(outer, p.mid, (.85 if b else .55) * flare, "glowS")
    body = poly(outer, p.outer) + poly(mid, p.mid) + line([(cx, top), (cx, top + L * .82)], 1.6 if not b else 2.4, p.core)
    # side fins of light at the nozzle (the lance's focusing lens)
    for s in (-1, 1):
        body += poly([(cx + s * w, top), (cx + s * (w + 3.5), top + 2), (cx + s * w * .9, top + 9)], p.accent)
    step = (i % 3) / 3.0
    for k in range(4 if not b else 5):
        y = top + L * (.12 + (k + step) * .19)
        if y > top + L * .9:
            continue
        s = 1.0 - (y - top) / L * .5
        body += poly(diamond(cx, y, 3.2 * s * (1.25 if b else 1), 4.5 * s), p.core)
    if smear:
        body += speed_lines(cx, top, L, p.mid, .85, spread=8)
    return back, body


def ghost(fr, b, p, i):
    """Jade Phantom: a wispy ghost trail -- a waving translucent ribbon that
    sheds angular wisps off its tail."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    w = 8.5 * wm * (1.12 if b else 1)
    ph = i * 0.9
    wave = lambda t, k: math.sin(ph + t * 5.0) * 4.0 * t
    outer = ribbon(cx, top, L, w, 8, half=lambda t: (1 - t) ** .6, off=wave)
    mid = ribbon(cx, top, L * .72, w * .55, 8, half=lambda t: (1 - t) ** .7,
                 off=lambda t, k: math.sin(ph + t * 3.6) * 2.9 * t)
    back = glowpoly(outer, p.mid, (.65 if b else .4) * flare)
    body = poly(outer, p.outer, op(.82)) + poly(mid, p.mid, op(.9))
    body += poly([(cx - 2.2, top), (cx + 2.2, top), (cx + math.sin(ph + 1.4) * 1.2, top + L * (.42 if b else .3))], p.core)
    r = rng("ghost", i, b)
    for k in range(2 if not b else 3):          # detached wisps (angular commas)
        y = top + L * (.55 + k * .17) + r.uniform(-3, 3)
        x = cx + math.sin(ph + (y - top) / L * 5.0) * 4 * ((y - top) / L) + (7 if k % 2 else -7)
        s = r.uniform(2.6, 3.6)
        d = 1 if k % 2 else -1
        wisp = [(x, y - s), (x + d * s * .9, y), (x + d * s * .3, y + s * 1.8), (x - d * s * .5, y + s * .3)]
        body += poly(wisp, p.mid if k % 2 else p.outer, op(.75))
    if smear:
        body += speed_lines(cx, top, L, p.accent, .6)
    return back, body


def afterburner(fr, b, p, i):
    """Gold Warden: chunky afterburner cones -- a wide blunt cone stacked with
    hard Mach shock diamonds, a red ring at the throat."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    w = 11.0 * wm * (1.08 if b else 1)
    outer = [(cx - w, top), (cx + w, top), (cx + w * .78, top + L * .45), (cx + w * .35, top + L * .88),
             (cx, top + L), (cx - w * .35, top + L * .88), (cx - w * .78, top + L * .45)]
    mid = xf(outer, cx=cx, cy=top, sx=.68, sy=.82)
    back = glowpoly(outer, p.outer, (.8 if b else .6) * flare)
    body = poly(outer, p.outer) + poly(mid, p.mid)
    body += poly([(cx - w * .78, top), (cx + w * .78, top), (cx + w * .6, top + 4), (cx - w * .6, top + 4)], p.accent)
    n = 3 if not b else 4
    shift = (i % 2) * 3
    for k in range(n):
        y = top + 12 + shift + k * L * .2
        s = (1 - k * .18) * (1.2 if b else 1)
        body += poly(diamond(cx, y, 5.5 * s, 6 * s), p.core)
        body += poly(diamond(cx, y, 2.6 * s, 3 * s), p.mid)
    if smear:
        body += speed_lines(cx, top, L, p.core, .7, spread=12)
    return back, body


def forked(fr, b, p, i):
    """Lightning: forked lightning sparks -- a main bolt down a faint
    yellow plume, two or three forks splitting off, cyan spark chips."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    r = rng("fork", i, b)
    w = 8.0 * (1.1 if b else 1)
    haze = [(cx - w, top), (cx + w, top), (cx + w * .4, top + L * .6), (cx, top + L * .9), (cx - w * .4, top + L * .6)]
    back = glowpoly(haze, p.outer, (.75 if b else .5) * flare)
    body = poly(haze, p.dark, op(.55)) + poly(xf(haze, cx=cx, cy=top, sx=.55, sy=.7), p.outer, op(.6))
    pts_ = [(cx, top)]
    n = 7
    for k in range(1, n + 1):
        pts_.append((cx + r.uniform(-4.5, 4.5) * (k / n + .3), top + L * k / n))
    body += line(pts_, 4.2 if b else 3.2, p.outer) + line(pts_, 1.8 if b else 1.2, p.core)
    for f in range(2 if not b else 3):
        k = r.randint(2, n - 2)
        x, y = pts_[k]
        d = r.choice((-1, 1))
        fork = [(x, y), (x + d * r.uniform(4, 7), y + r.uniform(6, 10)), (x + d * r.uniform(7, 12), y + r.uniform(14, 22))]
        body += line(fork, 2.4, p.outer) + line(fork, .9, p.core)
    for k in range(3 if not b else 5):
        x, y = cx + r.uniform(-11, 11), top + L * r.uniform(.15, .8)
        body += poly(diamond(x, y, 1.3, 2.2), p.mid)
    if smear:
        body += speed_lines(cx, top, L, p.mid, .8)
    return back, body


def blowtorch(fr, b, p, i):
    """Ligher: a blowtorch -- a tight orange jet with the hard cyan inner cone
    of a torch at full pressure, a bone needle at its tip."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    w = 6.4 * wm * (1.1 if b else 1)
    outer = [(cx - w, top), (cx + w, top), (cx + w * .78, top + L * .5), (cx + w * .3, top + L * .85),
             (cx, top + L), (cx - w * .3, top + L * .85), (cx - w * .78, top + L * .5)]
    mid = xf(outer, cx=cx, cy=top, sx=.66, sy=.8)
    jitter = (i % 3 - 1) * 1.0
    cone = [(cx - w * .5, top), (cx + w * .5, top), (cx + jitter * .3, top + 26 + (8 if b else 0) + jitter * 2)]
    back = glowpoly(outer, p.outer, (.8 if b else .55) * flare)
    body = poly(outer, p.outer) + poly(mid, p.mid)
    body += glowpoly(cone, p.accent, .8) + poly(cone, p.accent)
    body += poly(xf(cone, cx=cx, cy=top, sx=.45, sy=.6), p.core)
    body += poly(diamond(cx, top + L * .52, 2.2, 3.6), p.core)
    if smear:
        body += speed_lines(cx, top, L, p.accent, .75, spread=9)
    return back, body


def pods(fr, b, p, i):
    """Paranoid: twin pod jets -- each pod fires two short parallel jets
    through a hex iris (the watching eye) that blinks open and shut."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    body, back = "", ""
    w = 4.4 * wm * (1.12 if b else 1)
    for s in (-1, 1):
        x = cx + s * 4.6
        Ls = L * (1 if s > 0 else .9) if i % 2 == 0 else L * (.9 if s > 0 else 1)
        jet = [(x - w, top), (x + w, top), (x + w * .5, top + Ls * .6), (x, top + Ls), (x - w * .5, top + Ls * .6)]
        back += glowpoly(jet, p.mid, (.75 if b else .5) * flare)
        body += poly(jet, p.outer) + poly(xf(jet, cx=x, cy=top, sx=.55, sy=.75), p.mid)
        body += poly([(x - w * .3, top), (x + w * .3, top), (x, top + Ls * (.45 if b else .32))], p.core)
    open_ = .55 + .45 * abs(math.cos(i * 1.1))
    iris = ngon(cx, top + 5, 9.5 * (1.1 if b else 1), 6, 30, sy=.42 * open_)
    body += inkpoly(iris, 1.8, p.mid) + poly(diamond(cx, top + 5, 2.0, 2.0 * open_ + .4), p.accent)
    for k in range(2 if not b else 3):
        y = top + 18 + k * 14 + (i % 2) * 5
        body += line([(cx - 6, y), (cx, y + 4), (cx + 6, y)], 1.4, p.core, op(.8))
    if smear:
        body += speed_lines(cx, top, L, p.mid, .8, spread=12)
    return back, body


def gatling(fr, b, p, i):
    """Saboteur: smoky gatling exhaust -- a stuttering muzzle flash at the
    nozzle and a column of angular fire puffs cooling into dusk smoke cels."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    w = 8.0 * wm * (1.1 if b else 1)
    hot = i % 2 == 0
    muzzle = star(cx, top + 10, (9 if hot else 7) * (1.15 if b else 1), 4, 4 if hot else 5, rot=45 if hot else 0)
    jet = [(cx - w * .8, top), (cx + w * .8, top), (cx + w * .45, top + L * .4), (cx, top + L * .58),
           (cx - w * .45, top + L * .4)]
    back = glowpoly(muzzle, p.mid, (.85 if b else .6) * flare) + glowpoly(jet, p.outer, .55 * flare)
    body = poly(jet, p.outer) + poly(xf(jet, cx=cx, cy=top, sx=.55, sy=.75), p.mid)
    shift = (i % 4) * 4
    puffs = 6 if b else 5
    for k in range(puffs):
        y = top + 16 + k * (L - 22) / puffs + shift * (1 - k / puffs)
        t = (y - top) / L
        r = (9.0 - t * 4.0) * (1.1 if b else 1)
        x = cx + (3 if k % 2 else -3) * t
        oct_ = ngon(x, y, r, 8, 22.5 + k * 9)
        if t < .45:         # hot puffs: purple fire with a sodium heart
            back += glowpoly(oct_, p.outer, .5 * flare)
            body += poly(oct_, p.outer) + poly(ngon(x, y, r * .55, 8, 22.5), p.mid)
        else:               # cooling into hard dusk smoke cels
            body += poly(oct_, p.accent) + poly(ngon(x - r * .25, y - r * .25, r * .5, 8, 22.5), p.dark)
            body += poly(ngon(x + r * .3, y + r * .3, r * .3, 8, 22.5), INDIGO_0)
    body += poly(muzzle, p.outer) + poly(xf(muzzle, cx=cx, cy=top + 10, sx=.6, sy=.6), p.mid)
    body += poly(xf(muzzle, cx=cx, cy=top + 10, sx=.3, sy=.3), p.core)
    if smear:
        body += speed_lines(cx, top, L, p.mid, .8)
    return back, body


def feathers(fr, b, p, i):
    """Dove: feathery streaks -- a slim light streak flanked by angular
    feather vanes in a chevron, amber tips, the vanes fanning on the beat."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    w = 4.8 * wm * (1.15 if b else 1)
    spine = [(cx - w, top), (cx + w, top), (cx + w * .4, top + L * .6), (cx, top + L), (cx - w * .4, top + L * .6)]
    back = glowpoly(spine, p.mid, (.75 if b else .5) * flare)
    body = ""
    fan = .8 + .25 * math.sin(i * 1.3)
    rows = 3 if not b else 4
    for k in range(rows):
        y0 = top + 6 + k * L * .17
        flen = (L * .42) * (1 - k * .13)
        for s in (-1, 1):
            spread = math.radians((14 + 10 * fan) * (1 - k * .12))
            ex, ey = cx + s * (2.5 + math.sin(spread) * flen), y0 + math.cos(spread) * flen
            bx, by = cx + s * 2.5, y0
            vane = [(bx, by), (lerp(bx, ex, .45) + s * 3.2, lerp(by, ey, .4)),
                    (ex, ey), (lerp(bx, ex, .5) - s * .6, lerp(by, ey, .55))]
            body += poly(vane, p.outer, op(.9))
            body += line([(cx + s * 2.5, y0), (ex, ey)], 1.0, p.mid)
            body += poly(diamond(ex, ey, 1.4, 2.4), p.accent)
    body += poly(spine, p.outer) + poly(xf(spine, cx=cx, cy=top, sx=.55, sy=.8), p.mid)
    body += poly([(cx - 1.6, top), (cx + 1.6, top), (cx, top + L * (.55 if b else .42))], p.core)
    if smear:
        body += speed_lines(cx, top, L, p.mid, .7, spread=12)
    return back, body


def hexjet(fr, b, p, i):
    """Turtle: an angular bubble jet -- a teal jet that sheds hard-edged hex
    cells (shell plates), each one a flat ring, never a round bubble."""
    L, wm, smear, flare = fr
    cx, top = PW / 2, HEAD
    w = 9.5 * wm * (1.1 if b else 1)
    jet = [(cx - w, top), (cx + w, top), (cx + w * .5, top + L * .42), (cx, top + L * .62), (cx - w * .5, top + L * .42)]
    back = glowpoly(jet, p.outer, (.75 if b else .5) * flare)
    body = poly(jet, p.outer) + poly(xf(jet, cx=cx, cy=top, sx=.6, sy=.78), p.mid)
    body += poly([(cx - w * .28, top), (cx + w * .28, top), (cx, top + L * (.38 if b else .28))], p.core)
    r = rng("hex", i, b)
    cells = 5 if b else 4
    for k in range(cells):
        y = top + L * (.3 + .7 * (k + (i % 3) / 3.0) / cells)
        if y > top + L:
            continue
        t = (y - top) / L
        rr = (8.4 - t * 3.4) * (1.1 if b else 1)
        x = cx + r.uniform(-5, 5) * t
        h = hexagon(x, y, rr, 30)
        back += glowpoly(h, p.outer, .4 * flare)
        body += poly(h, p.outer) + poly(hexagon(x, y, rr * .62, 30), p.mid) + inkpoly(hexagon(x, y, rr * .62, 30), 1.0, p.dark)
        body += poly([(x - rr * .5, y - rr * .2), (x - rr * .1, y - rr * .55), (x + rr * .05, y - rr * .4), (x - rr * .35, y - rr * .05)], p.core)
    if smear:
        body += speed_lines(cx, top, L, p.mid, .75)
    return back, body


# ---------------------------------------------------------- spinner drift --
# Spinner canvas: 64 u square, centre (32, 32). The hull's rim sits at about
# r = 26 u when the ring is sized to the hull (ShipExhaustStyle.RingSize).
# Screen angle a (degrees, counter-clockwise, y up) -> SVG point.
C = SW / 2


def at(a, r, cx=C, cy=C):
    return (cx + r * math.cos(math.radians(a)), cy - r * math.sin(math.radians(a)))


def arc_band(a0, a1, r_in, r_out, taper=True, step=8):
    """A crescent from angle a0 (thin tail) to a1 (fat head), straight
    segments every `step` degrees. The hull spins counter-clockwise, so a
    blade's afterimage trails clockwise of it: a0 < a1."""
    n = max(2, int(abs(a1 - a0) / step))
    outer, inner = [], []
    for k in range(n + 1):
        t = k / n
        a = lerp(a0, a1, t)
        th = (t if taper else 1)
        outer.append(at(a, lerp(r_in + (r_out - r_in) * .5, r_out, th)))
        inner.append(at(a, lerp(r_in + (r_out - r_in) * .5, r_in, th)))
    return outer + list(reversed(inner))


def spin_blades_ring(fr, b, p, i):
    """Ninja ring: four blade-arc afterimages trailing the shuriken's tips,
    each a hue crescent with a bone cutting edge and red cut nicks. The ring
    is parented to the spinning hull, so the arcs stay on the blade tips; the
    flipbook pulses their sweep (snap long on the smear)."""
    ph, reach, smear, flare = fr
    sweep = (64 if not smear else 140) * reach * (1.2 if b else 1)
    back, body = "", ""
    r_out, r_in = 28.5, 20.5
    for k in range(4):
        tip = 90 * k + 3
        band = arc_band(tip - sweep, tip, r_in, r_out)
        back += glowpoly(band, p.mid, (.75 if b else .5) * flare)
        if smear:      # ghosted second blade one step back
            body += poly(arc_band(tip - sweep - 30, tip - sweep * .55, 15, 21), p.outer, op(.45))
        body += poly(band, p.outer, op(.88))
        body += poly(arc_band(tip - sweep * .6, tip, r_out - (5.2 if b else 4.2), r_out, step=6), p.mid)
        body += poly(arc_band(tip - sweep * .3, tip, r_out - (3.0 if b else 2.2), r_out, step=6), p.core)
        for n in range(1 if not b else 2):
            a = tip - sweep * (.3 + n * .2) - (i % 2) * 6
            x0, y0 = at(a, r_out + 1.2)
            x1, y1 = at(a - 7, r_out - 5)
            body += line([(x0, y0), (x1, y1)], 1.3, p.accent)
    return back, body


def curl(x0, y0, side, drop, turns, r0, ph, n=18):
    """A cut streak: falls `drop` u, then hooks into a tightening spiral of
    `turns` turns (radius r0 -> r0/4) curling towards `side`."""
    pts_ = [(x0, y0)]
    yb = y0 + drop
    pts_.append((x0 + side * 1.5, lerp(y0, yb, .5)))
    for s in range(n + 1):
        t = s / n
        # start on the streak (left of the centre for side +1), keep falling
        a = (math.pi if side > 0 else 0.0) - side * (t * turns * 2 * math.pi + ph)
        r = r0 * (1 - .75 * t)
        cx_ = x0 + side * r0
        pts_.append((cx_ + math.cos(a) * r, yb + math.sin(a) * r))
    return pts_


def spin_blades_wake(fr, b, p, i):
    """Ninja wake: shuriken-trail streaks that curl -- cut marks peeling off
    the star, falling behind and hooking into tight spirals."""
    ph, reach, smear, flare = fr
    back, body = "", ""
    drop = (22 if not b else 28) * reach
    for k in range(3):
        side = (-1, 1, -1)[k] if i % 2 == 0 else (1, -1, 1)[k]
        x0 = C + (-9, 9, 0)[k] * (1 if i % 2 == 0 else -1)
        y0 = WAKE_HEAD + 1 + k * 6
        r0 = (7.5 - k * 1.5) * (1.15 if b else 1)
        pts_ = curl(x0, y0, side, drop - k * 3, 1.15, r0, math.radians(ph * 2 + k * 40))
        w = (2.8 if b else 2.2) * (1.25 if smear else 1)
        back += line(pts_, w * 2.2, p.mid, f'{op(.45 * flare)} filter="url(#glowS)"')
        body += line(pts_, w, p.outer) + line(pts_[:4], w * .45, p.core)
        hx, hy = pts_[2]
        body += line([(hx - 2.5 * side, hy - 1.5), (hx + 2.5 * side, hy + 1.5)], 1.2, p.accent)
    if smear:
        for k in range(4):
            x = C + (-10 + k * 7)
            body += line([(x, WAKE_HEAD + 6), (x, WAKE_HEAD + 6 + drop * 1.3)], 1.0, p.mid, op(.7))
    return back, body


def spin_vortex_ring(fr, b, p, i):
    """UFO ring: an orbiting ring of light pulses -- eight sparks on a thin
    ring, a chase of brighter ones, each dragging a short arc tail. The
    sparks advance an eighth of the ring per loop, so the loop is seamless."""
    ph, reach, smear, flare = fr
    back, body = "", ""
    R = 27.5
    ring = [at(a, R) for a in range(0, 360, 15)]
    body += inkpoly(ring, 1.4 if not b else 2.2, p.outer, op(.8))
    lead = i % 4
    for k in range(8):
        a = k * 45 + ph
        hot = (k % 4) == lead
        tail = (30 if hot else 16) * reach * (1.4 if b else 1) * (2.2 if smear else 1)
        band = arc_band(a - tail, a, R - 2.2, R + 2.2, step=5)
        body += poly(band, p.mid if hot else p.outer, op(.9))
        x, y = at(a, R)
        s = (3.2 if hot else 2.1) * (1.2 if b else 1)
        back += glowpoly(diamond(x, y, s * 1.6, s * 1.6), p.accent if hot else p.mid, .8 * flare)
        body += poly(diamond(x, y, s, s), p.accent if hot else p.mid)
        body += poly(diamond(x, y, s * .45, s * .45), p.core)
    return back, body


def spin_vortex_wake(fr, b, p, i):
    """UFO wake: a tractor-beam shimmer straight under the disc (scan bars
    marching down a pale cyan cone), then a violet vortex funnel: stacked
    elliptical swirl arcs, narrowing as they fall behind, turning per frame."""
    ph, reach, smear, flare = fr
    back, body = "", ""
    L = (54 if not b else 58) * reach
    L = min(L, SW - WAKE_HEAD - 2)
    top = WAKE_HEAD
    bl = L * .42
    beam = [(C - 9, top), (C + 9, top), (C + 15, top + bl), (C - 15, top + bl)]
    back += glowpoly(beam, p.accent, (.4 if b else .28) * flare)
    body += poly(beam, p.accent, op(.16 if not b else .24))
    bars = 3 if not b else 4
    for k in range(bars):
        t = (k + (i % 4) / 4.0) / bars
        y = top + 2 + t * (bl - 3)
        hw = 9 + 6 * t
        body += line([(C - hw, y), (C + hw, y)], 1.3 if not b else 1.7, p.accent, op(.9 - t * .5))
    rings = 5 if not b else 6
    for k in range(rings):
        t = k / (rings - 1)
        y = top + bl * .55 + (L - bl * .55 - 3) * t
        rx = (17 - 12 * t) * (1.08 if b else 1)
        ry = rx * .3
        a0 = ph * 8 + k * 50
        pts_ = []
        for s in range(9):
            a = math.radians(a0 + s * 25)    # a 200 degree sweep of the swirl
            pts_.append((C + math.cos(a) * rx + math.sin(t * 3 + i) * 1.5, y + math.sin(a) * ry))
        w = (2.6 - 1.2 * t) * (1.25 if b else 1) * (1.3 if smear else 1)
        back += line(pts_, w * 2, p.mid, f'{op(.4 * flare)} filter="url(#glowS)"')
        body += line(pts_, w, p.outer if k % 2 else p.mid)
        hx, hy = pts_[-1]
        body += poly(diamond(hx, hy, 1.2 + w * .3, 1.2 + w * .3), p.core)
    if smear:
        for x in (C - 12, C + 12):
            body += line([(x, top + bl), (x + (3 if x > C else -3), top + L)], 1, p.mid, op(.6))
    return back, body


PLUME = {
    "CometTrail": comet, "VoltZigzag": volt, "SolarTongues": solar, "HaloRings": halo, "IonLance": ion,
    "GhostWisp": ghost, "Afterburner": afterburner, "ForkedLightning": forked, "Blowtorch": blowtorch,
    "PodJets": pods, "GatlingSmoke": gatling, "FeatherStreaks": feathers, "HexJet": hexjet,
}
SPIN = {
    "SpinBlades": (spin_blades_ring, spin_blades_wake),
    "SpinVortex": (spin_vortex_ring, spin_vortex_wake),
}


def boosted(fr):
    """Boost drawings: longer (capped at the canvas), a touch wider, hotter."""
    L, wm, smear, flare = fr
    return (min(124.0, L * 1.12 + 4), wm, smear, min(1.0, flare + .25))


def render(svgtext, name, zoom):
    p = os.path.join(BUILD, name + ".svg")
    with open(p, "w") as fh:
        fh.write(svgtext)
    png = p[:-4] + ".png"
    subprocess.run(["resvg", "--zoom", str(zoom), p, png], check=True)
    return Image.open(png).convert("RGBA")


def build_strips():
    """-> list of (ship id, layer name, [frame images])"""
    strips = []
    for sid, key, kind, pal in STYLES:
        frames, _ = FRAMES[kind]
        if kind in SPINNERS:
            ring_fn, wake_fn = SPIN[kind]
            for layer, fn in (("Ring", ring_fn), ("Wake", wake_fn)):
                for b in (False, True):
                    imgs = []
                    for i, fr in enumerate(frames):
                        back, body = fn(fr, b, pal, i)
                        doc = svg(SW, SW, f'<g id="glow-back">{back}</g><g id="drift">{body}</g>',
                                  f"{key} {layer.lower()}{' boost' if b else ''} frame {i}")
                        imgs.append(render(doc, f"{key}_{layer}{'B' if b else ''}_{i}", SZOOM))
                    strips.append((sid, layer + ("Boost" if b else ""), imgs))
        else:
            fn = PLUME[kind]
            for b in (False, True):
                imgs = []
                for i, fr in enumerate(frames):
                    back, body = fn(boosted(fr) if b else fr, b, pal, i)
                    doc = svg(PW, PH, f'<g id="glow-back">{back}</g><g id="plume">{body}</g>',
                              f"{key} plume{' boost' if b else ''} frame {i}")
                    imgs.append(render(doc, f"{key}_Plume{'B' if b else ''}_{i}", PZOOM))
                strips.append((sid, "Plume" + ("Boost" if b else ""), imgs))
    return strips


def pack(strips):
    """Shelf-pack each strip (frames side by side, GUTTER apart) into rows of
    ATLAS_W. Tallest strips first so each shelf wastes little."""
    order = sorted(range(len(strips)), key=lambda k: (-strips[k][2][0].size[1], k))
    placed = {}
    shelves = []                      # [y, height, x used]
    y_next = 0
    for k in order:
        imgs = strips[k][2]
        fw, fh = imgs[0].size
        sw = len(imgs) * (fw + GUTTER)
        for sh in shelves:            # first shelf it fits on (same height class)
            if sh[1] >= fh and sh[1] - fh < 40 and sh[2] + sw <= ATLAS_W:
                placed[k] = (sh[2], sh[0])
                sh[2] += sw
                break
        else:
            shelves.append([y_next, fh, sw])
            placed[k] = (0, y_next)
            y_next += fh + GUTTER
    y, shelf = y_next, 0
    height = y + shelf
    height = (height + 63) // 64 * 64
    atlas = Image.new("RGBA", (ATLAS_W, height), (0, 0, 0, 0))
    rects = []
    for k, (sid, layer, imgs) in enumerate(strips):
        px, py = placed[k]
        fw, fh = imgs[0].size
        for i, im in enumerate(imgs):
            atlas.alpha_composite(im, (px + i * (fw + GUTTER), py))
        # Unity rects are bottom-up
        rects.append((sid, layer, px, height - py - fh, fw, fh, len(imgs), fw + GUTTER))
    return atlas, rects


def hexs(c):
    return f'"{c.upper()}"'


def write_cs(rects):
    with open(CS) as fh:
        src = fh.read()
    lines = []
    for sid, key, kind, pal in STYLES:
        _, ticks = FRAMES[kind]
        lines.append(f"        /* {sid:2d} {key:<12} */ Row({sid}, \"{key}\", ExhaustKind.{kind}, "
                     f"{hexs(pal.outer)}, {hexs(pal.mid)}, {hexs(pal.core)}, {hexs(pal.dark)}, {hexs(pal.accent)}, "
                     f"new[] {{ {', '.join(map(str, ticks))} }}),")
    lines.append("    };")
    lines.append("")
    lines.append("    // Atlas strips: ship, layer, x, y (bottom-up), frame w, h, frames, stride (px).")
    lines.append("    static readonly Strip[] strips =")
    lines.append("    {")
    for sid, layer, x, y, w, h, n, stride in rects:
        lines.append(f"        new Strip({sid}, ExhaustLayer.{layer}, {x}, {y}, {w}, {h}, {n}, {stride}),")
    block = "\n".join(lines)
    new = re.sub(r"(// BEGIN GENERATED EXHAUST\n).*?(\n\s*// END GENERATED EXHAUST)",
                 lambda m: m.group(1) + block + m.group(2), src, flags=re.S)
    with open(CS, "w") as fh:
        fh.write(new)


def main():
    os.makedirs(BUILD, exist_ok=True)
    strips = build_strips()
    atlas, rects = pack(strips)
    os.makedirs(OUT, exist_ok=True)
    atlas.save(os.path.join(OUT, "exhaust_atlas.png"), optimize=True)
    write_cs(rects)
    print("exhaust atlas", atlas.size, "strips", len(rects),
          "plume head pivot y =", 1 - HEAD / PH, "wake head pivot y =", 1 - WAKE_HEAD / SW)


if __name__ == "__main__":
    main()
