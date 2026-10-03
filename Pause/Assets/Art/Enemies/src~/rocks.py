"""Rock / obstacle hazards: three per world, tag Astr. They tumble in game
(AsteroidSpin), so the flipbook animates the light on them -- rim glints,
pulsing cores, cracks and vents -- not the rotation."""
from common import *

# (light level, glint) -- 0..3 idle loop, 4..5 accent tell
FRAMES = [
    (0.5, False),   # 0 key
    (0.25, False),  # 1 dim (anticipation)
    (1.0, True),    # 2 flare + glint
    (0.6, False),   # 3 settle
    (0.15, False),  # 4 tell: everything sucks in
    (1.5, True),    # 5 tell: pop
]
IDLE_TICKS = [5, 3, 2, 3]
TELL_TICKS = [2, 3]


def glint(p, x, y, r):
    p.glow += spark(x, y, r, BONE, 0)


def rim(p, pts_, level, col=AMBER):
    """The AMBER city-glow rim kick on the lit edge (hazards only)."""
    c = col if level >= 0.4 else DIM.get(col, col)
    p.highlight += line(pts_, 3.2 if level < 1 else 4.2, c)


def crater(p, x, y, r, rock_sh=ROCK_SH, rock_hi=ROCK_HI):
    c = ngon(x, y, r, 6, 15, 1, 0.8)
    p.base += poly(c, rock_sh)
    p.base += poly([c[1], c[2], c[3], (x + r * 0.3, y + r * 0.1)], rock_hi)
    p.ink += inkpoly(c, 2)


# ------------------------------------------------------------------ Space ----
def space_crater(i):
    lv, gl = FRAMES[i]
    p = Parts()
    O = [(48, 12), (78, 10), (102, 26), (116, 52), (110, 84), (90, 110), (56, 116), (26, 102), (12, 74), (18, 40)]
    cel(p, O, ROCK, ROCK_SH, ROCK_HI, sh_off=(12, 12))
    for (x, y), r in (((46, 58), 13), ((80, 42), 9), ((76, 84), 11), ((38, 88), 6)):
        crater(p, x, y, r)
    rim(p, [(14, 72), (19, 41), (48, 14), (60, 13)], lv)
    p.ink += line([(60, 26), (66, 40), (58, 46)], 1.6) + line([(98, 64), (90, 70)], 1.6)
    if gl:
        glint(p, 28, 34, 7 * lv)
    if i == 5:
        p.glow += spark(100, 98, 6, AMBER, 20)
    return p


def space_cluster(i):
    lv, gl = FRAMES[i]
    p = Parts()
    a = jag(46, 54, [30, 34, 28, 32, 30, 26, 32, 28], 10)
    b = jag(84, 50, [26, 24, 28, 22, 26, 24, 22], 30)
    c = jag(66, 88, [26, 30, 24, 28, 26, 24], 0)
    for o in (b, c, a):
        cel(p, o, ROCK, ROCK_SH, ROCK_HI, sh_off=(9, 9), detail=True)
    for (x, y), r in (((40, 50), 8), ((86, 52), 6), ((66, 90), 7)):
        c_ = ngon(x, y, r, 6, 15, 1, 0.8)
        p.detail += poly(c_, ROCK_SH) + inkpoly(c_, 2)
    # weld seams: the three boulders are fused, with a magenta mineral seam
    seam = [(56, 68), (66, 62), (76, 70)]
    p.detail += line(seam, 5)
    p.glow += line(seam, 2.2, MAGENTA if lv >= 0.4 else MAGENTA_SH)
    if lv >= 1:
        p.glow_back += line(seam, 8, MAGENTA, 'opacity="0.6" filter="url(#glowS)"')
    p.detail += line([(18, 56), (24, 34), (40, 24)], 3.2, AMBER if lv >= 0.4 else SODIUM_SH)
    if gl:
        glint(p, 30, 30, 7 * lv)
    return p


def space_dark(i):
    lv, gl = FRAMES[i]
    p = Parts()
    O = [(40, 14), (86, 8), (116, 38), (108, 76), (118, 98), (80, 118), (40, 112), (14, 86), (22, 56), (12, 34)]
    cel(p, O, ROCK, ROCK_SH, ROCK_HI, sh_off=(18, 16))
    # coal facets: big hard planes
    p.shadow += poly([(64, 60), (108, 76), (118, 98), (80, 118)], ROCK_SH)
    p.ink += line([(40, 14), (64, 60), (86, 8)], 2) + line([(64, 60), (22, 56)], 2) + line([(64, 60), (80, 118)], 2)
    p.ink += line([(64, 60), (108, 76)], 2)
    rim(p, [(14, 34), (40, 16), (84, 10)], lv)
    rim(p, [(16, 84), (23, 58)], lv)
    # a buried mineral eye that blinks
    core(p, 72, 84, 6, MAGENTA, lv, n=6, rot=0)
    if gl:
        glint(p, 34, 30, 7 * lv)
    return p


# ------------------------------------------------------------------ Frost ----
def crystal(p, x, y, length, width, rot, col=ICE, sh=ICE_SH, hi=ICE_HI, detail=True):
    """A hexagonal ice prism pointing along rot (deg, 0 = up)."""
    w = width / 2
    shape = [(x - w, y), (x - w, y - length * 0.72), (x, y - length), (x + w, y - length * 0.72), (x + w, y)]
    shape = xf(shape, x, y, rot=rot)
    cel(p, shape, col, sh, hi, sh_off=(w * 0.9, w * 0.5), detail=detail)
    p.detail += line(xf([(x, y - 2), (x, y - length + 3)], x, y, rot=rot), 1.4)
    return shape


def frost_shard(i):
    lv, gl = FRAMES[i]
    p = Parts()
    for x, y, L, w, r in ((60, 84, 44, 22, -150), (70, 84, 40, 20, 145), (48, 80, 52, 24, -34),
                          (82, 80, 48, 22, 36), (64, 86, 76, 30, 0)):
        crystal(p, x, y, L, w, r)
    base_ = [(36, 82), (50, 72), (78, 72), (94, 84), (82, 100), (48, 100)]
    cel(p, base_, ICE_SH, INK, ICE, sh_off=(6, 6), ink_w=3.5, detail=True)
    core(p, 64, 54, 6, CYAN, lv, n=6, rot=0)
    if gl:
        glint(p, 58, 22, 8 * lv)
    if i == 4:
        p.glow += line([(54, 40), (60, 52), (56, 62)], 1.6, BONE) + line([(76, 50), (72, 60)], 1.6, BONE)
    return p


def frost_chunk(i):
    lv, gl = FRAMES[i]
    p = Parts()
    O = [(30, 22), (74, 12), (110, 30), (116, 74), (96, 112), (48, 116), (14, 90), (12, 50)]
    cel(p, O, STEEL, STEEL_SH, None, sh_off=(14, 14))
    # frost crust over the top half (ICE), one frozen crater below with ice inside
    crust = [(30, 22), (74, 12), (110, 30), (114, 56), (92, 52), (78, 64), (56, 54), (36, 66), (13, 58), (12, 50)]
    cel(p, crust, ICE, ICE_SH, ICE_HI, sh_off=(6, 8), ink_w=3, detail=True)
    x, y, r = 62, 90, 15
    c = ngon(x, y, r, 6, 15, 1, 0.8)
    p.detail += poly(c, ICE_SH) + inkpoly(c, 2.5)
    p.glow += poly(ngon(x, y, r * 0.55, 6, 15, 1, 0.8), CYAN if lv >= 0.4 else TEAL_SH)
    p.detail += line([(30, 84), (40, 92), (36, 104)], 1.8) + line([(92, 72), (100, 88)], 1.8)
    for x, L in ((40, 12), (60, 18), (98, 10)):   # icicles off the crust edge
        y = 60 if x < 70 else 52
        ic = [(x - 3, y), (x + 3, y), (x, y + L)]
        p.detail += poly(ic, ICE_HI) + inkpoly(ic, 1.5)
    if gl:
        glint(p, 40, 26, 8 * lv)
    if i == 5:
        p.glow += spark(100, 96, 6, CYAN, 0)
    return p


def frost_rime(i):
    lv, gl = FRAMES[i]
    p = Parts()
    O = star(64, 64, 56, 24, 6, 8, [1, 1, 0.8, 1, 0.92, 1, 1.05, 1, 0.86, 1, 0.95, 1])
    cel(p, O, ICE, ICE_SH, ICE_HI, sh_off=(10, 10))
    for k in range(6):   # facet lines from every point to the heart
        p.ink += line([O[k * 2], (64, 64)], 1.6)
    hexo = ngon(64, 64, 16, 6, 0)
    p.ink += poly(hexo, INK)
    core(p, 64, 64, 9, CYAN, lv, n=6, rot=0, socket=False)
    if gl:
        glint(p, 40, 24, 8 * lv)
    return p


# ---------------------------------------------------------------- Verdant ----
def thorn(p, x, y, L, rot, col=MAGENTA):
    t = xf([(x - 4, y), (x, y - L), (x + 4, y)], x, y, rot=rot)
    p.detail += poly(t, col) + poly([t[1], t[2], (x, y)], MAGENTA_SH if col == MAGENTA else BARK_SH) + inkpoly(t, 1.8)


def leaf(p, x, y, L, rot, col=BILE, sh=BILE_SH):
    lf = xf([(x, y), (x - L * 0.3, y - L * 0.5), (x, y - L), (x + L * 0.3, y - L * 0.5)], x, y, rot=rot)
    p.detail += poly(lf, col) + poly([lf[0], lf[2], lf[3]], sh) + inkpoly(lf, 1.8) + line([lf[0], lf[2]], 1.2)


def verdant_pod(i):
    lv, gl = FRAMES[i]
    p = Parts()
    for k in range(8):
        a = k * 45 + 22
        r = math.radians(a - 90)
        thorn(p, 64 + 36 * math.cos(r) * 0.9, 64 + 46 * math.sin(r), 16, a)
    O = [(64, 8), (88, 26), (98, 60), (88, 98), (64, 120), (40, 98), (30, 60), (40, 26)]
    cel(p, O, BARK, BARK_SH, BARK_HI, sh_off=(12, 8), detail=True)
    # leaf armour plates
    for side in (-1, 1):
        plate = [(64, 22), (64 + side * 22, 40), (64 + side * 26, 78), (64 + side * 12, 104), (64, 108)]
        cel(p, plate, BILE, BILE_SH, BILE_HI, sh_off=(6 * side, 6), ink_w=2.5, detail=True)
    # the seam: opens wider as it charges
    w = 2 + 4 * min(lv, 1.5)
    seam = [(64, 26), (64 + w, 64), (64, 102), (64 - w, 64)]
    p.detail += poly(seam, INK)
    p.glow += poly(lerp_pts(seam, [(64, 64)] * 4, 0.12), BILE_LIGHT if lv >= 0.4 else BILE_SH)
    if lv >= 1:
        p.glow_back += halo(seam, BILE_LIGHT, 0.75)
        p.glow += poly([(64, 52), (64 + w * 0.4, 64), (64, 76), (64 - w * 0.4, 64)], BONE)
    if gl:
        glint(p, 48, 28, 6 * lv)
    return p


def verdant_spore(i):
    lv, gl = FRAMES[i]
    p = Parts()
    O = [(36, 18), (84, 14), (114, 44), (110, 88), (82, 114), (40, 112), (14, 84), (16, 42)]
    cel(p, O, BARK, BARK_SH, BARK_HI, sh_off=(14, 14))
    moss = [(36, 18), (84, 14), (114, 44), (100, 50), (80, 40), (60, 50), (40, 42), (22, 52), (16, 42)]
    cel(p, moss, MOSS, BILE_SH, BILE_HI, sh_off=(4, 6), ink_w=3, detail=True)
    vents = [(34, 70, 8, -30), (70, 70, 13, 10), (88, 98, 8, 40), (52, 100, 6, -10)]
    for x, y, r, rot in vents:
        v = xf([(x - r, y), (x - r * 0.4, y - r * 0.7), (x + r * 0.4, y - r * 0.7), (x + r, y), (x + r * 0.4, y + r * 0.7), (x - r * 0.4, y + r * 0.7)], x, y, rot=rot)
        p.detail += poly(v, INK)
        p.glow += poly(lerp_pts(v, [(x, y)] * 6, 0.3), BILE_LIGHT if lv >= 0.4 else BILE_SH)
        if lv >= 1:
            p.glow_back += halo_circle(x, y, r * 1.6, BILE_LIGHT, 0.6)
    if i == 5:   # spore puff: angular cels popping out of the vents
        for x, y, s in ((36, 50, 7), (92, 40, 8), (70, 120, 6), (100, 104, 5)):
            puff = ngon(x, y, s, 5, 10)
            p.glow += poly(puff, BILE_LIGHT) + inkpoly(puff, 1.5)
    if gl:
        glint(p, 34, 28, 6 * lv)
    return p


def verdant_knot(i):
    lv, gl = FRAMES[i]
    p = Parts()
    for a in (0, 60, 120):
        br = xf([(58, 10), (70, 10), (72, 118), (56, 118)], 64, 64, rot=a)
        cel(p, br, BARK, BARK_SH, BARK_HI, sh_off=(5, 5), ink_w=3.5, detail=True)
        for t, side in ((0.2, 1), (0.42, -1), (0.62, 1), (0.82, -1)):
            x, y = 64 + (t - 0.5) * 100 * math.sin(math.radians(a)), 64 - (t - 0.5) * 100 * math.cos(math.radians(a))
            thorn(p, x, y, 11, a + 90 * side)
    hub = ngon(64, 64, 18, 6, 0)
    cel(p, hub, BILE, BILE_SH, BILE_HI, sh_off=(5, 5), ink_w=3, detail=True)
    for x, y, r in ((22, 30, -40), (104, 100, 140), (100, 26, 50)):
        leaf(p, x, y, 16, r)
    core(p, 64, 64, 7, BILE_LIGHT, lv, n=6, rot=0)
    if gl:
        glint(p, 40, 40, 6 * lv)
    return p


# ------------------------------------------------------------------ Ember ----
def lava_cracks(p, cracks, lv):
    for c in cracks:
        p.detail += line(c, 5)
        col = (AMBER if lv >= 1 else SODIUM) if lv >= 0.4 else SODIUM_SH
        p.glow += line(c, 2.4, col)
        if lv >= 1:
            p.glow_back += line(c, 9, SODIUM, 'opacity="0.65" filter="url(#glowS)"')
            p.glow += line(c, 0.9, BONE)


def ember_magma(i):
    lv, gl = FRAMES[i]
    p = Parts()
    O = [(46, 12), (82, 12), (110, 34), (116, 70), (98, 106), (62, 118), (26, 104), (12, 70), (20, 34)]
    cel(p, O, CHAR, CHAR_SH, CHAR_HI, sh_off=(12, 12))
    lava_cracks(p, [[(30, 40), (46, 52), (44, 70), (60, 80), (58, 100)],
                    [(46, 52), (70, 44), (84, 26)],
                    [(60, 80), (86, 76), (100, 90)],
                    [(86, 76), (96, 54)]], lv)
    rim(p, [(14, 68), (21, 36), (46, 14)], lv, SODIUM)
    if i == 5:
        for x, y in ((20, 20), (108, 18), (116, 108)):
            e = ngon(x, y, 4, 4, 45)
            p.glow += poly(e, AMBER) + inkpoly(e, 1.3)
    return p


def ember_cinder(i):
    lv, gl = FRAMES[i]
    p = Parts()
    O = [(24, 22), (100, 16), (116, 52), (108, 100), (70, 116), (20, 106), (10, 62)]
    cel(p, O, CHAR, CHAR_SH, CHAR_HI, sh_off=(16, 12))
    p.ink += line([(24, 22), (52, 50), (100, 16)], 2) + line([(52, 50), (40, 108)], 2) + line([(52, 50), (112, 76)], 2)
    # molten window: the core shows through a split
    win = [(62, 58), (84, 54), (92, 74), (80, 92), (60, 86)]
    p.detail += poly(win, INK)
    col = (AMBER if lv >= 1 else SODIUM) if lv >= 0.4 else SODIUM_SH
    p.glow += poly(lerp_pts(win, [(76, 72)] * 5, 0.2), col)
    if lv >= 0.4:
        p.glow += poly(lerp_pts(win, [(76, 72)] * 5, 0.62), BONE if lv >= 1 else AMBER)
    if lv >= 1:
        p.glow_back += halo(win, SODIUM, 0.7)
    p.detail += inkpoly(win, 3)
    rim(p, [(12, 60), (24, 24), (60, 20)], lv, SODIUM)
    return p


def ember_obsidian(i):
    lv, gl = FRAMES[i]
    p = Parts()
    O = [(62, 4), (86, 36), (100, 80), (78, 124), (46, 118), (28, 74), (40, 30)]
    cel(p, O, BRUISE, OBSIDIAN, BRUISE_HI, sh_off=(14, 6))
    p.shadow += poly([(62, 4), (86, 36), (100, 80), (78, 124), (64, 70)], OBSIDIAN)
    p.ink += line([(62, 4), (64, 70), (78, 124)], 2) + line([(28, 74), (64, 70), (100, 80)], 2)
    p.highlight += poly([(58, 12), (44, 34), (36, 66), (46, 36)], BONE)
    rim(p, [(30, 78), (42, 30), (60, 8)], lv, SODIUM)
    lava_cracks(p, [[(64, 70), (56, 92), (62, 108)]], lv)
    if gl:
        glint(p, 50, 26, 7 * lv)
    return p


DRAW = {
    "space": {"crater": space_crater, "cluster": space_cluster, "dark": space_dark},
    "frost": {"shard": frost_shard, "chunk": frost_chunk, "rime": frost_rime},
    "verdant": {"pod": verdant_pod, "spore": verdant_spore, "knot": verdant_knot},
    "ember": {"magma": ember_magma, "cinder": ember_cinder, "obsidian": ember_obsidian},
}
