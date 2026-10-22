"""Check Sodium sheets against the approved 1536px reference sheets.

Each output cell is reduced from 1024px to the 512px reference grid by taking every other
pixel. The reported metrics are the requested silhouette, canopy/blue-region, and total pixel
change percentages.
"""
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def hsv(rgb):
    rgb = rgb.astype(np.float32) / 255.0
    mx = rgb.max(axis=-1)
    mn = rgb.min(axis=-1)
    delta = mx - mn
    hue = np.zeros_like(mx)
    nz = delta != 0
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    sel = nz & (mx == r)
    hue[sel] = ((g[sel] - b[sel]) / delta[sel]) % 6
    sel = nz & (mx == g)
    hue[sel] = (b[sel] - r[sel]) / delta[sel] + 2
    sel = nz & (mx == b)
    hue[sel] = (r[sel] - g[sel]) / delta[sel] + 4
    hue /= 6.0
    saturation = np.zeros_like(mx)
    nonblack = mx != 0
    saturation[nonblack] = delta[nonblack] / mx[nonblack]
    return hue, saturation, mx


def cell(im, row, col, size):
    return im[row * size:(row + 1) * size, col * size:(col + 1) * size]


def metrics(ref, out):
    ref_mask = ~np.all(ref == GREEN, axis=-1)
    out_mask = ~np.all(out == GREEN, axis=-1)
    silhouette = 100.0 * np.count_nonzero(ref_mask != out_mask) / ref_mask.size

    hue, saturation, value = hsv(ref)
    canopy = (
        ref_mask
        & (hue >= 160.0 / 360.0)
        & (hue <= 230.0 / 360.0)
        & (saturation > 0.22)
        & (value > 0.18)
    )
    if np.count_nonzero(canopy):
        canopy_diff = 100.0 * np.count_nonzero(np.any(ref[canopy] != out[canopy], axis=-1)) / np.count_nonzero(canopy)
    else:
        canopy_diff = 0.0

    changed = 100.0 * np.count_nonzero(np.any(ref != out, axis=-1)) / (ref.shape[0] * ref.shape[1])
    return silhouette, canopy_diff, float(changed)


def main():
    all_ok = True
    summary = []
    for state in range(3):
        ref_sheet = np.array(Image.open(ROOT / f"skinref_state{state}.png").convert("RGB"), dtype=np.uint8)
        out_sheet = np.array(Image.open(ROOT / f"skin_Sodium_state{state}.png").convert("RGB"), dtype=np.uint8)
        if out_sheet.shape != (3072, 3072, 3):
            raise SystemExit(f"state{state}: expected 3072x3072 RGB, got {out_sheet.shape}")
        print(f"skin_Sodium_state{state}.png")
        for row in range(3):
            for col in range(3):
                ref = cell(ref_sheet, row, col, 512)
                out_hi = cell(out_sheet, row, col, 1024)
                out = out_hi[::2, ::2]
                silhouette, canopy, changed = metrics(ref, out)
                exempt = row == 2 and col == 2
                ok = silhouette < 1.5 and canopy < 6.0 and (exempt or changed > 12.0)
                all_ok &= ok
                summary.append((state, row * 3 + col, silhouette, canopy, changed, ok))
                tag = "PASS" if ok else "FAIL"
                suffix = " (hit-flash)" if exempt else ""
                print(
                    f"  cell {row * 3 + col}: silhouette={silhouette:.3f}% "
                    f"canopy_blue={canopy:.3f}% changed={changed:.3f}%{suffix} {tag}"
                )

    passed = sum(item[-1] for item in summary)
    print(f"SUMMARY: {passed}/{len(summary)} cells passed")
    raise SystemExit(0 if all_ok else 1)


if __name__ == "__main__":
    main()
