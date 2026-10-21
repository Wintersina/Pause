#!/usr/bin/env python3
"""Validate the J1c keyed silhouette and damage deltas."""

from pathlib import Path

import numpy as np
from PIL import Image


GREEN = np.array([0, 255, 0], dtype=np.uint8)


def load(path: Path):
    im = Image.open(path).convert("RGB")
    arr = np.asarray(im)
    mask = np.any(arr != GREEN, axis=2)
    return im, arr, mask


def bbox(mask):
    ys, xs = np.where(mask)
    return (int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)) if len(xs) else None


def changed_percent(a, b, region):
    return float(np.any(a != b, axis=2)[region].mean() * 100.0)


def side_changed_percent(a, b, region, left):
    h, w = region.shape
    x0, x1 = (0, w // 2) if left else (w // 2, w)
    side = region.copy()
    side[:, :x0] = False
    side[:, x1:] = False
    return changed_percent(a, b, side)


def silhouette_delta(a, b):
    return float(np.logical_xor(a, b).mean() * 100.0)


def main():
    root = Path(__file__).resolve().parents[1]
    paths = [root / "key_state0.png", root / "key_state1.png", root / "key_state2.png"]
    loaded = [load(p) for p in paths]
    print("file          size        non-green bbox                 silhouette-px")
    for p, (im, _, mask) in zip(paths, loaded):
        print(f"{p.name:13} {im.size!s:11} {str(bbox(mask)):29} {int(mask.sum()):13}")

    _, a0, m0 = loaded[0]
    _, a1, m1 = loaded[1]
    _, a2, m2 = loaded[2]
    d01 = silhouette_delta(m0, m1)
    d02 = silhouette_delta(m0, m2)
    c01 = changed_percent(a0, a1, m0)
    c02 = changed_percent(a0, a2, m0)
    c21 = changed_percent(a2, a1, m0)
    c2_left = side_changed_percent(a0, a2, m0, left=True)
    c2_right = side_changed_percent(a0, a2, m0, left=False)

    print("\ncomparison                         percent")
    print(f"state0 vs state1 silhouette delta  {d01:7.3f}%")
    print(f"state0 vs state2 silhouette delta  {d02:7.3f}%")
    print(f"state0 -> state1 colour change     {c01:7.3f}%")
    print(f"state0 -> state2 colour change     {c02:7.3f}%")
    print(f"state2 -> state1 colour change     {c21:7.3f}%")
    print(f"state2 left-half colour change     {c2_left:7.3f}%")
    print(f"state2 right-half colour change    {c2_right:7.3f}%")

    ok = all(im.size == (1024, 1024) for im, _, _ in loaded)
    ok &= d01 == 0.0 and d02 == 0.0
    ok &= c01 > 8.0 and c02 > 30.0 and c21 > 15.0
    ok &= c2_left > 25.0 and c2_right > 25.0
    if not ok:
        raise SystemExit("J1 validation failed")
    print("\nJ1 validation: PASS")


if __name__ == "__main__":
    main()
