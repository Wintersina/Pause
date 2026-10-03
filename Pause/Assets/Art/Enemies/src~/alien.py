"""Alien, one per world: the crowned bug from the art-style sample, re-grown
for each planet. They fly in lines (the alien achievements key on them), so
the idle is a classic invader wiggle; the tell, when the player is close, is a
crouch and a mandible chomp with the eye flaring."""
from common import *

# (mandible open deg, sy, dy, lid 0 open..1 shut, flare)
FRAMES = [
    (10, 1.00, 0, 0.0, False),    # 0 key
    (-6, 0.92, 5, 0.2, False),    # 1 crouch
    (30, 1.08, -6, 0.0, True),    # 2 snap
    (16, 1.00, -1, 0.75, False),  # 3 settle + blink
    (-12, 0.86, 8, 0.0, False),   # 4 tell: deep crouch, jaws shut
    (44, 1.12, -8, 0.0, True),    # 5 tell: CHOMP
]
IDLE_TICKS = [4, 2, 3, 3]
TELL_TICKS = [2, 3]

SKINS = {
    #          shell  shell_sh  shell_hi  mand    mand_sh    crest      eye
    "space": (BILE, BILE_SH, BILE_HI, BRUISE, BRUISE_SH, BRUISE, BILE_LIGHT),
    "frost": (ICE, ICE_SH, ICE_HI, STEEL, STEEL_SH, ICE_HI, MAGENTA),
    "verdant": (MOSS, BILE_SH, BILE_HI, BARK, BARK_SH, MAGENTA, BILE_LIGHT),
    "ember": (CHAR, CHAR_SH, CHAR_HI, GUN, GUN_SH, SODIUM, MAGENTA),
}


def draw(world, i):
    shell, shell_sh, shell_hi, mand, mand_sh, crest_c, eye = SKINS[world]
    ang, sy, dy, lid, flare = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 70, 1 / sy ** 0.5, sy, 0, dy)

    # crown: crests (space), ice spikes (frost), leaves (verdant), flames (ember)
    if world == "verdant":
        for x, a, L in ((46, -40, 30), (56, -14, 34), (72, 14, 34), (82, 40, 30)):
            lf = xf([(x, 30), (x - 8, 30 - L * 0.5), (x, 30 - L), (x + 8, 30 - L * 0.5)], x, 30, rot=a)
            cel(p, T(lf), BILE, BILE_SH, BILE_HI, sh_off=(3, 3), ink_w=2.5)
            p.ink += line(T([lf[0], lf[2]]), 1.2)
        bud = [(60, 22), (64, 6), (68, 22), (64, 26)]
        p.base += poly(T(bud), MAGENTA)
        p.ink += inkpoly(T(bud), 2.5)
    elif world == "ember":
        flick = (0, 3, -4, 2, 5, -8)[i]
        for x, L in ((44, 22), (54, 32), (64, 42), (74, 32), (84, 22)):
            fl = [(x - 7, 32), (x - 4 + flick * 0.3, 32 - L * 0.6), (x + flick * 0.5, 32 - L), (x + 3, 32 - L * 0.55), (x + 7, 32)]
            p.glow_back += halo(T(fl), SODIUM, 0.6)
            p.glow += poly(T(fl), SODIUM) + poly(T(lerp_pts(fl, [(x, 30)] * 5, 0.5)), AMBER) + inkpoly(T(fl), 2)
    else:
        crestL = [(56, 30), (46, 6), (42, 26)]
        crestC = [(60, 22), (64, 2), (68, 22), (64, 26)]
        c_sh = ICE_SH if world == "frost" else BILE_SH
        for c in (crestL, mx(crestL, 64)):
            p.base += poly(T(c), crest_c if world == "frost" else c_sh)
            p.ink += inkpoly(T(c), 3)
        p.base += poly(T(crestC), crest_c if world == "space" else ICE_HI)
        p.shadow += poly(T([(64, 2), (68, 22), (64, 26)]), BRUISE_SH if world == "space" else ICE)
        p.ink += inkpoly(T(crestC), 3)

    # mandibles pivot at (46,88)/(82,88)
    mandL0 = [(48, 86), (40, 92), (36, 112), (46, 124), (46, 108), (54, 94)]
    for m, piv, rot in ((mandL0, (46, 88), ang), (mx(mandL0, 64), (82, 88), -ang)):
        mm = T(xf(m, piv[0], piv[1], rot=rot))
        cel(p, mm, mand, mand_sh, None, sh_off=(4, 4), ink_w=3)

    head_half = [(64, 18), (56, 30), (42, 26), (36, 40), (24, 46), (30, 60), (34, 80), (48, 92), (64, 96)]
    head = T(mirror(head_half, 64))
    cel(p, head, shell, shell_sh, shell_hi, sh_off=(10, 8), detail=True)
    plateL = [(42, 26), (36, 40), (24, 46), (30, 60), (44, 52), (50, 36)]
    for pl in (plateL, mx(plateL, 64)):
        p.detail += inkpoly(T(pl), 2)
    p.detail += line(T([(52, 80), (64, 84), (76, 80)]), 2)
    p.detail += line(T([(40, 66), (48, 72)]), 1.6) + line(T([(88, 66), (80, 72)]), 1.6)
    if world == "frost":   # frost rime on the shoulders
        for side in (-1, 1):
            r = [(64 + side * 30, 44), (64 + side * 40, 46), (64 + side * 34, 54)]
            p.detail += poly(T(r), BONE) + inkpoly(T(r), 1.4)
    if world == "ember":   # lava cracks
        for c in ([(38, 50), (46, 58), (42, 70)], [(90, 50), (82, 58), (86, 70)]):
            p.detail += line(T(c), 4.5)
            p.glow += line(T(c), 2, AMBER if flare else SODIUM)

    sock = T([(50, 52), (64, 44), (78, 52), (74, 66), (64, 70), (54, 66)])
    p.detail += poly(sock, INK)
    e = T([(54, 53), (64, 48), (74, 53), (71, 63), (64, 66), (57, 63)])
    if flare:
        p.glow_back += poly(T([(40, 40), (88, 40), (84, 76), (44, 76)]), eye, 'opacity="0.55" filter="url(#glowM)"')
    p.glow += poly(e, eye)
    p.glow += poly(T([(62, 52), (66, 52), (65.5, 62), (62.5, 62)]), INK)
    if lid > 0:
        y0 = 47 + (66 - 47) * lid
        p.glow += poly(T([(52, 46), (76, 46), (76, y0), (52, y0)]), shell_sh)
        p.glow += line(T([(53, y0), (75, y0)]), 2.2)
    p.glow += inkpoly(sock, 2.5)
    if i == 5:
        p.glow += speed_lines([30, 98], 100, 18, eye, 0.7)
    return p
