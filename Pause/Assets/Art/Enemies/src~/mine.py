"""Rail mine, one per world: the approved rail_bomb_themes_atlas design
(rail clamp on the left, armoured hub, four lugs, glowing core, arming
build-up to a burst), redrawn as flat ink cels. The clamp side faces the wall
it hangs on; the game flips the sprite for right-hand rails."""
from common import *

CX, CY = 72, 64
LUG_ANGLES = (-118, -42, 42, 118)

SKINS = {
    # world: hub, hub shadow, hub highlight, light
    "space": (STEEL, STEEL_SH, STEEL_HI, CYAN),
    "frost": (ICE, ICE_SH, ICE_HI, CYAN),
    "verdant": (BARK, BARK_SH, BARK_HI, BILE_LIGHT),
    "ember": (CHAR, CHAR_SH, CHAR_HI, SODIUM),
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


def draw(world, i):
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
