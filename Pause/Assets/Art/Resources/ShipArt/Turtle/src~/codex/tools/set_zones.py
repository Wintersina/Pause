"""Writes the hand-placed damage zones (ellipses cx, cy, rx, ry in the 256 cell; see diff_view.py) into restyle.json."""
import json
import os

p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "restyle.json")
d = json.load(open(p))
d["damage_zones"] = {
    "s1": [[94, 92, 25, 31], [128, 110, 20, 15], [170, 174, 19, 34], [128, 235, 14, 9]],
    "s2": [[192, 64, 25, 25], [176, 124, 19, 38], [133, 184, 29, 25], [100, 88, 30, 40], [128, 62, 15, 17]],
}
json.dump(d, open(p, "w"), indent=1)
