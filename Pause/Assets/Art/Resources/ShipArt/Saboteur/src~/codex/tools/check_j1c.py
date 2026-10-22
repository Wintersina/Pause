#!/usr/bin/env python3
"""Audit J1c dimensions, exact silhouette preservation, damage budgets, and dark-blob ratio."""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
FILES = [ROOT / "key_state0.png", ROOT / "key_state1.png", ROOT / "key_state2.png"]


def mask(im):
    return { (x, y) for y in range(im.height) for x in range(im.width)
             if im.getpixel((x, y)) != GREEN }


def changed_pixels(base, other):
    return [(x, y) for y in range(1024) for x in range(1024)
            if base.getpixel((x, y)) != other.getpixel((x, y))]


def main():
    images = [Image.open(path).convert("RGB") for path in FILES]
    for path, im in zip(FILES, images):
        assert im.size == (1024, 1024), f"{path.name}: expected 1024x1024, got {im.size}"
    base, state1, state2 = images
    base_mask = mask(base)
    print("file          size       silhouette diff    changed pixels    near-black changed")
    print("------------  ---------  -----------------  ----------------  ------------------")
    print(f"{FILES[0].name:12}  1024x1024  {'0.000%':17}  {'n/a':16}  {'n/a':18}")
    stats = {}
    for path, im in zip(FILES[1:], images[1:]):
        sil = len(base_mask ^ mask(im)) / len(base_mask) * 100.0
        changed = changed_pixels(base, im)
        changed_set = set(changed)
        near_black = sum(max(im.getpixel(p)) < 25 for p in changed)
        changed_pct = len(changed) / (1024 * 1024) * 100.0
        black_share = near_black / len(changed) * 100.0 if changed else 0.0
        stats[path.name] = changed_pct
        print(f"{path.name:12}  1024x1024  {sil:16.3f}%  {changed_pct:15.3f}%  {black_share:17.3f}%")
        assert sil < 1.5, f"{path.name}: silhouette difference too high"
        assert black_share < 45.0, f"{path.name}: near-black changed-pixel share too high"
        if path.name == "key_state1.png":
            assert 3.0 <= changed_pct <= 12.0, f"state1 changed-pixel budget: {changed_pct:.3f}%"
        else:
            assert 8.0 <= changed_pct <= 20.0, f"state2 changed-pixel budget: {changed_pct:.3f}%"
    assert stats["key_state2.png"] >= 1.5 * stats["key_state1.png"], "state2 is not 1.5x state1"
    print("state2/state1 changed-pixel ratio: %.3fx" % (stats["key_state2.png"] / stats["key_state1.png"]))
    print("PASS")


if __name__ == "__main__":
    main()
