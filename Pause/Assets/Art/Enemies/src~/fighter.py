"""Small fighters, four tiers per world (the spawner's escalating extras).
Nose DOWN. Idle: hover bob, visor blink, nav lights chasing, antennae and
claws twitching, plates breathing. Tell: wind-up (squash, prongs or wings
pinch, visor and lights dim) then snap (stretch, flare, speed lines).

Detail pass: every hull is cut with one hard mecha shadow plane on top of
its cel crescent, panel seams and plating breaks, rivets, grilles, BONE
specular kicks on the lit (upper-left) edges, glyph decals and warning
stripes on the machines, chitin plates, spiracles and veins on the insects,
and small hard-bloom hex / slit lights instead of soft halos."""
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

# per-frame secondary motion
NAV = (1.0, 0.2, 0.6, 1.0, 0.15, 1.5)      # nav / accent light level (chases the visor blink)
NAV2 = (0.2, 1.0, 1.0, 0.2, 0.15, 1.5)     # the other side's lights, alternating
TWITCH = (0, 6, -4, 3, -8, 10)             # antennae / feelers (deg)
BREATH = (0, 0.8, 1.2, 0.4, -1.0, 1.6)     # plates breathing out (u)


def at(T, x, y):
    return T([(x, y)])[0]


def form(p, outline, base, sh, hi=None, cut=None, sh_off=(6, 6), hi_off=(3, 3), ink_w=4, detail=False):
    """cel() plus one hard angular shadow plane (cut, clipped to the form),
    then the INK contour on top of both."""
    cid = cel(p, outline, base, sh, hi, sh_off=sh_off, hi_off=hi_off, ink_w=0, detail=detail)
    s = poly(cut, sh, f'clip-path="url(#{cid})"') if cut else ""
    k = inkpoly(outline, ink_w) if ink_w else ""
    if detail:
        p.detail += s + k
    else:
        p.shadow += s
        p.ink += k
    return cid


def visor(p, T, shape, vis, light=MAGENTA, scan=True):
    """INK visor socket, flat light, BONE heart, a scan line and a hard flat
    bloom (no blur)."""
    v = T(shape)
    c = (sum(x for x, _ in v) / len(v), sum(y for _, y in v) / len(v))
    p.detail += poly(lerp_pts(v, [c] * len(v), -0.18), INK)
    inner = lerp_pts(v, [c] * len(v), 0.22)
    if vis > 0.4:
        p.glow += poly(lerp_pts(v, [c] * len(v), -0.55), light, f'opacity="{0.18 + 0.16 * min(vis, 1):.2f}"')
    p.glow += poly(inner, light if vis > 0.4 else DIM[light])
    if vis > 0.4:
        p.glow += poly(ngon(c[0], c[1], 2.6, 4, 45), BONE, f'opacity="{f(min(1, vis))}"')
        p.glow += line([inner[0], inner[1]], 1.0, BONE)   # hard kick on the visor's top edge
    if scan and vis <= 0.4:
        p.glow += line([lerp_pts([inner[0]], [inner[3]], 0.5)[0], lerp_pts([inner[1]], [inner[2]], 0.5)[0]], 1.0, INK)
    if vis > 0.8:
        p.glow += poly(star(c[0], c[1], 14, 2, 4, 0), light, 'opacity="0.55"')


def navlight(p, T, x, y, r, lv, light=MAGENTA):
    cx, cy = at(T, x, y)
    hexlight(p, cx, cy, r, lv, light, rot=0)


# ------------------------------------------------------------------ Space ----
def space(tier, i):
    """Steel claw line: Needle, Claw, Twin Claw, Warden."""
    sp, sx, sy, dy, vis = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 52, sx, sy, 0, dy)
    wide = {1: 0.78, 2: 1.0, 3: 1.0, 4: 1.06}[tier]
    W = lambda q: [(64 + (x - 64) * wide, y) for x, y in q]
    TW = lambda q: T(W(q))
    br = BREATH[i]
    tw = TWITCH[i]
    # antennae behind the hull (the sensor mast at the back)
    for k, side in enumerate((-1, 1) if tier in (1, 4) else (1,)):
        bx, by = at(T, 64 + side * 6 * wide, 16)
        antenna(p, bx, by, 13 if tier == 1 else 11, side * (14 + tw * 0.8) + (tw if side > 0 else 0) * 0.3,
                (NAV if k == 0 else NAV2)[i], MAGENTA, tip=1.8, layer="base")
    if tier >= 3:   # outer blades behind the hull
        for side in (-1, 1):
            bl = W([(64 + side * 40, 30), (64 + side * (60 + sp), 52), (64 + side * (56 + sp), 82), (64 + side * 46, 50)])
            cut = W([(64 + side * 46, 50), (64 + side * (60 + sp), 52), (64 + side * (56 + sp), 82)])
            form(p, T(bl), BRUISE, BRUISE_SH, BRUISE_HI, cut=T(cut), sh_off=(3, 3), ink_w=3)
            p.ink += line(TW([(64 + side * 44, 36), (64 + side * (56 + sp), 70)]), 1.4)
            spec(p, TW([(64 + side * 42, 33), (64 + side * (58 + sp), 53)]), 1.2, layer="highlight")
    prong_n = 1 if tier == 1 else 3 if tier == 4 else 2
    half = [(64, 14), (52, 14), (40, 6), (8, 18), (4, 32), (22, 40)]
    if prong_n == 1:
        half += [(40, 56), (48, 70), (56, 74), (60, 104), (64, 108)]
    else:
        half += [(30 - sp, 60), (36 - sp * 1.6, 96), (44 - sp * 0.6, 92), (46, 70), (54, 66), (58, 84), (64, 90)]
    out = T(W(mirror(half, 64)))
    # one hard mecha plane: the right flank below the shoulder line
    form(p, out, STEEL, STEEL_SH, STEEL_HI, cut=TW([(64, 44), (96, 36), (140, 44), (140, 140), (64, 140)]),
         sh_off=(10, 6), ink_w=4.4)
    # panel seams and plating breaks
    seams(p, [TW([(64, 16), (64, 40)]), TW([(64, 54), (64, 88 if prong_n > 1 else 104)]),
              TW([(48, 24), (64, 32), (80, 24)]), TW([(42, 46), (54, 50)]), TW([(86, 46), (74, 50)])], 1.6, layer="ink")
    if prong_n == 1:
        seams(p, [TW([(50, 66), (58, 78)]), TW([(78, 66), (70, 78)])], 1.4, layer="ink")
        spec(p, TW([(41, 58), (48, 69), (55, 74)]), 1.3, layer="highlight")
    else:
        seams(p, [TW([(32 - sp, 66), (40, 70)]), TW([(96 + sp, 66), (88, 70)])], 1.4, layer="ink")
        spec(p, TW([(24, 42), (31 - sp, 60), (36 - sp * 1.6, 92)]), 1.3, layer="highlight")
    rivets(p, TW([(50, 18), (78, 18), (50, 60), (78, 60)]), 1.5)
    gx, gy = at(T, 64, 27)
    grille(p, gx, gy, 12 * wide + 2, 6, 3)
    if prong_n == 1:   # belly intake and the needle's tip segments
        gx, gy = at(T, 64, 64)
        grille(p, gx, gy, 8, 6, 2)
        seams(p, [TW([(59, 84), (69, 84)]), TW([(61, 96), (67, 96)])], 1.3, layer="ink")
        rivets(p, TW([(56, 72), (72, 72)]), 1.3)
    else:              # prong roots: bolted collars and a segment break
        rivets(p, TW([(42, 74), (86, 74)]), 1.3)
        seams(p, [TW([(38 - sp * 1.2, 84), (45 - sp * 0.5, 82)]), TW([(90 + sp * 1.2, 84), (83 + sp * 0.5, 82)])], 1.3, layer="ink")
        if prong_n == 2 and tier == 3:
            gx, gy = at(T, 64, 64)
            grille(p, gx, gy, 10, 6, 2)
    # wings: bruise armour with a hard plane, rivets, a glyph and stripes
    wingL = W([(40, 8 - br), (10, 19 - br), (7, 30), (22, 37), (34, 30)])
    for n, wg in enumerate((wingL, mx(wingL, 64))):
        s = 1 if n else -1
        cut = [(64 + s * (64 - 26) * wide, 22), (64 + s * 70, 10), (64 + s * 70, 40), (64 + s * 42 * wide, 38)]
        form(p, T(wg), BRUISE, BRUISE_SH, BRUISE_HI, cut=T(cut), sh_off=(4, 4), ink_w=2.8, detail=True)
        rivets(p, TW([(64 + s * 38, 14), (64 + s * 30, 30), (64 + s * 46, 31)]), 1.3)
        seams(p, [TW([(64 + s * 34, 11 - br), (64 + s * 40, 32)])], 1.2)
        navlight(p, T, 64 + s * 50 * wide, 26, 2.2, (NAV if n else NAV2)[i])
    gx, gy = at(T, 64 - 26 * wide, 22)
    glyph(p, gx, gy, 2.4, seed=tier, w=1.2)
    gx, gy = at(T, 64 + 26 * wide, 22)
    if tier >= 2:
        stripes(p, gx, gy, 12, 5, 3, rot=-22)
    else:
        glyph(p, gx, gy, 2.4, seed=tier + 3, w=1.2)
    spec(p, TW([(40, 9 - br), (12, 19 - br)]), 1.2)
    if prong_n == 3:   # centre ram prong + bruise shoulder armour
        ram = W([(56, 60), (72, 60), (68, 112 + sp), (64, 120 + sp), (60, 112 + sp)])
        form(p, T(ram), STEEL, STEEL_SH, STEEL_HI, cut=TW([(64, 60), (80, 60), (80, 130), (64, 130)]),
             sh_off=(4, 4), ink_w=3, detail=True)
        seams(p, [TW([(58, 76), (70, 76)]), TW([(59, 92), (69, 92)])], 1.3)
        rivets(p, TW([(60, 66), (68, 66)]), 1.2)
        spec(p, TW([(57, 64), (61, 108 + sp)]), 1.1)
        for side in (-1, 1):
            sh_ = W([(64 + side * 16, 16 - br), (64 + side * 32, 22 - br), (64 + side * 30, 44), (64 + side * 14, 40)])
            cut = W([(64 + side * 23, 30), (64 + side * 40, 20), (64 + side * 40, 50), (64 + side * 10, 50)])
            form(p, T(sh_), BRUISE, BRUISE_SH, BRUISE_HI, cut=T(cut), sh_off=(3, 3), ink_w=2.6, detail=True)
            rivets(p, TW([(64 + side * 20, 22), (64 + side * 22, 38)]), 1.2)
    if tier == 3:
        for vx in (54, 74):
            visor(p, T, [(vx - 7, 42), (vx + 7, 42), (vx + 5, 51), (vx - 5, 51)], vis)
    else:
        visor(p, T, [(52, 40), (76, 40), (72, 52), (56, 52)], vis)
    if tier == 4:
        core(p, *T([(64, 74)])[0], 5, MAGENTA, vis + 0.1, n=4, rot=45)
    if tier == 2:   # the claw's twin slit sensors between the prongs
        for vx in (46, 82):
            cx, cy = at(T, 64 + (vx - 64) * wide, 62)
            slitlight(p, cx, cy, 6, 2, NAV[i], MAGENTA, rot=90)
    if i == 5:
        p.glow += speed_lines([36, 50, 78, 92], 4, 16, MAGENTA, 0.7)
    return p


# ------------------------------------------------------------------ Frost ----
def frost(tier, i):
    """Ice drone line: Flake, Icicle, Frost Kite, Hailstorm."""
    sp, sx, sy, dy, vis = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 56, sx, sy, 0, dy)
    tw = TWITCH[i]
    blades = {1: (-60, 60), 2: (-120, -60, 60, 120), 3: (-150, -90, -30, 30, 90, 150),
              4: (-150, -110, -70, 70, 110, 150, 180)}[tier]
    L = {1: 40, 2: 40, 3: 36, 4: 40}[tier] + sp
    if tier >= 2:   # sensor mast on top of the hub
        bx, by = at(T, 64, 34)
        antenna(p, bx, by, 14, tw, NAV[i], CYAN, tip=2, layer="base")
    for n, a in enumerate(blades):   # crystal blades radiate from the hub (0 deg = up, 180 = down at the player)
        r = math.radians(a - 90)
        bx, by = 64 + 18 * math.cos(r), 56 + 18 * math.sin(r)
        w = 7 if a % 180 else 8
        Ln = L + (BREATH[i] if n % 2 else -BREATH[i])
        shape = xf([(bx - w, by), (bx - w * 0.8, by - Ln * 0.7), (bx, by - Ln), (bx + w * 0.8, by - Ln * 0.7), (bx + w, by)], bx, by, rot=a)
        cut = xf([(bx, by), (bx, by - Ln), (bx + w * 2, by - Ln), (bx + w * 2, by)], bx, by, rot=a)
        form(p, T(shape), ICE, ICE_SH, ICE_HI, cut=T(cut), sh_off=(w * 0.5, 2), ink_w=3)
        p.ink += line(T(xf([(bx, by - 3), (bx, by - Ln + 6)], bx, by, rot=a)), 1.2)
        # facet break across the blade and a hard BONE kick on its lit edge
        p.ink += line(T(xf([(bx - w * 0.8, by - Ln * 0.7), (bx, by - Ln * 0.56), (bx + w * 0.8, by - Ln * 0.7)], bx, by, rot=a)), 1.3)
        p.highlight += line(T(xf([(bx - w + 1.5, by - 3), (bx - w * 0.8 + 1.5, by - Ln * 0.66)], bx, by, rot=a)), 1.2, BONE)
        p.ink += line(T(xf([(bx - w * 0.9, by - Ln * 0.3), (bx, by - Ln * 0.2), (bx + w * 0.9, by - Ln * 0.3)], bx, by, rot=a)), 1.2)
        # coolant light where each blade plugs into the hub, on a bolted collar
        lx, ly = at(T, 64 + 27 * math.cos(r), 56 + 27 * math.sin(r))
        hexlight(p, lx, ly, 1.9, (NAV if n % 2 else NAV2)[i], CYAN, rot=0)
        rivets(p, T([(64 + 33 * math.cos(r), 56 + 33 * math.sin(r))]), 1.2, color=STEEL_HI)
    if tier >= 1:   # icicle lance pointing at the player, two stabiliser crystals at its root
        for side in (-1, 1):
            fin_ = [(64 + side * 5, 70), (64 + side * 13, 76), (64 + side * 6, 84)]
            p.base += poly(T(fin_), ICE) + poly(T([fin_[1], fin_[2], (64 + side * 7, 77)]), ICE_SH)
            p.ink += inkpoly(T(fin_), 2)
        lance = [(58, 70), (70, 70), (64, 112 + sp * 1.5)]
        form(p, T(lance), ICE_HI, ICE, None, cut=T([(64, 70), (80, 70), (80, 130), (64, 130)]), sh_off=(3, 0), ink_w=3)
        rivets(p, T([(61, 73), (67, 73)]), 1.1, color=STEEL_HI)
        tip = 112 + sp * 1.5
        for t in (0.28, 0.52):
            y = 70 + (tip - 70) * t
            hw = 6 * (1 - t)
            p.ink += line(T([(64 - hw, y), (64 + hw, y)]), 1.3)
        p.highlight += line(T([(59.5, 73), (63, tip - 8)]), 1.2, BONE)
    hub = ngon(64, 56, 24 if tier < 4 else 27, 6, 0)
    form(p, T(hub), STEEL, STEEL_SH, STEEL_HI, cut=T([(64, 56), (110, 40), (110, 110), (40, 110)]), sh_off=(7, 7), detail=True)
    p.detail += inkpoly(T(ngon(64, 56, 14, 6, 0)), 2)
    seams(p, [T([(64, 32), (64, 42)]), T([(44, 44), (52, 49)]), T([(84, 44), (76, 49)])], 1.4)
    rivets(p, T([(64 + 19 * math.cos(math.radians(a)), 56 + 19 * math.sin(math.radians(a))) for a in (-60, 0, 60, 120, 180, 240)]), 1.4)
    spec(p, T([(44, 45), (53, 35), (62, 33)]), 1.2)
    gx, gy = at(T, 64, 40)
    grille(p, gx, gy, 10, 4, 2)
    seams(p, [T([(47, 66), (55, 70)]), T([(81, 66), (73, 70)])], 1.3)
    gx, gy = at(T, 51, 60)
    glyph(p, gx, gy, 1.9, seed=tier + 2, w=1.1, color=ICE_HI)
    if tier == 4:   # armour collar
        for k in range(6):
            a = k * 60
            seg = xf([(58, 30), (70, 30), (68, 36), (60, 36)], 64, 56, rot=a)
            p.detail += poly(T(seg), ICE_HI) + inkpoly(T(seg), 1.6)
        gx, gy = at(T, 77, 65)
        stripes(p, gx, gy, 9, 4, 3, rot=30)
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
    tw = TWITCH[i]
    breath = 0.5 + 0.5 * (NAV[i] > 0.5) + (0.6 if i == 5 else 0)
    pairs = 2 if tier == 4 else 1
    for k in range(pairs):
        for side in (-1, 1):
            base_ = (64 + side * 10, 40 + k * 14)
            wl = 48 if tier >= 3 else 46
            a = side * (70 - k * 34) + side * beat
            wing = xf([(base_[0], base_[1]), (base_[0] - 7, base_[1] - wl * 0.45), (base_[0], base_[1] - wl), (base_[0] + 7, base_[1] - wl * 0.45)],
                      base_[0], base_[1], rot=a)
            cel(p, T(wing), BILE_HI, BILE, None, sh_off=(3 * side, 3), ink_w=2.5)
            # veins: the spine and two branches into a wing cell
            p.ink += line(T([wing[0], wing[2]]), 1.2)
            mid = lerp_pts([wing[0]], [wing[2]], 0.45)[0]
            p.ink += line(T([mid, lerp_pts([wing[1]], [wing[2]], 0.4)[0]]), 1.0)
            p.ink += line(T([lerp_pts([wing[0]], [wing[2]], 0.7)[0], lerp_pts([wing[3]], [wing[2]], 0.35)[0]]), 1.0)
            cell_ = [mid, lerp_pts([wing[1]], [wing[2]], 0.4)[0], lerp_pts([wing[0]], [wing[2]], 0.7)[0]]
            p.ink += poly(T(cell_), BILE, 'opacity="1"')
    # abdomen behind (up), thorax, head with mandibles toward the player
    ab = [(64, 2 + (6 if tier == 1 else 0)), (78, 14), (80, 30), (64, 40), (48, 30), (50, 14)]
    abc = (BRUISE, BRUISE_SH, BRUISE_HI) if tier >= 2 else (BILE, BILE_SH, BILE_HI)
    form(p, T(ab), *abc, cut=T([(64, 0), (90, 0), (90, 44), (64, 44)]), sh_off=(5, 5), ink_w=4)
    if tier >= 2:
        for y in (14, 22, 30):
            p.ink += line(T([(52, y), (76, y)]), 2)
        p.highlight += line(T([(52, 15), (50, 28)]), 1.3, BONE)
        sting = [(60, 4), (68, 4), (64, -8 - sp)]
        p.detail += poly(T(sting), MAGENTA) + poly(T([(64, 4), (68, 4), (64, -8 - sp)]), MAGENTA_SH) + inkpoly(T(sting), 1.8)
    else:
        cx, cy = at(T, 64, 15)
        scales(p, cx, cy, 2, 2, 8, BILE_HI, BILE_SH)
    for side in (-1, 1):   # spiracles breathing along the abdomen
        sx_, sy_ = at(T, 64 + side * 12, 24)
        hexlight(p, sx_, sy_, 1.7, breath, BILE_LIGHT, rot=0)
    thorax = [(64, 34), (80, 42), (82, 58), (64, 66), (46, 58), (48, 42)]
    form(p, T(thorax), BILE, BILE_SH, BILE_HI, cut=T([(64, 34), (90, 34), (90, 70), (64, 70)]), sh_off=(6, 6), detail=True)
    seams(p, [T([(50, 46), (64, 52), (78, 46)]), T([(64, 52), (64, 64)])], 1.3)
    rivets(p, T([(54, 56), (74, 56)]), 1.4, color=BILE_HI)
    p.detail += line(T([(50, 43), (63, 36)]), 1.2, BONE)
    if tier == 4:   # the queen's jewel
        jx, jy = at(T, 64, 45)
        hexlight(p, jx, jy, 3, NAV[i], BILE_LIGHT)
    if tier >= 3:   # mantis scythes reaching down
        for side in (-1, 1):
            arm = [(64 + side * 14, 58), (64 + side * (34 + sp), 72), (64 + side * (30 + sp), 108), (64 + side * (24 + sp), 82), (64 + side * 10, 66)]
            form(p, T(arm), BILE, BILE_SH, BILE_HI, cut=T([(64 + side * (27 + sp), 70), (64 + side * 60, 70), (64 + side * 60, 120), (64 + side * (30 + sp), 108)]),
                 sh_off=(3 * side, 3), ink_w=2.6, detail=True)
            rivets(p, T([(64 + side * (33 + sp), 73)]), 1.6, color=BILE_HI)   # elbow joint
            blade = [(64 + side * (30 + sp), 108), (64 + side * (36 + sp), 94), (64 + side * (32 + sp), 86)]
            p.detail += poly(T(blade), BONE) + inkpoly(T(blade), 1.5)
            teeth(p, T([(64 + side * (25 + sp), 84)])[0], T([(64 + side * (29 + sp), 104)])[0], 3, 2.6, inward=side)
    head = [(64, 62), (78, 68), (76, 84), (64, 90), (52, 84), (50, 68)]
    form(p, T(head), BILE, BILE_SH, BILE_HI, cut=T([(64, 60), (90, 60), (90, 96), (64, 96)]), sh_off=(5, 5), detail=True)
    # feelers twitching out of the head's cheeks, swept out past the mandibles
    for side in (-1, 1):
        hx, hy = at(T, 64 + side * 12, 80)
        antenna(p, hx, hy, 16 if tier < 4 else 19, 180 - side * (58 + tw), NAV[i], BILE_LIGHT, tip=1.6)
    for side in (-1, 1):   # mandibles
        m = [(64 + side * 6, 86), (64 + side * (14 + sp), 98), (64 + side * 6, 104 + sp)]
        p.detail += poly(T(m), BRUISE) + poly(T([m[1], m[2], (64 + side * 7, 95)]), BRUISE_SH) + inkpoly(T(m), 1.8)
        teeth(p, T([(64 + side * 6, 88)])[0], T([(64 + side * 6, 102 + sp)])[0], 2, 2.2, inward=-side)
        eye = [(64 + side * 4, 70), (64 + side * 13, 70), (64 + side * 12, 80), (64 + side * 5, 78)]
        p.detail += poly(T(eye), INK)
        e2 = lerp_pts(T(eye), [T([(64 + side * 8.5, 75)])[0]] * 4, 0.3)
        if vis > 0.4:
            p.glow += poly(lerp_pts(T(eye), [T([(64 + side * 8.5, 75)])[0]] * 4, -0.4), BILE_LIGHT, 'opacity="0.3"')
        p.glow += poly(e2, BILE_LIGHT if vis > 0.4 else BILE_SH)
        if vis > 0.4:   # compound facets: a hard BONE kick and a dark facet line
            p.glow += poly([e2[0], lerp_pts([e2[0]], [e2[1]], 0.5)[0], lerp_pts([e2[0]], [e2[3]], 0.5)[0]], BONE)
            p.glow += line([lerp_pts([e2[0]], [e2[3]], 0.6)[0], lerp_pts([e2[1]], [e2[2]], 0.6)[0]], 0.9, BILE_SH)
        if vis > 0.8:
            p.glow += poly(star(*T([(64 + side * 8.5, 75)])[0], 11, 1.8, 4, 0), BILE_LIGHT, 'opacity="0.55"')
    # legs tucked under the thorax, with knee joints
    for side in (-1, 1):
        for k, y in enumerate((44, 52)):
            knee = (64 + side * 28, y + 6 + k * 4 + (BREATH[i] if k else -BREATH[i]) * 0.6)
            p.ink += line(T([(64 + side * 16, y), knee, (64 + side * 26, y + 16 + k * 4)]), 2)
            rivets(p, T([knee]), 1.3, color=BILE_SH, layer="ink")
    return p


# ------------------------------------------------------------------ Ember ----
def ember(tier, i):
    """Scorched line: Cinder, Scorch, Brand, Pyre."""
    sp, sx, sy, dy, vis = FRAMES[i]
    p = Parts()
    T = lambda q: xf(q, 64, 56, sx, sy, 0, dy)
    flick = (0, 4, -2, 2, -5, 8)[i]
    tw = TWITCH[i]
    if tier >= 3:   # flame fins: lights, so they glow (hard flat bloom)
        for side in (-1, 1):
            fin = [(64 + side * 30, 26), (64 + side * (52 + sp), 8 - flick), (64 + side * 46, 26), (64 + side * (58 + sp), 22 - flick * 0.5), (64 + side * 40, 44)]
            c = (64 + side * 40, 30)
            p.glow_back += poly(T(lerp_pts(fin, [c] * 5, -0.3)), SODIUM, 'opacity="0.35"')
            p.glow += poly(T(fin), SODIUM) + poly(T(lerp_pts(fin, [c] * 5, 0.45)), AMBER) + inkpoly(T(fin), 2)
            p.glow += poly(T(lerp_pts(fin, [c] * 5, 0.78)), BONE)
    if tier == 2:   # twin heat-sensor spikes at the back
        for side in (-1, 1):
            bx, by = at(T, 64 + side * 7, 14)
            antenna(p, bx, by, 10, side * (18 + tw * 0.6), (NAV if side < 0 else NAV2)[i], SODIUM, tip=1.8, layer="base")
    # scorched bat-wing: scalloped trailing edge, a hooked tail at the player
    span = {1: 0.84, 2: 0.94, 3: 1.0, 4: 1.0}[tier]
    half = [(64, 6), (54, 20), (30, 12), (4, 28), (14, 42), (6, 60), (28, 58), (36 - sp, 82), (50, 70), (58, 96 + sp), (64, 104 + sp)]
    S = lambda q: [(64 + (x - 64) * span, y) for x, y in q]
    out = T(mirror(S(half), 64))
    form(p, out, CHAR, CHAR_SH, CHAR_HI, cut=T(S([(64, 40), (110, 30), (140, 40), (140, 140), (64, 140)])), sh_off=(10, 6), ink_w=4.4)
    # wing ribs out to the scallops, plating breaks along the body
    for side in (-1, 1):
        ribs = [[(64 + side * 12, 24), (64 + side * 40, 18)], [(64 + side * 14, 34), (64 + side * 54, 34)],
                [(64 + side * 14, 44), (64 + side * 46, 52)], [(64 + side * 12, 56), (64 + side * (28 + sp * 0.5), 74)]]
        seams(p, [T(S(r)) for r in ribs], 1.5, layer="ink")
        rivets(p, T(S([(64 + side * 40, 18), (64 + side * 54, 34), (64 + side * 46, 52)])), 1.3, color=CHAR_HI)
    for side in (-1, 1):   # heat-sink fins on the wings and a seam down each scallop
        for k in range(3):
            x0 = 64 + side * (22 + k * 5)
            p.ink += line(T(S([(x0, 60 - k), (x0 + side * 3, 66 - k)])), 1.4)
        seams(p, [T(S([(64 + side * 50, 58), (64 + side * 40, 46)]))], 1.3, layer="ink")
    rivets(p, T([(59, 18), (69, 18), (58, 56), (70, 56)]), 1.3, color=CHAR_HI)
    seams(p, [T([(61, 92 + sp * 0.5), (64, 98 + sp * 0.8), (67, 92 + sp * 0.5)])], 1.2, layer="ink")
    spec(p, T(S([(31, 13), (6, 28)])), 1.3, layer="highlight")
    spec(p, T(S([(53, 21), (62, 9)])), 1.2, layer="highlight")
    seams(p, [T([(56, 60), (64, 64), (72, 60)]), T([(60, 84), (64, 88), (68, 84)])], 1.4, layer="ink")
    if tier == 4:   # pyre: horns + exhaust grille
        for side in (-1, 1):
            horn = [(64 + side * 18, 14), (64 + side * (40 + sp), -2), (64 + side * 30, 22)]
            p.detail += poly(T(horn), CHAR_HI) + poly(T([horn[1], horn[2], (64 + side * 24, 18)]), CHAR_SH) + inkpoly(T(horn), 2.2)
            p.detail += line(T([(64 + side * 21, 15), (64 + side * (37 + sp), 0)]), 1.1, BONE)
            seams(p, [T([(64 + side * 24, 12), (64 + side * 28, 18)]), T([(64 + side * 30, 7), (64 + side * 33, 14)])], 1.2)
    gx, gy = at(T, 64, 27)
    grille(p, gx, gy, 9, 10, 3, bars=CHAR_HI)
    # molten vents along the hull: dim when it inhales, white-hot on the snap
    hot = AMBER if vis > 0.8 else SODIUM if vis > 0.3 else SODIUM_SH
    vents = [[(44, 26), (56, 30)], [(84, 26), (72, 30)]]
    if tier >= 2:
        vents += [[(36, 52), (44, 68)], [(92, 52), (84, 68)]]
    for v in vents:
        p.ink += line(T(S(v)), 5)
        p.glow += line(T(S(v)), 2.4, hot)
    if vis > 0.3:
        for v in vents:
            p.glow_back += line(T(S(v)), 9, SODIUM, f'opacity="{0.25 + 0.2 * min(vis, 1):.2f}"')
    if vis > 0.8:
        for v in vents:
            p.glow += line(T(S(v)), 0.9, BONE)
    # decals: a glyph on the left wing, stripes on the right from Brand up
    gx, gy = at(T, 64 - 30 * span, 44)
    glyph(p, gx, gy, 2.3, seed=tier + 1, w=1.2)
    gx, gy = at(T, 64 + 30 * span, 44)
    if tier >= 3:
        stripes(p, gx, gy, 12, 5, 3, rot=-15)
    else:
        glyph(p, gx, gy, 2.3, seed=tier + 4, w=1.2)
    for side, lv in ((-1, NAV[i]), (1, NAV2[i])):   # wingtip nav lights
        navlight(p, T, 64 + side * 52 * span, 34, 2, lv)
    visor(p, T, [(52, 40), (76, 40), (72, 52), (56, 52)], vis)
    if tier >= 2:
        core(p, *T([(64, 70)])[0], 4.5, SODIUM, vis, n=4, rot=45)
    else:
        cx, cy = at(T, 64, 72)
        slitlight(p, cx, cy, 2.4, 9, vis, SODIUM)
    if i == 5:
        p.glow += speed_lines([40, 54, 74, 88], 2, 14, SODIUM, 0.7)
    return p


def draw(world, tier, i):
    return {"space": space, "frost": frost, "verdant": verdant, "ember": ember}[world](tier, i)
