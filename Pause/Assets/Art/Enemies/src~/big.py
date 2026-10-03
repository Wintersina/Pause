"""Big enemy, one per world: a slow, armoured heavy with one big glowing
core. Idle: the core breathes and the armour shifts. Tell: the armour pulls
in (anticipation), then slams open with the core flaring -- the charge-up."""
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

SKINS = {
    "space": (STEEL, STEEL_SH, STEEL_HI, BRUISE, BRUISE_SH, BRUISE_HI, MAGENTA),
    "frost": (ICE_SH, INK, ICE, ICE, ICE_SH, ICE_HI, CYAN),
    "ember": (CHAR, CHAR_SH, CHAR_HI, GUN, GUN_SH, GUN_HI, SODIUM),
}


def armoured(world, i):
    hull, hull_sh, hull_hi, plate, plate_sh, plate_hi, light = SKINS[world]
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

    if world == "frost":   # crown of ice spikes, longer when it charges
        for k, (x, a) in enumerate(((40, -40), (52, -18), (64, 0), (76, 18), (88, 40))):
            L = (24 if k == 2 else 18) + max(0, spread) * 1.2
            sp = xf([(x - 5, CY - 26), (x, CY - 26 - L), (x + 5, CY - 26)], x, CY - 26, rot=a)
            p.detail += poly(sp, ICE_HI) + poly([sp[1], sp[2], (x, CY - 26)], ICE) + inkpoly(sp, 2)
    if world == "ember":   # horns + furnace grille
        for side in (-1, 1):
            horn = [(CX + side * 20, CY - 30), (CX + side * 46, CY - 54 - spread), (CX + side * 34, CY - 24)]
            p.detail += poly(horn, CHAR_HI) + poly([horn[1], horn[2], (CX + side * 26, CY - 26)], CHAR_SH) + inkpoly(horn, 2.5)

    core(p, CX, CY, 12, light, lv)
    if world == "space":
        p.glow += line([(CX - 6, CY), (CX + 6, CY)], 1.6, INK) if lv < 1 else ""
    if world == "ember":
        for k in range(3):
            y = CY + 18 + k * 5
            p.detail += line([(CX - 12 + k * 2, y), (CX + 12 - k * 2, y)], 2.2)
            p.glow += line([(CX - 10 + k * 2, y), (CX + 10 - k * 2, y)], 1, AMBER if lv >= 1 else SODIUM_SH)
    if i == 5:
        p.glow += speed_lines([20, 30, 98, 108], 96, 22, light, 0.8)
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
    return bloom(i) if world == "verdant" else armoured(world, i)
