#!/usr/bin/env python3
"""Merges a restyled hull's measured rows (build_restyle.py -> <Key>/src~/data.json) into the ship's
own data file, ShipArt/<Key>/<Key>_data.json: sprite rect + ppu, nozzles, damage emitters, the
flame strips and the painted flag. Colours, skin names and every gameplay row are left untouched.
No shared file is edited.

    python3 patch_restyle.py NeonComet
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import shiplib  # noqa: E402

KIND = {"sparks": "Sparks", "arc": "Arc", "smoke": "Smoke", "flame": "Flame", "leak": "Leak", "smolder": "Smolder"}

key = shiplib.resolve(sys.argv[1])
data = json.load(open(os.path.join(shiplib.src_dir(key), "data.json")))
x, y, w, h = data["rect"]
current = shiplib.load_data(key)
exhaust = dict(current.get("exhaust", {}))
if data.get("strips"):
    exhaust["strips"] = data["strips"]
shiplib.update_data(
    key,
    painted=True,
    rect=dict(x=x, y=y, w=w, h=h, ppu=round(data["ppu"], 4)),
    nozzles=[dict(x=round(nx, 1), y=round(ny, 1), scale=k) for nx, ny, k in data["nozzles"]],
    emitters=[dict(state=st, kind=KIND[k], u=round(u, 1), v=round(v, 1)) for st, k, u, v in data["emitters"]],
    exhaust=exhaust,
)
print("wrote", shiplib.data_path(key))
