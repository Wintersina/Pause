#!/usr/bin/env python3
"""Attack shapes, secret-power effects and secret-meter badges for every ship.

Same style and palette as weapons.py (flat cels, thick INK, hard-edged light,
PALETTES row per ship), same 24 fps tick tables. Two shared atlases:

  ../../Resources/Weapons/AttackFx.png      128px cells, 16 x 15, row = ship id - 1
      cols 0-3   attack loop    beam / flame cone / chain arc / javelin
                                column / rail flash / orbit ring ...
      cols 4-7   attack burst   fireball blast / strike splash / sparks ...
      cols 8-11  power loop     cloak shimmer / magnet field / black hole ...
      cols 12-15 power burst    shield pop / EMP ring / nova ...
  ../../Resources/Weapons/SecretMeter.png   64px cells, 16 x 15, row = ship id - 1
      cols 0-9   badge filling  cols 10-13 ready loop
      col 14     fire flash     col 15 icon only

ShipFxArt.cs slices the same layout. Shapes meant to be stretched in game
(beams, cones, arcs, columns) fill the whole cell along their long axis:
vertical ones run bottom (origin) to top, the chain arc runs left to right.

    python3 weapons.py              renders these too
    python3 weapons.py --fx-only    just these
"""
import math
import os
import random
import shutil
import subprocess
import tempfile

from PIL import Image, ImageDraw

import weapons as W
from weapons import (INK, NIGHT, NIGHT_HI, WHITE, PURE, GUN, GUN_SH, GUN_HI, PALETTES,
                     P, n, pts, poly, line, circle, path, g, cel, inked_line, light, light_circle,
                     speed_lines, star, burst, hexagon, ngon, band, jag, clamp01, ease, step, mix, frame)

ROWS = 15
COLS = 16
CELL = 128
METER_CELL = 64

# Which attack / power each ship row draws (ShipLoadout.cs is the source of
# truth for the gameplay; keep in step).
ATTACKS = {1: "comet", 2: "chain", 3: "fireball", 4: "rail", 5: "volley", 6: "volley", 7: "volley",
           8: "javelin", 9: "torch", 10: "seeker", 11: "ricochet", 12: "gatling", 13: "beam",
           14: "fan", 15: "orbit"}
POWERS = {1: "pulse", 2: "emp", 3: "nova", 4: "capacitor", 5: "battery", 6: "cloak", 7: "magnet",
          8: "clap", 9: "decoy", 10: "bubble", 11: "dash", 12: "hole", 13: "shower", 14: "mend",
          15: "shell"}


class Pal:
    def __init__(self, ship):
        (self.main, self.shade, self.hi, self.energy, self.energy_hi, self.spot) = PALETTES[ship]
        self.ship = ship

    def rng(self, kind, k):
        return random.Random(self.ship * 7919 + kind * 131 + k)


# ------------------------------------------------------------ attack loop ----

def bolt_h(rng, y=64, amp=14, steps=9):
    return jag(rng, 0, y, 128, y, steps, amp)


def attack_loop(doc, p, k):
    kind = ATTACKS[p.ship]
    rng = p.rng(1, k)
    if kind == "chain":
        # left-to-right lightning arc, a fresh jag every drawing
        pts_ = bolt_h(rng)
        s = line(pts_, p.energy, 20, op=0.35)
        s += inked_line(pts_, p.energy, 7, WHITE, ink=5)
        for _ in range(2):
            i = rng.randint(2, 6)
            x, y = pts_[i]
            s += inked_line(jag(rng, x, y, x + rng.uniform(10, 22), y + rng.choice([-1, 1]) * rng.uniform(14, 24), 3, 4),
                            p.energy, 3, WHITE, ink=3)
        return s
    if kind in ("beam",):
        # tractor beam: a tall column with stacked rings scrolling up
        w = 34 + (k % 2) * 3
        s = poly([(64 - w, 128), (64 - w, 0), (64 + w, 0), (64 + w, 128)], fill=p.energy, op=0.3)
        s += poly([(64 - w * 0.62, 128), (64 - w * 0.62, 0), (64 + w * 0.62, 0), (64 + w * 0.62, 128)], fill=p.energy)
        s += poly([(64 - w * 0.22, 128), (64 - w * 0.22, 0), (64 + w * 0.22, 0), (64 + w * 0.22, 128)], fill=WHITE)
        for i in range(4):
            y = (128 - (i * 32 + k * 8)) % 128 + 4
            s += poly(ngon(64, y, w * 0.95, 10, 0, sy=0.22), stroke=INK, w=5)
            s += poly(ngon(64, y, w * 0.95, 10, 0, sy=0.22), stroke=p.main, w=2.6)
        return s
    if kind == "rail":
        # rail flash, played once: thick bar, thinner, split lines, ghost
        widths = [30, 18, 10, 5]
        w = widths[k]
        s = ""
        if k < 3:
            s += poly([(64 - w * 1.8, 128), (64 - w * 1.8, 0), (64 + w * 1.8, 0), (64 + w * 1.8, 128)], fill=p.energy, op=0.35)
            s += poly([(64 - w, 128), (64 - w, 0), (64 + w, 0), (64 + w, 128)], fill=p.main if k == 0 else p.energy)
            s += poly([(64 - w * 0.45, 128), (64 - w * 0.45, 0), (64 + w * 0.45, 0), (64 + w * 0.45, 128)], fill=WHITE)
        for i in range(5 - k):
            y = (i * 29 + k * 11) % 128
            s += poly(ngon(64, y, 22 - k * 3, 8, 0, sy=0.25), stroke=p.energy_hi, w=3)
        if k == 3:
            s += line([(56, 128), (56, 0)], p.energy, 2, op=0.6) + line([(72, 128), (72, 0)], p.energy, 2, op=0.6)
        return s
    if kind == "javelin":
        # strike column, played once: the bolt down the lane, then the after-glow
        if k <= 1:
            pts_ = jag(rng, 64, 0, 64, 128, 8, 10 if k == 0 else 6)
            s = line(pts_, p.energy, 34, op=0.3)
            s += inked_line(pts_, p.energy, 12 - k * 4, WHITE, ink=5)
            return s
        w = 16 - (k - 2) * 7
        return poly([(64 - w, 128), (64 - w, 0), (64 + w, 0), (64 + w, 128)], fill=p.energy, op=0.45 - 0.15 * (k - 2)) + \
            speed_lines(rng, 64, 64, 10, 60, 6, p.energy_hi, 2, a0=-10, a1=10) + \
            speed_lines(rng, 64, 64, 10, 60, 6, p.energy_hi, 2, a0=170, a1=190)
    if kind == "torch":
        # the blowtorch cone: apex at the bottom middle, widening to the top
        jit = [rng.uniform(-6, 6) for _ in range(8)]
        outer = [(64, 128), (10 + jit[0], 40), (18 + jit[1], 10), (40, 2 + jit[2]), (64, 12 + jit[3]),
                 (88, 2 + jit[4]), (110 + jit[5], 10), (118 + jit[6], 40)]
        s = cel(doc, outer, p.main, p.shade, None, ink=5)
        mid = [(64, 124), (28 + jit[1], 44), (40, 18 + jit[2]), (64, 28 + jit[3]), (88, 18 + jit[4]), (100 + jit[5], 44)]
        s += poly(mid, fill=p.energy)
        core = [(64, 120), (48, 58 + jit[6]), (64, 46 + jit[7]), (80, 58 + jit[0])]
        s += poly(core, fill=WHITE)
        s += speed_lines(rng, 64, 128, 40, 118, 5, p.energy_hi, 2.4, a0=-28, a1=28)
        return s
    if kind == "orbit":
        # the disc's orbit track: hex plates round a ring, a lit arc chasing
        s = ""
        for i in range(12):
            a0 = i * 30 + 4
            s += poly(band(64, 64, 50, 58, a0, a0 + 22), fill=NIGHT, stroke=INK, w=3, op=0.75)
        head = k * 90
        s += poly(band(64, 64, 49, 59, head - 80, head), fill=p.energy, op=0.85)
        s += poly(band(64, 64, 52, 56, head - 40, head), fill=WHITE)
        return s
    if kind == "fireball":
        # flame ring (unused in play; reads as the fireball's heat haze)
        return poly(burst(rng, 64, 64, 50, 36, 12, k * 7), stroke=p.main, w=4, op=0.7)
    if kind == "gatling":
        # tracer: a short hot streak
        s = poly([(58, 128), (64, 10), (70, 128)], fill=p.energy, op=0.6)
        s += poly([(61, 128), (64, 30), (67, 128)], fill=WHITE)
        return s
    if kind == "volley":
        # lock-on reticle (unused in play; the volley uses the shot art)
        r = 46 - k * 4
        s = ""
        for i in range(4):
            s += poly(band(64, 64, r - 5, r + 3, i * 90 + 20, i * 90 + 70), fill=p.energy, stroke=INK, w=3)
        return s
    # comet, seeker, ricochet, fan: a streak of the ship's light
    s = poly([(52, 128), (64, 6), (76, 128)], fill=p.main, op=0.55)
    s += poly([(58, 128), (64, 30), (70, 128)], fill=p.energy)
    return s


def attack_burst(doc, p, k):
    kind = ATTACKS[p.ship]
    rng = p.rng(2, 0)
    if kind == "fireball":
        # a solar blast: pinch, flash, the held sun burst, breaking up
        if k == 0:
            return poly(star(64, 64, 26, 9, 6, 0), fill=WHITE, stroke=INK, w=3)
        if k == 1:
            return poly(star(64, 64, 62, 26, 12, 0), fill=PURE, stroke=W.KANEDA, w=4)
        if k == 2:
            s = poly(burst(rng, 64, 64, 62, 40, 14, 6), fill=p.main, stroke=INK, w=5)
            s += poly(burst(rng, 64, 64, 44, 28, 12, 20), fill=p.energy)
            s += poly(burst(rng, 64, 64, 22, 14, 8, 0), fill=WHITE)
            return s
        s = poly(band(64, 64, 52, 60, 0, 359.9, 30), fill=p.energy_hi, op=0.8)
        for i in range(10):
            ang = i * 36 + rng.uniform(-8, 8)
            x, y = P(64, 64, 42, ang)
            s += cel(doc, [P(x, y, 9, ang), P(x, y, 4, ang + 120), P(x, y, 4, ang - 120)], p.main, p.shade, None, ink=3)
        s += poly(burst(rng, 64, 64, 22, 14, 8, 30), fill=p.main, stroke=INK, w=4)
        return s
    if kind == "javelin":
        # the strike splash where the javelin lands
        if k == 0:
            return poly(star(64, 64, 58, 18, 6, 0, sy=0.6), fill=PURE, stroke=W.KANEDA, w=4)
        s = poly(star(64, 70, 54 - k * 8, 14, 8, k * 8, sy=0.55), fill=p.energy, stroke=INK, w=4)
        s += poly(star(64, 70, 30 - k * 6, 8, 8, k * 8, sy=0.55), fill=WHITE)
        s += speed_lines(rng, 64, 70, 30, 62, 8, p.energy_hi, 2.6, a0=-80, a1=80)
        return s
    if kind == "beam":
        # emitter flare at the beam's base
        s = poly(ngon(64, 64, 40 - k * 6, 10, k * 9, sy=0.4), fill=p.energy, stroke=INK, w=4)
        s += poly(ngon(64, 64, 22 - k * 3, 10, 0, sy=0.4), fill=WHITE)
        return s
    if kind == "gatling":
        # gatling muzzle flash, pointing up
        if k >= 3:
            return speed_lines(rng, 64, 80, 20, 60, 6, p.energy_hi, 2.4, a0=-25, a1=25)
        s = poly(star(64, 64, 54 - k * 10, 12, 6, 0, sx=0.55), fill=p.main, stroke=INK, w=4)
        s += poly(star(64, 64, 28 - k * 5, 6, 6, 0, sx=0.5), fill=WHITE)
        return s
    # sparks at a hit / muzzle ring (chain, rail, torch, orbit, ...)
    if k == 0:
        return poly(star(64, 64, 46, 14, 6, 15), fill=PURE, stroke=W.KANEDA, w=4)
    s = poly(star(64, 64, 44 - k * 9, 12, 6, k * 20), fill=p.energy, stroke=INK, w=4)
    s += poly(star(64, 64, 20 - k * 4, 6, 6, k * 20), fill=WHITE)
    s += speed_lines(rng, 64, 64, 30, 58 - k * 4, 8, p.energy_hi, 2.4)
    return s


# ------------------------------------------------------------ power fx ------

def power_loop(doc, p, k):
    kind = POWERS[p.ship]
    rng = p.rng(3, k)
    if kind == "cloak":
        # phase shimmer: broken outline rings sliding round, hollow middle
        s = ""
        for r, w, off in ((54, 4, 0), (44, 3, 30), (34, 2.4, 60)):
            for i in range(6):
                a0 = i * 60 + off + k * 15 * (1 if r != 44 else -1)
                s += poly(band(64, 64, r - w, r + w, a0, a0 + 34), fill=p.energy if r != 44 else p.energy_hi, op=0.75)
        return s
    if kind == "magnet":
        # field arcs drawing inward
        s = ""
        for i in range(3):
            r = 58 - ((i * 16 + k * 5) % 48)
            for j in range(4):
                a0 = j * 90 + 15
                s += poly(band(64, 64, r - 3, r + 3, a0, a0 + 60), fill=p.main if i % 2 else p.energy, stroke=INK, w=2)
        s += poly(ngon(64, 64, 10, 6, k * 15), fill=p.energy, stroke=INK, w=3)
        return s
    if kind == "hole":
        # a little black hole: an accretion ring and a swallowed core
        s = ""
        for i in range(8):
            a0 = i * 45 + k * 22
            s += poly(band(64, 64, 30, 56, a0, a0 + 26), fill=p.main if i % 2 else p.energy, stroke=INK, w=3)
        s += poly(band(64, 64, 26, 32, 0, 359.9, 24), fill=p.energy_hi)
        s += poly(ngon(64, 64, 26, 16, 0), fill=INK)
        s += poly(ngon(64, 64, 18, 16, 0), fill="#05030A")
        return s
    if kind == "bubble":
        # time bubble: a ring of clock ticks, slowly sweeping hand
        s = poly(ngon(64, 64, 58, 24, 0), fill=p.energy, op=0.14)
        s += poly(ngon(64, 64, 58, 24, 0), stroke=INK, w=6)
        s += poly(ngon(64, 64, 58, 24, 0), stroke=p.energy, w=3)
        for i in range(12):
            s += line([P(64, 64, 48, i * 30), P(64, 64, 56 if i % 3 == 0 else 52, i * 30)],
                      p.energy_hi if i % 3 == 0 else p.energy, 3, cap="butt")
        a = k * 30
        s += line([(64, 64), P(64, 64, 40, a)], p.main, 4, op=0.9)
        return s
    if kind == "decoy":
        # the flare: a burning star sputtering
        s = poly(burst(rng, 64, 64, 34 + (k % 2) * 6, 16, 8, k * 12), fill=p.main, stroke=INK, w=4)
        s += light(star(64, 64, 20 + (k % 2) * 4, 8, 4, k * 22), p.energy, p.energy_hi)
        s += speed_lines(rng, 64, 64, 36, 58, 6, p.energy_hi, 2.4)
        return s
    if kind == "shell":
        # a hex shell dome around the hull
        s = ""
        for i, (r, a) in enumerate([(0, 0)] + [(30, j * 60 + 30) for j in range(6)]):
            x, y = P(64, 64, r, a)
            s += poly(hexagon(x, y, 16), fill=p.energy, op=0.25 + 0.1 * ((i + k) % 2))
            s += poly(hexagon(x, y, 16), stroke=p.energy_hi if (i + k) % 3 == 0 else p.energy, w=2.6)
        s += poly(hexagon(64, 64, 56, 0), stroke=INK, w=6) + poly(hexagon(64, 64, 56, 0), stroke=p.main, w=3)
        return s
    if kind == "emp":
        # crackle round a stunned enemy
        s = ""
        for i in range(3):
            a0 = rng.uniform(0, 360)
            p0 = P(64, 64, 34, a0)
            p1 = P(64, 64, 34, a0 + 70)
            s += inked_line(jag(rng, p0[0], p0[1], p1[0], p1[1], 5, 6), p.energy, 3, WHITE, ink=3)
        s += poly(ngon(64, 64, 44, 12, k * 15), stroke=p.energy, w=2.4, op=0.7)
        return s
    # the rest only burst; a quiet ring keeps the loop drawable
    return poly(ngon(64, 64, 48 + (k % 2) * 4, 12, k * 15), stroke=p.energy, w=3, op=0.6)


def power_burst(doc, p, k):
    kind = POWERS[p.ship]
    rng = p.rng(4, 0)
    e = (k + 1) / 4.0
    if k == 0 and kind not in ("dash", "battery", "mend", "shower", "capacitor"):
        return poly(star(64, 64, 30, 10, 6, 0), fill=PURE, stroke=W.KANEDA, w=4)
    if kind in ("pulse", "shell"):
        # a hex shield popping outward
        r = 22 + 36 * ease(e)
        s = poly(hexagon(64, 64, r, 0), fill=p.energy, op=0.25 * (1 - e) + 0.1)
        s += poly(hexagon(64, 64, r, 0), stroke=INK, w=6) + poly(hexagon(64, 64, r, 0), stroke=p.energy_hi, w=3)
        s += poly(hexagon(64, 64, r * 0.7, 30), stroke=p.energy, w=2.4, op=1 - e)
        return s
    if kind in ("emp", "clap"):
        r = 20 + 40 * ease(e)
        s = poly(band(64, 64, r - 6 + 3 * e, r, 0, 359.9, 30), fill=p.energy, stroke=INK, w=3)
        for i in range(6 if kind == "emp" else 8):
            a0 = i * (60 if kind == "emp" else 45) + rng.uniform(-10, 10)
            p0 = P(64, 64, r * 0.5, a0)
            p1 = P(64, 64, r + 6, a0 + rng.uniform(-12, 12))
            s += inked_line(jag(rng, p0[0], p0[1], p1[0], p1[1], 4, 5), p.energy if kind == "emp" else p.main, 3, WHITE, ink=3)
        return s
    if kind == "nova":
        r = 24 + 36 * ease(e)
        s = poly(burst(rng, 64, 64, r, r * 0.72, 16, k * 6, jitter=0.08), fill=p.main, stroke=INK, w=5, op=1 - 0.25 * k / 3)
        s += poly(burst(rng, 64, 64, r * 0.7, r * 0.5, 12, k * 6), fill=p.energy)
        s += poly(ngon(64, 64, r * 0.36, 12, 0), fill=WHITE)
        return s
    if kind == "capacitor":
        # sparks jumping into the gun
        s = poly(ngon(64, 64, 16 + 30 * (1 - e), 8, 22.5), stroke=p.energy, w=4, op=0.9)
        for i in range(6):
            a0 = i * 60 + k * 20
            p0 = P(64, 64, 56 - 30 * e, a0)
            s += inked_line(jag(rng, p0[0], p0[1], 64, 64, 3, 4), p.energy, 2.6, WHITE, ink=3)
        s += light(star(64, 64, 12 + 6 * e, 4, 4, 45), p.energy, p.energy_hi, rings=1)
        return s
    if kind == "battery":
        # a pause glyph pops up out of a ring
        y = 72 - 22 * ease(e)
        s = poly(ngon(64, 64, 30 + 26 * e, 12, 0), stroke=p.energy, w=4, op=1 - e * 0.7)
        for dx in (-11, 11):
            bar = [(64 + dx - 6, y - 16), (64 + dx + 6, y - 16), (64 + dx + 6, y + 16), (64 + dx - 6, y + 16)]
            s += cel(doc, bar, p.main, p.shade, None, ink=4)
            s += line([(64 + dx - 2, y - 12), (64 + dx - 2, y + 8)], WHITE, 2.4, cap="butt")
        return s
    if kind == "cloak":
        r = 20 + 40 * ease(e)
        s = ""
        for i in range(8):
            a0 = i * 45 + k * 10
            s += poly(band(64, 64, r - 5, r, a0, a0 + 28), fill=p.energy_hi if i % 2 else p.energy, op=1 - e * 0.6)
        return s
    if kind == "dash":
        # afterimage: ghost chevrons and speed lines across the cell
        s = ""
        for i in range(3):
            x = 30 + i * 22 + k * 6
            ch = [(x - 12, 38), (x + 8, 64), (x - 12, 90), (x - 4, 64)]
            s += poly(ch, fill=p.energy if i == 2 else p.main, stroke=INK, w=3, op=1 - (2 - i) * 0.3 - k * 0.15)
        for i in range(6):
            y = 30 + i * 14
            s += line([(4 + k * 8, y), (60 + k * 10, y)], p.energy_hi, 2, op=0.8 - k * 0.15, cap="butt")
        return s
    if kind == "hole":
        # implosion: a ring collapsing inward
        r = 60 - 40 * ease(e)
        s = poly(band(64, 64, r - 5, r, 0, 359.9, 30), fill=p.energy, stroke=INK, w=3)
        s += speed_lines(rng, 64, 64, r, r + 20, 10, p.energy_hi, 2.4)
        s += poly(ngon(64, 64, 10, 12, 0), fill=INK)
        return s
    if kind == "shower":
        # a scatter of star dust glints falling out of a ring
        s = poly(ngon(64, 40, 44, 12, 0, sy=0.3), stroke=p.energy, w=4, op=1 - e * 0.6)
        for i in range(7):
            x = 16 + i * 16
            y = 44 + 50 * ease(e) * (0.6 + 0.4 * ((i * 37) % 10) / 10.0)
            s += poly(star(x, y, 9, 3, 4, 0), fill=W.AMBER, stroke=INK, w=2.4)
        return s
    if kind == "mend":
        # a repair cross and feathers lifting
        r = 16 + 6 * (k % 2)
        cross = [(64 - 6, 64 - r), (64 + 6, 64 - r), (64 + 6, 64 - 6), (64 + r, 64 - 6), (64 + r, 64 + 6),
                 (64 + 6, 64 + 6), (64 + 6, 64 + r), (64 - 6, 64 + r), (64 - 6, 64 + 6), (64 - r, 64 + 6),
                 (64 - r, 64 - 6), (64 - 6, 64 - 6)]
        s = poly(ngon(64, 64, 30 + 26 * e, 12, 0), stroke=p.energy, w=3, op=1 - e * 0.6)
        s += cel(doc, [(x, y - 14 * e) for x, y in cross], p.main, p.shade, WHITE, ink=4)
        for i in range(3):
            x, y = P(64, 64 - 20 * e, 40, -60 + i * 60)
            s += poly([(x, y - 8), (x + 4, y + 6), (x - 4, y + 6)], fill=p.energy_hi, stroke=INK, w=2)
        return s
    if kind == "decoy":
        r = 20 + 40 * ease(e)
        s = poly(burst(rng, 64, 64, r, r * 0.6, 10, k * 9), fill=p.main, stroke=INK, w=4, op=1 - 0.2 * k)
        s += poly(burst(rng, 64, 64, r * 0.6, r * 0.35, 8, 0), fill=p.energy)
        return s
    if kind == "magnet":
        r = 58 - 30 * ease(e)
        return poly(ngon(64, 64, r, 16, 0), stroke=p.energy, w=4, op=0.9)
    if kind == "bubble":
        r = 20 + 38 * ease(e)
        return poly(ngon(64, 64, r, 24, 0), stroke=INK, w=6) + poly(ngon(64, 64, r, 24, 0), stroke=p.energy, w=3)
    return poly(ngon(64, 64, 20 + 36 * e, 12, 0), stroke=p.energy, w=4)


# ------------------------------------------------------------ meter badge ----

METER_INK = 6.0


def icon(doc, p, kind, col, hi):
    """The power's glyph, roughly within r 24 of the badge centre."""
    if kind == "pulse":
        sh = [(64, 40), (84, 48), (82, 70), (64, 88), (46, 70), (44, 48)]
        return cel(doc, sh, col, None, None, ink=4) + poly([(64, 48), (76, 53), (64, 76)], fill=hi)
    if kind == "emp":
        bolt = [(70, 38), (52, 66), (64, 66), (58, 90), (78, 58), (66, 58)]
        return cel(doc, bolt, col, None, None, ink=4)
    if kind == "nova":
        return cel(doc, star(64, 64, 26, 12, 8, 0), col, None, None, ink=4) + poly(ngon(64, 64, 8, 8, 0), fill=hi)
    if kind == "capacitor":
        s = cel(doc, [(50, 44), (78, 44), (78, 86), (50, 86)], col, None, None, ink=4)
        s += poly([(58, 38), (70, 38), (70, 44), (58, 44)], fill=INK)
        s += poly([(67, 50), (56, 68), (64, 68), (60, 80), (72, 62), (64, 62)], fill=hi)
        return s
    if kind == "battery":
        s = cel(doc, [(48, 44), (59, 44), (59, 84), (48, 84)], col, None, None, ink=4)
        s += cel(doc, [(69, 44), (80, 44), (80, 84), (69, 84)], col, None, None, ink=4)
        return s
    if kind == "cloak":
        eye = [(38, 64), (52, 52), (76, 52), (90, 64), (76, 76), (52, 76)]
        return cel(doc, eye, col, None, None, ink=4) + poly(ngon(64, 64, 8, 6, 0), fill=INK) + \
            line([(42, 84), (86, 44)], INK, 5)
    if kind == "magnet":
        u = [(44, 42), (56, 42), (56, 70), (64, 78), (72, 70), (72, 42), (84, 42), (84, 72), (70, 90), (58, 90), (44, 72)]
        s = cel(doc, u, col, None, None, ink=4)
        s += poly([(44, 42), (56, 42), (56, 50), (44, 50)], fill=hi) + poly([(72, 42), (84, 42), (84, 50), (72, 50)], fill=hi)
        return s
    if kind == "clap":
        s = ""
        for i, y in enumerate((50, 64, 78)):
            s += inked_line([(44, y + 10), (64, y - 6), (84, y + 10)], col if i else hi, 5, ink=4)
        return s
    if kind == "decoy":
        fl = [(64, 36), (78, 60), (74, 82), (64, 90), (54, 82), (50, 60)]
        return cel(doc, fl, col, None, None, ink=4) + poly([(64, 56), (70, 72), (64, 84), (58, 72)], fill=hi)
    if kind == "bubble":
        hg = [(46, 40), (82, 40), (68, 64), (82, 88), (46, 88), (60, 64)]
        return cel(doc, hg, col, None, None, ink=4) + poly([(56, 82), (72, 82), (64, 72)], fill=hi)
    if kind == "dash":
        s = ""
        for x in (46, 64):
            s += cel(doc, [(x, 44), (x + 18, 64), (x, 84), (x + 6, 64)], col if x == 64 else hi, None, None, ink=4)
        return s
    if kind == "hole":
        s = poly(ngon(64, 64, 26, 16, 0), fill=col, stroke=INK, w=4)
        s += poly(band(64, 64, 12, 20, 30, 250), fill=hi)
        s += poly(ngon(64, 64, 10, 12, 0), fill=INK)
        return s
    if kind == "shower":
        s = cel(doc, star(64, 58, 22, 7, 4, 0), col, None, None, ink=4)
        s += poly(star(46, 84, 7, 2.5, 4, 0), fill=hi, stroke=INK, w=2) + poly(star(82, 82, 7, 2.5, 4, 0), fill=hi, stroke=INK, w=2)
        return s
    if kind == "mend":
        r = 22
        cross = [(58, 64 - r), (70, 64 - r), (70, 58), (64 + r, 58), (64 + r, 70), (70, 70), (70, 64 + r),
                 (58, 64 + r), (58, 70), (64 - r, 70), (64 - r, 58), (58, 58)]
        return cel(doc, cross, col, None, None, ink=4)
    if kind == "shell":
        return cel(doc, hexagon(64, 64, 24), col, None, None, ink=4) + poly(hexagon(64, 64, 12), stroke=hi, w=3)
    return ""


def badge_base(doc, p, lit):
    """Hex badge: ink outline, night plate, a rim of six segments, `lit` of
    which (0..1) glow in the power's colour."""
    s = poly(hexagon(64, 64, 58, 0), fill=INK, stroke=INK, w=METER_INK)
    s += poly(hexagon(64, 64, 52, 0), fill=NIGHT)
    segs = 6
    f = lit * segs
    for i in range(segs):
        a0 = i * 60 + 4 - 30
        part = clamp01(f - i)
        s += poly(band(64, 64, 44, 54, a0, a0 + 52), fill=NIGHT_HI)
        if part > 0:
            s += poly(band(64, 64, 44, 54, a0, a0 + 52 * part), fill=p.energy if part >= 1 else p.main)
    return s


def meter_fill(doc, p, i):
    c = i / 9.0
    s = badge_base(doc, p, c)
    kind = POWERS[p.ship]
    # the icon: dim gun-metal, lit from the bottom up as it fills
    s += icon(doc, p, kind, GUN_HI, GUN)
    if c > 0:
        cid = doc.uid("m")
        top = 92 - 56 * c
        doc.defs.append('<clipPath id="%s"><polygon points="%s"/></clipPath>' % (cid, pts([(0, top), (128, top), (128, 128), (0, 128)])))
        s += g(icon(doc, p, kind, p.main if c < 1 else p.energy, WHITE), clip=cid)
    return s


def meter_ready(doc, p, k):
    kind = POWERS[p.ship]
    s = ""
    # the bloom behind it: hard-edged steps, a light
    s += poly(hexagon(64, 64, 62 + (k % 2) * 2, 0), fill=p.energy, op=0.35)
    s += badge_base(doc, p, 1.0)
    s += icon(doc, p, kind, p.energy if k % 2 else WHITE, p.main)
    if k % 2 == 0:
        s += poly(hexagon(64, 64, 60, 0), stroke=WHITE, w=3)
    inner = g(s, tf="translate(64 64) scale(%s) translate(-64 -64)" % ("1.06" if k == 0 else "1"))
    return inner


def meter_flash(doc, p, k):
    return poly(star(64, 64, 62, 24, 6, 0), fill=PURE, stroke=W.KANEDA, w=5) + \
        icon(doc, p, POWERS[p.ship], p.energy, WHITE)


def meter_icon(doc, p, k):
    return icon(doc, p, POWERS[p.ship], p.energy, WHITE)


# ------------------------------------------------------------------- build ----

def render(cells, cell, rows, out_png, tmp):
    atlas = Image.new("RGBA", (COLS * cell, rows * cell), (0, 0, 0, 0))
    jobs = []
    for name, row, col, text, src in cells:
        sp = os.path.join(src, name + ".svg")
        with open(sp, "w") as fh:
            fh.write(text)
        pp = os.path.join(tmp, "%d_%s.png" % (row, name))
        jobs.append((subprocess.Popen(["resvg", "-w", str(cell), "-h", str(cell), sp, pp]), name, row, col, pp))
    images = {}
    for proc, name, row, col, pp in jobs:
        if proc.wait() != 0:
            raise SystemExit("resvg failed on %s" % name)
        im = Image.open(pp).convert("RGBA")
        ImageDraw.Draw(im).rectangle([0, 0, cell - 1, cell - 1], outline=(0, 0, 0, 0), width=2 if cell >= 128 else 1)
        atlas.paste(im, (col * cell, row * cell))
        images[(row, col)] = im
    atlas.save(out_png, optimize=True)
    return images


def build(preview_dir=None):
    tmp = tempfile.mkdtemp()
    try:
        attack_cells, meter_cells = [], []
        for ship in range(1, ROWS + 1):
            p = Pal(ship)
            key = W.ROSTER[ship].key
            src = os.path.join(W.ART, key, "src~", "fx")
            os.makedirs(src, exist_ok=True)
            for old in os.listdir(src):
                if old.endswith(".svg"):
                    os.remove(os.path.join(src, old))
            row = ship - 1
            for k in range(4):
                attack_cells.append(("attack_loop_%d" % k, row, k, frame(lambda d, kk: attack_loop(d, p, kk), k, hold=2), src))
                attack_cells.append(("attack_burst_%d" % k, row, 4 + k, frame(lambda d, kk: attack_burst(d, p, kk), k, hold=[1, 1, 2, 3][k]), src))
                attack_cells.append(("power_loop_%d" % k, row, 8 + k, frame(lambda d, kk: power_loop(d, p, kk), k, hold=[3, 2, 3, 2][k]), src))
                attack_cells.append(("power_burst_%d" % k, row, 12 + k, frame(lambda d, kk: power_burst(d, p, kk), k, hold=[1, 2, 2, 3][k]), src))
            for i in range(10):
                meter_cells.append(("meter_%d" % i, row, i, frame(lambda d, ii: meter_fill(d, p, ii), i), src))
            for k in range(4):
                meter_cells.append(("meter_ready_%d" % k, row, 10 + k, frame(lambda d, kk: meter_ready(d, p, kk), k, hold=[4, 2, 2, 2][k]), src))
            meter_cells.append(("meter_flash", row, 14, frame(lambda d, kk: meter_flash(d, p, kk), 0, hold=3), src))
            meter_cells.append(("meter_icon", row, 15, frame(lambda d, kk: meter_icon(d, p, kk), 0), src))
        attack = render(attack_cells, CELL, ROWS, os.path.join(W.OUT, "AttackFx.png"), tmp)
        meter = render(meter_cells, METER_CELL, ROWS, os.path.join(W.OUT, "SecretMeter.png"), tmp)
        print("built AttackFx, SecretMeter")
        if preview_dir:
            preview(attack, meter, preview_dir)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def preview(attack, meter, out_dir):
    """One strip per ship: attack loop + burst, power loop + burst, then the
    meter filling, ready, flash -- on the indigo night."""
    os.makedirs(out_dir, exist_ok=True)
    from PIL import ImageFont
    for ship in range(1, ROWS + 1):
        row = ship - 1
        W_ = 16 * CELL
        H = CELL + 30 + 76
        bg = Image.new("RGBA", (W_, H), W.rgb(W.NIGHT_1) + (255,))
        d = ImageDraw.Draw(bg)
        labels = ["atk loop"] * 4 + ["atk burst"] * 4 + ["pwr loop"] * 4 + ["pwr burst"] * 4
        for col in range(16):
            bg.alpha_composite(attack[(row, col)], (col * CELL, 18))
            d.text((col * CELL + 4, 2), "%s %d" % (labels[col], col % 4), fill=(200, 200, 255, 255))
        y = CELL + 26
        for col in range(16):
            im = meter[(row, col)]
            bg.alpha_composite(im, (col * CELL + 32, y))
        d.text((4, y + 66), "meter 0-9 | ready 0-3 | flash | icon      %s  attack=%s power=%s"
               % (W.ROSTER[ship].key, ATTACKS[ship], POWERS[ship]), fill=(255, 220, 160, 255))
        bg.convert("RGB").save(os.path.join(out_dir, "fx_%02d_%s.png" % (ship, W.ROSTER[ship].key)))
