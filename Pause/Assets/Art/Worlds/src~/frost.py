"""Frost: polar night, an undulating aurora curtain, flat cel ice ranges at
two depths, a glacier river with travelling meltwater dashes, towering ice
peaks, erupting ice geysers, snow gusts and drifting flakes."""
import math
import random

from bgkit import (TAU, World, doc, blur, lin, wrap_y, PNoise, edge_range, peak, pts, ink_attr, grain_defs,
                   grain, jitter_ridge)
from palette import FROST as P, BONE
import space

W, H = 512, 1024


def sky():
    rnd = random.Random(21)
    s = P["sky"]
    defs = (lin("bg", [(0, s[0], 1), (0.25, s[1], 1), (0.5, s[2], 1), (0.75, s[3], 1), (1, s[4], 1)])
            + lin("vig", [(0, P["vignette"], 0.75), (0.3, P["vignette"], 0), (0.7, P["vignette"], 0),
                          (1, P["vignette"], 0.75)], 0, 0, 1, 0)
            + blur("b50", 50) + grain_defs())
    body = [f'<rect width="{W}" height="{H}" fill="url(#bg)"/>']
    glow = []
    for i in range(9):
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        c = rnd.choice(P["sky_glow"])
        glow.append((y, lambda dy, x=x, y=y, c=c, rx=rnd.uniform(120, 240), ry=rnd.uniform(60, 140):
                     f'<ellipse cx="{x:.0f}" cy="{y + dy:.0f}" rx="{rx:.0f}" ry="{ry:.0f}" fill="{c}" '
                     f'opacity="0.5" filter="url(#b50)"/>'))
    body.append(wrap_y(glow, H))
    stars = []
    for i in range(360):
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        r = rnd.choice([0.5, 0.7, 0.9])
        a = rnd.uniform(0.2, 0.6)
        c = rnd.choice(P["stars"])
        stars.append((y, lambda dy, x=x, y=y, r=r, a=a, c=c:
                      f'<rect x="{x - r:.1f}" y="{y + dy - r:.1f}" width="{2 * r:.1f}" height="{2 * r:.1f}" '
                      f'fill="{c}" opacity="{a:.2f}"/>'))
    body.append(wrap_y(stars, H))
    body.append(f'<rect width="{W}" height="{H}" fill="url(#vig)"/>')
    body.append(grain(W, H))
    return doc(W, H, "".join(body), defs)


def tower(rnd, x, base, w, h, dy, side):
    """Ice megastructure seen at a slant: flat face, one shadow side, a
    pale kick on the slanted roof and a few sparse amber windows."""
    slant = h * 0.06 * (1 if side == 0 else -1)
    top_l, top_r = base - h - (slant if slant > 0 else 0), base - h + (slant if slant < 0 else 0)
    face = [(x, base + dy), (x, top_l + dy), (x + w, top_r + dy), (x + w, base + dy)]
    sw = w * 0.32
    shade = [(x + w - sw, base + dy), (x + w - sw, top_r + dy + (top_l - top_r) * sw / w), (x + w, top_r + dy),
             (x + w, base + dy)]
    s = [f'<polygon points="{pts(face)}" fill="{P["tower"]}"/>',
         f'<polygon points="{pts(shade)}" fill="{P["tower_dark"]}"/>',
         f'<polyline points="{x + 1:.1f},{top_l + dy + 2:.1f} {x + w - 1:.1f},{top_r + dy + 2:.1f}" '
         f'stroke="{P["tower_kick"]}" stroke-width="3" fill="none"/>',
         f'<polygon points="{pts(face)}" fill="none" {ink_attr(2)}/>']
    for i in range(int(h / 26)):
        if rnd.random() < 0.35:
            wx = x + rnd.uniform(4, w - sw - 8)
            wy = base + dy - rnd.uniform(10, h - 16)
            s.append(f'<rect x="{wx:.1f}" y="{wy:.1f}" width="4" height="5" fill="{P["window"]}"/>')
    return "".join(s)


def far():
    """Ice towers along both edges (a Neo-Tokyo of ice), stacked so lower
    (nearer) ones overlap the bases of higher ones."""
    rnd = random.Random(31)
    items = []
    for side in (0, 1):
        for i in range(9):
            y = (i + rnd.uniform(-0.2, 0.2)) * H / 9
            w = rnd.uniform(34, 60)
            h = rnd.uniform(120, 220)
            reach = rnd.uniform(70, 165)
            x = (reach - w) if side == 0 else (W - reach)
            items.append((y + h * 0.4, lambda dy, x=x, y=y, w=w, h=h, side=side, seed=rnd.random():
                          tower(random.Random(seed), x, y + h * 0.4, w, h, dy, side)))
    # A dark ice shelf ties the bases together along each edge.
    shelves = []
    for side in (0, 1):
        ys = [H * k / 24 for k in range(25)]
        xs = [52 + 16 * ((k * 7919) % 5) / 4 for k in range(25)]
        xs[-1] = xs[0]
        if side == 0:
            shape = [(-10, -10)] + list(zip(xs, ys)) + [(-10, H + 10)]
        else:
            shape = [(W + 10, -10)] + [(W - x, y) for x, y in zip(xs, ys)] + [(W + 10, H + 10)]
        shelves.append(f'<polygon points="{pts(shape)}" fill="{P["far"]["dark"]}" {ink_attr(2)}/>')
    return doc(W, H, wrap_y(items, H) + "".join(shelves), "")


def river_geom():
    rnd = random.Random(41)
    n1, n2 = PNoise(rnd), PNoise(rnd)
    return (lambda y: W / 2 + 12 * n1(y / H)), (lambda y: 64 + 12 * n2(y / H))


def mid():
    rnd = random.Random(51)
    cxf, hwf = river_geom()
    ys = [H * i / 48 for i in range(49)]
    body = []
    # Snowfields either side of the glacier, ink-edged.
    bl = [(cxf(y) - hwf(y) - 70 - 20 * math.sin(y / H * TAU * 3), y) for y in ys]
    br = [(cxf(y) + hwf(y) + 70 + 20 * math.sin(y / H * TAU * 2 + 1), y) for y in ys]
    body.append(f'<polygon points="{pts(bl + list(reversed(br)))}" fill="{P["snowfield"]}"/>')
    for side in (bl, br):
        body.append(f'<polyline points="{pts(side)}" fill="none" {ink_attr(2.5)}/>')
    drifts = []
    for i in range(30):
        y = rnd.uniform(0, H)
        sd = rnd.choice((-1, 1))
        x = cxf(y) + sd * (hwf(y) + rnd.uniform(16, 60))
        L = rnd.uniform(20, 50)
        drifts.append((y, lambda dy, x=x, y=y, L=L: f'<polygon points="{x - 6:.1f},{y + dy:.1f} {x:.1f},{y + dy - L / 2:.1f} '
                       f'{x + 6:.1f},{y + dy:.1f} {x:.1f},{y + dy + L / 2:.1f}" fill="{P["snowfield_hi"]}"/>'))
    body.append(wrap_y(drifts, H))
    left = [(cxf(y) - hwf(y), y) for y in ys]
    right = [(cxf(y) + hwf(y), y) for y in ys]
    body.append(f'<polygon points="{pts(left + list(reversed(right)))}" fill="{P["ice"]}"/>')
    # One shadow plane: the shaded right bank of the channel.
    sh = [(cxf(y) + hwf(y) * 0.45, y) for y in ys]
    body.append(f'<polygon points="{pts(sh + list(reversed(right)))}" fill="{P["ice_shadow"]}"/>')
    plates = []
    for i in range(34):
        y = rnd.uniform(0, H)
        x = cxf(y) + rnd.uniform(-0.75, 0.75) * hwf(y)
        r = rnd.uniform(6, 14)
        nv = rnd.randint(4, 6)
        a0 = rnd.uniform(0, TAU)
        k = [(x + r * math.cos(a) * rnd.uniform(0.6, 1.2), y + r * 1.4 * math.sin(a) * rnd.uniform(0.6, 1.2))
             for a in [a0 + j * TAU / nv for j in range(nv)]]
        plates.append((y, lambda dy, k=k: f'<polygon points="{pts([(px, py + dy) for px, py in k])}" '
                       f'fill="{P["ice_plate"]}" {ink_attr(1.5)}/>'))
    body.append(wrap_y(plates, H))
    for side in (left, right):
        body.append(f'<polyline points="{pts(side)}" fill="none" {ink_attr(3)}/>')
    pines = []
    for i in range(80):
        y = rnd.uniform(0, H)
        sd = rnd.choice((-1, 1))
        x = cxf(y) + sd * (hwf(y) + rnd.uniform(20, 66))
        s = rnd.uniform(5, 9)
        pines.append((y, lambda dy, x=x, y=y, s=s: f'<polygon points="{x:.1f},{y + dy - s * 2:.1f} '
                      f'{x - s * 0.6:.1f},{y + dy:.1f} {x + s * 0.6:.1f},{y + dy:.1f}" fill="{P["pine"]}"/>'))
    body.append(wrap_y(pines, H))
    d, b = edge_range(rnd, W, H, P["near"], 6, 130, 200, 130, 210, 118, cap=0.36, rim=P["rim"], ink=3,
                      foot=P["near"]["dark"])
    return doc(W, H, "".join(body) + b, d)


def flow():
    """Meltwater highlight dashes over the river core (128 px strip). It
    scrolls faster than the river so the dashes read as flowing."""
    w = 128
    rnd = random.Random(61)
    items = []
    for i in range(30):
        x = rnd.uniform(40, 88)
        y = rnd.uniform(0, H)
        L = rnd.uniform(14, 44)
        items.append((y, lambda dy, x=x, y=y, L=L:
                      f'<rect x="{x - 1.6:.1f}" y="{y + dy - L / 2:.1f}" width="3.2" height="{L:.1f}" '
                      f'fill="{P["water_dash"]}" opacity="0.8"/>'))
    return doc(w, H, wrap_y(items, H), "")


def aurora(phase, w=508, h=200, seed=71):
    """Aurora curtain (a light, so a soft glow is allowed under hard rays).
    The fold travels along the ribbon; ray brightness steps with `phase`."""
    rnd = random.Random(seed)
    p = phase * TAU
    defs = (blur("b16", 16)
            + lin("ray", [(0, P["aurora_fringe"], 0.0), (0.1, P["aurora_fringe"], 0.7), (0.24, P["aurora_core"], 1),
                          (0.6, P["aurora_body"], 0.55), (1, P["aurora_body"], 0)])
            + lin("fade", [(0, "#fff", 0), (0.12, "#fff", 1), (0.88, "#fff", 1), (1, "#fff", 0)], 0, 0, 1, 0)
            + f'<mask id="ends" maskUnits="userSpaceOnUse" x="0" y="0" width="{w}" height="{h}">'
              f'<rect width="{w}" height="{h}" fill="url(#fade)"/></mask>')

    def cy(x):
        t = x / w
        return 62 + 22 * math.sin(TAU * t + p) + 10 * math.sin(TAU * t * 2.3 - 2 * p)

    curve = [(x, cy(x)) for x in range(0, w + 1, 8)]
    body = [f'<polyline points="{pts(curve)}" stroke="{P["aurora_body"]}" stroke-width="44" fill="none" '
            f'opacity="0.3" transform="translate(0 34)" filter="url(#b16)"/>']
    for i in range(80):
        x = 4 + i * (w - 8) / 79
        y = cy(x)
        k = 0.5 + 0.5 * math.sin(x * 0.05 - p * 2) * math.sin(x * 0.013 + p)
        k = round(k * 3) / 3                 # stepped: cel-style light levels
        L = 46 + 70 * k + rnd.uniform(0, 14)
        body.append(f'<rect x="{x - 2:.1f}" y="{y - 12:.1f}" width="4" height="{L:.1f}" fill="url(#ray)" '
                    f'opacity="{0.25 + 0.6 * k:.2f}"/>')
    body.append(f'<polyline points="{pts(curve)}" stroke="{P["aurora_core"]}" stroke-width="2.5" fill="none" opacity="0.7"/>')
    return doc(w, h, f'<g mask="url(#ends)">{"".join(body)}</g>', defs)


def ice_peak(seed):
    rnd = random.Random(seed)
    w, h = 300, 300
    # Skirt slopes off the sprite's left edge; the director places the
    # sprite against a screen edge (mirrored on the right).
    d, b = peak(rnd, w * 0.55, h * 0.62, w * 0.8, h * 0.58, P["big"], cap=0.42, rim=P["rim"], ink=4,
                side=0, skirt=0.62)
    return doc(w, h, b, d)


def geyser(phase):
    """Ice geyser: the jet snaps up, holds, and collapses into frost chips.
    Cartoon timing: fast rise, a held peak, a fall."""
    w, h = 96, 192
    rnd = random.Random(5)
    t = phase
    life = min(1.0, t / 0.25) if t < 0.6 else max(0.0, 1 - (t - 0.6) / 0.4)
    jh = 20 + 150 * life
    base = h - 10
    body = [f'<polygon points="30,{base} 66,{base} 58,{base - 8} 38,{base - 8}" fill="{P["geyser_shadow"]}" {ink_attr(2)}/>']
    if life > 0.05:
        wd = 7 + 6 * life
        body.append(f'<polygon points="{48 - wd},{base - 6} {48 - wd * 0.5:.1f},{base - jh:.1f} {48 + wd * 0.5:.1f},{base - jh:.1f} '
                    f'{48 + wd},{base - 6}" fill="{P["geyser"]}" {ink_attr(2.2)}/>')
        body.append(f'<polygon points="{48 + wd * 0.2:.1f},{base - 6} {48 + wd * 0.3:.1f},{base - jh:.1f} '
                    f'{48 + wd * 0.5:.1f},{base - jh:.1f} {48 + wd},{base - 6}" fill="{P["geyser_shadow"]}"/>')
        body.append(f'<rect x="{48 - wd * 0.4:.1f}" y="{base - jh * 0.8:.1f}" width="2.5" height="{jh * 0.4:.1f}" fill="{BONE}"/>')
        for k in range(6):
            a = -math.pi / 2 + (k - 2.5) * 0.35
            r = 10 + 10 * life
            x = 48 + math.cos(a) * r
            y = base - jh + math.sin(a) * r * 0.6
            body.append(f'<polygon points="{x:.1f},{y - 5:.1f} {x + 4:.1f},{y:.1f} {x:.1f},{y + 5:.1f} {x - 4:.1f},{y:.1f}" '
                        f'fill="{P["geyser"]}" {ink_attr(1.2)}/>')
    for i in range(10):
        tt = (t * 1.5 + i / 10) % 1
        x = 48 + rnd.uniform(-34, 34) * tt
        y = base - jh * (1 - tt) * 0.9 - 6
        if life > 0.1 or tt > 0.5:
            body.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="3" height="3" fill="{P["geyser"]}" opacity="{1 - tt * 0.7:.2f}"/>')
    return doc(w, h, "".join(body))


def snow_gust(seed):
    """A gust of wind-blown snow: tapered hard streaks, no soft blobs."""
    rnd = random.Random(seed)
    w, h = 256, 128
    g = []
    for i in range(16):
        y = rnd.uniform(20, 108)
        x0 = rnd.uniform(0, 120)
        L = rnd.uniform(60, 130)
        g.append(f'<polygon points="{x0:.0f},{y:.0f} {x0 + L:.0f},{y - 1.5:.0f} {x0 + L + 6:.0f},{y:.0f} {x0 + L:.0f},{y + 1.5:.0f}" '
                 f'fill="{BONE}" opacity="{rnd.uniform(0.4, 0.9):.2f}"/>')
    return doc(w, h, "".join(g))


def build():
    w = World("Frost")
    w.tile("sky", sky())
    w.tile("far", far())
    w.tile("mid", mid())
    w.tile("flow", flow())
    w.flipbook("anim", "aurora", 8, aurora)
    w.sprite("fx", "peak0", ice_peak(3))
    w.sprite("fx", "peak1", ice_peak(8))
    w.sprite("fx", "peak2", ice_peak(17))
    w.flipbook("fx", "geyser", 8, geyser)
    w.sprite("fx", "cloud", snow_gust(4))
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
         ("strip", "flow", t * v * 0.10 + t * 1.6, 0.25, 1)]
    c.append(("sprite", "fx", f"geyser_{fr(8, 9):02d}", -1.3, 7 - (t * v * 0.10) % 14, 0.85, 0, (0.7, 0.88, 1, 0.8), False))
    c.append(("sprite", "fx", "peak0", -1.6, 7 - (t * v * 0.16 + 3) % 16, 2.9, 0, (1, 1, 1, 1), False))
    c.append(("sprite", "fx", "peak2", 1.6, 7 - (t * v * 0.16 + 10) % 16, 2.6, 0, (1, 1, 1, 1), True))
    c.append(("sprite", "anim", f"aurora_{fr(8, 8):02d}", 0.0, 7 - (t * v * 0.22 + 4) % 18, 6.2, -8, (0.75, 0.9, 0.9, 0.6), False))
    for i in range(26):
        x = ((i * 0.618 + 0.3 * math.sin(t * 0.8 + i)) % 1) * 5 - 2.5
        y = (((i * 0.377) % 1) * 13 - t * (0.6 + v * 0.5)) % 13 - 6.5
        c.append(("sprite", "fx", "dot", x, y, 0.07 + 0.04 * (i % 3), 0, (0.8, 0.9, 1, 0.5), False))
    return c


if __name__ == "__main__":
    build()
