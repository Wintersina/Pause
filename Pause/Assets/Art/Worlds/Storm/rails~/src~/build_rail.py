"""Assemble the painted Storm rail with Tide's proven structural alpha mask.

The generator supplies every visible material and lamp pixel. This script only
registers its two edits, repairs transparent cutouts, and cleans accidental hues.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
ART = HERE.parents[3]
TIDE = ART / "Resources/Worlds/Tide/rail_tide_wide_v1.png"


def rgb_to_hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    rgb = rgb.astype(np.float32) / 255
    hi, lo = rgb.max(axis=2), rgb.min(axis=2)
    delta = hi - lo
    sat = np.divide(delta, hi, out=np.zeros_like(hi), where=hi > 0)
    hue = np.zeros_like(hi)
    live = delta > 0
    r = (hi == rgb[:, :, 0]) & live
    g = (hi == rgb[:, :, 1]) & live & ~r
    b = live & ~r & ~g
    hue[r] = ((rgb[:, :, 1][r] - rgb[:, :, 2][r]) / delta[r]) % 6
    hue[g] = (rgb[:, :, 2][g] - rgb[:, :, 0][g]) / delta[g] + 2
    hue[b] = (rgb[:, :, 0][b] - rgb[:, :, 1][b]) / delta[b] + 4
    return (hue * 60) % 360, sat, hi


def hsv_to_rgb(hue: np.ndarray, sat: np.ndarray, val: np.ndarray) -> np.ndarray:
    h = hue / 60
    c = val * sat
    second = c * (1 - np.abs(h % 2 - 1))
    low = val - c
    out = np.empty((*hue.shape, 3), dtype=np.float32)
    parts = ((c, second, 0), (second, c, 0), (0, c, second),
             (0, second, c), (second, 0, c), (c, 0, second))
    sector = np.floor(h).astype(np.int8)
    for i, channels in enumerate(parts):
        use = sector == i
        for ch, part in enumerate(channels):
            out[:, :, ch][use] = part[use] if isinstance(part, np.ndarray) else part
    return np.uint8(np.clip(np.rint((out + low[:, :, None]) * 255), 0, 255))


def fill_nearest_painted(result: np.ndarray, required: np.ndarray) -> int:
    """Copy nearest painted texels into small registration holes, without AA."""
    valid = result[:, :, 3] >= 128
    missing = required & ~valid
    original_missing = int(missing.sum())
    for _ in range(32):
        if not missing.any():
            break
        updates = 0
        for dy, dx in ((0, -1), (-1, 0), (0, 1), (1, 0)):
            source = np.roll(result, (dy, dx), axis=(0, 1))
            source_valid = np.roll(valid, (dy, dx), axis=(0, 1))
            if dy == 1:
                source_valid[0] = False
            elif dy == -1:
                source_valid[-1] = False
            if dx == 1:
                source_valid[:, 0] = False
            elif dx == -1:
                source_valid[:, -1] = False
            take = missing & source_valid
            result[take, :3] = source[take, :3]
            valid[take] = True
            missing[take] = False
            updates += int(take.sum())
        if updates == 0:
            break
    # A few Tide-mask islands lie across a transparent gap from the edited
    # paint. Find the nearest painted sample without interpolating its color.
    for y, x in np.argwhere(missing):
        for radius in range(1, 129):
            y0, y1 = max(0, y-radius), min(valid.shape[0], y+radius+1)
            x0, x1 = max(0, x-radius), min(valid.shape[1], x+radius+1)
            neighbors = np.argwhere(valid[y0:y1, x0:x1])
            if len(neighbors):
                neighbors[:, 0] += y0
                neighbors[:, 1] += x0
                nearest = neighbors[np.argmin(np.sum((neighbors - (y, x))**2, axis=1))]
                result[y, x, :3] = result[nearest[0], nearest[1], :3]
                valid[y, x] = True
                missing[y, x] = False
                break
    assert not missing.any(), f"{missing.sum()} pixels lack painted neighbors"
    return original_missing


def clean_hues(result: np.ndarray) -> None:
    hue, sat, val = rgb_to_hsv(result[:, :, :3])
    active = (result[:, :, 3] > 0) & (sat >= 0.08) & (val >= 0.08)
    bright = val >= 0.68

    # Storm's tiny work lights may be pale yellow; warm low-value hull pixels
    # become slate. Pink warning lamps stay confined to their painted locations.
    warm = active & (hue >= 15) & (hue < 75)
    work = warm & bright
    hull = warm & ~bright
    hue[work] = 58
    sat[work] = np.minimum(sat[work], 0.25)
    hue[hull] = 218
    sat[hull] = np.minimum(sat[hull], 0.17)

    red = active & ((hue < 15) | (hue >= 345))
    red_lamp = red & bright & (sat >= 0.35)
    hue[red_lamp] = 331
    hue[red & ~red_lamp] = 218
    sat[red & ~red_lamp] = np.minimum(sat[red & ~red_lamp], 0.18)

    foreign_cool = active & (hue >= 75) & (hue < 190)
    hue[foreign_cool & bright] = 205
    hue[foreign_cool & ~bright] = 218
    sat[foreign_cool] = np.minimum(sat[foreign_cool], 0.45)
    violet = active & (hue > 235) & (hue < 310)
    hue[violet] = 220
    sat[violet] = np.minimum(sat[violet], 0.30)
    vivid_blue = active & (hue >= 190) & (hue <= 235) & bright
    sat[vivid_blue] = np.minimum(sat[vivid_blue], 0.50)
    steel = active & ~bright & (hue >= 190) & (hue <= 235)
    sat[steel] = np.minimum(sat[steel], 0.30)
    result[:, :, :3] = hsv_to_rgb(hue, sat, val)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if args.out.exists():
        raise SystemExit(f"Refusing to overwrite {args.out}")

    base = np.array(Image.open(HERE / "candidate_b.png").convert("RGBA"))
    seam = np.array(Image.open(HERE / "seam_edit.png").convert("RGBA"))
    tide = np.array(Image.open(TIDE).convert("RGBA"))
    assert base.shape == (2169, 725, 4)
    assert seam.shape[1:] == (725, 4) and seam.shape[0] >= 2168
    assert tide.shape == (2170, 725, 4)

    # The image edit is used at both ends. The selected original painted edit
    # is byte-for-byte the source of the center 90% before hue/alpha cleaning.
    strip = 108
    result = np.empty_like(tide)
    result[:strip] = seam[:strip]
    result[strip:2170-strip] = base[strip:2170-strip]
    result[2170-strip:] = seam[-strip:]

    required = tide[:, :, 3] == 255
    filled = fill_nearest_painted(result, required)
    result[:, :, 3] = np.where(required, 255, 0).astype(np.uint8)
    clean_hues(result)
    result[~required] = 0

    # The generated seam edit contributes the end strips. Match the terminal
    # row exactly with one nearest-neighbor scanline copy for point filtering.
    result[-1] = result[0]
    args.out.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(result, "RGBA").save(args.out, optimize=True)
    print(f"saved {args.out}; painted registration pixels filled: {filled}")


if __name__ == "__main__":
    main()
