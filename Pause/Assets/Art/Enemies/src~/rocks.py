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
    """A wide slab of frozen crater rock under a crust of ice spires."""
    lv, gl = FRAMES[i]
    p = Parts()
    for x, top, w in ((30, 16, 14), (52, 6, 16), (84, 20, 13), (104, 34, 10)):
        sp = [(x - w / 2, 50), (x - w * 0.3, top + 12), (x, top), (x + w * 0.3, top + 12), (x + w / 2, 50)]
        cel(p, sp, ICE_HI, ICE, None, sh_off=(w * 0.3, 0), ink_w=2.5)
    O = [(6, 64), (20, 46), (60, 40), (104, 44), (122, 62), (114, 86), (72, 96), (20, 90)]
    cel(p, O, STEEL, STEEL_SH, None, sh_off=(12, 12))
    crust = [(6, 64), (20, 46), (60, 40), (104, 44), (122, 62), (100, 64), (78, 58), (54, 66), (30, 60)]
    cel(p, crust, ICE, ICE_SH, ICE_HI, sh_off=(6, 6), ink_w=3, detail=True)
    x, y, r = 64, 78, 12
    c = ngon(x, y, r, 6, 15, 1.3, 0.7)
    p.detail += poly(c, ICE_SH) + inkpoly(c, 2.5)
    p.glow += poly(ngon(x, y, r * 0.55, 6, 15, 1.3, 0.7), CYAN if lv >= 0.4 else TEAL_SH)
    p.detail += line([(22, 76), (32, 84)], 1.8) + line([(100, 72), (108, 82)], 1.8)
    if gl:
        glint(p, 52, 10, 8 * lv)
    if i == 5:
        p.glow += spark(116, 92, 6, CYAN, 0)
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
    """A mossy rock sprouting three angular spore caps whose gills glow and
    puff."""
    lv, gl = FRAMES[i]
    p = Parts()
    O = [(20, 80), (40, 66), (88, 64), (110, 80), (102, 108), (64, 118), (26, 106)]
    cel(p, O, BARK, BARK_SH, BARK_HI, sh_off=(12, 10))
    p.ink += line([(44, 90), (60, 100), (58, 112)], 1.8) + line([(86, 86), (94, 100)], 1.8)
    gill = BILE_LIGHT if lv >= 0.4 else BILE_SH
    for cx, cy, w, h, rot in ((36, 50, 40, 20, -18), (74, 30, 50, 24, 6), (104, 58, 28, 16, 24)):
        stalk = xf([(cx - 5, cy + 4), (cx + 5, cy + 4), (cx + 6, cy + 24), (cx - 6, cy + 24)], cx, cy, rot=rot)
        cel(p, stalk, BILE_HI, BILE, None, sh_off=(3, 0), ink_w=2.5, detail=True)
        cap = xf([(cx - w / 2, cy + 4), (cx - w * 0.4, cy - h * 0.6), (cx, cy - h), (cx + w * 0.4, cy - h * 0.6), (cx + w / 2, cy + 4)], cx, cy, rot=rot)
        cel(p, cap, MOSS, BILE_SH, BILE_HI, sh_off=(5, 4), ink_w=3, detail=True)
        g = xf([(cx - w / 2 + 3, cy + 4), (cx + w / 2 - 3, cy + 4), (cx + w * 0.3, cy + 8), (cx - w * 0.3, cy + 8)], cx, cy, rot=rot)
        p.detail += poly(g, INK)
        p.glow += poly(lerp_pts(g, [xf([(cx, cy + 6)], cx, cy, rot=rot)[0]] * 4, 0.25), gill)
        if lv >= 1:
            p.glow_back += halo(g, BILE_LIGHT, 0.7)
        for k in (-1, 1):
            dot = xf([(cx + k * w * 0.18, cy - h * 0.45)], cx, cy, rot=rot)[0]
            p.detail += poly(ngon(dot[0], dot[1], 3, 5, 0), BILE_HI) + inkpoly(ngon(dot[0], dot[1], 3, 5, 0), 1.2)
    if i == 5:   # spore puff
        for x, y, r in ((18, 24, 6), (56, 4, 5), (110, 20, 6), (122, 44, 4)):
            puff = ngon(x, y, r, 5, 10)
            p.glow += poly(puff, BILE_LIGHT) + inkpoly(puff, 1.4)
    if gl:
        glint(p, 60, 14, 6 * lv)
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
    """A volcanic bomb: a twisted basalt spindle flung out of an eruption,
    split by magma cracks that pulse."""
    lv, gl = FRAMES[i]
    p = Parts()
    O = [(10, 116), (26, 84), (44, 58), (72, 32), (110, 8), (102, 40), (84, 72), (54, 100)]
    cel(p, O, CHAR, CHAR_SH, CHAR_HI, sh_off=(8, 8))
    p.ink += line([(26, 84), (40, 92)], 2) + line([(72, 32), (84, 44)], 2)
    lava_cracks(p, [[(24, 100), (40, 82), (52, 80), (66, 60), (80, 52), (96, 26)],
                    [(52, 80), (58, 92)], [(66, 60), (60, 50)]], lv)
    rim(p, [(12, 112), (27, 82), (45, 56), (72, 30)], lv, SODIUM)
    if i == 5:
        for x, y in ((14, 70), (40, 22), (118, 64), (100, 104)):
            e = ngon(x, y, 4, 4, 45)
            p.glow += poly(e, AMBER) + inkpoly(e, 1.3)
    return p

def ember_cinder(i):
    """Three basalt columns fused together; magma glows in the seams and
    through a split in the tallest."""
    lv, gl = FRAMES[i]
    p = Parts()
    hot = (AMBER if lv >= 1 else SODIUM) if lv >= 0.4 else SODIUM_SH
    for x0, x1, top, bot in ((14, 48, 44, 110), (80, 114, 56, 116), (44, 84, 12, 106)):
        mid = (x0 + x1) / 2
        col = [(x0, top + 8), (mid, top), (x1, top + 8), (x1, bot - 6), (mid, bot), (x0, bot - 6)]
        cel(p, col, CHAR, CHAR_SH, CHAR_HI, sh_off=(8, 4), ink_w=3.5, detail=True)
        face = [(x0, top + 8), (mid, top), (x1, top + 8), (mid, top + 16)]
        p.detail += poly(face, CHAR_HI) + inkpoly(face, 2)
        p.detail += line([(mid, top + 16), (mid, bot)], 1.8)
    for seam in ([(46, 60), (46, 104)], [(82, 70), (82, 108)]):
        p.glow += line(seam, 2.2, hot)
    win = [(56, 50), (72, 48), (74, 70), (64, 80), (54, 70)]
    p.detail += poly(win, INK)
    p.glow += poly(lerp_pts(win, [(64, 63)] * 5, 0.25), hot)
    if lv >= 1:
        p.glow_back += halo(win, SODIUM, 0.7)
        p.glow += poly(lerp_pts(win, [(64, 63)] * 5, 0.65), BONE)
    return p

def ember_obsidian(i):
    """Volcanic glass: a tall violet blade with a second shard forking off
    it, a sodium rim and one lava vein."""
    lv, gl = FRAMES[i]
    p = Parts()
    side = [(70, 66), (116, 30), (112, 64), (84, 104)]
    cel(p, side, BRUISE, OBSIDIAN, BRUISE_HI, sh_off=(8, 4), ink_w=3.5)
    p.ink += line([(70, 66), (112, 46)], 1.8)
    O = [(46, 4), (68, 34), (78, 80), (62, 124), (36, 116), (22, 72), (30, 28)]
    cel(p, O, BRUISE, OBSIDIAN, BRUISE_HI, sh_off=(14, 6), detail=True)
    p.detail += poly([(46, 4), (68, 34), (78, 80), (62, 124), (48, 70)], OBSIDIAN)
    p.detail += line([(46, 4), (48, 70), (62, 124)], 2) + line([(22, 72), (48, 70), (78, 80)], 2)
    p.detail += inkpoly(O, 4)
    p.detail += poly([(42, 12), (30, 34), (26, 66), (34, 36)], BONE)
    rim(p, [(24, 76), (32, 30), (44, 8)], lv, SODIUM)
    lava_cracks(p, [[(48, 70), (40, 92), (46, 108)]], lv)
    if gl:
        glint(p, 36, 26, 7 * lv)
    return p


DRAW = {
    "space": {"crater": space_crater, "cluster": space_cluster, "dark": space_dark},
    "frost": {"shard": frost_shard, "chunk": frost_chunk, "rime": frost_rime},
    "verdant": {"pod": verdant_pod, "spore": verdant_spore, "knot": verdant_knot},
    "ember": {"magma": ember_magma, "cinder": ember_cinder, "obsidian": ember_obsidian},
}
