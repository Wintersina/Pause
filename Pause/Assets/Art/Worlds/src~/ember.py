"""Ember, seen from atmosphere level: basalt plains far below threaded
with sodium fissures, distant hazed cones, a narrow lava river valley,
small far-off erupting volcanoes (flat cels, 8-frame eruption) and lava
bursts, and ash-cloud cels / haze bands / sparks and ash passing close by.
No red: lava is sodium."""
import math
import random

from bgkit import (TAU, World, doc, blur, lin, wrap_y, PNoise, edge_range, peak, pts, ink_attr,
                   hard_glow, jitter_ridge, grain_defs, grain)
from palette import EMBER as P
from altitude import hazed, cloud_cel, haze_band, scatter_peaks
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
        a = rnd.uniform(0.15, 0.32)   # deepest layer: faint (aerial perspective)
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
    """Distant basalt cones, crater glints and tiny heat-extractor frames.
    The frames echo Space's built silhouette language while staying far,
    dark and subordinate to the lava lane."""
    rnd = random.Random(211)
    d, b = scatter_peaks(rnd, W, H, P["far"], 26, 26, 52, cap=0.14, ink=1.2)
    glints = []
    for i in range(10):
        x, y = rnd.uniform(20, W - 20), rnd.uniform(0, H)
        glints.append((y, lambda dy, x=x, y=y: f'<rect x="{x:.0f}" y="{y + dy:.0f}" width="3" height="2" fill="{P["crack_hot"]}" opacity="0.6"/>'))
    rigs = []
    for i in range(7):
        x, y = rnd.uniform(48, W - 48), rnd.uniform(0, H)
        rw, rh = rnd.uniform(10, 16), rnd.uniform(16, 26)
        rigs.append((y, lambda dy, x=x, y=y, rw=rw, rh=rh:
                     f'<g transform="translate(0 {dy:.1f})">'
                     f'<polygon points="{x-rw:.1f},{y:.1f} {x-rw*.55:.1f},{y-rh:.1f} '
                     f'{x+rw*.55:.1f},{y-rh:.1f} {x+rw:.1f},{y:.1f}" fill="{P["far"]["lit"]}" {ink_attr(1)}/>'
                     f'<rect x="{x-rw*.7:.1f}" y="{y-rh*.52:.1f}" width="{rw*1.4:.1f}" height="2" fill="{P["far"]["dark"]}"/>'
                     f'<rect x="{x-1:.1f}" y="{y-rh-4:.1f}" width="2" height="3" fill="{P["crack_hot"]}" opacity="0.7"/>'
                     f'</g>'))
    hd, hb = hazed(b + wrap_y(rigs, H), P["air"], 0.25)
    return doc(W, H, hb + wrap_y(glints, H), d + hd)


def river_geom():
    rnd = random.Random(221)
    n1, n2 = PNoise(rnd), PNoise(rnd)
    return (lambda y: W / 2 + 9 * n1(y / H)), (lambda y: 20 + 4 * n2(y / H))


def mid():
    """The lava valley far below: a narrow sodium lava river with crust,
    obsidian banks and low cracked ridges along the edges."""
    rnd = random.Random(231)
    cxf, hwf = river_geom()
    ys = [H * i / 48 for i in range(49)]
    body = []
    bl = [(cxf(y) - hwf(y) - 9, y) for y in ys]
    br = [(cxf(y) + hwf(y) + 9, y) for y in ys]
    body.append(f'<polygon points="{pts(bl + list(reversed(br)))}" fill="{P["bank"]}"/>')
    left = [(cxf(y) - hwf(y), y) for y in ys]
    right = [(cxf(y) + hwf(y), y) for y in ys]
    body.append(f'<polygon points="{pts(left + list(reversed(right)))}" fill="{P["lava"]}"/>')
    sh = [(cxf(y) + hwf(y) * 0.4, y) for y in ys]
    body.append(f'<polygon points="{pts(sh + list(reversed(right)))}" fill="{P["lava_shadow"]}"/>')
    plates = []
    for i in range(30):
        y = rnd.uniform(0, H)
        x = cxf(y) + rnd.uniform(-0.6, 0.6) * hwf(y)
        r = rnd.uniform(2.5, 5)
        k = [(x - r, y), (x, y - r * 1.3), (x + r, y), (x, y + r * 1.3)]
        plates.append((y, lambda dy, k=k: f'<polygon points="{pts([(px, py + dy) for px, py in k])}" '
                       f'fill="{P["crust"]}" stroke="{P["seam"]}" stroke-width="0.8"/>'))
    body.append(wrap_y(plates, H))
    for side in (left, right, bl, br):
        body.append(f'<polyline points="{pts(side)}" fill="none" {ink_attr(1.6)}/>')
    rocks = []
    for i in range(60):
        y = rnd.uniform(0, H)
        sd = rnd.choice((-1, 1))
        x = cxf(y) + sd * (hwf(y) + rnd.uniform(14, 70))
        r = rnd.uniform(2, 4.5)
        rocks.append((y, lambda dy, x=x, y=y, r=r: f'<polygon points="{x - r:.1f},{y + dy:.1f} {x - r * 0.3:.1f},{y + dy - r * 0.8:.1f} '
                      f'{x + r:.1f},{y + dy - r * 0.3:.1f} {x + r * 0.6:.1f},{y + dy + r * 0.6:.1f}" fill="{P["rock"]}"/>'))
    body.append(wrap_y(rocks, H))
    d, b = edge_range(rnd, W, H, P["near"], 12, 50, 84, 40, 70, 100, cap=0.12, rim=P["rim"], ink=1.4,
                      cracks=P["crack"], foot=P["near"]["dark"])
    hd, hb = hazed("".join(body) + b, P["air"], 0.10)
    return doc(W, H, hb, d + hd)


def flow():
    """Amber lava dashes on the river core (128 px strip, within +-8 px)."""
    w = 128
    rnd = random.Random(241)
    items = []
    for i in range(30):
        x = rnd.uniform(57, 71)
        y = rnd.uniform(0, H)
        L = rnd.uniform(6, 16)
        items.append((y, lambda dy, x=x, y=y, L=L:
                      f'<rect x="{x - 1:.1f}" y="{y + dy - L / 2:.1f}" width="2.2" height="{L:.1f}" '
                      f'fill="{P["lava_dash"]}" opacity="0.8"/>'))
    return doc(w, H, wrap_y(items, H), "")


VOLCANO_FRAMES = 8


def volcano(phase):
    """A stratovolcano seen from altitude, drawn as a flat cel: an apron,
    an angular cone with one hard shadow plane and one highlight ridge,
    a thick ink contour, a crater rim with a sodium lava pool, two lava
    channels whose amber dashes run downhill, and a small eruption above
    the crater with cartoon timing: hold, anticipation, burst, settle."""
    w, h = 256, 288
    f = int(phase * VOLCANO_FRAMES)
    apron = [(10, 262), (58, 236), (198, 232), (248, 258), (212, 282), (44, 284)]
    cone = [(40, 252), (64, 210), (82, 168), (100, 128), (110, 118), (146, 118), (156, 126), (172, 166),
            (190, 206), (218, 252), (160, 260), (100, 260)]
    shadow = [(146, 118), (156, 126), (172, 166), (190, 206), (218, 252), (160, 260), (150, 200), (140, 140)]
    kick = [(100, 128), (82, 168), (64, 210), (54, 228), (62, 228), (74, 206), (90, 168), (106, 132)]
    rim = [(108, 118), (118, 110), (138, 110), (148, 118), (138, 124), (118, 124)]
    pool = [(116, 117), (124, 113), (134, 113), (140, 117), (134, 121), (122, 121)]
    ch1 = [(118, 124), (124, 124), (112, 170), (106, 208), (96, 244), (88, 244), (98, 206), (104, 168)]
    ch2 = [(136, 124), (141, 124), (152, 168), (158, 206), (166, 240), (160, 240), (151, 206), (145, 168)]
    solid = (f'<polygon points="{pts(apron)}" fill="{P["apron"]}"/>'
             f'<polygon points="{pts(apron)}" fill="none" {ink_attr(3)}/>'
             f'<polygon points="{pts(cone)}" fill="{P["cone_lit"]}"/>'
             f'<polygon points="{pts(shadow)}" fill="{P["cone_dark"]}"/>'
             f'<polygon points="{pts(kick)}" fill="{P["cone_hi"]}"/>'
             f'<polygon points="{pts(rim)}" fill="{P["cone_dark"]}"/>'
             f'<polygon points="{pts(cone)}" fill="none" {ink_attr(4)}/>')
    d, body = hazed(solid, P["air"], 0.15)
    hot = 1.0 if f in (2, 3, 4) else 0.75
    lava = (f'<g opacity="{0.85 * hot:.2f}">'
            f'<polygon points="{pts(ch1)}" fill="{P["eruption_hot"]}" {ink_attr(1.6)}/>'
            f'<polygon points="{pts(ch2)}" fill="{P["crack"]}" {ink_attr(1.6)}/>'
            f'<polygon points="{pts(pool)}" fill="{P["eruption_hot"]}"/></g>'
            f'<polygon points="{pts(rim)}" fill="none" {ink_attr(2.5)}/>')
    dashes = []
    for i in range(3):
        t = (i / 3 + phase) % 1
        x = 121 - 28 * t
        y = 128 + 110 * t
        dashes.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="2.5" height="7" fill="{P["eruption_hi"]}"/>')
    # Eruption timing (8 frames): 0-1 hold, 2 anticipation (pool flares),
    # 3-4 burst, 5-7 settle while smoke cels climb.
    erupt = []
    if f >= 2:
        erupt.append(hard_glow(128, 116, 16 if f == 2 else 22, P["eruption_hot"], 0.7, 3))
    if f in (3, 4):
        jh = 70 if f == 3 else 56
        erupt.append(f'<polygon points="118,114 124,{114 - jh} 128,{114 - jh - 10} 132,{114 - jh} 138,114" fill="{P["eruption_hot"]}"/>')
        erupt.append(f'<polygon points="124,114 128,{114 - jh + 8} 132,114" fill="{P["eruption_hi"]}"/>')
        erupt.append(f'<rect x="127" y="{114 - jh + 18}" width="2" height="{jh - 22}" fill="{P["eruption_core"]}"/>')
        for k, (dx, dy) in enumerate([(-26, -50), (24, -44), (-14, -70), (18, -66), (-34, -30), (32, -28)]):
            s = 1.0 if f == 3 else 1.35
            x, y = 128 + dx * s, 112 + dy * (1.0 if f == 3 else 0.85) + (0 if f == 3 else 10)
            r = 3.5
            erupt.append(f'<polygon points="{x:.1f},{y - r:.1f} {x + r:.1f},{y:.1f} {x:.1f},{y + r:.1f} {x - r:.1f},{y:.1f}" '
                         f'fill="{P["eruption_hi"]}" {ink_attr(1.2)}/>')
    smoke = []
    puffs = [(0, 128, 96, 8), (1, 120, 80, 10), (2, 136, 64, 12), (3, 124, 46, 14), (4, 132, 26, 16)]
    for k, x, y, r in puffs:
        age = (f - 3 - k) % VOLCANO_FRAMES
        if f < 3 and k > 1:
            continue
        lift = age * 3
        smoke.append(smoke_cel(x + (k % 2) * 4, y - lift if f >= 3 else y + 20, r, k * 25 + f * 10,
                               P["smoke_cel"], P["smoke_cel_dark"], 0.9 - 0.08 * age))
    return doc(w, h, body + "".join(smoke) + lava + "".join(dashes) + "".join(erupt), d)


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
    w.flipbook("anim", "volcano", VOLCANO_FRAMES, volcano)
    w.flipbook("fx", "burst", 8, burst, size=(64, 64))
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
         ("strip", "flow", t * v * 0.024 + t * 0.25, 0.25, 0.85)]
    c.append(("sprite", "fx", f"burst_{fr(8, 10):02d}", 0.05, 3 - (t * v * 0.026) % 10, 0.3, 0, (0.9, 0.8, 0.7, 0.85), False))
    c.append(("sprite", "anim", f"volcano_{fr(8, 6):02d}", -1.3, 2.0 - (t * v * 0.036) % 12, 1.4, 0, (1, 1, 1, 1), False))
    c.append(("sprite", "anim", f"volcano_{(fr(8, 6) + 4) % 8:02d}", 1.5, -3.0 - (t * v * 0.036) % 12 + 6, 1.1, 0, (1, 1, 1, 1), True))
    c.append(("sprite", "fx", "haze", 0.0, 3 - (t * v * 0.12) % 14, 7.0, 0, (1, 1, 1, 0.35), False))
    c.append(("sprite", "fx", "cloud0", 0.4, 7 - (t * v * 0.3 + 3) % 20, 2.4, 0, (1, 1, 1, 0.35), False))
    for i in range(22):
        x = ((i * 0.618 + 0.15 * math.sin(t * 1.3 + i)) % 1) * 5 - 2.5
        y = (((i * 0.377) % 1) * 13 - t * v * 0.45 + t * 1.1) % 13 - 6.5
        c.append(("sprite", "fx", "dot", x, y, 0.06 + 0.03 * (i % 3), 0, (1, 0.55, 0.18, 0.75), False))
    return c
if __name__ == "__main__":
    build()
