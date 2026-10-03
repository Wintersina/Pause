"""Rail mines, one distinct design per world. Each hangs from its rail on
the left (the game mirrors it on right-hand rails), idles with a breathing
core and arms to a burst when the ship is close:

  space    Rail Mine      the approved rail bomb: clamp, steel hub, four lugs
  frost    Geode Mine     a crystal geode gripped by two ice hooks
  verdant  Burr Mine      a thorny seed burr on a vine tendril; splits to arm
  ember    Crucible Mine  a basalt crucible on chains; its magma boils over"""
from common import *

CX, CY = 72, 64
LUG_ANGLES = (-118, -42, 42, 118)

SKINS = {
    # world: hub, hub shadow, hub highlight, light
    "space": (STEEL, STEEL_SH, STEEL_HI, CYAN),
}

# (core level, slot lit, hub scale, extra fx) -- 0..3 idle, 4..5 arming tell
FRAMES = [
    (0.2, False, 1.00, None),     # 0 dormant key pose (hold)
    (0.6, True, 1.00, None),      # 1 lights come up
    (1.0, True, 1.02, "conduit"), # 2 pulse: hot core, conduits lit
    (0.7, True, 1.00, None),      # 3 settle
    (1.2, True, 0.95, "arc"),     # 4 arming: hub pulls in, arcs crawl
    (1.6, True, 1.05, "burst"),   # 5 burst-ready: spikes punch out
]
IDLE_TICKS = [6, 2, 2, 4]
TELL_TICKS = [2, 2]


def rail_mine(world, i):
    """The approved rail bomb (Space). Kept parametric over SKINS."""
    hub_c, hub_sh, hub_hi, light = SKINS[world]
    level, lit, s, fx = FRAMES[i]
    p = Parts()
    slot = light if lit else DIM[light]
    S = lambda pts_: xf(pts_, CX, CY, s, s)

    # --- rail clamp + arm (gunmetal, every world) ---
    plate = chamfer_rect(13, 64, 14, 56, 4)
    arm = chamfer_rect(31, 64, 24, 18, 4)
    p.base += poly(plate, GUN) + poly(arm, GUN)
    p.shadow += poly([(13, 36), (20, 40), (20, 88), (13, 92)], GUN_SH) + poly([(19, 64), (43, 64), (43, 73), (19, 73)], GUN_SH)
    p.highlight += poly([(8, 40), (10, 37), (10, 70), (8, 70)], GUN_HI)
    p.ink += inkpoly(plate, 3.5) + inkpoly(arm, 3)
    p.base += poly(chamfer_rect(13, 64, 5, 34, 1.5), INK) + poly(chamfer_rect(30, 64, 10, 8, 2), INK)
    p.glow += poly(chamfer_rect(13, 64, 2.6, 30, 1), slot) + poly(chamfer_rect(30, 64, 7, 5, 1.5), slot)
    if world == "frost":   # icicles hang off the clamp
        for x, L in ((9, 14), (14, 20), (18, 10)):
            ic = [(x - 2.5, 91), (x + 2.5, 91), (x, 91 + L)]
            p.base += poly(ic, ICE_HI)
            p.ink += inkpoly(ic, 1.6)

    # --- lugs ---
    for k, a in enumerate(LUG_ANGLES):
        r = math.radians(a)
        lx, ly = CX + 38 * s * math.cos(r), CY + 38 * s * math.sin(r)
        lug = chamfer_rect(lx, ly, 22, 12, 3.5, rot=a + 90)
        lug_col = BARK_SH if world == "verdant" else GUN
        p.base += poly(lug, lug_col)
        half = chamfer_rect(lx, ly + 0, 22, 12, 3.5, rot=a + 90)[3:7]
        p.shadow += poly(half, GUN_SH)
        p.ink += inkpoly(lug, 3)
        win = chamfer_rect(lx, ly, 10, 3.6, 1, rot=a + 90)
        p.glow += poly(win, slot)
        if world == "verdant":   # magenta thorn tip on every lug
            tx, ty = CX + 52 * s * math.cos(r), CY + 52 * s * math.sin(r)
            n = (-math.sin(r) * 4, math.cos(r) * 4)
            th = [(lx + n[0] + math.cos(r) * 5, ly + n[1] + math.sin(r) * 5), (tx, ty),
                  (lx - n[0] + math.cos(r) * 5, ly - n[1] + math.sin(r) * 5)]
            p.base += poly(th, MAGENTA)
            p.shadow += poly([th[1], th[2], (lx + math.cos(r) * 6, ly + math.sin(r) * 6)], MAGENTA_SH)
            p.ink += inkpoly(th, 2)

    # --- hub ---
    hub = S(ngon(CX, CY, 36, 8, 22.5))
    p.base += poly(hub, hub_c)
    p.shadow += poly([hub[1], hub[2], hub[3], hub[4], hub[5], (CX, CY)], hub_sh)
    p.highlight += poly(S([(CX - 32, CY - 10), (CX - 14, CY - 31), (CX - 8, CY - 30), (CX - 27, CY - 8)]), hub_hi)
    p.ink += inkpoly(hub, 4)
    ring = S(ngon(CX, CY, 22, 8, 22.5))
    p.ink += inkpoly(ring, 2)
    for k in (0, 2, 4, 6):
        p.ink += line([hub[k], ring[k]], 2)

    if world == "frost":   # faceted crystal hub + a crystal horn
        for k in range(8):
            p.ink += line([ring[k], hub[(k + 1) % 8]], 1.4)
        horn = S([(CX + 6, CY - 30), (CX + 20, CY - 56), (CX + 18, CY - 28)])
        p.base += poly(horn, ICE_HI)
        p.shadow += poly(S([(CX + 20, CY - 56), (CX + 18, CY - 28), (CX + 13, CY - 30)]), ICE)
        p.ink += inkpoly(horn, 2.5)
    if world == "verdant":  # vines wrap the bark hub, leaves sprout
        for v in ([(CX - 34, CY + 8), (CX - 18, CY - 10), (CX + 2, CY + 4), (CX + 20, CY - 12), (CX + 34, CY - 4)],
                  [(CX - 26, CY + 26), (CX - 6, CY + 16), (CX + 12, CY + 28), (CX + 28, CY + 18)]):
            v = S(v)
            p.detail += line(v, 7)
            p.detail += line(v, 3.6, BILE)
        for lx, ly, a in ((CX - 18, CY - 10, -60), (CX + 20, CY - 12, -30), (CX + 12, CY + 28, 40)):
            leaf = xf([(lx, ly), (lx + 5, ly - 4), (lx + 13, ly), (lx + 5, ly + 4)], lx, ly, rot=a)
            p.detail += poly(leaf, BILE_HI)
            p.detail += inkpoly(leaf, 1.8)
    if world == "ember":   # magma cracks: dark when dormant, sodium when lit
        cracks = [[(CX - 30, CY - 6), (CX - 20, CY - 2), (CX - 16, CY + 8), (CX - 22, CY + 20)],
                  [(CX + 4, CY - 34), (CX + 8, CY - 24), (CX + 2, CY - 22)],
                  [(CX + 30, CY + 4), (CX + 22, CY + 10), (CX + 24, CY + 22), (CX + 14, CY + 30)]]
        for c in cracks:
            c = S(c)
            p.ink += line(c, 4.5)
            if lit:
                p.glow += line(c, 2.2, AMBER if level >= 1 else SODIUM)
                if level >= 1:
                    p.glow_back += line(c, 6, SODIUM, 'opacity="0.6" filter="url(#glowS)"')

    # --- core ---
    core(p, CX, CY, 11 * s, light, level)
    if world == "space" and level < 1.5:   # targeting crosshair
        for a, b in (((CX - 8, CY), (CX - 3, CY)), ((CX + 3, CY), (CX + 8, CY)),
                     ((CX, CY - 8), (CX, CY - 3)), ((CX, CY + 3), (CX, CY + 8))):
            p.glow += line([a, b], 1.4, INK)
    if world == "frost" and level >= 0.4:   # snowflake core
        for k in range(3):
            a = math.radians(k * 60)
            p.glow += line([(CX - 8 * math.cos(a), CY - 8 * math.sin(a)), (CX + 8 * math.cos(a), CY + 8 * math.sin(a))], 1.6, BONE)

    # --- fx ---
    if fx == "conduit":
        for k in (1, 3, 5, 7):
            p.glow += line([ring[k], hub[k]], 1.6, light)
    if fx == "arc":
        if world == "space":
            p.glow += arc_bolt([(CX - 30, CY - 34), (CX - 10, CY - 46), (CX + 6, CY - 38), (CX + 26, CY - 50), (CX + 44, CY - 30)], light)
            p.glow += arc_bolt([(CX + 46, CY + 6), (CX + 52, CY + 24), (CX + 38, CY + 34), (CX + 30, CY + 52)], light)
        elif world == "frost":
            for x, y, r in ((CX - 30, CY - 44, 6), (CX + 44, CY - 36, 5), (CX + 50, CY + 30, 7), (CX - 20, CY + 50, 5)):
                p.glow += spark(x, y, r, ICE_HI)
            p.glow += line([(CX - 46, CY - 6), (CX - 40, CY - 30), (CX - 20, CY - 46)], 2, ICE_HI, 'opacity="0.8"')
        elif world == "verdant":
            for x, y, a in ((CX - 30, CY - 46, 20), (CX + 44, CY - 40, -30), (CX + 50, CY + 36, 60), (CX - 14, CY + 52, 120)):
                leaf = xf([(x, y), (x + 5, y - 4), (x + 13, y), (x + 5, y + 4)], x, y, rot=a)
                p.glow += poly(leaf, BILE_LIGHT) + inkpoly(leaf, 1.4)
            p.glow += arc_bolt([(CX - 40, CY - 20), (CX - 24, CY - 44), (CX, CY - 50)], BILE_LIGHT)
        else:
            for x, y, r in ((CX - 30, CY - 46, 5), (CX + 40, CY - 44, 4), (CX + 52, CY + 26, 5), (CX - 8, CY + 52, 4)):
                e = ngon(x, y, r, 4, 45)
                p.glow += poly(e, AMBER) + inkpoly(e, 1.3)
            p.glow += arc_bolt([(CX - 40, CY - 24), (CX - 28, CY - 44), (CX - 4, CY - 50)], SODIUM)
    if fx == "burst":
        big = star(CX, CY, 56, 7, 4, 0)
        small = star(CX, CY, 40, 6, 4, 45)
        p.glow_back += halo(big, light, 0.7)
        p.glow += poly(small, light) + poly(big, light) + poly(star(CX, CY, 46, 3, 4, 0), BONE)
        p.glow += (f'<circle cx="{CX}" cy="{CY}" r="50" fill="none" stroke="{light}" stroke-width="2" opacity="0.8"/>')
    return p


# ------------------------------------------------------------------ Frost ----
def geode(i):
    """A tall hexagonal crystal geode held off the rail by two ice hooks.
    Arming: crystals grow out of it, then shoot out in a frost star."""
    level, lit, s, fx = FRAMES[i]
    grow = {None: 0, "conduit": 3, "arc": 8, "burst": 18}[fx]
    p = Parts()
    gx, gy = 72, 62
    S = lambda q: xf(q, gx, gy, s, s)
    # the rail bar and two crystal hooks that grip the geode
    bar = [(4, 26), (10, 22), (12, 104), (6, 108)]
    cel(p, bar, ICE_SH, INK, ICE, sh_off=(2, 0), ink_w=3)
    for y, d in ((40, 1), (88, -1)):
        hook = [(8, y - 6 * d), (40, y - 2 * d), (48, y + 8 * d), (38, y + 6 * d), (10, y + 4 * d)]
        cel(p, hook, ICE_HI, ICE, None, sh_off=(0, 3 * d), ink_w=3)
    # crystals growing out of the geode (behind it)
    for a, L, w in ((-60, 22, 12), (10, 26, 13), (70, 20, 11), (150, 16, 10)):
        r = math.radians(a - 90)
        bx, by = gx + 26 * math.cos(r), gy + 30 * math.sin(r)
        ln = L + grow * (1.0 if a != 150 else 0.6)
        pr = xf([(bx - w / 2, by), (bx - w / 2 * 0.8, by - ln * 0.7), (bx, by - ln), (bx + w / 2 * 0.8, by - ln * 0.7), (bx + w / 2, by)], bx, by, rot=a)
        cel(p, S(pr), ICE, ICE_SH, ICE_HI, sh_off=(w * 0.4, 2), ink_w=2.5)
    body = S([(gx, gy - 46), (gx + 26, gy - 26), (gx + 28, gy + 22), (gx, gy + 48), (gx - 26, gy + 24), (gx - 28, gy - 24)])
    cel(p, body, ICE, ICE_SH, ICE_HI, sh_off=(10, 6), detail=True)
    inner = S(ngon(gx, gy, 16, 6, 0))
    for a, b in zip(body, [inner[0], inner[1], inner[2], inner[3], inner[4], inner[5]]):
        p.detail += line([a, b], 1.6)
    p.detail += poly(inner, INK)
    core(p, gx, gy, 10 * s, CYAN, level, n=6, rot=0, socket=False)
    if level >= 0.4:   # snowflake heart
        for k in range(3):
            a = math.radians(k * 60 + 90)
            p.glow += line([(gx - 9 * math.cos(a), gy - 9 * math.sin(a)), (gx + 9 * math.cos(a), gy + 9 * math.sin(a))], 1.6, BONE)
    if fx == "arc":
        p.glow += line(S([(gx - 10, gy - 30), (gx - 4, gy - 18), (gx - 12, gy - 8)]), 1.6, BONE)
        p.glow += line(S([(gx + 12, gy + 18), (gx + 6, gy + 30)]), 1.6, BONE)
        for x, y in ((112, 20), (118, 96), (40, 116)):
            p.glow += spark(x, y, 5, ICE_HI)
    if fx == "burst":
        st = star(gx, gy, 58, 10, 6, 0)
        p.glow_back += halo(st, CYAN, 0.75)
        p.glow += poly(st, ICE_HI) + inkpoly(st, 2) + poly(star(gx, gy, 40, 6, 6, 30), CYAN)
    return p


# ---------------------------------------------------------------- Verdant ----
def burr(i):
    """A spiky seed burr hung from a coiled vine tendril. Arming: its husk
    splits along three glowing seams, then bursts into thorns and spores."""
    level, lit, s, fx = FRAMES[i]
    split = {None: 0, "conduit": 1, "arc": 5, "burst": 10}[fx]
    p = Parts()
    bx, by = 74, 64
    # tendril from the rail, curling onto the burr's stalk
    tendril = [(2, 22), (14, 18), (24, 26), (18, 36), (28, 44), (42, 40), (50, 50)]
    p.ink += line(tendril, 8)
    p.ink += line(tendril, 4.4, BILE)
    low = [(2, 104), (16, 110), (28, 100), (40, 86)]
    p.ink += line(low, 7)
    p.ink += line(low, 3.6, BILE)
    for x, y, a in ((14, 18, -40), (28, 100, 200), (24, 26, 60)):
        lf = xf([(x, y), (x - 4, y - 7), (x, y - 14), (x + 4, y - 7)], x, y, rot=a)
        p.ink += poly(lf, BILE_HI) + inkpoly(lf, 1.6)
    # the husk: three segments that pull apart from the centre
    for k in range(3):
        a0 = k * 120 - 90
        mid = math.radians(a0 + 60)
        ox, oy = split * math.cos(mid), split * math.sin(mid)
        seg = [(bx + ox, by + oy)]
        for t in range(9):
            a = math.radians(a0 + t * 15)
            r = 36 if t % 2 == 0 else 30
            seg.append((bx + ox + r * s * math.cos(a), by + oy + r * s * math.sin(a)))
        cel(p, seg, BARK, BARK_SH, BARK_HI, sh_off=(6, 6), ink_w=3, detail=True)
        for t in range(0, 9, 2):   # thorns off the husk
            a = math.radians(a0 + t * 15)
            tx, ty = bx + ox + 34 * s * math.cos(a), by + oy + 34 * s * math.sin(a)
            th = [(tx - 4 * math.sin(a), ty + 4 * math.cos(a)),
                  (tx + (14 + split * 0.6) * math.cos(a), ty + (14 + split * 0.6) * math.sin(a)),
                  (tx + 4 * math.sin(a), ty - 4 * math.cos(a))]
            p.detail += poly(th, MAGENTA if t % 4 == 0 else BILE_HI) + inkpoly(th, 1.6)
    # glowing seams between the segments
    for k in range(3):
        a = math.radians(k * 120 - 90)
        seam = [(bx, by), (bx + 34 * math.cos(a), by + 34 * math.sin(a))]
        p.detail += line(seam, 4 + split * 0.8)
        p.glow += line(seam, 1.6 + split * 0.4, BILE_LIGHT if lit else BILE_SH)
    core(p, bx, by, (7 + split * 0.5) * s, BILE_LIGHT, level, n=6, rot=0)
    if fx == "arc":
        for x, y, r in ((116, 30, 6), (114, 104, 5), (60, 118, 5)):
            puff = ngon(x, y, r, 5, 15)
            p.glow += poly(puff, BILE_LIGHT) + inkpoly(puff, 1.4)
    if fx == "burst":
        st = star(bx, by, 56, 9, 9, 10)
        p.glow_back += halo(st, BILE_LIGHT, 0.7)
        p.glow += poly(st, BILE_LIGHT) + poly(star(bx, by, 36, 6, 9, 30), BONE)
    return p


# ------------------------------------------------------------------ Ember ----
def crucible(i):
    """A basalt crucible slung from the rail on chains. Its magma breathes
    under the rim; arming, it boils over and erupts."""
    level, lit, s, fx = FRAMES[i]
    boil = {None: 0, "conduit": 3, "arc": 7, "burst": 12}[fx]
    p = Parts()
    # chain from the rail to the crucible's ear
    for k in range(3):
        x = 8 + k * 11
        link = chamfer_rect(x, 50, 12, 7, 2.5)
        p.base += poly(link, GUN)
        p.ink += inkpoly(link, 2.5) + poly(chamfer_rect(x, 50, 5, 2.4, 0.8), INK)
    hookbar = [(2, 28), (8, 26), (8, 74), (2, 76)]
    cel(p, hookbar, GUN, GUN_SH, GUN_HI, sh_off=(2, 0), ink_w=3)
    # the crucible: wide rim, tapering bowl, two lugs
    bowl = [(36, 36), (112, 36), (106, 60), (96, 100), (84, 112), (64, 112), (52, 100), (42, 60)]
    cel(p, S_(bowl, s), CHAR, CHAR_SH, CHAR_HI, sh_off=(12, 6))
    for x in (40, 108):
        ear = chamfer_rect(x, 48, 10, 14, 3)
        cel(p, ear, GUN, GUN_SH, GUN_HI, sh_off=(2, 2), ink_w=2.5, detail=True)
    rim = chamfer_rect(74, 34, 84, 12, 4)
    cel(p, S_(rim, s), CHAR_HI, CHAR, None, sh_off=(0, 4), ink_w=3, detail=True)
    # magma surface inside the rim, bulging as it boils
    hot = (AMBER if level >= 1 else SODIUM) if lit else SODIUM_SH
    surf = [(38, 30), (56, 26 - boil * 0.4), (74, 22 - boil), (92, 26 - boil * 0.5), (110, 30), (104, 34), (44, 34)]
    p.glow += poly(surf, hot) + inkpoly(surf, 2.5)
    if level >= 1:
        p.glow_back += halo(surf, SODIUM, 0.75)
        for x, y, r in ((58, 22 - boil, 3.5), (86, 20 - boil, 3)):
            p.glow += poly(ngon(x, y, r, 6, 0), BONE) + inkpoly(ngon(x, y, r, 6, 0), 1.2)
    # cracks down the bowl and the core window
    for c in ([(56, 44), (62, 62), (56, 78)], [(94, 46), (88, 66), (96, 84)]):
        p.detail += line(c, 4.5)
        p.glow += line(c, 2, hot)
    win = [(66, 60), (82, 60), (88, 76), (74, 92), (60, 76)]
    p.detail += poly(win, INK)
    core(p, 74, 74, 8 * s, SODIUM, level, n=5, rot=0, socket=False)
    if fx == "arc":   # magma drips over the rim
        for x, L in ((50, 14), (100, 10)):
            d = [(x - 3, 36), (x + 3, 36), (x, 36 + L)]
            p.glow += poly(d, SODIUM) + inkpoly(d, 1.4)
    if fx == "burst":   # eruption
        flame = [(40, 34), (50, 2), (60, 24), (74, -4), (88, 22), (98, 4), (108, 34)]
        p.glow_back += halo(flame, SODIUM, 0.8)
        p.glow += poly(flame, SODIUM) + poly(lerp_pts(flame, [(74, 34)] * 7, 0.45), AMBER) + inkpoly(flame, 2.5)
        for x, y in ((24, 10), (120, 16), (118, 96), (30, 116)):
            e = ngon(x, y, 4, 4, 45)
            p.glow += poly(e, AMBER) + inkpoly(e, 1.3)
    return p


def S_(q, s, cx=74, cy=74):
    return xf(q, cx, cy, s, s)


def draw(world, i):
    if world == "frost":
        return geode(i)
    if world == "verdant":
        return burr(i)
    if world == "ember":
        return crucible(i)
    return rail_mine(world, i)
