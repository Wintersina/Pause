#!/usr/bin/env python3
"""Writes Paragon's measured rows (src~/data.json, from build_paragon.py) into its data file,
ShipArt/Paragon/Paragon_data.json: rect + ppu, nozzles, damage emitters and skins. The exhaust
style row and the flame strips are written by build_paragon.py (shiplib.write_flame_sheet).

    python3 patch_cs.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "src~"))
import shiplib  # noqa: E402

KEY = "Paragon"
data = json.load(open(os.path.join(HERE, "data.json")))
HUE = ["#2E9BE6", "#174C8C", "#8FD3FF"]   # Paragon's identity (neon blue); red is its armour
NOTE = ("1) left wing-pod panel blown, wiring sparking; scorch on the inner pod; 2) right inner pod panel open, "
        "right wing-tip pod torn and burning, hull and canopy cracked")

x, y, w, h = data["rect"]
skin = lambda name, kind, hue, shadow, high: dict(name=name, kind=kind, hue=hue, shadow=shadow, highlight=high,
                                                 pattern="", accent="", accentShadow="", accentHighlight="")
skins = [skin("Stock", "Stock", *HUE)] + [skin(s["name"], "Palette", s["hue"], s["shadow"], s["highlight"]) for s in data["skins"]]
shiplib.update_data(
    KEY, painted=True,
    rect=dict(x=x, y=y, w=w, h=h, ppu=round(data["ppu"], 4)), hue=HUE,
    nozzles=[dict(x=round(nx, 1), y=round(ny, 1), scale=k) for nx, ny, k in data["nozzles"]],
    damageNote=NOTE,
    emitters=[dict(state=st, kind=k.capitalize(), u=round(u, 1), v=round(v, 1)) for st, k, u, v in data["emitters"]],
    skins=skins)
print("wrote", shiplib.data_path(KEY))
