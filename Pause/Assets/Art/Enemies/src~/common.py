"""Shared plumbing for the per-world enemy rosters.

Reuses the sample pipeline's helpers (docs/art-samples/src/akira.py) for the
geometry and the fixed layer stack, but every colour here is a palette TOKEN
("@STEEL@"), never a hex: render.sh fills them in from palette.env, so a
restyle is a palette edit and a re-render (same as Art/UI/Tutorial/src~).

Every enemy is one flipbook strip of FRAME_COUNT drawings on a 128 u canvas:

    0..3  idle loop      (key pose hold, anticipation, snap, settle)
    4..5  tell           (attack / anticipation: lunge, arming, wind-up)
    6     hit flash      (flat BONE silhouette, INK contour, 1-2 ticks)

Enemies face DOWN (they fly at the player); the chaser rises from below the
board, so it faces UP. Light comes from the upper left.
"""
import math
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "docs", "art-samples", "src"))

from akira import (f, pts, poly, inkpoly, line, mirror, mx, xf, star, ngon,  # noqa: E402,F401
                   svg as _svg)

FRAME_COUNT = 7
IDLE = (0, 1, 2, 3)
TELL = (4, 5)
HIT = 6


def T(name):
    return "@" + name + "@"


# palette tokens ------------------------------------------------------------
INK, BONE, FLASH_SH = T("INK"), T("BONE"), T("FLASH_SH")
GUN, GUN_SH, GUN_HI = T("GUN"), T("GUN_SH"), T("GUN_HI")
STEEL, STEEL_SH, STEEL_HI = T("STEEL"), T("STEEL_SH"), T("STEEL_HI")
BRUISE, BRUISE_SH, BRUISE_HI = T("BRUISE"), T("BRUISE_SH"), T("BRUISE_HI")
BILE, BILE_SH, BILE_HI, BILE_LIGHT = T("BILE"), T("BILE_SH"), T("BILE_HI"), T("BILE_LIGHT")
ROCK, ROCK_SH, ROCK_HI = T("ROCK"), T("ROCK_SH"), T("ROCK_HI")
MAGENTA, MAGENTA_SH = T("MAGENTA"), T("MAGENTA_SH")
CYAN, TEAL, TEAL_SH = T("CYAN"), T("TEAL"), T("TEAL_SH")
AMBER, SODIUM, SODIUM_SH = T("AMBER"), T("SODIUM"), T("SODIUM_SH")
ICE, ICE_SH, ICE_HI = T("ICE"), T("ICE_SH"), T("ICE_HI")
BARK, BARK_SH, BARK_HI, MOSS = T("BARK"), T("BARK_SH"), T("BARK_HI"), T("MOSS")
CHAR, CHAR_SH, CHAR_HI, OBSIDIAN = T("CHAR"), T("CHAR_SH"), T("CHAR_HI"), T("OBSIDIAN")

import akira as _akira  # noqa: E402


def inkpoly(points, w, color=INK, extra=""):
    """akira.inkpoly, defaulting to the INK token rather than its hex."""
    return _akira.inkpoly(points, w, color, extra)


def line(points, w, color=INK, extra=""):
    """akira.line, defaulting to the INK token rather than its hex."""
    return _akira.line(points, w, color, extra)


# Each world's enemy look, for art that must join a world's cast (the
# per-world bosses): hull base, its one shadow and one highlight, an accent
# material, the signature light and its dimmed tone. Mirrored at runtime by
# EnemyPalette.ThemeFor(world).
WORLD_THEMES = {
    "space":   dict(hull=STEEL, hull_sh=STEEL_SH, hull_hi=STEEL_HI, accent=BRUISE, light=MAGENTA, light_dim=MAGENTA_SH),
    "frost":   dict(hull=ICE, hull_sh=ICE_SH, hull_hi=ICE_HI, accent=STEEL, light=CYAN, light_dim=TEAL_SH),
    "verdant": dict(hull=BILE, hull_sh=BILE_SH, hull_hi=BILE_HI, accent=BRUISE, light=BILE_LIGHT, light_dim=BILE_SH),
    "ember":   dict(hull=CHAR, hull_sh=CHAR_SH, hull_hi=CHAR_HI, accent=GUN, light=SODIUM, light_dim=SODIUM_SH),
}

# A light's dimmed (charging / dormant) tone.
DIM = {MAGENTA: MAGENTA_SH, CYAN: TEAL_SH, TEAL: TEAL_SH, BILE_LIGHT: BILE_SH,
       AMBER: SODIUM_SH, SODIUM: SODIUM_SH, ICE_HI: ICE_SH}


class Parts:
    """The six layers of art-style.md section 6, filled piece by piece."""

    def __init__(self):
        self.glow_back = ""
        self.base = ""
        self.shadow = ""
        self.highlight = ""
        self.ink = ""
        self.detail = ""   # secondary forms that sit on top of the hull ink (vines, leaves, plates)
        self.glow = ""

    def render(self, flash=False):
        if flash:
            return flash_layers(self)
        out = []
        if self.glow_back:
            out.append(f'<g id="glow-back">{self.glow_back}</g>')
        out.append(f'<g id="base">{self.base}</g>')
        out.append(f'<g id="shadow">{self.shadow}</g>')
        out.append(f'<g id="highlight">{self.highlight}</g>')
        out.append(f'<g id="ink">{self.ink}</g>')
        if self.detail:
            out.append(f'<g id="detail">{self.detail}</g>')
        if self.glow:
            out.append(f'<g id="glow">{self.glow}</g>')
        return "\n".join(out)


_FILL = re.compile(r'fill="@[A-Z_]+@"')
_STROKE = re.compile(r'stroke="@(?!INK@)[A-Z_]+@"')


def _bone(src, fill):
    return _STROKE.sub(f'stroke="{BONE}"', _FILL.sub(f'fill="{fill}"', src))


def flash_layers(p):
    """Hit flash: the key pose as one flat BONE cel, one FLASH_SH shadow, the
    same INK contour, no lights. Reads as a white pop for 1-2 ticks."""
    base = _bone(p.base, BONE)
    sh = _bone(p.shadow, FLASH_SH)
    hi = _bone(p.highlight, BONE)
    # INK fills inside the detail layer stay ink; everything else pops white
    det = _STROKE.sub(f'stroke="{BONE}"', re.sub(r'fill="@(?!INK@)[A-Z_]+@"', f'fill="{BONE}"', p.detail))
    ticks = ""
    for k in range(4):  # four hard impact wedges just outside the silhouette
        a = math.radians(45 + k * 90)
        x0, y0 = 64 + 50 * math.cos(a), 64 + 50 * math.sin(a)
        x1, y1 = 64 + 62 * math.cos(a), 64 + 62 * math.sin(a)
        n = (-math.sin(a) * 3, math.cos(a) * 3)
        w = [(x0 + n[0], y0 + n[1]), (x1, y1), (x0 - n[0], y0 - n[1])]
        ticks += poly(w, BONE) + inkpoly(w, 1.5)
    return "\n".join([f'<g id="base">{base}</g>', f'<g id="shadow">{sh}</g>',
                      f'<g id="highlight">{hi}{ticks}</g>', f'<g id="ink">{p.ink}</g>', f'<g id="detail">{det}</g>'])


def svg_doc(body, comment):
    return _svg(128, 128, body, comment)


# shape helpers ---------------------------------------------------------------
def chamfer_rect(cx, cy, w, h, c=None, rot=0):
    """Rectangle with cut corners (never rounded), rotated about its centre."""
    c = min(w, h) * 0.28 if c is None else c
    x0, x1, y0, y1 = cx - w / 2, cx + w / 2, cy - h / 2, cy + h / 2
    p = [(x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c),
         (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c)]
    return xf(p, cx, cy, rot=rot) if rot else p


def lerp(a, b, t):
    return a + (b - a) * t


def lerp_pts(a, b, t):
    return [(lerp(p[0], q[0], t), lerp(p[1], q[1], t)) for p, q in zip(a, b)]


def halo(points, color, op=0.6, filt="glowM"):
    return poly(points, color, f'opacity="{f(op)}" filter="url(#{filt})"')


def halo_circle(cx, cy, r, color, op=0.6, filt="glowM"):
    return (f'<circle cx="{f(cx)}" cy="{f(cy)}" r="{f(r)}" fill="{color}" '
            f'opacity="{f(op)}" filter="url(#{filt})"/>')


def core(p, cx, cy, r, light, level, n=8, rot=22.5, socket=True):
    """A glowing core: INK socket, flat light, BONE heart.
    level 0 dormant, 0.5 lit, 1 hot (halo), 1.5 flare (halo + 4-point star)."""
    if socket:
        sock = ngon(cx, cy, r + 3.5, n, rot)
        p.ink += poly(sock, INK)
    col = light if level >= 0.4 else DIM.get(light, light)
    if level >= 1:
        p.glow_back += halo_circle(cx, cy, r * (2.2 + 0.8 * (level - 1)), light, 0.75)
    body = ngon(cx, cy, r, n, rot)
    p.glow += poly(body, col)
    if level >= 0.4:
        p.glow += poly(ngon(cx, cy, r * (0.38 + 0.25 * min(level, 1.5)), 4, 45), BONE)
    else:
        p.glow += poly(ngon(cx, cy, r * 0.3, 4, 45), light)
    if level >= 1.5:
        s = star(cx, cy, r * 2.4, r * 0.35, 4, 0)
        p.glow += halo(s, light, 0.8, "glowS") + poly(s, BONE)
    if socket:
        p.glow += inkpoly(ngon(cx, cy, r + 3.5, n, rot), 2.5)


def spark(cx, cy, r, color=BONE, rot=0):
    s = star(cx, cy, r, r * 0.22, 4, rot)
    return poly(s, color) + inkpoly(s, 1.2)


def speed_lines(xs, y0, length, color=CYAN, op=0.75, up=True):
    out = ""
    for k, x in enumerate(xs):
        a = y0 + (k % 3) * 5
        b = a + length * (1 if not up else 1)
        out += line([(x, a), (x, b)], 1.5, color, f'opacity="{f(op)}"')
    return out


def arc_bolt(points, color=CYAN):
    """Electric arc: zig-zag light line with a blurred copy (lights only)."""
    return (line(points, 4, color, 'opacity="0.6" filter="url(#glowS)"')
            + line(points, 1.8, color) + line(points, 0.8, BONE))


# cel shading -----------------------------------------------------------------
_uid = [0]


def _d(points):
    return "M" + " L".join(f"{f(x)},{f(y)}" for x, y in points) + " Z"


def shift(points, dx, dy):
    return [(x + dx, y + dy) for x, y in points]


def cel(p, outline, base, sh, hi=None, sh_off=(8, 8), hi_off=(3.5, 3.5), ink_w=4, detail=False):
    """One flat form, cel-painted: BASE fill, ONE hard shadow crescent on the
    lower right (the outline minus a copy nudged toward the light) and ONE thin
    highlight band on the upper-left edge, both clipped to the outline, then
    an INK contour. detail=True draws the whole form on the detail layer (on
    top of the hull's ink), for plates, wings and limbs that overlap it."""
    _uid[0] += 1
    cid = f"c{_uid[0]}"
    clip = f'<clipPath id="{cid}"><path d="{_d(outline)}"/></clipPath>'
    b = clip + poly(outline, base)
    lit = shift(outline, -sh_off[0], -sh_off[1])
    s = f'<path d="{_d(outline)} {_d(lit)}" fill="{sh}" fill-rule="evenodd" clip-path="url(#{cid})"/>'
    h = ""
    if hi:
        inner = shift(outline, hi_off[0], hi_off[1])
        h = f'<path d="{_d(outline)} {_d(inner)}" fill="{hi}" fill-rule="evenodd" clip-path="url(#{cid})"/>'
    k = inkpoly(outline, ink_w) if ink_w else ""
    if detail:
        p.detail += b + s + h + k
    else:
        p.base += b
        p.shadow += s
        p.highlight += h
        p.ink += k
    return cid


def jag(cx, cy, radii, rot=0, sx=1, sy=1):
    """Irregular polygon from a list of radii (angles evenly spread)."""
    n = len(radii)
    return [(cx + r * sx * math.cos(math.radians(rot + i * 360 / n - 90)),
             cy + r * sy * math.sin(math.radians(rot + i * 360 / n - 90))) for i, r in enumerate(radii)]
