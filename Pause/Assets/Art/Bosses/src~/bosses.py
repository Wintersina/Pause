#!/usr/bin/env python3
"""Parametric SVG source for the four end-of-level bosses.

Style (docs/art-style.md): flat 80s TV-anime cels in the Akira palette. Every
solid form is a flat base, ONE hard shadow tone (lower right: light comes from
the upper left) and ONE highlight kick, wrapped in a thick INK contour. Shapes
are angular and panel-lined. Only lights glow (eyes, engines, cores, maws).
Bosses are enemies, so they are cold / sickly / bruised and never use the
player's Kaneda red; their lights are MAGENTA (machines) or BILE_LIGHT
(organics). Bosses are the "large set piece" class: 5 u outer contour,
2.5 u panel lines, 1.6 u detail -- authored here on a 256 u canvas, so the
strokes are doubled to stay ~3% of the longest edge.

Each boss is a list of Parts (hull, wings, jaw ...) drawn back to front. A
pose (one row of a frame table) moves, opens, flares and lights those parts;
the death frames push the same parts apart. Because the parts overlap, each
part carries its own base/shadow/highlight/ink stack (class="..."), inside
the top-level groups glow-back / body / glow / fx.

    python3 bosses.py                 write ../<World>/src~/*.svg, render with
                                      resvg, pack ../../Resources/Bosses/*.png
    python3 bosses.py --preview DIR   also write a review sheet + GIF per boss
    python3 bosses.py --only Space,Ember

Body atlas (BossArt.cs slices the same layout; keep the two in step), 5 x 4
cells of 384 px (a 256 u canvas at 1.5x), row 0 on top:
    row 0  idle 0..3 | hit
    row 1  tell0 a,b | tell1 a,b | fire
    row 2  tell2 a,b | death 0..2
    row 3  death 3..4 | retreat 0..1 | portrait
Shots atlas, 8 x 1 cells of 128 px:
    bolt 0..1 | shard 0..1 | lane telegraph | beam 0..1 | muzzle charge
Plus <World>_card.png (name card) and the shared warning.png.
"""
import math
import os
import random
import shutil
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.normpath(os.path.join(HERE, ".."))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Resources", "Bosses"))
FONT = os.path.normpath(os.path.join(HERE, "..", "..", "Orbitron", "Orbitron-Bold.ttf"))

CANVAS = 256          # boss authoring canvas (u)
BODY_PX = 384         # exported cell (1.5x)
SHOT_PX = 128
# Body atlases are 5 x 8.  The original first four rows are the shared
# encounter poses; the lower four rows give the Void Archon a richer combat
# loop (and leave room for the other bosses to receive their own sets).
BODY_COLS, BODY_ROWS = 5, 8
SHOT_COLS = 8

# Hold ticks @24fps -- BossArt.cs plays them back with the same tables.
IDLE_TICKS = [6, 3, 3, 3]
DEATH_TICKS = [2, 3, 3, 4, 6]
RETREAT_TICKS = [2, 2]
SHOT_TICKS = [2, 2]
BEAM_TICKS = [2, 2]

# ---------------------------------------------------------------- palette ----
# docs/art-samples/src/akira.py names.
NIGHT_0, NIGHT_1, INDIGO_0, INDIGO_1, DUSK = "#070A16", "#0E1424", "#1A1F45", "#2A2E6B", "#3A2A5C"
MAGENTA, MAGENTA_SH = "#FF2E88", "#8E1450"
INK, BONE, PURE = "#140C14", "#F4EAD4", "#FFFFFF"
STEEL, STEEL_SH, STEEL_HI = "#5A6A88", "#262D44", "#A3B4CC"
BRUISE, BRUISE_SH, BRUISE_HI = "#74409A", "#3A1E52", "#A86CD0"
BILE, BILE_SH, BILE_HI, BILE_LIGHT = "#8FA84E", "#3E5229", "#D4E68E", "#C8FF3A"
ROCK, ROCK_SH, ROCK_HI = "#605878", "#2C2638", "#958AA4"
GUN, GUN_SH, GUN_HI = "#2C2D40", "#1A1A28", "#5A5C78"
ICE = "#9FE8F0"       # Frost world rim; a Frost highlight / crystal tone here

# Lane colours (the backdrop tone behind the play lane), for previews.
LANE = {"Space": "#0E1424", "Frost": "#0A1A2A", "Verdant": "#0B1F1C", "Ember": "#24090E"}

OUT_W, PANEL_W, DETAIL_W = 10.0, 5.0, 3.2   # 2x the guide's 5 / 2.5 / 1.6 on a 256 u canvas
C = 128.0


# ---------------------------------------------------------------- helpers ----
def fmt(v):
    return ("%.2f" % v).rstrip("0").rstrip(".")


def pts(p):
    return " ".join("%s,%s" % (fmt(x), fmt(y)) for x, y in p)


def mirror(half):
    """half runs from a point on the axis, down the LEFT side, to a point on
    the axis; returns the full symmetric polygon."""
    return list(half) + [(2 * C - x, y) for x, y in reversed(half[1:-1])]


def mx(p):
    return [(2 * C - x, y) for x, y in p]


def shift(p, dx=0.0, dy=0.0):
    return [(x + dx, y + dy) for x, y in p]


def rot(p, deg, cx, cy):
    r = math.radians(deg)
    c, s = math.cos(r), math.sin(r)
    return [(cx + (x - cx) * c - (y - cy) * s, cy + (x - cx) * s + (y - cy) * c) for x, y in p]


def scale(p, sx, sy, cx, cy):
    return [(cx + (x - cx) * sx, cy + (y - cy) * sy) for x, y in p]


def lerp(a, b, t):
    return a + (b - a) * t


def star(cx, cy, r_out, r_in, n, seed, jitter=0.18, start=-90.0):
    rnd = random.Random(seed)
    out = []
    for i in range(n * 2):
        a = math.radians(start + i * 180.0 / n)
        r = (r_out if i % 2 == 0 else r_in) * (1.0 + rnd.uniform(-jitter, jitter))
        out.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return out


def ngon(cx, cy, r, n, start=-90.0, sx=1.0, sy=1.0):
    return [(cx + math.cos(math.radians(start + i * 360.0 / n)) * r * sx,
             cy + math.sin(math.radians(start + i * 360.0 / n)) * r * sy) for i in range(n)]


# ---------------------------------------------------------------- parts -----
class Part:
    """One rigid piece of a boss with its own cel stack and lights."""

    def __init__(self, name, anchor):
        self.name = name
        self.anchor = anchor
        self.items = []   # (layer, kind, points, colour, width, opacity)

    def base(self, p, c):
        self.items.append(("base", "fill", p, c, 0, 1))
        return self

    def shade(self, p, c):
        self.items.append(("shadow", "fill", p, c, 0, 1))
        return self

    def hi(self, p, c):
        self.items.append(("highlight", "fill", p, c, 0, 1))
        return self

    def hole(self, p, c=INK):
        """A flat INK (or dark) cut-out that stays dark in the hit flash."""
        self.items.append(("ink", "hole", p, c, 0, 1))
        return self

    def outline(self, p, w=OUT_W):
        self.items.append(("ink", "outline", p, INK, w, 1))
        return self

    def line(self, p, w=PANEL_W):
        self.items.append(("ink", "line", p, INK, w, 1))
        return self

    def light(self, p, c, glow=True, opacity=1.0):
        self.items.append(("glow", "light" if glow else "flat", p, c, 0, opacity))
        return self

    def solid(self, p, base, shadow=None, shadow_pts=None, hi=None, hi_pts=None, w=OUT_W):
        """Base + one shadow + one kick + contour in one call."""
        self.base(p, base)
        if shadow and shadow_pts:
            for sp in (shadow_pts if isinstance(shadow_pts[0], list) else [shadow_pts]):
                self.shade(sp, shadow)
        if hi and hi_pts:
            for hp in (hi_pts if isinstance(hi_pts[0], list) else [hi_pts]):
                self.hi(hp, hi)
        self.outline(p, w)
        return self


def el(kind, p, c, w, opacity, flash):
    op = "" if opacity >= 1 else ' opacity="%s"' % fmt(opacity)
    if kind == "fill":
        return '<polygon points="%s" fill="%s"%s/>' % (pts(p), BONE if flash else c, op)
    if kind == "hole":
        return '<polygon points="%s" fill="%s"%s/>' % (pts(p), INK if flash else c, op)
    if kind == "outline":
        return '<polygon points="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linejoin="round"/>' % (
            pts(p), c, fmt(w))
    if kind == "line":
        return '<polyline points="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linejoin="round" stroke-linecap="round"/>' % (
            pts(p), c, fmt(w))
    raise ValueError(kind)


def light_els(kind, p, c, opacity, flash):
    if flash:
        return [], ['<polygon points="%s" fill="%s"/>' % (pts(p), BONE)]
    back = []
    if kind == "light":
        back.append('<polygon points="%s" fill="%s" filter="url(#blur)" opacity="%s"/>' % (
            pts(p), c, fmt(0.62 * opacity)))
    op = "" if opacity >= 1 else ' opacity="%s"' % fmt(opacity)
    return back, ['<polygon points="%s" fill="%s"%s/>' % (pts(p), c, op)]


class Pose:
    def __init__(self, **kw):
        self.bob = 0.0          # vertical offset (u)
        self.sx = 1.0           # squash / stretch (drawn, about the centre)
        self.sy = 1.0
        self.phase = 0.0        # idle cycle 0..1 (flaps, blinks)
        self.lights = 0.75      # light level 0..1
        self.open = [0.0, 0.0, 0.0]   # per tell pose: how far it has opened
        self.charge = [0.0, 0.0, 0.0] # per tell pose: weapon charge glow
        self.fire = False
        self.flash = False
        self.death = -1         # 0..4
        self.smear = 0.0        # warp smear 0..1 (retreat / arrival)
        self.dx = 0.0
        for k, v in kw.items():
            setattr(self, k, v)


def lit(level, on, off):
    return on if level >= 0.5 else off


# ------------------------------------------------------------- Space ------
# VOID ARCHON: a capital carrier seen from above, prow down. Wedge hull,
# bruise armour wings, a bridge tower, a hangar bay that opens for the fan,
# a prow cannon that extends for the aimed volleys and two side lances that
# drop and charge for the lane beams. Engines along the rear (top) edge.

def space(p):
    L = lit(p.lights, MAGENTA, MAGENTA_SH)
    parts = []
    lance = p.open[2]
    for side in (0, 1):
        f = (lambda q: q) if side == 0 else mx
        ext = 14 * lance
        pr = Part("lance%d" % side, f([(26, 150)])[0])
        body = f([(38, 92), (18, 102), (8, 156), (14, 190 + ext), (26, 200 + ext), (36, 184 + ext), (46, 124)])
        pr.solid(body, STEEL,
                 STEEL_SH, f([(38, 92), (46, 124), (36, 184 + ext), (26, 200 + ext), (28, 150)]) if side == 0
                 else f([(18, 102), (8, 156), (14, 190 + ext), (26, 200 + ext), (24, 150), (26, 104)]),
                 STEEL_HI, f([(18, 104), (23, 103), (14, 156), (11, 156)]) if side == 0 else f([(38, 94), (41, 94), (46, 124), (43, 124)]))
        pr.line(f([(12, 140), (42, 132)]))
        pr.line(f([(16, 168 + ext * .5), (36, 162 + ext * .5)]), DETAIL_W)
        tip = f([(25, 186 + ext), (31, 194 + ext), (25, 202 + ext), (19, 194 + ext)])
        pr.light(tip, MAGENTA if (lance > 0.2 or p.lights >= .5) else MAGENTA_SH)
        if p.charge[2] > 0:
            pr.light(star(f([(25, 0)])[0][0], 196 + ext, 12 + 14 * p.charge[2], 5, 6, 7 + side, .1), MAGENTA)
            pr.light(ngon(f([(25, 0)])[0][0], 196 + ext, 5 + 4 * p.charge[2], 6), BONE, glow=False)
        parts.append(pr)

    # engines (rear edge)
    eng = Part("engines", (128, 46))
    flick = [0, 4, 2, 5][int(p.phase * 4) % 4]
    for x in (82, 104, 152, 174):
        noz = [(x - 9, 54), (x + 9, 54), (x + 7, 42), (x - 7, 42)]
        eng.solid(noz, STEEL_SH, w=PANEL_W * 1.2)
        eng.light([(x - 6, 41), (x + 6, 41), (x + 4, 30 - flick), (x - 4, 30 - flick)], L)
        eng.light([(x - 3, 40), (x + 3, 40), (x + 2, 34 - flick * .5), (x - 2, 34 - flick * .5)], BONE, glow=False)
    parts.append(eng)

    hull = Part("hull", (128, 120))
    half = [(128, 50), (78, 50), (46, 62), (30, 84), (42, 104), (60, 132), (84, 170), (108, 196), (128, 204)]
    H = mirror(half)
    hull.base(H, STEEL)
    hull.shade([(128, 150), (198, 130), (214, 104), (226, 84), (232, 90), (172, 170), (148, 196), (128, 204)], STEEL_SH)
    hull.shade([(84, 170), (108, 196), (128, 204), (128, 190), (110, 182)], STEEL_SH)
    hull.hi([(78, 53), (46, 65), (33, 84), (38, 86), (49, 69), (80, 58)], STEEL_HI)
    hull.hi([(62, 134), (86, 170), (90, 168), (67, 133)], STEEL_HI)
    hull.outline(H)
    hull.line([(128, 116), (128, 132)])
    hull.line([(60, 132), (104, 124)])
    hull.line(mx([(60, 132), (104, 124)]))
    hull.line([(44, 104), (90, 108)], DETAIL_W)
    hull.line(mx([(44, 104), (90, 108)]), DETAIL_W)
    for i in range(3):
        y = 140 + i * 12
        hull.hole([(70 + i * 7, y), (82 + i * 7, y - 2), (84 + i * 7, y + 3), (72 + i * 7, y + 5)])
        hull.hole(mx([(70 + i * 7, y), (82 + i * 7, y - 2), (84 + i * 7, y + 3), (72 + i * 7, y + 5)]))
    blink = int(p.phase * 4) % 2 == 0
    for q in ([(46, 72), (52, 70), (52, 76), (46, 78)], [(68, 138), (74, 136), (75, 142), (69, 144)]):
        hull.light(q, L if blink else MAGENTA_SH)
        hull.light(mx(q), MAGENTA_SH if blink else L)
    parts.append(hull)

    for side in (0, 1):
        f = (lambda q: q) if side == 0 else mx
        ar = Part("armour%d" % side, f([(80, 82)])[0])
        plate = f([(62, 56), (102, 56), (106, 92), (72, 106), (48, 88)])
        ar.base(plate, BRUISE)
        ar.shade(f([(48, 88), (72, 106), (106, 92), (104, 82), (72, 94)]) if side == 0
                 else f([(62, 56), (102, 56), (106, 92), (96, 92), (94, 62)]), BRUISE_SH)
        ar.hi(f([(64, 59), (100, 59), (99, 64), (67, 64)]) if side == 0 else f([(50, 87), (54, 87), (74, 102), (71, 104)]), BRUISE_HI)
        ar.outline(plate, OUT_W * .8)
        ar.line(f([(66, 74), (98, 70)]), DETAIL_W)
        parts.append(ar)

    br = Part("bridge", (128, 86))
    tower = mirror([(128, 58), (110, 58), (102, 74), (104, 104), (128, 116)])
    br.solid(tower, STEEL, STEEL_SH, [(128, 58), (146, 58), (154, 74), (152, 104), (128, 116)],
             STEEL_HI, [(110, 61), (104, 74), (108, 74), (113, 62)])
    br.line([(110, 94), (146, 94)], DETAIL_W)
    br.hole([(112, 76), (144, 76), (141, 86), (115, 86)])
    br.light([(115, 78), (141, 78), (139, 84), (117, 84)], L)
    parts.append(br)

    # hangar bay: doors slide apart for the fan
    hg = Part("hangar", (128, 150))
    o = p.open[1] * 15
    bay = [(104, 132), (152, 132), (146, 166), (110, 166)]
    hg.hole(bay)
    g = p.charge[1]
    hg.light([(112, 138), (144, 138), (140, 160), (116, 160)], MAGENTA if g > 0 else MAGENTA_SH, opacity=.55 + .45 * g)
    if g > 0:
        hg.light(star(128, 149, 10 + 16 * g, 5, 6, 3, .1), MAGENTA)
        hg.light(ngon(128, 149, 4 + 5 * g, 6), BONE, glow=False)
    for side in (0, 1):
        dl = [(104 - o, 132), (128 - o, 132), (128 - o, 166), (110 - o, 166)]
        door = dl if side == 0 else mx(dl)
        hg.base(door, BRUISE)
        hg.shade([(128 - o - 7, 132), (128 - o, 132), (128 - o, 166), (128 - o - 7, 166)] if side == 0
                 else mx([(110 - o, 160), (128 - o, 160), (128 - o, 166), (110 - o, 166)]), BRUISE_SH)
        hg.outline(door, PANEL_W * 1.3)
    parts.append(hg)

    cn = Part("cannon", (128, 200))
    e = p.open[0] * 11
    barrel = [(119, 176), (137, 176), (139, 206 + e), (132, 218 + e), (124, 218 + e), (117, 206 + e)]
    cn.solid(barrel, GUN_HI, GUN, [(128, 176), (137, 176), (139, 206 + e), (132, 218 + e), (128, 218 + e)],
             STEEL_HI, [(120, 180), (123, 180), (121, 204 + e), (119, 204 + e)], OUT_W * .85)
    cn.line([(118, 196 + e * .5), (138, 196 + e * .5)], DETAIL_W)
    cn.light([(128, 214 + e), (133, 220 + e), (128, 226 + e), (123, 220 + e)], L)
    if p.charge[0] > 0:
        cn.light(star(128, 222 + e, 12 + 18 * p.charge[0], 5, 7, 11, .12), MAGENTA)
        cn.light(ngon(128, 222 + e, 5 + 5 * p.charge[0], 6), BONE, glow=False)
    parts.append(cn)
    return parts, (128, 222 + e)


# ------------------------------------------------------------- Frost ------
# HOARFROST LEVIATHAN: an armoured ice-whale skull, head on and prow down,
# with a crown of ice crystals, pectoral fins and an icicle-toothed jaw.
# Jaw drops for the shard fan; fins flare for the aimed lances; the crown
# lifts and charges for the frost lanes.

def frost(p):
    L = lit(p.lights, MAGENTA, MAGENTA_SH)
    parts = []
    flap = math.sin(p.phase * math.tau) * 4

    for side in (0, 1):
        f = (lambda q: q) if side == 0 else mx
        fin = Part("fin%d" % side, f([(40, 150)])[0])
        ang = (-flap - 16 * p.open[1]) if side == 0 else (flap + 16 * p.open[1])
        shape = rot(f([(58, 128), (16, 146), (4, 182), (22, 178), (34, 190), (62, 158)]), ang, *f([(58, 140)])[0])
        sh = rot(f([(62, 158), (34, 190), (22, 178), (40, 160)]), ang, *f([(58, 140)])[0])
        kick = rot(f([(54, 130), (18, 147), (16, 152), (54, 135)]), ang, *f([(58, 140)])[0])
        fin.solid(shape, BRUISE, BRUISE_SH, sh, ICE, kick)
        fin.line(rot(f([(54, 142), (16, 168)]), ang, *f([(58, 140)])[0]), DETAIL_W)
        fin.line(rot(f([(58, 150), (30, 182)]), ang, *f([(58, 140)])[0]), DETAIL_W)
        parts.append(fin)

    lift = p.open[2] * 8
    crown = Part("crown", (128, 52))
    crystals = [(76, 88, 24, 9, -7), (180, 88, 24, 9, 7), (100, 76, 36, 11, -4), (156, 76, 36, 11, 4), (128, 72, 46, 13, 0)]
    for cx, by, h, w, tilt in crystals:
        h2 = h + lift * (1.4 if cx == 128 else 1.0)
        c = [(cx - w, by), (cx - w * .7, by - h2 * .62), (cx + tilt, by - h2), (cx + w * .7, by - h2 * .7), (cx + w, by)]
        crown.solid(c, ICE, STEEL, [(cx + tilt, by - h2), (cx + w * .7, by - h2 * .7), (cx + w, by), (cx + tilt * .3, by)],
                    BONE, [(cx - w * .7 + 1, by - h2 * .62), (cx + tilt - 1, by - h2 + 3), (cx - w * .35, by - h2 * .55)], OUT_W * .75)
        core = [(cx + tilt * .5, by - h2 * .78), (cx + 3, by - h2 * .45), (cx + tilt * .3, by - h2 * .2), (cx - 3, by - h2 * .45)]
        if p.charge[2] > 0 or p.lights >= .9:
            crown.light(core, MAGENTA)
        else:
            crown.light(core, MAGENTA_SH, glow=False)
    if p.charge[2] > 0:
        crown.light(star(128, 30 - lift, 14 + 18 * p.charge[2], 6, 6, 21, .12), MAGENTA)
        crown.light(ngon(128, 30 - lift, 5 + 4 * p.charge[2], 6), BONE, glow=False)
    parts.append(crown)

    # maw (behind skull and jaw)
    o = p.open[0] * 16
    maw = Part("maw", (128, 186))
    maw.hole([(82, 166), (174, 166), (160, 200 + o), (96, 200 + o)])
    g = p.charge[0]
    maw.light([(104, 176), (152, 176), (144, 192 + o * .7), (112, 192 + o * .7)], MAGENTA if g > 0 else MAGENTA_SH, opacity=.5 + .5 * g)
    if g > 0:
        maw.light(star(128, 186 + o * .5, 12 + 18 * g, 5, 7, 31, .12), MAGENTA)
        maw.light(ngon(128, 186 + o * .5, 5 + 5 * g, 6), BONE, glow=False)
    parts.append(maw)

    jaw = Part("jaw", (128, 200))
    J = shift(mirror([(128, 172), (72, 166), (80, 200), (106, 222), (128, 228)]), 0, o)
    jaw.solid(J, STEEL, STEEL_SH, shift([(128, 200), (184, 166), (176, 200), (150, 222), (128, 228)], 0, o),
              STEEL_HI, shift([(74, 168), (80, 168), (86, 196), (82, 197)], 0, o))
    for x in (92, 108, 148, 164):
        tooth = shift([(x - 6, 172), (x + 6, 172), (x, 158)], 0, o)
        jaw.base(tooth, ICE)
        jaw.hi(shift([(x - 4, 171), (x - 1, 171), (x, 162)], 0, o), BONE)
        jaw.outline(tooth, DETAIL_W)
    jaw.line(shift([(128, 196), (128, 222)], 0, o), DETAIL_W)
    jaw.line(shift([(90, 200), (114, 210)], 0, o), DETAIL_W)
    jaw.line(shift(mx([(90, 200), (114, 210)]), 0, o), DETAIL_W)
    parts.append(jaw)

    skull = Part("skull", (128, 112))
    S = mirror([(128, 60), (96, 62), (66, 74), (46, 96), (40, 122), (48, 150), (64, 168), (88, 176), (128, 178)])
    skull.base(S, STEEL)
    skull.shade([(128, 140), (196, 126), (216, 122), (208, 150), (192, 168), (168, 176), (128, 178)], STEEL_SH)
    skull.hi([(96, 64), (68, 76), (50, 96), (55, 97), (71, 80), (97, 69)], ICE)
    skull.hi([(42, 122), (49, 148), (53, 147), (47, 122)], STEEL_HI)
    for x in (92, 108, 128, 148, 164):
        tooth = [(x - 6, 174), (x + 6, 174), (x, 190 - (2 if x == 128 else 0))]
        skull.base(tooth, ICE)
        skull.hi([(x - 4, 175), (x - 1, 175), (x, 185)], BONE)
        skull.outline(tooth, DETAIL_W)
    skull.outline(S)
    # brow armour
    for side in (0, 1):
        f = (lambda q: q) if side == 0 else mx
        brow = f([(62, 100), (112, 92), (118, 108), (74, 118)])
        skull.base(brow, BRUISE)
        skull.shade(f([(74, 118), (118, 108), (116, 102), (78, 110)]), BRUISE_SH)
        skull.outline(brow, PANEL_W * 1.2)
        eye = f([(80, 122), (110, 114), (106, 126), (84, 130)])
        skull.hole(eye)
        skull.light(f([(84, 122), (106, 117), (103, 124), (87, 127)]), L if p.lights > .2 else MAGENTA_SH)
        if p.charge[1] > 0:
            skull.light(star(*f([(95, 122)])[0], 9 + 12 * p.charge[1], 4, 5, 41 + side, .1), MAGENTA)
    skull.line([(128, 64), (128, 90)])
    skull.line([(128, 132), (128, 168)], DETAIL_W)
    skull.line([(54, 140), (100, 146)], DETAIL_W)
    skull.line(mx([(54, 140), (100, 146)]), DETAIL_W)
    for x, y in ((70, 84), (186, 84), (60, 130), (196, 130)):
        skull.hole(ngon(x, y, 3, 4, 45))
    parts.append(skull)
    return parts, (128, 186 + o * .5)


# ----------------------------------------------------------- Verdant ------
# THE BLOOM QUEEN: a carnivorous hive flower seen from above. Six bruise
# petals around a bile-green trap head with glowing hive eyes and antennae,
# two thorned vines hanging down. Petals pull in for the thorn volleys,
# spread wide with glowing pollen for the spore fan; the vines lift and
# charge for the root lashes.

def verdant(p):
    L = lit(p.lights, BILE_LIGHT, BILE_SH)
    parts = []
    breathe = math.sin(p.phase * math.tau) * 3
    cx, cy = 128.0, 112.0
    reach = 1.0 + .14 * p.open[1] - .12 * p.open[0]
    spread = 10 * p.open[1] - 6 * p.open[0]
    for i, a in enumerate((-140, 140, -95, 95, -45, 45)):
        sgn = -1 if a < 0 else 1
        ang = a + sgn * spread
        R = (104 + breathe) * reach
        W = 36.0
        local = [(0, 10), (W * .62, R * .3), (W * .5, R * .74), (0, R), (-W * .42, R * .8), (-W * .6, R * .34)]
        shadow = [(0, 10), (W * .62, R * .3), (W * .5, R * .74), (0, R), (W * .1, R * .4)]
        kick = [(-W * .6, R * .34), (-W * .42, R * .8), (-W * .32, R * .78), (-W * .48, R * .36)]
        vein = [(0, R * .22), (0, R * .86)]

        def place(q):
            return [(cx + x * math.cos(math.radians(ang)) - y * math.sin(math.radians(ang)),
                     cy + x * math.sin(math.radians(ang)) + y * math.cos(math.radians(ang))) for x, y in q]
        pt = Part("petal%d" % i, place([(0, R * .6)])[0])
        pt.solid(place(local), BRUISE, BRUISE_SH, place(shadow), BRUISE_HI, place(kick), OUT_W * .85)
        pt.line(place(vein), DETAIL_W)
        sac = place([(0, R * .66), (5, R * .74), (0, R * .82), (-5, R * .74)])
        pt.light(sac, BILE_LIGHT if (p.open[1] > .2 or p.lights >= .5) else BILE_SH)
        if p.charge[1] > 0:
            pt.light(star(*place([(0, R * .74)])[0], 7 + 9 * p.charge[1], 3, 5, 51 + i, .15), BILE_LIGHT)
        parts.append(pt)

    lash = p.open[2]
    for side in (0, 1):
        f = (lambda q: q) if side == 0 else mx
        vn = Part("vine%d" % side, f([(76, 196)])[0])
        up = 24 * lash
        out = 10 * lash
        spine = [(100, 150), (84, 172 - up * .3), (90, 192 - up * .6), (70, 214 - up - out * .2), (62, 236 - up * 1.3)]
        spine = [(x - out * (k / 4.0), y) for k, (x, y) in enumerate(spine)]
        poly = []
        for k, (x, y) in enumerate(spine):
            w = 9 - k * 1.6
            poly.append((x - w, y))
        for k, (x, y) in reversed(list(enumerate(spine))):
            w = 9 - k * 1.6
            poly.append((x + w, y))
        poly = f(poly)
        vn.base(poly, BILE_SH)
        vn.hi(f([(x - (9 - k * 1.6) + 1, y) for k, (x, y) in enumerate(spine)][:3] +
                [(x - (9 - k * 1.6) + 4, y) for k, (x, y) in reversed(list(enumerate(spine)))][2:]), BILE)
        vn.outline(poly, OUT_W * .75)
        for k in (1, 2, 3):
            x, y = spine[k]
            th = f([(x + 7 - k, y - 3), (x + 16 - k, y + 2), (x + 6 - k, y + 4)])
            vn.base(th, BONE)
            vn.outline(th, DETAIL_W)
        tx, ty = f([spine[-1]])[0]
        vn.light([(tx, ty - 5), (tx + 5, ty), (tx, ty + 6), (tx - 5, ty)], BILE_LIGHT if lash > .2 else BILE_SH)
        if p.charge[2] > 0:
            vn.light(star(tx, ty, 9 + 14 * p.charge[2], 4, 6, 61 + side, .15), BILE_LIGHT)
            vn.light(ngon(tx, ty, 4 + 3 * p.charge[2], 6), BONE, glow=False)
        parts.append(vn)

    # maw + jaws
    o = p.open[0]
    maw = Part("maw", (128, 156))
    maw.hole([(104, 136), (152, 136), (146, 170 + 8 * o), (110, 170 + 8 * o)])
    g = max(p.charge[0], p.charge[1] * .6)
    maw.light([(114, 144), (142, 144), (138, 162 + 6 * o), (118, 162 + 6 * o)], BILE_LIGHT if g > 0 else BILE_SH, opacity=.5 + .5 * g)
    if g > 0:
        maw.light(star(128, 156 + 4 * o, 10 + 16 * g, 4, 7, 71, .14), BILE_LIGHT)
        maw.light(ngon(128, 156 + 4 * o, 4 + 4 * g, 6), BONE, glow=False)
    parts.append(maw)

    for side in (0, 1):
        f = (lambda q: q) if side == 0 else mx
        jw = Part("jaw%d" % side, f([(106, 156)])[0])
        ang = (14 + 12 * o) * (1 if side == 0 else -1)
        piv = f([(110, 138)])[0]
        jaw = rot(f([(124, 140), (104, 136), (92, 150), (98, 172), (114, 180), (122, 168)]), ang, *piv)
        jw.solid(jaw, BILE, BILE_SH, rot(f([(122, 168), (114, 180), (98, 172), (108, 168)]), ang, *piv),
                 BILE_HI, rot(f([(102, 139), (94, 150), (97, 151), (104, 142)]), ang, *piv), OUT_W * .8)
        for k in range(3):
            y = 146 + k * 9
            t = rot(f([(122 - k * 1.5, y), (122 - k * 1.5, y + 6), (130 - k, y + 3)]), ang, *piv)
            jw.base(t, BONE)
            jw.outline(t, DETAIL_W)
        parts.append(jw)

    hd = Part("head", (128, 100))
    hb = mirror([(128, 66), (104, 72), (88, 90), (88, 116), (104, 134), (128, 140)])
    hd.solid(hb, BILE, BILE_SH, [(128, 108), (168, 100), (168, 116), (152, 134), (128, 140)],
             BILE_HI, [(104, 74), (90, 90), (94, 91), (106, 78)])
    hd.line([(128, 70), (128, 84)], DETAIL_W)
    hd.line([(96, 120), (112, 132)], DETAIL_W)
    hd.line(mx([(96, 120), (112, 132)]), DETAIL_W)
    for k, x in enumerate((102, 118, 138, 154)):
        y = 98 + (4 if k in (0, 3) else 0)
        eye = [(x - 7, y), (x, y - 5), (x + 7, y), (x, y + 6)]
        hd.hole(eye)
        hd.light([(x - 4, y), (x, y - 3), (x + 4, y), (x, y + 3)], L if p.lights > .2 else BILE_SH)
    for side in (0, 1):
        f = (lambda q: q) if side == 0 else mx
        sway = math.sin(p.phase * math.tau + side) * 3
        stalk = f([(114, 72), (104 + sway, 50), (98 + sway, 36)])
        hd.line(stalk, PANEL_W * 1.3)
        pod = f([(98 + sway, 30), (103 + sway, 36), (98 + sway, 42), (93 + sway, 36)])
        hd.light(pod, L)
    parts.append(hd)
    return parts, (128, 156 + 4 * o)


# ------------------------------------------------------------- Ember ------
# CINDER DRAKE: a basalt dragon seen from above, head down, bruise wings
# spread, magma (magenta-hot) cracks along its back. Wings beat up for the
# ember fan; the head rears back and the nostrils flare for the aimed
# fireballs; the wings sweep wide and the chest vents glow for the lanes.

def ember(p):
    L = lit(p.lights, MAGENTA, MAGENTA_SH)
    parts = []
    flap = math.sin(p.phase * math.tau) * 5
    for side in (0, 1):
        f = (lambda q: q) if side == 0 else mx
        ang = (flap - 16 * p.open[0] + 9 * p.open[2]) * (1 if side == 0 else -1)
        piv = f([(100, 96)])[0]
        sx = 1.0 + .06 * p.open[2]

        def wing(q):
            return rot(scale(f(q), sx if side == 0 else sx, 1.0, *piv), -ang, *piv)
        wg = Part("wing%d" % side, f([(50, 104)])[0])
        mem = wing([(100, 88), (20, 44), (36, 74), (6, 98), (30, 112), (14, 150), (50, 138), (42, 172), (98, 122)])
        wg.base(mem, BRUISE)
        wg.shade(wing([(36, 74), (6, 98), (30, 112), (60, 104)]), BRUISE_SH)
        wg.shade(wing([(14, 150), (50, 138), (42, 172), (70, 132)]), BRUISE_SH)
        wg.hi(wing([(98, 89), (22, 46), (26, 52), (98, 94)]), BRUISE_HI)
        wg.outline(mem)
        for tip in ((20, 44), (6, 98), (14, 150), (42, 172)):
            wg.line(wing([(98, 102), tip]), PANEL_W * 1.1)
            cl = wing([(tip[0] - 4, tip[1] + 2), tip, (tip[0] + 2, tip[1] + 6)])
            wg.base(wing([(tip[0] - 5, tip[1] + 3), (tip[0] - 3, tip[1] - 6), (tip[0] + 4, tip[1] + 4)]), BONE)
            wg.outline(wing([(tip[0] - 5, tip[1] + 3), (tip[0] - 3, tip[1] - 6), (tip[0] + 4, tip[1] + 4)]), DETAIL_W)
        parts.append(wg)

    rear = -10 * p.open[1]
    bd = Part("body", (128, 70))
    B = mirror([(128, 26), (114, 34), (104, 62), (106, 98), (128, 112)])
    bd.solid(B, ROCK, ROCK_SH, [(128, 60), (152, 62), (150, 98), (128, 112)], ROCK_HI, [(114, 36), (106, 62), (110, 62), (117, 38)])
    for k in range(4):
        y = 34 + k * 17
        sp = [(122, y + 8), (128, y - 6), (134, y + 8)]
        bd.base(sp, BONE)
        bd.shade([(128, y - 6), (134, y + 8), (128, y + 8)], ROCK_HI)
        bd.outline(sp, DETAIL_W)
    vent = p.charge[2]
    for crack in ([(110, 46), (116, 56), (112, 70)], [(146, 50), (140, 62), (146, 76)], [(112, 84), (120, 96)], [(144, 82), (136, 98)]):
        bd.light([(x, y) for x, y in crack] + [(x + 3, y + 1) for x, y in reversed(crack)], MAGENTA if (vent > 0 or p.lights >= .5) else MAGENTA_SH)
    if vent > 0:
        bd.light(star(128, 92, 12 + 16 * vent, 5, 7, 81, .14), MAGENTA)
        bd.light(ngon(128, 92, 5 + 4 * vent, 6), BONE, glow=False)
    parts.append(bd)

    o = p.open[0] * .6 + p.open[1] * .4
    hd = Part("head", (128, 150 + rear))
    jaw = shift(mirror([(128, 172), (110, 168), (114, 204), (128, 214)]), 0, rear + 10 * o)
    maw = shift([(112, 168), (144, 168), (140, 200 + 8 * o), (116, 200 + 8 * o)], 0, rear)
    hd.hole(maw)
    g = max(p.charge[0], p.charge[1])
    hd.light(shift([(118, 174), (138, 174), (134, 194 + 6 * o), (122, 194 + 6 * o)], 0, rear), MAGENTA if g > 0 else MAGENTA_SH, opacity=.5 + .5 * g)
    hd.solid(jaw, ROCK, ROCK_SH, shift([(128, 172), (146, 168), (142, 204), (128, 214)], 0, rear + 10 * o),
             ROCK_HI, shift([(111, 170), (114, 170), (117, 202), (115, 203)], 0, rear + 10 * o), OUT_W * .85)
    H = shift(mirror([(128, 100), (106, 106), (92, 124), (96, 148), (108, 172), (116, 196), (128, 202)]), 0, rear)
    hd.solid(H, ROCK, ROCK_SH, shift([(128, 150), (164, 146), (148, 172), (140, 196), (128, 202)], 0, rear),
             ROCK_HI, shift([(106, 108), (94, 124), (98, 125), (108, 112)], 0, rear))
    for side in (0, 1):
        f = (lambda q: q) if side == 0 else mx
        horn = shift(f([(102, 116), (96, 106), (80, 86), (70, 64), (86, 82), (110, 108)]), 0, rear)
        hd.base(horn, BONE)
        hd.shade(shift(f([(80, 86), (70, 64), (86, 82), (96, 98)]), 0, rear), ROCK_HI)
        hd.outline(horn, OUT_W * .7)
        eye = shift(f([(100, 128), (116, 132), (114, 140), (102, 136)]), 0, rear)
        hd.hole(eye)
        hd.light(shift(f([(103, 131), (113, 134), (112, 137), (104, 135)]), 0, rear), L if p.lights > .2 else MAGENTA_SH)
        nos = shift(f([(118, 186), (124, 184), (123, 191), (118, 191)]), 0, rear)
        hd.light(nos, MAGENTA if (p.charge[1] > 0 or p.lights >= .5) else MAGENTA_SH)
        if p.charge[1] > 0:
            hd.light(star(*shift(f([(121, 188)]), 0, rear)[0], 7 + 10 * p.charge[1], 3, 5, 91 + side, .14), MAGENTA)
    hd.line(shift([(128, 106), (128, 150)], 0, rear))
    hd.line(shift([(100, 152), (118, 158)], 0, rear), DETAIL_W)
    hd.line(shift(mx([(100, 152), (118, 158)]), 0, rear), DETAIL_W)
    if p.charge[0] > 0:
        hd.light(star(128, 206 + rear + 8 * o, 12 + 18 * p.charge[0], 5, 7, 99, .14), MAGENTA)
        hd.light(ngon(128, 206 + rear + 8 * o, 5 + 5 * p.charge[0], 6), BONE, glow=False)
    parts.append(hd)
    return parts, (128, 206 + rear + 8 * o)


BOSSES = {
    #  key: (draw, light, light_sh, explosion body tones)
    "Space": (space, MAGENTA, MAGENTA_SH),
    "Frost": (frost, MAGENTA, MAGENTA_SH),
    "Verdant": (verdant, BILE_LIGHT, BILE_SH),
    "Ember": (ember, MAGENTA, MAGENTA_SH),
}
WORLD_INDEX = {"Space": 1, "Frost": 2, "Verdant": 3, "Ember": 4}
NAMES = {
    "Space": ("VOID ARCHON", "CAPITAL CARRIER"),
    "Frost": ("HOARFROST LEVIATHAN", "CRYO FORTRESS"),
    "Verdant": ("THE BLOOM QUEEN", "HIVE MOTHER"),
    "Ember": ("CINDER DRAKE", "VOLCANIC WYRM"),
}


# ------------------------------------------------------------ compose -----
def compose(key, pose, hold):
    draw, light, light_sh = BOSSES[key]
    parts, muzzle = draw(pose)
    flash = pose.flash or pose.death == 0
    back, body, glow, fx = [], [], [], []

    d = pose.death
    dist = [0, 5, 14, 30, 46][d] if d >= 0 else 0
    spin = [0, 3, 9, 18, 26][d] if d >= 0 else 0

    def part_tf(i, part):
        if d <= 0:
            return ""
        ax, ay = part.anchor
        vx, vy = ax - C, ay - 120.0
        n = math.hypot(vx, vy) or 1.0
        sgn = 1 if i % 2 == 0 else -1
        return ' transform="translate(%s %s) rotate(%s %s %s)"' % (
            fmt(vx / n * dist), fmt(vy / n * dist + (dist * .3)), fmt(spin * sgn), fmt(ax), fmt(ay))

    show_parts = d < 4
    if show_parts:
        for i, part in enumerate(parts):
            tf = part_tf(i, part)
            layers = {"base": [], "shadow": [], "highlight": [], "ink": []}
            lights_b, lights_f = [], []
            for layer, kind, p, c, w, op in part.items:
                if layer == "glow":
                    if d >= 2:
                        continue  # lights die as it breaks up
                    b, f = light_els(kind, p, c, op, flash)
                    lights_b += b
                    lights_f += f
                else:
                    layers[layer].append(el(kind, p, c, w, op, flash))
            inner = "".join('<g class="%s">%s</g>' % (k, "".join(v)) for k, v in layers.items() if v)
            body.append('<g class="part" data-part="%s"%s>%s</g>' % (part.name, tf, inner))
            if lights_b:
                back.append('<g%s>%s</g>' % (tf, "".join(lights_b)))
            if lights_f:
                glow.append('<g%s>%s</g>' % (tf, "".join(lights_f)))

    # warp smear: ghost copies trailing + speed lines (drawn, not tweened)
    if pose.smear > 0:
        ghosts = []
        for k, (dy, op) in enumerate(((26, .38), (52, .2))):
            sil = []
            for part in parts:
                for layer, kind, p, c, w, o in part.items:
                    if kind == "fill" and layer == "base":
                        sil.append('<polygon points="%s" fill="%s"/>' % (pts(p), light_sh))
            ghosts.append('<g transform="translate(0 %s)" opacity="%s">%s</g>' % (fmt(-dy * pose.smear * (1 if pose.bob <= 0 else -1)), fmt(op), "".join(sil)))
        body.insert(0, "".join(ghosts))
        rnd = random.Random(int(pose.phase * 100) + 5)
        for k in range(9):
            x = 30 + k * 24 + rnd.uniform(-6, 6)
            y0 = rnd.uniform(0, 60)
            fx.append('<polyline points="%s" fill="none" stroke="%s" stroke-width="%s" opacity=".75" stroke-linecap="square"/>' % (
                pts([(x, y0), (x, y0 + rnd.uniform(120, 200))]), BONE if k % 2 else light, fmt(rnd.uniform(2, 4))))

    # fire: a muzzle flash star at the main weapon
    if pose.fire:
        mxp, myp = muzzle
        fx.append('<polygon points="%s" fill="%s" stroke="%s" stroke-width="%s" stroke-linejoin="round"/>' % (
            pts(star(mxp, myp + 6, 30, 12, 7, 5, .2, 90)), light, INK, fmt(PANEL_W)))
        fx.append('<polygon points="%s" fill="%s"/>' % (pts(star(mxp, myp + 6, 17, 7, 7, 6, .15, 90)), BONE))

    # death overlays: cracks, cel bursts, shards, smoke
    if d >= 0:
        rnd = random.Random(1000 + sum(map(ord, key)))
        if d <= 2:
            for k in range(6):
                a = math.radians(k * 60 + rnd.uniform(-20, 20))
                path = [(C, 122)]
                r = 0
                for s in range(4):
                    r += rnd.uniform(14, 24) * (1 + d * .3)
                    j = rnd.uniform(-10, 10)
                    path.append((C + math.cos(a) * r - math.sin(a) * j, 122 + math.sin(a) * r + math.cos(a) * j))
                fx.append(el("line", path, INK, PANEL_W * 1.4, 1, False))
                if not flash:
                    fx.append('<polyline points="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round" stroke-linejoin="round"/>' % (pts(path), light, fmt(DETAIL_W)))
        bursts = {0: [(C, 122, 30)], 1: [(100, 108, 36), (158, 140, 30)], 2: [(C, 120, 96)], 3: [(C, 124, 70)], 4: []}[d]
        for k, (bx, by, br) in enumerate(bursts):
            if d == 3:
                continue
            fx.append('<polygon points="%s" fill="%s" stroke="%s" stroke-width="%s" stroke-linejoin="round"/>' % (
                pts(star(bx, by, br, br * .55, 9, 200 + d * 7 + k, .22)), BRUISE if d == 2 else light, INK, fmt(OUT_W * .8)))
            fx.append('<polygon points="%s" fill="%s"/>' % (pts(star(bx, by, br * .72, br * .4, 8, 300 + d + k, .2, -70)), light if d == 2 else BONE))
            fx.append('<polygon points="%s" fill="%s"/>' % (pts(star(bx, by, br * .42, br * .22, 7, 400 + d + k, .2)), BONE if d == 2 else PURE))
        if d >= 2:
            for k in range(10 if d == 2 else 7):
                a = math.radians(k * 36 + rnd.uniform(-12, 12))
                r = rnd.uniform(70, 110) + (d - 2) * 30
                sx, sy = C + math.cos(a) * r, 122 + math.sin(a) * r * .8
                sh = rot([(sx - 7, sy - 4), (sx + 8, sy - 2), (sx + 2, sy + 8)], rnd.uniform(0, 360), sx, sy)
                fx.append('<polygon points="%s" fill="%s" stroke="%s" stroke-width="%s" stroke-linejoin="round"/>' % (
                    pts(sh), STEEL if key != "Verdant" else BILE, INK, fmt(DETAIL_W)))
        if d >= 3:
            cels = 7 if d == 3 else 5
            for k in range(cels):
                a = math.radians(k * 360.0 / cels + rnd.uniform(-15, 15))
                r = (34 if d == 3 else 62) + rnd.uniform(-8, 8)
                sx, sy = C + math.cos(a) * r, 122 + math.sin(a) * r * .75 - (d - 3) * 10
                size = (30 if d == 3 else 26) + rnd.uniform(-5, 6)
                cel = star(sx, sy, size, size * .78, 5, 500 + k + d, .18)
                fx.append('<polygon points="%s" fill="%s" stroke="%s" stroke-width="%s" stroke-linejoin="round"/>' % (
                    pts(cel), DUSK, INK, fmt(PANEL_W * 1.2)))
                fx.append('<polygon points="%s" fill="%s"/>' % (pts(shift(scale(cel, .55, .55, sx, sy), 4, 5)), INDIGO_0))
            for k in range(8 if d == 3 else 4):
                a = rnd.uniform(0, math.tau)
                r = rnd.uniform(40, 120)
                ex, ey = C + math.cos(a) * r, 122 + math.sin(a) * r * .8
                fx.append('<polygon points="%s" fill="%s"/>' % (pts([(ex, ey - 5), (ex + 3, ey), (ex, ey + 5), (ex - 3, ey)]), light))

    sx, sy = pose.sx, pose.sy
    if pose.smear > 0:
        sx, sy = sx * (1 - .1 * pose.smear), sy * (1 + .32 * pose.smear)
    tf = "translate(%s %s) scale(%s %s) translate(%s %s)" % (
        fmt(C + pose.dx), fmt(C + pose.bob), fmt(sx), fmt(sy), fmt(-C), fmt(-C))
    svg = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">',
           '<!-- %s boss, hold: %s ticks @24fps. Generated by Art/Bosses/src~/bosses.py; edit there. -->' % (key, hold),
           '<defs><filter id="blur" filterUnits="userSpaceOnUse" x="-64" y="-64" width="384" height="384">'
           '<feGaussianBlur stdDeviation="5"/></filter></defs>',
           '<g transform="%s">' % tf,
           '<g id="glow-back">%s</g>' % "".join(back),
           '<g id="body">%s</g>' % "".join(body),
           '<g id="glow">%s</g>' % "".join(glow),
           '</g>',
           '<g id="fx" transform="%s">%s</g>' % (tf, "".join(fx)),
           '</svg>']
    return "\n".join(svg)


def frame_table(key):
    """(name, pose, hold ticks) in atlas order (BossArt.cs)."""
    T = []
    idle = [Pose(phase=0.0, lights=.8), Pose(phase=.25, bob=-3, lights=1.0),
            Pose(phase=.5, bob=2, sy=.985, sx=1.012, lights=.7), Pose(phase=.75, bob=0, lights=.85)]
    for i, p in enumerate(idle):
        T.append(("idle_%d" % i, p, IDLE_TICKS[i]))
    T.append(("hit", Pose(flash=True), 2))
    for tell in (0, 1):
        o = [0.0, 0.0, 0.0]
        c = [0.0, 0.0, 0.0]
        o[tell] = .3
        T.append(("tell%d_a" % tell, Pose(open=list(o), charge=list(c), sx=1.03, sy=.95, bob=2, lights=.4), 2))
        o[tell] = 1.0
        c[tell] = 1.0
        T.append(("tell%d_b" % tell, Pose(open=list(o), charge=list(c), sx=.99, sy=1.02, bob=-2, lights=1.0), 4))
    T.append(("fire", Pose(fire=True, bob=-6, sy=1.035, sx=.985, lights=1.0, open=[.6, .6, .6]), 3))
    T.append(("tell2_a", Pose(open=[0, 0, .3], sx=1.03, sy=.95, bob=2, lights=.4), 2))
    T.append(("tell2_b", Pose(open=[0, 0, 1.0], charge=[0, 0, 1.0], sx=.99, sy=1.02, bob=-2, lights=1.0), 4))
    for i in range(3):
        T.append(("death_%d" % i, Pose(death=i, lights=.6), DEATH_TICKS[i]))
    for i in range(3, 5):
        T.append(("death_%d" % i, Pose(death=i, lights=.6), DEATH_TICKS[i]))
    T.append(("retreat_0", Pose(smear=.7, phase=.1, lights=1.0), 2))
    T.append(("retreat_1", Pose(smear=1.0, phase=.6, lights=1.0, sx=.97), 2))
    T.append(("portrait", Pose(lights=1.0), 0))
    # The first two bosses get twenty additional, intentionally held
    # drawings. These are not tweened in game: their idle and attack states
    # snap through hand-authored frames during the encounter.
    if key == "Space":
        for i in range(8):
            phase = i / 8.0
            T.append(("archon_idle_%d" % i, Pose(
                phase=phase, bob=(-2, -3, -2, 0, 2, 3, 2, 0)[i],
                sx=(1.0, 1.006, 1.012, 1.006, 1.0, .994, .99, .994)[i],
                sy=(1.0, .994, .99, .994, 1.0, 1.006, 1.01, 1.006)[i],
                lights=(.65, .8, 1.0, .86, .7, .82, 1.0, .78)[i]), 2))
        for tell in range(3):
            for stage in range(4):
                o = [0.0, 0.0, 0.0]
                c = [0.0, 0.0, 0.0]
                o[tell] = (.22, .52, .82, 1.0)[stage]
                c[tell] = (.08, .35, .7, 1.0)[stage]
                T.append(("archon_tell%d_%d" % (tell, stage), Pose(
                    phase=.2 + stage * .14, open=o, charge=c,
                    bob=(2, 1, -1, -3)[stage],
                    sx=(1.025, 1.012, 1.0, .985)[stage],
                    sy=(.965, .985, 1.005, 1.025)[stage],
                    lights=(.45, .62, .85, 1.0)[stage]), 2))
    elif key == "Frost":
        # A weighty whale-fortress idle: fins paddle, the skull drifts and
        # the crown's trapped aurora pulses through the ice.
        for i in range(8):
            phase = i / 8.0
            T.append(("leviathan_idle_%d" % i, Pose(
                phase=phase, bob=(0, -2, -3, -2, 0, 2, 3, 2)[i],
                sx=(1.0, 1.008, 1.014, 1.008, 1.0, .992, .986, .992)[i],
                sy=(1.0, .992, .986, .992, 1.0, 1.008, 1.014, 1.008)[i],
                lights=(.58, .7, .88, 1.0, .8, .66, .74, .9)[i]), 2))
        # Jaw: unseal, drop, illuminate, spray.  Eyes/fins: sweep apart and
        # glare. Crown: rises through three ice-bright charge states.
        for tell in range(3):
            for stage in range(4):
                o = [0.0, 0.0, 0.0]
                c = [0.0, 0.0, 0.0]
                o[tell] = (.18, .45, .76, 1.0)[stage]
                c[tell] = (.05, .3, .68, 1.0)[stage]
                T.append(("leviathan_tell%d_%d" % (tell, stage), Pose(
                    phase=.1 + stage * .17, open=o, charge=c,
                    bob=(2, 1, -1, -3)[stage],
                    sx=(1.024, 1.012, 1.0, .986)[stage],
                    sy=(.966, .986, 1.006, 1.026)[stage],
                    lights=(.4, .6, .84, 1.0)[stage]), 2))
    else:
        # Keep every atlas the same dimensions so runtime slicing remains
        # deterministic while the other bosses await their expanded sets.
        for i in range(20):
            T.append(("reserve_idle_%02d" % i, Pose(phase=(i % 8) / 8.0,
                bob=(-2, 0, 2, 0)[i % 4], lights=.72 + .06 * (i % 3)), 2))
    assert len(T) == BODY_COLS * BODY_ROWS
    return T


# ------------------------------------------------------------- shots ------
def shot_svg(key, cell, hold):
    _, light, light_sh = BOSSES[key]
    s = []
    frame = cell % 2

    def ink_poly(p, fill, w=4.0):
        return '<polygon points="%s" fill="%s" stroke="%s" stroke-width="%s" stroke-linejoin="round" paint-order="stroke"/>' % (pts(p), fill, INK, fmt(w))

    def glow(p, c, op=.65):
        return '<polygon points="%s" fill="%s" filter="url(#g)" opacity="%s"/>' % (pts(p), c, fmt(op))

    def fill(p, c, op=1.0):
        o = "" if op >= 1 else ' opacity="%s"' % fmt(op)
        return '<polygon points="%s" fill="%s"%s/>' % (pts(p), c, o)

    if cell in (0, 1):    # bolt
        if key == "Space":
            stretch = 6 if frame else 0
            body = [(64, 22 - stretch), (78, 58), (64, 108 + stretch), (50, 58)]
            s += [glow(body, light), ink_poly(body, light), fill([(64, 34), (70, 58), (64, 92 + stretch), (58, 58)], BONE),
                  fill([(64, 22 - stretch), (78, 58), (71, 58)], MAGENTA_SH)]
            if frame:
                s += [fill([(40, 40), (46, 42), (44, 48)], BONE), fill([(86, 76), (90, 82), (84, 82)], BONE)]
        elif key == "Frost":
            body = [(64, 112), (75, 66), (70, 20), (58, 20), (53, 66)]
            s += [glow([(64, 100), (70, 60), (64, 40), (58, 60)], light, .5), ink_poly(body, ICE),
                  fill([(64, 112), (75, 66), (70, 20), (64, 20), (66, 66)], STEEL),
                  fill([(56, 24), (60, 24), (57, 66), (54, 66)], BONE),
                  fill([(64, 52), (69, 66), (64, 84), (59, 66)], MAGENTA)]
            if frame:
                s += [fill(star(42, 44, 7, 2, 4, 1, 0), BONE), fill(star(86, 90, 6, 2, 4, 2, 0), ICE)]
        elif key == "Verdant":
            a = 5 if frame else 0
            body = rot([(64, 112), (73, 72), (70, 30), (64, 18), (58, 30), (55, 72)], a, 64, 64)
            s += [ink_poly(body, BILE), fill(rot([(64, 112), (73, 72), (70, 30), (64, 18), (66, 72)], a, 64, 64), BILE_SH),
                  fill(rot([(58, 32), (61, 30), (58, 72), (56, 72)], a, 64, 64), BILE_HI),
                  glow(rot([(64, 112), (69, 96), (64, 88), (59, 96)], a, 64, 64), light),
                  fill(rot([(64, 112), (69, 96), (64, 88), (59, 96)], a, 64, 64), light)]
        else:  # Ember fireball
            tail = 8 if frame else 0
            flame = [(64, 8 - tail), (76, 44), (86, 30 - tail * .5), (84, 62), (44, 62), (42, 30 - tail * .5), (52, 44)]
            s += [glow(ngon(64, 74, 30, 8), light, .55), ink_poly(flame, MAGENTA_SH),
                  fill([(64, 22 - tail), (72, 50), (56, 50)], light),
                  ink_poly(star(64, 76, 30, 22, 7, 3 + frame, .12), BRUISE),
                  fill(star(64, 76, 22, 15, 7, 13 + frame, .1), light), fill(ngon(64, 78, 10, 6), BONE),
                  fill([(46, 70), (54, 62), (56, 70)], BRUISE_HI)]
    elif cell in (2, 3):  # shard
        a = 12 if frame else 0
        if key == "Space":
            body = rot([(64, 98), (90, 52), (76, 48), (64, 68), (52, 48), (38, 52)], a, 64, 70)
            s += [glow(body, light, .5), ink_poly(body, light), fill(rot([(64, 88), (80, 56), (76, 54), (64, 74)], a, 64, 70), BONE),
                  fill(rot([(64, 98), (90, 52), (84, 51), (64, 86)], a, 64, 70), MAGENTA_SH)]
        elif key == "Frost":
            hexs = ngon(64, 64, 26, 6, a)
            s += [ink_poly(hexs, ICE), fill([(64, 64)] + hexs[1:4], STEEL), fill([hexs[4], hexs[5], (64, 64)], BONE, .9),
                  glow(ngon(64, 64, 9, 4, a), light), fill(ngon(64, 64, 7, 4, a), light)]
        elif key == "Verdant":
            pod = ngon(64, 64, 24, 8, a)
            spikes = []
            for k in range(6):
                ang = math.radians(k * 60 + a)
                cx, cy = 64 + math.cos(ang) * 26, 64 + math.sin(ang) * 26
                spikes.append(ink_poly(rot([(cx - 4, cy), (cx + 10, cy), (cx, cy + 4)], k * 60 + a, cx, cy), BONE, 2.5))
            r = 13 if frame else 10
            s += spikes + [ink_poly(pod, BILE_SH), fill(ngon(64, 64, 24, 8, a)[0:4] + [(64, 64)], BILE_SH),
                           glow(ngon(64, 64, r + 4, 6), light), fill(ngon(64, 64, r, 6), light), fill(ngon(62, 61, 4, 4), BONE)]
        else:
            body = rot([(64, 100), (84, 62), (66, 30), (44, 60)], a, 64, 64)
            s += [glow(body, light, .45), ink_poly(body, BRUISE), fill(rot([(64, 100), (84, 62), (66, 30), (66, 66)], a, 64, 64), BRUISE_SH),
                  fill(rot([(64, 84), (72, 62), (64, 48), (56, 62)], a, 64, 64), light),
                  fill(rot([(46, 60), (64, 34), (60, 46)], a, 64, 64), BRUISE_HI)]
    elif cell == 4:       # lane telegraph: stretch-safe vertical bars
        s += [fill([(10, 0), (118, 0), (118, 128), (10, 128)], light, .22),
              fill([(10, 0), (22, 0), (22, 128), (10, 128)], light, .9),
              fill([(106, 0), (118, 0), (118, 128), (106, 128)], light, .9),
              fill([(6, 0), (10, 0), (10, 128), (6, 128)], INK, .9),
              fill([(118, 0), (122, 0), (122, 128), (118, 128)], INK, .9),
              fill([(61, 0), (67, 0), (67, 128), (61, 128)], BONE, .55)]
    elif cell in (5, 6):  # beam
        core = 18 if frame == 0 else 12
        bodyw = 46 if frame == 0 else 50
        s += ['<rect x="0" y="0" width="128" height="128" fill="%s" opacity=".35" filter="url(#g)"/>' % light,
              fill([(64 - bodyw - 6, 0), (64 + bodyw + 6, 0), (64 + bodyw + 6, 128), (64 - bodyw - 6, 128)], INK),
              fill([(64 - bodyw, 0), (64 + bodyw, 0), (64 + bodyw, 128), (64 - bodyw, 128)], light),
              fill([(64 - bodyw, 0), (64 - bodyw + 8, 0), (64 - bodyw + 8, 128), (64 - bodyw, 128)], light_sh),
              fill([(64 + bodyw - 8, 0), (64 + bodyw, 0), (64 + bodyw, 128), (64 + bodyw - 8, 128)], light_sh),
              fill([(64 - core, 0), (64 + core, 0), (64 + core, 128), (64 - core, 128)], BONE)]
    else:                 # muzzle charge
        s += [glow(star(64, 64, 58, 22, 8, 9, .1), light, .7), fill(star(64, 64, 56, 22, 8, 9, .1), light),
              fill(star(64, 64, 34, 13, 8, 10, .08, -67.5), BONE), fill(ngon(64, 64, 9, 8), PURE)]
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128" width="128" height="128">'
            '<!-- %s boss shot cell %d, hold: %s ticks @24fps -->'
            '<defs><filter id="g" filterUnits="userSpaceOnUse" x="-32" y="-32" width="192" height="192">'
            '<feGaussianBlur stdDeviation="4"/></filter></defs>%s</svg>') % (key, cell, hold, "".join(s))


SHOT_CELLS = ["bolt_0", "bolt_1", "shard_0", "shard_1", "telegraph", "beam_0", "beam_1", "charge"]


# ------------------------------------------------------- name card ------
def card_svg(key):
    name, title = NAMES[key]
    _, light, light_sh = BOSSES[key]
    size = min(86, int(820 / (0.80 * len(name))))
    sub = "WORLD %d  //  %s  //  %s" % (WORLD_INDEX[key], key.upper(), title)
    slab = [(52, 28), (1004, 28), (972, 260), (20, 260)]
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 288" width="1024" height="288">
<!-- {key} boss name card. Generated by Art/Bosses/src~/bosses.py. -->
<g id="base"><polygon points="{pts(shift(slab, 10, 10))}" fill="{INK}"/><polygon points="{pts(slab)}" fill="{INDIGO_0}"/>
<polygon points="{pts([(52, 28), (150, 28), (118, 260), (20, 260)])}" fill="{light}"/></g>
<g id="shadow"><polygon points="{pts([(150, 210), (979, 210), (972, 260), (118, 260)])}" fill="{NIGHT_1}"/>
<polygon points="{pts([(96, 150), (140, 150), (118, 260), (82, 260)])}" fill="{light_sh}"/></g>
<g id="highlight"><polygon points="{pts([(158, 40), (996, 40), (995, 46), (157, 46)])}" fill="{light}"/>
<polygon points="{pts([(60, 36), (142, 36), (141, 42), (59, 42)])}" fill="{BONE}"/></g>
<g id="ink"><polygon points="{pts(slab)}" fill="none" stroke="{INK}" stroke-width="10" stroke-linejoin="miter"/>
<polyline points="{pts([(150, 28), (118, 260)])}" fill="none" stroke="{INK}" stroke-width="6"/>
<polygon points="{pts([(62, 120), (100, 92), (128, 120), (100, 148)])}" fill="{INK}"/>
<polygon points="{pts([(74, 120), (100, 102), (116, 120), (100, 138)])}" fill="{BONE}"/></g>
<g id="type" font-family="Orbitron" font-weight="700">
<text x="176" y="{148 - (86 - size) // 3}" font-size="{size}" fill="{BONE}" stroke="{INK}" stroke-width="10" stroke-linejoin="round" paint-order="stroke" transform="skewX(-8)">{name}</text>
<text x="196" y="236" font-size="30" fill="{light}" stroke="{INK}" stroke-width="6" stroke-linejoin="round" paint-order="stroke" transform="skewX(-8)">{sub}</text>
</g></svg>'''


def warning_svg():
    stripes = []
    for k in range(-2, 40):
        x = k * 34
        stripes.append(f'<polygon points="{pts([(x, 0), (x + 17, 0), (x - 3, 34), (x - 20, 34)])}" fill="{MAGENTA}"/>')
    band = "".join(stripes)
    slab = [(40, 16), (1008, 16), (984, 176), (16, 176)]
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 192" width="1024" height="192">
<!-- Boss intro warning slab. Generated by Art/Bosses/src~/bosses.py. -->
<defs><clipPath id="s"><polygon points="{pts(slab)}"/></clipPath></defs>
<polygon points="{pts(shift(slab, 8, 8))}" fill="{INK}"/>
<polygon points="{pts(slab)}" fill="{INK}"/>
<g clip-path="url(#s)"><g transform="translate(0 16)">{band}</g><g transform="translate(0 142)">{band}</g>
<polygon points="{pts([(0, 52), (1024, 52), (1024, 140), (0, 140)])}" fill="{INDIGO_0}"/>
<polygon points="{pts([(0, 52), (1024, 52), (1024, 57), (0, 57)])}" fill="{DUSK}"/></g>
<polygon points="{pts(slab)}" fill="none" stroke="{INK}" stroke-width="10" stroke-linejoin="miter"/>
<g font-family="Orbitron" font-weight="700" font-size="76" transform="skewX(-8)">
<text x="532" y="130" text-anchor="middle" fill="{MAGENTA}">WARNING</text>
<text x="526" y="124" text-anchor="middle" fill="{BONE}" stroke="{INK}" stroke-width="9" stroke-linejoin="round" paint-order="stroke">WARNING</text></g>
<g fill="{BONE}" stroke="{INK}" stroke-width="5" stroke-linejoin="round">
<polygon points="{pts([(118, 70), (144, 70), (132, 122), (122, 122)])}"/><polygon points="{pts([(120, 128), (132, 128), (130, 138), (118, 138)])}"/>
<polygon points="{pts([(900, 70), (926, 70), (914, 122), (904, 122)])}"/><polygon points="{pts([(902, 128), (914, 128), (912, 138), (900, 138)])}"/></g>
</svg>'''


# ------------------------------------------------------------- render -----
def resvg(svg_path, png_path, w=None, h=None):
    cmd = ["resvg", "--use-font-file", FONT, "--font-family", "Orbitron"]
    if w:
        cmd += ["-w", str(w)]
    if h:
        cmd += ["-h", str(h)]
    subprocess.run(cmd + [svg_path, png_path], check=True)


def build(key, preview=None):
    src = os.path.join(ART, key, "src~")
    os.makedirs(src, exist_ok=True)
    for old in os.listdir(src):
        if old.endswith(".svg"):
            os.remove(os.path.join(src, old))
    tmp = tempfile.mkdtemp()
    try:
        frames = []
        atlas = Image.new("RGBA", (BODY_COLS * BODY_PX, BODY_ROWS * BODY_PX), (0, 0, 0, 0))
        for i, (name, pose, hold) in enumerate(frame_table(key)):
            svg = compose(key, pose, hold)
            path = os.path.join(src, "%s_%02d_%s.svg" % (key.lower(), i, name))
            with open(path, "w") as f:
                f.write(svg)
            png = os.path.join(tmp, "%02d.png" % i)
            resvg(path, png, BODY_PX, BODY_PX)
            im = Image.open(png).convert("RGBA")
            frames.append((name, im, hold))
            atlas.paste(im, ((i % BODY_COLS) * BODY_PX, (i // BODY_COLS) * BODY_PX))
        os.makedirs(OUT, exist_ok=True)
        atlas.save(os.path.join(OUT, key + ".png"))

        shots = Image.new("RGBA", (SHOT_COLS * SHOT_PX, SHOT_PX), (0, 0, 0, 0))
        shot_frames = []
        for c, name in enumerate(SHOT_CELLS):
            hold = 2
            path = os.path.join(src, "%s_shot_%d_%s.svg" % (key.lower(), c, name))
            with open(path, "w") as f:
                f.write(shot_svg(key, c, hold))
            png = os.path.join(tmp, "s%d.png" % c)
            resvg(path, png, SHOT_PX, SHOT_PX)
            im = Image.open(png).convert("RGBA")
            shot_frames.append(im)
            shots.paste(im, (c * SHOT_PX, 0))
        shots.save(os.path.join(OUT, key + "_shots.png"))

        path = os.path.join(src, "%s_card.svg" % key.lower())
        with open(path, "w") as f:
            f.write(card_svg(key))
        resvg(path, os.path.join(OUT, key + "_card.png"))

        if preview:
            write_preview(key, frames, shot_frames, os.path.join(OUT, key + "_card.png"), preview)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def build_warning():
    src = os.path.join(ART, "src~")
    path = os.path.join(src, "warning.svg")
    with open(path, "w") as f:
        f.write(warning_svg())
    os.makedirs(OUT, exist_ok=True)
    resvg(path, os.path.join(OUT, "warning.png"))


def label_font(size):
    try:
        return ImageFont.truetype(FONT, size)
    except Exception:
        return ImageFont.load_default()


def write_preview(key, frames, shots, card_png, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    lane = LANE[key]
    cell = 256
    groups = [
        ("IDLE", ["idle_0", "idle_1", "idle_2", "idle_3"]),
        ("ATTACK TELLS  (anticipation, charged)  +  FIRE / HIT", ["tell0_a", "tell0_b", "tell1_a", "tell1_b", "tell2_a", "tell2_b", "fire", "hit"]),
        ("DEATH  +  RETREAT", ["death_0", "death_1", "death_2", "death_3", "death_4", "retreat_0", "retreat_1"]),
    ]
    by = {n: im for n, im, h in frames}
    cols = 8
    head = 46
    W = cols * cell + 40
    card = Image.open(card_png).convert("RGBA")
    card_h = int(card.height * (W - 40) / card.width / 2)
    H = 70 + len(groups) * (cell + head + 30) + 150 + card_h + 40
    sheet = Image.new("RGBA", (W, H), lane)
    d = ImageDraw.Draw(sheet)
    name, title = NAMES[key]
    d.text((20, 18), "%s  -  %s  (%s)" % (name, title, key.upper()), font=label_font(30), fill=BONE)
    y = 70
    small = label_font(16)
    for label, names in groups:
        d.text((20, y), label, font=label_font(20), fill="#A3B4CC")
        y += head - 14
        for i, n in enumerate(names):
            im = by[n].resize((cell, cell), Image.LANCZOS)
            sheet.alpha_composite(im, (20 + i * cell, y))
            d.text((24 + i * cell, y + cell - 20), n, font=small, fill="#A3B4CC")
        y += cell + 30
    d.text((20, y), "PROJECTILES  bolt x2 | shard x2 | lane telegraph | beam x2 | muzzle charge", font=label_font(20), fill="#A3B4CC")
    y += 34
    for i, im in enumerate(shots):
        sheet.alpha_composite(im.resize((112, 112), Image.LANCZOS), (20 + i * 124, y))
    y += 130
    sheet.alpha_composite(card.resize((W - 40, card_h * 2 // 2 * 1), Image.LANCZOS).resize(((W - 40) // 2, card_h), Image.LANCZOS), (20, y))
    sheet.convert("RGB").save(os.path.join(out_dir, "boss_%s_sheet.png" % key.lower()))

    # GIF: idle loop, each tell, fire, hit, death -- on the tick table.
    seq = []
    for _ in range(2):
        seq += [("idle_0", 6), ("idle_1", 3), ("idle_2", 3), ("idle_3", 3)]
    for t in (0, 1, 2):
        seq += [("tell%d_a" % t, 2), ("tell%d_b" % t, 10), ("fire", 3), ("idle_0", 4)]
    seq += [("hit", 2), ("idle_0", 4), ("death_0", 2), ("death_1", 3), ("death_2", 3), ("death_3", 4), ("death_4", 6)]
    gif = []
    durs = []
    for n, ticks in seq:
        bg = Image.new("RGBA", (cell, cell), lane)
        bg.alpha_composite(by[n].resize((cell, cell), Image.LANCZOS))
        gif.append(bg.convert("P", palette=Image.ADAPTIVE))
        durs.append(int(ticks * 1000 / 24))
    gif[0].save(os.path.join(out_dir, "boss_%s.gif" % key.lower()), save_all=True, append_images=gif[1:],
                duration=durs, loop=0, disposal=2)


def main(argv):
    preview = None
    only = None
    i = 0
    while i < len(argv):
        if argv[i] == "--preview":
            preview = argv[i + 1]
            i += 2
        elif argv[i] == "--only":
            only = argv[i + 1].split(",")
            i += 2
        else:
            raise SystemExit("unknown argument " + argv[i])
    build_warning()
    for key in BOSSES:
        if only and key not in only:
            continue
        build(key, preview)
        print("boss", key, "ok")


if __name__ == "__main__":
    main(sys.argv[1:])
