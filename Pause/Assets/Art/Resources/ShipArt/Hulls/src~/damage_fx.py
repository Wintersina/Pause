"""The damage-FX flipbooks every hurt hull draws from (ShipDamageFx).

    python3 damage_fx.py [--preview <dir>]

Writes ../../DamageFx.png (Resources/ShipArt/DamageFx), a grid of 64 px
cells, 4 columns x 8 rows, flat cartoon cels with INK outlines in the hull
style's palette (docs/art-style.md), each row a 4-drawing flipbook:

    0  crackle   the spark flash on a broken panel (pinch, star, break up)
    1  spark     one flying spark, a streak pointing up (+y)
    2  arc       an electrical arc across the cell, left to right
    3  smoke     an engine smoke puff: pop, swell, break, fade-thin
    4  flame     a small flame lick, base at the top, tip pointing down
    5  drop      a fuel / coolant droplet, WHITE fill (tinted in game to
                 the hull's colour), round, stretched, falling, splat
    6  chunk     hull debris, WHITE fill (tinted to the hull's colour),
                 four shapes
    7  scrap     dark metal debris (GUN tones), four shapes
    8  foam      fire-retardant foam landing on a hot spot: a bubbly white /
                 pale-blue splat that pops, swells, breaks up and thins out
    9  spray     retardant spray in flight, a streak of droplets pointing up
                 (+y): a fat jet, a thinner jet, a droplet trio, a fine mist

ShipDamageFx slices the same layout (ShipDamageFx.Row*).
"""
import math
import os
import random
import subprocess
import sys
import tempfile

from PIL import Image

from hullkit import *  # noqa: F401,F403

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "DamageFx.png"))
CELL = 64
COLS, ROWS = 4, 10
WHITE = "#FFFFFF"
WHITE_SH = "#B4B4BC"   # the tintable shadow (multiplied by the hull colour)


def jag(rng, x0, y0, x1, y1, steps, amp):
    out = [(x0, y0)]
    for i in range(1, steps):
        t = i / steps
        out.append((x0 + (x1 - x0) * t + rng.uniform(-1, 1) * 1.5, y0 + (y1 - y0) * t + rng.uniform(-amp, amp)))
    out.append((x1, y1))
    return out


def crackle(k):
    c = 32
    if k == 0:
        return poly(star(c, c, 9, 3, 4, rot=45), BONE) + inkpoly(star(c, c, 9, 3, 4, rot=45), 2.5)
    if k == 1:
        s = poly(star(c, c, 24, 7, 6, rot=10), AMBER) + inkpoly(star(c, c, 24, 7, 6, rot=10), 3)
        return s + poly(star(c, c, 11, 4, 6, rot=10), BONE)
    if k == 2:
        s = poly(star(c, c, 20, 9, 5, rot=40), SODIUM) + inkpoly(star(c, c, 20, 9, 5, rot=40), 3)
        return s + poly(star(c, c, 9, 4, 5, rot=40), AMBER)
    s = ""
    for a in (20, 140, 260):
        x, y = c + 16 * math.cos(math.radians(a)), c + 16 * math.sin(math.radians(a))
        s += poly(ngon(x, y, 4.5, 4, a), SODIUM_SH) + inkpoly(ngon(x, y, 4.5, 4, a), 2)
    return s


def spark(k):
    # a hot streak pointing up the cell (+y in Unity)
    L = [26, 22, 16, 10][k]
    w = [6, 5, 4, 3][k]
    body = [(32, 32 - L), (32 + w, 32 + 2), (32, 32 + L * .35), (32 - w, 32 + 2)]
    s = poly(body, AMBER if k < 2 else SODIUM) + inkpoly(body, 2.5)
    core = [(32, 32 - L * .7), (32 + w * .45, 32), (32, 32 + L * .15), (32 - w * .45, 32)]
    return s + poly(core, BONE)


def arc(k):
    rng = random.Random(77 + k)
    p = jag(rng, 2, 32, 62, 32, 7, 9)
    s = line(p, 8, INK) + line(p, 4.5, CYAN) + line(p, 1.6, BONE)
    i = rng.randint(2, 5)
    x, y = p[i]
    b = jag(rng, x, y, x + rng.uniform(6, 12), y + rng.choice([-1, 1]) * rng.uniform(10, 16), 3, 3)
    s += line(b, 5, INK) + line(b, 2.4, CYAN)
    if k % 2 == 0:
        s += poly(star(x, y, 6, 2, 4, rot=45), BONE) + inkpoly(star(x, y, 6, 2, 4, rot=45), 1.5)
    return s


def puff(cx, cy, r, base, sh):
    o = ngon(cx, cy, r, 9, 7, sy=.9)
    lobes = [ngon(cx + r * .55 * math.cos(a), cy + r * .5 * math.sin(a), r * .55, 8)
             for a in (math.radians(d) for d in (200, 300, 60))]
    s = ""
    for shape in [o] + lobes:
        s += inkpoly(shape, 5)
    for shape in [o] + lobes:
        s += poly(shape, base)
    s += poly(ngon(cx + r * .3, cy + r * .3, r * .55, 8), sh)
    return s


def smoke(k):
    if k == 0:
        return puff(32, 32, 9, GUN_HI, ROCK)
    if k == 1:
        return puff(32, 32, 16, ROCK_HI, ROCK)
    if k == 2:
        return puff(32, 30, 20, ROCK_HI, ROCK)
    s = ""
    for x, y, r in ((20, 26, 9), (42, 30, 10), (30, 44, 7)):
        s += puff(x, y, r, ROCK, ROCK_SH)
    return s


def flame(k):
    # base at y=8, tip pointing down the cell (Unity -y)
    L = [44, 52, 40, 48][k]
    w = [11, 12, 10, 11][k]
    sway = [0, 4, -3, 2][k]
    o = [(32 - w, 10), (32 - w * .7, 4), (32 + w * .7, 4), (32 + w, 10), (32 + w * .6, 10 + L * .5),
         (32 + sway, 8 + L), (32 - w * .6, 10 + L * .45)]
    m = [(32 - w * .6, 9), (32 + w * .6, 9), (32 + w * .3, 8 + L * .45), (32 + sway * .7, 8 + L * .75),
         (32 - w * .35, 8 + L * .4)]
    i = [(32 - w * .3, 8), (32 + w * .3, 8), (32 + sway * .3, 8 + L * .38)]
    return (poly(o, RED) + inkpoly(o, 3) + poly(m, SODIUM) + poly(i, AMBER)
            + poly([(32 - 2, 8), (32 + 2, 8), (32, 8 + L * .18)], BONE))


def drop(k):
    if k == 0:
        o = ngon(32, 34, 9, 10)
    elif k == 1:
        o = [(32, 12), (40, 30), (40, 40), (32, 48), (24, 40), (24, 30)]
    elif k == 2:
        o = [(32, 18), (38, 32), (38, 42), (32, 50), (26, 42), (26, 32)]
    else:
        o = [(14, 40), (24, 34), (32, 38), (42, 32), (50, 40), (40, 44), (24, 44)]
    xs = [p[0] for p in o]
    cx = (min(xs) + max(xs)) / 2
    s = poly(o, WHITE) + f'<clipPath id="d{k}"><polygon points="{pts(o)}"/></clipPath>'
    s += f'<g clip-path="url(#d{k})">' + poly(box(cx + 1, 0, 64, 64), WHITE_SH) + "</g>"
    s += inkpoly(o, 3)
    if k < 3:
        s += poly(ngon(cx - 3, 30, 2.2, 4), BONE)
    return s


CHUNKS = [
    [(18, 22), (40, 16), (48, 30), (36, 46), (20, 40)],
    [(22, 18), (46, 24), (42, 44), (16, 38)],
    [(16, 30), (30, 14), (50, 24), (44, 34), (28, 48)],
    [(20, 20), (44, 18), (40, 30), (48, 44), (24, 42)],
]


def chunk(k, base, sh, hi):
    o = CHUNKS[k]
    s = poly(o, base) + f'<clipPath id="c{k}{base[1:]}"><polygon points="{pts(o)}"/></clipPath>'
    s += f'<g clip-path="url(#c{k}{base[1:]})">' + poly(box(34, 0, 64, 64), sh) + poly(box(0, 0, 64, 22), hi) + "</g>"
    s += inkpoly(o, 3.5)
    return s


FOAM = "#F4FBFF"       # retardant: cold white
FOAM_SH = "#A9D8F2"    # its pale-blue shade
FOAM_DEEP = "#5FA8D8"


def bubbles(cx, cy, r, n, seed):
    rng = random.Random(seed)
    s = ""
    for _ in range(n):
        a = rng.uniform(0, math.tau)
        d = rng.uniform(0, r)
        x, y, rr = cx + math.cos(a) * d, cy + math.sin(a) * d, rng.uniform(r * .16, r * .3)
        s += poly(ngon(x, y, rr, 8), FOAM) + inkpoly(ngon(x, y, rr, 8), 1.6, FOAM_DEEP)
    return s


def foam_blob(cx, cy, r, seed):
    """A bubbly foam splat: lobed white body, pale-blue shade, bubble rims."""
    rng = random.Random(seed)
    lobes = [ngon(cx, cy, r * .72, 10)]
    for i in range(6):
        a = math.tau * i / 6 + rng.uniform(-.3, .3)
        d = r * rng.uniform(.45, .6)
        lobes.append(ngon(cx + math.cos(a) * d, cy + math.sin(a) * d, r * rng.uniform(.34, .46), 8))
    s = "".join(inkpoly(o, 4.5) for o in lobes)
    s += "".join(poly(o, FOAM) for o in lobes)
    s += poly(ngon(cx + r * .28, cy + r * .3, r * .5, 9), FOAM_SH)
    s += bubbles(cx - r * .1, cy - r * .1, r * .55, 4, seed + 1)
    return s


def foam(k):
    if k == 0:
        return foam_blob(32, 32, 11, 3)
    if k == 1:
        return foam_blob(32, 32, 19, 5)
    if k == 2:
        s = foam_blob(30, 30, 16, 7)
        for x, y, r in ((50, 16, 4), (12, 46, 3.5), (52, 48, 3)):
            s += poly(ngon(x, y, r, 8), FOAM) + inkpoly(ngon(x, y, r, 8), 2)
        return s
    s = ""
    for x, y, r in ((20, 24, 7), (42, 28, 8), (30, 44, 6), (50, 48, 3.5)):
        s += poly(ngon(x, y, r, 8), FOAM_SH) + inkpoly(ngon(x, y, r, 8), 2.2)
        s += poly(ngon(x - r * .3, y - r * .3, r * .35, 6), FOAM)
    return s


def spray(k):
    # a streak of retardant flying up the cell (+y in Unity)
    if k < 2:
        L, w = (26, 8) if k == 0 else (24, 5.5)
        body = [(32, 32 - L), (32 + w, 32 - L * .2), (32 + w * .8, 32 + L * .5), (32, 32 + L * .7),
                (32 - w * .8, 32 + L * .5), (32 - w, 32 - L * .2)]
        s = inkpoly(body, 3) + poly(body, FOAM_SH)
        core = [(32, 32 - L * .85), (32 + w * .45, 32 - L * .2), (32, 32 + L * .4), (32 - w * .45, 32 - L * .2)]
        s += poly(core, FOAM)
        for y in (32 + L * .85, 32 + L * 1.0):
            s += poly(ngon(32 + (3 if k else -3), y, 2.6, 6), FOAM) + inkpoly(ngon(32 + (3 if k else -3), y, 2.6, 6), 1.6)
        return s
    if k == 2:
        s = ""
        for x, y, r in ((32, 16, 6), (24, 34, 4.5), (39, 44, 4)):
            s += poly(ngon(x, y, r, 8), FOAM) + inkpoly(ngon(x, y, r, 8), 2.2)
            s += poly(ngon(x + r * .3, y + r * .3, r * .45, 6), FOAM_SH)
        return s
    s = ""
    for x, y, r in ((30, 18, 3.5), (38, 28, 3), (26, 34, 3), (34, 44, 2.5), (28, 52, 2)):
        s += poly(ngon(x, y, r, 6), FOAM_SH) + inkpoly(ngon(x, y, r, 6), 1.6)
    return s


ROW_FNS = [crackle, spark, arc, smoke, flame, drop,
           lambda k: chunk(k, WHITE, WHITE_SH, WHITE),
           lambda k: chunk(k, GUN, GUN_SH, GUN_HI),
           foam, spray]


def build():
    sheet = Image.new("RGBA", (COLS * CELL, ROWS * CELL), (0, 0, 0, 0))
    with tempfile.TemporaryDirectory() as tmp:
        for r, fn in enumerate(ROW_FNS):
            for k in range(COLS):
                path = os.path.join(tmp, f"{r}_{k}.svg")
                with open(path, "w") as fh:
                    fh.write(svg(CELL, CELL, fn(k), f"damage fx row {r} drawing {k}. Generated by damage_fx.py"))
                png = path[:-4] + ".png"
                subprocess.run(["resvg", path, png], check=True)
                im = Image.open(png).convert("RGBA")
                a = im.getchannel("A").point(lambda v: 0 if v < 4 else v)
                im.putalpha(a)
                sheet.alpha_composite(im, (k * CELL, r * CELL))
    sheet.save(OUT, optimize=True)
    return sheet


def main():
    sheet = build()
    print("wrote", OUT, sheet.size)
    if "--preview" in sys.argv:
        out = sys.argv[sys.argv.index("--preview") + 1]
        os.makedirs(out, exist_ok=True)
        bg = Image.new("RGBA", sheet.size, (14, 20, 36, 255))
        bg.alpha_composite(sheet)
        bg.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST).save(os.path.join(out, "damage_fx_atlas.png"))


if __name__ == "__main__":
    main()
