"""Per-boss rigs: the cut (parts), the motion (idle / turn / hit / three attacks) and the material FX.

Coordinates are in 384-px cell space (the 576-px Cinder Drake and Dune-Eater are scaled by the rig).
Positive rotation is clockwise on screen (y down). z < 0 draws behind the static core."""
import math
import numpy as np
import rig
from rig import Part, orb, ring, spark, bolt, shard
from moves import P, add, idle_loop, turn, hit, attack, ease_in, ease_in_out, TELL, FIRE, RECOIL


def mirror(poly, cx=192):
    return [(2 * cx - x, y) for x, y in poly]


def mx(pt, cx=192):
    return (2 * cx - pt[0], pt[1])


# ---------------------------------------------------------------------------------------------------------
# Hoarfrost Leviathan (Frost): ice. Claw arms, a dropping fanged jaw, a crown of ice spikes, two side horns.
# Attacks: 0 icicle spray (Jaw), 1 glare beams (EyeL / EyeR), 2 blowhole hail (Crown).
# ---------------------------------------------------------------------------------------------------------
ICE = ((70, 150, 225), (125, 215, 255), (236, 250, 255))
ICE_EDGE = (245, 252, 255)


def frost():
    armL = [(28, 148), (127, 148), (127, 232), (117, 240), (117, 352), (28, 352)]
    hornL = [(56, 18), (135, 18), (135, 100), (124, 112), (112, 140), (100, 152), (56, 152)]
    parts = [
        Part('jaw', [(157, 301), (237, 301), (240, 348), (154, 348)], (197, 288), z=1, fill='maw'),
        Part('crown', [(136, 22), (250, 22), (250, 76), (136, 76)], (193, 76), z=-1, fill='clear'),
        Part('armL', armL, (114, 192), z=-1, fill='clear'),
        Part('armR', mirror(armL), mx((114, 192)), z=-1, fill='clear'),
        Part('hornL', hornL, (110, 136), z=-2, fill='clear', parent='armL'),
        Part('hornR', mirror(hornL), mx((110, 136)), z=-2, fill='clear', parent='armR'),
    ]
    idle = idle_loop({
        'armL': [(0, 2.6, 0.)], 'armR': [(0, -2.6, .5)],
        'jaw': [(2, 1.2, 1.2)], 'crown': [(2, -1.2, 2.)],
        'hornL': [(0, -1.4, .8)], 'hornR': [(0, 1.4, 1.3)],
    })
    # turning left (moving left): the limbs trail right, the jaw swings with the head
    lean = {'armL': P(-5), 'armR': P(-7), 'hornL': P(-3), 'hornR': P(-3), 'jaw': P(0, -2, 0), 'crown': P(0, 2, 0)}
    turnL = turn(lean)
    turnR = turn({k: (-v[0], -v[1], v[2], v[3], v[4]) for k, v in lean.items()})
    hitf = hit({'armL': P(-5), 'armR': P(5), 'jaw': P(0, 0, 4), 'crown': P(0, 0, 3), 'hornL': P(3), 'hornR': P(-3)})
    # 0 icicle spray: the jaw gapes, the claws draw back and up, ice gathers in the gullet
    # (user: "spread its arms wide when its doing big attack from mouth")
    a0 = attack(wind={'jaw': P(0, 0, 16, 1, 1.08), 'armL': P(30, -10, -8), 'armR': P(-30, 10, -8), 'crown': P(0, 0, 2),
                      'hornL': P(-6), 'hornR': P(6)},
                strike={'jaw': P(0, 0, 21, 1, 1.12), 'armL': P(38, -14, -6), 'armR': P(-38, 14, -6), 'crown': P(0, 0, -2),
                        'hornL': P(-8), 'hornR': P(8)},
                tremble={'jaw': P(0, 0, 1), 'armL': P(1), 'armR': P(-1)})
    # 1 glare beams: the head lowers, horns sweep back, claws brace out wide
    a1 = attack(wind={'jaw': P(0, 0, -1), 'armL': P(11, -4, 0), 'armR': P(-11, 4, 0), 'hornL': P(-7), 'hornR': P(7),
                      'crown': P(0, 0, 3)},
                strike={'armL': P(13, -5, -3), 'armR': P(-13, 5, -3), 'hornL': P(-9), 'hornR': P(9), 'jaw': P(0, 0, 3),
                        'crown': P(0, 0, 1)},
                tremble={'armL': P(1.2), 'armR': P(-1.2)})
    # 2 blowhole hail: the crown spikes heave up, horns flare out, claws lift
    a2 = attack(wind={'crown': P(0, 0, -9, 1, 1.08), 'hornL': P(8), 'hornR': P(-8), 'armL': P(-6, 0, -5),
                      'armR': P(6, 0, -5), 'jaw': P(0, 0, 3)},
                strike={'crown': P(0, 0, 4, 1, .94), 'hornL': P(-4), 'hornR': P(4), 'armL': P(4, 0, 2), 'armR': P(-4, 0, 2),
                        'jaw': P(0, 0, 6)},
                tremble={'crown': P(0, 0, -1)})

    pal = ICE
    jaw_c = (193, 306)

    def fx_tell0(t):
        def f(img, k):
            cy = jaw_c[1] + 8 * t
            orb(img, k, jaw_c[0], cy, 2 + 4 * t, pal, strength=.3 + .7 * t)
            # frost crystals condense round the gaping maw, drawn in toward it
            for j in range(int(1 + t * 8)):
                a = 200 + j * 18.5
                r = 44 - 20 * t
                x = jaw_c[0] + math.cos(math.radians(a)) * r * 1.2
                y = cy + 4 + math.sin(math.radians(a)) * -r * .55
                shard(img, k, x, y, 4 + 5 * t, math.degrees(math.atan2(cy - y, jaw_c[0] - x)), pal[1], ICE_EDGE, .95)
            return img
        return f

    def fx_fire0(t):
        def f(img, k):
            s = 1 - t
            orb(img, k, jaw_c[0], jaw_c[1] + 4, 16 * s + 4, pal, strength=s)
            spark(img, k, jaw_c[0], jaw_c[1] + 6, 12, 40 + 30 * t, pal[2], seed=11 + int(t * 9), strength=s,
                  spread=110, heading=90)
            for j in range(5):
                shard(img, k, jaw_c[0] + (j - 2) * 14 * (1 + t), jaw_c[1] + 14 + 22 * t + abs(j - 2) * 4, 6, 90 + (j - 2) * 18,
                      pal[1], ICE_EDGE, s)
            return img
        return f

    def fx_rec0(t):
        def f(img, k):
            orb(img, k, *jaw_c, 6 * (1 - t), pal, strength=.5 * (1 - t))
            return img
        return f

    eyes = [(162, 206), (223, 206)]

    def fx_tell1(t):
        def f(img, k):
            for e in eyes:
                orb(img, k, *e, 3 + 7 * t, pal, strength=.5 + .5 * t)
                ring(img, k, *e, 22 * (1 - t) + 9, 1.5, pal[1], .5 + .5 * t)
            return img
        return f

    def fx_fire1(t):
        def f(img, k):
            s = 1 - t
            for i, e in enumerate(eyes):
                orb(img, k, *e, 12 * s + 3, pal, strength=s)
                spark(img, k, e[0], e[1], 8, 30, pal[2], seed=23 + i, strength=s, spread=360)
                for yy in range(0, int(140 * (1 - .5 * t)), 2):
                    orb(img, k, e[0], e[1] + 10 + yy, 2.2 * s + .5, pal, strength=s * .8)
            return img
        return f

    def fx_rec1(t):
        def f(img, k):
            for e in eyes:
                orb(img, k, *e, 5 * (1 - t), pal, strength=.6 * (1 - t))
            return img
        return f

    crown = (193, 70)

    def fx_tell2(t, dy=0):
        def f(img, k):
            cy = crown[1] - 9 * ease_in_out(t)
            orb(img, k, crown[0], cy, 4 + 10 * t, pal, strength=.4 + .6 * t)
            for j in range(int(2 + 6 * t)):
                a = j * 45 + t * 90
                r = 30 - 12 * t
                shard(img, k, crown[0] + math.cos(math.radians(a)) * r, cy + math.sin(math.radians(a)) * r * .6,
                      4 + 2 * t, a, pal[1], ICE_EDGE, .85)
            return img
        return f

    def fx_fire2(t):
        def f(img, k):
            s = 1 - t
            orb(img, k, crown[0], crown[1] - 4, 16 * s + 4, pal, strength=s)
            for j in range(7):
                a = -90 + (j - 3) * 24
                r = 20 + 50 * t
                shard(img, k, crown[0] + math.cos(math.radians(a)) * r, crown[1] - 4 + math.sin(math.radians(a)) * r,
                      7, a, pal[1], ICE_EDGE, s)
            return img
        return f

    def fx_rec2(t):
        def f(img, k):
            orb(img, k, *crown, 5 * (1 - t), pal, strength=.5 * (1 - t))
            return img
        return f

    def jaw_maw(g):
        return (g, pal[1], jaw_c, 26)

    fxs = {0: (fx_tell0, fx_fire0, fx_rec0, True), 1: (fx_tell1, fx_fire1, fx_rec1, False),
           2: (fx_tell2, fx_fire2, fx_rec2, False)}
    return dict(key='Frost', cell=384, src_frame=0, parts=parts, maw_dark=(10, 18, 30),
                idle=idle, turnL=turnL, turnR=turnR, hit=hitf, attacks=[a0, a1, a2], fx=fxs, maw=jaw_maw,
                idle_maw=.15)


BOSSES = {'frost': frost}


# ---------------------------------------------------------------------------------------------------------
# Generic material FX: charge at the attack's emitters during the tell, a burst at the release, embers after.
# Coordinates in the rig's base px.
# ---------------------------------------------------------------------------------------------------------
def material_fx(emitters, mat, pal, edge, size=1., heading=90., spread=100., streak=40., aura=True):
    s = size

    def particles(img, k, cx, cy, t, out, seed):
        rng = np.random.RandomState(seed)
        n = 7
        for j in range(n):
            a = j * (360 / n) + seed * 13
            r = ((28 + 10 * rng.rand()) * (1 - .55 * t) if not out else (10 + 46 * t)) * s
            x = cx + math.cos(math.radians(a)) * r
            y = cy + math.sin(math.radians(a)) * r * .7
            if mat == 'ice':
                shard(img, k, x, y, (4 + 4 * t) * s, a, pal[1], edge, .9)
            elif mat == 'fire':
                orb(img, k, x, y - (8 * t if not out else -8 * t), (1.6 + 1.2 * rng.rand()) * s, pal, .9)
            elif mat == 'spore':
                orb(img, k, x, y, (1.8 + 1.5 * t) * s, pal, .85)
            elif mat == 'water':
                orb(img, k, x, y, (1.5 + 1.2 * rng.rand()) * s, pal, .8)
            elif mat == 'storm':
                bolt(img, k, cx, cy, x, y, edge if j % 2 else pal[1], seed + j, .9, jag=4 * s)
            elif mat == 'sand':
                for q in range(3):
                    orb(img, k, x + rng.randn() * 3 * s, y + rng.randn() * 3 * s, .9 * s, pal, .8)
            elif mat == 'void':
                ring(img, k, cx, cy, r * .8, 1.4 * s, pal[1], .7)
                break
        return img

    def tell(t):
        def f(img, k):
            for i, (x, y) in enumerate(emitters):
                orb(img, k, x, y, (2 + 7 * t) * s, pal, strength=.35 + .65 * t)
                if aura:
                    ring(img, k, x, y, (26 * (1 - t) + 8) * s, 1.4 * s, pal[1], .35 + .5 * t)
                if t > .2:
                    particles(img, k, x, y, t, False, 3 + i * 7 + int(t * 8))
            return img
        return f

    def fire(t):
        def f(img, k):
            q = 1 - t
            for i, (x, y) in enumerate(emitters):
                orb(img, k, x, y, (15 * q + 3) * s, pal, strength=q)
                spark(img, k, x, y, 11, (streak + 30 * t) * s, edge, seed=11 + i * 5 + int(t * 9), strength=q,
                      spread=spread, heading=heading)
                particles(img, k, x, y, t, True, 41 + i * 3)
            return img
        return f

    def rec(t):
        def f(img, k):
            for x, y in emitters:
                orb(img, k, x, y, 6 * (1 - t) * s, pal, strength=.5 * (1 - t))
            return img
        return f

    return tell, fire, rec


def e576(*pts):
    return [(x * 1.5, y * 1.5) for x, y in pts]


# ---------------------------------------------------------------------------------------------------------
# Cinder Drake (Ember, 576 px): fire. Beating wings, a skull head that rears and a jaw that opens before the
# breath, raking claws, flaring exhaust jets.  Attacks: 0 fire breath / jaw flamethrower (Jaw),
# 1 furnace slugs (Furnace, CannonL, CannonR), 2 wing inferno (the jets).
# ---------------------------------------------------------------------------------------------------------
FIRE = ((200, 70, 20), (255, 150, 40), (255, 236, 170))
FIRE_EDGE = (255, 214, 120)


def ember():
    clawL = [(52, 318), (222, 318), (230, 440), (222, 540), (52, 540)]
    wingL = [(30, 50), (200, 50), (218, 134), (160, 134), (160, 276), (236, 276), (236, 318), (30, 318)]
    flameL = [(188, 20), (250, 20), (250, 112), (188, 112)]
    head = [(236, 336), (200, 350), (185, 400), (170, 472), (200, 480), (236, 526), (346, 526), (390, 480), (406, 472), (392, 400), (378, 350), (342, 336)]
    jaw = [(250, 482), (338, 482), (344, 530), (244, 530)]
    parts = [
        Part('jaw', jaw, (294, 470), z=2, fill='maw', parent='head'),
        Part('head', head, (290, 352), z=1, fill='body'),
        Part('clawL', clawL, (214, 338), z=-1, fill='clear'),
        Part('clawR', mirror(clawL, 288), mx((214, 338), 288), z=-1, fill='clear'),
        Part('wingL', wingL, (234, 236), z=-2, fill='clear', curl=.5),
        Part('wingR', mirror(wingL, 288), mx((234, 236), 288), z=-2, fill='clear', curl=.5),
        Part('flameL', flameL, (219, 112), z=1, fill='body'),
        Part('flameR', mirror(flameL, 288), mx((219, 112), 288), z=1, fill='body'),
    ]
    idle = idle_loop({
        # (user: "flap the wings more often"): two full beats a loop (0.75 s each), bigger
        'wingL': [(0, 11., 0., 2), (3, -.09, 0., 2)], 'wingR': [(0, -11., 0., 2), (3, -.09, 0., 2)],
        'clawL': [(0, 2.5, 1.)], 'clawR': [(0, -2.5, 1.4)],
        'head': [(2, 1.5, .6), (0, .8, 2.)], 'jaw': [(2, 1.5, 1.6)],
        'flameL': [(4, .1, 0.), (4, .06, 2.1)], 'flameR': [(4, .1, 1.), (4, .06, 3.)],
    })
    lean = {'wingL': P(-5, 0, 0, .94), 'wingR': P(-7, 0, 0, 1.0), 'clawL': P(-6), 'clawR': P(-8), 'head': P(-3, -3, 0),
            'flameL': P(-6), 'flameR': P(-6)}
    turnL = turn(lean)
    turnR = turn({k: (-v[0], -v[1], v[2], v[3] if k != 'wingL' else 1., .94 if k == 'wingR' else v[4]) for k, v in lean.items()})
    hitf = hit({'head': P(0, 0, -6), 'jaw': P(0, 0, 6), 'wingL': P(-6), 'wingR': P(6), 'clawL': P(-6), 'clawR': P(6)})
    a0 = attack(wind={'head': P(0, 0, -5), 'jaw': P(0, 0, 18, 1, 1.08), 'wingL': P(12, 0, 0, .9), 'wingR': P(-12, 0, 0, .9),
                      'clawL': P(10, -4, -6), 'clawR': P(-10, 4, -6), 'flameL': P(0, 0, 0, 1, 1.25), 'flameR': P(0, 0, 0, 1, 1.25)},
                strike={'head': P(0, 0, 6), 'jaw': P(0, 0, 24, 1, 1.1), 'wingL': P(-10, 0, 0, 1.03), 'wingR': P(10, 0, 0, 1.03),
                        'clawL': P(-8, 3, 6), 'clawR': P(8, -3, 6)},
                tremble={'head': P(0, 0, 1), 'jaw': P(0, 0, 1.5)})
    a1 = attack(wind={'clawL': P(-9, 6, -10), 'clawR': P(9, -6, -10), 'wingL': P(6, 0, 0, .95), 'wingR': P(-6, 0, 0, .95),
                      'head': P(0, 0, 5), 'jaw': P(0, 0, 4), 'flameL': P(0, 0, 0, 1, 1.4), 'flameR': P(0, 0, 0, 1, 1.4)},
                strike={'clawL': P(8, -4, 6), 'clawR': P(-8, 4, 6), 'head': P(0, 0, -6), 'jaw': P(0, 0, 8),
                        'wingL': P(-6), 'wingR': P(6), 'flameL': P(0, 0, 0, 1, .8), 'flameR': P(0, 0, 0, 1, .8)},
                tremble={'clawL': P(1.5), 'clawR': P(-1.5)})
    a2 = attack(wind={'wingL': P(16, 0, -4, .86), 'wingR': P(-16, 0, -4, .86), 'clawL': P(12, 0, -8), 'clawR': P(-12, 0, -8),
                      'head': P(0, 0, -6), 'jaw': P(0, 0, 12), 'flameL': P(0, 0, 0, 1.1, 1.5), 'flameR': P(0, 0, 0, 1.1, 1.5)},
                strike={'wingL': P(-14, 0, 4, 1.04), 'wingR': P(14, 0, 4, 1.04), 'clawL': P(-6, 0, 4), 'clawR': P(6, 0, 4),
                        'head': P(0, 0, 6), 'jaw': P(0, 0, 18), 'flameL': P(0, 0, 0, 1, 1.7), 'flameR': P(0, 0, 0, 1, 1.7)},
                tremble={'wingL': P(1.5), 'wingR': P(-1.5)})
    jaw_c = (290, 492)
    f0 = material_fx([(jaw_c[0], jaw_c[1] + 14)], 'fire', FIRE, FIRE_EDGE, size=1.2, streak=60, aura=False)
    f1 = material_fx(e576((193, 264), (126, 119), (257, 119)), 'fire', FIRE, FIRE_EDGE, size=1.2)
    f2 = material_fx([(219, 40), (357, 40)], 'fire', FIRE, FIRE_EDGE, size=1.3, heading=-90, spread=60)
    # the breath: the maw carries the glow; the tell orb sits low in the gullet
    # the beat keeps going between the moves: a full wing beat through every turn, hit and recoil
    def overlay(move, i, n):
        if move.startswith(('turn', 'hit', 'recoil')):
            w = math.sin(2 * math.pi * (i + 1) / n)
            return {'wingL': P(10 * w, 0, 0, 1 - .08 * w), 'wingR': P(-10 * w, 0, 0, 1 - .08 * w)}
        return {}
    return dict(key='Ember', cell=576, base=576, src_frame=20, parts=parts, maw_dark=(28, 8, 4), overlay=overlay,
                idle=idle, turnL=turnL, turnR=turnR, hit=hitf, attacks=[a0, a1, a2],
                fx={0: (*f0, True), 1: (*f1, False), 2: (*f2, False)},
                maw=lambda g: (g, FIRE[1], (jaw_c[0], jaw_c[1] + 6), 46), idle_maw=.25, tell_seconds=[.7, .6, .8])


BOSSES['ember'] = ember


def swap_lr(pose):
    """The mirrored pose: L <-> R parts, rotation and x offset negated."""
    out = {}
    for k, v in pose.items():
        n = k[:-1] + ('R' if k.endswith('L') else 'L') if k[-1] in 'LR' and k[:-1] else k
        out[n] = (-v[0], -v[1], v[2], v[3], v[4])
    return out


def sym(pose):
    """Add the mirrored R entries for every L entry."""
    out = dict(pose)
    for k, v in pose.items():
        if k.endswith('L'):
            out[k[:-1] + 'R'] = (-v[0], -v[1], v[2], v[3], v[4])
    return out


def pair(name, poly, pivot, cx=192, **kw):
    return [Part(name + 'L', poly, pivot, **kw), Part(name + 'R', mirror(poly, cx), mx(pivot, cx), **{**kw, 'parent': (kw['parent'][:-1] + 'R') if kw.get('parent') else None})]


# ---------------------------------------------------------------------------------------------------------
# Void Archon (Space): void (Space keeps its magenta cue). Grappling claws, shoulder turrets that swivel,
# a twin chin cannon that recoils, pod guns that swing, engine flames.
# Attacks: 0 chin cannon (Chin), 1 core burst (Core), 2 pod lasers (PodL / PodR).
# ---------------------------------------------------------------------------------------------------------
VOID = ((150, 30, 170), (240, 90, 230), (255, 225, 250))
VOID_EDGE = (255, 200, 245)


def space():
    parts = [
        Part('chin', [(170, 262), (215, 262), (215, 338), (170, 338)], (192, 266), z=1, fill='body'),
        *pair('pod', [(124, 226), (169, 226), (169, 304), (124, 304)], (147, 238), z=1, fill='body'),
        *pair('claw', [(36, 188), (127, 188), (127, 342), (36, 342)], (118, 200), z=-1, fill='clear'),
        *pair('gun', [(58, 86), (108, 86), (108, 126), (58, 126)], (100, 118), z=1, fill='body'),
        *pair('flame', [(134, 6), (182, 6), (182, 50), (134, 50)], (158, 50), z=1, fill='body'),
    ]
    idle = idle_loop({
        'clawL': [(0, 3., 0.)], 'clawR': [(0, -3., .6)], 'gunL': [(0, 4., 1.)], 'gunR': [(0, -4., 1.6)],
        'podL': [(0, 2., 2.)], 'podR': [(0, -2., 2.4)], 'chin': [(2, 1.2, 0.)],
        'flameL': [(4, .14, 0.), (4, .06, 2.4)], 'flameR': [(4, .14, 1.3), (4, .06, 3.5)],
    })
    lean = {'clawL': P(-6), 'clawR': P(-8), 'gunL': P(-8), 'gunR': P(-8), 'podL': P(-4), 'podR': P(-4),
            'flameL': P(-8), 'flameR': P(-8), 'chin': P(0, 2, 0)}
    hitf = hit(sym({'clawL': P(-6), 'gunL': P(6), 'podL': P(4), 'chin': P(0, 0, -4)}))
    a0 = attack(wind=sym({'chin': P(0, 0, 8, 1, 1.08), 'clawL': P(-9, 4, -4), 'podL': P(-6), 'gunL': P(-10)}),
                strike=sym({'chin': P(0, 0, -6, 1, .94), 'clawL': P(6, -2, 2), 'podL': P(4), 'gunL': P(6)}),
                tremble={'chin': P(0, 0, 1)})
    a1 = attack(wind=sym({'clawL': P(14, -4, -6), 'gunL': P(16), 'podL': P(8, -3, 0), 'chin': P(0, 0, -3),
                          'flameL': P(0, 0, 0, 1.1, 1.4)}),
                strike=sym({'clawL': P(-6, 2, 4), 'gunL': P(-6), 'podL': P(-6, 2, 0), 'chin': P(0, 0, 4)}),
                tremble=sym({'clawL': P(1.2)}))
    a2 = attack(wind=sym({'podL': P(-14, -3, 7), 'clawL': P(8, 0, -8), 'gunL': P(-8), 'chin': P(0, 0, -2)}),
                strike=sym({'podL': P(-18, -4, 3), 'clawL': P(10, 0, -4), 'chin': P(0, 0, 2)}),
                tremble=sym({'podL': P(1)}))
    # (user: "move its arm up when its doing attacks too, keep these frames tho"): the approved poses plus both claws
    # raised (swung out and up from the shoulder), held high through the release and lowered in the recoil
    up_w, up_s = sym({'clawL': P(40, -4, -24)}), sym({'clawL': P(46, -4, -28)})
    a0, a1, a2 = [attack(wind=add(w, up_w), strike=add(st, up_s), tremble=tr) for w, st, tr in (
        (sym({'chin': P(0, 0, 8, 1, 1.08), 'clawL': P(-9, 4, -4), 'podL': P(-6), 'gunL': P(-10)}),
         sym({'chin': P(0, 0, -6, 1, .94), 'clawL': P(6, -2, 2), 'podL': P(4), 'gunL': P(6)}), {'chin': P(0, 0, 1)}),
        (sym({'clawL': P(14, -4, -6), 'gunL': P(16), 'podL': P(8, -3, 0), 'chin': P(0, 0, -3), 'flameL': P(0, 0, 0, 1.1, 1.4)}),
         sym({'clawL': P(-6, 2, 4), 'gunL': P(-6), 'podL': P(-6, 2, 0), 'chin': P(0, 0, 4)}), sym({'clawL': P(1.2)})),
        (sym({'podL': P(-14, -3, 7), 'clawL': P(8, 0, -8), 'gunL': P(-8), 'chin': P(0, 0, -2)}),
         sym({'podL': P(-18, -4, 3), 'clawL': P(10, 0, -4), 'chin': P(0, 0, 2)}), sym({'podL': P(1)})))]
    f0 = material_fx([(192, 300)], 'void', VOID, VOID_EDGE, streak=50)
    f1 = material_fx([(192, 184)], 'void', VOID, VOID_EDGE, size=1.3, spread=360)
    f2 = material_fx([(146, 294), (239, 294)], 'void', VOID, VOID_EDGE, spread=40)
    return dict(key='Space', cell=384, src_frame=20, parts=parts, idle=idle, turnL=turn(lean), turnR=turn(swap_lr(lean)),
                hit=hitf, attacks=[a0, a1, a2], fx={0: (*f0, False), 1: (*f1, False), 2: (*f2, False)},
                tell_seconds=[.7, .8, .95])


# ---------------------------------------------------------------------------------------------------------
# Iron Kraken (Tide): water. Six tentacles that curl (root + hooked tip), a beak jaw that gapes.
# Attacks: 0 beak jet (Beak), 1 port cluster (LeftA / LeftB), 2 starboard cluster (RightA / RightB).
# ---------------------------------------------------------------------------------------------------------
WATER = ((30, 120, 160), (90, 220, 230), (225, 255, 255))
WATER_EDGE = (200, 250, 255)


def tide():
    parts = [
        Part('jaw', [(160, 250), (226, 250), (222, 288), (164, 288)], (193, 240), z=2, fill='maw'),
        *pair('tentA', [(28, 44), (126, 44), (130, 150), (112, 196), (28, 196)], (122, 184), z=-1, fill='clear', curl=1.4),
        *pair('tentB', [(18, 166), (128, 166), (128, 258), (80, 258), (80, 300), (18, 300)], (126, 214), z=-1, fill='clear', curl=1.4),
        *pair('tentC', [(80, 258), (176, 258), (176, 362), (60, 362), (60, 300), (80, 300)], (168, 266), z=-1, fill='clear', curl=1.4),
    ]
    # violent: every tentacle thrashes on its own beat (one or two lashes a loop, out of phase), lengthening as it
    # flings out and bunching as it whips back
    idle = idle_loop({
        'tentAL': [(0, 22., 0.), (0, 8., 1.1, 2), (4, .08, .4)], 'tentAR': [(0, -20., 2.2), (0, -9., .3, 2), (4, .08, 2.6)],
        'tentBL': [(0, 24., 1.4), (0, 9., 2.4, 2), (3, .1, 1.8)], 'tentBR': [(0, -23., 3.6), (0, -8., 1.7, 2), (3, .1, 4.)],
        'tentCL': [(0, -26., 2.8), (0, 10., .5, 2), (4, .1, 3.2)], 'tentCR': [(0, 25., .9), (0, -9., 2.9, 2), (4, .1, 1.3)],
        'jaw': [(2, 2.5, .4), (2, 1.5, 1.9, 2)],
    })
    lean = {'tentAL': P(-34, 0, 0, 1, 1.12), 'tentAR': P(-40, 0, 0, 1, .9), 'tentBL': P(-30, 0, 0, .88), 'tentBR': P(-38, 0, 0, 1.18),
            'tentCL': P(-32, 0, 0, 1, 1.1), 'tentCR': P(-40, 0, 0, 1, 1.15), 'jaw': P(0, -3, 0)}
    hitf = hit(sym({'tentAL': P(-30, 0, 0, 1, .85), 'tentBL': P(-28, 0, 0, .85), 'tentCL': P(30, 0, 0, 1, .85), 'jaw': P(0, 0, 6)}))
    # the attacks: the tentacles coil back (bunched), then STRETCH out in a lunge -- down at the ship -- and thrash home
    a0 = attack(wind={**sym({'tentAL': P(30, 0, 0, 1, .82), 'tentBL': P(34, 0, 0, .8), 'tentCL': P(-30, 0, 0, 1, .8)}),
                      'jaw': P(0, 0, 14, 1.04, 1.08)},
                strike={**sym({'tentAL': P(-42, 0, 0, 1, 1.38), 'tentBL': P(-46, 0, 0, 1.42), 'tentCL': P(26, 0, 0, 1, 1.45)}),
                        'jaw': P(0, 0, 19, 1.06, 1.12)},
                tremble={**sym({'tentAL': P(6), 'tentBL': P(-7), 'tentCL': P(6)}), 'jaw': P(0, 0, 1.5)})
    port_w = {'tentAL': P(40, 0, -3, 1, .8), 'tentBL': P(44, 0, -3, .78), 'tentCL': P(-28, 0, 0, 1, .82),
              'tentAR': P(-18, 0, 0, 1, .9), 'tentBR': P(-16, 0, 0, .9), 'tentCR': P(14), 'jaw': P(0, 0, 4)}
    port_s = {'tentAL': P(-50, 2, 2, 1, 1.32), 'tentBL': P(-58, 2, 2, 1.5), 'tentCL': P(30, 0, 0, 1, 1.45),
              'tentAR': P(14, 0, 0, 1, 1.12), 'tentBR': P(16, 0, 0, 1.12), 'tentCR': P(-16), 'jaw': P(0, 0, 7)}
    a1 = attack(port_w, port_s, tremble={'tentAL': P(7), 'tentBL': P(-7), 'tentCL': P(6), 'tentAR': P(-4)})
    a2 = attack(swap_lr(port_w), swap_lr(port_s), tremble={'tentAR': P(-7), 'tentBR': P(7), 'tentCR': P(-6), 'tentAL': P(4)})
    f0 = material_fx([(193, 262)], 'water', WATER, WATER_EDGE, aura=False, streak=55)
    f1 = material_fx([(125, 154), (83, 234)], 'water', WATER, WATER_EDGE, heading=110, spread=80)
    f2 = material_fx([(260, 154), (302, 234)], 'water', WATER, WATER_EDGE, heading=70, spread=80)
    return dict(key='Tide', cell=384, src_frame=20, parts=parts, maw_dark=(6, 20, 26), idle=idle, turnL=turn(lean),
                turnR=turn(swap_lr(lean)), hit=hitf, attacks=[a0, a1, a2],
                fx={0: (*f0, True), 1: (*f1, False), 2: (*f2, False)},
                maw=lambda g: (g, WATER[1], (193, 262), 24), idle_maw=.2, tell_seconds=[.9, .8, .8])


# ---------------------------------------------------------------------------------------------------------
# Tempest Engine (Storm): storm neon (white-violet, never pink). An eye whose slit pupil tracks and dilates,
# side vanes that flare, coil dishes that swivel, thruster flames.
# Poses: 0 the eye beam (Core: the eye), 1 crown hail (Rods), 2 the coil lightning (CoilL / CoilR).
# ---------------------------------------------------------------------------------------------------------
STORM = ((96, 70, 220), (165, 145, 255), (238, 234, 255))
STORM_EDGE = (225, 220, 255)


def storm():
    parts = [
        Part('pupil', [(192, 132), (205, 160), (207, 182), (205, 204), (192, 232), (179, 204), (177, 182), (179, 160)], (192, 182), z=1, fill='body', shade=.9),
        *pair('vane', [(38, 138), (86, 138), (86, 264), (38, 264)], (84, 200), z=-1, fill='clear'),
        *pair('dish', [(66, 28), (146, 28), (146, 126), (120, 142), (66, 142)], (128, 132), z=1, fill='body'),
        *pair('jet', [(98, 312), (160, 312), (160, 356), (98, 356)], (130, 314), z=1, fill='body'),
    ]
    idle = idle_loop({
        'pupil': [(1, 3., 0.), (3, .12, 1.)], 'vaneL': [(1, -1.5, 0.), (0, 2., .5)], 'vaneR': [(1, 1.5, .4), (0, -2., .9)],
        'dishL': [(0, 3., 1.)], 'dishR': [(0, -3., 1.5)],
        'jetL': [(4, .16, 0.), (4, .07, 2.)], 'jetR': [(4, .16, 1.2), (4, .07, 3.1)],
    })
    lean = {'pupil': P(0, -6, 0, .85), 'vaneL': P(-4, 2, 0), 'vaneR': P(-4, 2, 0), 'dishL': P(-7), 'dishR': P(-7),
            'jetL': P(-10), 'jetR': P(-10)}
    hitf = hit({**sym({'vaneL': P(4, 3, 0), 'dishL': P(-6)}), 'pupil': P(0, 0, 0, .5, .92)})
    a0 = attack(wind={**sym({'vaneL': P(-6, -8, 0), 'dishL': P(-6), 'jetL': P(0, 0, 0, 1.1, 1.3)}), 'pupil': P(0, 0, 4, 1.6, 1.02)},
                strike={**sym({'vaneL': P(4, 3, 0), 'dishL': P(6)}), 'pupil': P(0, 0, 0, 1.9, 1.05)},
                tremble={'pupil': P(0, 1, 0)})
    a1 = attack(wind={**sym({'dishL': P(16, 2, -4), 'vaneL': P(6, -3, -4)}), 'pupil': P(0, 0, -6, 1.3, .95)},
                strike={**sym({'dishL': P(-6, 0, 2), 'vaneL': P(-4, 2, 2)}), 'pupil': P(0, 0, -2, .8)},
                tremble=sym({'dishL': P(1.5)}))
    a2 = attack(wind={**sym({'dishL': P(-18, -3, 2), 'vaneL': P(-10, -10, 0)}), 'pupil': P(0, 0, 0, .55)},
                strike={**sym({'dishL': P(-24, -4, 4), 'vaneL': P(-12, -12, 0)}), 'pupil': P(0, 0, 0, 1.5)},
                tremble=sym({'vaneL': P(1.2, -1)}))
    # (user: "as long as the eye can shoot beams and coils can shoot lightning attacks")
    # 0 the EYE BEAM: the eye charges (pupil dilates, light gathers in it), then a beam lances straight down out of it
    eye = (192, 182)

    def tell0(t):
        def f(img, k):
            orb(img, k, *eye, 3 + 13 * t, STORM, strength=.3 + .7 * t)
            ring(img, k, *eye, 44 * (1 - t) + 14, 1.5, STORM[1], .3 + .6 * t)
            if t > .4:
                for j in range(int(t * 6)):
                    a = j * 60 + t * 140
                    bolt(img, k, eye[0] + math.cos(math.radians(a)) * 36, eye[1] + math.sin(math.radians(a)) * 36,
                         eye[0], eye[1], STORM_EDGE, 7 + j + int(t * 10), .7, jag=3)
            return img
        return f

    def fire0(t):
        def f(img, k):
            q = 1 - t
            w = 11 * q + 4                                       # the beam narrows as it burns out
            orb(img, k, *eye, 20 * q + 6, STORM, strength=1)
            y = eye[1] + 8
            while y < 384:
                orb(img, k, eye[0], y, w * .62, STORM, strength=.55 + .45 * q)
                y += 3
            spark(img, k, eye[0], eye[1] + 30, 10, 40, STORM_EDGE, seed=31 + int(t * 9), strength=q, spread=70, heading=90)
            return img
        return f

    def rec0(t):
        def f(img, k):
            orb(img, k, *eye, 8 * (1 - t), STORM, strength=.6 * (1 - t))
            return img
        return f
    f0 = (tell0, fire0, rec0)
    f1 = material_fx([(193, 68)], 'storm', STORM, STORM_EDGE, heading=-90, spread=120)
    # 2 the COIL LIGHTNING: arcs crawl up both Tesla coils and jump between their prongs, then forks of lightning
    # leap out of each coil, down and outward
    coils = [(112, 106), (273, 106)]

    def tell2(t):
        def f(img, k):
            for i, (x, y) in enumerate(coils):
                orb(img, k, x, y, 2 + 8 * t, STORM, strength=.35 + .65 * t)
                for j in range(1 + int(t * 5)):
                    a = -150 + j * 50 + (i * 30)
                    r = 34 - 10 * t
                    bolt(img, k, x, y, x + math.cos(math.radians(a)) * r, y + math.sin(math.radians(a)) * r,
                         STORM_EDGE if j % 2 else STORM[1], 3 + i * 11 + j + int(t * 8), .85, jag=4)
            return img
        return f

    def fire2(t):
        def f(img, k):
            q = 1 - t
            for i, (x, y) in enumerate(coils):
                side = 1 if i == 0 else -1                     # down and OUTWARD (left coil to the left)
                orb(img, k, x, y, 16 * q + 4, STORM, strength=q)
                for j in range(4):
                    ang = math.radians(90 + side * (20 + j * 17))
                    L = (100 + 70 * (1 - abs(j - 1.5) / 1.5)) * (.6 + .4 * q) + 40 * t
                    x1, y1 = x + math.cos(ang) * L, y + math.sin(ang) * L
                    bolt(img, k, x, y, x1, y1, STORM_EDGE, 41 + i * 9 + j + int(t * 13), q * .95 + .05, jag=7, width=2)
                    bolt(img, k, x, y, x1, y1, STORM[1], 61 + i * 9 + j + int(t * 13), q * .8, jag=9)
            return img
        return f

    def rec2(t):
        def f(img, k):
            for i, (x, y) in enumerate(coils):
                orb(img, k, x, y, 6 * (1 - t), STORM, strength=.5 * (1 - t))
                if t < .6:
                    bolt(img, k, x, y, x + (-1 if i == 0 else 1) * 18, y + 22, STORM_EDGE, 90 + i + int(t * 7), .6 * (1 - t), jag=4)
            return img
        return f
    f2 = (tell2, fire2, rec2)
    return dict(key='Storm', cell=384, src_frame=0, parts=parts, idle=idle, turnL=turn(lean), turnR=turn(swap_lr(lean)),
                hit=hitf, attacks=[a0, a1, a2], fx={0: (*f0, False), 1: (*f1, False), 2: (*f2, False)},
                tell_seconds=[1., .9, 1.])


# ---------------------------------------------------------------------------------------------------------
# Dune-Eater (Desert, 576 px): sand / sun. Sail fins that fan, mandible jaws that gape, a dropping maw,
# raking claws, the lens torch.  Attacks: 0 grit fan (Maw), 1 dune lob (Boom: the right claw), 2 lens beam (Lens).
# ---------------------------------------------------------------------------------------------------------
SAND = ((170, 120, 40), (240, 198, 105), (255, 246, 205))
SAND_EDGE = (255, 238, 180)


def desert():
    C = 288
    parts = [
        Part('jaw', [(256, 438), (332, 438), (328, 482), (260, 482)], (294, 430), z=2, fill='maw'),
        *pair('mand', [(222, 392), (282, 392), (300, 470), (256, 492), (218, 476)], (278, 400), cx=C, z=1, fill='body'),
        *pair('claw', [(86, 328), (226, 328), (226, 540), (86, 540)], (176, 344), cx=C, z=-1, fill='clear'),
        *pair('sail', [(50, 50), (250, 50), (262, 190), (215, 225), (170, 250), (140, 310), (50, 330)], (246, 220), cx=C, z=-2, fill='clear', curl=.7),
        Part('torch', [(252, 30), (324, 30), (324, 128), (252, 128)], (288, 128), z=1, fill='body'),
    ]
    idle = idle_loop({
        # (user: "pincers move more bitey, and wings more flappy"): two big sail beats a loop; the pincers bite (overlay)
        'sailL': [(0, 9., 0., 2), (3, -.08, 0., 2)], 'sailR': [(0, -9., .3, 2), (3, -.08, .3, 2)],
        'clawL': [(0, 2.5, 1.)], 'clawR': [(0, -2.5, 1.5)],
        'jaw': [(2, 1.5, 2.)], 'torch': [(4, .12, 0.), (3, .05, 1.7)],
    })
    lean = {'sailL': P(-5, 0, 0, .95), 'sailR': P(-7), 'clawL': P(-6), 'clawR': P(-8), 'mandL': P(-4), 'mandR': P(-4),
            'jaw': P(0, -3, 0), 'torch': P(-8)}
    hitf = hit(sym({'sailL': P(-5), 'clawL': P(-6), 'mandL': P(8), 'jaw': P(0, 0, 3)}))
    a0 = attack(wind=sym({'mandL': P(-26, -5, 0), 'jaw': P(0, 0, 18, 1.04, 1.08), 'clawL': P(10, -4, -8), 'sailL': P(6, 0, 0, .94)}),
                strike=sym({'mandL': P(-34, -7, 2), 'jaw': P(0, 0, 24, 1.06, 1.1), 'clawL': P(-8, 4, 6), 'sailL': P(-5)}),
                tremble=sym({'mandL': P(1.5)}))
    a1 = attack(wind={'clawR': P(-26, -8, -16), 'clawL': P(6, 0, -3), 'sailR': P(-8, 0, 0, .92), 'mandR': P(6), 'jaw': P(0, 0, 4)},
                strike={'clawR': P(18, 6, 10), 'clawL': P(-4), 'sailR': P(6), 'mandR': P(-4), 'jaw': P(0, 0, 6)},
                tremble={'clawR': P(-2)})
    a2 = attack(wind=sym({'sailL': P(16, 0, 0, .92), 'clawL': P(-8, 0, -4), 'mandL': P(6), 'torch': P(0, 0, 0, 1.15, 1.45)}),
                strike=sym({'sailL': P(-12, 0, 0, 1.02), 'clawL': P(6, 0, 4), 'mandL': P(-6), 'torch': P(0, 0, 0, .9, .8)}),
                tremble=sym({'sailL': P(1.2)}))
    f0 = material_fx([(288, 460)], 'sand', SAND, SAND_EDGE, size=1.3, aura=False, streak=60)
    f1 = material_fx(e576((306, 297)), 'sand', SAND, SAND_EDGE, size=1.3, heading=-60, spread=80)
    f2 = material_fx(e576((192, 131)), 'sand', SAND, SAND_EDGE, size=1.4, streak=70)
    def bite(open_):
        # the pincers: negative = gape (L), the right one mirrored
        return {'mandL': P(-open_), 'mandR': P(open_)}

    def overlay(move, i, n):
        if move == 'idle':
            # two bites a loop: a quick gape (2 frames), a hard snap shut (a touch past closed), settle
            seq = [0, 10, 20, 26, -5, -2, 0, 8, 18, 24, -5, -2]
            return bite(seq[i])
        if move.startswith(('turn', 'recoil')):
            w = math.sin(2 * math.pi * (i + 1) / n)
            return {**bite([14, 24, -4, 10, 18, -3][i % 6]), 'sailL': P(9 * w, 0, 0, 1 - .07 * w), 'sailR': P(-9 * w, 0, 0, 1 - .07 * w)}
        if move == 'hit':
            return bite([-6, 12, 4][i])
        if move == 'tell0':
            return bite(6 * (i % 2) if i >= 5 else 0)              # chattering as the maw loads
        if move.startswith('fire'):
            # chomp, chomp: the release snaps shut and gapes again (attack 0 the hardest)
            # (attack 0's strike is already the full gape: shut on 1 and 3 over it; the others gape and snap)
            return bite([0, -30, -2, -26][i] if move == 'fire0' else [14, 22, -4, 0][i])
        if move.startswith('tell'):
            return bite(10 * math.sin(math.pi * i / 7))
        return {}
    return dict(key='Desert', cell=576, base=576, src_frame=20, parts=parts, maw_dark=(26, 16, 8), idle=idle, overlay=overlay,
                turnL=turn(lean), turnR=turn(swap_lr(lean)), hit=hitf, attacks=[a0, a1, a2],
                fx={0: (*f0, True), 1: (*f1, False), 2: (*f2, False)},
                maw=lambda g: (g, SAND[1], (C, 462), 40), idle_maw=.2, tell_seconds=[1., .9, 1.])


# ---------------------------------------------------------------------------------------------------------
# Bloom Queen (Verdant): vines / spores (jade green). Leaf fronds that fan open, outer claws that aim the
# acid cannons, inner fangs that part round the stinger, flaring torch stalks.
# Attacks: 0 stinger thorns (Stinger), 1 spore bloom (the six petal tips), 2 acid cannons (CannonL / CannonR).
# ---------------------------------------------------------------------------------------------------------
SPORE = ((40, 130, 60), (110, 230, 110), (225, 255, 215))
SPORE_EDGE = (205, 255, 200)


def verdant():
    parts = [
        Part('stinger', [(166, 248), (219, 248), (219, 292), (166, 292)], (192, 250), z=1, fill='body'),
        *pair('fang', [(118, 252), (166, 252), (172, 348), (118, 348)], (150, 262), z=1, fill='body'),
        *pair('claw', [(48, 236), (118, 236), (118, 338), (48, 338)], (104, 248), z=-1, fill='clear'),
        *pair('leaf', [(34, 30), (142, 30), (142, 140), (118, 152), (118, 236), (34, 236)], (134, 176), z=-2, fill='clear', curl=.8),
        *pair('stalk', [(138, 8), (182, 8), (182, 92), (138, 92)], (160, 92), z=1, fill='body'),
    ]
    idle = idle_loop({
        'leafL': [(0, 3.5, 0.)], 'leafR': [(0, -3.5, .6)], 'clawL': [(0, 2.5, 1.2)], 'clawR': [(0, -2.5, 1.8)],
        'fangL': [(0, -2.5, 2.)], 'fangR': [(0, 2.5, 2.)], 'stinger': [(2, 1.5, 0.)],
        'stalkL': [(4, .12, 0.), (0, 2., 1.)], 'stalkR': [(4, .12, 1.4), (0, -2., 2.)],
    })
    lean = {'leafL': P(-5), 'leafR': P(-7), 'clawL': P(-6), 'clawR': P(-7), 'fangL': P(-4), 'fangR': P(-4),
            'stalkL': P(-8), 'stalkR': P(-8), 'stinger': P(0, 2, 0)}
    hitf = hit(sym({'leafL': P(-6), 'clawL': P(-6), 'fangL': P(6), 'stinger': P(0, 0, -3)}))
    a0 = attack(wind={**sym({'fangL': P(-16, -3, -2), 'clawL': P(-6), 'leafL': P(3)}), 'stinger': P(0, 0, 10, 1, 1.15)},
                strike={**sym({'fangL': P(-22, -5, 0), 'clawL': P(6)}), 'stinger': P(0, 0, 15, 1, 1.2)},
                tremble={'stinger': P(0, 0, 1)})
    a1 = attack(wind=sym({'leafL': P(20), 'clawL': P(8), 'fangL': P(5), 'stalkL': P(0, 0, 0, 1.1, 1.4)}),
                strike=sym({'leafL': P(-9), 'clawL': P(-4), 'fangL': P(-3), 'stalkL': P(0, 0, 0, 1, .8)}),
                tremble=sym({'leafL': P(1.5)}))
    a2 = attack(wind={**sym({'clawL': P(-18, 6, -10), 'fangL': P(-6), 'leafL': P(-4)}), 'stinger': P(0, 0, -3)},
                strike={**sym({'clawL': P(-10, 3, -4), 'fangL': P(4), 'leafL': P(3)}), 'stinger': P(0, 0, 2)},
                tremble=sym({'clawL': P(-1.5)}))
    f0 = material_fx([(192, 282)], 'spore', SPORE, SPORE_EDGE, streak=50)
    f1 = material_fx([(124, 60), (261, 62), (56, 83), (329, 83), (48, 144), (335, 144)], 'spore', SPORE, SPORE_EDGE,
                     size=.7, spread=360, streak=26)
    f2 = material_fx([(95, 251), (290, 251)], 'spore', SPORE, SPORE_EDGE, spread=50)
    # (user: "these are great for its vine attacks and other attacks make it have new art too"): poses 0-2 above are
    # kept as they were approved and play the vine attacks (the vine weave, vine lash, trunk toss); three new,
    # distinct poses play the others:
    # 3 stinger thorns: the stinger rears back, fangs clamp; then it JABS down, fangs flung wide, thorns fly
    a3 = attack(wind={**sym({'fangL': P(10, 3, -2), 'clawL': P(8, 0, -4), 'leafL': P(-8, 0, 0, .96)}), 'stinger': P(0, 0, -12, 1, .78)},
                strike={**sym({'fangL': P(-30, -6, 2), 'clawL': P(-10, 2, 4), 'leafL': P(6)}), 'stinger': P(0, 0, 22, 1, 1.38)},
                tremble={'stinger': P(0, 0, -1.5), 'fangL': P(2), 'fangR': P(-2)})
    # 4 spore bloom: the crown closes like a bud (leaves fold in, stalks lean together) while the bulb swells -- then
    # BURSTS open wide, every frond flung out, spores puffing off the petal tips
    a4 = attack(wind={**sym({'leafL': P(-16, 6, 0, .86), 'clawL': P(-8, 3, 0), 'fangL': P(6), 'stalkL': P(8, 4, 2, 1, .85)}),
                      'stinger': P(0, 0, -4)},
                strike={**sym({'leafL': P(30, -6, -4, 1.1, 1.06), 'clawL': P(16, -4, -4), 'fangL': P(-12, -2, 0),
                               'stalkL': P(-10, -3, 0, 1.1, 1.5)}), 'stinger': P(0, 0, 6)},
                tremble=sym({'leafL': P(-1.5), 'stalkL': P(1.5)}))
    # 5 acid cannons: the outer claws (the cannons) swing out to the rails and lift, the fangs tuck; the release
    # kicks them back up and they rake in toward the middle as the acid hoses
    a5 = attack(wind={**sym({'clawL': P(22, -6, -10), 'fangL': P(10, 2, -3), 'leafL': P(4, 0, 0, .97)}), 'stinger': P(0, 0, -6, 1, .9)},
                strike={**sym({'clawL': P(4, -2, -18), 'fangL': P(12, 2, -2), 'leafL': P(-4)}), 'stinger': P(0, 0, -4)},
                tremble=sym({'clawL': P(1.8, 0, -1)}))
    ACID = ((120, 160, 20), (200, 235, 60), (245, 255, 190))
    THORN = ((70, 90, 30), (150, 190, 70), (230, 245, 180))
    tip = (192, 290)

    def tell3(t):
        def f(img, k):
            orb(img, k, tip[0], tip[1] - 8 * t, 2 + 5 * t, SPORE, strength=.4 + .6 * t)
            for j in range(int(1 + 5 * t)):            # venom beads up and drips off the stinger
                orb(img, k, tip[0] + (j % 2 * 2 - 1) * 3, tip[1] + 4 + j * 5 * t, 1.6, ACID, .8)
            return img
        return f

    def fire3(t):
        def f(img, k):
            q = 1 - t
            orb(img, k, tip[0], tip[1] + 22, 12 * q + 3, SPORE, strength=q)
            for j in range(5):                         # a spread of thorns flung down
                a = 90 + (j - 2) * 14
                r = 18 + 40 * t + abs(j - 2) * 4
                shard(img, k, tip[0] + math.cos(math.radians(a)) * r, tip[1] + 22 + math.sin(math.radians(a)) * r,
                      11, a, THORN[1], THORN[2], .6 + .4 * q)
            spark(img, k, tip[0], tip[1] + 22, 9, 30, SPORE_EDGE, seed=5 + int(t * 9), strength=q, spread=120, heading=90)
            return img
        return f

    def rec3(t):
        def f(img, k):
            orb(img, k, *tip, 5 * (1 - t), SPORE, strength=.5 * (1 - t))
            return img
        return f
    petals = [(124, 60), (261, 62), (56, 83), (329, 83), (48, 144), (335, 144)]
    bulb = (192, 182)

    def tell4(t):
        def f(img, k):
            orb(img, k, *bulb, 6 + 12 * t, SPORE, strength=.4 + .6 * t)
            for j in range(int(3 + 9 * t)):            # motes spiral in to the swelling bulb
                a = j * 40 + t * 200
                r = 90 * (1 - t) + 20
                orb(img, k, bulb[0] + math.cos(math.radians(a)) * r, bulb[1] + math.sin(math.radians(a)) * r * .8, 1.8, SPORE, .85)
            return img
        return f

    def fire4(t):
        def f(img, k):
            q = 1 - t
            orb(img, k, *bulb, 16 * q + 4, SPORE, strength=q)
            for i, (x, y) in enumerate(petals):        # a puff of spores off every petal tip, drifting out
                dx, dy = x - bulb[0], y - bulb[1]
                n = math.hypot(dx, dy)
                ux, uy = dx / n, dy / n
                for j in range(5):
                    rr = 6 + 30 * t + j * 5
                    orb(img, k, x + ux * rr + (j - 2) * 4 * uy, y + uy * rr - (j - 2) * 4 * ux, 2.6 + 2 * t, SPORE, .45 + .5 * q)
                ring(img, k, x, y, 6 + 22 * t, 1.2, SPORE[1], .7 * q)
            return img
        return f

    def rec4(t):
        def f(img, k):
            for x, y in petals:
                orb(img, k, x, y + 10 * t, 3 * (1 - t), SPORE, .6 * (1 - t))
            return img
        return f
    mouths = [(95, 251), (290, 251)]

    def tell5(t):
        def f(img, k):
            for i, (x, y) in enumerate(mouths):
                orb(img, k, x, y, 3 + 8 * t, ACID, strength=.35 + .65 * t)   # the acid boils up in the cannon
                for j in range(int(1 + 4 * t)):
                    orb(img, k, x + (j - 2) * 3, y + 6 + ((j * 7 + int(t * 20)) % 14), 1.5, ACID, .8)
            return img
        return f

    def fire5(t):
        def f(img, k):
            q = 1 - t
            for i, (x, y) in enumerate(mouths):
                side = 1 if i == 0 else -1                     # the left cannon out to the left rail
                orb(img, k, x, y, 13 * q + 4, ACID, strength=q)
                ang = math.radians(90 + side * 30 * (1 - t))  # out at the rail, swinging in to straight down
                for d in range(6, 150, 4):                  # a hose of acid
                    w = 4.5 * q + 1.5 + d * .015
                    orb(img, k, x + math.cos(ang) * d + math.sin(d * .3) * 1.5, y + math.sin(ang) * d, w * .6, ACID, .45 + .5 * q)
            return img
        return f

    def rec5(t):
        def f(img, k):
            for x, y in mouths:
                orb(img, k, x, y, 5 * (1 - t), ACID, .5 * (1 - t))
                orb(img, k, x, y + 8 + 16 * t, 1.6, ACID, .8 * (1 - t))      # a last drip
            return img
        return f
    return dict(key='Verdant', cell=384, src_frame=0, parts=parts, idle=idle, turnL=turn(lean), turnR=turn(swap_lr(lean)),
                hit=hitf, attacks=[a0, a1, a2, a3, a4, a5],
                fx={0: (*f0, False), 1: (*f1, False), 2: (*f2, False), 3: (tell3, fire3, rec3, False),
                    4: (tell4, fire4, rec4, False), 5: (tell5, fire5, rec5, False)},
                tell_seconds=[.6, .9, 1., .6, .9, 1.])


BOSSES.update(space=space, tide=tide, storm=storm, desert=desert, verdant=verdant)
