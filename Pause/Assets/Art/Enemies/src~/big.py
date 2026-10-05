"""Big enemy, one distinct heavy per world, each with one big glowing core.
Idle: the core breathes, the armour shifts, the small lights blink in turn.
Tell: it pulls in (anticipation), then slams open with the core flaring --
the charge-up.

  space    Bastion        octagonal armoured pod, four plates, twin cannons,
                          a sensor mast pair, bolted panels, warning stripes
  frost    Glacier Golem  an iceberg hulk with a riveted brow, a cyan visor,
                          frost-vent cheeks and a crystal maw
  verdant  Bloom Maw      a spinning carnivorous flower with a toothed,
                          glowing maw that opens and snaps shut
  ember    Magma Skull    a horned basalt skull bolted together, temple vents
                          and a furnace jaw behind a toothed grille

Detail pass: mecha shading is one hard shadow PLANE per form (cut on the
lower right, plane()), BONE specular kicks on the lit upper-left edges,
panel seams, rivets, grilles, glyph decals and small hex / slit lights with a
hard flat bloom (common.py toolkit)."""
from common import *

CX, CY = 64, 60
CONTOUR = 5
# (core level, plate spread, squash sy) -- 0..3 idle, 4..5 tell
FRAMES = [
    (0.6, 0, 1.00),    # 0 key
    (0.3, -2, 0.96),   # 1 anticipation
    (1.0, 3, 1.04),    # 2 pulse
    (0.6, 1, 1.00),    # 3 settle
    (0.1, -5, 0.92),   # 4 tell: clamp shut
    (1.6, 10, 1.06),   # 5 tell: slam open, core flare
]
IDLE_TICKS = [6, 2, 3, 3]
TELL_TICKS = [3, 4]
# two small lights blinking out of step: (a, b) level per frame
BLINK = [(0.6, 0.2), (0.2, 0.6), (1.0, 0.6), (0.6, 0.2), (0.1, 0.1), (1.5, 1.5)]


def faceted(p, outline, base, sh, hi, cut, ink_w=CONTOUR, sh_off=(4, 4), detail=False):
    """A cel form shaded the mecha way: a thin offset crescent for the rim
    plus ONE hard shadow plane (cut, clipped to the form), same shadow tone,
    then the INK contour on top."""
    cid = cel(p, outline, base, sh, hi, sh_off=sh_off, ink_w=0, detail=detail)
    if detail:
        p.detail += plane_svg(cid, cut, sh) + inkpoly(outline, ink_w)
    else:
        plane(p, cid, cut, sh)
        p.ink += inkpoly(outline, ink_w)
    return cid


def inset(pts_, c, t):
    return lerp_pts(pts_, [c] * len(pts_), t)


# ------------------------------------------------------------------ Space ----
def bastion(i):
    """Space: the armoured pod."""
    hull, hull_sh, hull_hi = STEEL, STEEL_SH, STEEL_HI
    plate, plate_sh, plate_hi, light = BRUISE, BRUISE_SH, BRUISE_HI, MAGENTA
    lv, spread, sy = FRAMES[i]
    ba, bb = BLINK[i]
    p = Parts()
    S = lambda pts_: xf(pts_, CX, CY, 1 / sy ** 0.5, sy)

    # sensor mast pair on top (behind the hub), tips blink out of step
    for x, rot, l in ((50, -24, 1), (78, 24, 0)):
        root = S([(x, 32)])[0]
        antenna(p, root[0], root[1], 22 + max(0, spread) * 0.6, rot, ba if l else bb, light, tip=2.6, layer="base")

    # twin cannon prongs pointing at the player: ribbed barrels, vented
    for x in (46, 82):
        side = -1 if x < 64 else 1
        prong = S([(x - 8, 80), (x + 8, 80), (x + 6 + side * 2, 112), (x, 121), (x - 6 + side * 2, 112)])
        faceted(p, prong, plate, plate_sh, plate_hi, S([(x + 1, 70), (x + 20, 70), (x + 20, 130), (x + 1, 130)]), ink_w=3.6)
        for y in (90, 98):
            p.ink += line(S([(x - 7, y), (x + 7, y)]), 1.6)
        grille(p, *S([(x, 104)])[0], 7, 5, 2, layer="ink")
        mouth = S([(x - 3.5, 112), (x + 3.5, 112), (x, 118)])
        p.ink += inkpoly(mouth, 2.4)
        p.glow += poly(mouth, light if lv >= 0.4 else DIM[light])
        if lv >= 1.5:
            p.glow += poly(star(*S([(x, 120)])[0], 13, 2.5, 4, 0), light, 'opacity="0.7"') + spark(*S([(x, 120)])[0], 7, BONE)

    hub = S(ngon(CX, CY, 38, 8, 22.5))
    faceted(p, hub, hull, hull_sh, hull_hi, S([(CX + 6, CY - 60), (CX + 80, CY - 60), (CX + 80, CY + 80), (CX - 60, CY + 80), (CX - 14, CY + 16)]))
    # BONE kick along the lit upper-left edges
    spec(p, inset([hub[5], hub[6], hub[7]], (CX, CY), 0.1), 1.8, layer="highlight")
    ring = S(ngon(CX, CY, 24, 8, 22.5))
    p.ink += inkpoly(ring, 2.2)
    for k in range(8):   # panel seams hub -> ring, bolts at the ring corners
        if k % 2:
            p.ink += line([hub[k], ring[k]], 1.6)
    rivets(p, inset(ring, (CX, CY), -0.18), 1.5, layer="ink")
    rivets(p, [inset([hub[k]], (CX, CY), 0.12)[0] for k in (1, 3, 5, 7)], 1.7, layer="ink")
    # unit markings: a glyph decal upper left, warning stripes on the chin
    glyph(p, *S([(CX - 19, CY - 20)])[0], 2.3, seed=2, layer="ink")
    stripes(p, *S([(CX, CY + 31)])[0], 18, 4.5, n=3, layer="ink")
    # side sensors
    hexlight(p, *S([(CX - 30, CY + 2)])[0], 2.6, ba, light, rot=0)
    hexlight(p, *S([(CX + 30, CY + 2)])[0], 2.6, bb, light, rot=0)

    # four armour plates on the diagonals; spread pushes them out
    for k, a in enumerate((-135, -45, 45, 135)):
        r = math.radians(a)
        d = 36 + spread
        px, py = CX + d * math.cos(r), CY + d * math.sin(r) * sy
        pl = chamfer_rect(px, py, 32, 17, 5, rot=a + 90)
        cut = xf([(px, py - 20), (px + 30, py - 20), (px + 30, py + 20), (px, py + 20)], px, py, rot=a + 90)
        faceted(p, pl, plate, plate_sh, plate_hi, cut[:1] + [cut[1], cut[2]] + [cut[3]], ink_w=3.2, detail=True)
        p.detail += poly(chamfer_rect(px, py, 13, 4.4, 1.2, rot=a + 90), INK)
        p.glow += poly(chamfer_rect(px, py, 10, 2.4, 0.8, rot=a + 90), light if lv >= 0.4 else DIM[light])
        ends = xf([(px - 12, py), (px + 12, py)], px, py, rot=a + 90)
        rivets(p, ends, 1.5)
        p.detail += line(xf([(px - 15, py + 5), (px + 15, py + 5)], px, py, rot=a + 90), 1.2)
        if k in (0, 1):
            spec(p, xf([(px - 12, py - 6.5), (px + 6, py - 6.5)], px, py, rot=a + 90), 1.3)

    core(p, CX, CY, 12, light, lv)
    p.glow += line([(CX - 6, CY), (CX + 6, CY)], 1.6, INK) if lv < 1 else ""
    if i == 5:
        p.glow += speed_lines([16, 26, 102, 112], 92, 24, light, 0.8)
    return p


# ------------------------------------------------------------------ Frost ----
def glacier(i):
    """Frost: a hunched iceberg with crystal shoulders, a riveted brow, one
    cyan visor, frost-vent cheeks and a jagged ice maw that cracks open."""
    lv, spread, sy = FRAMES[i]
    ba, bb = BLINK[i]
    p = Parts()
    S = lambda q: xf(q, 64, 66, 0.92 / sy ** 0.5, 0.92 * sy)
    jaw = max(0, spread) * 1.2 + 4
    # shoulder crystal clusters (behind), each prism with its facet ridge
    for side in (-1, 1):
        for k, (dx, L, a) in enumerate(((30, 30, 30), (40, 22, 60), (22, 24, 10))):
            bx, by = 64 + side * dx, 30
            Lk = L + max(0, spread) + (1.5 if (k + (side > 0) + i) % 3 == 0 else 0)
            cr = xf([(bx - 6, by), (bx - 5, by - L * 0.7), (bx, by - Lk), (bx + 5, by - L * 0.7), (bx + 6, by)], bx, by, rot=side * a)
            cel(p, S(cr), ICE_HI, ICE, None, sh_off=(3, 0), ink_w=2.8)
            p.base += line(S(xf([(bx, by - 2), (bx, by - Lk + 4)], bx, by, rot=side * a)), 1.2, ICE)
    # ice fists hanging at its sides (behind the body), knuckles ridged;
    # they clench up on the anticipation and swing out on the slam
    lift = {1: -3, 4: -5, 5: 4}.get(i, 0)
    for side in (-1, 1):
        fx = 64 + side * 50
        fist = [(fx - 11, 68 + lift), (fx + 11, 68 + lift), (fx + 13, 92 + lift), (fx + 6, 102 + lift),
                (fx - 6, 102 + lift), (fx - 13, 92 + lift)]
        fist = [(64 + (x - 64) * (1.04 if i == 5 else 1), y) for x, y in fist]
        fcid = cel(p, S(fist), ICE, ICE_SH, ICE_HI, sh_off=(3, 3), ink_w=0)
        plane(p, fcid, S([(fx + side * 1, 60 + lift), (fx + side * 20, 60 + lift), (fx + side * 20, 110 + lift), (fx + side * 1, 110 + lift)]), ICE_SH)
        p.ink += inkpoly(S(fist), 4)
        for y in (84, 93):
            p.ink += line(S([(fx - 11, y + lift), (fx + 11, y + lift)]), 1.5)
        rivets(p, S([(fx - 6, 76 + lift), (fx + 6, 76 + lift)]), 1.5, color=ICE_HI, layer="ink")
    body = [(20, 36), (44, 18), (84, 18), (108, 36), (104, 84), (82, 106), (46, 106), (24, 84)]
    faceted(p, S(body), ICE, ICE_SH, ICE_HI, S([(70, 0), (130, 0), (130, 130), (40, 130), (80, 70)]), detail=True)
    # rime chips frozen onto the hide
    for x, y in ((30, 52), (98, 52), (40, 96), (88, 96), (58, 24)):
        c = S(ngon(x, y, 2.6, 4, 45, 1.4, 1))
        p.detail += poly(c, ICE_HI) + inkpoly(c, 1)
    spec(p, S([(24, 38), (45, 22), (70, 22)]), 1.8)
    # facet ridges and frost cracks
    for a, b in (((44, 18), (54, 44)), ((84, 18), (76, 44)), ((20, 36), (40, 56)), ((108, 36), (90, 58)),
                 ((24, 84), (40, 74)), ((104, 84), (90, 74))):
        p.detail += line(S([a, b]), 1.8)
    p.detail += line(S([(30, 92), (36, 98), (34, 104)]), 1.4) + line(S([(96, 90), (92, 98)]), 1.4)
    # carved rune on the forehead
    glyph(p, *S([(64, 30)])[0], 2.6, seed=4, color=ICE_SH, w=1.6)
    # brow plate, bolted with frozen studs
    brow = S([(30, 38), (98, 38), (94, 45), (34, 45)])
    p.detail += poly(brow, ICE_SH) + inkpoly(brow, 2.2)
    rivets(p, S([(38, 41.5), (52, 41.5), (76, 41.5), (90, 41.5)]), 1.6, color=ICE_HI)
    # visor slit
    vx, vy = S([(64, 51)])[0]
    slitlight(p, vx, vy, 44, 5.2 * sy, max(lv, 0.15) if i != 1 else 0.2, CYAN)
    if lv >= 1:
        p.glow += poly(star(vx, vy, 30, 2.4, 2, 90), CYAN, 'opacity="0.55"')
    # frost-vent cheeks and shoulder lamps
    grille(p, *S([(33, 66)])[0], 10, 9, 3, rot=-12, bars=ICE)
    grille(p, *S([(95, 66)])[0], 10, 9, 3, rot=12, bars=ICE)
    hexlight(p, *S([(30, 30)])[0], 2.6, ba, CYAN, rot=0)
    hexlight(p, *S([(98, 30)])[0], 2.6, bb, CYAN, rot=0)
    # the maw: an INK mouth with crystal teeth, wider on the tell
    mouth = [(40, 74), (88, 74), (82, 80 + jaw), (46, 80 + jaw)]
    p.detail += poly(S(mouth), INK)
    core(p, *S([(64, 80 + jaw * 0.5)])[0], 5 + jaw * 0.3, CYAN, lv, n=6, rot=0, socket=False)
    for k in range(5):
        x = 44 + k * 10
        t = [(x - 4, 74), (x + 4, 74), (x, 82)]
        b = [(x + 1, 80 + jaw), (x + 7, 80 + jaw), (x + 4, 72 + jaw)]
        p.detail += poly(S(t), ICE_HI) + inkpoly(S(t), 1.3)
        if k < 4:
            p.detail += poly(S(b), ICE_HI) + inkpoly(S(b), 1.3)
    # icicle beard under the chin
    for x, L in ((52, 8), (64, 12), (76, 8)):
        ic = S([(x - 3, 104), (x + 3, 104), (x, 104 + L + max(0, spread) * 0.4)])
        p.detail += poly(ic, ICE_HI) + inkpoly(ic, 1.6)
    if i == 5:
        for x, y in ((12, 100), (116, 100), (64, 124)):
            p.glow += spark(x, y, 6, ICE_HI)
    return p


# ------------------------------------------------------------------ Ember ----
def magma_skull(i):
    """Ember: a horned basalt skull bolted together. Its furnace jaw glows
    behind a toothed grille and drops open on the tell."""
    lv, spread, sy = FRAMES[i]
    ba, bb = BLINK[i]
    p = Parts()
    S = lambda q: xf(q, 64, 64, 0.86 / sy ** 0.5, sy)
    drop = max(0, spread) * 1.4
    hot = (AMBER if lv >= 1 else SODIUM) if lv >= 0.4 else SODIUM_SH
    # horns sweep up and out, banded like a ram's
    for side in (-1, 1):
        lift = max(0, spread) + (1.5 if i in (2, 3) else 0)
        horn = [(64 + side * 22, 30), (64 + side * 46, 2 - lift), (64 + side * 52, 18), (64 + side * 36, 40)]
        cel(p, S(horn), CHAR_HI, CHAR, None, sh_off=(3 * side, 3), ink_w=3.4)
        for t in (0.3, 0.5, 0.7):
            a = lerp_pts([horn[0], horn[3]], [horn[1], horn[2]], t)
            p.ink += line(S(a), 1.6)
        spec(p, S([(64 + side * 26, 30), (64 + side * 44, 6 - lift)]), 1.4, layer="ink")
    # lower jaw (furnace) behind the skull, bolted at the hinges
    jaw = [(38, 80), (90, 80), (84, 104 + drop), (64, 113 + drop), (44, 104 + drop)]
    faceted(p, S(jaw), GUN, GUN_SH, GUN_HI, S([(66, 70), (100, 70), (100, 130), (66, 130)]), ink_w=4)
    p.ink += poly(S([(46, 84), (82, 84), (78, 100 + drop), (50, 100 + drop)]), INK)
    furnace = S([(50, 86), (78, 86), (75, 98 + drop), (53, 98 + drop)])
    p.glow += poly(furnace, hot)
    if lv >= 1:
        p.glow += poly(inset(furnace, S([(64, 92 + drop * 0.5)])[0], -0.25), SODIUM, 'opacity="0.35"')
        p.glow += poly(lerp_pts(furnace, [S([(64, 92 + drop * 0.5)])[0]] * 4, 0.5), BONE)
    for k in range(4):
        x = 54 + k * 7
        p.glow += line(S([(x, 86), (x, 98 + drop)]), 2, INK)
    rivets(p, S([(42, 92), (86, 92), (58, 106 + drop), (70, 106 + drop)]), 1.7, layer="ink")
    # lower fangs on the jaw lip, pointing up into the furnace
    for x in (50, 78):
        f_ = S([(x - 3, 100 + drop), (x + 3, 100 + drop), (x, 93 + drop)])
        p.ink += poly(f_, BONE) + inkpoly(f_, 1.1)
    # hinge pistons either side of the jaw
    for side in (-1, 1):
        pis = S([(64 + side * 26, 78), (64 + side * 30, 78), (64 + side * 30, 96 + drop * 0.6), (64 + side * 26, 96 + drop * 0.6)])
        p.base += poly(pis, GUN_HI)
        p.ink += inkpoly(pis, 1.8)
    stripes(p, *S([(64, 109 + drop)])[0], 12, 3.6, n=2, layer="ink")
    skull = [(64, 14), (98, 22), (110, 46), (100, 76), (84, 86), (44, 86), (28, 76), (18, 46), (30, 22)]
    faceted(p, S(skull), CHAR, CHAR_SH, CHAR_HI, S([(70, 0), (130, 0), (130, 130), (50, 130), (66, 66), (84, 40)]), detail=True)
    spec(p, S([(22, 46), (32, 25), (60, 17)]), 1.8)
    # plate breaks: brow band and cheek plates, bolted
    seams(p, [S([(24, 40), (44, 36), (64, 38), (84, 36), (104, 40)]), S([(30, 74), (40, 66)]), S([(98, 74), (88, 66)]),
              S([(64, 62), (64, 84)])], 1.6)
    rivets(p, S([(30, 42), (98, 42), (36, 70), (92, 70), (48, 80), (80, 80)]), 1.6)
    # temple vents
    grille(p, *S([(24, 58)])[0], 6, 12, 3, rot=8)
    grille(p, *S([(104, 58)])[0], 6, 12, 3, rot=-8)
    # eye sockets with sodium pupils (hard slits inside), and a cracked brow
    for side in (-1, 1):
        sock = [(64 + side * 10, 46), (64 + side * 30, 42), (64 + side * 28, 60), (64 + side * 14, 60)]
        p.detail += poly(S(sock), INK)
        e = lerp_pts(S(sock), [S([(64 + side * 20, 52)])[0]] * 4, 0.35)
        if lv >= 0.4:
            p.glow += poly(lerp_pts(S(sock), [S([(64 + side * 20, 52)])[0]] * 4, 0.1), SODIUM, f'opacity="{0.25 + 0.15 * min(lv, 1.5):.2f}"')
        p.glow += poly(e, hot)
        ex, ey = S([(64 + side * 20, 52)])[0]
        p.glow += line([(ex, ey - 4), (ex, ey + 4)], 1.6, BONE if lv >= 0.4 else INK)
    for c in ([(64, 16), (60, 28), (66, 36)], [(40, 30), (48, 35)], [(88, 30), (80, 35)]):
        p.detail += line(S(c), 4)
        p.glow += line(S(c), 1.8, hot)
    glyph(p, *S([(44, 74)])[0], 1.9, seed=1, color=SODIUM if lv >= 0.4 else SODIUM_SH, w=1.2)
    p.detail += poly(S([(60, 64), (68, 64), (64, 74)]), INK)   # nose
    # upper teeth along the skull's lip, over the furnace
    teeth(p, S([(46, 86)])[0], S([(82, 86)])[0], 5, 5.5, BONE)
    hexlight(p, *S([(64, 24)])[0], 2.4, ba, SODIUM, rot=0)   # a third eye in the brow
    # cheek magma vents, breathing out of step
    hexlight(p, *S([(30, 66)])[0], 2.2, bb, SODIUM, rot=0)
    hexlight(p, *S([(98, 66)])[0], 2.2, ba, SODIUM, rot=0)
    seams(p, [S([(46, 22), (52, 30)]), S([(82, 22), (76, 30)]), S([(106, 50), (98, 54)]), S([(22, 50), (30, 54)])], 1.3)
    if i == 5:
        for x, y in ((16, 104), (112, 104), (28, 122), (100, 124)):
            e = ngon(x, y, 4, 4, 45)
            p.glow += poly(e, AMBER) + inkpoly(e, 1.3)
    return p


# ---------------------------------------------------------------- Verdant ----
def bloom(i):
    """Verdant: a spinning carnivorous flower.  The silhouette stays rigid
    while its central maw works; it must never read as five flapping wings."""
    lv, _, _ = FRAMES[i]
    ba, bb = BLINK[i]
    p = Parts()
    # The old animation pushed every petal in and out, which made this heavy
    # look like it was flapping.  Advance the whole flower around its centre
    # instead, then reserve the squash/stretch exclusively for the mouth.
    spin = (0, 18, 36, 54, 72, 96)[i]
    openness = (0.35, 0.55, 0.72, 0.5, 0.12, 1.0)[i]
    R = lambda q: xf(q, CX, CY, rot=spin)
    # sepals (back leaves): ribbed, with chitin scales at the root
    for a in (-150, -30, 90):
        lf = xf([(CX, CY), (CX - 11, CY - 24), (CX, CY - 48), (CX + 11, CY - 24)], CX, CY, rot=a + 90)
        lf = R(lf)
        cid = cel(p, lf, BILE, BILE_SH, BILE_HI, sh_off=(4, 4), ink_w=3.2)
        plane(p, cid, [lf[0], lf[2], lf[3]], BILE_SH)
        p.ink += line([lf[0], lf[2]], 1.4)
        for t in (0.35, 0.6):
            a0 = lerp_pts([lf[0]], [lf[2]], t)[0]
            p.ink += line([a0, lerp_pts([lf[1]], [lf[2]], t * 0.7)[0]], 1.1) + line([a0, lerp_pts([lf[3]], [lf[2]], t * 0.7)[0]], 1.1)
    # the maw inside
    maw = R(xf(ngon(CX, CY, 27, 8, 22.5), CX, CY, 1, 0.58 + 0.42 * openness))
    p.base += poly(maw, INK)
    core(p, *R([(CX, CY)])[0], 6 + 6 * openness, BILE_LIGHT, lv, socket=False)
    for k in range(8):   # outer thorn teeth ringing the maw
        a = math.radians(k * 45)
        t = [(CX + 26 * math.cos(a - 0.18), CY + 26 * math.sin(a - 0.18)),
             (CX + (21 - 10 * openness) * math.cos(a), CY + (21 - 10 * openness) * math.sin(a)),
             (CX + 26 * math.cos(a + 0.18), CY + 26 * math.sin(a + 0.18))]
        t = R(t)
        shade_tip = R([(CX + 25 * math.cos(a), CY + 25 * math.sin(a))])[0]
        p.detail += poly(t, BONE) + poly([t[1], t[2], shade_tip], FLASH_SH) + inkpoly(t, 1.4)
    for k in range(8):   # inner ring, offset
        a = math.radians(k * 45 + 22.5)
        t = [(CX + 19 * math.cos(a - 0.2), CY + 19 * math.sin(a - 0.2)),
             (CX + (16 - 7 * openness) * math.cos(a), CY + (16 - 7 * openness) * math.sin(a)),
             (CX + 19 * math.cos(a + 0.2), CY + 19 * math.sin(a + 0.2))]
        t = R(t)
        p.detail += poly(t, BONE) + inkpoly(t, 1.1)
    # stamens: stalks with bud lights, blinking in turn
    for k in range(5):
        a = math.radians(k * 72 - 54)
        tip = (CX + (12 + 8 * openness) * math.cos(a), CY + (12 + 8 * openness) * math.sin(a))
        root, tip = R([(CX, CY), tip])
        p.detail += line([root, tip], 2.6) + line([root, tip], 1.1, BILE_HI)
        hexlight(p, tip[0], tip[1], 2.2, ba if k % 2 else bb, BILE_LIGHT, rot=0)
    # Five rigid petals turn together as one body.  Only the mouth above moves.
    for k in range(5):
        a = k * 72 - 90
        r = math.radians(a)
        d = 27
        px, py = CX + d * math.cos(r), CY + d * math.sin(r)
        L = 35
        P = lambda q: R(xf(q, px, py, rot=a + 90))
        pet = P([(px, py + 10), (px - 16, py - 8), (px - 8, py - L * 0.7), (px, py - L), (px + 8, py - L * 0.7), (px + 16, py - 8)])
        cid = cel(p, pet, BRUISE, BRUISE_SH, BRUISE_HI, sh_off=(4, 4), ink_w=0, detail=True)
        p.detail += plane_svg(cid, P([(px, py + 12), (px, py - L), (px + 20, py - L), (px + 20, py + 12)]), BRUISE_SH)
        p.detail += inkpoly(pet, 3.2)
        # veins: midrib and two side veins
        p.detail += line(P([(px, py + 6), (px, py - L + 8)]), 1.5)
        p.detail += line(P([(px, py - 6), (px - 9, py - 18)]), 1.1) + line(P([(px, py - 12), (px + 8, py - 24)]), 1.1)
        # magenta warning spots and the thorn tip
        for q in ((px - 6, py - 4), (px + 5, py - 14)):
            d_ = P(ngon(q[0], q[1], 2, 4, 45))
            p.detail += poly(d_, MAGENTA_SH) + inkpoly(d_, 0.9)
        p.detail += line(P([(px - 15, py - 8), (px - 8, py - L * 0.7)]), 1.3, BONE)
        tip = P([(px - 3, py - L + 2), (px, py - L - 8), (px + 3, py - L + 2)])
        p.detail += poly(tip, MAGENTA) + inkpoly(tip, 1.4)
        side = P([(px + 12, py - 12), (px + 19, py - 15), (px + 13, py - 6)])
        p.detail += poly(side, MAGENTA) + inkpoly(side, 1.1)
    if i == 5:
        for x, y in ((16, 28), (112, 32), (22, 112), (108, 108)):
            puff = ngon(x, y, 6, 5, 20)
            p.glow += poly(puff, BILE_LIGHT) + inkpoly(puff, 1.4)
    return p


def draw(world, i):
    return {"space": bastion, "frost": glacier, "verdant": bloom, "ember": magma_skull}[world](i)
