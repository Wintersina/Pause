"""Alien, one distinct invader per world. They fly in lines (the alien
achievements key on them), so the idle is a classic invader wiggle; the
tell, when the player is close, is a crouch then a snap with the eye flaring.

  space    Bile Mite    the style-guide crowned bug
  frost    Cryo Jelly   a crystal-domed jellyfish trailing icicle tentacles
  verdant  Snap Sprout  a walking flytrap whose jaws snap shut
  ember    Ember Imp    a living flame wearing a basalt mask"""
from common import *

# (open deg, sy, dy, lid 0 open..1 shut, flare)
FRAMES = [
    (10, 1.00, 0, 0.0, False),    # 0 key
    (-6, 0.92, 5, 0.2, False),    # 1 crouch
    (30, 1.08, -6, 0.0, True),    # 2 snap
    (16, 1.00, -1, 0.75, False),  # 3 settle + blink
    (-12, 0.86, 8, 0.0, False),   # 4 tell: deep crouch
    (44, 1.12, -8, 0.0, True),    # 5 tell: SNAP
]
IDLE_TICKS = [4, 2, 3, 3]
TELL_TICKS = [2, 3]


def slit_eye(p, T, sock, eye_pts, light, flare, lid, lid_col):
    p.detail += poly(T(sock), INK)
    if flare:
        p.glow_back += halo(T(sock), light, 0.6)
    p.glow += poly(T(eye_pts), light)
    cx = sum(x for x, _ in eye_pts) / len(eye_pts)
    cy = sum(y for _, y in eye_pts) / len(eye_pts)
    p.glow += poly(T([(cx - 2, cy - 6), (cx + 2, cy - 6), (cx + 1.5, cy + 5), (cx - 1.5, cy + 5)]), INK)
    if lid > 0:
        ys = [y for _, y in eye_pts]
        y0 = min(ys) + (max(ys) - min(ys)) * lid
        xs = [x for x, _ in eye_pts]
        p.glow += poly(T([(min(xs), min(ys) - 1), (max(xs), min(ys) - 1), (max(xs), y0), (min(xs), y0)]), lid_col)
        p.glow += line(T([(min(xs) + 1, y0), (max(xs) - 1, y0)]), 2.2)
    p.glow += inkpoly(T(sock), 2.5)


def mite(i):
    """Space: bile carapace, bruise crests and mandibles, one slit eye."""
    ang, sy, dy, lid, flare = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 70, 1 / sy ** 0.5, sy, 0, dy)
    crestL = [(56, 30), (46, 6), (42, 26)]
    crestC = [(60, 22), (64, 2), (68, 22), (64, 26)]
    for c in (crestL, mx(crestL, 64)):
        p.base += poly(T(c), BILE_SH)
        p.ink += inkpoly(T(c), 3)
    p.base += poly(T(crestC), BRUISE)
    p.shadow += poly(T([(64, 2), (68, 22), (64, 26)]), BRUISE_SH)
    p.ink += inkpoly(T(crestC), 3)
    mandL0 = [(48, 86), (40, 92), (36, 112), (46, 124), (46, 108), (54, 94)]
    for m, piv, rot in ((mandL0, (46, 88), ang), (mx(mandL0, 64), (82, 88), -ang)):
        cel(p, T(xf(m, piv[0], piv[1], rot=rot)), BRUISE, BRUISE_SH, None, sh_off=(4, 4), ink_w=3)
    head_half = [(64, 18), (56, 30), (42, 26), (36, 40), (24, 46), (30, 60), (34, 80), (48, 92), (64, 96)]
    cel(p, T(mirror(head_half, 64)), BILE, BILE_SH, BILE_HI, sh_off=(10, 8), detail=True)
    plateL = [(42, 26), (36, 40), (24, 46), (30, 60), (44, 52), (50, 36)]
    for pl in (plateL, mx(plateL, 64)):
        p.detail += inkpoly(T(pl), 2)
    p.detail += line(T([(52, 80), (64, 84), (76, 80)]), 2)
    slit_eye(p, T, [(50, 52), (64, 44), (78, 52), (74, 66), (64, 70), (54, 66)],
             [(54, 53), (64, 48), (74, 53), (71, 63), (64, 66), (57, 63)], BILE_LIGHT, flare, lid, BILE_SH)
    if i == 5:
        p.glow += speed_lines([30, 98], 100, 18, BILE_LIGHT, 0.7)
    return p


def jelly(i):
    """Frost: a faceted ice dome with one magenta eye, trailing icicle
    tentacles that sway; the snap pulls them in, then lashes them out."""
    ang, sy, dy, lid, flare = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 50, 1.08 / sy ** 0.5, 1.08 * sy, 0, dy + 3)
    sway = (0, 4, -5, 2, 0, 0)[i]
    reach = 1.0 + (ang - 10) / 70.0
    for k, x in enumerate((34, 48, 64, 80, 94)):
        L = (50 if k in (1, 3) else 40 if k == 2 else 34) * reach
        sw = sway * (1 if k % 2 else -1)
        t = [(x - 5, 56), (x + 5, 56), (x + 2 + sw, 56 + L * 0.6), (x + sw * 1.4, 56 + L), (x - 2 + sw, 56 + L * 0.6)]
        cel(p, T(t), ICE_HI, ICE, None, sh_off=(2, 0), ink_w=2.2)
    dome = [(64, 6), (94, 18), (106, 44), (100, 58), (28, 58), (22, 44), (34, 18)]
    cel(p, T(dome), ICE, ICE_SH, ICE_HI, sh_off=(10, 8), detail=True)
    for a, b in (((64, 6), (64, 30)), ((34, 18), (48, 34)), ((94, 18), (80, 34)), ((22, 44), (40, 50)), ((106, 44), (88, 50))):
        p.detail += line(T([a, b]), 1.6)
    rim = [(26, 54), (102, 54), (98, 62), (30, 62)]
    cel(p, T(rim), STEEL, STEEL_SH, STEEL_HI, sh_off=(0, 3), ink_w=2.5, detail=True)
    slit_eye(p, T, [(50, 32), (64, 26), (78, 32), (74, 46), (54, 46)],
             [(54, 33), (64, 29), (74, 33), (71, 43), (57, 43)], MAGENTA, flare, lid, ICE_SH)
    if i == 5:
        for x, y in ((14, 70), (114, 70)):
            p.glow += spark(x, y, 5, ICE_HI)
    return p


def flytrap(i):
    """Verdant: a flytrap head on a root body. Its jaws gape and snap; the
    bile glow is its gullet."""
    ang, sy, dy, lid, flare = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 70, 1 / sy ** 0.5, sy, 0, dy + 5)
    gape = max(-6, ang) * 0.35
    # root legs
    for side in (-1, 1):
        for k, (x, y) in enumerate(((20, 120), (34, 124))):
            p.ink += line(T([(64 + side * 8, 92), (64 + side * (64 - x) * 0.6, 108), (64 + side * (64 - x), y)]), 6)
            p.ink += line(T([(64 + side * 8, 92), (64 + side * (64 - x) * 0.6, 108), (64 + side * (64 - x), y)]), 3, BARK)
    stem = [(56, 76), (72, 76), (74, 100), (54, 100)]
    cel(p, T(stem), MOSS, BILE_SH, BILE_HI, sh_off=(4, 0), ink_w=3)
    # gullet behind the jaws
    gul = [(30, 56 - gape * 0.3), (98, 56 - gape * 0.3), (90, 70), (38, 70)]
    p.ink += poly(T(gul), INK)
    glow_c = BILE_LIGHT if (flare or ang > 0) else BILE_SH
    p.glow += poly(T(lerp_pts(gul, [(64, 63)] * 4, 0.3)), glow_c)
    if flare:
        p.glow_back += halo(T(gul), BILE_LIGHT, 0.7)
    # lower jaw
    low = [(20, 62), (40, 82), (64, 88), (88, 82), (108, 62), (88, 70), (64, 74), (40, 70)]
    cel(p, T(low), BILE, BILE_SH, BILE_HI, sh_off=(4, 4), ink_w=3)
    # upper jaw lifts with the gape
    up = [(18, 58 - gape), (30, 26 - gape), (64, 12 - gape), (98, 26 - gape), (110, 58 - gape), (88, 52 - gape * 0.6), (64, 48 - gape * 0.6), (40, 52 - gape * 0.6)]
    cel(p, T(up), BILE, BILE_SH, BILE_HI, sh_off=(8, 6), ink_w=3, detail=True)
    p.detail += poly(T([(30, 30 - gape), (64, 18 - gape), (98, 30 - gape), (88, 40 - gape), (64, 32 - gape), (40, 40 - gape)]), MAGENTA)
    p.detail += inkpoly(T([(30, 30 - gape), (64, 18 - gape), (98, 30 - gape), (88, 40 - gape), (64, 32 - gape), (40, 40 - gape)]), 1.6)
    for k in range(7):   # teeth/cilia along both jaws
        x = 26 + k * 12.6
        t1 = [(x - 3, 54 - gape * 0.8), (x + 3, 54 - gape * 0.8), (x, 64 - gape * 0.5)]
        t2 = [(x - 3, 66), (x + 3, 66), (x, 58)]
        p.detail += poly(T(t1), BONE) + inkpoly(T(t1), 1.2) + poly(T(t2), BONE) + inkpoly(T(t2), 1.2)
    if i == 5:
        p.glow += speed_lines([24, 104], 96, 16, BILE_LIGHT, 0.7)
    return p


def imp(i):
    """Ember: a living flame (drawn as flat cels so it reads at size) behind a
    basalt mask with two magenta eyes; it flares and grins on the snap."""
    ang, sy, dy, lid, flare = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 66, 0.94 / sy ** 0.5, 0.94 * sy, 0, dy)
    flick = (0, 4, -5, 2, 6, -9)[i]
    flame = [(64, 126), (30, 104), (20, 70), (30, 40 + flick * 0.5), (36, 58), (40, 26 - flick * 0.6),
             (52, 44), (64, 10 + flick * 0.4), (76, 44), (88, 26 + flick * 0.6), (92, 58), (98, 40 - flick * 0.5),
             (108, 70), (98, 104)]
    p.glow_back += halo(T(flame), SODIUM, 0.55 if not flare else 0.8)
    cel(p, T(flame), SODIUM, SODIUM_SH, AMBER, sh_off=(10, 8), ink_w=3.5)
    inner = lerp_pts(flame, [(64, 84)] * len(flame), 0.45)
    p.highlight += poly(T(inner), AMBER)
    mask = [(36, 52), (64, 44), (92, 52), (88, 84), (64, 96), (40, 84)]
    cel(p, T(mask), CHAR, CHAR_SH, CHAR_HI, sh_off=(6, 6), detail=True)
    for side in (-1, 1):
        horn = [(64 + side * 20, 50), (64 + side * 34, 30), (64 + side * 28, 52)]
        p.detail += poly(T(horn), CHAR_HI) + inkpoly(T(horn), 2)
        sock = [(64 + side * 6, 60), (64 + side * 22, 58), (64 + side * 20, 70), (64 + side * 8, 70)]
        e = lerp_pts(sock, [(64 + side * 14, 64.5)] * 4, 0.3)
        p.detail += poly(T(sock), INK)
        p.glow += poly(T(e), MAGENTA if lid < 0.5 else MAGENTA_SH)
        if flare:
            p.glow_back += halo(T(sock), MAGENTA, 0.6)
    grin = 2 + max(0, ang) * 0.12
    mouth = [(50, 78), (78, 78), (72, 80 + grin), (56, 80 + grin)]
    p.detail += poly(T(mouth), INK)
    p.glow += poly(T(lerp_pts(mouth, [(64, 79 + grin * 0.5)] * 4, 0.3)), AMBER if flare else SODIUM)
    return p


def draw(world, i):
    return {"space": mite, "frost": jelly, "verdant": flytrap, "ember": imp}[world](i)
