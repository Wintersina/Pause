"""Validate delivered Space planet atlases and write validation.json."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
REPO = HERE.parents[6]
OLD = REPO / "Pause/Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/anim.json"


def red_fraction(crop: Image.Image) -> float:
    rgba = np.asarray(crop.convert("RGBA"))
    hsv = np.asarray(crop.convert("HSV"))
    alpha = rgba[:, :, 3] > 128
    hue = hsv[:, :, 0].astype(np.float32) * (360 / 255)
    saturation = hsv[:, :, 1].astype(np.float32) / 255
    value = hsv[:, :, 2].astype(np.float32) / 255
    distance = np.abs((hue - 354 + 180) % 360 - 180)
    red = (distance < 28) & (saturation > 0.45) & (value > 0.2) & alpha
    return float(red.sum() / max(1, alpha.sum()))


def check(scale: int) -> dict:
    suffix = "" if scale == 2 else "_4096"
    path = OUT / f"anim_hires{suffix}.png"
    sheet = Image.open(path)
    data = json.loads((OUT / f"anim_hires{suffix}.json").read_text())
    old = json.loads(OLD.read_text())["sprites"]
    side = 1024 * scale
    cell = 256 * scale
    assert sheet.size == (side, side) and sheet.mode == "RGBA"
    assert (data["pixelScale"], data["sheetW"], data["sheetH"]) == (scale, side, side)
    assert len(data["sprites"]) == 16
    assert [e["n"] for e in data["sprites"]] == [e["n"] for e in old]
    all_alpha = np.asarray(sheet.getchannel("A"))
    items = []
    for i, (entry, original) in enumerate(zip(data["sprites"], old)):
        width, height = original["w"] * scale, original["h"] * scale
        x = i % 4 * cell + (cell - width) // 2
        y = (3 - i // 4) * cell + (cell - height) // 2
        assert (entry["x"], entry["y"], entry["w"], entry["h"]) == (x, y, width, height)
        top = side - y - height
        crop = sheet.crop((x, top, x + width, top + height))
        alpha = np.asarray(crop.getchannel("A"))
        ys, xs = np.nonzero(alpha > 0)
        assert len(xs) > 0
        bbox = [int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)]
        borders = [bbox[0], bbox[1], width - bbox[2], height - bbox[3]]
        expected_border = round(2.5 * scale)
        assert all(abs(border - expected_border) <= 2 * scale for border in borders)
        # The body must fill the cut to within two old-art pixels on all sides.
        assert abs((bbox[2] - bbox[0]) - (width - 5 * scale)) <= 2 * scale
        assert abs((bbox[3] - bbox[1]) - (height - 5 * scale)) <= 2 * scale
        assert min(x - i % 4 * cell, top - i // 4 * cell) >= 6 * scale
        disc_ratio = (bbox[2] - bbox[0]) / (bbox[3] - bbox[1])
        if i < 12:
            assert 1.02 <= disc_ratio <= 1.07
        fraction = red_fraction(crop)
        assert fraction < 0.04, (entry["n"], fraction)
        items.append({"name": entry["n"], "discBBoxInRect": bbox,
                      "clearBorderPx": borders, "discRatio": round(disc_ratio, 4),
                      "redAdjacentFraction": round(fraction, 5)})
    # No stray alpha, halos, or overlapping neighboring cells.
    occupied = np.zeros((side, side), dtype=np.bool_)
    for entry in data["sprites"]:
        x, y, w, h = (entry[key] for key in ("x", "y", "w", "h"))
        occupied[side - y - h:side - y, x:x + w] = True
    assert not np.any(all_alpha[~occupied])
    return {"sheet": path.name, "size": list(sheet.size), "mode": sheet.mode,
            "spriteCount": len(items), "sprites": items}


def main() -> None:
    runtime = check(2)
    master = check(4)
    large = Image.open(OUT / "anim_hires_4096.png")
    small = Image.open(OUT / "anim_hires.png")
    assert np.array_equal(np.asarray(large.resize((2048, 2048), Image.Resampling.BOX)), np.asarray(small))
    assert Image.open(OUT / "preview_phone.png").size == (1170, 2532)
    assert Image.open(OUT / "preview.png").size == (4096, 2192)
    result = {"passed": True, "runtime": runtime, "master": master,
              "exactBoxDownsample": True, "phonePreview": [1170, 2532]}
    (HERE / "validation.json").write_text(json.dumps(result, indent=2) + "\n")
    print("PASS: 16 centered discs, exact 2x box downsample, JSON and preview geometry, clear borders, red hue check")


if __name__ == "__main__":
    main()
