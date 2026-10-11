#!/usr/bin/env python3
"""Build the game's outposts.png: the two approved dark moons (city_02 the half-built scaffolded sphere,
city_03 the cratered moon with a domed outpost) from Codex's landmarks/cities.png, as two square cells
with the disc 78% of the cell and centred (the BackdropPlanet shader turns them).
The spired city planets and rings (backdrop_v4~/cityparts) were NOT approved and are not installed.
Usage: repack_cities.py <backdrop_v4~> <dst dir>"""
import json, sys
from PIL import Image

CELL, DISC = 320, 0.78   # modest resolution: outposts only appear far away and small (SpaceDirector.OutpostMaxTier)

def main(v4, dst):
    old = Image.open(f"{v4}/landmarks/cities.png").convert("RGBA")
    oj = {s["n"]: s for s in json.load(open(f"{v4}/landmarks/cities.json"))["sprites"]}
    H = old.height
    sheet = Image.new("RGBA", (CELL * 2, CELL), (0, 0, 0, 0))
    meta = []
    for i, src in enumerate(("city_02", "city_03")):
        s = oj[src]
        c = old.crop((s["x"], H - s["y"] - s["h"], s["x"] + s["w"], H - s["y"]))
        bb = c.split()[3].point(lambda v: 255 if v > 40 else 0).getbbox()
        r = (bb[2] - bb[0]) / 2.0
        cx, cy = (bb[0] + bb[2]) / 2.0, bb[3] - r      # disc fitted from the sprite's extent
        sc = CELL * DISC / (2 * r)
        c = c.resize((round(c.width * sc), round(c.height * sc)), Image.LANCZOS)
        cell = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        cell.alpha_composite(c, (round(CELL / 2 - cx * sc), round(CELL / 2 - cy * sc)))
        sheet.alpha_composite(cell, (i * CELL, 0))
        meta.append({"n": f"outpost_0{i}", "x": i * CELL, "y": 0, "w": CELL, "h": CELL})
    sheet.save(f"{dst}/outposts.png", optimize=True)
    json.dump({"pixelScale": 2 * CELL / 1024.0 * 0 + round(2 * 0.3125 * 1.0, 4), "sheetW": CELL * 2, "sheetH": CELL, "sprites": meta},
              open(f"{dst}/outposts.json", "w"), indent=1)
    print("outposts", sheet.size)

main(sys.argv[1], sys.argv[2])
