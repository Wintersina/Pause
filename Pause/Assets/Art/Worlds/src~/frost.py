"""Frost, seen from atmosphere level: a polar ice plain far below with
distant hazed massifs and ice towers, a glacier-fed river valley, small
far-off landmarks (valley glaciers, ice massifs, geysers), an aurora at the
ship's altitude, and cloud cels / haze bands / snow passing close by."""
import math
import random

from bgkit import (TAU, World, doc, blur, lin, wrap_y, PNoise, edge_range, peak, pts, ink_attr, grain_defs,
                   grain, jitter_ridge)
from palette import FROST as P, BONE
from altitude import hazed, cloud_cel, haze_band, scatter_peaks
import space

W, H = 512, 1024


def sky():
    """Deepest layer: the polar ice plain far below. Tiny crevasse lines,
    pale pressure patches and a few snow glints; heavy aerial haze."""
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
    patches = []
    for i in range(70):
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        r = rnd.uniform(4, 12)
        nv = rnd.randint(4, 6)
        a0 = rnd.uniform(0, TAU)
        k = [(x + r * math.cos(a0 + j * TAU / nv) * rnd.uniform(0.6, 1.2),
              y + r * 0.7 * math.sin(a0 + j * TAU / nv) * rnd.uniform(0.6, 1.2)) for j in range(nv)]
        patches.append((y, lambda dy, k=k: f'<polygon points="{pts([(px, py + dy) for px, py in k])}" '
                        f'fill="{P["snowfield_hi"]}" opacity="0.7"/>'))
    for i in range(60):
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        seg = jitter_ridge(rnd, x, y, x + rnd.uniform(-30, 30), y + rnd.uniform(10, 30), 3, 3)
        patches.append((y, lambda dy, seg=seg: f'<polyline transform="translate(0 {dy})" points="{pts(seg)}" '
                        f'stroke="{P["vignette"]}" stroke-width="1" fill="none" opacity="0.7"/>'))
    for i in range(120):
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        patches.append((y, lambda dy, x=x, y=y, a=rnd.uniform(0.2, 0.5): f'<rect x="{x:.1f}" y="{y + dy:.1f}" '
                        f'width="1.2" height="1.2" fill="{P["stars"][1]}" opacity="{a:.2f}"/>'))
    body.append(wrap_y(patches, H))
    body.append(f'<rect width="{W}" height="{H}" fill="url(#vig)"/>')
    body.append(grain(W, H))
    return doc(W, H, "".join(body), defs)


def far():
    """Distant massifs and a few tiny ice towers, small and heavily hazed."""
    rnd = random.Random(31)
    d, b = scatter_peaks(rnd, W, H, P["far"], 26, 26, 54, cap=0.38, ink=1.2)
    towers = []
    for i in range(10):
        x, y = rnd.uniform(20, W - 20), rnd.uniform(0, H)
        n = rnd.randint(2, 4)
        for j in range(n):
            tw = rnd.uniform(5, 9)
            th = rnd.uniform(14, 34)
            tx = x + j * (tw + 2)
            towers.append((y, lambda dy, tx=tx, y=y, tw=tw, th=th, lit=rnd.random() < 0.6:
                           f'<polygon points="{tx:.1f},{y + dy:.1f} {tx:.1f},{y + dy - th:.1f} {tx + tw:.1f},{y + dy - th - 2:.1f} '
                           f'{tx + tw:.1f},{y + dy:.1f}" fill="{P["tower"]}" {ink_attr(1)}/>'
                           + (f'<rect x="{tx + 2:.1f}" y="{y + dy - th * 0.6:.1f}" width="1.6" height="2" fill="{P["window"]}"/>'
                              if lit else "")))
    hd, hb = hazed(b + wrap_y(towers, H), P["air"], 0.45)
    return doc(W, H, hb, d + hd)


def river_geom():
    rnd = random.Random(41)
    n1, n2 = PNoise(rnd), PNoise(rnd)
    return (lambda y: W / 2 + 9 * n1(y / H)), (lambda y: 22 + 4 * n2(y / H))


def mid():
    """The valley far below: a glacier-fed river between snowfields, small
    peaks along both edges. Moderate haze."""
    rnd = random.Random(51)
    cxf, hwf = river_geom()
    ys = [H * i / 48 for i in range(49)]
    body = []
    bl = [(cxf(y) - hwf(y) - 34 - 10 * math.sin(y / H * TAU * 3), y) for y in ys]
    br = [(cxf(y) + hwf(y) + 34 + 10 * math.sin(y / H * TAU * 2 + 1), y) for y in ys]
    body.append(f'<polygon points="{pts(bl + list(reversed(br)))}" fill="{P["snowfield"]}"/>')
    for side in (bl, br):
        body.append(f'<polyline points="{pts(side)}" fill="none" {ink_attr(1.6)}/>')
    left = [(cxf(y) - hwf(y), y) for y in ys]
    right = [(cxf(y) + hwf(y), y) for y in ys]
    body.append(f'<polygon points="{pts(left + list(reversed(right)))}" fill="{P["ice"]}"/>')
    sh = [(cxf(y) + hwf(y) * 0.4, y) for y in ys]
    body.append(f'<polygon points="{pts(sh + list(reversed(right)))}" fill="{P["ice_shadow"]}"/>')
    plates = []
    for i in range(40):
        y = rnd.uniform(0, H)
        x = cxf(y) + rnd.uniform(-0.6, 0.6) * hwf(y)
        r = rnd.uniform(2.5, 5)
        k = [(x - r, y), (x, y - r * 1.3), (x + r, y), (x, y + r * 1.3)]
        plates.append((y, lambda dy, k=k: f'<polygon points="{pts([(px, py + dy) for px, py in k])}" '
                       f'fill="{P["ice_plate"]}" {ink_attr(0.8)}/>'))
    body.append(wrap_y(plates, H))
    for side in (left, right):
        body.append(f'<polyline points="{pts(side)}" fill="none" {ink_attr(1.8)}/>')
    pines = []
    for i in range(140):
        y = rnd.uniform(0, H)
        sd = rnd.choice((-1, 1))
        x = cxf(y) + sd * (hwf(y) + rnd.uniform(8, 36))
        s = rnd.uniform(2, 3.5)
        pines.append((y, lambda dy, x=x, y=y, s=s: f'<polygon points="{x:.1f},{y + dy - s * 2:.1f} '
                      f'{x - s * 0.6:.1f},{y + dy:.1f} {x + s * 0.6:.1f},{y + dy:.1f}" fill="{P["pine"]}"/>'))
    body.append(wrap_y(pines, H))
    d, b = edge_range(rnd, W, H, P["near"], 12, 50, 84, 40, 70, 100, cap=0.4, rim=P["rim"], ink=1.4,
                      foot=P["near"]["dark"])
    hd, hb = hazed("".join(body) + b, P["air"], 0.25)
    return doc(W, H, hb, d + hd)


def flow():
    """Meltwater dashes on the river core (128 px strip; river half-width
    ~22 px, meander +-9, so dashes stay within +-9 of the centre)."""
    w = 128
    rnd = random.Random(61)
    items = []
    for i in range(36):
        x = rnd.uniform(56, 72)
        y = rnd.uniform(0, H)
        L = rnd.uniform(6, 16)
        items.append((y, lambda dy, x=x, y=y, L=L:
                      f'<rect x="{x - 1:.1f}" y="{y + dy - L / 2:.1f}" width="2" height="{L:.1f}" '
                      f'fill="{P["water_dash"]}" opacity="0.7"/>'))
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


def glacier(phase):
    """A valley glacier seen from altitude: an ice tongue flowing down
    between two inked rock ridges, chevron crevasses, a dark medial
    moraine, an ice-cliff snout and a meltwater lake whose dashes flow
    (4 frames). One shadow + one highlight tone per form; hazed."""
    w, h = 256, 256
    lr = [(0, 14), (58, 0), (90, 40), (80, 110), (96, 176), (74, 214), (40, 256), (0, 256)]
    rr = [(256, 24), (196, 0), (166, 50), (178, 120), (156, 182), (184, 220), (214, 256), (256, 256)]
    tl = [(76, 0), (96, 46), (86, 112), (100, 176), (112, 200)]
    tr = [(184, 0), (164, 52), (176, 120), (154, 180), (144, 200)]
    tongue = tl + list(reversed(tr))
    g = []
    # rock ridges: base, one shadow plane, snow kick on the crest
    g.append(f'<polygon points="{pts(lr)}" fill="{P["big"]["lit"]}"/>')
    g.append(f'<polygon points="{pts([(58, 0), (90, 40), (80, 110), (96, 176), (74, 214), (60, 214), (66, 120), (70, 40)])}" '
             f'fill="{P["big"]["dark"]}"/>')
    g.append(f'<polygon points="{pts([(10, 14), (58, 2), (70, 22), (40, 26)])}" fill="{P["big"]["cap"]}"/>')
    g.append(f'<polygon points="{pts(rr)}" fill="{P["big"]["dark"]}"/>')
    g.append(f'<polygon points="{pts([(196, 0), (166, 50), (178, 120), (196, 118), (186, 52), (214, 6)])}" fill="{P["big"]["lit"]}"/>')
    g.append(f'<polygon points="{pts([(196, 2), (240, 22), (220, 30), (188, 22)])}" fill="{P["big"]["cap_dark"]}"/>')
    # ice tongue
    g.append(f'<polygon points="{pts(tongue)}" fill="{P["big"]["cap"]}"/>')
    g.append(f'<polygon points="{pts([(150, 0), (184, 0), (164, 52), (176, 120), (154, 180), (144, 200), (132, 200), (150, 120), (140, 50)])}" '
             f'fill="{P["big"]["cap_dark"]}"/>')
    g.append(f'<polygon points="{pts([(80, 0), (96, 0), (106, 46), (96, 112), (106, 170), (100, 176), (86, 112), (96, 46)])}" '
             f'fill="{P["ice_hi"]}"/>')
    # crevasses: chevrons pointing downstream
    for y in (26, 54, 82, 112, 140, 164):
        cx = 130 + 4 * math.sin(y * 0.07)
        half = 30 - y * 0.08
        g.append(f'<polyline points="{cx - half:.0f},{y - 6} {cx:.0f},{y + 4} {cx + half:.0f},{y - 6}" fill="none" '
                 f'{ink_attr(1.8)}/>')
    g.append(f'<polyline points="130,0 126,60 134,120 128,190" fill="none" stroke="{P["big"]["dark"]}" stroke-width="3" '
             f'stroke-dasharray="10 6"/>')
    # snout: ice cliff
    snout = [(100, 176), (112, 200), (120, 194), (128, 204), (136, 194), (144, 200), (154, 180)]
    g.append(f'<polygon points="{pts(snout + [(154, 210), (100, 210)])}" fill="{P["geyser_shadow"]}"/>')
    # meltwater lake
    lake = [(96, 208), (160, 206), (176, 230), (150, 248), (106, 246), (88, 228)]
    g.append(f'<polygon points="{pts(lake)}" fill="{P["lake"]}"/>')
    ink = (f'<polygon points="{pts(lr)}" fill="none" {ink_attr(3)}/>'
           f'<polygon points="{pts(rr)}" fill="none" {ink_attr(3)}/>'
           f'<polygon points="{pts(tongue)}" fill="none" {ink_attr(3)}/>'
           f'<polyline points="{pts(snout)}" fill="none" {ink_attr(2.5)}/>'
           f'<polygon points="{pts(lake)}" fill="none" {ink_attr(2.5)}/>')
    d, body = hazed("".join(g) + ink, P["air"], 0.3)
    k = int(phase * 4)
    lights = []
    for i, (x, y) in enumerate([(108, 222), (126, 230), (144, 220), (118, 238), (140, 236)]):
        if (i + k) % 2 == 0:
            lights.append(f'<rect x="{x}" y="{y - 1}" width="8" height="2" fill="{P["water_dash"]}" opacity="0.8"/>')
    return doc(w, h, body + "".join(lights), d)


def massif(seed):
    """A cluster of three ice peaks with one snow cap each, seen far off."""
    rnd = random.Random(seed)
    w, h = 256, 200
    ds, bs = [], []
    for cx, base, pw, ph in [(150, 190, 170, 150), (78, 196, 130, 110), (206, 198, 110, 90)]:
        d, b = peak(rnd, cx, base, pw, ph, P["big"], cap=0.42, rim=P["rim"], ink=3)
        ds.append(d)
        bs.append(b)
    hd, hb = hazed("".join(bs), P["air"], 0.3)
    return doc(w, h, hb, "".join(ds) + hd)


def build():
    w = World("Frost")
    w.tile("sky", sky())
    w.tile("far", far())
    w.tile("mid", mid())
    w.tile("flow", flow())
    w.flipbook("anim", "aurora", 8, aurora)
    w.flipbook("fx", "glacier", 4, glacier)
    w.sprite("fx", "massif0", massif(3))
    w.sprite("fx", "massif1", massif(17))
    w.flipbook("fx", "geyser", 8, geyser, size=(48, 96))
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
         ("strip", "flow", t * v * 0.024 + t * 0.35, 0.25, 1)]
    c.append(("sprite", "fx", f"geyser_{fr(8, 9):02d}", -1.0, 4 - (t * v * 0.026) % 10, 0.35, 0, (0.7, 0.88, 1, 0.8), False))
    c.append(("sprite", "fx", f"glacier_{fr(4, 4):02d}", 1.3, 2.5 - (t * v * 0.034) % 12, 1.5, 0, (1, 1, 1, 1), False))
    c.append(("sprite", "fx", "massif0", -1.4, -1.5 - (t * v * 0.034) % 12 + 6, 1.4, 0, (1, 1, 1, 1), False))
    c.append(("sprite", "anim", f"aurora_{fr(8, 8):02d}", 0.0, 6 - (t * v * 0.06 + 2) % 16, 6.2, -8, (0.75, 0.9, 0.9, 0.5), False))
    c.append(("sprite", "fx", "haze", 0.0, 6 - (t * v * 0.12) % 14, 7.0, 0, (1, 1, 1, 0.35), False))
    c.append(("sprite", "fx", "cloud0", 0.8, 7 - (t * v * 0.3 + 5) % 20, 2.4, 0, (1, 1, 1, 0.35), False))
    for i in range(26):
        x = ((i * 0.618 + 0.3 * math.sin(t * 0.8 + i)) % 1) * 5 - 2.5
        y = (((i * 0.377) % 1) * 13 - t * (0.6 + v * 0.5)) % 13 - 6.5
        c.append(("sprite", "fx", "dot", x, y, 0.07 + 0.04 * (i % 3), 0, (0.8, 0.9, 1, 0.5), False))
    return c
if __name__ == "__main__":
    build()
