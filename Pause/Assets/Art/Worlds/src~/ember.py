"""Ember: a volcanic world in flat anime cels. Basalt plains threaded with
sodium-amber fissures, layered ridges with glowing cracks, a lava river
with drifting crust and travelling highlight dashes, erupting volcanoes,
lava bursts, rising sparks and falling ash."""
import math
import random

from bgkit import (TAU, World, doc, blur, lin, wrap_y, PNoise, edge_range, peak, pts, ink_attr,
                   hard_glow, jitter_ridge, grain_defs, grain)
from palette import EMBER as P
import space

W, H = 512, 1024


def sky():
    rnd = random.Random(201)
    s = P["sky"]
    defs = (lin("bg", [(0, s[0], 1), (0.25, s[1], 1), (0.5, s[2], 1), (0.75, s[3], 1), (1, s[4], 1)])
            + lin("vig", [(0, P["vignette"], 0.75), (0.28, P["vignette"], 0), (0.72, P["vignette"], 0),
                          (1, P["vignette"], 0.75)], 0, 0, 1, 0)
            + blur("b40", 40) + grain_defs())
    body = [f'<rect width="{W}" height="{H}" fill="url(#bg)"/>']
    smoke = []
    for i in range(10):
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        c = rnd.choice(P["smoke"])
        smoke.append((y, lambda dy, x=x, y=y, c=c, rx=rnd.uniform(100, 200), ry=rnd.uniform(60, 130):
                      f'<ellipse cx="{x:.0f}" cy="{y + dy:.0f}" rx="{rx:.0f}" ry="{ry:.0f}" fill="{c}" '
                      f'opacity="0.55" filter="url(#b40)"/>'))
    body.append(wrap_y(smoke, H))
    cracks = []
    for i in range(16):
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        seg = jitter_ridge(rnd, x, y, x + rnd.uniform(-70, 70), y + rnd.uniform(40, 110), 4, 10)
        a = rnd.uniform(0.35, 0.7)
        cracks.append((y, lambda dy, seg=seg, a=a: f'<g transform="translate(0 {dy})">'
                       f'<polyline points="{pts(seg)}" stroke="{P["crack"]}" stroke-width="4" fill="none" opacity="{a * 0.4:.2f}"/>'
                       f'<polyline points="{pts(seg)}" stroke="{P["crack_hot"]}" stroke-width="1.4" fill="none" opacity="{a:.2f}"/></g>'))
    body.append(wrap_y(cracks, H))
    ash = []
    for i in range(200):
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        c = rnd.choice(P["ash"] + [P["spark"]])
        a = rnd.uniform(0.25, 0.55)
        ash.append((y, lambda dy, x=x, y=y, c=c, a=a: f'<rect x="{x:.1f}" y="{y + dy:.1f}" width="1.4" height="1.4" '
                    f'fill="{c}" opacity="{a:.2f}"/>'))
    body.append(wrap_y(ash, H))
    body.append(f'<rect width="{W}" height="{H}" fill="url(#vig)"/>')
    body.append(grain(W, H))
    return doc(W, H, "".join(body), defs)


def far():
    rnd = random.Random(211)
    d, b = edge_range(rnd, W, H, P["far"], 7, 150, 240, 140, 230, 165, cap=0.14, ink=2.2,
                      cracks=P["crack"], foot=P["far"]["dark"])
    return doc(W, H, b, d)


def river_geom():
    rnd = random.Random(221)
    n1, n2 = PNoise(rnd), PNoise(rnd)
    return (lambda y: W / 2 + 12 * n1(y / H)), (lambda y: 60 + 10 * n2(y / H))


def mid():
    rnd = random.Random(231)
    cxf, hwf = river_geom()
    ys = [H * i / 48 for i in range(49)]
    defs = blur("b10", 10)
    body = []
    gl = [(cxf(y) - hwf(y) - 26, y) for y in ys]
    gr = [(cxf(y) + hwf(y) + 26, y) for y in ys]
    body.append(f'<polygon points="{pts(gl + list(reversed(gr)))}" fill="{P["lava_glow"]}" opacity="0.45" filter="url(#b10)"/>')
    bl = [(cxf(y) - hwf(y) - 14, y) for y in ys]
    br = [(cxf(y) + hwf(y) + 14, y) for y in ys]
    body.append(f'<polygon points="{pts(bl + list(reversed(br)))}" fill="{P["bank"]}"/>')
    left = [(cxf(y) - hwf(y), y) for y in ys]
    right = [(cxf(y) + hwf(y), y) for y in ys]
    body.append(f'<polygon points="{pts(left + list(reversed(right)))}" fill="{P["lava"]}"/>')
    sh = [(cxf(y) + hwf(y) * 0.4, y) for y in ys]
    body.append(f'<polygon points="{pts(sh + list(reversed(right)))}" fill="{P["lava_shadow"]}"/>')
    plates = []
    for i in range(22):
        y = rnd.uniform(0, H)
        x = cxf(y) + rnd.uniform(-0.8, 0.8) * hwf(y)
        r = rnd.uniform(5, 10)
        nv = rnd.randint(4, 6)
        a0 = rnd.uniform(0, TAU)
        k = [(x + r * math.cos(a) * rnd.uniform(0.6, 1.2), y + r * 1.4 * math.sin(a) * rnd.uniform(0.6, 1.2))
             for a in [a0 + j * TAU / nv for j in range(nv)]]
        plates.append((y, lambda dy, k=k: f'<polygon points="{pts([(px, py + dy) for px, py in k])}" '
                       f'fill="{P["crust"]}" stroke="{P["seam"]}" stroke-width="1.4" stroke-opacity="0.6" stroke-linejoin="miter"/>'))
    body.append(wrap_y(plates, H))
    for side in (left, right, bl, br):
        body.append(f'<polyline points="{pts(side)}" fill="none" {ink_attr(2.5)}/>')
    rocks = []
    for i in range(36):
        y = rnd.uniform(0, H)
        sd = rnd.choice((-1, 1))
        x = cxf(y) + sd * (hwf(y) + rnd.uniform(24, 100))
        r = rnd.uniform(4, 9)
        rocks.append((y, lambda dy, x=x, y=y, r=r: f'<polygon points="{x - r:.1f},{y + dy:.1f} {x - r * 0.3:.1f},{y + dy - r * 0.8:.1f} '
                      f'{x + r:.1f},{y + dy - r * 0.3:.1f} {x + r * 0.6:.1f},{y + dy + r * 0.6:.1f}" fill="{P["rock"]}" {ink_attr(1.5)}/>'
                      f'<polygon points="{x - r * 0.6:.1f},{y + dy - r * 0.1:.1f} {x - r * 0.3:.1f},{y + dy - r * 0.6:.1f} '
                      f'{x + r * 0.2:.1f},{y + dy - r * 0.3:.1f}" fill="{P["rock_hi"]}"/>'))
    body.append(wrap_y(rocks, H))
    d, b = edge_range(rnd, W, H, P["near"], 6, 130, 200, 130, 210, 118, cap=0.12, rim=P["rim"], ink=3,
                      cracks=P["crack"], foot=P["near"]["dark"])
    return doc(W, H, "".join(body) + b, defs + d)


def flow():
    """Lava highlight dashes over the river core; scroll faster than the
    crust so the river visibly flows."""
    w = 128
    rnd = random.Random(241)
    items = []
    for i in range(26):
        x = rnd.uniform(40, 88)
        y = rnd.uniform(0, H)
        L = rnd.uniform(16, 48)
        items.append((y, lambda dy, x=x, y=y, L=L:
                      f'<rect x="{x - 4:.1f}" y="{y + dy - L / 2:.1f}" width="8" height="{L:.1f}" '
                      f'fill="{P["lava_glow"]}" opacity="0.6"/>'
                      f'<rect x="{x - 1.6:.1f}" y="{y + dy - L / 2 + 3:.1f}" width="3.2" height="{L - 6:.1f}" '
                      f'fill="{P["lava_dash"]}" opacity="0.9"/>'))
    return doc(w, H, wrap_y(items, H), "")


def volcano():
    """Flat cone with one shadow plane, ink outline and a glowing crater.
    The skirt slopes off the sprite's left edge (mirrored on the right)."""
    rnd = random.Random(251)
    w, h = 300, 260
    d, b = peak(rnd, w * 0.55, h * 0.7, w * 0.8, h * 0.56, P["cone"], cap=0.14, rim=P["rim"],
                cracks=P["crack_hot"], side=0, skirt=0.5, ink=4)
    apex_x = None
    # Crater sits on the apex; find it from the outline polygon (highest point).
    import re
    ptsl = [tuple(map(float, p.split(","))) for p in re.search(r'points="([^"]+)"', b).group(1).split()]
    apex_x, apex_y = min(ptsl, key=lambda p: p[1])
    crater = (hard_glow(apex_x, apex_y + 4, 26, P["eruption_hot"], 0.8, 3)
              + f'<polygon points="{apex_x - 16:.0f},{apex_y + 2:.0f} {apex_x + 16:.0f},{apex_y + 2:.0f} '
                f'{apex_x + 10:.0f},{apex_y + 9:.0f} {apex_x - 10:.0f},{apex_y + 9:.0f}" fill="{P["eruption_hi"]}" {ink_attr(2.5)}/>')
    return doc(w, h, b + crater, d)


def smoke_cel(cx, cy, r, rot, light, dark, alpha):
    """Angular smoke cel: a chamfered lump (flat base, one shadow wedge, ink)."""
    pts_ = []
    for i in range(7):
        a = math.radians(rot + i * 360 / 7)
        rr = r * (1.0 if i % 2 == 0 else 0.78)
        pts_.append((cx + rr * math.cos(a), cy + rr * math.sin(a) * 0.85))
    shade = [(cx, cy)] + pts_[1:4]
    return (f'<g opacity="{alpha:.2f}"><polygon points="{pts(pts_)}" fill="{light}"/>'
            f'<polygon points="{pts(shade)}" fill="{dark}"/>'
            f'<polygon points="{pts(pts_)}" fill="none" {ink_attr(2)}/></g>')


def eruption(phase):
    """Cartoon eruption above the crater (anchored bottom-centre). Key pose
    held, then a snap: the fountain is a light (no ink), smoke rolls up as
    angular DUSK/INDIGO cels, lava bombs fly on parabolas. No red."""
    w, h = 192, 256
    rnd = random.Random(19)
    body = []
    for i in range(6):
        t = (i / 6 + phase) % 1
        y = h - 40 - t * (h - 60)
        r = 12 + t * 26
        x = w / 2 + math.sin(i * 2.1) * 18 * t
        body.append(smoke_cel(x, y, r, i * 23 + phase * 40, P["smoke_cel"], P["smoke_cel_dark"], 1 - t * 0.6))
    k = phase * 4 % 1
    life = 1.0 if k < 0.5 else 0.55          # snappy: big for two beats, small for two
    jh = 40 + 80 * life
    base = h - 8
    body.append(hard_glow(w / 2, base - 4, 30, P["eruption_hot"], 0.7, 3))
    body.append(f'<polygon points="{w / 2 - 12},{base} {w / 2 - 4},{base - jh:.0f} {w / 2 + 4},{base - jh:.0f} {w / 2 + 12},{base}" '
                f'fill="{P["eruption_hot"]}"/>')
    body.append(f'<polygon points="{w / 2 - 5},{base} {w / 2 - 1.5},{base - jh * 0.8:.0f} {w / 2 + 1.5},{base - jh * 0.8:.0f} {w / 2 + 5},{base}" '
                f'fill="{P["eruption_hi"]}"/>')
    body.append(f'<rect x="{w / 2 - 1}" y="{base - jh * 0.6:.0f}" width="2" height="{jh * 0.5:.0f}" fill="{P["eruption_core"]}"/>')
    for i in range(12):
        t = (i / 12 + phase * 2) % 1
        vx = rnd.uniform(-60, 60)
        vy = rnd.uniform(150, 220)
        x = w / 2 + vx * t
        y = base - (vy * t - 200 * t * t)
        if y > base - 2:
            continue
        r = rnd.uniform(3, 5)
        body.append(f'<polygon points="{x:.1f},{y - r:.1f} {x + r:.1f},{y:.1f} {x:.1f},{y + r:.1f} {x - r:.1f},{y:.1f}" '
                    f'fill="{P["eruption_hi"]}" {ink_attr(1.5)}/>')
    return doc(w, h, "".join(body))


def burst(phase):
    """Lava burst: a crust plate heaves (anticipation), cracks open on a
    sodium seam, then flings angular shards. Held key pose first."""
    s = 96
    body = []
    cx, cy = 48, 58
    if phase < 0.25:                       # hold: a dark plate with a dim seam
        k = 1.0
    elif phase < 0.5:                      # anticipation: the plate heaves up
        k = 1.0 + (phase - 0.25) * 1.6
    else:
        k = None
    if k is not None:
        plate = [(cx - 22 * k, cy), (cx - 12 * k, cy - 10 * k), (cx + 10 * k, cy - 12 * k), (cx + 24 * k, cy - 2),
                 (cx + 14 * k, cy + 8), (cx - 14 * k, cy + 9)]
        body.append(f'<polygon points="{pts(plate)}" fill="{P["crust"]}" {ink_attr(2)}/>')
        seam_a = 0.4 if phase < 0.25 else 0.9
        body.append(f'<polyline points="{cx - 14 * k:.1f},{cy:.1f} {cx - 2:.1f},{cy - 5 * k:.1f} {cx + 6:.1f},{cy + 2:.1f} '
                    f'{cx + 16 * k:.1f},{cy - 3:.1f}" stroke="{P["eruption_hot"]}" stroke-width="2.4" fill="none" '
                    f'opacity="{seam_a}"/>')
    else:
        t = (phase - 0.5) / 0.5
        body.append(hard_glow(cx, cy, 26 * (1 - t) + 6, P["eruption_hot"], 0.8 * (1 - t) + 0.1, 3))
        for i in range(8):
            a = i * TAU / 8 + 0.3
            r = 10 + 30 * t
            x, y = cx + math.cos(a) * r, cy + math.sin(a) * r * 0.7 - 16 * t + 20 * t * t
            q = 5 * (1 - t) + 1.5
            shard = [(x, y - q), (x + q * 0.7, y + q * 0.4), (x - q * 0.7, y + q * 0.6)]
            body.append(f'<polygon points="{pts(shard)}" fill="{P["lava_dash"]}" {ink_attr(1.2)}/>')
    return doc(s, s, "".join(body))


def build():
    w = World("Ember")
    w.tile("sky", sky())
    w.tile("far", far())
    w.tile("mid", mid())
    w.tile("flow", flow())
    w.flipbook("anim", "eruption", 12, eruption)
    w.flipbook("anim", "burst", 8, burst)
    w.sprite("fx", "volcano", volcano())
    w.sprite("fx", "star", space.star_sprite())
    w.sprite("fx", "dot", space.soft_dot())
    w.sprite("fx", "streak", space.streak())
    w.pack()


def preview(t):
    v = 30 * (0.15 + 0.25)
    fr = lambda n, fps: int(t * fps) % n
    shimmer = 0.035 * math.sin(t * 7.3)
    c = [("tile", "sky", t * v * 0.010, 1, (1, 1, 1, 1)),
         ("tile", "far", t * v * 0.04, 1, (1, 1, 1, 1)),
         ("tile", "mid", t * v * 0.10, 1, (1, 1, 1, 1)),
         ("strip", "flow", t * v * 0.10 + t * 0.9, 0.25 + shimmer * 0.05, 0.75)]
    vy = 7 - (t * v * 0.16 + 5) % 16
    vx = -1.5
    c.append(("sprite", "fx", "volcano", vx, vy, 2.8, 0, (1, 1, 1, 1), False))
    c.append(("sprite", "anim", f"eruption_{fr(12, 12):02d}", vx + 2.8 * (0.55 - 0.5) + 0.05, vy + 2.8 * 260 / 300 * 0.5 - 0.2 + 0.95,
              1.7, 0, (0.9, 0.8, 0.75, 0.9), False))
    c.append(("sprite", "anim", f"burst_{fr(8, 10):02d}", 0.3, 7 - (t * v * 0.10 + 3) % 14, 0.6, 0, (0.9, 0.75, 0.65, 0.85), False))
    for i in range(22):
        x = ((i * 0.618 + 0.15 * math.sin(t * 1.3 + i)) % 1) * 5 - 2.5
        y = (((i * 0.377) % 1) * 13 - t * v * 0.45 + t * 1.1) % 13 - 6.5
        c.append(("sprite", "fx", "dot", x, y, 0.06 + 0.03 * (i % 3), 0, (1, 0.55, 0.18, 0.75), False))
    return c


if __name__ == "__main__":
    build()
