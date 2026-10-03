"""Sprite bodies (no <svg> wrapper) for the Akira sample sheet.

Each function returns an SVG fragment authored in its own viewBox (noted in the
docstring). Animated assets take a frame index and are parametric: the frame
table at the top of each function is the whole animation.
"""
from akira import *

# =================================================================== PLAYER ==
# viewBox 128 x 128, nose up. Light comes from the upper left.
SHIP_HALF = [(64, 4), (59, 18), (56, 36), (53, 50), (46, 57), (12, 86), (8, 98),
             (18, 101), (42, 95), (44, 104), (47, 118), (58, 118), (60, 108), (64, 110)]
SHIP = mirror(SHIP_HALF, 64)
NOZZLES = [(53, 118), (75, 118)]


def ship(lights_on=True):
    """Player default hull ('Neon Comet' redrawn). viewBox 128x128."""
    base = poly(SHIP, RED)
    # Gunmetal engine pods + wing roots (secondary material)
    podL = [(44, 96), (46, 104), (47, 118), (58, 118), (60, 108), (58, 92), (50, 88)]
    base += poly(podL, GUN) + poly(mx(podL, 64), GUN)
    # Off-white racing stripes across each wing (Akira bike livery)
    stripeL = [(24, 81), (30, 77), (38, 93), (31, 95)]
    base += poly(stripeL, BONE) + poly(mx(stripeL, 64), BONE)

    # Shadow: right half of the fuselage, trailing edges of both wings
    sh = poly([(64, 4), (69, 18), (72, 36), (75, 50), (76, 88), (64, 110)], RED_SH)
    sh += poly([(12, 86), (8, 98), (18, 101), (42, 95), (40, 88)], RED_SH)
    sh += poly([(116, 86), (120, 98), (110, 101), (86, 95), (82, 82), (100, 75)], RED_SH)
    sh += poly(mx(podL, 64)[:4] + [(70, 96)], GUN_SH)
    sh += poly([(31, 95), (38, 93), (36, 89), (29, 90)], "#B9AE98")  # stripe in shadow

    # Highlight kicks: hard off-white slivers on lit edges
    hi = poly([(64, 6), (60, 18), (58, 32), (61, 20)], BONE)
    hi += poly([(46, 58), (14, 86), (20, 85), (48, 61)], BONE)
    hi += poly([(46, 98), (49, 115), (51, 115), (48, 98)], GUN_HI)

    # Canopy: teal glass, flat, one shadow, one kick
    can = [(64, 22), (59, 34), (59, 48), (64, 54), (69, 48), (69, 34)]
    base += poly(can, TEAL)
    sh += poly([(64, 22), (69, 34), (69, 48), (64, 54)], TEAL_SH)
    hi += poly([(62, 28), (60.5, 34), (61, 44), (62.5, 34)], BONE)

    ink = inkpoly(SHIP, 4)
    ink += inkpoly(can, 2.5)
    ink += line([(64, 54), (64, 106)], 2)                                       # spine
    ink += line([(53, 50), (52, 88)], 2) + line([(75, 50), (76, 88)], 2)        # wing roots
    ink += inkpoly(podL, 2) + inkpoly(mx(podL, 64), 2)
    ink += line([(24, 81), (30, 77), (38, 93), (31, 95), (24, 81)], 1.8)
    ink += line([(104, 81), (98, 77), (90, 93), (97, 95), (104, 81)], 1.8)
    ink += line([(30, 68), (38, 80)], 1.5) + line([(98, 68), (90, 80)], 1.5)    # panel ticks
    # nozzle mouths
    for cx, cy in NOZZLES:
        ink += poly([(cx - 5, cy - 3), (cx + 5, cy - 3), (cx + 4, cy + 2), (cx - 4, cy + 2)], INK)

    glow = ""
    if lights_on:
        glow += glow_dot(10, 97, 2.2, AMBER) + glow_dot(118, 97, 2.2, AMBER)
        for cx, cy in NOZZLES:
            glow += poly([(cx - 3.5, cy - 1.5), (cx + 3.5, cy - 1.5), (cx + 3, cy + 1.5), (cx - 3, cy + 1.5)], AMBER)
    return layers(base, sh, hi, ink, glow)


# Exhaust light-trail flipbook. Frames: (length, width, smear, flare)
EXHAUST_FRAMES = [
    (58, 1.00, False, 0.6),   # 0 key: rest length
    (74, 1.05, False, 0.8),   # 1 stretch
    (118, 0.70, True, 1.0),   # 2 smear: long, thin, speed lines (the Akira streak)
    (86, 0.90, False, 0.9),   # 3 recoil
    (64, 1.10, False, 0.7),   # 4 squash: short and fat
    (70, 1.00, False, 0.75),  # 5 settle -> loops to 0
]
EXHAUST_TIMING = [2, 2, 1, 2, 2, 2]  # in 24 fps ticks ("on 2s", smear on 1)


def exhaust(i, top=118):
    """Tail-light streaks from both nozzles. Authored in the ship's 128 space;
    extends below the hull (use a 128x256 canvas)."""
    L, wmul, smear, flare = EXHAUST_FRAMES[i % len(EXHAUST_FRAMES)]
    back, body = "", ""
    for cx, _ in NOZZLES:
        w = 7.5 * wmul
        outer = [(cx - w, top), (cx + w, top), (cx + w * 0.55, top + L * 0.55), (cx, top + L), (cx - w * 0.55, top + L * 0.55)]
        mid = [(cx - w * 0.62, top), (cx + w * 0.62, top), (cx + w * 0.3, top + L * 0.5), (cx, top + L * 0.78), (cx - w * 0.3, top + L * 0.5)]
        core = [(cx - w * 0.3, top), (cx + w * 0.3, top), (cx, top + L * 0.45)]
        back += poly(outer, RED, f'opacity="{0.55 * flare}" filter="url(#glowM)"')
        body += poly(outer, RED) + poly(mid, SODIUM) + poly(core, BONE)
        # hard shock diamond: 2-frame cel shape that rides the streak
        dy = top + L * (0.22 if i % 2 == 0 else 0.3)
        body += poly([(cx, dy - 4), (cx + 2.6, dy), (cx, dy + 4), (cx - 2.6, dy)], BONE)
    lines = ""
    if smear:
        for k, x in enumerate([40, 46, 82, 88, 64]):
            y0 = top + 10 + k * 7
            lines += line([(x, y0), (x, y0 + 60 + k * 9)], 1.4, CYAN, 'opacity="0.8"')
        # smear frame: a second, ghosted streak pair offset backwards
        for cx, _ in NOZZLES:
            body += poly([(cx - 2, top + 20), (cx + 2, top + 20), (cx, top + L + 22)], RED_HI, 'opacity="0.85"')
    return back, body + lines


def ship_with_exhaust(i):
    back, trail = exhaust(i)
    return f'<g id="glow-back">{back}</g><g id="trail">{trail}</g>' + ship()


# =================================================================== ENEMIES =
# Common fighter. viewBox 128 x 104, nose DOWN (enemies fly toward the player).
def fighter_outline(spread=0.0, sx=1.0, sy=1.0, dy=0.0):
    half = [(64, 14), (52, 14), (40, 6), (8, 18), (4, 32), (22, 40),
            (30 - spread, 60), (36 - spread * 1.6, 96), (44 - spread * 0.6, 92),
            (46, 70), (54, 66), (58, 84), (64, 90)]
    return xf(mirror(half, 64), 64, 52, sx, sy, 0, dy)


FIGHTER_FRAMES = [  # (prong spread, sx, sy, dy, visor 0..1) -- idle with a "threat" beat
    (0, 1.00, 1.00, 0, 0.55),     # 0 key pose (hold)
    (-3, 1.04, 0.94, 2, 0.35),    # 1 anticipation: squash, prongs pinch, visor dims
    (5, 0.97, 1.06, -2, 1.00),    # 2 snap: stretch, prongs flare, visor flares
    (2, 1.00, 1.00, -1, 0.75),    # 3 settle
]
FIGHTER_TIMING = [6, 2, 3, 3]


def fighter(i=0):
    spread, sx, sy, dy, vis = FIGHTER_FRAMES[i % len(FIGHTER_FRAMES)]
    T = lambda p: xf(p, 64, 52, sx, sy, 0, dy)
    out = fighter_outline(spread, sx, sy, dy)
    base = poly(out, STEEL)
    wingL = [(40, 8), (10, 19), (7, 30), (22, 37), (34, 30)]
    base += poly(T(wingL), BRUISE) + poly(T(mx(wingL, 64)), BRUISE)
    # one hard shadow tone: the right half + undersides of the prongs
    sh = poly(T([(64, 14), (76, 14), (82, 30), (82, 66), (74, 66), (70, 84), (64, 90)]), STEEL_SH)
    sh += poly(T(mx(wingL, 64)), BRUISE_SH)
    pr = [(98 + spread, 60), (92 + spread * 1.6, 96), (84 + spread * 0.6, 92), (82, 70)]
    sh += poly(T(pr), STEEL_SH)
    hi = poly(T([(52, 15), (40, 8), (12, 19), (14, 21), (40, 11)]), STEEL_HI)
    hi += poly(T([(31 - spread, 62), (36 - spread * 1.6, 92), (34 - spread, 62)]), STEEL_HI)
    visor = T([(52, 42), (76, 42), (72, 52), (56, 52)])
    base += poly(visor, INK)
    ink = inkpoly(out, 4)
    ink += inkpoly(T(wingL), 2) + inkpoly(T(mx(wingL, 64)), 2)
    ink += line(T([(64, 16), (64, 40)]), 2) + line(T([(64, 54), (64, 86)]), 2)
    ink += line(T([(46, 70), (40, 44), (52, 42)]), 2) + line(T([(82, 70), (88, 44), (76, 42)]), 2)
    ink += inkpoly(visor, 2.5)
    vcol = MAGENTA if vis > 0.5 else MAGENTA_SH
    glow = poly(T([(55, 44), (73, 44), (70.5, 50), (57.5, 50)]), vcol)
    glow += poly(T([(58, 45), (64, 45), (62, 49), (59, 49)]), BONE, f'opacity="{vis}"')
    if vis > 0.7:
        glow = poly(T([(48, 40), (80, 40), (74, 55), (54, 55)]), MAGENTA, f'opacity="{vis * 0.7}" filter="url(#glowM)"') + glow
    return layers(base, sh, hi, ink, glow)


# Alien. viewBox 128 x 128. Sickly bile carapace, one eye, mandible blades.
ALIEN_FRAMES = [  # (mandible open deg, sy, dy, eye lid 0 open..1 shut, flare)
    (10, 1.00, 0, 0.0, False),   # 0 key
    (-6, 0.92, 5, 0.2, False),   # 1 anticipation: crouch, mandibles close
    (30, 1.08, -6, 0.0, True),   # 2 snap: stretch, mandibles wide, eye flare
    (16, 1.00, -1, 0.75, False), # 3 settle + blink
]
ALIEN_TIMING = [4, 2, 3, 3]


def alien(i=0):
    ang, sy, dy, lid, flare = ALIEN_FRAMES[i % len(ALIEN_FRAMES)]
    T = lambda p: xf(p, 64, 70, 1 / sy ** 0.5, sy, 0, dy)
    head_half = [(64, 18), (56, 30), (42, 26), (36, 40), (24, 46), (30, 60), (34, 80), (48, 92), (64, 96)]
    head = T(mirror(head_half, 64))
    crestL = [(56, 30), (46, 6), (42, 26)]
    crestC = [(60, 22), (64, 2), (68, 22), (64, 26)]
    # mandibles pivot at (44, 88) / (84, 88)
    mandL0 = [(48, 86), (40, 92), (36, 112), (46, 124), (46, 108), (54, 94)]
    mandL = T(xf(mandL0, 46, 88, rot=ang))
    mandR = T(xf(mx(mandL0, 64), 82, 88, rot=-ang))
    base = poly(T(crestL), BILE_SH) + poly(T(mx(crestL, 64)), BILE_SH) + poly(T(crestC), BRUISE)
    base += poly(mandL, BRUISE) + poly(mandR, BRUISE)
    base += poly(head, BILE)
    plateL = [(42, 26), (36, 40), (24, 46), (30, 60), (44, 52), (50, 36)]
    base += poly(T(plateL), BILE) + poly(T(mx(plateL, 64)), BILE)
    sh = poly(T([(64, 18), (72, 30), (86, 26), (92, 40), (104, 46), (98, 60), (94, 80), (80, 92), (64, 96), (64, 76), (78, 66)]), BILE_SH)
    sh += poly(T([(48, 92), (64, 96), (64, 84), (52, 80)]), BILE_SH)
    sh += poly(T(xf([(46, 108), (46, 124), (42, 120)], 46, 88, rot=ang)), BRUISE_SH)
    sh += poly(T([(64, 2), (68, 22), (64, 26)]), BRUISE_SH)
    hi = poly(T([(42, 28), (37, 40), (26, 46), (38, 42)]), BILE_HI)
    hi += poly(T([(56, 30), (47, 9), (52, 30)]), BILE_HI)
    # eye: hard hexagon socket, lit
    sock = T([(50, 52), (64, 44), (78, 52), (74, 66), (64, 70), (54, 66)])
    base += poly(sock, INK)
    eye = T([(54, 53), (64, 48), (74, 53), (71, 63), (64, 66), (57, 63)])
    glow = ""
    if flare:
        glow += poly(T([(40, 40), (88, 40), (84, 76), (44, 76)]), BILE_LIGHT, 'opacity="0.55" filter="url(#glowM)"')
    glow += poly(eye, BILE_LIGHT)
    glow += poly(T([(62, 52), (66, 52), (65.5, 62), (62.5, 62)]), INK)  # slit pupil
    if lid > 0:
        y0 = 47 + (66 - 47) * lid
        glow += poly(T([(52, 46), (76, 46), (76, y0), (52, y0)]), BILE_SH)
        glow += line(T([(53, y0), (75, y0)]), 2.2)
    ink = inkpoly(head, 4) + inkpoly(mandL, 3) + inkpoly(mandR, 3)
    ink += inkpoly(T(crestL), 3) + inkpoly(T(mx(crestL, 64)), 3) + inkpoly(T(crestC), 3)
    ink += inkpoly(T(plateL), 2) + inkpoly(T(mx(plateL, 64)), 2)
    ink += inkpoly(sock, 2.5)
    ink += line(T([(52, 80), (64, 84), (76, 80)]), 2)
    ink += line(T([(40, 66), (48, 72)]), 1.6) + line(T([(88, 66), (80, 72)]), 1.6)
    # the eye light sits inside its ink socket, so the socket is re-inked on top
    return layers(base, sh, hi, ink, glow + inkpoly(sock, 2.5))


# Rail mine. viewBox 128 x 128. Bruise-violet hub, steel spikes, magenta core.
MINE_FRAMES = [  # (spike length, hub scale, core 0..1, rot)
    (20, 1.00, 0.45, 0),     # 0 key (hold)
    (11, 0.92, 0.20, 0),     # 1 anticipation: spikes retract, hub squashes
    (30, 1.06, 1.00, 6),     # 2 pop: spikes punch out, core flashes white
    (22, 1.00, 0.70, 3),     # 3 settle
]
MINE_TIMING = [6, 2, 2, 4]


def mine(i=0):
    L, hs, core, rot = MINE_FRAMES[i % len(MINE_FRAMES)]
    cx = cy = 64
    spikes_b, spikes_s, spikes_i = "", "", ""
    for k in range(8):
        a = math.radians(rot + k * 45 - 90)
        r0 = 30 * hs
        tip = (cx + (r0 + L) * math.cos(a), cy + (r0 + L) * math.sin(a))
        p1 = (cx + r0 * math.cos(a - 0.22), cy + r0 * math.sin(a - 0.22))
        p2 = (cx + r0 * math.cos(a + 0.22), cy + r0 * math.sin(a + 0.22))
        spikes_b += poly([p1, tip, p2], STEEL)
        spikes_s += poly([(cx + r0 * math.cos(a), cy + r0 * math.sin(a)), tip, p2], STEEL_SH)
        spikes_i += inkpoly([p1, tip, p2], 3)
    hub = ngon(cx, cy, 34 * hs, 8, rot + 22.5)
    inner = ngon(cx, cy, 22 * hs, 8, rot + 22.5)
    base = spikes_b + poly(hub, BRUISE) + poly(inner, BRUISE_SH)
    sh = spikes_s + poly([hub[2], hub[3], hub[4], hub[5], (cx, cy)], BRUISE_SH)
    hi = poly([hub[6], hub[7], hub[0], (cx - 4, cy - 26 * hs), (cx - 22 * hs, cy - 10)], BRUISE_HI)
    ink = spikes_i + inkpoly(hub, 4) + inkpoly(inner, 2.5)
    for k in range(0, 8, 2):
        ink += line([hub[k], inner[k]], 2)
    corep = ngon(cx, cy, 12 * hs, 4, rot + 45)
    ccol = MAGENTA if core > 0.4 else MAGENTA_SH
    glow = ""
    if core > 0.6:
        glow += f'<circle cx="64" cy="64" r="{f(26 * core)}" fill="{MAGENTA}" opacity="0.7" filter="url(#glowM)"/>'
    glow += poly(corep, BONE if core >= 1 else ccol) + inkpoly(corep, 2.5)
    glow += poly(ngon(cx, cy, 5 * hs, 4, rot + 45), BONE if core > 0.4 else MAGENTA)
    return layers(base, sh, hi, ink, glow)


# ================================================================= HAZARDS ==
ROCK_OUT = [(48, 10), (78, 8), (104, 24), (120, 52), (112, 86), (90, 114), (54, 120), (24, 104), (8, 74), (14, 38)]


def asteroid():
    """Faceted rock. viewBox 128x128. Amber city-glow rim on the lit edge."""
    base = poly(ROCK_OUT, ROCK)
    sh = poly([(120, 52), (112, 86), (90, 114), (54, 120), (24, 104), (40, 96), (70, 98), (92, 80), (100, 56)], ROCK_SH)
    hi = poly([(14, 38), (48, 10), (78, 8), (70, 16), (44, 20), (22, 42)], ROCK_HI)
    rim = poly([(8, 74), (14, 38), (48, 10), (52, 14), (20, 42), (14, 72)], AMBER)
    # craters: angular sockets with a hard inner shadow
    craters = [((44, 58), 13), ((80, 40), 9), ((76, 84), 11), ((36, 88), 6)]
    cb, cs, ci = "", "", ""
    for (x, y), r in craters:
        c = ngon(x, y, r, 6, 15, 1, 0.8)
        cb += poly(c, ROCK_SH)
        cs += poly([c[4], c[5], c[0], (x + r * 0.2, y)], INK, 'opacity="0.55"')
        ci += inkpoly(c, 2)
        cb += poly([c[1], c[2], c[3], (x + r * 0.3, y + r * 0.1)], ROCK_HI)
    ink = inkpoly(ROCK_OUT, 4) + ci
    ink += line([(60, 24), (66, 40), (58, 46)], 1.6) + line([(98, 64), (90, 70)], 1.6)
    return layers(base + cb, sh + cs, hi + rim, ink)


# Explosion / impact. viewBox 128x128. Hard cel shapes, ink outlines.
EXPLOSION_TIMING = [1, 2, 2, 2, 3, 3]


def explosion(i):
    cx = cy = 64
    out = ""
    if i == 0:  # impact frame: flat white flash, squashed burst, red ink
        b = star(cx, cy, 46, 18, 8, 0, [1, 1, 0.8, 1, 1.1, 1, 0.75, 1])
        b = xf(b, cx, cy, 1.15, 0.85)
        out += poly(b, BONE) + inkpoly(b, 4, RED)
        out += poly(star(cx, cy, 16, 7, 4, 45), RED)
    elif i == 1:  # burst: stretch, white core, red rim, debris launched
        b = star(cx, cy, 58, 26, 10, 9, [1, 1, 0.82, 1, 1.12, 1, 0.9, 1, 1.05, 1])
        m = star(cx, cy, 40, 20, 10, 27)
        c = star(cx, cy, 22, 12, 6, 0)
        out += f'<circle cx="64" cy="64" r="46" fill="{SODIUM}" opacity="0.6" filter="url(#glowL)"/>'
        out += poly(b, RED) + poly(m, AMBER) + poly(c, BONE) + inkpoly(b, 4)
        for k in range(6):
            a = math.radians(k * 60 + 20)
            x, y = cx + 52 * math.cos(a), cy + 52 * math.sin(a)
            sh = xf([(x - 3, y - 5), (x + 4, y - 2), (x + 2, y + 5), (x - 4, y + 3)], x, y, rot=k * 40)
            out += poly(sh, AMBER) + inkpoly(sh, 2)
    elif i == 2:  # fireball: angular lobes, 3 flat tones
        lobes = [(-14, -12, 30), (16, -14, 26), (20, 14, 28), (-16, 16, 26), (0, 0, 34)]
        for dx, dy, r in lobes:
            out += poly(ngon(cx + dx, cy + dy, r, 7, dx * 3), RED)
        for dx, dy, r in lobes:
            out += inkpoly(ngon(cx + dx, cy + dy, r, 7, dx * 3), 8)
        for dx, dy, r in lobes:
            out += poly(ngon(cx + dx, cy + dy, r, 7, dx * 3), RED)
        for dx, dy, r in lobes[:4]:
            out += poly(ngon(cx + dx * 0.7 - 3, cy + dy * 0.7 - 3, r * 0.6, 6, 10), SODIUM)
        out += poly(ngon(cx - 6, cy - 6, 15, 6), AMBER) + poly(ngon(cx - 8, cy - 9, 6, 4), BONE)
        for k in range(7):
            a = math.radians(k * 51 + 5)
            x, y = cx + 60 * math.cos(a), cy + 60 * math.sin(a)
            sh = xf([(x - 3, y - 4), (x + 3, y - 2), (x + 1, y + 4)], x, y, rot=k * 50)
            out += poly(sh, SODIUM) + inkpoly(sh, 1.8)
    elif i == 3:  # breakup: shockwave ring + darkening chunks
        out += f'<circle cx="64" cy="64" r="58" fill="none" stroke="{BONE}" stroke-width="3"/>'
        out += f'<circle cx="64" cy="64" r="58" fill="none" stroke="{INK}" stroke-width="1.2" transform="translate(0 0)"/>'
        # fire tears apart into jagged shards flung outward (stretched along travel)
        for dx, dy, r, col in [(-26, -18, 20, RED_SH), (24, -22, 17, RED), (22, 24, 19, RED_SH), (-22, 22, 15, RED), (0, 0, 17, SODIUM)]:
            ang = math.degrees(math.atan2(dy, dx)) if (dx or dy) else 0
            p = star(cx + dx, cy + dy, r, r * 0.62, 4, ang + 45, [1.3, 1, 0.8, 1, 1.1, 1, 0.9, 1])
            out += poly(p, col) + inkpoly(p, 3.5)
        out += poly(star(cx + 2, cy - 2, 9, 4, 4, 20), AMBER) + inkpoly(star(cx + 2, cy - 2, 9, 4, 4, 20), 2)
    elif i == 4:  # smoke cels: dusk violet, angular puffs, few embers
        for dx, dy, r, col in [(-22, -16, 20, DUSK), (20, -20, 16, INDIGO_1), (18, 18, 18, DUSK), (-14, 20, 15, INDIGO_1), (2, -2, 14, DUSK)]:
            p = ngon(cx + dx, cy + dy, r, 7, dx * 5)
            out += poly(p, col) + poly(xf(p, cx + dx, cy + dy, 0.55, 0.55, 3, 4), INDIGO_0) + inkpoly(p, 3.5)
        for x, y in [(30, 40), (96, 56), (70, 100), (44, 92)]:
            out += poly(ngon(x, y, 4, 4, 45), SODIUM) + inkpoly(ngon(x, y, 4, 4, 45), 1.6)
    else:  # dissipate: small chunks drifting out
        for dx, dy, r in [(-34, -24, 10), (30, -30, 8), (32, 26, 9), (-26, 30, 7)]:
            p = ngon(cx + dx, cy + dy, r, 6, dx * 5)
            out += poly(p, INDIGO_0, 'opacity="0.9"') + inkpoly(p, 2.5, INK, 'opacity="0.9"')
        out += poly(ngon(96, 40, 2.5, 4, 45), SODIUM)
    return out


# ================================================================= PICKUPS ==
# viewBox 64x64.
STARDUST_FRAMES = [(1.0, True), (0.55, False), (0.12, False), (-0.55, False)]  # (x scale, glint)
STARDUST_TIMING = [4, 2, 1, 2]


def stardust(i=0):
    sx, glint = STARDUST_FRAMES[i % len(STARDUST_FRAMES)]
    s = abs(sx)
    T = lambda p: xf(p, 32, 32, sx if abs(sx) > 0.05 else 0.08, 1)
    shape = [(32, 4), (39, 25), (58, 32), (39, 39), (32, 60), (25, 39), (6, 32), (25, 25)]
    out = T(shape)
    base = poly(out, AMBER)
    sh = poly(T([(32, 32), (58, 32), (39, 39), (32, 60)]), SODIUM_SH) + poly(T([(32, 32), (39, 25), (58, 32)]), SODIUM)
    hi = poly(T([(32, 6), (26, 25), (31, 30)]), BONE) if s > 0.3 else ""
    ink = inkpoly(out, 3.2)
    if s < 0.2:  # smear: edge-on, a bright vertical cut
        base = poly(T(shape), BONE)
        ink = line([(32, 2), (32, 62)], 3.2) + poly([(30.5, 6), (33.5, 6), (33.5, 58), (30.5, 58)], BONE)
    glow = f'<polygon points="{pts(out)}" fill="{AMBER}" opacity="0.45" filter="url(#glowS)"/>'
    if glint:
        g = star(50, 12, 9, 1.6, 4)
        hi += poly(g, BONE) + inkpoly(g, 1.2)
    return layers(base, sh, hi, ink, glow_back=glow)


HEAL_FRAMES = [(1.0, 1.0, 0.0), (1.06, 0.92, 0.0), (0.94, 1.08, 1.0), (1.0, 1.0, 0.4)]  # (sx, sy, flash)
HEAL_TIMING = [5, 2, 2, 3]


def heal(i=0):
    sx, sy, fl = HEAL_FRAMES[i % len(HEAL_FRAMES)]
    T = lambda p: xf(p, 32, 34, sx, sy)
    hexo = T(ngon(32, 32, 27, 6, 0))
    hexi = T(ngon(32, 32, 17, 6, 0))
    cross = T([(28, 20), (36, 20), (36, 28), (44, 28), (44, 36), (36, 36), (36, 44), (28, 44), (28, 36), (20, 36), (20, 28), (28, 28)])
    base = poly(hexo, TEAL_SH) + poly(hexi, HEAL)
    sh = poly([hexo[1], hexo[2], hexo[3], hexi[3], hexi[2], hexi[1]], HEAL_SH)
    sh += poly([hexi[1], hexi[2], hexi[3], T([(32, 32)])[0]], HEAL_SH, 'opacity="0.6"')
    hi = poly([hexo[5], hexo[0], hexi[0], hexi[5]], TEAL_SH) + poly(T([(22, 20), (28, 15), (24, 22)]), BONE)
    hi += poly(cross, BONE if fl < 1 else BONE)
    # chamfer bolts on the shell = "energy cell", not a bubble
    for k in (0, 2, 4):
        hi += poly(T(ngon(*ngon(32, 32, 22, 6, 0)[k], 2.2, 4, 45)), HEAL)
    ink = inkpoly(hexo, 3.2) + inkpoly(hexi, 2.2) + inkpoly(cross, 2)
    for a, b in zip(hexo, hexi):
        ink += line([a, b], 1.6)
    glow_back = poly(hexo, HEAL, f'opacity="{0.35 + 0.4 * fl}" filter="url(#glowM)"')
    glow = poly(hexi, BONE, f'opacity="{0.55 * fl}"') + (inkpoly(cross, 2) if fl else "")
    return layers(base, sh, hi, ink, glow, glow_back)


# =================================================================== ROBOT ==
# Tutorial guide. viewBox 128 x 124 (portrait, replaces contra2.png 110x107).
ROBOT_MOUTH = [0, 7, 14]          # jaw drop per frame: closed, mid, open
ROBOT_TIMING = [3, 2, 3]


def robot(i=0):
    drop = ROBOT_MOUTH[i % 3]
    ARM, ARM_SH, ARM_HI = "#B7B4C8", "#5E5A7A", BONE
    helm_half = [(64, 4), (42, 10), (26, 28), (22, 60), (36, 80)]
    helm = mirror(helm_half + [(64, 82)], 64)
    jaw_half = [(64, 78 + drop), (44, 78 + drop), (40, 88 + drop), (48, 100 + drop), (64, 104 + drop)]
    jaw = mirror(jaw_half, 64)
    # neck / cables
    neck = [(46, 92), (82, 92), (90, 124), (38, 124)]
    base = poly(neck, GUN) + poly(jaw, ARM) + poly(helm, ARM)
    crest = [(64, 0), (70, 10), (70, 40), (64, 46), (58, 40), (58, 10)]
    base += poly(crest, RED)
    earL = [(20, 40), (28, 36), (30, 64), (20, 62)]
    base += poly(earL, RED) + poly(mx(earL, 64), RED)
    visor = [(32, 42), (96, 42), (92, 58), (70, 62), (58, 62), (36, 58)]
    base += poly(visor, INK)
    sh = poly([(64, 4), (86, 10), (102, 28), (106, 60), (92, 80), (64, 82), (64, 64), (88, 60), (96, 42)], ARM_SH)
    sh += poly([(64, 78 + drop), (84, 78 + drop), (88, 88 + drop), (80, 100 + drop), (64, 104 + drop)], ARM_SH)
    sh += poly([(64, 0), (70, 10), (70, 40), (64, 46)], RED_SH) + poly(mx(earL, 64), RED_SH)
    sh += poly([(64, 92), (82, 92), (90, 124), (64, 124)], GUN_SH)
    hi = poly([(56, 7), (42, 11), (27, 28), (33, 27), (46, 15)], ARM_HI)
    hi += poly([(58, 11), (60, 10), (60, 38), (58, 38)], RED_HI)
    ink = inkpoly(neck, 3) + line([(56, 96), (54, 124)], 2) + line([(72, 96), (74, 124)], 2)
    ink += inkpoly(helm, 4) + inkpoly(jaw, 3.5) + inkpoly(crest, 3) + inkpoly(earL, 3) + inkpoly(mx(earL, 64), 3)
    ink += inkpoly(visor, 3)
    ink += line([(30, 66), (46, 70), (52, 78)], 2) + line([(98, 66), (82, 70), (76, 78)], 2)
    ink += line([(40, 26), (52, 22)], 1.6) + line([(88, 26), (76, 22)], 1.6)
    # mouth: grille slot that opens with the jaw
    mouth = [(46, 80), (82, 80), (79, 80 + drop * 0.9 + 3), (49, 80 + drop * 0.9 + 3)]
    ink += poly(mouth, INK)
    glow = ""
    if drop > 0:
        glow += poly([(50, 82), (78, 82), (76, 80 + drop * 0.9 + 1), (52, 80 + drop * 0.9 + 1)], TEAL)
        for k in range(4):
            x = 55 + k * 6
            glow += line([(x, 82), (x, 80 + drop * 0.9 + 1)], 1.6, INK)
    # visor: sodium eye band with two hard eye shapes
    glow += poly([(36, 47), (92, 47), (90, 54), (38, 54)], SODIUM)
    for ex in (48, 80):
        e = [(ex - 9, 46), (ex + 9, 46), (ex + 6, 55), (ex - 6, 55)]
        glow += poly(e, AMBER) + poly([(ex - 6, 47.5), (ex - 1, 47.5), (ex - 3, 52)], BONE)
    glow = f'<polygon points="{pts([(34, 44), (94, 44), (90, 57), (38, 57)])}" fill="{SODIUM}" opacity="0.6" filter="url(#glowS)"/>' + glow
    return layers(base, sh, hi, ink, glow)
