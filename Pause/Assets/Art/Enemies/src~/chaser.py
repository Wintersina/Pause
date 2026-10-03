"""Chaser, one per world. It climbs up from below the board and hunts the
player, so it faces UP. Idle (drifting): hover, eye pulse. Tell (while it is
chasing): crouch with jaws shut, then the lunge -- stretched, jaws wide,
thruster flaring, speed lines streaming behind it."""
from common import *

# (jaw open deg, sx, sy, dy, eye, thrust length)
FRAMES = [
    (8, 1.00, 1.00, 0, 0.6, 10),     # 0 key
    (4, 1.00, 1.00, 2, 0.5, 8),      # 1 bob
    (10, 1.00, 1.00, 1, 1.0, 14),    # 2 eye pulse
    (6, 1.00, 1.00, -1, 0.6, 10),    # 3 settle
    (-4, 1.08, 0.90, 4, 0.2, 4),     # 4 tell: crouch, jaws shut (anticipation)
    (26, 0.92, 1.10, -4, 1.4, 30),   # 5 tell: LUNGE
]
IDLE_TICKS = [6, 3, 2, 3]
TELL_TICKS = [2, 3]

SKINS = {
    #          body   body_sh   body_hi   jaw     jaw_sh     jaw_hi     eye        thrust
    "space": (STEEL, STEEL_SH, STEEL_HI, BRUISE, BRUISE_SH, BRUISE_HI, MAGENTA, MAGENTA),
    "frost": (ICE, ICE_SH, ICE_HI, STEEL, STEEL_SH, STEEL_HI, MAGENTA, CYAN),
    "verdant": (BRUISE, BRUISE_SH, BRUISE_HI, BILE, BILE_SH, BILE_HI, BILE_LIGHT, BILE_LIGHT),
    "ember": (CHAR, CHAR_SH, CHAR_HI, GUN, GUN_SH, GUN_HI, MAGENTA, SODIUM),
}


def draw(world, i):
    body, body_sh, body_hi, jaw, jaw_sh, jaw_hi, eye, thrust = SKINS[world]
    ang, sx, sy, dy, ev, tl = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 64, sx, sy, 0, dy)

    # thruster flame below (it flies up)
    fl = [(56, 100), (72, 100), (68, 100 + tl * 0.6), (64, 104 + tl), (60, 100 + tl * 0.6)]
    p.glow_back += halo(T(fl), thrust, 0.6)
    p.glow += poly(T(fl), thrust) + poly(T(lerp_pts(fl, [(64, 102)] * 5, 0.5)), BONE)
    if i == 5:
        p.glow += speed_lines([40, 50, 78, 88], 96, 26, thrust, 0.75)

    # side fins / wings
    if world == "verdant":
        beat = (0, 8, -6, 4, -14, 18)[i]
        for side in (-1, 1):
            w = xf([(64 + side * 12, 60), (64 + side * 22, 34), (64 + side * 44, 30), (64 + side * 38, 52)], 64 + side * 12, 60, rot=side * beat)
            cel(p, T(w), BILE_HI, BILE, None, sh_off=(3 * side, 3), ink_w=2.5)
    else:
        for side in (-1, 1):
            fin = [(64 + side * 18, 60), (64 + side * 46, 86), (64 + side * 40, 98), (64 + side * 20, 90)]
            cel(p, T(fin), jaw, jaw_sh, jaw_hi, sh_off=(3, 3), ink_w=3)

    # jaws: two prongs at the nose, hinged at (54,44)/(74,44)
    for side in (-1, 1):
        hx = 64 + side * 10
        prong = [(hx, 46), (hx + side * 2, 18), (hx - side * 4, 4), (hx - side * 8, 22), (hx - side * 6, 42)]
        prong = xf(prong, hx, 46, rot=side * ang)
        cel(p, T(prong), jaw, jaw_sh, jaw_hi, sh_off=(3, 3), ink_w=3)
        tooth = xf([(hx - side * 7, 26), (hx - side * 12, 30), (hx - side * 6, 34)], hx, 46, rot=side * ang)
        p.detail += poly(T(tooth), BONE) + inkpoly(T(tooth), 1.4)

    if world == "frost":   # the lance: an icicle between the jaws that shoots out on the lunge
        ext = max(0, ang - 8) * 0.9
        lance = [(60, 40), (68, 40), (64, 8 - ext)]
        cel(p, T(lance), ICE_HI, ICE, None, sh_off=(2, 0), ink_w=2.5)
    if world == "verdant":   # stinger forward
        ext = max(0, ang - 8) * 0.8
        st = [(59, 44), (69, 44), (64, 12 - ext)]
        p.base += poly(T(st), MAGENTA)
        p.ink += inkpoly(T(st), 2.2)

    hull = [(64, 30), (78, 40), (84, 66), (76, 98), (64, 104), (52, 98), (44, 66), (50, 40)]
    cel(p, T(hull), body, body_sh, body_hi, sh_off=(8, 6), detail=True)
    p.detail += line(T([(64, 74), (64, 98)]), 2)
    if world == "ember":   # molten seam down the back
        p.detail += line(T([(56, 80), (64, 92), (72, 80)]), 5)
        p.glow += line(T([(56, 80), (64, 92), (72, 80)]), 2.2, AMBER if ev > 0.8 else SODIUM)
    if world == "verdant":
        for y in (78, 86, 94):
            p.detail += line(T([(54, y), (74, y)]), 1.8)
    # the eye: big, angular, hungry
    sock = [(52, 52), (64, 44), (76, 52), (72, 64), (56, 64)]
    p.detail += poly(T(sock), INK)
    e = lerp_pts(T(sock), [T([(64, 55)])[0]] * 5, 0.25)
    p.glow += poly(e, eye if ev > 0.4 else DIM[eye])
    p.glow += poly(T([(62.5, 49), (65.5, 49), (65, 61), (63, 61)]), INK)
    if ev > 0.9:
        p.glow_back += halo(T(sock), eye, 0.8)
        p.glow += poly(T([(57, 52), (60, 50), (59, 55)]), BONE)
    return p
