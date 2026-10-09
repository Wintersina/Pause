"""Measure the shipped rime cutter cells with EnemyRosterTest's detail rule."""

from pathlib import Path
import math

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
ART = HERE.parents[2] / "Resources" / "Enemies"
SIDE = 192


def cells(path, count):
    strip = Image.open(path).convert("RGBA")
    assert strip.size == (count * SIDE, SIDE), (path, strip.size)
    return [np.array(strip.crop((i * SIDE, 0, (i + 1) * SIDE, SIDE)))
            for i in range(count)]


def measure(pixels):
    mask = pixels[:, :, 3] > 128
    yy, xx = np.where(mask)
    assert len(xx), "empty sprite"
    rgb = pixels[:, :, :3]
    boundaries = np.zeros((SIDE, SIDE), dtype=bool)
    boundaries[:, :-1] |= mask[:, :-1] & mask[:, 1:] & \
                           np.any(rgb[:, :-1] != rgb[:, 1:], axis=2)
    boundaries[:-1, :] |= mask[:-1, :] & mask[1:, :] & \
                           np.any(rgb[:-1, :] != rgb[1:, :], axis=2)
    return {
        "tones": len(np.unique(rgb[mask], axis=0)),
        "boundaries": int(boundaries.sum()),
        "area": int(mask.sum()),
        "centroid": (float(xx.mean()), float(yy.mean())),
        "bbox": (int(xx.min()), int(yy.min()), int(xx.max()) + 1,
                 int(yy.max()) + 1),
        "margins": (int(xx.min()), int(SIDE - 1 - xx.max()),
                    int(yy.min()), int(SIDE - 1 - yy.max())),
    }


def main():
    original = cells(HERE / "original_frost_rock_rime.png", 7)
    new = cells(ART / "frost_rock_rime.png", 7)
    death = cells(ART / "Death" / "frost_rock_rime.png", 3)
    measures = [measure(pixels) for pixels in new]
    idle_zero = measures[0]
    for i, (before, after, result) in enumerate(zip(original, new, measures)):
        assert np.array_equal(before[:, :, 3], after[:, :, 3]), f"cell {i} alpha changed"
        assert result["tones"] >= 450 and result["boundaries"] >= 4500
        assert min(result["margins"]) >= 6
        if i < 4:
            ratio = result["area"] / idle_zero["area"]
            offset = math.dist(result["centroid"], idle_zero["centroid"])
            assert 1 <= ratio <= 1.03 and offset <= 2, (i, ratio, offset)
        print(f"strip {i}: {result['tones']} tones, {result['boundaries']} boundaries, "
              f"area {result['area']}, bbox {result['bbox']}, margins {result['margins']}")
    death_measures = [measure(pixels) for pixels in death]
    for i, result in enumerate(death_measures):
        assert min(result["margins"]) >= 6
        print(f"death {i}: size {SIDE}x{SIDE}, area {result['area']}, "
              f"bbox {result['bbox']}, margins {result['margins']}")
    offset = math.dist(death_measures[0]["centroid"], measures[3]["centroid"])
    assert offset <= 3
    print(f"death flash vs idle 3 centroid offset: {offset:.2f}px")
    print("all checks passed")


if __name__ == "__main__":
    main()
