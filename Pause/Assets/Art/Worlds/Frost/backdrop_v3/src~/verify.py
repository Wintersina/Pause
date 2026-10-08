"""Audit exported Frost backdrop tiles and preview."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
manifest = json.loads((ROOT / "manifest.json").read_text())
assert {entry["file"] for entry in manifest["files"]} == {
    "sky.png", "far.png", "mid.png", "flow.png"
}

contrast = {}
for entry in manifest["files"]:
    path = ROOT / entry["file"]
    im = Image.open(path)
    assert im.size == (512, 1024), path
    assert im.mode == "RGBA", path
    assert entry["size"] == [512, 1024], path
    pixels = np.asarray(im)
    alpha = pixels[:, :, 3]
    value = pixels[:, :, :3].max(axis=2).astype(np.float32) / 255
    visible = alpha > 0
    opaque = alpha == 255
    assert visible.any() and opaque.any(), path
    p90 = float(np.percentile(value[visible], 90))
    opaque_p90 = float(np.percentile(value[opaque], 90))
    p95 = float(np.percentile(value[visible], 95))
    maximum = float(value[visible].max())
    accent = float((value[visible] > .4).mean())
    assert p90 <= .33 and opaque_p90 <= .33, (path, p90, opaque_p90)
    assert p95 <= .35 and maximum <= .60 and accent <= .03, path
    assert np.array_equal(pixels[:, 0], pixels[:, -1]), path
    assert np.array_equal(pixels[0], pixels[-1]), path
    contrast[path.stem] = float(value[visible].std())
    print(f"{path.name}: {im.size} {im.mode}, p90={p90:.3f}, "
          f"p95={p95:.3f}, max={maximum:.3f}, accent={accent:.4%}, "
          f"wrap-edge difference=0, alpha0={(alpha == 0).mean():.1%}")

assert contrast["sky"] < contrast["far"] < contrast["mid"], contrast
assert np.asarray(Image.open(ROOT / "flow.png"))[:, :, 3].min() == 0
preview = Image.open(ROOT / "preview_a.png")
assert preview.size == (3380, 2500), preview.size
print("PASS: contrast rises sky < far < mid; flow is partially transparent; preview exists")
