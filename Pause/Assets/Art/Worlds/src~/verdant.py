"""Verdant: a night jungle in flat anime cels. Spiky canopy far below,
layered green ridges, a river with travelling highlight dashes, cliffs with
cascading waterfalls, overgrown ruin towers with blinking neon glyphs,
pulsing signal obelisks, fireflies and drifting spores."""
import math
import random

from bgkit import (TAU, World, doc, blur, lin, wrap_y, PNoise, edge_range, pts, ink_attr, hard_glow,
                   jitter_ridge, grain_defs, grain)
from palette import VERDANT as P
import space

W, H = 512, 1024


def crown(rnd, x, y, r, base, hi, dy=0, ink=None):
    """Top-down tree crown: a spiky flat star with one highlight wedge."""
    n = rnd.randint(6, 8)
    a0 = rnd.uniform(0, TAU)
    outer = []
    for i in range(n * 2):
        a = a0 + i * math.pi / n
        rr = r if i % 2 == 0 else r * rnd.uniform(0.5, 0.65)
        outer.append((x + rr * math.cos(a), y + dy + rr * math.sin(a)))
    s = [f'<polygon points="{pts(outer)}" fill="{base}" {ink_attr(ink) if ink else ""}/>']
    hi_pts = [(x, y + dy)] + [p for i, p in enumerate(outer) if 3 <= (i - int(n * 1.25)) % (2 * n) <= 6]
    if len(hi_pts) > 2:
        s.append(f'<polygon points="{pts(hi_pts)}" fill="{hi}"/>')
    return "".join(s)


def sky():
    """Opaque far layer: the canopy far below in the dark, with neon blooms."""
    rnd = random.Random(101)
    s = P["sky"]
    defs = (lin("bg", [(0, s[0], 1), (0.25, s[1], 1), (0.5, s[2], 1), (0.75, s[3], 1), (1, s[4], 1)])
            + lin("vig", [(0, P["vignette"], 0.75), (0.28, P["vignette"], 0), (0.72, P["vignette"], 0),
                          (1, P["vignette"], 0.75)], 0, 0, 1, 0)
            + blur("b40", 40) + grain_defs())
    body = [f'<rect width="{W}" height="{H}" fill="url(#bg)"/>']
    crowns = []
    for i in range(300):
        x, y = rnd.uniform(-10, W + 10), rnd.uniform(0, H)
        r = rnd.uniform(8, 16)
        base = rnd.choice(P["canopy"])
        seed = rnd.random()
        crowns.append((y, lambda dy, x=x, y=y, r=r, base=base, seed=seed:
                       crown(random.Random(seed), x, y, r, base, P["canopy_hi"], dy)))
    body.append(wrap_y(crowns, H))
    bloom = []
    for i in range(36):
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        c = rnd.choice(P["blossom"])
        bloom.append((y, lambda dy, x=x, y=y, c=c: f'<rect x="{x - 1.5:.0f}" y="{y + dy - 1.5:.0f}" width="3" height="3" '
                      f'fill="{c}" opacity="0.6"/>'))
    body.append(wrap_y(bloom, H))
    mist = []
    for i in range(8):
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        mist.append((y, lambda dy, x=x, y=y, rx=rnd.uniform(120, 220), ry=rnd.uniform(50, 110):
                     f'<ellipse cx="{x:.0f}" cy="{y + dy:.0f}" rx="{rx:.0f}" ry="{ry:.0f}" fill="{P["mist"]}" '
                     f'opacity="0.4" filter="url(#b40)"/>'))
    body.append(wrap_y(mist, H))
    body.append(f'<rect width="{W}" height="{H}" fill="url(#vig)"/>')
    body.append(grain(W, H))
    return doc(W, H, "".join(body), defs)


def far():
    rnd = random.Random(111)
    d, b = edge_range(rnd, W, H, P["far"], 7, 180, 280, 100, 170, 170, cap=0.18, ink=2.2,
                      foot=P["far"]["dark"])
    return doc(W, H, b, d)


def river_geom():
    rnd = random.Random(121)
    n1, n2 = PNoise(rnd), PNoise(rnd)
    return (lambda y: W / 2 + 14 * n1(y / H)), (lambda y: 62 + 10 * n2(y / H))


def mid():
    rnd = random.Random(131)
    cxf, hwf = river_geom()
    ys = [H * i / 48 for i in range(49)]
    body = []
    bl = [(cxf(y) - hwf(y) - 22, y) for y in ys]
    br = [(cxf(y) + hwf(y) + 22, y) for y in ys]
    body.append(f'<polygon points="{pts(bl + list(reversed(br)))}" fill="{P["bank"]}"/>')
    left = [(cxf(y) - hwf(y), y) for y in ys]
    right = [(cxf(y) + hwf(y), y) for y in ys]
    body.append(f'<polygon points="{pts(left + list(reversed(right)))}" fill="{P["water"]}"/>')
    sh = [(cxf(y) + hwf(y) * 0.4, y) for y in ys]
    body.append(f'<polygon points="{pts(sh + list(reversed(right)))}" fill="{P["water_shadow"]}"/>')
    pads = []
    for i in range(40):
        y = rnd.uniform(0, H)
        sd = rnd.choice((-1, 1))
        x = cxf(y) + sd * hwf(y) * rnd.uniform(0.6, 0.92)
        r = rnd.uniform(3, 6)
        c = rnd.choice([P["pad"], P["stone"]])
        pads.append((y, lambda dy, x=x, y=y, r=r, c=c:
                     f'<polygon points="{x - r:.1f},{y + dy:.1f} {x:.1f},{y + dy - r * 0.8:.1f} {x + r:.1f},{y + dy:.1f} '
                     f'{x:.1f},{y + dy + r * 0.8:.1f}" fill="{c}" {ink_attr(1.2)}/>'))
    body.append(wrap_y(pads, H))
    for side in (left, right, bl, br):
        body.append(f'<polyline points="{pts(side)}" fill="none" {ink_attr(2.5)}/>')
    trees = []
    for i in range(40):
        y = rnd.uniform(0, H)
        sd = rnd.choice((-1, 1))
        x = cxf(y) + sd * (hwf(y) + rnd.uniform(30, 110))
        r = rnd.uniform(14, 26)
        seed = rnd.random()
        trees.append((y, lambda dy, x=x, y=y, r=r, seed=seed:
                      crown(random.Random(seed), x + r * 0.25, y + r * 0.3, r, P["tree_dark"], P["tree_dark"], dy)
                      + crown(random.Random(seed), x, y, r, P["tree"], P["tree_hi"], dy, ink=2)))
    body.append(wrap_y(trees, H))
    d, b = edge_range(rnd, W, H, P["near"], 5, 140, 210, 110, 180, 118, cap=0.2, rim=P["rim"], ink=3,
                      foot=P["near"]["dark"])
    return doc(W, H, "".join(body) + b, d)


def flow():
    w = 128
    rnd = random.Random(141)
    items = []
    for i in range(30):
        x = rnd.uniform(40, 88)
        y = rnd.uniform(0, H)
        L = rnd.uniform(12, 36)
        items.append((y, lambda dy, x=x, y=y, L=L:
                      f'<rect x="{x - 1.5:.1f}" y="{y + dy - L / 2:.1f}" width="3" height="{L:.1f}" '
                      f'fill="{P["water_dash"]}" opacity="0.75"/>'))
    return doc(w, H, wrap_y(items, H), "")


def waterfall(phase):
    """Cliff ledge with a cascade: flat water sheet, highlight dashes that
    fall on a snappy cycle, and a splash crown at the foot."""
    w, h = 128, 256
    rnd = random.Random(7)
    top = 36
    ledge = [(0, 44), (16, 22), (40, 30), (62, 16), (90, 28), (112, 18), (128, 34), (128, 84), (0, 92)]
    body = [f'<polygon points="{pts(ledge)}" fill="{P["cliff"]}" {ink_attr(3)}/>',
            f'<polygon points="64,16 90,28 112,18 128,34 128,84 64,88" fill="{P["cliff_dark"]}"/>',
            f'<polygon points="{pts(ledge)}" fill="none" {ink_attr(3)}/>',
            f'<polygon points="44,{top} 84,{top} 88,{h - 40} 40,{h - 40}" fill="{P["fall"]}" {ink_attr(2.5)}/>',
            f'<polygon points="70,{top} 84,{top} 88,{h - 40} 72,{h - 40}" fill="{P["fall_shadow"]}"/>']
    for i in range(14):
        x = 46 + rnd.uniform(0, 34)
        L = rnd.uniform(16, 40)
        y = top + ((rnd.uniform(0, 1) + phase) % 1) * (h - 40 - top)
        body.append(f'<rect x="{x:.1f}" y="{y - L:.1f}" width="2.5" height="{L:.1f}" fill="{P["fall_hi"]}"/>')
    body.append(f'<rect x="40" y="{top}" width="48" height="{h - 40 - top}" fill="none"/>')
    # Splash: angular spray that pulses with the cycle.
    k = 0.5 + 0.5 * math.sin(phase * TAU * 2)
    spray = []
    for i in range(9):
        a = math.pi + i * math.pi / 8
        r = 26 + 8 * k + (6 if i % 2 else 0)
        spray.append((64 + r * math.cos(a), h - 36 + r * 0.55 * math.sin(a)))
    spray = [(36, h - 30)] + spray + [(92, h - 30)]
    body.append(f'<polygon points="{pts(spray)}" fill="{P["fall_hi"]}" opacity="0.85" {ink_attr(2)}/>')
    body.append(f'<polygon points="30,{h - 30} 98,{h - 30} 90,{h - 20} 38,{h - 20}" fill="{P["fall"]}" {ink_attr(2)}/>')
    return doc(w, h, "".join(body))


def ruin(phase):
    """Overgrown ruin tower: angular slabs, one shadow plane, ink outlines;
    neon glyphs blink in a 4-step sequence with hard bloom."""
    w, h = 200, 200
    k = int(phase * 4)
    lit, dark = P["stone_lit"], P["stone_dark"]
    body = []
    slabs = [[(30, 170), (170, 170), (160, 120), (40, 120)],
             [(52, 120), (148, 120), (140, 70), (60, 70)],
             [(72, 70), (128, 70), (122, 30), (78, 30)],
             [(88, 30), (112, 30), (100, 8)]]
    for s in slabs:
        body.append(f'<polygon points="{pts(s)}" fill="{lit}"/>')
        mx = sum(p[0] for p in s) / len(s)
        sh = [(max(x, mx), y) for x, y in s]
        body.append(f'<polygon points="{pts(sh)}" fill="{dark}"/>')
        body.append(f'<polygon points="{pts(s)}" fill="none" {ink_attr(3)}/>')
    body.append(f'<polygon points="{pts([(10, 184), (190, 184), (176, 170), (24, 170)])}" fill="{dark}" {ink_attr(3)}/>')
    rnd = random.Random(12)
    for i in range(5):
        seg = jitter_ridge(rnd, rnd.uniform(40, 160), rnd.uniform(40, 160), rnd.uniform(40, 160), rnd.uniform(60, 180), 4, 8)
        body.append(f'<polyline points="{pts(seg)}" stroke="#1e4a30" stroke-width="3" fill="none"/>')
    glyphs = [(64, 145), (100, 145), (136, 145), (80, 96), (120, 96), (100, 52)]
    for i, (x, y) in enumerate(glyphs):
        on = (i + k) % 3 == 0 or (i == 5 and k % 2 == 0)
        c = P["glyph_b"] if i % 2 else P["glyph_a"]
        if on:
            body.append(hard_glow(x, y, 13, c, 0.9, 3))
        body.append(f'<polygon points="{x},{y - 6} {x + 6},{y} {x},{y + 6} {x - 6},{y}" fill="{c if on else dark}" '
                    f'{ink_attr(1.5)}/>')
    return doc(w, h, "".join(body))


def obelisk(seed):
    """Angular signal pylon with a neon band (pulsed in code)."""
    rnd = random.Random(seed)
    w, h = 120, 200
    tall = rnd.uniform(0.75, 0.95)
    top = h - 16 - (h - 30) * tall
    body = [f'<polygon points="40,{h - 16} 80,{h - 16} 70,{top:.0f} 60,{top - 14:.0f} 50,{top:.0f}" fill="{P["obelisk"]}"/>',
            f'<polygon points="60,{h - 16} 80,{h - 16} 70,{top:.0f} 60,{top - 14:.0f}" fill="{P["obelisk_dark"]}"/>',
            f'<polygon points="40,{h - 16} 80,{h - 16} 70,{top:.0f} 60,{top - 14:.0f} 50,{top:.0f}" fill="none" {ink_attr(3)}/>']
    for j in range(3):
        y = top + 20 + j * 34
        body.append(f'<rect x="{56 + j}" y="{y:.0f}" width="{8 - 2 * j}" height="5" fill="{P["obelisk_light"]}"/>')
    body.append(hard_glow(60, top - 6, 16, P["obelisk_light"], 0.8, 3))
    body.append(f'<polygon points="28,{h - 4} 92,{h - 4} 84,{h - 16} 36,{h - 16}" fill="{P["obelisk_dark"]}" {ink_attr(2.5)}/>')
    return doc(w, h, "".join(body))


def build():
    w = World("Verdant")
    w.tile("sky", sky())
    w.tile("far", far())
    w.tile("mid", mid())
    w.tile("flow", flow())
    w.flipbook("anim", "waterfall", 8, waterfall)
    w.flipbook("anim", "ruin", 4, ruin)
    w.sprite("fx", "obelisk0", obelisk(1))
    w.sprite("fx", "obelisk1", obelisk(5))
    w.sprite("fx", "star", space.star_sprite())
    w.sprite("fx", "dot", space.soft_dot())
    w.sprite("fx", "streak", space.streak())
    w.pack()


def preview(t):
    v = 30 * (0.15 + 0.25)
    fr = lambda n, fps: int(t * fps) % n
    c = [("tile", "sky", t * v * 0.010, 1, (1, 1, 1, 1)),
         ("tile", "far", t * v * 0.04, 1, (1, 1, 1, 1)),
         ("tile", "mid", t * v * 0.10, 1, (1, 1, 1, 1)),
         ("strip", "flow", t * v * 0.10 + t * 1.4, 0.25, 1)]
    c.append(("sprite", "anim", f"waterfall_{fr(8, 12):02d}", -1.6, 7 - (t * v * 0.10 + 2) % 15, 1.2, 0, (0.8, 0.9, 0.9, 1), False))
    c.append(("sprite", "anim", f"ruin_{fr(4, 3):02d}", 1.6, 7 - (t * v * 0.16 + 6) % 16, 1.9, 0, (0.85, 0.9, 0.88, 1), False))
    beat = (t * 0.6) % 1
    pop = 1.08 if beat < 0.12 else 1.0
    c.append(("sprite", "fx", "obelisk0", -1.7, 7 - (t * v * 0.16 + 12) % 16, 0.9 * pop, 0, (0.7, 0.75, 0.8, 0.9), False))
    for i in range(16):
        x = ((i * 0.618 + 0.2 * math.sin(t * 0.7 + i)) % 1) * 5 - 2.5
        y = (((i * 0.377) % 1) * 13 - t * v * 0.35 + t * 0.25) % 13 - 6.5
        blink = math.sin(t * 2.4 + i * 2.1) > 0.55
        c.append(("sprite", "fx", "dot", x, y, 0.11, 0, (0.75, 1, 0.45, 0.8 if blink else 0.05), False))
    return c


if __name__ == "__main__":
    build()
