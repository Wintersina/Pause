#!/usr/bin/env python3
"""Validate J1 dimensions, keying, silhouette invariance, and damage coverage."""

from pathlib import Path
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
NAMES = ["key_state0.png", "key_state1.png", "key_state2.png"]
GREEN = (0, 255, 0)


def load(name):
    p = ROOT / name
    im = Image.open(p).convert("RGB")
    if im.size != (1024, 1024):
        raise AssertionError(f"{name}: expected 1024x1024, got {im.size}")
    return im


def mask(im):
    px = im.load()
    out = bytearray(1024 * 1024)
    for y in range(1024):
        row = y * 1024
        for x in range(1024):
            out[row + x] = px[x, y] != GREEN
    return out


def bbox(m):
    xs, ys = [], []
    for y in range(1024):
        row = y * 1024
        for x in range(1024):
            if m[row + x]:
                xs.append(x)
                ys.append(y)
    return (min(xs), min(ys), max(xs) + 1, max(ys) + 1) if xs else None


def pct_changed(a, b, indices=None):
    if indices is None:
        indices = range(1024 * 1024)
    indices = list(indices)
    changed = sum(a[i] != b[i] for i in indices)
    return changed * 100.0 / len(indices)


def changed_indices(a, b, allowed=None):
    if allowed is None:
        allowed = range(1024 * 1024)
    return [i for i in allowed if a[i] != b[i]]


def main():
    ims = [load(n) for n in NAMES]
    masks = [mask(im) for im in ims]
    print("file           size       non-green bbox")
    for n, im, m in zip(NAMES, ims, masks):
        print(f"{n:14} {im.width}x{im.height}  {bbox(m)}")

    assert masks[0] == masks[1] == masks[2], "outer silhouette differs between states"
    silhouette = [i for i, v in enumerate(masks[0]) if v]
    sil0_1 = pct_changed(masks[0], masks[1])
    sil0_2 = pct_changed(masks[0], masks[2])
    print(f"silhouette pixels differing: state0->1 {sil0_1:.4f}% | state0->2 {sil0_2:.4f}%")

    pixels = [list(im.get_flattened_data()) for im in ims]
    c01 = pct_changed(pixels[0], pixels[1])
    c02 = pct_changed(pixels[0], pixels[2])
    c12 = pct_changed(pixels[1], pixels[2])
    print(f"colour pixels differing:     state0->1 {c01:.4f}% | state0->2 {c02:.4f}% | state1->2 {c12:.4f}%")

    left = [i for i in silhouette if (i % 1024) < 512]
    right = [i for i in silhouette if (i % 1024) >= 512]
    for label, idx in (("state1", 1), ("state2", 2)):
        a = pixels[0]
        b = pixels[idx]
        print(f"{label} changed ship pixels: left {pct_changed(a, b, left):.4f}% | right {pct_changed(a, b, right):.4f}%")

    assert c01 > 3.0, "state 1 is not damaged enough"
    assert c02 > 12.0, "state 2 is not damaged enough"
    assert c12 > 5.0, "state 2 does not add enough damage beyond state 1"
    assert sil0_1 < 1.5 and sil0_2 < 1.5, "silhouette drift exceeds 1.5%"
    left2 = pct_changed(pixels[0], pixels[2], left)
    right2 = pct_changed(pixels[0], pixels[2], right)
    assert left2 > 6.0 and right2 > 6.0, "state 2 damage is not readable on both halves"
    print("PASS")


if __name__ == "__main__":
    main()
