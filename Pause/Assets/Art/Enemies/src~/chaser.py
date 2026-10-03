"""Chaser, one distinct hunter per world. It climbs up from below the board
and hunts the player, so it faces UP. Idle (drifting): hover, eye pulse. Tell
(while it is chasing): a crouch (anticipation), then the lunge -- stretched,
weapon out, thruster or wings flaring, speed lines streaming behind.

  space    Steel Hound   steel interceptor with hinged jaws
  frost    Frost Lancer  a needle of ice whose lance shoots out
  verdant  Dragonsting   a four-winged dragonfly with a long barbed tail
  ember    Cinder Fang   a wide salamander head whose molten jaws gape"""
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


def eye(p, T, sock, centre, ev, light):
    p.detail += poly(T(sock), INK)
    e = lerp_pts(T(sock), [T([centre])[0]] * len(sock), 0.28)
    p.glow += poly(e, light if ev > 0.4 else DIM[light])
    if ev > 0.9:
        p.glow_back += halo(T(sock), light, 0.8)


def hound(i):
    """Space: steel interceptor, hinged bruise jaws, magenta eye and thruster."""
    ang, sx, sy, dy, ev, tl = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 64, sx, sy, 0, dy)
    fl = [(56, 100), (72, 100), (68, 100 + tl * 0.6), (64, 104 + tl), (60, 100 + tl * 0.6)]
    p.glow_back += halo(T(fl), MAGENTA, 0.6)
    p.glow += poly(T(fl), MAGENTA) + poly(T(lerp_pts(fl, [(64, 102)] * 5, 0.5)), BONE)
    if i == 5:
        p.glow += speed_lines([40, 50, 78, 88], 96, 26, MAGENTA, 0.75)
    for side in (-1, 1):
        fin = [(64 + side * 18, 60), (64 + side * 46, 86), (64 + side * 40, 98), (64 + side * 20, 90)]
        cel(p, T(fin), BRUISE, BRUISE_SH, BRUISE_HI, sh_off=(3, 3), ink_w=3)
    for side in (-1, 1):
        hx = 64 + side * 10
        prong = xf([(hx, 46), (hx + side * 2, 18), (hx - side * 4, 4), (hx - side * 8, 22), (hx - side * 6, 42)], hx, 46, rot=side * ang)
        cel(p, T(prong), BRUISE, BRUISE_SH, BRUISE_HI, sh_off=(3, 3), ink_w=3)
        tooth = xf([(hx - side * 7, 26), (hx - side * 12, 30), (hx - side * 6, 34)], hx, 46, rot=side * ang)
        p.detail += poly(T(tooth), BONE) + inkpoly(T(tooth), 1.4)
    hull = [(64, 30), (78, 40), (84, 66), (76, 98), (64, 104), (52, 98), (44, 66), (50, 40)]
    cel(p, T(hull), STEEL, STEEL_SH, STEEL_HI, sh_off=(8, 6), detail=True)
    p.detail += line(T([(64, 74), (64, 98)]), 2)
    eye(p, T, [(52, 52), (64, 44), (76, 52), (72, 64), (56, 64)], (64, 55), ev, MAGENTA)
    p.glow += poly(T([(62.5, 49), (65.5, 49), (65, 61), (63, 61)]), INK)
    return p


def lancer(i):
    """Frost: a needle of ice. On the lunge the lance shoots out of its nose
    and the fins fold back."""
    ang, sx, sy, dy, ev, tl = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 64, sx, sy, 0, dy + 6)
    ext = max(0, ang - 8) * 0.7
    fl = [(59, 104), (69, 104), (66, 104 + tl * 0.5), (64, 108 + tl * 0.8), (62, 104 + tl * 0.5)]
    p.glow_back += halo(T(fl), CYAN, 0.6)
    p.glow += poly(T(fl), CYAN) + poly(T(lerp_pts(fl, [(64, 106)] * 5, 0.5)), BONE)
    if i == 5:
        p.glow += speed_lines([46, 56, 72, 82], 92, 24, CYAN, 0.75)
    fold = 1 - min(1, max(0, ang - 8) / 24)   # fins sweep back on the lunge
    for side in (-1, 1):
        fin = [(64 + side * 6, 70), (64 + side * (16 + 18 * fold), 92), (64 + side * (12 + 10 * fold), 106), (64 + side * 6, 98)]
        cel(p, T(fin), ICE_HI, ICE, None, sh_off=(2 * side, 2), ink_w=2.5)
        wing = [(64 + side * 8, 52), (64 + side * (22 + 10 * fold), 60), (64 + side * 9, 72)]
        cel(p, T(wing), STEEL, STEEL_SH, STEEL_HI, sh_off=(2 * side, 2), ink_w=2.5)
    lance = [(60, 24), (68, 24), (64, 2 - ext)]
    cel(p, T(lance), BONE, ICE_HI, None, sh_off=(2, 0), ink_w=2.5)
    body = [(64, 14), (72, 30), (74, 70), (70, 100), (64, 106), (58, 100), (54, 70), (56, 30)]
    cel(p, T(body), ICE, ICE_SH, ICE_HI, sh_off=(5, 4), detail=True)
    for y in (40, 84):
        p.detail += line(T([(57, y), (64, y + 4), (71, y)]), 1.6)
    eye(p, T, [(58, 52), (70, 52), (68, 64), (60, 64)], (64, 58), ev, MAGENTA)
    p.glow += line(T([(64, 54), (64, 62)]), 1.2, INK)
    return p


def dragonsting(i):
    """Verdant: a dragonfly. Four long wings that blur on the lunge, compound
    eyes, mandibles forward and a long barbed tail behind."""
    ang, sx, sy, dy, ev, tl = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 64, sx, sy, 0, dy)
    beat = (0, 9, -7, 4, -14, 18)[i]
    for k, (y, L) in enumerate(((44, 52), (56, 46))):
        for side in (-1, 1):
            a = side * (90 - (14 if k == 0 else -12)) + side * beat * (1 if k == 0 else -1)
            base_ = (64 + side * 6, y)
            w = xf([base_, (base_[0] - 6, base_[1] - L * 0.45), (base_[0], base_[1] - L), (base_[0] + 6, base_[1] - L * 0.45)],
                   base_[0], base_[1], rot=a)
            cel(p, T(w), BILE_HI, MOSS, None, sh_off=(0, 3), ink_w=2.2)
            p.ink += line(T([w[0], w[2]]), 1.1)
    tail = [(60, 66), (68, 66), (67, 108), (64, 118 + tl * 0.2), (61, 108)]
    cel(p, T(tail), BRUISE, BRUISE_SH, BRUISE_HI, sh_off=(3, 0), ink_w=2.5)
    for y in (76, 86, 96, 106):
        p.ink += line(T([(61, y), (67, y)]), 1.6)
    barb = [(60, 112), (68, 112), (64, 124)]
    p.base += poly(T(barb), MAGENTA)
    p.ink += inkpoly(T(barb), 1.8)
    thorax = [(64, 34), (74, 40), (74, 62), (64, 68), (54, 62), (54, 40)]
    cel(p, T(thorax), BRUISE, BRUISE_SH, BRUISE_HI, sh_off=(4, 4), detail=True)
    head = [(64, 16), (76, 24), (76, 36), (64, 40), (52, 36), (52, 24)]
    cel(p, T(head), BILE, BILE_SH, BILE_HI, sh_off=(4, 4), detail=True)
    for side in (-1, 1):
        m = xf([(64 + side * 4, 18), (64 + side * 9, 6), (64 + side * 2, 12)], 64 + side * 4, 18, rot=-side * (ang - 8))
        p.detail += poly(T(m), BONE) + inkpoly(T(m), 1.4)
        e = [(64 + side * 3, 22), (64 + side * 12, 24), (64 + side * 11, 34), (64 + side * 4, 34)]
        eye(p, T, e, (64 + side * 7.5, 28), ev, BILE_LIGHT)
    if i == 5:
        p.glow += speed_lines([44, 54, 74, 84], 92, 26, BILE_LIGHT, 0.75)
    return p


def cinder_fang(i):
    """Ember: a wide salamander head. Its upper and lower jaws hinge apart on
    the lunge to show a molten throat; a flame trails behind."""
    ang, sx, sy, dy, ev, tl = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 64, sx, sy, 0, dy + 5)
    gape = max(0, ang) * 0.35
    hot = AMBER if ev > 0.9 else SODIUM
    fl = [(54, 96), (74, 96), (70, 100 + tl * 0.5), (64, 104 + tl * 0.9), (58, 100 + tl * 0.5)]
    p.glow_back += halo(T(fl), SODIUM, 0.6)
    p.glow += poly(T(fl), SODIUM) + poly(T(lerp_pts(fl, [(64, 100)] * 5, 0.5)), AMBER)
    if i == 5:
        p.glow += speed_lines([36, 48, 80, 92], 94, 24, SODIUM, 0.75)
    # back frills
    for side in (-1, 1):
        fr = [(64 + side * 30, 60), (64 + side * 54, 70), (64 + side * 44, 80), (64 + side * 50, 92), (64 + side * 28, 84)]
        cel(p, T(fr), GUN, GUN_SH, GUN_HI, sh_off=(3, 3), ink_w=2.5)
    # molten throat between the jaws
    throat = [(40, 40 - gape * 0.4), (88, 40 - gape * 0.4), (84, 52), (44, 52)]
    p.ink += poly(T(throat), INK)
    p.glow += poly(T(lerp_pts(throat, [(64, 46)] * 4, 0.2)), hot)
    if ev > 0.9:
        p.glow_back += halo(T(throat), SODIUM, 0.8)
    # upper jaw lifts with the gape, lower jaw is the head
    up = [(26, 42 - gape), (40, 18 - gape), (64, 10 - gape), (88, 18 - gape), (102, 42 - gape), (84, 38 - gape * 0.5), (64, 34 - gape * 0.5), (44, 38 - gape * 0.5)]
    cel(p, T(up), CHAR, CHAR_SH, CHAR_HI, sh_off=(8, 5), ink_w=3)
    for k in range(5):
        x = 44 + k * 10
        t = [(x - 3, 36 - gape * 0.5), (x + 3, 36 - gape * 0.5), (x, 44 - gape * 0.5)]
        p.detail += poly(T(t), BONE) + inkpoly(T(t), 1.2)
    head = [(24, 46), (104, 46), (98, 72), (80, 96), (64, 100), (48, 96), (30, 72)]
    cel(p, T(head), CHAR, CHAR_SH, CHAR_HI, sh_off=(10, 8), detail=True)
    for side in (-1, 1):
        e = [(64 + side * 16, 58), (64 + side * 32, 56), (64 + side * 30, 66), (64 + side * 18, 66)]
        eye(p, T, e, (64 + side * 24, 61), ev, MAGENTA)
    p.detail += line(T([(50, 80), (64, 88), (78, 80)]), 4.5)
    p.glow += line(T([(50, 80), (64, 88), (78, 80)]), 2, hot)
    return p


def draw(world, i):
    return {"space": hound, "frost": lancer, "verdant": dragonsting, "ember": cinder_fang}[world](i)
