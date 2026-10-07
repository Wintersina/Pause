#!/usr/bin/env python3
"""Small FX sprites for the boss attacks (BossAttackFx.cs).

  python3 build_boss_attack_fx.py [--preview DIR]      (needs Pillow)

Output: Art/Resources/BossAttackFx/boss_attack_fx.png, imported
point-filtered and uncompressed by Editor/Importers/BossAttackFxImporter.cs and sliced
at runtime by BossAttackFx (6 x 4 cells of 48 px; one row per boss, in
BossCatalog order Space, Frost, Verdant, Ember):

  col 0..2  rail spark: the ping a ricochet or an absorbed shot makes on a
            side rail -- a hot point, then a four-way splash, then embers
  col 3..4  muzzle flash: the burst at a body part the instant it fires, and
            the flicker at the root of a live beam
  col 5     charge ring: the tell's glow gathering at the part about to fire

Neon pixel art like the rest of the game's FX: drawn on a 16 x 16 grid and
blown up 3x (hard pixels at any filter), three tones per boss (a white-hot
core, the boss's neon, a dark rim), hard alpha steps and an ordered dither
instead of soft gradients. No player red anywhere.
"""
import math
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
OUT = os.path.join(ASSETS, "Art", "Resources", "BossAttackFx")

GRID = 16
UP = 3
CELL = GRID * UP
COLS = 6

# core, neon, rim
PALETTES = [
    ((0xFF, 0xEE, 0xF6), (0xFF, 0x2E, 0x88), (0x7A, 0x10, 0x48)),   # Space: magenta
    ((0xF0, 0xFF, 0xFF), (0x6E, 0xDC, 0xF5), (0x1E, 0x5A, 0xA0)),   # Frost: ice
    ((0xF6, 0xFF, 0xDC), (0xC8, 0xFF, 0x3C), (0x3C, 0x78, 0x10)),   # Verdant: bile
    ((0xFF, 0xF4, 0xC8), (0xFF, 0x8C, 0x28), (0x96, 0x22, 0x6E)),   # Ember: magma (orange, plum rim)
]

BAYER4 = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def lit(level, x, y):
    return (BAYER4[y % 4][x % 4] + 0.5) / 16.0 < level


def grid():
    return [[None] * GRID for _ in range(GRID)]


def put(g, x, y, c):
    if 0 <= x < GRID and 0 <= y < GRID:
        g[y][x] = c


def spark(pal, step):
    core, neon, rim = pal
    g = grid()
    c = 7.5
    if step == 0:
        for y in range(GRID):
            for x in range(GRID):
                d = math.hypot(x - c, y - c)
                if d < 1.6:
                    put(g, x, y, core)
                elif d < 3.0:
                    put(g, x, y, neon)
                elif d < 4.0 and lit(0.5, x, y):
                    put(g, x, y, rim)
    elif step == 1:
        for y in range(GRID):
            for x in range(GRID):
                dx, dy = abs(x - c), abs(y - c)
                d = math.hypot(x - c, y - c)
                ray = min(dx, dy) < 0.8 and d < 7.2
                diag = abs(dx - dy) < 0.8 and d < 5.0
                if d < 1.2:
                    put(g, x, y, core)
                elif ray:
                    put(g, x, y, core if d < 3.5 else neon)
                elif diag:
                    put(g, x, y, neon if d < 3.5 else rim)
                elif d < 2.6:
                    put(g, x, y, neon)
    else:
        # scattered embers flying out
        pts = [(2, 7), (13, 8), (7, 2), (8, 13), (4, 4), (11, 11), (11, 4), (4, 11), (1, 9), (14, 6)]
        for i, (x, y) in enumerate(pts):
            put(g, x, y, neon if i % 3 else core)
            if i < 4:
                put(g, x + (1 if x < 8 else -1), y, rim)
    return g


def flash(pal, step):
    core, neon, rim = pal
    g = grid()
    c = 7.5
    r_core = 2.4 if step == 0 else 1.8
    r_neon = 4.2 if step == 0 else 3.6
    reach = 7.4 if step == 0 else 6.0
    for y in range(GRID):
        for x in range(GRID):
            dx, dy = abs(x - c), abs(y - c)
            d = math.hypot(x - c, y - c)
            if d < r_core:
                put(g, x, y, core)
            elif d < r_neon:
                put(g, x, y, neon if lit(0.85, x, y) else core)
            elif min(dx, dy) < 0.8 and d < reach:
                put(g, x, y, neon if d < reach - 1.5 else rim)
            elif d < r_neon + 1.2 and lit(0.4, x, y):
                put(g, x, y, rim)
    return g


def ring(pal):
    core, neon, rim = pal
    g = grid()
    c = 7.5
    for y in range(GRID):
        for x in range(GRID):
            d = math.hypot(x - c, y - c)
            if 5.6 < d < 7.0:
                put(g, x, y, neon if lit(0.75, x, y) else rim)
            elif 4.4 < d <= 5.6:
                put(g, x, y, core if lit(0.35, x, y) else None)
            elif d < 1.5:
                put(g, x, y, core)
    return g


def blit(img, g, col, row):
    px = img.load()
    for y in range(GRID):
        for x in range(GRID):
            c = g[y][x]
            if c is None:
                continue
            for j in range(UP):
                for i in range(UP):
                    px[col * CELL + x * UP + i, row * CELL + y * UP + j] = c + (255,)


def build():
    img = Image.new("RGBA", (COLS * CELL, len(PALETTES) * CELL), (0, 0, 0, 0))
    for row, pal in enumerate(PALETTES):
        for s in range(3):
            blit(img, spark(pal, s), s, row)
        for s in range(2):
            blit(img, flash(pal, s), 3 + s, row)
        blit(img, ring(pal), 5, row)
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    img = build()
    path = os.path.join(OUT, "boss_attack_fx.png")
    img.save(path)
    print("wrote", path)
    if "--preview" in sys.argv:
        d = sys.argv[sys.argv.index("--preview") + 1]
        os.makedirs(d, exist_ok=True)
        bg = Image.new("RGBA", img.size, (16, 16, 26, 255))
        bg.alpha_composite(img)
        bg.resize((img.width * 3, img.height * 3), Image.NEAREST).save(os.path.join(d, "bossatk-fx-sheet.png"))


if __name__ == "__main__":
    main()
