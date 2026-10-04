"""Side walls for every world, as flat cel art.

    python3 walls.py            -> Resources/Worlds/<World>/wallLeft.png / wallRight.png
                                   and Space's scene walls Art/left.png / Art/right.png

The walls are the gameS1 / tutorialS5 `leftPipe` / `rightPipe` quads
(1.43 x ~10.8 units at x = +/-3.21). Their material repeats the texture once
per quad and scrolls its UV offset, so each texture is 64 x 448 and loops
vertically (every motif's period divides 448). Only the inner strip is on
screen on a phone (the quad's inner edge is at x = +/-2.495, the view edge at
+/-2.85: about the inner 16 px), so each wall puts its face -- the lit edge,
the lights, the spikes -- in the inner 20 px and keeps the outer body plain.
The left wall's inner edge is the texture's right side; the right wall is
the mirror image. Rail mines grip that inner edge.

Rules (docs/art-style.md): flat fills only, at most one shadow and one
highlight tone per material, background ink outlines, crisp pixel edges (no
anti-aliased ramps), lights as small flat shapes with a stepped hard glow.
"""
import os
import subprocess

from PIL import Image

from bgkit import ART_WORLDS, RESOURCES, doc
from palette import (BG_INK, AMBER, SODIUM, SODIUM_SH, CYAN, TEAL, TEAL_SH, BONE, INDIGO_0, NIGHT_0)

W, H = 64, 448
FACE = 44            # x where the inner face (the on-screen strip) starts
RED = "#D8232C"      # Kaneda red: running lights only (tiny, on the frame)

CRISP = 'shape-rendering="crispEdges"'


def rect(x, y, w, h, c, extra=""):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{c}" {CRISP} {extra}/>'


def poly(points, c, extra=""):
    p = " ".join(f"{x},{y}" for x, y in points)
    return f'<polygon points="{p}" fill="{c}" {CRISP} {extra}/>'


def tiled(period, fn):
    """fn(y0) -> svg for one period starting at y0; repeated to cover the
    tile plus one period either side so it loops."""
    assert H % period == 0, period
    return "".join(fn(y0) for y0 in range(-period, H + period, period))


def light(x, y, w, h, c, glow):
    """A flat running light with a two-step hard glow (square halos)."""
    return (rect(x - 3, y - 3, w + 6, h + 6, glow, 'opacity="0.35"')
            + rect(x - 1, y - 1, w + 2, h + 2, glow, 'opacity="0.6"')
            + rect(x, y, w, h, c))


# ------------------------------------------------------------------ Space ---

def space():
    """Panel-lined hull girders: an indigo hull body with panel seams and
    rivets, a lit girder face with a truss, red / amber running lights."""
    hull, hull_sh, hull_hi = "#1A1C3A", "#101228", "#2C3366"
    face, face_sh, face_hi = "#23274C", "#14162E", "#4A5288"
    b = [rect(0, 0, W, H, hull), rect(0, 0, 10, H, hull_sh)]

    def panel(y):
        s = [rect(0, y, FACE, 1, BG_INK), rect(0, y + 1, FACE, 2, hull_hi),
             rect(26, y, 1, 64, BG_INK), rect(27, y + 1, 2, 63, hull_hi)]
        for x in (4, 20, 34):
            s.append(rect(x, y + 6, 2, 2, hull_hi) + rect(x, y + 56, 2, 2, hull_hi))
        s.append(rect(12, y + 26, 10, 12, hull_sh) + rect(12, y + 26, 10, 1, BG_INK))
        return "".join(s)
    b.append(tiled(64, panel))
    # girder face: base, one shadow (outer half), one highlight (inner lip)
    b.append(rect(FACE, 0, W - FACE, H, face))
    b.append(rect(FACE, 0, 6, H, face_sh))
    b.append(rect(W - 4, 0, 2, H, face_hi))

    def truss(y):
        s = []
        for i in range(16):        # stepped diagonal brace (pixel-snapped)
            s.append(rect(FACE + 6 + (i * 12) // 16, y + i * 2, 2, 2, face_sh))
        s.append(rect(FACE, y + 31, W - FACE, 2, BG_INK) + rect(FACE, y + 33, W - FACE - 4, 1, face_hi))
        return "".join(s)
    b.append(tiled(32, truss))

    def lights(y):
        # amber running lights; one red in every seven panels (a tiny accent)
        red = (y // 64) % 7 == 3
        return (light(53, y + 12, 3, 4, AMBER, SODIUM)
                + light(53, y + 44, 3, 4, RED if red else AMBER, RED if red else SODIUM))
    b.append(tiled(64, lights))
    b.append(rect(FACE - 1, 0, 1, H, BG_INK))
    b.append(rect(W - 2, 0, 2, H, BG_INK))
    return "".join(b)


# ------------------------------------------------------------------ Frost ---

def frost():
    """Ice-crystal pillars: a dark ice column with block joints, a lit
    pillar face and angular crystals jutting inward (one shadow facet, one
    rim kick), small cyan glints."""
    col, col_sh, col_hi = "#0F2134", "#091626", "#1D4058"
    face, face_sh = "#16324A", "#0F2134"
    ice, ice_sh, ice_hi = "#3F7FA0", "#245070", "#9FE8F0"
    b = [rect(0, 0, W, H, col), rect(0, 0, 12, H, col_sh)]

    def block(y):
        return (rect(0, y, FACE, 1, BG_INK) + rect(0, y + 1, FACE, 2, col_hi)
                + rect(18, y + 20, 2, 24, col_hi) + rect(30, y + 36, 2, 16, col_sh))
    b.append(tiled(56, block))
    b.append(rect(FACE - 8, 0, W - FACE + 8, H, face))
    b.append(rect(FACE - 8, 0, 6, H, face_sh))
    b.append(rect(FACE - 9, 0, 1, H, BG_INK))

    def crystal(y):
        # a long and a short crystal per period, pointing at the playfield
        s = [poly([(W - 18, y + 2), (W, y + 14), (W, y + 18), (W - 18, y + 30)], ice),
             poly([(W - 18, y + 16), (W, y + 16), (W, y + 18), (W - 18, y + 30)], ice_sh),
             poly([(W - 18, y + 2), (W - 4, y + 12), (W - 6, y + 13), (W - 18, y + 6)], ice_hi),
             f'<polygon points="{W - 18},{y + 2} {W},{y + 14} {W},{y + 18} {W - 18},{y + 30}" fill="none" '
             f'stroke="{BG_INK}" stroke-width="1.5" {CRISP}/>',
             poly([(W - 12, y + 36), (W, y + 44), (W - 12, y + 52)], ice),
             poly([(W - 12, y + 44), (W, y + 44), (W - 12, y + 52)], ice_sh),
             f'<polygon points="{W - 12},{y + 36} {W},{y + 44} {W - 12},{y + 52}" fill="none" '
             f'stroke="{BG_INK}" stroke-width="1.5" {CRISP}/>',
             rect(FACE - 4, y + 22, 2, 2, CYAN), rect(FACE - 2, y + 48, 2, 2, TEAL)]
        return "".join(s)
    b.append(tiled(56, crystal))

    def window(y):
        return light(FACE - 5, y + 70, 2, 3, AMBER, SODIUM)
    b.append(tiled(112, window))
    return "".join(b)


# ---------------------------------------------------------------- Verdant ---

def verdant():
    """Vine-wrapped stone columns: indigo stone blocks with ink joints and a
    lit face, a green vine zig-zagging round the column with leaf
    triangles, small cyan / sodium glyphs glowing in the stone."""
    st, st_sh, st_hi = "#2A3254", "#161B32", "#46527E"
    face = "#323C64"
    vine, vine_hi = "#1E6A46", "#3A9A62"
    b = [rect(0, 0, W, H, st), rect(0, 0, 12, H, st_sh)]

    def blocks(y):
        return (rect(0, y, W, 2, BG_INK) + rect(0, y + 2, W, 2, st_hi)
                + rect(22, y + 2, 2, 54, BG_INK) + rect(24, y + 4, 2, 52, st_hi)
                + rect(0, y + 30, 22, 2, BG_INK))
    b.append(rect(FACE, 0, W - FACE, H, face))
    b.append(tiled(56, blocks))
    b.append(rect(FACE, 0, 4, H, st_sh))
    b.append(rect(W - 5, 0, 3, H, st_hi))

    def vines(y):
        s = []
        # stepped diagonal stem across the column, then back
        for i in range(28):
            x = 4 + (i * 56) // 28
            s.append(rect(x, y + i * 2, 4, 4, vine))
            s.append(rect(x, y + i * 2, 4, 1, vine_hi))
        for i in range(28):
            x = 60 - (i * 56) // 28
            s.append(rect(x - 4, y + 56 + i * 2, 4, 4, vine))
            s.append(rect(x - 4, y + 56 + i * 2, 4, 1, vine_hi))
        for k, (lx, ly) in enumerate([(18, 16), (38, 34), (50, 74), (30, 92)]):
            d = 1 if k % 2 == 0 else -1
            s.append(poly([(lx, y + ly), (lx + 8 * d, y + ly - 6), (lx + 6 * d, y + ly + 2)], vine))
            s.append(poly([(lx, y + ly), (lx + 8 * d, y + ly - 6), (lx + 3 * d, y + ly - 1)], vine_hi))
        return "".join(s)
    b.append(tiled(112, vines))

    def glyphs(y):
        def g(cx, cy, c):
            return (rect(cx - 4, cy - 4, 8, 8, c, 'opacity="0.35"')
                    + poly([(cx, cy - 3), (cx + 3, cy), (cx, cy + 3), (cx - 3, cy)], c))
        return g(56, y + 44, CYAN) + g(56, y + 100, SODIUM) + g(36, y + 70, TEAL)
    b.append(tiled(112, glyphs))
    b.append(rect(W - 2, 0, 2, H, BG_INK))
    return "".join(b)


# ------------------------------------------------------------------ Ember ---

def ember():
    """Basalt columns: dark faceted columns with one shadow facet and a lit
    edge, cracked into drums whose joints glow with flat magma seams
    (sodium with an amber core), never red."""
    ba, ba_sh, ba_hi = "#2A1416", "#170A0C", "#4A2420"
    b = [rect(0, 0, W, H, ba)]
    cols = [(0, 14), (14, 30), (30, 46), (46, W)]
    for i, (x0, x1) in enumerate(cols):
        b.append(rect(x0 + (x1 - x0) // 2, 0, (x1 - x0) - (x1 - x0) // 2, H, ba_sh))
        b.append(rect(x0 + 1, 0, 2, H, ba_hi))
        b.append(rect(x0, 0, 1, H, BG_INK))

    def drums(y):
        s = []
        for i, (x0, x1) in enumerate(cols):
            oy = y + (i * 23) % 64
            s.append(rect(x0, oy, x1 - x0, 2, BG_INK))
            if i in (1, 3):
                s.append(rect(x0 + 2, oy, x1 - x0 - 4, 2, SODIUM) + rect(x0 + 4, oy, x1 - x0 - 10, 1, AMBER)
                         + rect(x0 + 2, oy - 2, x1 - x0 - 4, 2, SODIUM_SH, 'opacity="0.7"'))
        return "".join(s)
    b.append(tiled(64, drums))

    def seam(y):
        # a magma vein running down the inner column, stepped
        s = []
        for i in range(16):
            x = 54 + (2 if (i // 4) % 2 else 0)
            hot = 4 <= i <= 9                     # only part of the vein is molten
            s.append(rect(x, y + i * 4, 2, 4, SODIUM if hot else SODIUM_SH))
            if hot:
                s.append(rect(x - 2, y + i * 4, 6, 4, SODIUM_SH, 'opacity="0.45"'))
        s.append(rect(54, y + 26, 2, 3, AMBER))
        return "".join(s)
    b.append(tiled(64, seam))
    b.append(rect(W - 2, 0, 2, H, BG_INK))
    return "".join(b)


WALLS = {"Space": space, "Frost": frost, "Verdant": verdant, "Ember": ember}


def out_paths(world):
    if world == "Space":
        art = os.path.dirname(ART_WORLDS)
        return os.path.join(art, "left.png"), os.path.join(art, "right.png")
    d = os.path.join(RESOURCES, world)
    return os.path.join(d, "wallLeft.png"), os.path.join(d, "wallRight.png")


def build(world):
    src = os.path.join(ART_WORLDS, "src~", "walls")
    os.makedirs(src, exist_ok=True)
    svg = os.path.join(src, world.lower() + "_wall.svg")
    with open(svg, "w") as f:
        f.write(doc(W, H, WALLS[world]()))
    left, right = out_paths(world)
    subprocess.run(["resvg", svg, left], check=True)
    im = Image.open(left).convert("RGBA")
    # Fully opaque: the walls hide the backdrop's edges.
    bg = Image.new("RGBA", im.size, (0, 0, 0, 255))
    bg.alpha_composite(im)
    bg.save(left, optimize=True)
    bg.transpose(Image.FLIP_LEFT_RIGHT).save(right, optimize=True)


if __name__ == "__main__":
    for w in WALLS:
        build(w)
