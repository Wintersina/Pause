"""Check every Aurum frame against the attached approved Paragon flipbook.

Percentages are measured at the 512 px reference cell size.  A changed pixel
has any RGB difference; blue change uses a summed RGB difference above 60.
The changed-pixel denominator is the entire cell, including chroma green.
"""

from pathlib import Path

import numpy as np
from PIL import Image

from build_skin_Aurum import hsv


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
GREEN = np.array((0, 255, 0), dtype=np.uint8)
POSES = ("key", "bob up", "lights off", "glint 1", "glint 2 off",
         "bob down", "bank left", "bank right", "hit flash")


def percent(mask):
    return 100.0 * np.count_nonzero(mask) / mask.size


def main():
    failures = []
    print("state cell pose          silhouette%   blue%   red-to-gold%   changed-all%   changed-ship%")
    for state in range(3):
        ref = np.asarray(Image.open(HERE / f"skinref_state{state}.png").convert("RGB"))
        path = OUT / f"skin_Aurum_state{state}.png"
        full = np.asarray(Image.open(path).convert("RGB"))
        if full.shape != (3072, 3072, 3):
            failures.append(f"state {state}: wrong sheet size {full.shape}")
            continue
        if not np.array_equal(full[::2, ::2], full[1::2, 1::2]):
            failures.append(f"state {state}: output is not 2x nearest-neighbour pixels")
        art = full[::2, ::2]
        for cell in range(9):
            row, col = divmod(cell, 3)
            y, x = row * 512, col * 512
            a = ref[y:y + 512, x:x + 512]
            b = art[y:y + 512, x:x + 512]
            in_a = np.any(a != GREEN, axis=2)
            in_b = np.any(b != GREEN, axis=2)
            silhouette = percent(in_a ^ in_b)
            ai = a.astype(np.int16)
            bi = b.astype(np.int16)
            blue = (ai[:, :, 2] > ai[:, :, 0] * 1.18) & (
                ai[:, :, 2] > ai[:, :, 1] * .78) & (ai[:, :, 2] > 38) & in_a
            blue_changed = np.abs(ai - bi).sum(axis=2) > 60
            blue_pct = 100 * np.count_nonzero(blue & blue_changed) / max(1, np.count_nonzero(blue))
            ah, ass, av = hsv(a)
            bh, bs, bv = hsv(b)
            former_red = ((ah <= 34) | (ah >= 350)) & (ass > .40) & (av > .19) & in_a
            # Damage cracks remain orange in the reference and the Aurum skin.
            if state:
                yy, xx = np.indices((512, 512))
                damage = ((state == 1) & (xx < 112) & (yy > 180)) | (
                    (state == 2) & (xx > 310) & (yy > 175))
                former_red &= ~(damage & (ah < 26) & (ass > .72) & (av > .48))
            gold = (bh >= 29) & (bh <= 59) & (bs > .33) & (bv > .18)
            gold_pct = 100 * np.count_nonzero(former_red & gold) / max(1, np.count_nonzero(former_red))
            changed = np.any(a != b, axis=2)
            changed_pct = percent(changed)
            changed_ship_pct = 100 * np.count_nonzero(changed & in_a) / max(1, np.count_nonzero(in_a))
            magenta = (bh > 280) & (bh < 345) & (bs > .4) & (bv > .25) & in_b
            gold_display = f"{gold_pct:6.2f}" if cell != 8 else "   n/a"
            print(f"  {state}     {cell}  {POSES[cell]:<12} {silhouette:8.2f}     "
                  f"{blue_pct:6.2f}       {gold_display}          {changed_pct:6.2f}          "
                  f"{changed_ship_pct:6.2f}")
            if silhouette >= 1.5:
                failures.append(f"state {state} cell {cell}: silhouette {silhouette:.2f}% >= 1.5%")
            if blue_pct >= 6:
                failures.append(f"state {state} cell {cell}: blue {blue_pct:.2f}% >= 6%")
            if cell != 8 and gold_pct <= 85:
                failures.append(f"state {state} cell {cell}: former red to gold {gold_pct:.2f}% <= 85%")
            if changed_pct <= 12:
                failures.append(f"state {state} cell {cell}: all pixels changed {changed_pct:.2f}% <= 12%")
            if np.any(magenta):
                failures.append(f"state {state} cell {cell}: {np.count_nonzero(magenta)} magenta pixels")
            if cell == 8 and not np.array_equal(a, b):
                failures.append(f"state {state} cell 8: hit flash is not identical to the approved bone-white pose")
    if failures:
        print("FAIL:")
        for failure in failures:
            print("-", failure)
        raise SystemExit(1)
    print("PASS: all per-cell Aurum requirements")


if __name__ == "__main__":
    main()
