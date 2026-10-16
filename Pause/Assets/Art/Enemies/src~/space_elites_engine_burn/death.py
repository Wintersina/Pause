#!/usr/bin/env python3
"""Death strips (3 x 384 px: flash, rupture, dispersal) for the four Space elites, from each hull's bare cell 0 (engines off), with the
HD recipe of Art/Enemies/Elite/src~/build_death_strips_hd.py in Space's palette. Writes Resources/Elites/Space/<key>_death.png and the
copy next to Codex's strips."""
import sys
from pathlib import Path
from PIL import Image
R = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(R / "Enemies/Elite/src~"))
import build_death_strips_hd as D
D.WORLDS["space"] = ("Space", (150, 245, 255), (120, 80, 255), [(255, 79, 216), (90, 220, 255), (200, 120, 255)], "plasma")
S = 384
for k in "rift_lancer eventide_bastion orbit_reaver singularity_hauler".split():
    key = f"space_elite_{k}"
    base = Image.open(R / f"Resources/Elites/Space/{key}.png").convert("RGBA").crop((0, 0, S, S))
    strip = D.build_one(key, base, "space", S)
    strip.save(R / f"Resources/Elites/Space/{key}_death.png")
    strip.save(R / f"Enemies/Elite/Space/{key}_death.png")
    print(key, strip.size)
