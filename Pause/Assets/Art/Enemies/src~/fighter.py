"""Small fighters, four tiers per world (the spawner's escalating extras).
Nose DOWN. Idle: hover bob, visor blink. Tell: wind-up (squash, prongs or
wings pinch, visor dims) then snap (stretch, flare)."""
from common import *

# (spread, sx, sy, dy, visor 0..1)
FRAMES = [
    (0, 1.00, 1.00, 0, 0.6),     # 0 key (hold)
    (1, 1.00, 1.00, 2, 0.5),     # 1 bob down
    (0, 1.00, 1.00, 1, 0.15),    # 2 blink
    (-1, 1.00, 1.00, -1, 0.7),   # 3 bob up
    (-4, 1.05, 0.93, 3, 0.25),   # 4 tell: wind-up squash
    (6, 0.96, 1.07, -3, 1.0),    # 5 tell: snap, visor flare
]
IDLE_TICKS = [6, 3, 2, 3]
TELL_TICKS = [2, 3]


def visor(p, T, shape, vis, light=MAGENTA):
    v = T(shape)
    p.ink += poly(v, INK)
    inner = lerp_pts(v, [(sum(x for x, _ in v) / len(v), sum(y for _, y in v) / len(v))] * len(v), 0.25)
    p.glow += poly(inner, light if vis > 0.4 else DIM[light])
    if vis > 0.4:
        cx_ = sum(x for x, _ in inner) / len(inner)
        cy_ = sum(y for _, y in inner) / len(inner)
        p.glow += poly(ngon(cx_, cy_, 2.4, 4, 45), BONE, f'opacity="{f(min(1, vis))}"')
    if vis > 0.8:
        p.glow_back += halo(v, light, 0.7)


# ------------------------------------------------------------------ Space ----
def space(tier, i):
    """Steel claw line: Needle, Claw, Twin Claw, Warden."""
    sp, sx, sy, dy, vis = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 52, sx, sy, 0, dy)
    wide = {1: 0.78, 2: 1.0, 3: 1.0, 4: 1.06}[tier]
    W = lambda q: [(64 + (x - 64) * wide, y) for x, y in q]
    if tier >= 3:   # outer blades behind the hull
        for side in (-1, 1):
            bl = W([(64 + side * 40, 30), (64 + side * (60 + sp), 52), (64 + side * (56 + sp), 82), (64 + side * 46, 50)])
            cel(p, T(bl), BRUISE, BRUISE_SH, BRUISE_HI, sh_off=(3, 3), ink_w=3)
    prong_n = 1 if tier == 1 else 3 if tier == 4 else 2
    half = [(64, 14), (52, 14), (40, 6), (8, 18), (4, 32), (22, 40)]
    if prong_n == 1:
        half += [(40, 56), (48, 70), (56, 74), (60, 104), (64, 108)]
    else:
        half += [(30 - sp, 60), (36 - sp * 1.6, 96), (44 - sp * 0.6, 92), (46, 70), (54, 66), (58, 84), (64, 90)]
    out = T(W(mirror(half, 64)))
    cel(p, out, STEEL, STEEL_SH, STEEL_HI, sh_off=(10, 6))
    wingL = W([(40, 8), (10, 19), (7, 30), (22, 37), (34, 30)])
    for wg in (wingL, mx(wingL, 64)):
        cel(p, T(wg), BRUISE, BRUISE_SH, BRUISE_HI, sh_off=(4, 4), ink_w=2.5, detail=True)
    p.ink += line(T([(64, 16), (64, 40)]), 2) + line(T([(64, 54), (64, 86)]), 2)
    if prong_n == 3:   # centre ram prong + bruise shoulder armour
        ram = W([(56, 60), (72, 60), (68, 112 + sp), (64, 120 + sp), (60, 112 + sp)])
        cel(p, T(ram), STEEL, STEEL_SH, STEEL_HI, sh_off=(4, 4), ink_w=3, detail=True)
        for side in (-1, 1):
            sh_ = W([(64 + side * 16, 16), (64 + side * 32, 22), (64 + side * 30, 44), (64 + side * 14, 40)])
            cel(p, T(sh_), BRUISE, BRUISE_SH, BRUISE_HI, sh_off=(3, 3), ink_w=2.5, detail=True)
    if tier == 3:
        for vx in (54, 74):
            visor(p, T, [(vx - 7, 42), (vx + 7, 42), (vx + 5, 51), (vx - 5, 51)], vis)
    else:
        visor(p, T, [(52, 40), (76, 40), (72, 52), (56, 52)], vis)
    if tier == 4:
        core(p, *T([(64, 74)])[0], 5, MAGENTA, vis + 0.1, n=4, rot=45)
    if i == 5:
        p.glow += speed_lines([36, 50, 78, 92], 4, 16, MAGENTA, 0.7)
    return p


# ------------------------------------------------------------------ Frost ----
def frost(tier, i):
    """Ice drone line: Flake, Icicle, Frost Kite, Hailstorm."""
    sp, sx, sy, dy, vis = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 56, sx, sy, 0, dy)
    blades = {1: (-60, 60), 2: (-120, -60, 60, 120), 3: (-150, -90, -30, 30, 90, 150),
              4: (-150, -110, -70, 70, 110, 150, 180)}[tier]
    L = {1: 40, 2: 40, 3: 36, 4: 40}[tier] + sp
    for a in blades:   # crystal blades radiate from the hub (0 deg = up, 180 = down at the player)
        r = math.radians(a - 90)
        bx, by = 64 + 18 * math.cos(r), 56 + 18 * math.sin(r)
        w = 7 if a % 180 else 8
        shape = xf([(bx - w, by), (bx - w * 0.8, by - L * 0.7), (bx, by - L), (bx + w * 0.8, by - L * 0.7), (bx + w, by)], bx, by, rot=a)
        cel(p, T(shape), ICE, ICE_SH, ICE_HI, sh_off=(w * 0.8, 3), ink_w=3)
        p.ink += line(T(xf([(bx, by - 3), (bx, by - L + 6)], bx, by, rot=a)), 1.2)
    if tier >= 1:   # icicle lance pointing at the player
        lance = [(58, 70), (70, 70), (64, 112 + sp * 1.5)]
        cel(p, T(lance), ICE_HI, ICE, None, sh_off=(3, 0), ink_w=3)
    hub = ngon(64, 56, 24 if tier < 4 else 27, 6, 0)
    cel(p, T(hub), STEEL, STEEL_SH, STEEL_HI, sh_off=(7, 7), detail=True)
    p.detail += inkpoly(T(ngon(64, 56, 14, 6, 0)), 2)
    if tier == 4:   # armour collar
        for k in range(6):
            a = k * 60
            seg = xf([(58, 30), (70, 30), (68, 36), (60, 36)], 64, 56, rot=a)
            p.detail += poly(T(seg), ICE_HI) + inkpoly(T(seg), 1.6)
    visor(p, T, [(54, 50), (74, 50), (70, 62), (58, 62)], vis)
    if vis > 0.4:
        p.glow += line(T([(64, 52), (64, 60)]), 1.2, INK)   # slit pupil
    if i == 5:
        for x, y in ((16, 24), (112, 30), (22, 100), (106, 96)):
            p.glow += spark(x, y, 5, ICE_HI)
    return p


# ---------------------------------------------------------------- Verdant ----
def verdant(tier, i):
    """Insectoid line: Gnat, Wasp, Mantis, Hornet Queen."""
    sp, sx, sy, dy, vis = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 56, sx, sy, 0, dy)
    beat = (0, 10, -6, 4, -12, 16)[i]   # wing beat angle per frame
    pairs = 2 if tier == 4 else 1
    for k in range(pairs):
        for side in (-1, 1):
            base_ = (64 + side * 10, 40 + k * 14)
            wl = 48 if tier >= 3 else 46
            a = side * (70 - k * 34) + side * beat
            wing = xf([(base_[0], base_[1]), (base_[0] - 7, base_[1] - wl * 0.45), (base_[0], base_[1] - wl), (base_[0] + 7, base_[1] - wl * 0.45)],
                      base_[0], base_[1], rot=a)
            cel(p, T(wing), BILE_HI, BILE, None, sh_off=(3 * side, 3), ink_w=2.5)
            p.ink += line(T([wing[0], wing[2]]), 1.2)
    # abdomen behind (up), thorax, head with mandibles toward the player
    ab = [(64, 2 + (6 if tier == 1 else 0)), (78, 14), (80, 30), (64, 40), (48, 30), (50, 14)]
    cel(p, T(ab), BRUISE if tier >= 2 else BILE, BRUISE_SH if tier >= 2 else BILE_SH, BRUISE_HI if tier >= 2 else BILE_HI, sh_off=(5, 5))
    if tier >= 2:
        for y in (14, 22, 30):
            p.ink += line(T([(52, y), (76, y)]), 2)
        sting = [(60, 4), (68, 4), (64, -8 - sp)]
        p.detail += poly(T(sting), MAGENTA) + inkpoly(T(sting), 1.8)
    thorax = [(64, 34), (80, 42), (82, 58), (64, 66), (46, 58), (48, 42)]
    cel(p, T(thorax), BILE, BILE_SH, BILE_HI, sh_off=(6, 6), detail=True)
    if tier >= 3:   # mantis scythes reaching down
        for side in (-1, 1):
            arm = [(64 + side * 14, 58), (64 + side * (34 + sp), 72), (64 + side * (30 + sp), 108), (64 + side * (24 + sp), 82), (64 + side * 10, 66)]
            cel(p, T(arm), BILE, BILE_SH, BILE_HI, sh_off=(3 * side, 3), ink_w=2.5, detail=True)
            blade = [(64 + side * (30 + sp), 108), (64 + side * (36 + sp), 94), (64 + side * (32 + sp), 86)]
            p.detail += poly(T(blade), BONE) + inkpoly(T(blade), 1.5)
    head = [(64, 62), (78, 68), (76, 84), (64, 90), (52, 84), (50, 68)]
    cel(p, T(head), BILE, BILE_SH, BILE_HI, sh_off=(5, 5), detail=True)
    for side in (-1, 1):   # mandibles
        m = [(64 + side * 6, 86), (64 + side * (14 + sp), 98), (64 + side * 6, 104 + sp)]
        p.detail += poly(T(m), BRUISE) + inkpoly(T(m), 1.8)
        eye = [(64 + side * 4, 70), (64 + side * 13, 70), (64 + side * 12, 80), (64 + side * 5, 78)]
        p.detail += poly(T(eye), INK)
        e2 = lerp_pts(T(eye), [T([(64 + side * 8.5, 75)])[0]] * 4, 0.3)
        p.glow += poly(e2, BILE_LIGHT if vis > 0.4 else BILE_SH)
        if vis > 0.8:
            p.glow_back += halo(e2, BILE_LIGHT, 0.7)
    # legs tucked under the thorax
    for side in (-1, 1):
        for k, y in enumerate((44, 52)):
            p.ink += line(T([(64 + side * 16, y), (64 + side * 28, y + 6 + k * 4), (64 + side * 26, y + 16 + k * 4)]), 2)
    return p


# ------------------------------------------------------------------ Ember ----
def ember(tier, i):
    """Scorched line: Cinder, Scorch, Brand, Pyre."""
    sp, sx, sy, dy, vis = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 56, sx, sy, 0, dy)
    flick = (0, 4, -2, 2, -5, 8)[i]
    if tier >= 3:   # flame fins: lights, so they glow
        for side in (-1, 1):
            fin = [(64 + side * 30, 26), (64 + side * (52 + sp), 8 - flick), (64 + side * 46, 26), (64 + side * (58 + sp), 22 - flick * 0.5), (64 + side * 40, 44)]
            p.glow_back += halo(T(fin), SODIUM, 0.55)
            p.glow += poly(T(fin), SODIUM) + poly(T(lerp_pts(fin, [(64 + side * 40, 30)] * 5, 0.45)), AMBER) + inkpoly(T(fin), 2)
    if tier == 1:
        half = [(64, 10), (46, 12), (10, 30), (26, 48), (46, 52), (56, 90), (64, 104)]
    else:
        half = [(64, 10), (48, 12), (14, 24), (10, 40), (28, 46), (34 - sp, 70), (40 - sp, 100), (50, 82), (56, 72), (64, 88)]
    out = T(mirror(half, 64))
    cel(p, out, CHAR, CHAR_SH, CHAR_HI, sh_off=(10, 6))
    if tier == 4:   # pyre: horns + exhaust grille
        for side in (-1, 1):
            horn = [(64 + side * 18, 14), (64 + side * (40 + sp), -2), (64 + side * 30, 22)]
            p.detail += poly(T(horn), CHAR_HI) + poly(T([horn[1], horn[2], (64 + side * 24, 18)]), CHAR_SH) + inkpoly(T(horn), 2.2)
    # molten vents along the hull: dim when it inhales, white-hot on the snap
    hot = AMBER if vis > 0.8 else SODIUM if vis > 0.3 else SODIUM_SH
    vents = [[(44, 26), (56, 30)], [(84, 26), (72, 30)]]
    if tier >= 2:
        vents += [[(36, 52), (44, 68)], [(92, 52), (84, 68)]]
    for v in vents:
        p.ink += line(T(v), 5)
        p.glow += line(T(v), 2.4, hot)
    if vis > 0.8:
        for v in vents:
            p.glow_back += line(T(v), 8, SODIUM, 'opacity="0.6" filter="url(#glowS)"')
    p.ink += line(T([(64, 14), (64, 38)]), 2)
    visor(p, T, [(52, 40), (76, 40), (72, 52), (56, 52)], vis)
    if tier >= 2:
        core(p, *T([(64, 70)])[0], 4.5, SODIUM, vis, n=4, rot=45)
    return p


def draw(world, tier, i):
    return {"space": space, "frost": frost, "verdant": verdant, "ember": ember}[world](tier, i)
