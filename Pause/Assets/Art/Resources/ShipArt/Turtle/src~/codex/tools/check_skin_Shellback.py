from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
REF_SIZE = 1536
CELL = 512
OUT_CELL = 1024
OUT_SIZE = 3072


CASES = (
    ("skin_Shellback_state0.png", "skinref_state0.png", tuple(range(9))),
    ("skin_Shellback_state1.png", "skinref_state1.png", (0,)),
    ("skin_Shellback_state2.png", "skinref_state2.png", (0,)),
)


def pixels(image: Image.Image):
    getter = getattr(image, "get_flattened_data", None)
    return list(getter()) if getter is not None else list(image.getdata())


def crop(image: Image.Image, index: int, size: int) -> Image.Image:
    x = (index % 3) * size
    y = (index // 3) * size
    return image.crop((x, y, x + size, y + size)).convert("RGB")


def non_green(pixel: tuple[int, int, int]) -> bool:
    return pixel != GREEN


def canopy(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    # Cyan canopy/glow region, deliberately broad enough to catch its ramp.
    return g >= 95 and b >= 95 and b >= r + 28 and g >= r + 18


def report(out: Image.Image, ref: Image.Image, name: str, index: int) -> tuple[float, float, float]:
    reference = ref.resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)
    out_pixels = pixels(out)
    ref_pixels = pixels(reference)
    ref_mask = [non_green(p) for p in ref_pixels]
    out_mask = [non_green(p) for p in out_pixels]
    silhouette_diff = sum(a != b for a, b in zip(ref_mask, out_mask)) / sum(ref_mask) * 100.0
    canopy_mask = [canopy(p) for p in ref_pixels]
    canopy_total = max(1, sum(canopy_mask))
    canopy_diff = sum(a != b for index, (a, b) in enumerate(zip(ref_pixels, out_pixels))
                      if canopy_mask[index])
    canopy_diff = canopy_diff / canopy_total * 100.0
    changed = sum(a != b for a, b in zip(ref_pixels, out_pixels)) / len(ref_pixels) * 100.0
    print(f"{name} cell {index}: silhouette {silhouette_diff:.4f}% | canopy {canopy_diff:.4f}% | changed {changed:.4f}%")
    return silhouette_diff, canopy_diff, changed


def main() -> None:
    all_pass = True
    for out_name, ref_name, indices in CASES:
        out = Image.open(ROOT / out_name).convert("RGB")
        ref = Image.open(ROOT / ref_name).convert("RGB")
        if out.size != (OUT_SIZE, OUT_SIZE):
            raise SystemExit(f"FAIL: {out_name} is {out.size}, expected 3072x3072")
        if ref.size != (REF_SIZE, REF_SIZE):
            raise SystemExit(f"FAIL: {ref_name} is {ref.size}, expected 1536x1536")
        for index in indices:
            values = report(crop(out, index, OUT_CELL), crop(ref, index, CELL), out_name, index)
            all_pass &= values[0] < 1.5 and values[1] < 6.0 and values[2] > 3.0
        # State 1/2 must have no painted cells outside the requested key cell.
        if indices == (0,):
            for index in range(1, 9):
                if any(pixel != GREEN for pixel in pixels(crop(out, index, OUT_CELL))):
                    print(f"{out_name} cell {index}: non-green pixels found in required-empty cell")
                    all_pass = False
    if not all_pass:
        raise SystemExit("FAIL: one or more Shellback constraints did not pass")
    print("PASS: dimensions, empty-cell contract, silhouette, canopy, and colour-change thresholds")


if __name__ == "__main__":
    main()
