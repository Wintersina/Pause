"""Check geometry, untouched-cell identity, Frost hues and rime outline."""

import colorsys
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent
LIVE = ROOT.parents[2] / "Resources" / "Enemies"
CHANGED = {
    "frost_rock_rime": list(range(7)),
    "frost_chaser": [2, 3],
    "frost_fighter_1": [3],
    "frost_fighter_2": [3],
    "frost_rock_shard": [1],
    "frost_big": [6],
}


def stats(a):
    yy, xx = np.where(a[:, :, 3] > 16)
    h = a.shape[0]
    return {
        "area": len(xx),
        "centroid": [round(float(xx.mean()), 3), round(float(yy.mean()), 3)],
        "bbox": [int(xx.min()), int(yy.min()), int(xx.max() + 1), int(yy.max() + 1)],
        "margin": int(min(xx.min(), yy.min(), h - 1 - xx.max(), h - 1 - yy.max())),
    }


def edge_darkness(a, depth):
    opaque = a[:, :, 3] > 128
    mask = Image.fromarray((opaque * 255).astype("uint8"), "L")
    eroded = np.asarray(mask.filter(ImageFilter.MinFilter(depth * 2 + 1))) > 128
    prior = (np.asarray(mask.filter(ImageFilter.MinFilter(depth * 2 - 1))) > 128
             if depth > 1 else opaque)
    layer = prior & ~eroded
    dark = a[:, :, :3].max(axis=2) < 65
    return round(float(dark[layer].mean()), 4)


def forbidden_hues(a):
    rgb = a[:, :, :3][a[:, :, 3] > 16]
    colors, counts = np.unique(rgb, axis=0, return_counts=True)
    red = purple = cream = 0
    for c, count in zip(colors, counts):
        h, s, v = colorsys.rgb_to_hsv(*(float(x) / 255 for x in c))
        hue = h * 360
        if (hue < 18 or hue > 345) and s > .5:
            red += int(count)
        if 260 < hue < 330 and s > .2:
            purple += int(count)
        if 30 < hue < 65 and s > .08 and v > .7:
            cream += int(count)
    return {"red": red, "purple": purple, "cream": cream}


report = {}
for name, touched in CHANGED.items():
    old = np.asarray(Image.open(ROOT / "original" / (name + ".png")).convert("RGBA"))
    new = np.asarray(Image.open(LIVE / (name + ".png")).convert("RGBA"))
    assert old.shape == new.shape
    h = old.shape[0]
    assert old.shape[1] == 7 * h
    cells = []
    for i in range(7):
        oa = old[:, i * h:(i + 1) * h]
        na = new[:, i * h:(i + 1) * h]
        same = np.array_equal(oa, na)
        assert same == (i not in touched), (name, i, same)
        item = stats(na)
        item["changed"] = not same
        if i in touched:
            assert item["margin"] >= (8 if name == "frost_big" else 6)
        cells.append(item)
    idles = cells[:4]
    base = idles[0]
    metrics = {
        "max_area_ratio_to_cell0": round(max(c["area"] / base["area"] for c in idles), 4),
        "min_area_ratio_to_cell0": round(min(c["area"] / base["area"] for c in idles), 4),
        "max_centroid_offset_to_cell0": round(max(
            ((c["centroid"][0] - base["centroid"][0]) ** 2 +
             (c["centroid"][1] - base["centroid"][1]) ** 2) ** .5
            for c in idles), 3),
        "modified_cell_margins": {str(i): cells[i]["margin"] for i in touched},
    }
    if name == "frost_chaser":
        assert metrics["max_area_ratio_to_cell0"] <= 1.04
        assert metrics["max_centroid_offset_to_cell0"] <= 3
    if name == "frost_fighter_1":
        assert .98 <= cells[3]["area"] / base["area"] <= 1.04
        assert abs(cells[3]["centroid"][0] - base["centroid"][0]) <= 3
        assert abs(cells[3]["centroid"][1] - base["centroid"][1]) <= 3
    if name == "frost_fighter_2":
        assert 11000 <= cells[3]["area"] <= 11700
    if name == "frost_rock_shard":
        assert cells[1]["centroid"] == cells[0]["centroid"]
        assert cells[1]["area"] == cells[0]["area"]
    if name == "frost_big":
        b = cells[3]
        hit = cells[6]
        metrics["hit_area_ratio_to_cell3"] = round(hit["area"] / b["area"], 4)
        metrics["hit_centroid_offset_to_cell3"] = [
            round(hit["centroid"][0] - b["centroid"][0], 3),
            round(hit["centroid"][1] - b["centroid"][1], 3),
        ]
        assert .93 <= metrics["hit_area_ratio_to_cell3"] <= 1.07
        assert max(abs(v) for v in metrics["hit_centroid_offset_to_cell3"]) <= 1
    if name == "frost_rock_rime":
        metrics["edge_dark_fraction_at_depth_2"] = edge_darkness(new[:, :h], 2)
        metrics["old_edge_dark_fraction_at_depth_2"] = edge_darkness(old[:, :h], 2)
        assert metrics["edge_dark_fraction_at_depth_2"] < .35
        assert metrics["max_centroid_offset_to_cell0"] < 2
        assert .98 <= metrics["min_area_ratio_to_cell0"] <= 1.02
        assert metrics["max_area_ratio_to_cell0"] <= 1.02
    if name in ("frost_rock_rime", "frost_big"):
        check = np.concatenate([new[:, i * h:(i + 1) * h]
                                for i in touched], axis=1)
        metrics["forbidden_hue_pixels"] = forbidden_hues(check)
        assert metrics["forbidden_hue_pixels"] == {"red": 0, "purple": 0, "cream": 0}
    report[name] = {"size": [int(new.shape[1]), int(new.shape[0])],
                    "metrics": metrics, "cells": cells}

assert (ROOT / "preview.png").is_file()
assert (ROOT / "preview.gif").is_file()
(ROOT / "verification.json").write_text(json.dumps(report, indent=2) + "\n")
for name, data in report.items():
    print(name, data["metrics"])
print("PASS: dimensions, untouched cells, margins, idle geometry, hue, outline, previews")
