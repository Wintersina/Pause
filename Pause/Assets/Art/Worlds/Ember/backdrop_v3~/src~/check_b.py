"""Validate packed Ember run B atlases and Unity rects."""

import colorsys
import json
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
EXPECTED = {"landmarks": 16, "pipes": 16, "fires": 16, "weather": 17, "sites": 12}
manifest = json.loads((ROOT / "manifest.json").read_text())
manifest_names = {(entry["atlas"], entry["name"])
                  for entry in manifest["run_b"]["pieces"]}
assert len(manifest_names) == sum(EXPECTED.values())

for family, expected in EXPECTED.items():
    atlas = Image.open(ROOT / f"{family}.png")
    assert atlas.size == (1024, 1024) and atlas.mode == "RGBA"
    pixels = np.asarray(atlas)
    rects = json.loads((ROOT / f"{family}.json").read_text())["sprites"]
    assert len(rects) == expected
    seen = np.zeros((1024, 1024), dtype=bool)
    min_margin = 256
    for rect in rects:
        x, y, w, h = rect["x"], 1024 - rect["y"] - rect["h"], rect["w"], rect["h"]
        assert 0 <= x < x + w <= 1024 and 0 <= y < y + h <= 1024, rect
        assert not seen[y:y+h, x:x+w].any(), rect
        seen[y:y+h, x:x+w] = True
        alpha = pixels[y:y+h, x:x+w, 3]
        ys, xs = np.where(alpha > 0)
        assert len(xs), rect
        margin = min(int(xs.min()), int(ys.min()), w - 1 - int(xs.max()), h - 1 - int(ys.max()))
        assert margin >= 14, (rect, margin)
        min_margin = min(min_margin, margin)
        assert (f"{family}.png", rect["n"]) in manifest_names
    assert not (pixels[~seen, 3] > 0).any()
    if family == "weather":
        assert pixels[:, :, 3].max() <= 80
        p90 = None
    else:
        opaque = pixels[:, :, 3] == 255
        assert opaque.any()
        p90 = float(np.percentile(pixels[opaque, :3].max(axis=1), 90) / 255)
        assert p90 <= .55, (family, p90)
    visible = pixels[pixels[:, :, 3] > 0, :3]
    for rgb in np.unique(visible, axis=0):
        h, s, v = colorsys.rgb_to_hsv(*(float(n) / 255 for n in rgb))
        if s > .5 and v > .2:
            distance_from_player_red = abs((h * 360 - 354 + 180) % 360 - 180)
            assert distance_from_player_red >= 22, (family, rgb, distance_from_player_red)
    print(f"{family}: {expected} sprites, min margin {min_margin}px, "
          f"p90={p90 if p90 is not None else 'n/a'}, alpha max {pixels[:, :, 3].max()}")

preview = Image.open(ROOT / "preview_b.png")
assert preview.size == (5430, 2510)
print("Atlas, Unity rect, margin, brightness, hue, alpha and preview checks passed.")
