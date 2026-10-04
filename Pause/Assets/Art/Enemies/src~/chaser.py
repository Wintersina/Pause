"""Chaser, one distinct hunter per world. It climbs up from below the board
and hunts the player, so it faces UP. Idle (drifting): hover, eye pulse,
sensors blinking and antennae / mandibles / feelers twitching on their own
beat. Tell (while it is chasing): a crouch (anticipation), then the lunge --
stretched, weapon out, thruster or wings flaring, speed lines streaming
behind.

  space    Steel Hound   steel interceptor with hinged jaws
  frost    Frost Lancer  a needle of ice whose lance shoots out
  verdant  Dragonsting   a four-winged dragonfly with a long barbed tail
  ember    Cinder Fang   a wide salamander head whose molten jaws gape

Detail pass: every form is cut with one hard angular shadow plane (80s mecha
shading) on top of its cel crescent, carries panel seams / chitin plates,
rivets, grilles, BONE specular kicks, and small hard-bloom accent lights."""
from common import *

# (jaw open deg, sx, sy, dy, eye, thrust length)
FRAMES = [
    (8, 1.00, 1.00, 0, 0.6, 10),     # 0 key
    (4, 1.00, 1.00, 2, 0.5, 8),      # 1 bob
    (10, 1.00, 1.00, 1, 1.0, 14),    # 2 eye pulse
    (6, 1.00, 1.00, -1, 0.6, 10),    # 3 settle
    (-4, 1.08, 0.90, 4, 0.2, 4),     # 4 tell: crouch (anticipation)
    (26, 0.92, 1.10, -4, 1.4, 30),   # 5 tell: LUNGE
]
IDLE_TICKS = [6, 3, 2, 3]
TELL_TICKS = [2, 3]

# per-frame twitch (deg) for antennae / feelers / mandibles, and a sensor
# blink pattern that runs against the eye pulse
TWITCH = (0, 7, -5, 3, -10, 14)
BLINK = (0.6, 1.0, 0.2, 0.6, 0.2, 1.4)


def form(p, pts, base, sh, hi=None, cut=None, sh_off=(6, 6), hi_off=(3, 3), ink_w=4, detail=False):
    """cel() plus ONE hard angular shadow plane (cut, same shadow tone), with
    the contour inked last so the plane never eats into it."""
    cid = cel(p, pts, base, sh, hi, sh_off=sh_off, hi_off=hi_off, ink_w=0, detail=detail)
    s = plane_svg(cid, cut, sh) if cut else ""
    if detail:
        p.detail += s + inkpoly(pts, ink_w)
    else:
        p.shadow += s
        p.ink += inkpoly(pts, ink_w)
    return cid


def chevrons(p, T, cx, cy, n, w, h, col, sh, step=None):
    """Stacked chevron plates (chitin / armour bands), each with its one
    shadow half and an INK rim -- reads cleaner at size than a scale grid."""
    step = step or h * 1.1
    for k in range(n):
        y = cy + k * step
        b = [(cx - w / 2, y), (cx, y - h * 0.5), (cx + w / 2, y), (cx + w / 2, y + h * 0.55), (cx, y + h * 0.05), (cx - w / 2, y + h * 0.55)]
        p.detail += poly(T(b), col) + poly(T([b[1], b[2], b[3], b[4]]), sh) + inkpoly(T(b), 1.3)


def eye(p, T, sock, centre, ev, light):
    p.detail += poly(T(sock), INK)
    e = lerp_pts(T(sock), [T([centre])[0]] * len(sock), 0.28)
    if ev > 0.4:   # hard flat bloom
        p.glow += poly(lerp_pts(T(sock), [T([centre])[0]] * len(sock), -0.25), light, f'opacity="{0.2 + 0.12 * min(ev, 1.5):.2f}"')
    p.glow += poly(e, light if ev > 0.4 else DIM[light])
    c = T([centre])[0]
    if ev > 0.4:
        p.glow += poly(ngon(c[0], c[1], 1.6 + ev, 4, 45), BONE)
    if ev > 0.9:
        p.glow += poly(star(c[0], c[1], 9 + 4 * (ev - 1), 1.4, 4, 0), light, 'opacity="0.55"')


def hound(i):
    """Space: steel interceptor, hinged bruise jaws, magenta eye and thruster,
    sensor antennae, shoulder vents, unit glyphs and hazard stripes."""
    ang, sx, sy, dy, ev, tl = FRAMES[i]
    tw, bl = TWITCH[i], BLINK[i]
    p = Parts()
    T = lambda q: xf(q, 64, 64, sx, sy, 0, dy)
    fl = [(56, 100), (72, 100), (68, 100 + tl * 0.6), (64, 104 + tl), (60, 100 + tl * 0.6)]
    p.glow += poly(T(lerp_pts(fl, [(64, 102)] * 5, -0.35)), MAGENTA, 'opacity="0.3"')
    p.glow += poly(T(fl), MAGENTA) + poly(T(lerp_pts(fl, [(64, 102)] * 5, 0.5)), BONE)
    for side in (-1, 1):   # twin manoeuvre jets
        j = [(64 + side * 30, 92), (64 + side * 36, 94), (64 + side * 33, 98 + tl * 0.35)]
        p.glow += poly(T(j), MAGENTA if ev > 0.4 else MAGENTA_SH)
    if i == 5:
        p.glow += speed_lines([40, 50, 78, 88], 96, 26, MAGENTA, 0.75)
    for side in (-1, 1):
        fin = [(64 + side * 18, 60), (64 + side * 46, 86), (64 + side * 40, 98), (64 + side * 20, 90)]
        form(p, T(fin), BRUISE, BRUISE_SH, BRUISE_HI,
             cut=T([(64 + side * 30, 74), (64 + side * 50, 90), (64 + side * 40, 100), (64 + side * 20, 92)]), sh_off=(3, 3), ink_w=3.2)
        seams(p, [T([(64 + side * 24, 66), (64 + side * 30, 90)])], 1.3, layer="ink")
        stripes(p, *T([(64 + side * 37, 86)])[0], 8, 4, n=2, rot=side * 41, layer="ink")
        rivets(p, T([(64 + side * 22, 64), (64 + side * 26, 86)]), 1.3, layer="ink")
    for side in (-1, 1):
        hx = 64 + side * 10
        prong = xf([(hx, 46), (hx + side * 2, 18), (hx - side * 4, 4), (hx - side * 8, 22), (hx - side * 6, 42)], hx, 46, rot=side * ang)
        cut = xf([(hx, 46), (hx + side * 2, 18), (hx - side * 4, 4), (hx - side * 2, 30)], hx, 46, rot=side * ang)
        form(p, T(prong), BRUISE, BRUISE_SH, BRUISE_HI, cut=T(cut), sh_off=(3, 3), ink_w=3.2)
        for ty in (26, 34):
            tooth = xf([(hx - side * 7, ty), (hx - side * 12, ty + 4), (hx - side * 6, ty + 7)], hx, 46, rot=side * ang)
            p.ink += poly(T(tooth), BONE) + inkpoly(T(tooth), 1.3)
        p.ink += line(T(xf([(hx - side * 1, 14), (hx - side * 3, 38)], hx, 46, rot=side * ang)), 1.8, BRUISE_HI)
    # sensor antennae off the shoulders (twitching)
    for side in (-1, 1):
        antenna(p, *T([(64 + side * 14, 40)])[0], 20, side * (28 + tw * 0.8), bl, MAGENTA, tip=2, layer="base")
    hull = [(64, 30), (78, 40), (84, 66), (76, 98), (64, 104), (52, 98), (44, 66), (50, 40)]
    form(p, T(hull), STEEL, STEEL_SH, STEEL_HI, cut=T([(64, 30), (78, 40), (84, 66), (76, 98), (64, 104), (66, 70)]),
         sh_off=(8, 6), detail=True)
    # panel breaks, vents, rivets, markings
    seams(p, [T([(66, 70), (64, 104)]), T([(46, 72), (82, 72)]), T([(50, 88), (78, 88)]), T([(52, 42), (76, 42)])], 1.6)
    grille(p, *T([(64, 80)])[0], 12, 9, n=3, rot=0)
    grille(p, *T([(56, 95)])[0], 6, 5, n=2)
    grille(p, *T([(72, 95)])[0], 6, 5, n=2)
    rivets(p, T([(50, 76), (78, 76), (50, 84), (78, 84), (56, 44), (72, 44)]), 1.3)
    glyph(p, *T([(52, 63)])[0], 2.2, seed=2, w=1.1)
    glyph(p, *T([(76, 63)])[0], 2.2, seed=4, color=STEEL_HI, w=1.1)
    spec(p, T([(51, 41), (45, 64), (47, 80)]), 1.6)
    spec(p, T([(63, 32), (52, 39)]), 1.4)
    eye(p, T, [(52, 52), (64, 44), (76, 52), (72, 64), (56, 64)], (64, 55), ev, MAGENTA)
    p.glow += poly(T([(62.5, 49), (65.5, 49), (65, 61), (63, 61)]), INK)
    hexlight(p, *T([(56, 46)])[0], 1.8, bl, MAGENTA, rot=0)
    hexlight(p, *T([(72, 46)])[0], 1.8, 1.4 - bl if bl < 1.4 else 0.2, MAGENTA, rot=0)
    return p


def lancer(i):
    """Frost: a needle of ice. On the lunge the lance shoots out of its nose
    and the fins fold back. Faceted crystal plates, steel collar with bolts,
    cyan running lights down the fins."""
    ang, sx, sy, dy, ev, tl = FRAMES[i]
    tw, bl = TWITCH[i], BLINK[i]
    p = Parts()
    T = lambda q: xf(q, 64, 64, sx, sy, 0, dy + 6)
    ext = max(0, ang - 8) * 0.7
    fl = [(59, 104), (69, 104), (66, 104 + tl * 0.5), (64, 108 + tl * 0.8), (62, 104 + tl * 0.5)]
    p.glow += poly(T(lerp_pts(fl, [(64, 106)] * 5, -0.4)), CYAN, 'opacity="0.3"')
    p.glow += poly(T(fl), CYAN) + poly(T(lerp_pts(fl, [(64, 106)] * 5, 0.5)), BONE)
    if i == 5:
        p.glow += speed_lines([46, 56, 72, 82], 92, 24, CYAN, 0.75)
    fold = 1 - min(1, max(0, ang - 8) / 24)   # fins sweep back on the lunge
    for side in (-1, 1):
        fx, fy = 64 + side * (16 + 18 * fold), 92
        fin = [(64 + side * 6, 70), (fx, fy), (64 + side * (12 + 10 * fold), 106), (64 + side * 6, 98)]
        form(p, T(fin), ICE_HI, ICE, None, cut=T([(64 + side * 6, 84), (fx, fy), (64 + side * (12 + 10 * fold), 106), (64 + side * 6, 98)]),
             sh_off=(2 * side, 2), ink_w=2.8)
        seams(p, [T([(64 + side * 8, 76), (64 + side * (12 + 12 * fold), 100)])], 1.2, layer="ink")
        hexlight(p, *T([(64 + side * (11 + 8 * fold), 90)])[0], 1.8, bl if side < 0 else (0.2 if bl > 0.9 else 1.0), CYAN, rot=0)
        wing = [(64 + side * 8, 52), (64 + side * (22 + 10 * fold), 60), (64 + side * 9, 72)]
        form(p, T(wing), STEEL, STEEL_SH, STEEL_HI, cut=T([(64 + side * 12, 64), (64 + side * (22 + 10 * fold), 60), (64 + side * 9, 72)]),
             sh_off=(2 * side, 2), ink_w=2.8)
        rivets(p, T([(64 + side * (14 + 4 * fold), 59)]), 1.2, layer="ink")
        # frost feelers off the collar, twitching
        antenna(p, *T([(64 + side * 7, 34)])[0], 14, side * (40 + tw), bl, CYAN, tip=1.6, layer="base")
    lance = [(60, 24), (68, 24), (64, 2 - ext)]
    form(p, T(lance), BONE, ICE_HI, None, cut=T([(64, 24), (68, 24), (64, 2 - ext)]), sh_off=(2, 0), ink_w=2.8)
    p.ink += line(T([(64, 22), (64, 8 - ext)]), 1.1, ICE)
    body = [(64, 14), (72, 30), (74, 70), (70, 100), (64, 106), (58, 100), (54, 70), (56, 30)]
    form(p, T(body), ICE, ICE_SH, ICE_HI, cut=T([(64, 14), (72, 30), (74, 70), (70, 100), (64, 106), (65, 60)]),
         sh_off=(5, 4), detail=True)
    for y in (40, 84, 94):
        seams(p, [T([(57, y), (64, y + 4), (71, y)])], 1.5)
    seams(p, [T([(65, 60), (64, 104)])], 1.3)
    # steel collar band with bolts
    collar = [(55, 68), (73, 68), (74, 76), (54, 76)]
    form(p, T(collar), STEEL, STEEL_SH, STEEL_HI, cut=T([(64, 68), (73, 68), (74, 76), (64, 76)]), sh_off=(0, 2), ink_w=2.2, detail=True)
    rivets(p, T([(58, 72), (64, 72), (70, 72)]), 1.2)
    slitlight(p, *T([(64, 88)])[0], 6, 1.6, bl, CYAN)
    # nose facets and a bolted nose ring
    for q in ([(64, 16), (59, 30), (64, 34)], [(57, 72), (60, 82), (56, 92)]):
        p.detail += poly(T(q), ICE_HI) + inkpoly(T(q), 1.1)
    seams(p, [T([(57, 34), (71, 34)])], 1.4)
    rivets(p, T([(60, 37), (68, 37)]), 1.1)
    for side in (-1, 1):   # crystal shards on the fins
        sh_ = [(64 + side * (9 + 6 * fold), 98), (64 + side * (14 + 9 * fold), 94), (64 + side * (12 + 8 * fold), 102)]
        p.ink += poly(T(sh_), BONE) + inkpoly(T(sh_), 1.1)
        rivets(p, T([(64 + side * 9, 78)]), 1.1, layer="ink")
    spec(p, T([(56, 32), (55, 66)]), 1.4)
    spec(p, T([(57, 74), (58, 98)]), 1.2, ICE_HI)
    glyph(p, *T([(60, 80)])[0], 1.4, seed=1, color=BONE, w=0.9)
    eye(p, T, [(58, 52), (70, 52), (68, 64), (60, 64)], (64, 58), ev, MAGENTA)
    p.glow += line(T([(64, 54), (64, 62)]), 1.2, INK)
    if i == 5:
        p.glow += spark(*T([(64, 2 - ext)])[0], 6, BONE, 0)
    return p


def dragonsting(i):
    """Verdant: a dragonfly. Four long veined wings that blur on the lunge,
    faceted compound eyes, twitching feelers and legs, mandibles forward,
    chitin plates and a long barbed tail behind."""
    ang, sx, sy, dy, ev, tl = FRAMES[i]
    tw, bl = TWITCH[i], BLINK[i]
    p = Parts()
    T = lambda q: xf(q, 64, 64, sx, sy, 0, dy)
    beat = (0, 9, -7, 4, -14, 18)[i]
    for k, (y, L) in enumerate(((44, 52), (56, 46))):
        for side in (-1, 1):
            a = side * (90 - (14 if k == 0 else -12)) + side * beat * (1 if k == 0 else -1)
            base_ = (64 + side * 6, y)
            bx, by = base_
            w = xf([base_, (bx - 6, by - L * 0.45), (bx, by - L), (bx + 6, by - L * 0.45)], bx, by, rot=a)
            cut = xf([base_, (bx, by - L), (bx + 6, by - L * 0.45)], bx, by, rot=a)
            form(p, T(w), BILE_HI, MOSS, None, cut=T(cut), sh_off=(0, 3), ink_w=2.4)
            p.ink += line(T([w[0], w[2]]), 1.2)
            for t in (0.35, 0.6, 0.82):   # cross veins
                c = xf([(bx - 4.5 * (1 - abs(t - 0.45)), by - L * t), (bx + 4.5 * (1 - abs(t - 0.45)), by - L * t)], bx, by, rot=a)
                p.ink += line(T(c), 0.9)
            tip = xf([(bx, by - L * 0.86)], bx, by, rot=a)[0]   # pterostigma
            p.ink += poly(T(ngon(tip[0], tip[1], 2.2, 4, a + 45)), BRUISE) + inkpoly(T(ngon(tip[0], tip[1], 2.2, 4, a + 45)), 0.9)
    # legs folded under the thorax (twitch)
    for side in (-1, 1):
        for k, (y0, ex, ey) in enumerate(((44, 14, 40), (52, 16, 54), (60, 12, 68))):
            j = (64 + side * 9, y0)
            knee = (64 + side * (ex + 2), y0 + (ey - y0) * 0.4 - 4 + (tw * 0.2 if k == 1 else 0))
            foot = (64 + side * (ex - 2), ey + (tw * 0.3 * side if k == 2 else 0))
            p.base += line(T([j, knee, foot]), 3.4) + line(T([j, knee, foot]), 1.4, BRUISE_HI)
    tail = [(60, 66), (68, 66), (67, 108), (64, 118 + tl * 0.2), (61, 108)]
    form(p, T(tail), BRUISE, BRUISE_SH, BRUISE_HI, cut=T([(64, 66), (68, 66), (67, 108), (64, 118 + tl * 0.2)]), sh_off=(3, 0), ink_w=2.8)
    for y in (74, 82, 90, 98, 106):
        p.ink += line(T([(60.5, y), (67.5, y)]), 1.5)
        p.ink += poly(T(ngon(64, y + 3.5, 1.1, 4, 45)), BILE_LIGHT if bl > 0.4 else BILE_SH)
    barb = [(60, 112), (68, 112), (64, 124)]
    p.base += poly(T(barb), MAGENTA)
    p.shadow += poly(T([(64, 112), (68, 112), (64, 124)]), MAGENTA_SH)
    p.ink += inkpoly(T(barb), 1.8)
    for side in (-1, 1):
        sp = [(64 + side * 3, 110), (64 + side * 8, 106), (64 + side * 4, 114)]
        p.ink += poly(T(sp), BONE) + inkpoly(T(sp), 1)
    thorax = [(64, 34), (74, 40), (74, 62), (64, 68), (54, 62), (54, 40)]
    form(p, T(thorax), BRUISE, BRUISE_SH, BRUISE_HI, cut=T([(64, 34), (74, 40), (74, 62), (64, 68)]), sh_off=(4, 4), detail=True)
    chevrons(p, T, 64, 44, 2, 16, 6, BRUISE_HI, BRUISE_SH)
    hexlight(p, *T([(64, 60)])[0], 2.4, ev, BILE_LIGHT, rot=0)
    spec(p, T([(55, 42), (55, 58)]), 1.3)
    head = [(64, 16), (76, 24), (76, 36), (64, 40), (52, 36), (52, 24)]
    form(p, T(head), BILE, BILE_SH, BILE_HI, cut=T([(64, 16), (76, 24), (76, 36), (64, 40)]), sh_off=(4, 4), detail=True)
    for side in (-1, 1):
        # feelers
        antenna(p, *T([(64 + side * 4, 18)])[0], 14, side * (24 + tw), bl, BILE_LIGHT, tip=1.5)
        m = xf([(64 + side * 4, 18), (64 + side * 9, 6), (64 + side * 2, 12)], 64 + side * 4, 18, rot=-side * (ang - 8 + tw * 0.3))
        p.detail += poly(T(m), BONE) + poly(T([m[0], m[1], (m[0][0] * 0.5 + m[1][0] * 0.5 - side, m[0][1] * 0.5 + m[1][1] * 0.5)]), FLASH_SH) + inkpoly(T(m), 1.4)
        e = [(64 + side * 3, 22), (64 + side * 12, 24), (64 + side * 11, 34), (64 + side * 4, 34)]
        eye(p, T, e, (64 + side * 7.5, 28), ev, BILE_LIGHT)
        for fx, fy in ((64 + side * 6, 26), (64 + side * 9, 30), (64 + side * 6, 31)):   # compound facets
            p.glow += poly(T(ngon(fx, fy, 0.9, 6, 0)), INK if ev > 0.4 else BILE_LIGHT)
    spec(p, T([(53, 25), (62, 18)]), 1.3)
    if i == 5:
        p.glow += speed_lines([44, 54, 74, 84], 92, 26, BILE_LIGHT, 0.75)
    return p


def cinder_fang(i):
    """Ember: a wide salamander head under basalt scale plates. Its upper and
    lower jaws hinge apart on the lunge to show a molten throat; lava pores
    pulse along its cheeks, spined frills twitch, a flame trails behind."""
    ang, sx, sy, dy, ev, tl = FRAMES[i]
    tw, bl = TWITCH[i], BLINK[i]
    p = Parts()
    T = lambda q: xf(q, 64, 64, sx, sy, 0, dy + 5)
    gape = max(0, ang) * 0.35
    hot = AMBER if ev > 0.9 else SODIUM
    fl = [(54, 96), (74, 96), (70, 100 + tl * 0.5), (64, 104 + tl * 0.9), (58, 100 + tl * 0.5)]
    p.glow += poly(T(lerp_pts(fl, [(64, 100)] * 5, -0.35)), SODIUM, 'opacity="0.3"')
    p.glow += poly(T(fl), SODIUM) + poly(T(lerp_pts(fl, [(64, 100)] * 5, 0.5)), AMBER)
    if i == 5:
        p.glow += speed_lines([36, 48, 80, 92], 94, 24, SODIUM, 0.75)
    # back frills, spined, flicking on the twitch beat
    for side in (-1, 1):
        fr = xf([(64 + side * 30, 60), (64 + side * 54, 70), (64 + side * 44, 80), (64 + side * 50, 92), (64 + side * 28, 84)],
                64 + side * 30, 72, rot=side * tw * 0.4)
        form(p, T(fr), GUN, GUN_SH, GUN_HI, cut=T([fr[0], fr[2], fr[3], fr[4]]), sh_off=(3, 3), ink_w=2.8)
        p.ink += line(T([fr[0], fr[2]]), 1.2) + line(T([fr[4], fr[2]]), 1.2)
        teeth(p, T([fr[0]])[0], T([fr[1]])[0], 3, 4, color=CHAR_HI, inward=side, layer="ink")
    # molten throat between the jaws
    throat = [(40, 40 - gape * 0.4), (88, 40 - gape * 0.4), (84, 52), (44, 52)]
    p.ink += poly(T(throat), INK)
    p.glow += poly(T(lerp_pts(throat, [(64, 46)] * 4, 0.2)), hot)
    p.glow += poly(T(lerp_pts(throat, [(64, 47)] * 4, 0.6)), AMBER if ev > 0.4 else SODIUM)
    if ev > 0.9:
        p.glow += poly(T(lerp_pts(throat, [(64, 46)] * 4, -0.15)), SODIUM, 'opacity="0.35"')
    # upper jaw lifts with the gape, lower jaw is the head
    up = [(26, 42 - gape), (40, 18 - gape), (64, 10 - gape), (88, 18 - gape), (102, 42 - gape), (84, 38 - gape * 0.5), (64, 34 - gape * 0.5), (44, 38 - gape * 0.5)]
    form(p, T(up), CHAR, CHAR_SH, CHAR_HI, cut=T([(64, 10 - gape), (88, 18 - gape), (102, 42 - gape), (84, 38 - gape * 0.5), (64, 34 - gape * 0.5), (70, 22 - gape)]),
         sh_off=(8, 5), ink_w=3.4)
    # brow plates and nostril slits on the upper jaw
    seams(p, [T([(40, 26 - gape), (52, 20 - gape), (64, 22 - gape), (76, 20 - gape), (88, 26 - gape)])], 1.5, layer="ink")
    for side in (-1, 1):
        n_ = [(64 + side * 5, 16 - gape), (64 + side * 10, 15 - gape), (64 + side * 9, 18 - gape)]
        p.ink += poly(T(n_), INK)
        rivets(p, T([(64 + side * 22, 30 - gape * 0.8), (64 + side * 32, 34 - gape * 0.9)]), 1.3, color=CHAR_HI, layer="ink")
    spec(p, T([(30, 40 - gape), (41, 20 - gape), (60, 12 - gape)]), 1.6, layer="ink")
    for k in range(5):
        x = 44 + k * 10
        t = [(x - 3, 36 - gape * 0.5), (x + 3, 36 - gape * 0.5), (x, 44 - gape * 0.5)]
        p.detail += poly(T(t), BONE) + inkpoly(T(t), 1.2)
    head = [(24, 46), (104, 46), (98, 72), (80, 96), (64, 100), (48, 96), (30, 72)]
    form(p, T(head), CHAR, CHAR_SH, CHAR_HI, cut=T([(64, 46), (104, 46), (98, 72), (80, 96), (64, 100), (70, 74)]),
         sh_off=(10, 8), detail=True)
    for k in range(4):   # lower teeth pointing up into the gape
        x = 49 + k * 10
        t = [(x - 3, 50), (x + 3, 50), (x, 43)]
        p.detail += poly(T(t), BONE) + inkpoly(T(t), 1.2)
    for k, (x, y) in enumerate(((54, 72), (64, 68), (74, 72))):   # basalt boss plates on the snout
        hx = ngon(x, y, 5.2, 6, 0, 1, 0.8)
        p.detail += poly(T(hx), CHAR_HI if k < 2 else CHAR) + poly(T([hx[0], hx[1], hx[2], hx[3]]), CHAR_SH) + inkpoly(T(hx), 1.4)
    seams(p, [T([(30, 72), (44, 76), (52, 92)]), T([(98, 72), (84, 76), (76, 92)]), T([(70, 74), (64, 100)])], 1.5)
    for k, (x, y) in enumerate(((36, 70), (92, 70), (42, 82), (86, 82))):   # lava pores
        hexlight(p, *T([(x, y)])[0], 2, (bl if k % 2 == 0 else ev), SODIUM, rot=0)
    spec(p, T([(27, 49), (32, 70), (46, 92)]), 1.5)
    for side in (-1, 1):
        e = [(64 + side * 16, 58), (64 + side * 32, 56), (64 + side * 30, 66), (64 + side * 18, 66)]
        eye(p, T, e, (64 + side * 24, 61), ev, MAGENTA)
        p.glow += poly(T([(64 + side * 23.2, 57.5), (64 + side * 24.8, 57.5), (64 + side * 24.6, 64.5), (64 + side * 23.4, 64.5)]), INK)
        p.detail += line(T([(64 + side * 14, 54), (64 + side * 34, 52)]), 2.4)   # brow ridge
    p.detail += line(T([(50, 80), (64, 88), (78, 80)]), 4.5)
    p.glow += line(T([(50, 80), (64, 88), (78, 80)]), 2, hot)
    return p


def draw(world, i):
    return {"space": hound, "frost": lancer, "verdant": dragonsting, "ember": cinder_fang}[world](i)
