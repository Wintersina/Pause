"""Alien, one distinct invader per world. They fly in lines (the alien
achievements key on them), so the idle is a classic invader wiggle with
blinking spiracles / rim lights and twitching legs, tentacles or roots; the
tell, when the player is close, is a crouch then a snap with the eye
flaring.

  space    Bile Mite    the style-guide crowned bug
  frost    Cryo Jelly   a crystal-domed jellyfish trailing icicle tentacles
  verdant  Snap Sprout  a walking flytrap whose jaws snap shut
  ember    Ember Imp    a living flame wearing a basalt mask

Detail pass: one hard angular shadow plane per form on top of its cel
crescent, chitin / scale plates, veins and cracks, teeth, BONE specular
kicks and small hard-bloom accent lights."""
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

TWITCH = (0, -6, 8, 3, -10, 12)       # legs / tentacles / roots
CHASE = (0, 1, 2, 3, 1, 2)            # which accent light is lit (a running chase)


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


def slit_eye(p, T, sock, eye_pts, light, flare, lid, lid_col):
    p.detail += poly(T(sock), INK)
    cx = sum(x for x, _ in eye_pts) / len(eye_pts)
    cy = sum(y for _, y in eye_pts) / len(eye_pts)
    if lid < 0.5:   # hard flat bloom
        p.glow += poly(T(lerp_pts(sock, [(cx, cy)] * len(sock), -0.22)), light, f'opacity="{0.42 if flare else 0.22}"')
    p.glow += poly(T(eye_pts), light)
    p.glow += poly(T(lerp_pts(eye_pts, [(cx, cy)] * len(eye_pts), 0.55)), BONE if flare else light)
    p.glow += poly(T([(cx - 2, cy - 6), (cx + 2, cy - 6), (cx + 1.5, cy + 5), (cx - 1.5, cy + 5)]), INK)
    p.glow += poly(T(ngon(cx - 3.5, cy - 3, 1.3, 4, 45)), BONE)   # hard catchlight
    if flare:
        p.glow += poly(T(star(cx, cy, 15, 1.6, 4, 0)), light, 'opacity="0.55"')
    if lid > 0:
        ys = [y for _, y in eye_pts]
        y0 = min(ys) + (max(ys) - min(ys)) * lid
        xs = [x for x, _ in eye_pts]
        p.glow += poly(T([(min(xs), min(ys) - 1), (max(xs), min(ys) - 1), (max(xs), y0), (min(xs), y0)]), lid_col)
        p.glow += line(T([(min(xs) + 1, y0), (max(xs) - 1, y0)]), 2.2)
    p.glow += inkpoly(T(sock), 2.5)


def mite(i):
    """Space: bile carapace in chitin plates, bruise crests and toothed
    mandibles, one slit eye, blinking spiracles and twitching legs."""
    ang, sy, dy, lid, flare = FRAMES[i]
    tw, ch = TWITCH[i], CHASE[i]
    p = Parts()
    T = lambda q: xf(q, 64, 70, 1 / sy ** 0.5, sy, 0, dy)
    # little legs under the carapace
    for side in (-1, 1):
        for k, y in enumerate((62, 74)):
            a = (64 + side * 32, y)
            b = (64 + side * 44, y + 8 + (tw if k == 0 else -tw) * 0.4 * side)
            c = (64 + side * 42, y + 18)
            p.base += line(T([a, b, c]), 4.2) + line(T([a, b, c]), 1.8, BRUISE)
            kn = ngon(b[0], b[1], 2.4, 6, 0)   # knuckle joint
            p.base += poly(T(kn), BRUISE_HI) + inkpoly(T(kn), 1.1)
    crestL = [(56, 30), (46, 6), (42, 26)]
    crestC = [(60, 22), (64, 2), (68, 22), (64, 26)]
    for c in (crestL, mx(crestL, 64)):
        p.base += poly(T(c), BILE_SH)
        p.ink += inkpoly(T(c), 3)
        p.ink += line(T([c[0], c[1]]), 1.2, BILE)
    p.base += poly(T(crestC), BRUISE)
    p.shadow += poly(T([(64, 2), (68, 22), (64, 26)]), BRUISE_SH)
    p.ink += inkpoly(T(crestC), 3)
    p.ink += line(T([(63, 8), (61.5, 20)]), 1.2, BONE)
    mandL0 = [(48, 86), (40, 92), (36, 112), (46, 124), (46, 108), (54, 94)]
    for side, (m, piv, rot) in zip((-1, 1), ((mandL0, (46, 88), ang), (mx(mandL0, 64), (82, 88), -ang))):
        mm = xf(m, piv[0], piv[1], rot=rot)
        form(p, T(mm), BRUISE, BRUISE_SH, None, cut=T([mm[2], mm[3], mm[4], mm[5]]), sh_off=(4, 4), ink_w=3.2)
        teeth(p, T([mm[4]])[0], T([mm[5]])[0], 3, 3.5, inward=side, layer="ink")
        p.ink += line(T([mm[1], mm[2]]), 1.2, BRUISE_HI)
    head_half = [(64, 18), (56, 30), (42, 26), (36, 40), (24, 46), (30, 60), (34, 80), (48, 92), (64, 96)]
    form(p, T(mirror(head_half, 64)), BILE, BILE_SH, BILE_HI, cut=T([(64, 18), (72, 30), (86, 26), (92, 40), (104, 46), (98, 60), (94, 80), (80, 92), (64, 96), (66, 72)]),
         sh_off=(10, 8), detail=True)
    plateL = [(42, 26), (36, 40), (24, 46), (30, 60), (44, 52), (50, 36)]
    for side, pl in ((-1, plateL), (1, mx(plateL, 64))):
        form(p, T(pl), BILE_HI if side < 0 else BILE, BILE_SH, None, cut=T([pl[2], pl[3], pl[4]]), sh_off=(2, 3), ink_w=2.2, detail=True)
        p.detail += line(T([pl[1], pl[4]]), 1.1)
    for side in (-1, 1):   # cheek chitin bands
        for k in range(2):
            y = 70 + k * 8
            b = [(64 + side * 9, y + 2), (64 + side * 18, y - 2), (64 + side * 25, y + 2), (64 + side * 18, y + 5), (64 + side * 9, y + 6)]
            p.detail += poly(T(b), BILE_HI) + poly(T([b[2], b[3], b[4]]), BILE_SH) + inkpoly(T(b), 1.3)
    seams(p, [T([(54, 87), (64, 91), (74, 87)]), T([(30, 72), (38, 76)]), T([(98, 72), (90, 76)])], 2)
    for k, (x, y) in enumerate(((36, 66), (92, 66), (40, 78), (88, 78))):   # spiracles
        hexlight(p, *T([(x, y)])[0], 2, 1.0 if k == ch else 0.5 if flare else 0.2, BILE_LIGHT, rot=0)
    spec(p, T([(27, 48), (32, 62), (35, 78)]), 1.5)
    spec(p, T([(44, 28), (54, 32)]), 1.3)
    slit_eye(p, T, [(50, 52), (64, 44), (78, 52), (74, 66), (64, 70), (54, 66)],
             [(54, 53), (64, 48), (74, 53), (71, 63), (64, 66), (57, 63)], BILE_LIGHT, flare, lid, BILE_SH)
    if i == 5:
        p.glow += speed_lines([30, 98], 100, 18, BILE_LIGHT, 0.7)
    return p


def jelly(i):
    """Frost: a faceted ice dome with one magenta eye, a steel rim ring of
    chasing cyan lights, segmented icicle tentacles that sway; the snap
    pulls them in, then lashes them out."""
    ang, sy, dy, lid, flare = FRAMES[i]
    ch = CHASE[i]
    p = Parts()
    T = lambda q: xf(q, 64, 50, 1.08 / sy ** 0.5, 1.08 * sy, 0, dy + 3)
    sway = (0, 4, -5, 2, 0, 0)[i]
    reach = 1.0 + (ang - 10) / 70.0
    for k, x in enumerate((34, 48, 64, 80, 94)):
        L = (50 if k in (1, 3) else 40 if k == 2 else 34) * reach
        sw = sway * (1 if k % 2 else -1)
        t = [(x - 5, 56), (x + 5, 56), (x + 2 + sw, 56 + L * 0.6), (x + sw * 1.4, 56 + L), (x - 2 + sw, 56 + L * 0.6)]
        form(p, T(t), ICE_HI, ICE, None, cut=T([(x, 56), (x + 5, 56), (x + 2 + sw, 56 + L * 0.6), (x + sw * 1.4, 56 + L)]), sh_off=(2, 0), ink_w=2.4)
        for s_ in (0.3, 0.52):   # segment rings
            y = 56 + L * s_
            p.ink += line(T([(x - 4 + sw * s_ * 1.6, y), (x + 4 + sw * s_ * 1.6, y)]), 1.1)
    dome = [(64, 6), (94, 18), (106, 44), (100, 58), (28, 58), (22, 44), (34, 18)]
    form(p, T(dome), ICE, ICE_SH, ICE_HI, cut=T([(64, 6), (94, 18), (106, 44), (100, 58), (80, 58), (80, 34), (64, 30)]),
         sh_off=(10, 8), detail=True)
    for a, b in (((64, 6), (64, 30)), ((34, 18), (48, 34)), ((94, 18), (80, 34)), ((22, 44), (40, 50)), ((106, 44), (88, 50))):
        p.detail += line(T([a, b]), 1.6)
    # crystal facets inside the dome
    for q in ([(40, 22), (52, 16), (50, 28)], [(86, 24), (96, 30), (88, 34)]):
        p.detail += poly(T(q), ICE_HI) + inkpoly(T(q), 1)
    spec(p, T([(26, 42), (36, 20), (60, 9)]), 1.8)
    spec(p, T([(44, 14), (52, 12)]), 1.2)
    rim = [(26, 54), (102, 54), (98, 62), (30, 62)]
    form(p, T(rim), STEEL, STEEL_SH, STEEL_HI, cut=T([(64, 54), (102, 54), (98, 62), (64, 62)]), sh_off=(0, 3), ink_w=2.6, detail=True)
    rivets(p, T([(32, 58), (96, 58), (38, 58), (90, 58)]), 1.2)
    seams(p, [T([(50, 55), (50, 61)]), T([(64, 55), (64, 61)]), T([(78, 55), (78, 61)])], 1.1)
    for q in ([(30, 46), (40, 40), (42, 52)], [(70, 14), (82, 18), (74, 24)]):   # more frost facets
        p.detail += poly(T(q), ICE_HI) + inkpoly(T(q), 1)
    for k, x in enumerate((44, 56, 72, 84)):   # chasing rim lights
        hexlight(p, *T([(x, 58)])[0], 1.7, 1.0 if k == ch or flare else 0.2, CYAN, rot=0)
    slit_eye(p, T, [(50, 32), (64, 26), (78, 32), (74, 46), (54, 46)],
             [(54, 33), (64, 29), (74, 33), (71, 43), (57, 43)], MAGENTA, flare, lid, ICE_SH)
    if i == 5:
        for x, y in ((14, 70), (114, 70)):
            p.glow += spark(x, y, 5, ICE_HI)
    return p


def flytrap(i):
    """Verdant: a flytrap head on a root body. Veined jaws gape and snap
    over a ring of teeth and cilia; bile spots pulse on the upper jaw; the
    bile glow is its gullet; knotted root legs shuffle."""
    ang, sy, dy, lid, flare = FRAMES[i]
    tw, ch = TWITCH[i], CHASE[i]
    p = Parts()
    T = lambda q: xf(q, 64, 70, 1 / sy ** 0.5, sy, 0, dy + 5)
    gape = max(-6, ang) * 0.35
    # root legs (shuffle)
    for side in (-1, 1):
        for k, (x, y) in enumerate(((20, 120), (34, 124))):
            sh_ = tw * 0.35 * (1 if k == 0 else -1)
            pts_ = [(64 + side * 8, 92), (64 + side * (64 - x) * 0.6, 108), (64 + side * (64 - x) + sh_, y)]
            p.ink += line(T(pts_), 6.2)
            p.ink += line(T(pts_), 3, BARK)
            p.ink += poly(T(ngon(pts_[1][0], pts_[1][1], 3.2, 6, 0)), BARK_SH) + inkpoly(T(ngon(pts_[1][0], pts_[1][1], 3.2, 6, 0)), 1.2)
            toe = [(pts_[2][0] - 3, pts_[2][1]), (pts_[2][0] + 3, pts_[2][1]), (pts_[2][0] + side * 5, pts_[2][1] + 3)]
            p.ink += poly(T(toe), BARK_SH) + inkpoly(T(toe), 1.2)
    stem = [(56, 76), (72, 76), (74, 100), (54, 100)]
    form(p, T(stem), MOSS, BILE_SH, BILE_HI, cut=T([(64, 76), (72, 76), (74, 100), (64, 100)]), sh_off=(4, 0), ink_w=3)
    for side in (-1, 1):   # stem leaves
        lf = [(64 + side * 8, 92), (64 + side * 20, 84), (64 + side * 16, 96)]
        p.ink += poly(T(lf), BILE) + poly(T([lf[1], lf[2], ((lf[0][0] + lf[1][0]) / 2, (lf[0][1] + lf[1][1]) / 2)]), BILE_SH) + inkpoly(T(lf), 1.4)
    # gullet behind the jaws
    gul = [(30, 56 - gape * 0.3), (98, 56 - gape * 0.3), (90, 70), (38, 70)]
    p.ink += poly(T(gul), INK)
    glow_c = BILE_LIGHT if (flare or ang > 0) else BILE_SH
    p.glow += poly(T(lerp_pts(gul, [(64, 63)] * 4, 0.3)), glow_c)
    if flare:
        p.glow += poly(T(lerp_pts(gul, [(64, 63)] * 4, 0.6)), BONE)
        p.glow += poly(T(lerp_pts(gul, [(64, 63)] * 4, -0.12)), BILE_LIGHT, 'opacity="0.35"')
    # lower jaw
    low = [(20, 62), (40, 82), (64, 88), (88, 82), (108, 62), (88, 70), (64, 74), (40, 70)]
    form(p, T(low), BILE, BILE_SH, BILE_HI, cut=T([(64, 74), (88, 70), (108, 62), (88, 82), (64, 88)]), sh_off=(4, 4), ink_w=3.2)
    seams(p, [T([(40, 76), (52, 79)]), T([(76, 79), (88, 76)])], 1.2, layer="ink")
    # upper jaw lifts with the gape
    up = [(18, 58 - gape), (30, 26 - gape), (64, 12 - gape), (98, 26 - gape), (110, 58 - gape), (88, 52 - gape * 0.6), (64, 48 - gape * 0.6), (40, 52 - gape * 0.6)]
    form(p, T(up), BILE, BILE_SH, BILE_HI, cut=T([(64, 12 - gape), (98, 26 - gape), (110, 58 - gape), (88, 52 - gape * 0.6), (64, 48 - gape * 0.6), (72, 30 - gape)]),
         sh_off=(8, 6), ink_w=3.4, detail=True)
    band = [(30, 30 - gape), (64, 18 - gape), (98, 30 - gape), (88, 40 - gape), (64, 32 - gape), (40, 40 - gape)]
    p.detail += poly(T(band), MAGENTA) + poly(T([(64, 18 - gape), (98, 30 - gape), (88, 40 - gape), (64, 32 - gape)]), MAGENTA_SH)
    p.detail += inkpoly(T(band), 1.6)
    # veins fanning across the jaw
    for a, b in (((64, 34), (64, 46)), ((50, 38), (36, 52)), ((78, 38), (92, 52)), ((44, 34), (26, 44)), ((84, 34), (102, 44))):
        p.detail += line(T([(a[0], a[1] - gape), (b[0], b[1] - gape * 0.7)]), 1.2)
    spec(p, T([(21, 54 - gape), (31, 28 - gape), (56, 15 - gape)]), 1.6)
    for k, (x, y) in enumerate(((44, 44), (84, 44), (64, 40))):   # bile spots
        hexlight(p, *T([(x, y - gape * 0.8)])[0], 2, 1.0 if (k == ch % 3 or flare) else 0.5, BILE_LIGHT, rot=0)
    for k in range(7):   # teeth/cilia along both jaws
        x = 26 + k * 12.6
        t1 = [(x - 3, 54 - gape * 0.8), (x + 3, 54 - gape * 0.8), (x, 64 - gape * 0.5)]
        t2 = [(x - 3, 66), (x + 3, 66), (x, 58)]
        p.detail += poly(T(t1), BONE) + inkpoly(T(t1), 1.2) + poly(T(t2), BONE) + inkpoly(T(t2), 1.2)
    for side in (-1, 1):   # cilia bristles at the jaw tips
        for k in range(3):
            a = (64 + side * (44 - k * 4), 56 - gape - k * 8)
            p.detail += line(T([a, (a[0] + side * 7, a[1] - 3)]), 1.6)
    if i == 5:
        p.glow += speed_lines([24, 104], 96, 16, BILE_LIGHT, 0.7)
    return p


def imp(i):
    """Ember: a living flame (flat cels so it reads at size) with licking
    inner tongues, behind a cracked basalt mask: ridged horns, brow plates,
    painted glyph marks, two magenta eyes and lava pores; it flares and
    grins on the snap."""
    ang, sy, dy, lid, flare = FRAMES[i]
    tw, ch = TWITCH[i], CHASE[i]
    p = Parts()
    T = lambda q: xf(q, 64, 66, 0.94 / sy ** 0.5, 0.94 * sy, 0, dy)
    flick = (0, 4, -5, 2, 6, -9)[i]
    flame = [(64, 126), (30, 104), (20, 70), (30, 40 + flick * 0.5), (36, 58), (40, 26 - flick * 0.6),
             (52, 44), (64, 10 + flick * 0.4), (76, 44), (88, 26 + flick * 0.6), (92, 58), (98, 40 - flick * 0.5),
             (108, 70), (98, 104)]
    p.glow_back += poly(T(lerp_pts(flame, [(64, 80)] * len(flame), -0.08)), SODIUM, f'opacity="{0.55 if flare else 0.3}"')
    form(p, T(flame), SODIUM, SODIUM_SH, AMBER, cut=T([(64, 126), (98, 104), (108, 70), (98, 40 - flick * 0.5), (92, 58), (82, 84), (64, 100)]),
         sh_off=(10, 8), ink_w=3.6)
    for side in (-1, 1):   # loose flame tongues licking off the sides (flat inked cels)
        tg = [(64 + side * 36, 98), (64 + side * (50 + flick * 0.4), 84 - flick * 0.6), (64 + side * 40, 88)]
        p.base += poly(T(tg), SODIUM) + poly(T([tg[1], tg[2], (64 + side * 40, 94)]), SODIUM_SH)
        p.ink += inkpoly(T(tg), 2)
    inner = lerp_pts(flame, [(64, 84)] * len(flame), 0.45)
    p.highlight += poly(T(inner), AMBER)
    core = lerp_pts(flame, [(64, 92)] * len(flame), 0.75)
    p.highlight += poly(T(core), BONE)
    # licking tongue lines inside the flame
    for x, top in ((44, 74), (84, 74), (64, 104)):
        p.ink += line(T([(x - 4, top + 14), (x + flick * 0.3, top), (x + 4, top + 10)]), 1.3, SODIUM_SH)
    mask = [(36, 52), (64, 44), (92, 52), (88, 84), (64, 96), (40, 84)]
    form(p, T(mask), CHAR, CHAR_SH, CHAR_HI, cut=T([(64, 44), (92, 52), (88, 84), (64, 96), (68, 72)]), sh_off=(6, 6), detail=True)
    for side in (-1, 1):
        horn = [(64 + side * 20, 50), (64 + side * (34 + tw * 0.15), 30), (64 + side * 28, 52)]
        p.detail += poly(T(horn), CHAR_HI) + poly(T([horn[1], horn[2], (64 + side * 24, 51)]), CHAR_SH) + inkpoly(T(horn), 2.2)
        for t in (0.35, 0.65):   # horn ridges
            a = (horn[0][0] + (horn[1][0] - horn[0][0]) * t, horn[0][1] + (horn[1][1] - horn[0][1]) * t)
            b = (horn[2][0] + (horn[1][0] - horn[2][0]) * t, horn[2][1] + (horn[1][1] - horn[2][1]) * t)
            p.detail += line(T([a, b]), 1.1)
        sock = [(64 + side * 6, 60), (64 + side * 22, 58), (64 + side * 20, 70), (64 + side * 8, 70)]
        e = lerp_pts(sock, [(64 + side * 14, 64.5)] * 4, 0.3)
        p.detail += poly(T(sock), INK)
        if lid < 0.5:
            p.glow += poly(T(lerp_pts(sock, [(64 + side * 14, 64.5)] * 4, -0.25)), MAGENTA, f'opacity="{0.45 if flare else 0.22}"')
        p.glow += poly(T(e), MAGENTA if lid < 0.5 else MAGENTA_SH)
        if lid < 0.5:
            p.glow += poly(T(ngon(64 + side * 14, 64.5, 1.6, 4, 45)), BONE)
        p.detail += line(T([(64 + side * 4, 57), (64 + side * 24, 54)]), 2.4)   # brow plate
        hexlight(p, *T([(64 + side * 16, 78)])[0], 1.7, 1.0 if (ch % 2 == (side > 0)) or flare else 0.2, SODIUM, rot=0)
    # cracks through the mask (lava shows through)
    for c in ([(64, 46), (60, 52), (64, 56)], [(84, 74), (78, 82)], [(42, 72), (48, 80)]):
        p.detail += line(T(c), 3)
        p.glow += line(T(c), 1.3, SODIUM if not flare else AMBER)
    glyph(p, *T([(64, 88)])[0], 1.6, seed=3, color=BONE, w=1)
    rivets(p, T([(41, 56), (87, 56), (43, 80), (85, 80), (40, 68), (88, 68)]), 1.4, color=CHAR_HI)
    for side in (-1, 1):   # horn collars
        col = [(64 + side * 18, 52), (64 + side * 24, 47), (64 + side * 29, 50), (64 + side * 24, 55)]
        p.detail += poly(T(col), GUN) + inkpoly(T(col), 1.3)
    seams(p, [T([(40, 84), (52, 88)]), T([(88, 84), (76, 88)])], 1.3)
    hexlight(p, *T([(64, 51)])[0], 2.2, 1.0 if flare else 0.5, SODIUM, rot=0)   # forehead ember
    spec(p, T([(38, 54), (41, 80)]), 1.4)
    spec(p, T([(44, 50), (60, 46)]), 1.2)
    grin = 2 + max(0, ang) * 0.12
    mouth = [(50, 78), (78, 78), (72, 80 + grin), (56, 80 + grin)]
    p.detail += poly(T(mouth), INK)
    p.glow += poly(T(lerp_pts(mouth, [(64, 79 + grin * 0.5)] * 4, 0.3)), AMBER if flare else SODIUM)
    for k in range(4):   # fangs in the grin
        x = 54 + k * 6.6
        t = [(x - 1.8, 78), (x + 1.8, 78), (x, 81 + grin * 0.4)]
        p.glow += poly(T(t), BONE) + inkpoly(T(t), 0.8)
    return p


def draw(world, i):
    return {"space": mite, "frost": jelly, "verdant": flytrap, "ember": imp}[world](i)
