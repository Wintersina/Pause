#!/usr/bin/env python3
"""Death strips (3 x 384 px: flash, rupture, dispersal) for the six Ember elites, rebuilt from each hull's flight cell 3 with the HD recipe of
Art/Enemies/Elite/src~/build_death_strips_hd.py in Ember's cue colours (white-gold core, orange slag, no pink outside Space).
Writes Resources/Elites/Ember/<key>_death.png and the copy next to Codex's strips (Enemies/Elite/Ember).
  python3 death.py"""
import sys
from pathlib import Path
from PIL import Image
R = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(R / "Enemies/Elite/src~"))
import build_death_strips_hd as D
D.PINK = (255, 214, 140)                      # the core accent: Ember white-gold, never pink outside Space
D.WORLDS["ember"] = ("Ember", (255, 220, 120), (255, 140, 40), [(255, 255, 255), (255, 150, 50), (255, 196, 90)], "slag")
S = 384
for k in "sunstoke coalrunner brass_vulture kilnback ash_wraith cauterizer".split():
    key = f"ember_elite_{k}"
    base = Image.open(R / f"Resources/Elites/Ember/{key}.png").convert("RGBA").crop((3 * S, 0, 4 * S, S))
    strip = D.build_one(key, base, "ember", S)
    strip.save(R / f"Resources/Elites/Ember/{key}_death.png")
    strip.save(R / f"Enemies/Elite/Ember/{key}_death.png")
    print(key, strip.size)
