#!/usr/bin/env python3
"""Contact sheet of a ship's installed art: the stock flipbook (27 cells) + every skin's 27 cells, plus the
nozzle points (cyan) and damage emitters (yellow / orange) marked on the idle cells, on a dark backdrop.

    python3 pixel_contact.py <Key> <out.png>
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import shiplib  # noqa: E402

CELL = 256
BG = (14, 20, 36, 255)


def main():
    key = shiplib.resolve(sys.argv[1])
    out = sys.argv[2]
    data = shiplib.load_data(key)
    sheets = [("Stock", shiplib.hull_path(key))] + [(s["name"], shiplib.skin_path(key, s["name"])) for s in data["skins"][1:]]
    sc = .5
    cw = int(CELL * sc)
    W = cw * 9
    H = (cw * 3 + 18) * len(sheets) + 40
    img = Image.new("RGBA", (W, H), BG)
    dr = ImageDraw.Draw(img)
    dr.text((6, 6), "%s  rect %s  (cyan = nozzles, yellow = emitters state 1, orange = state 2)" % (key, data["rect"]), fill=(230, 230, 230, 255))
    y = 26
    r = data["rect"]
    for name, p in sheets:
        im = Image.open(p).convert("RGBA").resize((W, cw * 3), Image.NEAREST)
        dr.text((6, y), name, fill=(230, 230, 230, 255))
        y += 14
        img.alpha_composite(im, (0, y))
        for st in range(3):
            for n in data["nozzles"]:
                for c in range(9):
                    ox, oy = n["offsets"][2 * c], n["offsets"][2 * c + 1]
                    x = (c * CELL + r["x"] + n["x"] + ox) * sc
                    yy = y + (st * CELL + CELL - r["y"] - (n["y"] + oy)) * sc
                    dr.rectangle([x - 1, yy - 1, x + 1, yy + 1], outline=(0, 255, 255, 255))
            for e in data["emitters"]:
                if e["state"] <= st:
                    x, yy = e["u"] * 2 * sc, y + (st * CELL + e["v"] * 2) * sc
                    col = (255, 240, 0, 255) if e["state"] == 1 else (255, 120, 0, 255)
                    dr.rectangle([x - 1, yy - 1, x + 1, yy + 1], outline=col)
        y += cw * 3 + 4
    img.convert("RGB").save(out)
    print("wrote", out)


if __name__ == "__main__":
    main()
