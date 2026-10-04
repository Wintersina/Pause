"""Verdant, seen from atmosphere level, as an 80s anime night-forest cel.

Depth is told with flat value steps rather than a haze wash:
  sky   blue-black / indigo night with the canopy far below as dark crowns,
        a scatter of sodium shrine lanterns and teal blooms
  far   rows of small angular pines (teal-green on an indigo shadow plane)
  mid   the river valley: a teal river with an inked bank, bigger pines one
        value step brighter, sodium lanterns along the banks
  flow  teal / cyan highlight dashes running down the river
Landmarks (small, far below): a plateau waterfall with a bright falling
sheet, an overgrown ruin tower and obelisks with blinking neon glyphs.
Close to the ship only air passes: indigo cloud cels, a thin haze band,
firefly and spore flipbooks.

Every form: flat base, one hard shadow plane (light from the upper left),
one highlight kick, background ink. Greens stay cool (blue-green) and dark
so the olive/lime Verdant enemies read on top.
"""
import math
import random

from bgkit import (TAU, World, doc, lin, wrap_y, PNoise, pts, ink_attr, hard_glow, uid,
                   grain_defs, grain)
from palette import VERDANT as P, BONE, AMBER, SODIUM, CYAN, TEAL, TEAL_SH
from altitude import hazed, haze_doc, cloud_cel, haze_band
import space

W, H = 512, 1024


# ------------------------------------------------------------------ shapes --

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
    # highlight wedge on the upper-left (light direction)
    hi_pts = [(x, y + dy)] + [p for p in outer if p[0] < x - r * 0.15 and p[1] < y + dy - r * 0.1]
    if len(hi_pts) > 2:
        hi_pts = [hi_pts[0]] + sorted(hi_pts[1:], key=lambda p: math.atan2(p[1] - y - dy, p[0] - x))
        s.append(f'<polygon points="{pts(hi_pts)}" fill="{hi}"/>')
    return "".join(s)


def pine(rnd, x, base, h, w, pal, ink, dy=0, kick=True):
    """An angular pine silhouette pointing up the screen: three chevron
    tiers, the right half one hard shadow plane, a kick sliver on the lit
    left edges, background ink around the whole tree."""
    y0 = base + dy
    tiers = 3
    left = []
    for k in range(tiers):
        yk = y0 - h * 0.30 * k
        hwk = w / 2 * (1 - 0.27 * k) * rnd.uniform(0.92, 1.05)
        left.append((x - hwk, yk))
        left.append((x - hwk * 0.42, yk - h * 0.16))
    apex = (x + rnd.uniform(-0.04, 0.04) * w, y0 - h)
    right = [(2 * x - px + rnd.uniform(-1, 1), py) for px, py in reversed(left)]
    stem = [(x + w * 0.07, y0 + h * 0.06), (x - w * 0.07, y0 + h * 0.06)]
    outline = left + [apex] + right + stem
    clip = uid("pc")
    shade = [apex, (x + w * 0.04, y0 + h * 0.1), (x + w, y0 + h * 0.1), (x + w, y0 - h * 1.1)]
    defs = f'<clipPath id="{clip}"><polygon points="{pts(outline)}"/></clipPath>'
    body = [poly(outline, pal["lit"]), f'<g clip-path="url(#{clip})">{poly(shade, pal["dark"])}</g>']
    if kick:
        ks = []
        for k in range(tiers):
            a, b = left[2 * k], left[2 * k + 1]
            ks.append(poly([b, a, (a[0] + w * 0.12, a[1] - 1.5), (b[0] + w * 0.05, b[1] + h * 0.03)], pal["kick"]))
        body.append(f'<g clip-path="url(#{clip})">{"".join(ks)}</g>')
    body.append(f'<polygon points="{pts(outline)}" fill="none" {ink_attr(ink)}/>')
    return defs, "".join(body)


def poly(points, fill, extra=""):
    return f'<polygon points="{pts(points)}" fill="{fill}" {extra}/>'


def pine_rows(rnd, pal, band, hmin, hmax, step, ink, kick=True, inner_scatter=0, avoid=None):
    """Pines stacked along both edges (and a few clumps inside), sorted by y
    so lower trees overlap the ones above, wrapped for a seamless tile."""
    items = []
    for side in (0, 1):
        y = 0.0
        while y < H:
            for _ in range(2):
                d = rnd.uniform(0, band)
                h = rnd.uniform(hmin, hmax) * (1.0 - 0.25 * d / band)
                x = d if side == 0 else W - d
                items.append((y + rnd.uniform(-step * 0.4, step * 0.4), x, h, h * rnd.uniform(0.58, 0.7), rnd.random()))
            y += step
    for _ in range(inner_scatter):
        cx, cy = rnd.uniform(band, W - band), rnd.uniform(0, H)
        if avoid and avoid(cx, cy):
            continue
        for j in range(rnd.randint(2, 4)):
            h = rnd.uniform(hmin, hmax) * 0.7
            items.append((cy + rnd.uniform(-14, 14), cx + rnd.uniform(-18, 18), h, h * 0.62, rnd.random()))
    out = []
    for y, x, h, w, seed in items:
        for dy in (-H, 0, H):
            out.append((y + dy, x, h, w, seed))
    out.sort(key=lambda t: t[0])
    defs, body = [], []
    for y, x, h, w, seed in out:
        if y - h > H + 10 or y + h * 0.2 < -10:
            continue
        d, b = pine(random.Random(seed), x, y, h, w, pal, ink, kick=kick)
        defs.append(d)
        body.append(b)
    return "".join(defs), "".join(body)


def edge_foot(rnd, color, reach, ink):
    """A continuous flat ground band behind each pine row (no gaps)."""
    out = []
    for side in (0, 1):
        n = PNoise(rnd)
        ys = [H * k / 32 for k in range(33)]
        fx = [reach * (0.8 + 0.2 * n(y / H)) for y in ys]
        if side == 0:
            shape = [(-10, -10)] + list(zip(fx, ys)) + [(-10, H + 10)]
        else:
            shape = [(W + 10, -10)] + [(W - x, y) for x, y in zip(fx, ys)] + [(W + 10, H + 10)]
        out.append(f'<polygon points="{pts(shape)}" fill="{color}" {ink_attr(ink)}/>')
    return "".join(out)


def lantern(x, y, color, r=2.0):
    """A shrine lantern: a tiny flat square with a stepped hard glow."""
    return (hard_glow(x, y, r * 3.2, color, 0.7, 2)
            + f'<rect x="{x - r:.1f}" y="{y - r:.1f}" width="{2 * r:.1f}" height="{2 * r:.1f}" fill="{color}"/>')


# ------------------------------------------------------------------ tiles ---

def sky():
    """Opaque far layer: indigo night, the canopy far below as dark crowns
    with sky gaps between, tiny shrine lanterns and teal blooms."""
    rnd = random.Random(101)
    s = P["sky"]
    defs = (lin("bg", [(0, s[0], 1), (0.25, s[1], 1), (0.5, s[2], 1), (0.75, s[3], 1), (1, s[4], 1)])
            + lin("vig", [(0, P["vignette"], 0.7), (0.25, P["vignette"], 0), (0.75, P["vignette"], 0),
                          (1, P["vignette"], 0.7)], 0, 0, 1, 0)
            + grain_defs())
    body = [f'<rect width="{W}" height="{H}" fill="url(#bg)"/>']
    crowns = []
    for i in range(240):
        x, y = rnd.uniform(-10, W + 10), rnd.uniform(0, H)
        r = rnd.uniform(9, 18)
        base = rnd.choice(P["canopy"])
        seed = rnd.random()
        crowns.append((y, lambda dy, x=x, y=y, r=r, base=base, seed=seed:
                       crown(random.Random(seed), x + r * 0.3, y + r * 0.3, r, P["canopy_gap"], P["canopy_gap"], dy)
                       + crown(random.Random(seed), x, y, r, base, P["canopy_hi"], dy, ink=1.0)))
    body.append(wrap_y(crowns, H))
    lights = []
    for i in range(46):
        x, y = rnd.uniform(8, W - 8), rnd.uniform(0, H)
        c = rnd.choice(P["blossom"])
        r = rnd.choice((1.0, 1.5, 1.5))
        lights.append((y, lambda dy, x=x, y=y, c=c, r=r: lantern(x, y + dy, c, r)))
    for i in range(5):
        x, y = rnd.uniform(30, W - 30), rnd.uniform(0, H)
        lights.append((y, lambda dy, x=x, y=y: f'<rect x="{x:.0f}" y="{y + dy:.0f}" width="2" height="2" '
                                                f'fill="{P["lantern_red"]}"/>'))
    body.append(wrap_y(lights, H))
    body.append(f'<rect width="{W}" height="{H}" fill="url(#vig)"/>')
    body.append(grain(W, H))
    return doc(W, H, "".join(body), defs)


def river_geom():
    rnd = random.Random(121)
    n1, n2 = PNoise(rnd), PNoise(rnd)
    return (lambda y: W / 2 + 10 * n1(y / H)), (lambda y: 24 + 4 * n2(y / H))


def far():
    """Far pine rows and tiny overgrown relay pylons: the industrial spine
    of an old forest world, kept below the canopy value step so gameplay
    sprites remain dominant."""
    rnd = random.Random(111)
    cxf, hwf = river_geom()
    foot = edge_foot(rnd, P["far"]["foot"], 150, 1.2)
    d, b = pine_rows(rnd, P["far"], 150, 34, 54, 20, 1.2, kick=True, inner_scatter=26,
                     avoid=lambda x, y: abs(x - cxf(y)) < hwf(y) + 60)
    relays = []
    for i in range(9):
        y = rnd.uniform(0, H)
        sd = -1 if i % 2 else 1
        x = cxf(y) + sd * (hwf(y) + rnd.uniform(72, 108))
        h = rnd.uniform(18, 30)
        relays.append((y, lambda dy, x=x, y=y, h=h:
                      f'<g transform="translate(0 {dy:.1f})">'
                      f'<polygon points="{x-8:.1f},{y:.1f} {x-5:.1f},{y-h:.1f} {x+5:.1f},{y-h:.1f} '
                      f'{x+8:.1f},{y:.1f}" fill="{P["stone_lit"]}" {ink_attr(1)}/>'
                      f'<polygon points="{x:.1f},{y-h-7:.1f} {x+5:.1f},{y-h:.1f} {x-5:.1f},{y-h:.1f}" fill="{P["stone_hi"]}"/>'
                      f'<rect x="{x-1:.1f}" y="{y-h-4:.1f}" width="2" height="3" fill="{P["glyph_a"]}"/>'
                      f'</g>'))
    hd, hb = hazed(foot + b + wrap_y(relays, H), P["air"], 0.12)
    return doc(W, H, hb, d + hd)


def mid():
    """The river valley: banks, a teal river with one shadow plane and an
    inked edge, crowns and bigger pines one value step up, sodium lanterns."""
    rnd = random.Random(131)
    cxf, hwf = river_geom()
    ys = [H * i / 64 for i in range(65)]
    body = []
    bl = [(cxf(y) - hwf(y) - 14, y) for y in ys]
    br = [(cxf(y) + hwf(y) + 14, y) for y in ys]
    body.append(f'<polygon points="{pts(bl + list(reversed(br)))}" fill="{P["bank"]}"/>')
    left = [(cxf(y) - hwf(y), y) for y in ys]
    right = [(cxf(y) + hwf(y), y) for y in ys]
    body.append(f'<polygon points="{pts(left + list(reversed(right)))}" fill="{P["water"]}"/>')
    sh = [(cxf(y) + hwf(y) * 0.35, y) for y in ys]
    body.append(f'<polygon points="{pts(sh + list(reversed(right)))}" fill="{P["water_shadow"]}"/>')
    # lit left water edge: one highlight tone
    hl = [(cxf(y) - hwf(y) + 3, y) for y in ys]
    body.append(f'<polyline points="{pts(hl)}" fill="none" stroke="{P["water_edge"]}" stroke-width="3"/>')
    for side in (left, right):
        body.append(f'<polyline points="{pts(side)}" fill="none" {ink_attr(2.2)}/>')
    for side in (bl, br):
        body.append(f'<polyline points="{pts(side)}" fill="none" {ink_attr(1.6)}/>')
    # crowns between the river and the pine rows
    trees = []
    for i in range(70):
        y = rnd.uniform(0, H)
        sd = rnd.choice((-1, 1))
        x = cxf(y) + sd * (hwf(y) + rnd.uniform(22, 80))
        r = rnd.uniform(7, 12)
        seed = rnd.random()
        trees.append((y, lambda dy, x=x, y=y, r=r, seed=seed:
                      crown(random.Random(seed), x + r * 0.35, y + r * 0.35, r, P["tree_dark"], P["tree_dark"], dy)
                      + crown(random.Random(seed), x, y, r, P["tree"], P["tree_hi"], dy, ink=1.4)))
    body.append(wrap_y(trees, H))
    # shrine lanterns along the banks
    lamps = []
    for i in range(14):
        y = rnd.uniform(0, H)
        sd = rnd.choice((-1, 1))
        x = cxf(y) + sd * (hwf(y) + rnd.uniform(16, 26))
        c = SODIUM if i % 3 else AMBER
        lamps.append((y, lambda dy, x=x, y=y, c=c: lantern(x, y + dy, c, 2.0)))
    body.append(wrap_y(lamps, H))
    foot = edge_foot(rnd, P["near"]["foot"], 92, 1.6)
    d, b = pine_rows(rnd, P["near"], 96, 60, 92, 30, 2.0, kick=True)
    return doc(W, H, "".join(body) + foot + b, d)


def flow():
    """Teal and cyan highlight dashes on the river (scrolls faster than the
    banks, so the water reads as running)."""
    w = 128
    rnd = random.Random(141)
    items = []
    for i in range(44):
        x = rnd.uniform(46, 80)
        y = rnd.uniform(0, H)
        L = rnd.uniform(8, 22)
        c = rnd.choice((TEAL, TEAL, CYAN))
        wd = 2 if c == TEAL else 2.5
        items.append((y, lambda dy, x=x, y=y, L=L, c=c, wd=wd:
                      f'<rect x="{x - wd / 2:.1f}" y="{y + dy - L / 2:.1f}" width="{wd}" height="{L:.1f}" fill="{c}"/>'))
    for i in range(6):
        x = rnd.uniform(48, 78)
        y = rnd.uniform(0, H)
        items.append((y, lambda dy, x=x, y=y:
                      f'<rect x="{x - 1:.1f}" y="{y + dy - 3:.1f}" width="2" height="6" fill="{BONE}"/>'))
    return doc(w, H, wrap_y(items, H), "")


# -------------------------------------------------------------- landmarks ---

def waterfall(phase):
    """A mesa-edge waterfall seen from altitude: a flat green plateau with
    crowns and a river to its lip, an inked indigo cliff band (one shadow
    plane, one lit ledge), a bright teal falling sheet whose cyan/bone
    dashes drop each frame, angular mist cels at the plunge pool."""
    w, h = 256, 256
    rnd = random.Random(7)
    top_edge = [(24, 40), (70, 12), (128, 4), (190, 10), (236, 40)]
    lip = [(240, 92), (198, 104), (150, 100), (128, 108), (106, 100), (54, 106), (16, 92)]
    plateau = top_edge + lip
    drop = 60
    cliff = list(reversed(lip)) + [(x, y + drop + (6 if i % 2 else 0)) for i, (x, y) in enumerate(lip)]
    cliff_shadow = [(150, 100), (198, 104), (240, 92), (240, 150), (198, 168), (150, 160)]
    river_top = [(118, 6), (138, 6), (144, 46), (138, 76), (142, 106), (114, 106), (118, 76), (112, 46)]
    fall = [(112, 104), (144, 104), (150, 168), (106, 168)]
    pool = [(84, 164), (172, 162), (186, 182), (156, 198), (100, 198), (74, 182)]
    tail = [(112, 196), (144, 196), (148, 224), (132, 252), (110, 224)]
    g = [poly(plateau, P["plateau"]),
         poly([(150, 6), (190, 10), (236, 40), (240, 92), (198, 104), (150, 100)], P["plateau_dark"]),
         poly([(24, 40), (70, 12), (100, 7), (60, 30), (34, 52)], P["plateau_hi"])]
    for i in range(12):
        x, y = rnd.uniform(36, 224), rnd.uniform(24, 88)
        if 98 < x < 158:
            continue
        r = rnd.uniform(7, 11)
        sd = random.Random(rnd.random())
        st = sd.random()
        g.append(crown(random.Random(st), x + r * 0.3, y + r * 0.3, r, P["tree_dark"], P["tree_dark"])
                 + crown(random.Random(st), x, y, r, P["tree"], P["tree_hi"], 0, ink=1.4))
    g.append(poly(river_top, P["water"]))
    g.append(poly([(128, 6), (138, 6), (144, 46), (138, 76), (142, 106), (128, 106)], P["water_shadow"]))
    g.append(poly(cliff, P["cliff"]))
    g.append(poly(cliff_shadow, P["cliff_dark"]))
    g.append(f'<polyline points="16,95 54,109 106,103" stroke="{P["cliff_hi"]}" stroke-width="4" fill="none"/>')
    for x in (36, 76, 180, 220):
        g.append(f'<polyline points="{x},112 {x + 5},134 {x - 2},156" fill="none" {ink_attr(1.6)}/>')
    g.append(poly(pool, P["water"]))
    g.append(poly(tail, P["water"]))
    g.append(f'<polygon points="{pts(plateau)}" fill="none" {ink_attr(3)}/>'
             f'<polygon points="{pts(cliff)}" fill="none" {ink_attr(3)}/>'
             f'<polygon points="{pts(river_top)}" fill="none" {ink_attr(2)}/>'
             f'<polygon points="{pts(pool)}" fill="none" {ink_attr(2.5)}/>'
             f'<polygon points="{pts(tail)}" fill="none" {ink_attr(2)}/>')
    # bright falling sheet: teal base, one shadow plane, falling dashes
    out = [poly(fall, P["fall"]),
           poly([(132, 104), (144, 104), (150, 168), (134, 168)], P["fall_shadow"])]
    for i in range(8):
        x = 112 + i * 4.2
        L = 12 + (i * 7) % 11
        y = 104 + ((i * 0.37 + phase) % 1) * 64
        c = P["fall_kick"] if i % 3 == 0 else P["fall_hi"]
        out.append(f'<rect x="{x:.1f}" y="{max(104, y - L / 2):.1f}" width="2.4" '
                   f'height="{min(L, 168 - max(104, y - L / 2)):.1f}" fill="{c}"/>')
    out.append(f'<polygon points="{pts(fall)}" fill="none" {ink_attr(2.5)}/>')
    big = int(phase * 8) % 2 == 0
    for j, (mx, my) in enumerate([(104, 172), (128, 168), (152, 172)]):
        r = (13 if big else 9) + (j % 2) * 2
        p = [(mx + r * math.cos(a), my + r * 0.7 * math.sin(a)) for a in [k * TAU / 6 + j for k in range(6)]]
        out.append(poly(p, P["fall_hi"], ink_attr(1.4)))
        out.append(poly([(mx - r * 0.5, my - r * 0.35), (mx, my - r * 0.6), (mx + r * 0.1, my)], BONE))
    for i in range(3):
        y = 200 + ((i / 3 + phase) % 1) * 40
        out.append(f'<rect x="{125 + (i % 2) * 5}" y="{y:.1f}" width="2" height="8" fill="{P["water_dash"]}"/>')
    return doc(w, h, "".join(g) + "".join(out))


GLYPHS = [(64, 145), (100, 145), (136, 145), (80, 96), (120, 96), (100, 52)]


def ruin(phase):
    """Overgrown ruin tower: indigo stone slabs, one shadow plane, a lit
    edge kick, green vines with a highlight, neon glyphs (cyan / sodium)
    blinking in a 4-step sequence with hard bloom, a red tip light."""
    w, h = 200, 200
    k = int(phase * 4)
    lit, dark = P["stone_lit"], P["stone_dark"]
    body = []
    slabs = [[(30, 170), (170, 170), (160, 120), (40, 120)],
             [(52, 120), (148, 120), (140, 70), (60, 70)],
             [(72, 70), (128, 70), (122, 30), (78, 30)],
             [(88, 30), (112, 30), (100, 8)]]
    body.append(poly([(10, 184), (190, 184), (176, 170), (24, 170)], dark, ink_attr(3)))
    for s in slabs:
        body.append(poly(s, lit))
        mx = sum(p[0] for p in s) / len(s)
        sh = [(max(x, mx), y) for x, y in s]
        body.append(poly(sh, dark))
        # lit top-left edge kick
        a, b = s[-1], s[0]
        body.append(f'<polyline points="{pts([s[0], s[-1]])}" stroke="{P["stone_hi"]}" stroke-width="3" '
                    f'fill="none" transform="translate(2 0)"/>')
        body.append(f'<polygon points="{pts(s)}" fill="none" {ink_attr(3)}/>')
    rnd = random.Random(12)
    for i in range(6):
        x0 = rnd.uniform(40, 160)
        seg = [(x0 + rnd.uniform(-10, 10), y) for y in range(int(rnd.uniform(40, 90)), int(rnd.uniform(130, 180)), 12)]
        body.append(f'<polyline points="{pts(seg)}" stroke="{P["vine"]}" stroke-width="4" fill="none" '
                    f'stroke-linejoin="miter"/>')
        body.append(f'<polyline points="{pts(seg)}" stroke="{P["vine_hi"]}" stroke-width="1.4" fill="none" '
                    f'transform="translate(-1 0)"/>')
    for i, (x, y) in enumerate(GLYPHS):
        on = (i + k) % 3 == 0 or (i == 5 and k % 2 == 0)
        c = P["glyph_b"] if i % 2 else P["glyph_a"]
        if on:
            body.append(hard_glow(x, y, 14, c, 0.9, 3))
        body.append(f'<polygon points="{x},{y - 7} {x + 7},{y} {x},{y + 7} {x - 7},{y}" fill="{c if on else P["glyph_off"]}" '
                    f'{ink_attr(1.5)}/>')
        if on:
            body.append(f'<rect x="{x - 1.5}" y="{y - 1.5}" width="3" height="3" fill="{BONE}"/>')
    if k % 2 == 0:
        body.append(hard_glow(100, 6, 6, P["lantern_red"], 0.8, 2))
    body.append(f'<rect x="98.5" y="4.5" width="3" height="3" fill="{P["lantern_red"] if k % 2 == 0 else dark}"/>')
    return doc(w, h, "".join(body))


def obelisk(seed):
    """Angular signal pylon: indigo stone, one shadow plane, teal glyph
    bands and a sodium tip (pulsed in code)."""
    rnd = random.Random(seed)
    w, h = 120, 200
    tall = rnd.uniform(0.75, 0.95)
    top = h - 16 - (h - 30) * tall
    shape = f"40,{h - 16} 80,{h - 16} 70,{top:.0f} 60,{top - 14:.0f} 50,{top:.0f}"
    body = [f'<polygon points="28,{h - 4} 92,{h - 4} 84,{h - 16} 36,{h - 16}" fill="{P["obelisk_dark"]}" {ink_attr(2.5)}/>',
            f'<polygon points="{shape}" fill="{P["obelisk"]}"/>',
            f'<polygon points="60,{h - 16} 80,{h - 16} 70,{top:.0f} 60,{top - 14:.0f}" fill="{P["obelisk_dark"]}"/>',
            f'<polyline points="42,{h - 18} 51,{top + 2:.0f}" stroke="{P["stone_hi"]}" stroke-width="2.5" fill="none"/>',
            f'<polygon points="{shape}" fill="none" {ink_attr(3)}/>']
    for j in range(3):
        y = top + 22 + j * 34
        body.append(hard_glow(60, y + 2.5, 9, P["obelisk_light"], 0.6, 2))
        body.append(f'<rect x="{54 + j}" y="{y:.0f}" width="{12 - 2 * j}" height="5" fill="{P["obelisk_light"]}"/>')
    body.append(hard_glow(60, top - 6, 16, P["obelisk_tip"], 0.8, 3))
    body.append(f'<rect x="57" y="{top - 9:.0f}" width="6" height="6" fill="{P["obelisk_tip"]}"/>')
    return doc(w, h, "".join(body))


# -------------------------------------------------------------- particles ---

# Firefly blink, 8 drawings at 8 fps: held dark rest, anticipation, a
# 4-point flash, settle (docs/art-style.md 3: holds, snappy, no tweening).
FIREFLY = ["rest", "rest", "rest", "dot", "diamond", "flash", "diamond", "dot"]


def firefly(phase):
    s = 32
    kind = FIREFLY[int(phase * len(FIREFLY)) % len(FIREFLY)]
    c, core = P["firefly"], P["firefly_core"]
    if kind == "rest":
        body = f'<rect x="14.5" y="14.5" width="3" height="3" fill="{SODIUM}" opacity="0.55"/>'
    elif kind == "dot":
        body = hard_glow(16, 16, 6, c, 0.5, 2) + f'<rect x="14" y="14" width="4" height="4" fill="{c}"/>'
    elif kind == "diamond":
        body = (hard_glow(16, 16, 10, c, 0.55, 2)
                + f'<polygon points="16,9 23,16 16,23 9,16" fill="{c}"/>'
                + f'<rect x="15" y="15" width="2" height="2" fill="{core}"/>')
    else:
        body = (hard_glow(16, 16, 14, c, 0.6, 3)
                + f'<polygon points="16,1 18.5,13.5 31,16 18.5,18.5 16,31 13.5,18.5 1,16 13.5,13.5" fill="{c}"/>'
                + f'<polygon points="16,9 19,16 16,23 13,16" fill="{core}"/>')
    return doc(s, s, body)


def spore(phase):
    """A drifting spore: a small angular hex that turns in 4 snappy steps,
    one shadow half, ink edge."""
    s = 24
    a0 = int(phase * 4) * (math.pi / 12)
    p = [(12 + 7 * math.cos(a0 + i * TAU / 6), 12 + 7 * math.sin(a0 + i * TAU / 6)) for i in range(6)]
    body = (poly(p, P["spore"], ink_attr(1.6))
            + poly([p[0], p[1], p[2], (12, 12)], TEAL_SH)
            + f'<rect x="10.5" y="10.5" width="3" height="3" fill="{BONE}"/>')
    return doc(s, s, body)


# ------------------------------------------------------------------ build ---

LANDMARK_HAZE = 0.08


def build():
    w = World("Verdant")
    w.tile("sky", sky())
    w.tile("far", far())
    w.tile("mid", mid())
    w.tile("flow", flow())
    w.flipbook("anim", "waterfall", 8, lambda p: haze_doc(waterfall(p), P["air"], LANDMARK_HAZE))
    w.flipbook("anim", "ruin", 4, lambda p: haze_doc(ruin(p), P["air"], LANDMARK_HAZE))
    w.sprite("fx", "obelisk0", haze_doc(obelisk(1), P["air"], LANDMARK_HAZE))
    w.sprite("fx", "obelisk1", haze_doc(obelisk(5), P["air"], LANDMARK_HAZE))
    w.sprite("fx", "cloud0", cloud_cel(4, P["cloud"], P["cloud_shadow"]))
    w.sprite("fx", "cloud1", cloud_cel(9, P["cloud"], P["cloud_shadow"]))
    w.sprite("fx", "haze", haze_band(P["band"]), size=(256, 48))
    w.flipbook("fx", "firefly", len(FIREFLY), firefly)
    w.flipbook("fx", "spore", 4, spore)
    w.sprite("fx", "star", space.star_sprite())
    w.sprite("fx", "dot", space.soft_dot())
    w.sprite("fx", "streak", space.streak())
    w.pack()


def preview(t):
    """Mirrors BackdropCatalog / VerdantDirector for the offline composer."""
    v = 30 * (0.2 + 0.25)
    fr = lambda n, fps: int(t * fps) % n
    c = [("tile", "sky", t * v * 0.006, 1, (1, 1, 1, 1)),
         ("tile", "far", t * v * 0.014, 1, (1, 1, 1, 1)),
         ("tile", "mid", t * v * 0.024, 1, (1, 1, 1, 1)),
         ("strip", "flow", t * v * 0.025 + t * 0.30, 0.25, 1)]
    c.append(("sprite", "anim", f"waterfall_{fr(8, 12):02d}", -1.2, 2.2 - (t * v * 0.030) % 12, 1.5, 0, (1, 1, 1, 1), False))
    c.append(("sprite", "anim", f"ruin_{fr(4, 3):02d}", 1.35, -1.5 - (t * v * 0.036) % 12 + 6, 1.05, 0, (1, 1, 1, 1), False))
    beat = (t * 0.6) % 1
    c.append(("sprite", "fx", "obelisk1", 1.7, -4.2 - (t * v * 0.036) % 12 + 6, 0.5, 0,
              (1, 1, 1, 1 if beat < 0.12 else 0.8), False))
    c.append(("sprite", "fx", "haze", 0.0, 4 - (t * v * 0.12) % 14, 7.0, 0, (1, 1, 1, 0.27), False))
    c.append(("sprite", "fx", "cloud1", -0.6, 7 - (t * v * 0.3 + 9) % 20, 2.4, 0, (1, 1, 1, 0.3), False))
    for i in range(14):
        x = ((i * 0.618 + 0.2 * math.sin(t * 0.7 + i)) % 1) * 5 - 2.5
        y = (((i * 0.377) % 1) * 13 - t * v * 0.4 + t * 0.25) % 13 - 6.5
        f = int(t * 8 + i * 2.7) % len(FIREFLY)
        c.append(("sprite", "fx", f"firefly_{f:02d}", x, y, 0.16, 0, (1, 1, 1, 0.9), False))
    for i in range(10):
        x = ((i * 0.43 + 0.1 * math.sin(t * 0.5 + i)) % 1) * 5.4 - 2.7
        y = (((i * 0.71) % 1) * 13 - t * v * 0.5 + t * 0.5) % 13 - 6.5
        c.append(("sprite", "fx", f"spore_{int(t * 4 + i) % 4:02d}", x, y, 0.09, 0, (1, 1, 1, 0.6), False))
    return c


if __name__ == "__main__":
    build()
