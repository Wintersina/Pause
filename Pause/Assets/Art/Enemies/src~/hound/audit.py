"""Validate the shipped seven-cell Steel Hound strip and print a compact report."""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
STRIP = HERE.parents[2] / "Resources/Enemies/space_chaser.png"
CELL = 192


def main() -> None:
    image = Image.open(STRIP)
    assert image.size == (1344, 192), f"unexpected sheet dimensions {image.size}"
    assert image.mode == "RGBA", f"expected RGBA, found {image.mode}"
    cells = [np.asarray(image.crop((i * CELL, 0, (i + 1) * CELL, CELL))).copy()
             for i in range(7)]
    centers = []
    for i, a in enumerate(cells):
        alpha = a[:, :, 3]
        yy, xx = np.where(alpha > 0)
        assert len(xx) > 0, f"cell {i} empty"
        margins = (int(xx.min()), int(yy.min()), int(CELL - 1 - xx.max()),
                   int(CELL - 1 - yy.max()))
        assert min(margins) >= 6, f"cell {i} margin violation: {margins}"
        solid_y, solid_x = np.where(alpha >= 224)
        center = (float(solid_x.mean()), float(solid_y.mean()))
        centers.append(center)
        colors = len(np.unique(a[alpha > 0], axis=0))
        assert colors >= 300, f"cell {i} has only {colors} colors"
        hsv = np.asarray(Image.fromarray(a, "RGBA").convert("RGB").convert("HSV"))
        hue, sat, val = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]
        red = (alpha > 0) & (sat > 128) & (val > 55) & ((hue < 11) | (hue > 244))
        assert not red.any(), f"cell {i} has {int(red.sum())} saturated red pixels"
        print(f"cell {i}: margins={margins}, solid centroid=({center[0]:.2f}, {center[1]:.2f}), "
              f"colors={colors:,}, solid pixels={len(solid_x):,}")
    reference = np.asarray(centers[:4])
    drift = np.max(np.abs(reference - reference[0]), axis=0)
    assert np.max(drift) <= 3, f"idle centroid drift {drift}"
    print(f"idle centroid max drift: dx={drift[0]:.2f}, dy={drift[1]:.2f}")
    for i in range(3):
        a, b = cells[i], cells[i + 1]
        body = (a[:, :, 3] >= 224) | (b[:, :, 3] >= 224)
        different = np.any(a != b, axis=2)
        changed = int(np.count_nonzero(different & body))
        ratio = changed / np.count_nonzero(body)
        assert ratio >= .03, f"idle {i}->{i+1} only {ratio:.1%} changed"
        sil_a = a[:, :, 3] >= 224
        sil_b = b[:, :, 3] >= 224
        intersection = np.count_nonzero(sil_a & sil_b)
        union = np.count_nonzero(sil_a | sil_b)
        print(f"idle {i}->{i+1}: changed body pixels={changed:,} ({ratio:.1%}), "
              f"solid silhouette IoU={intersection / union:.3f}")
    print("PASS: size, RGBA, margins, anchors, color detail, no player red, idle motion")


if __name__ == "__main__":
    main()
