"""Big enemy, one distinct heavy per world, each with one big glowing core.
Idle: the core breathes and the armour shifts. Tell: it pulls in
(anticipation), then slams open with the core flaring -- the charge-up.

  space    Bastion        octagonal armoured pod, four plates, twin cannons
  frost    Glacier Golem  an iceberg hulk with a cyan visor and a crystal maw
  verdant  Bloom Maw      a carnivorous bud whose petals fold back
  ember    Magma Skull    a horned basalt skull with a furnace jaw"""
from common import *

CX, CY = 64, 60
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


def bastion(i):
    """Space: the armoured pod."""
    hull, hull_sh, hull_hi = STEEL, STEEL_SH, STEEL_HI
    plate, plate_sh, plate_hi, light = BRUISE, BRUISE_SH, BRUISE_HI, MAGENTA
    lv, spread, sy = FRAMES[i]
    p = Parts()
    S = lambda pts_: xf(pts_, CX, CY, 1 / sy ** 0.5, sy)

    # twin cannon prongs pointing at the player
    for x in (46, 82):
        side = -1 if x < 64 else 1
        prong = S([(x - 7, 82), (x + 7, 82), (x + 5 + side * 2, 112), (x, 120), (x - 5 + side * 2, 112)])
        cel(p, prong, plate, plate_sh, plate_hi, sh_off=(4, 4), ink_w=3.5)
        mouth = S([(x - 3, 112), (x + 3, 112), (x, 117)])
        p.glow += poly(mouth, light if lv >= 0.4 else DIM[light])
        if lv >= 1.5:
            p.glow_back += halo_circle(*S([(x, 118)])[0], 9, light, 0.8)

    hub = S(ngon(CX, CY, 38, 8, 22.5))
    cel(p, hub, hull, hull_sh, hull_hi, sh_off=(12, 12))
    ring = S(ngon(CX, CY, 24, 8, 22.5))
    p.ink += inkpoly(ring, 2)

    # four armour plates on the diagonals; spread pushes them out
    for k, a in enumerate((-135, -45, 45, 135)):
        r = math.radians(a)
        d = 36 + spread
        px, py = CX + d * math.cos(r), CY + d * math.sin(r) * sy
        pl = chamfer_rect(px, py, 30, 16, 5, rot=a + 90)
        cel(p, pl, plate, plate_sh, plate_hi, sh_off=(4, 4), ink_w=3, detail=True)
        p.detail += poly(chamfer_rect(px, py, 12, 4, 1.2, rot=a + 90), INK)
        p.glow += poly(chamfer_rect(px, py, 9, 2.2, 0.8, rot=a + 90), light if lv >= 0.4 else DIM[light])

    core(p, CX, CY, 12, light, lv)
    p.glow += line([(CX - 6, CY), (CX + 6, CY)], 1.6, INK) if lv < 1 else ""
    if i == 5:
        p.glow += speed_lines([20, 30, 98, 108], 96, 22, light, 0.8)
    return p


def glacier(i):
    """Frost: a hunched iceberg with crystal shoulders, one cyan visor and a
    jagged ice maw that cracks open on the tell."""
    lv, spread, sy = FRAMES[i]
    p = Parts()
    S = lambda q: xf(q, 64, 66, 0.92 / sy ** 0.5, 0.92 * sy)
    jaw = max(0, spread) * 1.2 + 4
    # shoulder crystal clusters (behind)
    for side in (-1, 1):
        for k, (dx, L, a) in enumerate(((30, 30, 30), (40, 22, 60), (22, 24, 10))):
            bx, by = 64 + side * dx, 30
            cr = xf([(bx - 6, by), (bx - 5, by - L * 0.7), (bx, by - L - max(0, spread)), (bx + 5, by - L * 0.7), (bx + 6, by)], bx, by, rot=side * a)
            cel(p, S(cr), ICE_HI, ICE, None, sh_off=(3, 0), ink_w=2.5)
    body = [(20, 36), (44, 18), (84, 18), (108, 36), (104, 84), (82, 106), (46, 106), (24, 84)]
    cel(p, S(body), ICE, ICE_SH, ICE_HI, sh_off=(12, 10), detail=True)
    for a, b in (((44, 18), (54, 44)), ((84, 18), (76, 44)), ((20, 36), (40, 56)), ((108, 36), (90, 58))):
        p.detail += line(S([a, b]), 1.8)
    # brow + visor slit
    visor = [(36, 46), (92, 46), (88, 56), (40, 56)]
    p.detail += poly(S(visor), INK)
    vcol = CYAN if lv >= 0.4 else TEAL_SH
    p.glow += poly(S([(40, 48), (88, 48), (85, 54), (43, 54)]), vcol)
    if lv >= 0.4:
        p.glow += poly(S([(58, 49), (70, 49), (68, 53), (60, 53)]), BONE)
    if lv >= 1:
        p.glow_back += halo(S(visor), CYAN, 0.8)
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
    if i == 5:
        for x, y in ((14, 100), (114, 100), (64, 122)):
            p.glow += spark(x, y, 6, ICE_HI)
    return p


def magma_skull(i):
    """Ember: a horned basalt skull. Its furnace jaw glows behind a grille
    and drops open on the tell."""
    lv, spread, sy = FRAMES[i]
    p = Parts()
    S = lambda q: xf(q, 64, 64, 0.86 / sy ** 0.5, sy)
    drop = max(0, spread) * 1.4
    hot = (AMBER if lv >= 1 else SODIUM) if lv >= 0.4 else SODIUM_SH
    # horns sweep up and out
    for side in (-1, 1):
        horn = [(64 + side * 22, 30), (64 + side * 44, 4 - max(0, spread)), (64 + side * 50, 18), (64 + side * 36, 38)]
        cel(p, S(horn), CHAR_HI, CHAR, None, sh_off=(3 * side, 3), ink_w=3)
    # lower jaw (furnace) behind the skull
    jaw = [(40, 80), (88, 80), (82, 104 + drop), (64, 112 + drop), (46, 104 + drop)]
    cel(p, S(jaw), GUN, GUN_SH, GUN_HI, sh_off=(5, 5))
    p.ink += poly(S([(46, 84), (82, 84), (78, 100 + drop), (50, 100 + drop)]), INK)
    furnace = S([(50, 86), (78, 86), (75, 98 + drop), (53, 98 + drop)])
    p.glow += poly(furnace, hot)
    if lv >= 1:
        p.glow_back += halo(furnace, SODIUM, 0.85)
        p.glow += poly(lerp_pts(furnace, [S([(64, 92 + drop * 0.5)])[0]] * 4, 0.5), BONE)
    for k in range(4):
        x = 54 + k * 7
        p.glow += line(S([(x, 86), (x, 98 + drop)]), 2, INK)
    skull = [(64, 14), (98, 22), (110, 46), (100, 76), (84, 86), (44, 86), (28, 76), (18, 46), (30, 22)]
    cel(p, S(skull), CHAR, CHAR_SH, CHAR_HI, sh_off=(12, 10), detail=True)
    # eye sockets with sodium pupils, and a cracked brow
    for side in (-1, 1):
        sock = [(64 + side * 10, 46), (64 + side * 30, 42), (64 + side * 28, 60), (64 + side * 14, 60)]
        p.detail += poly(S(sock), INK)
        e = lerp_pts(S(sock), [S([(64 + side * 20, 52)])[0]] * 4, 0.35)
        p.glow += poly(e, hot)
        if lv >= 1:
            p.glow_back += halo(e, SODIUM, 0.7)
    for c in ([(64, 16), (60, 28), (66, 38)], [(40, 30), (48, 36)], [(88, 30), (80, 36)]):
        p.detail += line(S(c), 4)
        p.glow += line(S(c), 1.8, hot)
    p.detail += poly(S([(60, 64), (68, 64), (64, 74)]), INK)   # nose
    if i == 5:
        for x, y in ((18, 104), (110, 104), (30, 120), (98, 122)):
            e = ngon(x, y, 4, 4, 45)
            p.glow += poly(e, AMBER) + inkpoly(e, 1.3)
    return p


def bloom(i):
    """Verdant: a carnivorous bud. Petals breathe; the tell opens the maw."""
    lv, spread, sy = FRAMES[i]
    p = Parts()
    openness = max(0.0, min(1.0, (spread + 5) / 15))   # 0 shut .. 1 wide
    # sepals (back leaves)
    for a in (-150, -30, 90):
        r = math.radians(a)
        lf = xf([(CX, CY), (CX - 10, CY - 24), (CX, CY - 46), (CX + 10, CY - 24)], CX, CY, rot=a + 90)
        cel(p, lf, BILE, BILE_SH, BILE_HI, sh_off=(4, 4), ink_w=3)
    # the maw inside
    maw = ngon(CX, CY, 26, 8, 22.5)
    p.base += poly(maw, INK)
    core(p, CX, CY, 9 + 4 * openness, BILE_LIGHT, lv, socket=False)
    for k in range(8):   # thorn teeth ringing the maw
        a = math.radians(k * 45)
        t = [(CX + 26 * math.cos(a - 0.18), CY + 26 * math.sin(a - 0.18)),
             (CX + (16 - 4 * openness) * math.cos(a), CY + (16 - 4 * openness) * math.sin(a)),
             (CX + 26 * math.cos(a + 0.18), CY + 26 * math.sin(a + 0.18))]
        p.detail += poly(t, BONE) + inkpoly(t, 1.4)
    # five petals: closed they cover the maw, open they fold back
    for k in range(5):
        a = k * 72 - 90
        r = math.radians(a)
        d = 10 + 20 * openness
        px, py = CX + d * math.cos(r), CY + d * math.sin(r)
        L = 34
        pet = xf([(px, py + 10), (px - 16, py - 8), (px - 8, py - L * 0.7), (px, py - L), (px + 8, py - L * 0.7), (px + 16, py - 8)],
                 px, py, rot=a + 90)
        pet = xf(pet, CX, CY, 1, sy)
        cel(p, pet, BRUISE, BRUISE_SH, BRUISE_HI, sh_off=(5, 5), ink_w=3, detail=True)
        p.detail += line([xf([(px, py + 6)], px, py, rot=a + 90)[0], xf([(px, py - L + 8)], px, py, rot=a + 90)[0]], 1.4)
        tip = xf([(px - 3, py - L + 2), (px, py - L - 8), (px + 3, py - L + 2)], px, py, rot=a + 90)
        p.detail += poly(tip, MAGENTA) + inkpoly(tip, 1.4)
    if i == 5:
        for x, y in ((18, 30), (110, 34), (24, 110), (106, 106)):
            puff = ngon(x, y, 6, 5, 20)
            p.glow += poly(puff, BILE_LIGHT) + inkpoly(puff, 1.4)
    return p


def draw(world, i):
    return {"space": bastion, "frost": glacier, "verdant": bloom, "ember": magma_skull}[world](i)
