"""Validate Frost run B atlas geometry, alpha, margins and brightness."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
EXPECTED = {"landmarks": 16, "weather": 16, "sites": 12}


def check(name: str, count: int) -> None:
    im = Image.open(ROOT / f"{name}.png")
    assert im.size == (1024, 1024), (name, im.size)
    assert im.mode == "RGBA", (name, im.mode)
    arr = np.asarray(im)
    rects = json.loads((ROOT / f"{name}.json").read_text())["sprites"]
    assert len(rects) == count, (name, len(rects))
    assert len({r["n"] for r in rects}) == count, name
    occupied = np.zeros((1024, 1024), bool)
    opaque_values = []
    margin_min = 256
    for r in rects:
        x, y, w, h = (r[k] for k in ("x", "y", "w", "h"))
        assert (w, h) == (256, 256)
        assert x >= 0 and y >= 0 and x + w <= 1024 and y + h <= 1024, (name, r)
        top = 1024 - y - h
        assert not occupied[top:top + h, x:x + w].any(), (name, r)
        occupied[top:top + h, x:x + w] = True
        cell = arr[top:top + h, x:x + w]
        mask = cell[:, :, 3] > 0
        assert mask.any(), (name, r["n"])
        yy, xx = np.where(mask)
        margin = min(xx.min(), yy.min(), 255 - xx.max(), 255 - yy.max())
        margin_min = min(margin_min, margin)
        assert margin >= 6, (name, r["n"], margin)
        assert np.all(cell[~mask, :3] == 0), (name, r["n"], "dirty transparent RGB")
        if name != "weather":
            opaque_values.extend(np.max(cell[cell[:, :, 3] == 255, :3], axis=1).tolist())
    if name != "weather":
        p90 = float(np.percentile(np.asarray(opaque_values) / 255, 90))
        assert p90 <= .35, (name, p90)
        print(f"{name}: {count} pieces; min margin {margin_min}px; opaque HSV-V p90 {p90:.3f}")
    else:
        assert arr[:, :, 3].max() <= 104
        assert (arr[:, :, 3] > 0).sum() > 1000
        print(f"{name}: {count} pieces; min margin {margin_min}px; max alpha {arr[:, :, 3].max()}")
    if name == "sites":
        assert not (arr[768:, :, 3] > 0).any(), "unused fourth row must stay transparent"


if __name__ == "__main__":
    for family, expected_count in EXPECTED.items():
        check(family, expected_count)
    manifest = json.loads((ROOT / "manifest.json").read_text())
    assert len(manifest["files"]) == 4
    assert len(manifest["run_b"]["pieces"]) == sum(EXPECTED.values())
    print("manifest: run A retained; run B lists all 44 pieces")
