"""Independent export checks and 2x2 visual wrap review for Ember backdrop v3."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
MANIFEST = json.loads((ROOT / "manifest.json").read_text())


def main() -> None:
    assert len(MANIFEST["files"]) == 16
    review = Image.new("RGB", (2048, 4096), (20, 14, 20))
    draw = ImageDraw.Draw(review)
    for entry in MANIFEST["files"]:
        path = ROOT / entry["file"]
        im = Image.open(path)
        assert im.mode == "RGBA" and im.size == (512, 1024), path
        a = np.asarray(im)
        mask = a[:, :, 3] > 0
        p90 = float(np.percentile(a[:, :, :3].max(axis=2)[mask] / 255, 90))
        variant = int(entry["variant"][1:])
        low, high = (.32, .42) if variant == 4 else (.40, .50)
        assert low <= p90 <= high, (path, p90)
        assert abs(p90 - entry["p90_hsv_value_opaque"]) < .0002, path
        assert np.array_equal(a[:, 0], a[:, -1]), path
        assert np.array_equal(a[0], a[-1]), path
        hue_sat = np.asarray(im.convert("RGB").convert("HSV"))
        hue = hue_sat[:, :, 0].astype(np.float32) * 360 / 255
        sat = hue_sat[:, :, 1].astype(np.float32) / 255
        forbidden = ((hue < 22) | (hue > 338) | ((hue > 65) & (hue < 270))) & (sat > .08) & mask
        assert not forbidden.any(), (path, int(forbidden.sum()))
        if entry["role"] == "flow":
            assert .005 < mask.mean() < .25, path
            assert not (a[:, :, 3] == 255).any(), path
        else:
            assert mask.all(), path
        if entry["role"] == "mid":
            x = (variant - 1) % 2 * 1024
            y = (variant - 1) // 2 * 2048
            # Actual wrap: each join is between opposite sides of the tile.
            for dy in (0, 1024):
                for dx in (0, 512):
                    review.paste(im.convert("RGB"), (x + dx, y + dy))
            draw.text((x + 8, y + 8), entry["variant"].upper(), fill=(244, 212, 153))
        print(f'{entry["file"]}: RGBA 512x1024, p90={p90:.4f}, edge=0, alpha={mask.mean():.3f}')
    review.save(ROOT / "src~" / "wrap_review.png", optimize=True)
    print("PASS: 16 exports, four 2x2 mid-layer wraps")


if __name__ == "__main__":
    main()
