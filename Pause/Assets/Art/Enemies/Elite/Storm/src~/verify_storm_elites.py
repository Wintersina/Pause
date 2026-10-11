"""Measure the Storm elite flight/death art contract and print its audit table."""

from __future__ import annotations

from pathlib import Path
import colorsys
import json

import numpy as np
from PIL import Image

from build_storm_elites import SHIPS


ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src~" / "verification.json"
SIZE = 192


def measure_cell(im: Image.Image) -> dict:
    a = np.asarray(im.convert("RGBA"))
    mask = a[:, :, 3] >= 24
    yy, xx = np.where(mask)
    if len(xx) == 0:
        return {"empty": True}
    x0, x1 = int(xx.min()), int(xx.max()) + 1
    y0, y1 = int(yy.min()), int(yy.max()) + 1
    rgb = a[mask, :3]
    # Only visibly chromatic pixels count toward the player's red band.
    red = 0
    for r, g, b in rgb:
        h, s, _ = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        deg = h * 360
        red += bool(s > .20 and (deg >= 333 or deg <= 17))
    border_cut = bool(mask[:16].any() or mask[-16:].any() or mask[:, :16].any() or mask[:, -16:].any())
    return {
        "bbox": [x0, y0, x1, y1],
        "size": [x1-x0, y1-y0],
        "centroid": [round(float(xx.mean()), 3), round(float(yy.mean()), 3)],
        "min_margin": min(x0, y0, SIZE-x1, SIZE-y1),
        "colors": int(len(np.unique(rgb, axis=0))),
        "red_band_share_pct": round(red / len(rgb) * 100, 4),
        "border_cut": border_cut,
        "alpha_levels": int(len(np.unique(a[:, :, 3]))),
    }


def main() -> None:
    report = {}
    print("ship                  hover bbox          L/R size         centroid Δ L/R  min margin  colors  red %   cuts")
    print("-" * 117)
    for ship in SHIPS:
        key = "storm_elite_" + ship.name
        path = ROOT / f"{key}.png"
        im = Image.open(path)
        assert im.size == (1344, 192), (key, im.size)
        assert im.mode == "RGBA", (key, im.mode)
        cells = [measure_cell(im.crop((i*SIZE, 0, (i+1)*SIZE, SIZE))) for i in range(7)]
        assert all(not c.get("empty") for c in cells), key
        hover, left, right = cells[3:6]
        distances = [round(sum((bank["centroid"][j]-hover["centroid"][j])**2 for j in (0, 1))**.5, 3) for bank in (left, right)]
        margin = min(c["min_margin"] for c in cells)
        cuts = sum(c["border_cut"] for c in cells)
        max_red = max(c["red_band_share_pct"] for c in cells)
        min_colors = min(c["colors"] for c in cells)
        assert margin >= 16, (key, "margin", margin)
        assert max(distances) <= 4, (key, "centroid", distances)
        assert cuts == 0, (key, "border cuts", cuts)
        assert min_colors >= 1000, (key, "color detail", min_colors)
        assert max_red <= .1, (key, "red", max_red)
        for bank in (left, right):
            assert abs(bank["size"][0]-hover["size"][0]) <= 8, (key, "bank width")
            assert abs(bank["size"][1]-hover["size"][1]) <= 8, (key, "bank height")
        death = Image.open(ROOT / f"{key}_death.png")
        assert death.size == (576, 192) and death.mode == "RGBA", (key, "death")
        death_cells = [measure_cell(death.crop((i*SIZE, 0, (i+1)*SIZE, SIZE))) for i in range(3)]
        assert all(not c.get("empty") for c in death_cells), (key, "death empties")
        assert all(not c["border_cut"] and c["red_band_share_pct"] <= .1 for c in death_cells), (key, "death edge/color")
        assert hover["alpha_levels"] > 2, (key, "no true alpha")
        print(f"{ship.name:22} {str(hover['bbox']):19} {left['size'][0]:3}×{left['size'][1]:3} / {right['size'][0]:3}×{right['size'][1]:3}   {distances[0]:4.2f}/{distances[1]:4.2f} px     {margin:3}       {hover['colors']:4}   {max_red:5.3f}    {cuts}")
        report[ship.name] = {"strip_size": list(im.size), "mode": im.mode, "cells": cells, "hover_bank_centroid_delta": distances, "min_margin_all_cells": margin, "max_red_band_share_pct": max_red, "death_size": list(death.size)}
    OUT.write_text(json.dumps(report, indent=2) + "\n")
    print("PASS: 5 strips, 35 cells, 5 death strips; all geometry, alpha, detail and red-band checks")


if __name__ == "__main__":
    main()
