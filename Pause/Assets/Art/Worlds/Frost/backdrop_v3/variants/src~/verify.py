"""Audit the Frost variant exports, including a two-axis 2x2 wrap."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
ORDER = ("sky", "far", "mid", "flow")
manifest = json.loads((ROOT / "manifest.json").read_text())
assert [entry["id"] for entry in manifest["variants"]] == ["v1", "v2", "v3", "v4"]
assert manifest["preview"] == "preview_variants.png"

for variant in ("v2", "v3", "v4"):
    entry = next(item for item in manifest["variants"] if item["id"] == variant)
    assert [tile["layer"] for tile in entry["tiles"]] == list(ORDER)
    contrast = {}
    for tile in entry["tiles"]:
        layer = tile["layer"]
        path = ROOT / tile["file"]
        im = Image.open(path)
        assert im.size == (512, 1024), path
        assert im.mode == "RGBA", path
        a = np.asarray(im)
        value = a[:, :, :3].max(axis=2).astype(np.float32) / 255
        alpha = a[:, :, 3]
        visible = alpha > 0
        opaque = alpha == 255
        assert visible.any() and opaque.any(), path
        p90_visible = float(np.percentile(value[visible], 90))
        p90_opaque = float(np.percentile(value[opaque], 90))
        p95_visible = float(np.percentile(value[visible], 95))
        maximum = float(value[visible].max())
        accent = float((value[visible] > .4).mean())
        assert p90_opaque <= .33 and p90_visible <= .33, (path, p90_opaque, p90_visible)
        assert p95_visible <= .35 and maximum <= .60 and accent <= .03, path
        assert len(np.unique(a[:, :, :3].reshape(-1, 3), axis=0)) <= tile["palette_colors_max"], path
        # This is the join in an actual 2x2 repeat, checked for both axes.
        wrapped = np.tile(a, (2, 2, 1))
        assert np.array_equal(wrapped[:, 511], wrapped[:, 512]), path
        assert np.array_equal(wrapped[1023], wrapped[1024]), path
        if layer == "sky":
            assert np.all(alpha == 255), path
        if layer == "flow":
            assert .35 < float((alpha == 0).mean()) < .95, path
            assert len(np.unique(alpha)) >= 4, path
        contrast[layer] = float(value[visible].std())
        print(f"{variant}/{layer}: {im.size} {im.mode}, "
              f"p90 opaque={p90_opaque:.3f}, visible={p90_visible:.3f}, "
              f"p95={p95_visible:.3f}, max={maximum:.3f}, "
              f"accents={accent:.2%}, alpha0={(alpha == 0).mean():.1%}, "
              "2-axis wrap=exact")
    assert contrast["sky"] < contrast["far"] < contrast["mid"], (variant, contrast)

preview = Image.open(ROOT / "preview_variants.png")
assert preview.size == (4386, 6776), preview.size
print("PASS: 12 tiles; progressive sky < far < mid contrast; four-column preview")
