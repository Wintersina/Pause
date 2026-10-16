#!/usr/bin/env python3
"""Copy the built strips (Resources/Elites/Space) next to Codex's folder + the outline-pass originals, and print the defs' numbers."""
import shutil, json
from pathlib import Path
R = Path(__file__).resolve().parents[3]
for k in "rift_lancer eventide_bastion orbit_reaver singularity_hauler".split():
    n = f"space_elite_{k}.png"
    src = R / "Resources/Elites/Space" / n
    shutil.copy(src, R / "Enemies/Elite/Space" / n)
    shutil.copy(src, R / "Enemies/src~/ship_scale_border~/original" / n)
    d = json.load(open(R / f"Resources/Elites/Defs/space_elite_{k}.json"))
    print(k, {x: d.get(x) for x in ("cellWorldSize", "hullRadius", "noseDeg", "maxBank", "muzzles", "nozzles", "engineColor", "glowScale", "exhaustScale", "brain", "attack")})
