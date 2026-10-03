"""Verdant, seen from atmosphere level: the night canopy far below with
distant hazed hills, a narrow jungle river valley, small far-off landmarks
(plateau waterfalls, overgrown ruins with blinking glyphs, signal
obelisks), and cloud cels / haze bands / spores passing close by."""
import math
import random

from bgkit import (TAU, World, doc, blur, lin, wrap_y, PNoise, edge_range, pts, ink_attr, hard_glow,
                   jitter_ridge, grain_defs, grain)
from palette import VERDANT as P
from altitude import hazed, haze_doc, cloud_cel, haze_band, scatter_peaks
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
    """Distant green hills scattered far below, heavily hazed."""
    rnd = random.Random(111)
    d, b = scatter_peaks(rnd, W, H, P["far"], 30, 26, 52, cap=0.22, ink=1.2)
    hd, hb = hazed(b, P["air"], 0.45)
    return doc(W, H, hb, d + hd)


def river_geom():
    rnd = random.Random(121)
    n1, n2 = PNoise(rnd), PNoise(rnd)
    return (lambda y: W / 2 + 9 * n1(y / H)), (lambda y: 20 + 4 * n2(y / H))


def mid():
    """The jungle valley far below: a narrow river, banks, small crowns and
    low ridges along the edges. Moderate haze."""
    rnd = random.Random(131)
    cxf, hwf = river_geom()
    ys = [H * i / 48 for i in range(49)]
    body = []
    bl = [(cxf(y) - hwf(y) - 10, y) for y in ys]
    br = [(cxf(y) + hwf(y) + 10, y) for y in ys]
    body.append(f'<polygon points="{pts(bl + list(reversed(br)))}" fill="{P["bank"]}"/>')
    left = [(cxf(y) - hwf(y), y) for y in ys]
    right = [(cxf(y) + hwf(y), y) for y in ys]
    body.append(f'<polygon points="{pts(left + list(reversed(right)))}" fill="{P["water"]}"/>')
    sh = [(cxf(y) + hwf(y) * 0.4, y) for y in ys]
    body.append(f'<polygon points="{pts(sh + list(reversed(right)))}" fill="{P["water_shadow"]}"/>')
    for side in (left, right, bl, br):
        body.append(f'<polyline points="{pts(side)}" fill="none" {ink_attr(1.6)}/>')
    trees = []
    for i in range(110):
        y = rnd.uniform(0, H)
        sd = rnd.choice((-1, 1))
        x = cxf(y) + sd * (hwf(y) + rnd.uniform(14, 70))
        r = rnd.uniform(5, 10)
        seed = rnd.random()
        trees.append((y, lambda dy, x=x, y=y, r=r, seed=seed:
                      crown(random.Random(seed), x + r * 0.25, y + r * 0.3, r, P["tree_dark"], P["tree_dark"], dy)
                      + crown(random.Random(seed), x, y, r, P["tree"], P["tree_hi"], dy, ink=1.2)))
    body.append(wrap_y(trees, H))
    d, b = edge_range(rnd, W, H, P["near"], 12, 50, 84, 34, 60, 100, cap=0.2, rim=P["rim"], ink=1.4,
                      foot=P["near"]["dark"])
    hd, hb = hazed("".join(body) + b, P["air"], 0.25)
    return doc(W, H, hb, d + hd)


def flow():
    w = 128
    rnd = random.Random(141)
    items = []
    for i in range(34):
        x = rnd.uniform(57, 71)
        y = rnd.uniform(0, H)
        L = rnd.uniform(6, 14)
        items.append((y, lambda dy, x=x, y=y, L=L:
                      f'<rect x="{x - 1:.1f}" y="{y + dy - L / 2:.1f}" width="2" height="{L:.1f}" '
                      f'fill="{P["water_dash"]}" opacity="0.7"/>'))
    return doc(w, H, wrap_y(items, H), "")


def waterfall(phase):
    """A mesa-edge waterfall seen from altitude: an angular plateau island
    with a few crowns and a river running to its lip, an inked cliff band
    (one shadow plane, strata lines), the falling sheet with highlight
    dashes that drop each frame, angular mist cels at the plunge pool and a
    short river tail. 8 frames; mist steps between two sizes (snappy)."""
    w, h = 256, 256
    rnd = random.Random(7)
    top_edge = [(24, 40), (70, 12), (128, 4), (190, 10), (236, 40)]
    lip = [(240, 92), (198, 104), (150, 100), (128, 108), (106, 100), (54, 106), (16, 92)]
    plateau = top_edge + lip
    drop = 58
    cliff = list(reversed(lip)) + [(x, y + drop + (6 if i % 2 else 0)) for i, (x, y) in enumerate(lip)]
    cliff_shadow = [(150, 100), (198, 104), (240, 92), (240, 150), (198, 166), (150, 158)]
    river_top = [(120, 6), (136, 6), (142, 46), (136, 76), (140, 104), (116, 104), (120, 76), (114, 46)]
    fall = [(116, 104), (140, 104), (144, 166), (112, 166)]
    pool = [(92, 164), (164, 162), (178, 182), (150, 196), (104, 196), (82, 182)]
    tail = [(114, 194), (142, 194), (146, 222), (130, 250), (112, 222)]
    g = [f'<polygon points="{pts(plateau)}" fill="{P["plateau"]}"/>',
         f'<polygon points="{pts([(150, 6), (190, 10), (236, 40), (240, 92), (198, 104), (150, 100)])}" fill="{P["plateau_dark"]}"/>']
    for i in range(10):
        x, y = rnd.uniform(40, 220), rnd.uniform(26, 86)
        if 100 < x < 156:
            continue
        g.append(crown(random.Random(rnd.random()), x, y, rnd.uniform(6, 10), P["tree"], P["tree_hi"], 0, ink=1.4))
    g.append(f'<polygon points="{pts(river_top)}" fill="{P["water"]}"/>')
    g.append(f'<polygon points="{pts(cliff)}" fill="{P["rock"]}"/>')
    g.append(f'<polygon points="{pts(cliff_shadow)}" fill="{P["rock_dark"]}"/>')
    g.append(f'<polyline points="16,95 54,109 106,103" stroke="{P["rock_hi"]}" stroke-width="3" fill="none"/>')
    for x in (40, 80, 176, 216):
        g.append(f'<polyline points="{x},{110} {x + 4},{130} {x - 2},{150}" fill="none" {ink_attr(1.4)}/>')
    g.append(f'<polygon points="{pts(pool)}" fill="{P["water"]}"/>')
    g.append(f'<polygon points="{pts(tail)}" fill="{P["water"]}"/>')
    ink = (f'<polygon points="{pts(plateau)}" fill="none" {ink_attr(3)}/>'
           f'<polygon points="{pts(cliff)}" fill="none" {ink_attr(3)}/>'
           f'<polygon points="{pts(river_top)}" fill="none" {ink_attr(2)}/>'
           f'<polygon points="{pts(pool)}" fill="none" {ink_attr(2.5)}/>'
           f'<polygon points="{pts(tail)}" fill="none" {ink_attr(2)}/>')
    d, body = hazed("".join(g) + ink, P["air"], 0.3)
    out = [f'<polygon points="{pts(fall)}" fill="{P["fall"]}" {ink_attr(2.5)}/>',
           f'<polygon points="{pts([(130, 104), (140, 104), (144, 166), (132, 166)])}" fill="{P["fall_shadow"]}"/>']
    for i in range(6):
        x = 118 + i * 4
        L = 10 + (i * 7) % 9
        y = 104 + ((i * 0.37 + phase) % 1) * 62
        out.append(f'<rect x="{x}" y="{y - L / 2:.1f}" width="2" height="{L}" fill="{P["fall_hi"]}" opacity="0.85"/>')
    big = int(phase * 8) % 2 == 0
    for j, (mx, my) in enumerate([(106, 170), (128, 166), (150, 170)]):
        r = (11 if big else 8) + (j % 2) * 2
        p = [(mx + r * math.cos(a), my + r * 0.7 * math.sin(a)) for a in [k * TAU / 6 + j for k in range(6)]]
        out.append(f'<polygon points="{pts(p)}" fill="{P["fall_hi"]}" opacity="0.45" {ink_attr(1.2)}/>')
    for i in range(3):
        y = 198 + ((i / 3 + phase) % 1) * 40
        out.append(f'<rect x="{125 + (i % 2) * 5}" y="{y:.1f}" width="2" height="8" fill="{P["water_dash"]}" opacity="0.7"/>')
    return doc(w, h, body + "".join(out), d)
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
    w.flipbook("anim", "ruin", 4, lambda p: haze_doc(ruin(p), P["air"], 0.3))
    w.sprite("fx", "obelisk0", haze_doc(obelisk(1), P["air"], 0.3))
    w.sprite("fx", "obelisk1", haze_doc(obelisk(5), P["air"], 0.3))
    w.sprite("fx", "cloud0", cloud_cel(4, P["cloud"], P["cloud_shadow"]))
    w.sprite("fx", "cloud1", cloud_cel(9, P["cloud"], P["cloud_shadow"]))
    w.sprite("fx", "haze", haze_band(P["band"]), size=(256, 48))
    w.sprite("fx", "star", space.star_sprite())
    w.sprite("fx", "dot", space.soft_dot())
    w.sprite("fx", "streak", space.streak())
    w.pack()


def preview(t):
    v = 30 * (0.2 + 0.25)
    fr = lambda n, fps: int(t * fps) % n
    c = [("tile", "sky", t * v * 0.006, 1, (1, 1, 1, 1)),
         ("tile", "far", t * v * 0.014, 1, (1, 1, 1, 1)),
         ("tile", "mid", t * v * 0.024, 1, (1, 1, 1, 1)),
         ("strip", "flow", t * v * 0.024 + t * 0.3, 0.25, 1)]
    c.append(("sprite", "anim", f"waterfall_{fr(8, 12):02d}", -1.3, 2 - (t * v * 0.03) % 12, 1.5, 0, (1, 1, 1, 1), False))
    c.append(("sprite", "anim", f"ruin_{fr(4, 3):02d}", 1.4, -2 - (t * v * 0.036) % 12 + 6, 0.9, 0, (1, 1, 1, 1), False))
    c.append(("sprite", "fx", "haze", 0.0, 4 - (t * v * 0.12) % 14, 7.0, 0, (1, 1, 1, 0.35), False))
    c.append(("sprite", "fx", "cloud1", -0.6, 7 - (t * v * 0.3 + 9) % 20, 2.4, 0, (1, 1, 1, 0.35), False))
    for i in range(16):
        x = ((i * 0.618 + 0.2 * math.sin(t * 0.7 + i)) % 1) * 5 - 2.5
        y = (((i * 0.377) % 1) * 13 - t * v * 0.4 + t * 0.25) % 13 - 6.5
        blink = math.sin(t * 2.4 + i * 2.1) > 0.55
        c.append(("sprite", "fx", "dot", x, y, 0.08, 0, (0.75, 1, 0.45, 0.7 if blink else 0.05), False))
    return c
if __name__ == "__main__":
    build()
