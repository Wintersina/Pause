#!/usr/bin/env python3
"""Codex image inputs for the two Frost elites: the CURRENT (192 px) art, cell 0 (engines off, no flames) from the untouched Codex strip,
keyed onto flat #00FF40 and enlarged 4x (Lanczos) on a 1024 canvas, so Codex repaints the same design at full detail.
  python3 prep_inputs.py"""
from pathlib import Path
from PIL import Image
HERE = Path(__file__).resolve().parent
ORIG = HERE / "old"
for name in ("frost_elite_cryo_siren", "frost_elite_rimebreaker"):
    strip = Image.open(ORIG / f"{name}.png").convert("RGBA")
    cell = strip.crop((0, 0, 192, 192))
    cell = cell.crop(cell.getbbox())
    s = 4
    cell = cell.resize((cell.width * s, cell.height * s), Image.Resampling.LANCZOS)
    bg = Image.new("RGBA", (1024, 1024), (0, 255, 64, 255))
    bg.alpha_composite(cell, ((1024 - cell.width) // 2, (1024 - cell.height) // 2))
    out = HERE / "inputs" / f"{name.replace('frost_elite_', '')}_current.png"
    bg.convert("RGB").save(out)
    print(out, cell.size)
