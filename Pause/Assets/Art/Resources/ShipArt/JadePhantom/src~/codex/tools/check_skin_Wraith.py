"""Validate Wraith sheets against the approved 3x3 reference sheets."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BG = np.array([0, 255, 0], dtype=np.uint8)


def cell(a: np.ndarray, row: int, col: int) -> np.ndarray:
    return a[row * 1024 : (row + 1) * 1024, col * 1024 : (col + 1) * 1024]


def blue_region(ref: np.ndarray) -> np.ndarray:
    r, g, b = [ref[..., i].astype(np.int16) for i in range(3)]
    not_armour = ~(
        (g > r * 1.08) & (g >= b * 0.75) & ((g - r) > 8) & (g > 8)
    )
    return not_armour & (b > 60) & (b > r * 1.15) & (b > g * 1.05)


def pct(n: int, d: int) -> float:
    return 100.0 * n / max(d, 1)


def main() -> int:
    all_ok = True
    for state in range(3):
        ref_small = np.asarray(Image.open(ROOT / f"skinref_state{state}.png").convert("RGB"))
        ref = np.asarray(
            Image.fromarray(ref_small, "RGB").resize((3072, 3072), Image.Resampling.NEAREST)
        )
        out = np.asarray(Image.open(ROOT / f"skin_Wraith_state{state}.png").convert("RGB"))
        if out.shape != ref.shape:
            print(f"state{state}: FAIL dimensions output={out.shape} expected={ref.shape}")
            all_ok = False
            continue
        bg_ok = bool(np.all(out[np.all(ref == BG, axis=2)] == BG))
        print(f"state{state} background={'PASS' if bg_ok else 'FAIL'}")
        all_ok &= bg_ok
        for row in range(3):
            for col in range(3):
                rr = cell(ref, row, col)
                oo = cell(out, row, col)
                ref_sil = np.any(rr != BG, axis=2)
                out_sil = np.any(oo != BG, axis=2)
                sil_diff = pct(np.count_nonzero(ref_sil ^ out_sil), ref_sil.size)

                blue = blue_region(rr)
                pixel_diff = np.any(rr != oo, axis=2)
                canopy_diff = pct(np.count_nonzero(pixel_diff & blue), np.count_nonzero(blue))
                changed = pct(np.count_nonzero(pixel_diff), pixel_diff.size)
                ok = sil_diff < 1.5 and canopy_diff < 6.0 and changed > 3.0
                all_ok &= ok
                status = "PASS" if ok else "FAIL"
                print(
                    f"  cell {row},{col} {status} "
                    f"silhouette={sil_diff:.3f}% "
                    f"canopy_blue={canopy_diff:.3f}% "
                    f"changed={changed:.3f}%"
                )
    print("SUMMARY", "PASS" if all_ok else "FAIL")
    return 0 if all_ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
