"""Verify Batch A art deliverables and print one result row per badge.

Run: python3 Pause/Assets/Art/Achievements/src~/verify_batch_a.py
"""

from collections import defaultdict
from pathlib import Path
import math
import numpy as np
from PIL import Image
from build_batch_a import BADGES, BLACK, HERE, LIVE


def subject_dhash(image):
    # Hash the central painted subject so the common tier frame cannot mask a
    # duplicate icon. The 9x8 grayscale difference hash is perceptual.
    w, h = image.size
    small = image.convert("L").crop((round(w*.19), round(h*.19),
                                     round(w*.81), round(h*.81))).resize((9, 8), Image.Resampling.LANCZOS)
    arr = np.asarray(small, dtype=np.int16)
    bits = (arr[:, 1:] > arr[:, :-1]).flatten()
    return sum(int(bit) << i for i, bit in enumerate(bits))


def rim_stats(master):
    pixels = np.asarray(master, dtype=np.uint8)
    y, x = np.ogrid[:1024, :1024]
    r2 = (x-511.5)**2 + (y-511.5)**2
    annulus = pixels[(r2 > 375**2) & (r2 < 435**2)]
    bright = annulus[annulus.max(axis=1) > 75]
    return tuple(float(v) for v in bright.mean(axis=0))


def tier_ok(tier, rgb):
    r, g, b = rgb
    if tier == "Bronze":
        return r > g*1.2 and g > b*1.15
    if tier == "Silver":
        return max(rgb)-min(rgb) < 34 and b >= r-20
    if tier == "Gold":
        return r > g*1.18 and g > b*1.3
    return r > 175 and g > 145 and b > 105 and r-g < 45


def check_one(name, tier):
    errors = []
    master_path = HERE / f"{name}_1024.png"
    icon_path = LIVE / f"{name}.png"
    if not master_path.is_file() or not icon_path.is_file():
        return ["missing file"], 0, (0, 0, 0), None
    with Image.open(master_path) as master, Image.open(icon_path) as icon:
        if master.size != (1024, 1024) or master.mode != "RGB":
            errors.append("master size/mode")
        if icon.size != (128, 128) or icon.mode != "RGBA":
            errors.append("icon size/mode")
        if not master.info.get("icc_profile") or not icon.info.get("icc_profile"):
            errors.append("sRGB profile")
        arr = np.asarray(master.convert("RGB"), dtype=np.int16)
        y, x = np.ogrid[:1024, :1024]
        r2 = (x-511.5)**2 + (y-511.5)**2
        if np.any(arr[r2 > 460**2] != np.array(BLACK)):
            errors.append("outside 90% circle")
        corners = [arr[:64, :64], arr[:64, -64:], arr[-64:, :64], arr[-64:, -64:]]
        if any(np.max(np.abs(c-np.array(BLACK))) > 3 for c in corners):
            errors.append("corners")
        alpha = np.asarray(icon.getchannel("A"))
        yy, xx = np.ogrid[:128, :128]
        ideal = (xx-63.5)**2 + (yy-63.5)**2 <= 57.5**2
        if set(np.unique(alpha)) != {0, 255} or not np.array_equal(alpha == 255, ideal):
            errors.append("hard round alpha")
        colors = len(icon.getcolors(16384) or [])
        if colors < 800:
            errors.append("fewer than 800 colours")
        rim = rim_stats(master)
        if not tier_ok(tier, rim):
            errors.append("tier rim")
        return errors, colors, rim, subject_dhash(icon)


def main():
    print("id                       tier      128 colors  rim RGB       hash             status")
    print("-"*91)
    failures = 0
    hashes = defaultdict(list)
    for name, tier, _, _ in BADGES:
        errors, colors, rim, hash_value = check_one(name, tier)
        if hash_value is not None:
            hashes[hash_value].append(name)
        status = "PASS" if not errors else ", ".join(errors)
        failures += bool(errors)
        print(f"{name:<25}{tier:<10}{colors:>7}     "
              f"{tuple(round(v) for v in rim)!s:<14} {hash_value or 0:016x} {status}")
    duplicates = [v for v in hashes.values() if len(v) > 1]
    for group in duplicates:
        print("DUPLICATE perceptual hash:", ", ".join(group))
    failures += len(duplicates)
    print(f"\n{len(BADGES)} badges; {len(BADGES)-failures} pass; {failures} failures; "
          f"{len(hashes)} distinct subject perceptual hashes.")
    raise SystemExit(1 if failures else 0)


if __name__ == "__main__":
    main()
