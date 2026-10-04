"""Builds the neon pixel art reference samples in docs/art-samples/neon/ with PixelKit.

    python3 samples.py            # from this folder; needs numpy + Pillow only

Every sprite is drawn at native game-pixel size (64 game px per world unit) and only
upscaled (x4, nearest) on export. Each builder is a recipe the conversion agents can copy.
"""
from __future__ import annotations

import math
import os
import random
import subprocess

import numpy as np
from PIL import Image, ImageDraw, ImageFont

import pixelkit as pk

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "../../../../.."))
OUT = os.path.join(REPO, "docs/art-samples/neon")
X = pk.EXPORT_SCALE


# =========================================================================== rail mine
def rail_mine(world: str, stage: int, S=64) -> np.ndarray:
    """Rail mine in the reference layout: rail clamp on the left, sphere with four
    pods, neon core. stage 0 dormant, 1 waking, 2 charging, 3 burst."""
    P = pk.PAL[world]
    metal = P["metal"] if "metal" in P else P["basalt"]
    neon, glowc = P["neon"], P["glow"]
    cx, cy, r = 39, 32, 16
    seed = 7
    tex = pk.value_noise(S, S, 3, seed) * 0.7 + pk.grain(S, S, seed) * 0.3

    body = pk.disc(S, S, cx, cy, r)
    img = pk.blank(S, S)
    # four bumper pods on the diagonals, laid tangentially, each its own outlined part
    pod_lights = np.zeros((S, S), bool)
    for a in (-135, -45, 45, 135):
        px, py = pk.polar_pts(cx, cy, r + 4, a)
        t = math.radians(a + 90)
        p0 = (px - 4 * math.cos(t), py - 4 * math.sin(t))
        p1 = (px + 4 * math.cos(t), py + 4 * math.sin(t))
        m = pk.capsule(S, S, p0, p1, 3.4)
        stem = pk.capsule(S, S, pk.polar_pts(cx, cy, r - 2, a), (px, py), 1.6)
        img = pk.over(img, pk.part(stem, pk.height_dome(stem, 1.5), metal, strength=2, bias=-.15))
        img = pk.over(img, pk.part(m, pk.height_dome(m, 3), metal, tex=tex, tex_amt=.14,
                                   strength=3.5, spec=.04, rim_color=metal[3]))
        q0 = (px - 2.2 * math.cos(t), py - 2.2 * math.sin(t))
        q1 = (px + 2.2 * math.cos(t), py + 2.2 * math.sin(t))
        pod_lights |= pk.line(S, S, [q0, q1], 1)
    # the sphere: true-slope sphere shading, seams, scratches, rivets, neon rim
    sph = pk.shade(body, pk.height_sphere(body, cx, cy, r), metal, tex=tex, tex_amt=.16,
                   ambient=.04, strength=r, spec=.03, bias=-.06)
    seam = pk.ring(S, S, cx, cy, 11.5, 10.6) | pk.line(S, S, [(cx - r, cy), (cx - 12, cy)]) \
        | pk.line(S, S, [(cx + 12, cy), (cx + r, cy)])
    sph = pk.tone(sph, seam & body, metal, -2)
    sph = pk.tone(sph, pk.edge(pk.disc(S, S, cx, cy, 11.5) & ~pk.disc(S, S, cx, cy, 10.6), 1, 1) & body, metal, +1)
    sph = pk.tone(sph, pk.scratches(S, S, 16, seed, within=pk.erode(body, 2)), metal, +1)
    for a in (-160, -20, 20, 160, 90, -90, 55, 125):
        rx, ry = pk.polar_pts(cx, cy, 13.5, a)
        sph = pk.highlight(sph, [(int(round(rx)), int(round(ry)))], metal[-1])
    sph = pk.rim(sph, body, neon[1], 1, 1)
    img = pk.over(img, pk.outline(sph))
    # rail clamp: stub, connector block, vertical plate (front-most)
    plate = pk.round_rect(S, S, 3, 18, 12, 46, 2)
    block = pk.round_rect(S, S, 12, 26, 22, 38, 1)
    stub = pk.rect(S, S, 22, 29, 25, 35)
    for m in (stub, block, plate):
        img = pk.over(img, pk.part(m, pk.height_bevel(m, 2), metal, tex=tex, tex_amt=.1,
                                   strength=2, bias=-.04, rim_color=metal[3]))
    img = pk.tone(img, pk.rect(S, S, 9, 21, 9, 43), metal, -2)
    img = pk.tone(img, pk.rect(S, S, 13, 27, 13, 37), metal, +1)

    # ------------------------------------------------------------ neon (emissive) layer
    em = pk.blank(S, S)
    lv = [0, 1, 3, 4][stage]
    core_r = [3, 4, 4, 5][stage]
    socket = pk.disc(S, S, cx, cy, 6.5)
    img = pk.fill(img, socket & ~pk.disc(S, S, cx, cy, 5.5), pk.INK)
    img = pk.fill(img, pk.disc(S, S, cx, cy, 5.5), metal[0])
    ring_ = pk.ring(S, S, cx, cy, 5.5, 4.5)
    pk.fill(em, ring_, neon[min(4, 1 + lv // 2 + (stage > 0))])
    if stage == 0:
        cross = pk.rect(S, S, cx - 3, cy, cx + 3, cy) | pk.rect(S, S, cx, cy - 3, cx, cy + 3)
        pk.fill(em, cross, neon[1])
        pk.fill(em, pk.disc(S, S, cx, cy, 0.5), neon[3])
    else:
        pk.fill(em, pk.disc(S, S, cx, cy, core_r - 1), neon[min(4, 1 + lv)])
        pk.fill(em, pk.disc(S, S, cx, cy, max(0.5, core_r - 2.5)), neon[4])
    # pod lights + clamp slots
    pk.fill(em, pod_lights, neon[min(4, 1 + stage)])
    slots = pk.rect(S, S, 5, 21, 6, 25) | pk.rect(S, S, 5, 30, 6, 34) | pk.rect(S, S, 5, 39, 6, 43)
    pk.fill(em, slots, neon[2 if stage < 2 else 3])
    pk.fill(em, pk.rect(S, S, 15, 30, 18, 34), neon[2 + (stage >= 2)])
    pk.fill(em, pk.rect(S, S, 16, 31, 17, 32), neon[4])
    pk.fill(em, pk.rect(S, S, 23, 31, 23, 33), neon[2])

    # ------------------------------------------------------------ glow + FX per stage
    out = pk.blank(S, S)
    if stage == 3:
        out = pk.add(out, pk.halo(S, S, cx, cy, 24, glowc, steps=5, peak=.5))
    glow_r = [1.0, 1.6, 2.4, 3.2][stage]
    out = pk.add(out, pk.glow(em, glowc, glow_r, strength=[.5, .8, 1, 1.2][stage]))
    out = pk.over(out, img)
    if stage == 3:
        # burst: the shell cracks open, light spikes cross the silhouette
        spikes = pk.blank(S, S)
        for i, a in enumerate(range(0, 360, 45)):
            L = 26 if i % 2 == 0 else 15
            wdt = 2.6 if i % 2 == 0 else 1.6
            tip = pk.polar_pts(cx, cy, L, a - 90)
            spread = 16 if i % 2 == 0 else 12
            sa, sb = pk.polar_pts(cx, cy, 7, a - 90 - spread), pk.polar_pts(cx, cy, 7, a - 90 + spread)
            m = pk.poly(S, S, [sa, tip, sb])
            m |= pk.capsule(S, S, (cx, cy), tip, wdt * 0.25)
            pk.fill(spikes, m, neon[2])
            pk.fill(spikes, pk.capsule(S, S, (cx, cy), pk.polar_pts(cx, cy, L * .8, a - 90), .4), neon[4])
        out = pk.add(out, pk.glow(spikes, glowc, 2.0, 1.0))
        out = pk.over(out, spikes)
        pk.fill(em, pk.disc(S, S, cx, cy, 6), neon[3])
        pk.fill(em, pk.disc(S, S, cx, cy, 4), neon[4])
        pk.scatter(out, "dot", 14, (4, 2, 62, 62), seed=3, color=neon[3])
        pk.scatter(out, "twinkle", 3, (4, 2, 62, 62), seed=4, color=neon[3], sizes=[1])
    out = pk.over(out, em)
    if stage == 2:
        # charging: energy arcs orbit the shell
        fx = pk.blank(S, S)
        pk.orbit_arc(fx, cx, cy, r + 8, -80, 30, neon[3], wobble=.5, seed=1)
        pk.orbit_arc(fx, cx, cy, r + 7, 100, 190, neon[3], wobble=.5, seed=2)
        pk.arc(fx, (cx + 5, cy - 24), (cx + 23, cy - 7), neon[2], jag=1.2, seed=4, core=neon[4], bow=-4)
        out = pk.add(out, pk.glow(fx, glowc, 1.0, .45, steps=3))
        out = pk.over(out, fx)
        pk.scatter(out, "twinkle", 5, (8, 6, 60, 58), seed=9, color=neon[3], sizes=[1])
    if stage == 1:
        pk.scatter(out, "dot", 4, (14, 10, 58, 54), seed=5, color=neon[2])
    return out


# =========================================================================== enemies
def fighter(world: str, frame=0, S=58) -> np.ndarray:
    """Arrow fighter pointing DOWN (enemies fly toward the player). Chitin/metal hull,
    neon cockpit eye and engine vents; idle = engine flicker + light pulse."""
    P = pk.PAL[world]
    hull = P.get("metal", P.get("ice", P.get("basalt")))
    if world == "frost":
        hull = P["metal"]
    neon, glowc = P["neon"], P["glow"]
    cx = S // 2
    bob = [0, 0, 1, 1][frame % 4]
    y0 = 8 + bob
    body = pk.poly(S, S, [(cx, y0 + 40), (cx - 9, y0 + 18), (cx - 6, y0), (cx + 6, y0), (cx + 9, y0 + 18)])
    wingL = pk.poly(S, S, [(cx - 6, y0 + 6), (cx - 25, y0 + 22), (cx - 24, y0 + 30), (cx - 8, y0 + 24)])
    wingR = pk.poly(S, S, [(cx + 6, y0 + 6), (cx + 25, y0 + 22), (cx + 24, y0 + 30), (cx + 8, y0 + 24)])
    podL = pk.capsule(S, S, (cx - 21, y0 + 16), (cx - 21, y0 + 32), 2.6)
    podR = pk.capsule(S, S, (cx + 21, y0 + 16), (cx + 21, y0 + 32), 2.6)
    tex = pk.value_noise(S, S, 3, 11) * .6 + pk.grain(S, S, 11) * .4
    img = pk.blank(S, S)
    for m in (wingL, wingR):
        img = pk.over(img, pk.shade(m, pk.height_bevel(m, 3), hull, tex=tex, tex_amt=.14, bias=-.08))
    for m in (podL, podR):
        img = pk.over(img, pk.shade(m, pk.height_dome(m), hull, tex=tex, tex_amt=.1))
    img = pk.over(img, pk.shade(body, pk.height_dome(body, 5), hull, tex=tex, tex_amt=.14))
    # panel seams + scratches
    img = pk.tone(img, pk.line(S, S, [(cx, y0 + 2), (cx, y0 + 14)]) & body, hull, -2)
    img = pk.tone(img, pk.line(S, S, [(cx - 7, y0 + 18), (cx + 7, y0 + 18)]) & body, hull, -2)
    img = pk.tone(img, pk.line(S, S, [(cx - 12, y0 + 14), (cx - 20, y0 + 22)]) & wingL, hull, -1)
    img = pk.tone(img, pk.line(S, S, [(cx + 12, y0 + 14), (cx + 20, y0 + 22)]) & wingR, hull, -1)
    img = pk.tone(img, pk.scratches(S, S, 10, 3, within=pk.erode(body | wingL | wingR)), hull, +1)
    if world == "frost":
        # ice crust on the wing leading edges
        ice = P["ice"]
        crust = (pk.dilate(pk.edge(wingL, 0, -1) | pk.edge(wingR, 0, -1))) & (wingL | wingR)
        img = pk.fill(img, crust, ice[3])
        img = pk.fill(img, pk.edge(crust, 0, -1), ice[5])
    if world == "ember":
        mg = P["magma"]
        vein = pk.cracks(S, S, [(cx - 14, y0 + 20), (cx + 14, y0 + 20), (cx, y0 + 26)], 9, 5,
                         within=pk.erode(body | wingL | wingR))
        img = pk.fill(img, vein, mg[2])
    img = pk.rim(img, body | wingR, neon[1], 1, 1)
    img = pk.outline(img)
    # neon: cockpit eye, wing lights, engine vents (top: engines face away from the player)
    em = pk.blank(S, S)
    pulse = [0, 1, 2, 1][frame % 4]
    eye = pk.poly(S, S, [(cx - 3, y0 + 24), (cx + 3, y0 + 24), (cx, y0 + 31)])
    pk.fill(em, eye, neon[2 + (pulse > 0)])
    pk.fill(em, pk.rect(S, S, cx, y0 + 25, cx, y0 + 27), neon[4])
    for sx in (-21, 21):
        pk.fill(em, pk.rect(S, S, cx + sx, y0 + 30, cx + sx, y0 + 33), neon[2 + (pulse == 2)])
    flick = [3, 5, 4, 6][frame % 4]
    for sx in (-3, 3):
        pk.fill(em, pk.rect(S, S, cx + sx - 1, y0 - flick, cx + sx, y0 - 1), neon[1])
        pk.fill(em, pk.rect(S, S, cx + sx - 1, y0 - 2, cx + sx, y0 - 1), neon[3])
    out = pk.add(pk.blank(S, S), pk.glow(em, glowc, 1.4, .8))
    out = pk.over(out, img)
    out = pk.over(out, em)
    return out


def alien(world: str, frame=0, S=44) -> np.ndarray:
    """Organic alien: chitin carapace, bioluminescent eyes and tendrils."""
    P = pk.PAL[world]
    neon, glowc = P["neon"], P["glow"]
    shell = P["ice"] if world == "frost" else P.get("basalt", P.get("metal"))
    cx, cy = S // 2, 18
    sway = [0, 1, 0, -1][frame % 4]
    cap = pk.ellipse(S, S, cx - 15, cy - 11, cx + 15, cy + 9)
    jaw = pk.poly(S, S, [(cx - 11, cy + 4), (cx + 11, cy + 4), (cx + 6, cy + 13), (cx - 6, cy + 13)])
    legs = np.zeros((S, S), bool)
    for i, sx in enumerate((-9, -3, 3, 9)):
        legs |= pk.line(S, S, [(cx + sx, cy + 10), (cx + sx * 1.3 + sway * (1 if i % 2 else -1), cy + 21),
                               (cx + sx * 1.1, cy + 25)], 2)
    tex = pk.value_noise(S, S, 2, 21)
    img = pk.shade(legs, pk.height_dome(legs), shell, tex=tex, tex_amt=.1, bias=-.12)
    img = pk.over(img, pk.shade(jaw, pk.height_dome(jaw, 3), shell, tex=tex, tex_amt=.12, bias=-.05))
    hf = pk.height_dome(cap, 7)
    if world == "frost":
        ids, tilt = pk.facets(S, S, 9, 4, within=cap)
        hf = np.clip(hf + tilt * .25, 0, 1) * cap
    img = pk.over(img, pk.shade(cap, hf, shell, tex=tex, tex_amt=.12, strength=8))
    if world == "frost":
        img = pk.tone(img, pk.facet_lines(ids, cap), shell, -1)
    else:
        mg = P["magma"]
        img = pk.fill(img, pk.cracks(S, S, [(cx - 6, cy - 6), (cx + 7, cy - 3)], 8, 2, within=pk.erode(cap)), mg[2])
    img = pk.rim(img, cap, neon[1], 1, 1)
    img = pk.outline(img)
    em = pk.blank(S, S)
    blink = frame % 4 == 3
    for sx in (-6, 6):
        e = pk.disc(S, S, cx + sx, cy + 1, 2.2) if not blink else pk.rect(S, S, cx + sx - 2, cy + 1, cx + sx + 2, cy + 1)
        pk.fill(em, e, neon[2])
        if not blink:
            pk.fill(em, pk.disc(S, S, cx + sx - .5, cy + .5, .5), neon[4])
    pk.fill(em, pk.rect(S, S, cx - 1, cy - 8, cx + 1, cy - 7), neon[3])
    out = pk.add(pk.blank(S, S), pk.glow(em, glowc, 1.3, .9))
    out = pk.over(out, img)
    return pk.over(out, em)


def rock(world: str, S=44, seed=3) -> np.ndarray:
    """Asteroid/rock: lumpy silhouette, faceted material ramp, world accent in cracks."""
    P = pk.PAL[world]
    rng = random.Random(seed)
    cx = cy = S / 2
    pts = []
    ph, ps = rng.uniform(0, 6), rng.uniform(0, 6)
    for i in range(18):
        a = 2 * math.pi * i / 18
        rr = 14 + 2.2 * math.sin(2 * a + ph) + 1.4 * math.sin(3 * a + ps) + rng.uniform(-.8, .8)
        pts.append((cx + rr * math.cos(a), cy + rr * 0.92 * math.sin(a)))
    m = pk.poly(S, S, pts)
    ids, tilt = pk.facets(S, S, 8, seed, within=m)
    hf = np.clip(pk.height_dome(m, 6) + tilt * .18, 0, 1) * m
    mat = P["ice"] if world == "frost" else P["basalt"]
    tex = pk.value_noise(S, S, 3, seed) * .5 + pk.grain(S, S, seed) * .5
    img = pk.shade(m, hf, mat, tex=tex, tex_amt=.16, strength=8, dither=.35)
    img = pk.tone(img, pk.facet_lines(ids, m), mat, -1)
    # craters
    for (dx, dy, cr) in ((-5, -3, 2.5), (5, 5, 2), (6, -6, 1.5)):
        c = pk.disc(S, S, cx + dx, cy + dy, cr)
        img = pk.tone(img, c & m, mat, -2)
        img = pk.tone(img, pk.edge(c, 1, 1) & m, mat, +2)
    if world == "ember":
        mg = P["magma"]
        v = pk.cracks(S, S, [(cx, cy), (cx - 4, cy + 3)], 12, seed, within=pk.erode(m, 2))
        img = pk.fill(img, pk.dilate(v) & pk.erode(m), mg[1])
        img = pk.fill(img, v, mg[3])
        glowm = v
    else:
        # frost: rime crystals glinting on the lit side
        glowm = pk.edge(m, -1, -1) & (tilt > .3)
        img = pk.fill(img, glowm, P["ice"][5])
    img = pk.rim(img, m, P["neon"][1], 1, 1)
    img = pk.outline(img)
    out = pk.add(pk.blank(S, S), pk.glow(glowm, P["glow"], 1.2, .6))
    return pk.over(out, img)


# =========================================================================== boss bust
def boss_bust(world="space", W=176, H=144) -> np.ndarray:
    """Concept bust for a boss: big armoured head/hull, many material ramps,
    neon eye cluster, energy mouth; 176 px wide ~ 2.75 u (boss cell 3.3 u = 211 px)."""
    P = pk.PAL[world]
    metal, trim, neon, glowc = P["metal"], P["trim"], P["neon"], P["glow"]
    cx, cy = W // 2, 66
    tex = pk.value_noise(W, H, 5, 31) * .6 + pk.grain(W, H, 31) * .4
    img = pk.blank(W, H)
    # shoulders / pauldrons
    for s in (-1, 1):
        m = pk.ellipse(W, H, cx + s * 58 - 26, 70, cx + s * 58 + 26, 128)
        img = pk.over(img, pk.shade(m, pk.height_dome(m, 10), metal, tex=tex, tex_amt=.14, bias=-.06))
        for k in range(3):
            img = pk.tone(img, pk.line(W, H, [(cx + s * 40, 84 + k * 12), (cx + s * 78, 80 + k * 12)]) & m, metal, -2)
        img = pk.fill(img, pk.disc(W, H, cx + s * 58, 92, 3), metal[0])
        img = pk.fill(img, pk.disc(W, H, cx + s * 58, 92, 1.6), neon[2])
    # cables
    for s in (-1, 1):
        cab = pk.line(W, H, [(cx + s * 22, 100), (cx + s * 34, 118), (cx + s * 30, 140)], 4)
        img = pk.over(img, pk.shade(cab, pk.height_dome(cab), trim, bias=-.05))
    # head: crowned dome with a jaw
    head = pk.poly(W, H, [(cx - 34, 40), (cx - 26, 14), (cx - 10, 4), (cx + 10, 4), (cx + 26, 14),
                          (cx + 34, 40), (cx + 30, 86), (cx + 14, 110), (cx - 14, 110), (cx - 30, 86)])
    img = pk.over(img, pk.shade(head, pk.height_dome(head, 16), metal, tex=tex, tex_amt=.16, strength=10))
    crest = pk.poly(W, H, [(cx - 5, 2), (cx + 5, 2), (cx + 3, 44), (cx - 3, 44)])
    img = pk.over(img, pk.shade(crest, pk.height_bevel(crest, 2), trim))
    # visor band
    visor = pk.poly(W, H, [(cx - 32, 46), (cx + 32, 46), (cx + 28, 62), (cx - 28, 62)])
    img = pk.fill(img, visor, pk.INK)
    img = pk.fill(img, pk.edge(visor, 0, 1), metal[1])
    # jaw grille
    jaw = pk.poly(W, H, [(cx - 20, 78), (cx + 20, 78), (cx + 12, 104), (cx - 12, 104)])
    img = pk.fill(img, jaw, metal[0])
    for k in range(-16, 17, 6):
        img = pk.fill(img, pk.line(W, H, [(cx + k, 80), (cx + k * .6, 102)]) & jaw, metal[2])
    img = pk.tone(img, pk.scratches(W, H, 40, 4, (2, 5), within=pk.erode(head, 3)), metal, +1)
    for x in range(cx - 28, cx + 29, 8):
        img = pk.highlight(img, [(x, 68)], metal[-1])
    img = pk.rim(img, head, neon[1], 1, 1, width=1)
    img = pk.outline(img)
    # neon: six-eye cluster in the visor, mouth reactor, crest strip
    em = pk.blank(W, H)
    for i, ex in enumerate((-22, -12, -4, 4, 12, 22)):
        rr = 3.2 if abs(ex) == 12 else 2.2
        pk.fill(em, pk.disc(W, H, cx + ex, 54, rr), neon[2])
        pk.fill(em, pk.disc(W, H, cx + ex, 54, rr - 1.5), neon[4])
    pk.fill(em, pk.rect(W, H, cx - 1, 8, cx + 1, 40), neon[1])
    pk.fill(em, pk.rect(W, H, cx, 10, cx, 38), neon[3])
    mouth = pk.ellipse(W, H, cx - 9, 86, cx + 9, 98) & jaw
    pk.fill(em, mouth, neon[2])
    pk.fill(em, pk.ellipse(W, H, cx - 5, 89, cx + 5, 95), neon[4])
    out = pk.add(pk.blank(W, H), pk.glow(em, glowc, 2.6, 1.0))
    out = pk.over(out, img)
    out = pk.over(out, em)
    fx = pk.blank(W, H)
    pk.arc(fx, (cx - 9, 92), (cx - 30, 120), neon[3], 2, 1, core=neon[4])
    pk.arc(fx, (cx + 9, 92), (cx + 34, 124), neon[3], 2, 2, core=neon[4])
    out = pk.add(out, pk.glow(fx, glowc, 1.2, .7))
    return pk.over(out, fx)


# =========================================================================== background
def backdrop(world: str, W=184, H=240, seed=5) -> np.ndarray:
    """A background vignette: dark base, low-contrast neon pixel art, sparse accents.
    Obeys the v2 background limits (value <= 35 %, accents <= 60 % value / 70 % sat,
    accents <= 3 % of pixels)."""
    P = pk.PAL[world]
    bg = P["bg"]
    rng = random.Random(seed)
    yy, xx = np.mgrid[0:H, 0:W]
    v = (yy / H) * .9 + pk.value_noise(W, H, 24, seed, 3) * .55
    idx = np.clip(np.round(v * 2.2 + pk.bayer(W, H) * .9), 0, 2).astype(int)
    img = pk.blank(W, H)
    img[..., :3] = bg[idx]
    img[..., 3] = 255
    if world == "frost":
        # far ice cliffs, two parallax bands, bg ramp tones 3-4 only
        for band, (base, amp, tone_) in enumerate(((150, 24, 3), (190, 18, 4))):
            ridge = np.array([base - amp * abs(math.sin(x * .045 + band * 2) + .5 * math.sin(x * .13 + band))
                              for x in range(W)])
            m = yy > ridge[None, :]
            img = pk.fill(img, m, bg[tone_ - 1])
            img = pk.fill(img, pk.edge(m, 0, -1), bg[tone_])
            # facet strokes on cliff faces
            for k in range(14):
                x0 = rng.randrange(W)
                img = pk.fill(img, pk.line(W, H, [(x0, ridge[x0] + 4), (x0 + 6, ridge[x0] + 18)]) & m, bg[tone_])
        # aurora: thin sparse neon line, dimmed
        acc = pk.hexrgb(P["bg_neon"])
        for x in range(0, W, 1):
            y = int(40 + 14 * math.sin(x * .05) + 6 * math.sin(x * .17))
            if rng.random() < .55:
                pk._put(img, x, y, acc)
        pk.scatter(img, "snow", 7, (0, 0, W, 150), seed, color=bg[4], core=P["bg_neon"], sizes=[2])
        pk.scatter(img, "dot", 40, (0, 0, W, H), seed + 1, color=bg[4])
    elif world == "ember":
        for band, (base, tone_) in enumerate(((160, 3), (205, 4))):
            ridge = np.array([base - 30 * abs(math.sin(x * .03 + band * 1.7)) - 6 * math.sin(x * .2) for x in range(W)])
            m = yy > ridge[None, :]
            img = pk.fill(img, m, bg[tone_ - 1])
            img = pk.fill(img, pk.edge(m, 0, -1), bg[tone_])
        river = pk.cracks(W, H, [(10, 225), (100, 215), (170, 230)], 60, seed, within=yy > 205)
        img = pk.fill(img, river, P["bg_neon"])
        pk.scatter(img, "dot", 25, (0, 0, W, 200), seed, color=P["bg_neon"])
    else:
        pk.scatter(img, "dot", 60, (0, 0, W, H), seed, color=bg[4])
        pk.scatter(img, "twinkle", 5, (0, 0, W, H), seed, color=bg[4], core=P["bg_neon"], sizes=[1])
    return img



# =========================================================================== compose helpers
def label(im: Image.Image, text, xy, color=(220, 230, 240)):
    d = ImageDraw.Draw(im)
    d.text(xy, text, fill=color, font=ImageFont.load_default())


def panel(images, labels, pad=16, bg="#05060c", top=22):
    """Lay out already-upscaled PIL images in a row with labels."""
    W = sum(i.width for i in images) + pad * (len(images) + 1)
    H = max(i.height for i in images) + pad + top
    out = Image.new("RGBA", (W, H), pk.hexrgb(bg) + (255,))
    x = pad
    for im, t in zip(images, labels):
        out.alpha_composite(im, (x, top))
        label(out, t, (x, 4))
        x += im.width + pad
    return out


def ship_sprite(game_px=37) -> Image.Image:
    """Rest frame of a current (unchanged, cel-style) player hull, sized to the game:
    0.58 u longest edge = 37 game px -> 148 screen-texture px at x4."""
    sh = Image.open(os.path.join(REPO, "Pause/Assets/Art/Resources/ShipArt/Hulls/NeonComet.png")).convert("RGBA")
    cell = sh.crop((0, 0, 256, 256))
    cell = cell.crop(cell.getbbox())
    s = game_px * X / max(cell.size)
    return cell.resize((round(cell.width * s), round(cell.height * s)), Image.LANCZOS)


# =========================================================================== main
def main():
    os.makedirs(OUT, exist_ok=True)
    big = lambda a: pk.to_image(pk.upscale(a, X))

    # 1. Space rail mine: recreation vs the original row --------------------------------
    frames = [rail_mine("space", s) for s in range(4)]
    pk.save(pk.upscale(pk.strip(frames), X), os.path.join(OUT, "mine_space_strip.png"))
    pk.save_gif(frames, os.path.join(OUT, "mine_space_burst.gif"), [12, 4, 4, 8])
    try:
        raw = subprocess.run(["git", "-C", REPO, "show",
                              "18b5e5f^:Pause/Assets/Art/Resources/Vfx/rail_bomb_themes_atlas.png"],
                             capture_output=True, check=True).stdout
        import io
        orig = Image.open(io.BytesIO(raw)).convert("RGBA")
        row = orig.crop((0, 0, orig.width, orig.height // 4))
        row = row.resize((64 * X * 4, 64 * X), Image.LANCZOS)
        ours = big(pk.strip(frames))
        cmp_ = Image.new("RGBA", (ours.width + 32, ours.height * 2 + 70), pk.hexrgb("#05060c") + (255,))
        label(cmp_, "ORIGINAL reference (rail_bomb_themes_atlas.png @ 18b5e5f^, Space row)", (16, 6))
        cmp_.alpha_composite(row, (16, 22))
        label(cmp_, "PIXELKIT recreation (64x64 game px per frame, x4 nearest)  dormant > waking > charging > burst",
              (16, ours.height + 44))
        cmp_.alpha_composite(ours, (16, ours.height + 60))
        cmp_.save(os.path.join(OUT, "mine_space_vs_original.png"))
    except Exception as e:  # noqa: BLE001
        print("original atlas unavailable:", e)

    # 2. enemies for two worlds -----------------------------------------------------------
    ims, labs = [], []
    for w in ("frost", "ember"):
        f, a, r = fighter(w), alien(w), rock(w)
        for name, s in (("fighter", f), ("alien", a), ("rock", r)):
            pk.save(pk.upscale(s, X), os.path.join(OUT, f"{w}_{name}.png"))
            ims.append(big(s))
            labs.append(f"{w} {name}")
    panel(ims, labs).save(os.path.join(OUT, "enemies_frost_ember.png"))

    idle = [fighter("ember", i) for i in range(4)]
    pk.save(pk.upscale(pk.strip(idle), X), os.path.join(OUT, "ember_fighter_idle_strip.png"))
    pk.save_gif(idle, os.path.join(OUT, "ember_fighter_idle.gif"), [4, 4, 4, 4])

    # 3. boss bust -------------------------------------------------------------------------
    b = boss_bust("space")
    pk.save(pk.upscale(b, X), os.path.join(OUT, "boss_space_bust.png"))

    # 4. background vignette with an enemy and the player ship -----------------------------
    for w in ("frost",):
        bg = backdrop(w)
        m = pk.bg_check(bg)
        print(w, "backdrop limits", m)
        assert m["ok"], m
        comp = pk.over(bg, fighter(w), 34, 30)
        comp = pk.over(comp, rock(w), 120, 70)
        compim = big(comp)
        ship = ship_sprite()
        compim.alpha_composite(ship, (compim.width // 2 - ship.width // 2, compim.height - ship.height - 60))
        compim.save(os.path.join(OUT, f"vignette_{w}.png"))

    # 5. palette swatches ------------------------------------------------------------------
    sw = [big(pk.palette_swatch({k: v for k, v in pk.PAL[w].items()}, 8)) for w in pk.PAL]
    panel(sw, list(pk.PAL)).save(os.path.join(OUT, "palette.png"))

    # 6. contact sheet ---------------------------------------------------------------------
    parts = [Image.open(os.path.join(OUT, n)) for n in
             ("mine_space_vs_original.png", "enemies_frost_ember.png")]
    lower = panel([big(b), Image.open(os.path.join(OUT, "vignette_frost.png")),
                   Image.open(os.path.join(OUT, "palette.png"))],
                  ["boss concept bust (space)", "frost vignette: bg + fighter + rock + player ship", "palettes"])
    parts.append(lower)
    W = max(p.width for p in parts)
    sheet = Image.new("RGBA", (W, sum(p.height for p in parts)), pk.hexrgb("#05060c") + (255,))
    y = 0
    for p in parts:
        sheet.alpha_composite(p.convert("RGBA"), (0, y))
        y += p.height
    sheet.save(os.path.join(OUT, "sample-sheet.png"))
    print("wrote", OUT)


if __name__ == "__main__":
    main()
