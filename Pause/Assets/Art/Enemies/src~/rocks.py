"""Rock / obstacle hazards: three or four per world, tag Astr. Most tumble in
game (AsteroidSpin), so their flipbooks animate the light on them -- rim
glints, pulsing cores, cracks and vents -- not the rotation.

The FLOATING rocks (one or two per world, EnemyDef.floating) are chunks of
that world's ground drifting upright: a body of the world's rock under its
own cap (Verdant grass, Space regolith, Frost snow, Ember crust) with the
world's features hanging off it. They sway instead of tumbling, so their
frames also draw a small bob and wobble (FLOAT) on top of the light cycle.
The template is the first-pass grass-capped Spore Rock (commit 18b5e5f):
chunky angular body, a heavy ink contour, one hard shadow plane cut on the
lower right, one highlight edge on the lit left side, a cap clearly lighter
than the rock, and hard-edged hex lights with a small hard bloom."""
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


# ------------------------------------------------------- floating rocks ----
# Drawn bob / wobble per frame: (dy u, rotation deg, squash). Frame 0 (the key
# pose, the hit flash and the codex icon) is always the rest pose.
FLOAT = [
    (0.0, 0.0, 0.0),     # 0 key: at rest
    (1.5, 1.2, 0.0),     # 1 dips (anticipation)
    (-2.5, -1.5, 0.0),   # 2 rises on the flare
    (-1.0, -0.5, 0.0),   # 3 settles
    (2.5, 0.0, 0.05),    # 4 tell: sinks and squashes (inhale)
    (-2.5, 1.5, -0.04),  # 5 tell: pops up and stretches
]
CONTOUR = 5.2   # heavier than the 4 u enemy contour: the floating rocks read first


def bob(p, i, amp=1.0):
    dy, rot, sq = FLOAT[i]
    t = []
    if dy or rot:
        t.append(f"translate(0 {dy * amp:.2f}) rotate({rot * amp:.2f} 64 64)")
    if sq:
        t.append(f"translate(64 64) scale({1 + sq:.3f} {1 - sq:.3f}) translate(-64 -64)")
    p.xform = " ".join(t)



# ------------------------------------------------------------------ Space ----
def space_crater(i):
    """Beacon Rock (floating): a chunk of asteroid drifting upright -- a
    pale cratered regolith cap over a keel of dark rock that tapers to a
    point, a survey beacon on a mast blinking magenta, and two loose shards
    bobbing under it on their own beat."""
    lv, gl = FRAMES[i]
    p = Parts()
    bob(p, i)
    # loose shards under the keel, bobbing against the rock
    sdy = -1.2 * FLOAT[i][0]
    for o in ([(20, 94), (32, 88), (38, 98), (28, 106)], [(98, 100), (110, 94), (112, 104), (102, 110)]):
        o = shift(o, 0, sdy)
        cel(p, o, ROCK, ROCK_SH, None, sh_off=(4, 4), ink_w=3)
    # the beacon mast (behind the cap)
    mast = [(84, 34), (89, 34), (91, 13), (88, 13)]
    p.base += poly(mast, GUN_HI) + poly([(87, 34), (89, 34), (91, 13), (89.5, 13)], GUN_SH)
    p.ink += inkpoly(mast, 2)
    O = [(8, 46), (20, 32), (50, 26), (82, 28), (108, 34), (120, 48), (112, 64), (98, 80), (82, 96), (68, 116),
         (56, 100), (40, 84), (22, 68)]
    cid = cel(p, O, ROCK, ROCK_SH, None, sh_off=(6, 6), ink_w=CONTOUR)
    plane(p, cid, [(64, 54), (130, 46), (130, 130), (68, 130), (68, 116), (70, 84)], ROCK_SH)
    p.ink += line([(64, 56), (70, 84), (68, 114)], 2.2) + line([(30, 62), (46, 74), (70, 84)], 2)
    p.highlight += poly([(12, 56), (24, 69), (40, 85), (46, 86), (28, 68)], ROCK_HI)   # lit keel edge
    rim(p, [(26, 73), (41, 88), (55, 102)], lv)
    # regolith cap: paler than the rock, BONE kick on the top edge
    cap = [(8, 46), (20, 32), (50, 26), (82, 28), (108, 34), (120, 48), (112, 56), (100, 50), (86, 58), (70, 52),
           (54, 60), (38, 52), (24, 58), (14, 52)]
    cel(p, cap, ROCK_HI, ROCK, BONE, sh_off=(3, 5), hi_off=(1.5, 3), ink_w=3.6, detail=True)
    for x, y, r in ((34, 41, 9), (66, 37, 11), (98, 43, 6)):   # craters, seen from above
        c = ngon(x, y, r, 6, 30, 1, 0.5)
        p.detail += poly(c, ROCK) + poly([c[3], c[4], c[5], (x, y + r * 0.1)], ROCK_SH) + inkpoly(c, 2)
    hexlight(p, 89.5, 10, 4.2, lv, MAGENTA)
    if gl:
        glint(p, 22, 30, 7 * lv)
        teal_glint(p, 60, 104, 3.5 * lv)
    if i == 5:
        p.glow += spark(68, 122, 5, AMBER, 20)
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
    """A slanted wedge of coal: big hard planes meeting at one ridge, a
    buried magenta eye that blinks. Cut on the diagonal (not a round
    boulder) so it never reads as the Verdant spore rock's octagon."""
    lv, gl = FRAMES[i]
    p = Parts()
    O = [(34, 12), (98, 4), (122, 28), (108, 66), (76, 102), (40, 124), (12, 108), (16, 66)]
    cel(p, O, ROCK, ROCK_SH, ROCK_HI, sh_off=(16, 14))
    # coal facets: big hard planes
    p.shadow += poly([(66, 58), (122, 28), (108, 66), (76, 102)], ROCK_SH)
    p.ink += line([(34, 12), (66, 58), (98, 4)], 2) + line([(66, 58), (122, 28)], 2) + line([(66, 58), (76, 102)], 2)
    p.ink += line([(66, 58), (16, 66)], 2) + line([(66, 58), (40, 124)], 2)
    rim(p, [(17, 64), (34, 15), (96, 7)], lv)
    rim(p, [(14, 104), (18, 72)], lv)
    # a buried mineral eye that blinks
    core(p, 78, 74, 6, MAGENTA, lv, n=6, rot=0)
    if gl:
        glint(p, 42, 22, 7 * lv)
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
    """Frozen Chunk (floating): a block of frozen bedrock drifting upright
    under a heavy snow cap, a fringe of icicles hanging off its underside and
    a frost crystal glowing cyan in its face."""
    lv, gl = FRAMES[i]
    p = Parts()
    bob(p, i)
    # icicles first: the rock overlaps their roots
    icicles = ((30, 58, 24, 10), (45, 66, 42, 12), (62, 72, 50, 13), (79, 70, 34, 11), (95, 62, 38, 11),
               (108, 52, 18, 8))
    for x, y0, L, w in icicles:
        ic = [(x - w / 2, y0), (x + w / 2, y0), (x + w * 0.2, y0 + L * 0.72), (x, y0 + L)]
        p.base += poly(ic, ICE) + poly([(x + w * 0.05, y0), (x + w / 2, y0), (x + w * 0.2, y0 + L * 0.72), (x, y0 + L)], ICE_SH)
        p.ink += inkpoly(ic, 2.6)
    O = [(16, 40), (32, 28), (64, 24), (98, 28), (114, 40), (108, 56), (92, 68), (64, 76), (36, 70), (22, 58)]
    cid = cel(p, O, STEEL, STEEL_SH, None, sh_off=(6, 6), ink_w=CONTOUR)
    plane(p, cid, [(74, 44), (130, 34), (130, 100), (60, 100), (64, 76), (70, 60)], STEEL_SH)
    p.ink += line([(74, 44), (70, 60), (64, 76)], 2.2)
    p.highlight += poly([(17, 44), (23, 57), (36, 69), (40, 66), (26, 54)], STEEL_HI, f'clip-path="url(#{cid})"')
    # frost crystal set in the face: the cyan heart, plus a hairline crack
    p.ink += line([(36, 58), (44, 62), (50, 58)], 1.6)
    hexlight(p, 62, 58, 7.5, lv, CYAN, rot=0)
    hexlight(p, 88, 54, 3.6, lv * 0.8, CYAN, rot=0)
    # snow cap: overhangs the rock, pale ICE_HI with a BONE top edge
    cap = [(8, 40), (20, 24), (44, 16), (86, 15), (108, 22), (120, 38), (112, 46), (102, 40), (92, 50), (80, 42),
           (66, 52), (52, 42), (40, 50), (28, 42), (16, 48)]
    cel(p, cap, ICE_HI, ICE, BONE, sh_off=(3, 6), hi_off=(1.5, 3.5), ink_w=3.6, detail=True)
    p.detail += line([(52, 24), (64, 28), (76, 24)], 1.4) + line([(92, 28), (100, 32)], 1.4)   # wind-cut drifts
    if gl:
        glint(p, 26, 26, 7.5 * lv)
        teal_glint(p, 62, 118, 3.5 * lv)   # light catching the longest icicle tip
    if i == 4:   # the icicles tick with frost: BONE kicks down their lit edges
        for x, y0, L, w in icicles:
            p.glow += line([(x - w * 0.3, y0 + 3), (x - w * 0.06, y0 + L * 0.7)], 1.4, BONE)
    if i == 5:
        p.glow += spark(112, 86, 6, CYAN, 0) + spark(14, 74, 4, CYAN, 20)
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
    """Spore Rock (floating): the first-pass grass-capped rock (commit
    18b5e5f) pushed further into the Akira look -- a chunky octagonal chunk
    of earth under a bright grass cap, lime spore vents set in it as hard hex
    lights that pulse and puff, two roots trailing underneath."""
    lv, gl = FRAMES[i]
    p = Parts()
    bob(p, i)
    O = [(36, 18), (84, 14), (114, 44), (110, 88), (82, 114), (40, 112), (14, 84), (16, 42)]
    # roots trailing under the rock (drawn first: the body covers their tops)
    for r in ([(46, 108), (56, 110), (50, 122)], [(72, 110), (80, 108), (80, 119)]):
        p.base += poly(r, BARK_SH) + inkpoly(r, 3)
    cid = cel(p, O, BARK, BARK_SH, None, sh_off=(6, 6), ink_w=CONTOUR)
    # one hard shadow plane cut across the lower right, with its facet edge inked
    plane(p, cid, [(98, 48), (130, 40), (130, 130), (36, 130), (52, 106), (74, 92), (90, 72)], BARK_SH)
    p.ink += line([(52, 106), (74, 92), (90, 72), (98, 50)], 2.2)
    # one highlight edge down the lit left side, and a sodium kick of city glow
    p.highlight += poly([(16, 48), (14, 83), (27, 97), (22, 82), (22, 54)], BARK_HI, f'clip-path="url(#{cid})"')
    rim(p, [(18, 89), (32, 104)], lv, SODIUM)
    # the vents: hard hex lights with a small hard bloom
    for x, y, r, rot in ((34, 70, 8, -30), (70, 70, 13, 10), (88, 98, 8, 40), (52, 100, 6, -10)):
        hexlight(p, x, y, r, lv, BILE_LIGHT, rot=30 + rot, sy=0.8)
    # grass cap: brighter than the rock (BILE over BARK), BILE_HI top edge,
    # a hard BILE_SH shadow under its ragged lip, and two angular tufts
    for t in ([(42, 19), (45, 7), (49, 15), (53, 9), (56, 18)], [(18, 40), (14, 30), (22, 33), (22, 25), (29, 31)]):
        p.detail += poly(t, BILE) + inkpoly(t, 2.2)
    moss = [(36, 18), (84, 14), (114, 44), (106, 52), (98, 45), (88, 56), (78, 44), (66, 54), (56, 45), (44, 55),
            (34, 46), (22, 55), (16, 42)]
    cel(p, moss, BILE, BILE_SH, BILE_HI, sh_off=(3, 6), hi_off=(1.5, 4.5), ink_w=3.6, detail=True)
    p.detail += line([(60, 24), (70, 30)], 1.4) + line([(92, 26), (100, 34)], 1.4)   # blade strokes
    if i == 4:   # inhale: the puff drawn back into the vents as hard specks
        for x, y in ((40, 62), (78, 60), (60, 64), (94, 90)):
            p.glow += poly(ngon(x, y, 2.2, 4, 45), BILE_LIGHT)
    if i == 5:   # spore puff: angular cels popping out of the vents
        for x, y, s in ((30, 58, 7), (90, 36, 8), (70, 121, 6), (104, 108, 5), (9, 70, 5)):
            puff = ngon(x, y, s, 5, 10)
            p.detail += poly(puff, BILE_LIGHT) + inkpoly(puff, 1.8)
    if gl:
        glint(p, 34, 28, 6.5 * lv)
        teal_glint(p, 16, 96, 3.2 * lv)
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


def verdant_vine(i):
    """Vine Rock (floating): a smaller wedge of earth wearing an angular
    bush, three vines dangling under it with lime buds at their tips that
    glow and swing. Its tell: the buds flare and the vines whip."""
    lv, gl = FRAMES[i]
    p = Parts()
    bob(p, i, 0.8)
    swing = (0, 2, -3, -1, 3, -4)[i]
    vines = (
        ([(38, 78), (34, 92), (40, 104), (36 + swing * 0.6, 116)], 5),
        ([(64, 86), (68, 100), (62 + swing, 112), (66 + swing * 1.4, 124)], 6),
        ([(92, 74), (96, 86), (92 + swing * 0.7, 98)], 4.5),
    )
    for pts_, r in vines:
        p.base += line(pts_, 5.6) + line(pts_, 2.6, BILE_SH)
    for (pts_, r), side in zip(vines, (1, -1, 1)):
        x, y = pts_[1]
        leaf(p, x, y, 12, 120 * side)
    for pts_, r in vines:
        x, y = pts_[-1]
        hexlight(p, x, y + 2, r, lv, BILE_LIGHT, rot=0)
    O = [(22, 52), (40, 40), (88, 38), (108, 50), (100, 70), (78, 84), (52, 88), (30, 76)]
    cid = cel(p, O, BARK, BARK_SH, None, sh_off=(5, 5), ink_w=CONTOUR)
    plane(p, cid, [(70, 44), (130, 30), (130, 110), (60, 110), (64, 72)], BARK_SH)
    p.ink += line([(70, 46), (64, 72), (58, 88)], 2.2)
    p.highlight += poly([(23, 56), (31, 74), (44, 82), (36, 72), (28, 56)], BARK_HI, f'clip-path="url(#{cid})"')
    rim(p, [(32, 79), (46, 86)], lv, SODIUM)
    # the bush: a crown of hard leaf points, BILE over the dark earth
    bush = [(16, 56), (18, 40), (32, 34), (38, 18), (56, 20), (66, 6), (82, 18), (98, 16), (104, 32), (114, 40),
            (110, 54), (98, 56), (86, 50), (72, 58), (58, 50), (44, 58), (30, 52)]
    cel(p, bush, BILE, BILE_SH, BILE_HI, sh_off=(4, 6), hi_off=(1.5, 4), ink_w=3.6, detail=True)
    for a, b in (((38, 20), (46, 40)), ((66, 8), (66, 38)), ((98, 18), (88, 40))):
        p.detail += line([a, b], 1.4)   # leaf ribs
    hexlight(p, 64, 40, 4, lv * 0.8, BILE_LIGHT, rot=30)   # a bud hiding in the bush
    if i == 5:
        for x, y, s in ((20, 108, 5), (108, 104, 5), (88, 120, 4)):
            puff = ngon(x, y, s, 5, 10)
            p.detail += poly(puff, BILE_LIGHT) + inkpoly(puff, 1.6)
    if gl:
        glint(p, 40, 30, 6.5 * lv)
        teal_glint(p, 24, 80, 3 * lv)
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


def ember_islet(i):
    """Lava Islet (floating): a slab of basalt drifting upright under a cap
    of cooled black crust split by glowing cracks, magma seams running down
    its face and lava dripping off its underside, one drop at a time."""
    lv, gl = FRAMES[i]
    p = Parts()
    bob(p, i)
    hot = (AMBER if lv >= 1 else SODIUM) if lv >= 0.4 else SODIUM_SH
    # the drips: (x, top, length, detached drop y or None) per frame -- a
    # bead forms, stretches, lets go and falls while the next one forms
    drips = [
        [(66, 96, 9, None), (34, 80, 6, None)],
        [(66, 96, 14, None), (34, 80, 8, None)],
        [(66, 96, 5, 116), (34, 80, 11, None)],
        [(66, 96, 8, 124), (34, 80, 5, 98)],
        [(66, 96, 16, None), (34, 80, 12, None)],
        [(66, 96, 6, 112), (34, 80, 6, 96)],
    ][i]
    for x, top, L, drop in drips:
        bead = [(x - 4, top - 2), (x + 4, top - 2), (x + 3, top + L - 3), (x, top + L), (x - 3, top + L - 3)]
        p.glow += poly(bead, hot) + poly(lerp_pts(bead, [(x, top + L * 0.5)] * 5, 0.55), BONE if lv >= 1 else AMBER)
        if drop is not None:
            d = [(x, drop - 5), (x + 3.5, drop + 1), (x, drop + 5), (x - 3.5, drop + 1)]
            p.glow += poly(d, hot) + poly(lerp_pts(d, [(x, drop + 1)] * 4, 0.5), AMBER)
    O = [(10, 42), (26, 26), (60, 18), (98, 24), (118, 42), (114, 62), (100, 78), (80, 86), (68, 98), (54, 88),
         (34, 82), (18, 66)]
    cid = cel(p, O, CHAR, CHAR_SH, None, sh_off=(6, 6), ink_w=CONTOUR)
    plane(p, cid, [(80, 44), (130, 34), (130, 120), (60, 120), (68, 98), (76, 70)], CHAR_SH)
    p.ink += line([(80, 46), (76, 70), (68, 96)], 2.2)
    p.highlight += poly([(11, 46), (19, 64), (34, 80), (30, 70), (18, 52)], CHAR_HI, f'clip-path="url(#{cid})"')
    lava_cracks(p, [[(30, 54), (36, 64), (32, 76)], [(58, 58), (62, 72), (56, 84), (64, 94)], [(96, 50), (92, 64), (98, 74)]], lv)
    # the crust cap: cooled black rock over the paler basalt, cracked open
    cap = [(10, 42), (26, 26), (60, 18), (98, 24), (118, 42), (110, 50), (98, 44), (86, 54), (72, 46), (58, 56),
           (46, 46), (32, 56), (20, 48)]
    cel(p, cap, GUN, GUN_SH, CHAR_HI, sh_off=(3, 5), hi_off=(1.5, 3.5), ink_w=3.6, detail=True)
    for c in ([(30, 32), (40, 38), (38, 46)], [(56, 24), (62, 34), (74, 36), (80, 30)], [(92, 30), (100, 38)]):
        p.detail += line(c, 4.2)
        p.glow += line(c, 2, hot)
    hexlight(p, 64, 66, 5, lv, SODIUM, rot=0)   # a vent in the face where the seams meet
    if lv >= 1:
        p.glow_back += line([(58, 58), (62, 72), (56, 84)], 9, SODIUM, 'opacity="0.6" filter="url(#glowS)"')
    if gl:
        glint(p, 24, 30, 7 * lv)
    if i == 5:
        for x, y in ((12, 92), (110, 92), (96, 108), (22, 14)):
            e = ngon(x, y, 4, 4, 45)
            p.glow += poly(e, AMBER) + inkpoly(e, 1.3)
    return p


DRAW = {
    "space": {"crater": space_crater, "cluster": space_cluster, "dark": space_dark},
    "frost": {"shard": frost_shard, "chunk": frost_chunk, "rime": frost_rime},
    "verdant": {"pod": verdant_pod, "spore": verdant_spore, "knot": verdant_knot, "vine": verdant_vine},
    "ember": {"magma": ember_magma, "cinder": ember_cinder, "obsidian": ember_obsidian, "islet": ember_islet},
}

# The floating rocks (drawn upright with a bob; they sway in game instead of
# tumbling). Mirrored by EnemyDef.floating in EnemyRoster.cs.
FLOATING = ("space_rock_crater", "frost_rock_chunk", "verdant_rock_spore", "verdant_rock_vine", "ember_rock_islet")
