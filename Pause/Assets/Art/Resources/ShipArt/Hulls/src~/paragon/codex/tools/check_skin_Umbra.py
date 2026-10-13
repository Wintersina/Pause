"""Audit Umbra's 27 frames against the approved 512 px reference cells.

All changed-pixel percentages use the whole cell, including chroma green.
The blue metric counts source-blue pixels with a total RGB delta above 60.
The white hit-flash has no former-red armour, so its hue score is N/A.
"""

from pathlib import Path

import numpy as np
from PIL import Image

from build_skin_Umbra import GREEN, hsv


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
POSES = ("key", "bob up", "lights off", "glint 1", "glint 2 off",
         "bob down", "bank left", "bank right", "hit flash")


def pct(count, total):
    return 100 * count / max(1, total)


def main():
    failures = []
    print("state cell pose          silhouette%  blue%  red-to-violet%  changed-all%")
    for state in range(3):
        ref = np.asarray(Image.open(HERE / f"skinref_state{state}.png").convert("RGB"))
        path = OUT / f"skin_Umbra_state{state}.png"
        full = np.asarray(Image.open(path).convert("RGB"))
        if full.shape != (3072, 3072, 3):
            failures.append(f"state {state}: sheet is {full.shape}, expected 3072x3072 RGB")
            continue
        art = full[::2, ::2]
        if not (np.array_equal(full[::2, ::2], full[1::2, ::2])
                and np.array_equal(full[::2, ::2], full[::2, 1::2])
                and np.array_equal(full[::2, ::2], full[1::2, 1::2])):
            failures.append(f"state {state}: sheet is not exact 2x nearest-neighbour")
        for cell in range(9):
            row, col = divmod(cell, 3)
            y, x = row * 512, col * 512
            a = ref[y:y + 512, x:x + 512]
            b = art[y:y + 512, x:x + 512]
            in_a = np.any(a != GREEN, axis=2)
            in_b = np.any(b != GREEN, axis=2)
            silhouette = pct(np.count_nonzero(in_a ^ in_b), in_a.size)
            ai, bi = a.astype(np.int16), b.astype(np.int16)
            blue = (ai[:, :, 2] > ai[:, :, 0] * 1.18) & (
                ai[:, :, 2] > ai[:, :, 1] * .78) & (ai[:, :, 2] > 38) & in_a
            blue_changed = np.abs(ai - bi).sum(axis=2) > 60
            blue_score = pct(np.count_nonzero(blue & blue_changed), np.count_nonzero(blue))
            ah, ass, av = hsv(a)
            bh, bs, bv = hsv(b)
            former_red = ((ah <= 34) | (ah >= 350)) & (ass > .40) & (av > .19) & in_a
            if state:
                yy, xx = np.indices((512, 512))
                damage = (xx < 125) & (yy > 175)
                if state == 2:
                    damage |= (xx > 310) & (yy > 175)
                hot = (ah >= 15) & (ah <= 54) & (ass > .53) & (av > .34)
                former_red &= ~(damage & hot)
            violet = (bh >= 248) & (bh <= 282) & (bs > .30) & (bv > .16)
            violet_score = pct(np.count_nonzero(former_red & violet),
                               np.count_nonzero(former_red))
            changed = np.any(a != b, axis=2)
            changed_score = pct(np.count_nonzero(changed), changed.size)
            hue_text = f"{violet_score:6.2f}" if np.any(former_red) else "  n/a "
            print(f"  {state}     {cell}  {POSES[cell]:<12}  {silhouette:8.2f}  "
                  f"{blue_score:6.2f}       {hue_text}         {changed_score:6.2f}")
            if silhouette >= 1.5:
                failures.append(f"state {state} cell {cell}: silhouette {silhouette:.2f}% >= 1.5%")
            if blue_score >= 6:
                failures.append(f"state {state} cell {cell}: blue difference {blue_score:.2f}% >= 6%")
            if np.any(former_red) and violet_score <= 85:
                failures.append(f"state {state} cell {cell}: red-to-violet {violet_score:.2f}% <= 85%")
            if changed_score <= 12:
                failures.append(f"state {state} cell {cell}: changed-all {changed_score:.2f}% <= 12%")
            if cell == 8 and not np.array_equal(a, b):
                failures.append(f"state {state} cell 8: bone-white hit flash changed")
    if failures:
        print(f"FAIL: {len(failures)} checks")
        for failure in failures:
            print("-", failure)
        raise SystemExit(1)
    print("PASS: all per-cell Umbra requirements")


if __name__ == "__main__":
    main()
