#!/usr/bin/env python3
"""Secret-meter badges for every ship.

Same style and palette as weapons.py (flat cels, thick INK, hard-edged light,
PALETTES row per ship), same 24 fps tick tables. One shared atlas:

  ../../Resources/Weapons/SecretMeter.png   64px cells, 16 x 15, row = ship id - 1
      cols 0-9   badge filling  cols 10-13 ready loop
      col 14     fire flash     col 15 icon only

ShipFxArt.cs slices the same layout. The ship attack and secret-power effects are no longer drawn
here: they are Codex strips (Resources/Weapons/Powers2/<Ship>_<name>.png).

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

# Which power each ship row draws its badge icon for (ShipLoadout.cs is the source of
# truth for the gameplay; keep in step).
POWERS = {1: "pulse", 2: "emp", 3: "nova", 4: "capacitor", 5: "battery", 6: "cloak", 7: "magnet",
          8: "clap", 9: "decoy", 10: "bubble", 11: "dash", 12: "hole", 13: "shower", 14: "mend",
          15: "shell"}


class Pal:
    def __init__(self, ship):
        (self.main, self.shade, self.hi, self.energy, self.energy_hi, self.spot) = PALETTES[ship]
        self.ship = ship

    def rng(self, kind, k):
        return random.Random(self.ship * 7919 + kind * 131 + k)


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
        meter_cells = []
        for ship in range(1, ROWS + 1):
            p = Pal(ship)
            key = W.ROSTER[ship].key
            src = os.path.join(W.ART, key, "src~", "fx")
            os.makedirs(src, exist_ok=True)
            for old in os.listdir(src):
                if old.endswith(".svg"):
                    os.remove(os.path.join(src, old))
            row = ship - 1
            for i in range(10):
                meter_cells.append(("meter_%d" % i, row, i, frame(lambda d, ii: meter_fill(d, p, ii), i), src))
            for k in range(4):
                meter_cells.append(("meter_ready_%d" % k, row, 10 + k, frame(lambda d, kk: meter_ready(d, p, kk), k, hold=[4, 2, 2, 2][k]), src))
            meter_cells.append(("meter_flash", row, 14, frame(lambda d, kk: meter_flash(d, p, kk), 0, hold=3), src))
            meter_cells.append(("meter_icon", row, 15, frame(lambda d, kk: meter_icon(d, p, kk), 0), src))
        meter = render(meter_cells, METER_CELL, ROWS, os.path.join(W.OUT, "SecretMeter.png"), tmp)
        print("built SecretMeter")
        if preview_dir:
            preview(meter, preview_dir)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def preview(meter, out_dir):
    """One strip per ship: the meter filling, ready, flash, icon -- on the indigo night."""
    os.makedirs(out_dir, exist_ok=True)
    for ship in range(1, ROWS + 1):
        row = ship - 1
        W_ = 16 * METER_CELL
        H = METER_CELL + 24
        bg = Image.new("RGBA", (W_, H), W.rgb(W.NIGHT_1) + (255,))
        d = ImageDraw.Draw(bg)
        for col in range(16):
            bg.alpha_composite(meter[(row, col)], (col * METER_CELL, 2))
        d.text((4, METER_CELL + 6), "meter 0-9 | ready 0-3 | flash | icon      %s  power=%s"
               % (W.ROSTER[ship].key, POWERS[ship]), fill=(255, 220, 160, 255))
        bg.convert("RGB").save(os.path.join(out_dir, "fx_%02d_%s.png" % (ship, W.ROSTER[ship].key)))
