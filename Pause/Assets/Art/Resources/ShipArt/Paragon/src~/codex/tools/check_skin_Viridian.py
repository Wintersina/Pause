"""Report per-cell Viridian differences against the approved 512 px flipbook.

All percentages use the entire 512 x 512 reference cell unless labeled as a
subset. The hit-flash pose is checked and printed, even though it contains no
red armour and is intentionally unchanged.
"""

from pathlib import Path

import numpy as np
from PIL import Image

from build_skin_Viridian import hsv


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
CHROMA = np.array((0, 255, 0), dtype=np.uint8)
POSES = ("key", "bob up", "bob up off", "glint 1", "glint 2 off",
         "bob down", "bank left", "bank right", "hit flash")


def pct(mask):
    return 100.0 * np.count_nonzero(mask) / mask.size


def main():
    failures = []
    print("state cell pose          silhouette%   blue%   red-to-emerald%   changed-all%   changed-ship%")
    for state in range(3):
        ref = np.asarray(Image.open(HERE / f"skinref_state{state}.png").convert("RGB"))
        path = OUT / f"skin_Viridian_state{state}.png"
        full = np.asarray(Image.open(path).convert("RGB"))
        if full.shape != (3072, 3072, 3):
            failures.append(f"state {state}: size {full.shape} differs from 3072 x 3072")
            continue
        if not (np.array_equal(full[::2, ::2], full[1::2, ::2]) and
                np.array_equal(full[::2, ::2], full[::2, 1::2]) and
                np.array_equal(full[::2, ::2], full[1::2, 1::2])):
            failures.append(f"state {state}: pixels are not exact 2x nearest-neighbour blocks")
        art = full[::2, ::2]
        for cell in range(9):
            row, col = divmod(cell, 3)
            y, x = row * 512, col * 512
            a = ref[y:y + 512, x:x + 512]
            b = art[y:y + 512, x:x + 512]
            in_a = np.any(a != CHROMA, axis=2)
            in_b = np.any(b != CHROMA, axis=2)
            silhouette = pct(in_a ^ in_b)
            ai, bi = a.astype(np.int16), b.astype(np.int16)
            blue = (ai[:, :, 2] > ai[:, :, 0] * 1.18) & (
                ai[:, :, 2] > ai[:, :, 1] * .78) & (ai[:, :, 2] > 38) & in_a
            large_delta = np.abs(ai - bi).sum(2) > 60
            blue_difference = 100 * np.count_nonzero(blue & large_delta) / max(1, blue.sum())
            ah, ass, av = hsv(a)
            bh, bs, bv = hsv(b)
            former_red = ((ah <= 34) | (ah >= 350)) & (ass > .40) & (av > .19) & in_a
            if state:
                yy, xx = np.indices((512, 512))
                damage = ((state == 1) & (xx < 112) & (yy > 180)) | (
                    (state == 2) & (xx > 310) & (yy > 175))
                former_red &= ~(damage & (ah < 26) & (ass > .72) & (av > .48))
            emerald = (bh >= 140) & (bh <= 175) & (bs > .30) & (bv > .15)
            green_fraction = (100 * np.count_nonzero(former_red & emerald) /
                              max(1, former_red.sum()))
            changed_mask = np.any(a != b, axis=2)
            changed = pct(changed_mask)
            changed_ship = 100 * np.count_nonzero(changed_mask & in_a) / max(1, in_a.sum())
            green_text = f"{green_fraction:6.2f}" if cell != 8 else "   n/a"
            print(f"  {state}     {cell}  {POSES[cell]:<12} {silhouette:8.2f}     "
                  f"{blue_difference:6.2f}         {green_text}          "
                  f"{changed:6.2f}          {changed_ship:6.2f}")
            if silhouette >= 1.5:
                failures.append(f"state {state} cell {cell}: silhouette {silhouette:.2f}% >= 1.5%")
            if blue_difference >= 6:
                failures.append(f"state {state} cell {cell}: blue difference {blue_difference:.2f}% >= 6%")
            if cell != 8 and green_fraction <= 85:
                failures.append(f"state {state} cell {cell}: red to emerald {green_fraction:.2f}% <= 85%")
            if changed <= 12:
                failures.append(f"state {state} cell {cell}: all pixels changed {changed:.2f}% <= 12%")
            if cell == 8 and not np.array_equal(a, b):
                failures.append(f"state {state} cell 8: hit flash differs from approved art")
            if np.any(np.all(b == CHROMA, axis=2) & in_a):
                failures.append(f"state {state} cell {cell}: chroma green inside original ship")
            magenta = (bh >= 280) & (bh <= 345) & (bs > .39) & (bv > .23) & in_b
            if np.any(magenta):
                failures.append(f"state {state} cell {cell}: {magenta.sum()} magenta pixels")
    if failures:
        print("FAIL:")
        for failure in failures:
            print("-", failure)
        raise SystemExit(1)
    print("PASS: all requested per-cell thresholds")


if __name__ == "__main__":
    main()
