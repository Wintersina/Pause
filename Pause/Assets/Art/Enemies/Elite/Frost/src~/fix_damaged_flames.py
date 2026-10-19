#!/usr/bin/env python3
"""Make every engine of Frost's damaged burn loops animate (ElitePolishTest.DamagedLoops).

The damaged strips (the five cells after the main strip, loop drawings 1..5) had faint or no flames under some nozzles
(the left engine of Floe Harrower / Glacier Tender / Whiteout Sentinel, both engines of Cryo Siren). For each nozzle in the
elite's def JSON this clears the wispy sub-opaque pixels under the bell and draws a weak ragged cyan flame whose length
changes every drawing. Hull pixels (fully opaque) are never touched. Deterministic; re-runnable (idempotent).
Run from the repo root: python3 Pause/Assets/Art/Enemies/Elite/Frost/src~/fix_damaged_flames.py
"""
import json, random
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[0].parents[6]
DEFS = ROOT / "Pause/Assets/Art/Resources/Elites/Defs"
TARGETS = [ROOT / "Pause/Assets/Art/Resources/Elites/Frost", ROOT / "Pause/Assets/Art/Enemies/Elite/Frost"]
CELL = 384
FLAME = ((25, 145, 235), (90, 222, 255), (224, 255, 255))
LENGTHS = ((24, 16, 27, 19, 22), (17, 26, 20, 28, 15), (22, 28, 17, 24, 19), (19, 25, 28, 16, 23))   # per nozzle index (cycled), per drawing


def flame(draw, x, y, length, width, rng):
    outer, mid, core = FLAME
    for row in range(length):
        t = row / max(1, length - 1)
        half = max(2, round(width * max(.34, 1.0 - .72 * t)))
        jitter = rng.choice((-1, 0, 0, 0, 1)) if row > 4 else 0
        if row > length * .6 and rng.random() < .22:
            continue
        color = outer if row < 3 else mid if row < length * .62 else core
        draw.rectangle((x - half + jitter, y + row, x + half + jitter, y + row + 1), fill=(*color, 215 if row < length * .66 else 170))
    for _ in range(1 + rng.randrange(2)):
        yy = y + length + rng.randrange(2, 8)
        if yy < CELL - 3:
            xx = x + rng.randrange(-3, 4)
            draw.rectangle((xx, yy, xx + 1, yy + 1), fill=(*mid, 150))


def fix(path, nozzles):
    strip = Image.open(path).convert("RGBA")
    cells = strip.width // CELL
    for c in range(cells):
        cell = strip.crop((c * CELL, 0, (c + 1) * CELL, CELL))
        px = cell.load()
        layer = Image.new("RGBA", (CELL, CELL)); draw = ImageDraw.Draw(layer)
        for i, n in enumerate(nozzles):
            nx, ny = round(n["x"]), round(n["y"])
            for y in range(ny + 4, CELL):
                for x in range(max(0, nx - 16), min(CELL, nx + 17)):
                    if px[x, y][3] < 240: px[x, y] = (0, 0, 0, 0)
            flame(draw, nx, ny + 3, LENGTHS[i % 4][c % 5], 6 + (i % 2) * 2, random.Random(7300 + c * 53 + i * 17))
        cell.alpha_composite(layer)
        strip.paste(cell, (c * CELL, 0))
    strip.save(path)


for d in sorted(DEFS.glob("frost_elite_*.json")):
    nozzles = json.load(open(d))["nozzles"]
    for t in TARGETS:
        fix(t / (d.stem + "_damaged.png"), nozzles)
