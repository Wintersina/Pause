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
        # Optional SVG transform for the whole drawing (the floating rocks'
        # drawn bob / wobble). The hit flash always uses the key pose (frame
        # 0), which leaves this empty.
        self.xform = ""

    def render(self, flash=False):
        if flash:
            return flash_layers(self)
        body = self._layers()
        return f'<g transform="{self.xform}">\n{body}\n</g>' if self.xform else body

    def _layers(self):
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


# detail toolkit (the "more Akira, more detail" pass) -----------------------
# Everything here stays inside the flat-cel rules: flat fills, INK lines,
# BONE kicks, and lights that are flat shapes with a HARD bloom (a flat
# translucent copy, never a blur) so they read as cel-painted light. Most
# helpers draw on the detail layer (above the hull ink); pass layer= to
# change that.

def plane(p, cid, points, color):
    """A hard shadow plane: an angular polygon clipped to a cel() form (pass
    the cid cel() returns). Cut it like 80s mecha shading -- one confident
    facet, not a crescent. Goes on the shadow layer (for base forms)."""
    p.shadow += poly(points, color, f'clip-path="url(#{cid})"')


def plane_svg(cid, points, color):
    """plane() as a string, for forms drawn on the detail layer: append it
    right after the cel(..., detail=True) call."""
    return poly(points, color, f'clip-path="url(#{cid})"')


def hexlight(p, x, y, r, lv, light, rot=30, sy=1.0):
    """A hard-edged hex light: INK socket, flat light, BONE heart, and a small
    hard bloom (a flat translucent hex, plus four hard rays when hot).
    lv: <0.4 dim, 0.5 lit, 1 hot, 1.5 flare."""
    sock = ngon(x, y, r + 2.6, 6, rot, 1, sy)
    p.detail += poly(sock, INK)
    on = lv >= 0.4
    k = min(lv, 1.5)
    if on:
        p.glow += poly(ngon(x, y, r * (1.55 + 0.35 * k), 6, rot, 1, sy), light, f'opacity="{0.22 + 0.12 * k:.2f}"')
    if lv >= 1:
        p.glow += poly(star(x, y, r * (1.6 + 0.5 * (k - 1)), r * 0.26, 4, 0), light, 'opacity="0.6"')
    p.glow += poly(ngon(x, y, r, 6, rot, 1, sy), light if on else DIM.get(light, light))
    if on:
        p.glow += poly(ngon(x, y, r * (0.28 + 0.16 * k), 6, rot, 1, sy), BONE)


def slitlight(p, x, y, w, h, lv, light, rot=0):
    """A slit light (visor / sensor bar): INK slot, flat light bar, BONE core
    line, hard flat bloom when lit."""
    def box(hw, hh):
        return xf([(x - hw, y - hh), (x + hw, y - hh), (x + hw, y + hh), (x - hw, y + hh)], x, y, rot=rot)
    p.detail += poly(box(w / 2 + 2, h / 2 + 2), INK)
    on = lv >= 0.4
    if on:
        p.glow += poly(box(w / 2 + 4, h / 2 + 3), light, f'opacity="{0.2 + 0.15 * min(lv, 1.5):.2f}"')
    p.glow += poly(box(w / 2, h / 2), light if on else DIM.get(light, light))
    if on:
        p.glow += line(xf([(x - w * 0.32, y), (x + w * 0.32, y)], x, y, rot=rot), max(0.8, h * 0.3), BONE)


def _add(p, layer, s):
    setattr(p, layer, getattr(p, layer) + s)


def rivets(p, points, r=1.5, color=None, layer="detail"):
    """Bolt heads: tiny flat squares with an INK rim."""
    color = color or GUN_HI
    s = ""
    for x, y in points:
        q = ngon(x, y, r, 4, 45)
        s += poly(q, color) + inkpoly(q, 0.9)
    _add(p, layer, s)


def seams(p, lines_, w=1.5, layer="detail"):
    """Panel lines / plating breaks: thin INK polylines."""
    _add(p, layer, "".join(line(l, w) for l in lines_))


def grille(p, x, y, w, h, n=3, rot=0, bars=None, layer="detail"):
    """A vent grille: INK slot with n flat bars across it."""
    bars = bars or GUN_HI
    slot = xf([(x - w / 2, y - h / 2), (x + w / 2, y - h / 2), (x + w / 2, y + h / 2), (x - w / 2, y + h / 2)], x, y, rot=rot)
    s = poly(slot, INK)
    for k in range(n):
        yy = y - h / 2 + h * (k + 0.5) / n
        s += line(xf([(x - w / 2 + 1.2, yy), (x + w / 2 - 1.2, yy)], x, y, rot=rot), max(0.7, h / n * 0.45), bars)
    _add(p, layer, s)


def spec(p, points, w=1.4, color=None, layer="detail"):
    """A hard specular kick: a thin BONE sliver along a lit edge."""
    _add(p, layer, line(points, w, color or BONE))


# kanji-like glyph decals: strokes on a 3x3 grid (0..2), deterministic.
_GLYPHS = [
    [((0, 0), (2, 0)), ((1, 0), (1, 2)), ((0, 2), (2, 2))],
    [((0, 0), (0, 2)), ((0, 1), (2, 1)), ((2, 0), (2, 2))],
    [((0, 0), (2, 0)), ((2, 0), (2, 2)), ((0, 1), (2, 1)), ((1, 1), (0, 2))],
    [((1, 0), (1, 2)), ((0, 1), (2, 1)), ((0, 2), (2, 2))],
    [((0, 0), (2, 0)), ((0, 0), (0, 2)), ((0, 2), (2, 2)), ((1, 0), (1, 1))],
    [((0, 0), (2, 2)), ((2, 0), (1, 1)), ((0, 2), (2, 2))],
]


def glyph(p, x, y, s, seed=0, color=None, w=1.1, rot=0, layer="detail"):
    """A tiny kanji-like decal (unit markings); s = grid cell size in u."""
    color = color or BONE
    out = ""
    for a, b in _GLYPHS[seed % len(_GLYPHS)]:
        pa = (x + (a[0] - 1) * s, y + (a[1] - 1) * s)
        pb = (x + (b[0] - 1) * s, y + (b[1] - 1) * s)
        out += line(xf([pa, pb], x, y, rot=rot), w, color)
    _add(p, layer, out)


def stripes(p, x, y, w, h, n=3, rot=0, color=None, layer="detail"):
    """Warning stripes: a BONE (or given colour) plate with n hard diagonal
    INK bars."""
    color = color or BONE
    plate = xf([(x - w / 2, y - h / 2), (x + w / 2, y - h / 2), (x + w / 2, y + h / 2), (x - w / 2, y + h / 2)], x, y, rot=rot)
    s = poly(plate, color)
    step = w / n
    for k in range(n):
        x0 = x - w / 2 + k * step
        bar = [(x0, y + h / 2), (x0 + step * 0.45, y + h / 2), (x0 + step * 0.95, y - h / 2), (x0 + step * 0.5, y - h / 2)]
        bar = [(min(max(px, x - w / 2), x + w / 2), py) for px, py in bar]
        s += poly(xf(bar, x, y, rot=rot), INK)
    s += inkpoly(plate, 1.1)
    _add(p, layer, s)


def antenna(p, x, y, length, rot, lv, light, tip=2.2, layer="detail"):
    """A thin antenna / sensor whisker with a blinking tip light."""
    tipp = xf([(x, y - length)], x, y, rot=rot)[0]
    _add(p, layer, line([(x, y), tipp], 2.6) + line([(x, y), tipp], 1.1, GUN_HI))
    p.detail += poly(ngon(tipp[0], tipp[1], tip + 1.4, 4, 45), INK)
    p.glow += poly(ngon(tipp[0], tipp[1], tip, 4, 45), light if lv >= 0.4 else DIM.get(light, light))
    if lv >= 1:
        p.glow += poly(ngon(tipp[0], tipp[1], tip * 2.2, 4, 45), light, 'opacity="0.3"')


def teeth(p, a, b, n, length, color=None, inward=1, layer="detail"):
    """A row of n hard triangular teeth along edge a->b, pointing to the
    edge's left (inward=1) or right (-1)."""
    color = color or BONE
    (ax, ay), (bx, by) = a, b
    dx, dy = bx - ax, by - ay
    L = math.hypot(dx, dy) or 1
    nx, ny = -dy / L * inward, dx / L * inward
    s = ""
    for k in range(n):
        t0, t1 = k / n, (k + 1) / n
        p0 = (ax + dx * t0, ay + dy * t0)
        p1 = (ax + dx * t1, ay + dy * t1)
        tip = ((p0[0] + p1[0]) / 2 + nx * length, (p0[1] + p1[1]) / 2 + ny * length)
        tri = [p0, p1, tip]
        s += poly(tri, color) + inkpoly(tri, 1.1)
    _add(p, layer, s)


def scales(p, cx, cy, rows, cols, size, color, sh, layer="detail"):
    """Chitin / scale plates: a grid of small diamond plates, each with its
    one shadow half and an INK rim."""
    s = ""
    for r in range(rows):
        for c in range(cols):
            x = cx + (c - (cols - 1) / 2) * size + (size / 2 if r % 2 else 0)
            y = cy + r * size * 0.75
            q = [(x - size / 2, y), (x, y - size * 0.45), (x + size / 2, y), (x, y + size * 0.45)]
            s += poly(q, color) + poly([q[1], q[2], q[3]], sh) + inkpoly(q, 1)
    _add(p, layer, s)


def teal_glint(p, x, y, r):
    """A tiny teal accent glint (neon reflected off a hard edge)."""
    p.glow += spark(x, y, r, CYAN, 45)
