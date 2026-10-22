#!/usr/bin/env python3
"""Validate the IonLancer damaged and heavy-wreck redo sheets."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def rgb(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.uint8)


def pct(n: int, d: int) -> float:
    return 100.0 * n / d if d else 0.0


def damage_mask(a: np.ndarray) -> np.ndarray:
    r, g, b = [a[:, :, i].astype(np.int16) for i in range(3)]
    orange = (r > 100) & (g > 20) & (g < 190) & (b < 100) & (r > g + 35)
    dark = (r < 70) & (g < 70) & (b < 70)
    return orange | dark


def main() -> int:
    healthy = rgb(ROOT / "hull_redo_state0.png")
    print("IonLancer redo12 validation")
    print("=" * 112)
    print(f"{'file':28} {'cell':4} {'metric':48} {'value':10} result")
    print("-" * 112)
    failures = 0
    for state in (1, 2):
        name = f"hull_redo_state{state}_v2.png"
        sheet = rgb(ROOT / name)
        if sheet.shape != (2048, 3072, 3):
            print(f"{name:28} {'-':4} {'dimensions':48} {sheet.shape!s:10} FAIL")
            failures += 1
            continue
        cells = [sheet[(i // 3) * 1024:(i // 3 + 1) * 1024, (i % 3) * 1024:(i % 3 + 1) * 1024] for i in range(6)]
        healthy_cells = [healthy[(i // 3) * 1024:(i // 3 + 1) * 1024, (i % 3) * 1024:(i % 3 + 1) * 1024] for i in range(6)]
        keys = rgb(ROOT / f"key_state{state}.png")
        key_diff = [np.any(cells[i] != keys, axis=2) for i in range(3)]
        print(f"{name:28} {'0-2':4} {'dimensions':48} 3072x2048 PASS")
        for i, diff in enumerate(key_diff):
            value = pct(int(diff.sum()), diff.size)
            threshold = 0.4 if i == 0 else 0.8 if i == 1 else 0.0
            ok = value >= threshold and (i != 2 or not np.array_equal(cells[2], cells[1]))
            print(f"{name:28} {i:4} {'pixels differing from approved key':48} {value:8.3f}% {'PASS' if ok else 'FAIL'}")
            failures += not ok
        for i in (3, 4):
            ref_mask = np.any(healthy_cells[i] != GREEN, axis=2)
            out_mask = np.any(cells[i] != GREEN, axis=2)
            silhouette = pct(int(np.logical_xor(ref_mask, out_mask).sum()), int(ref_mask.sum()))
            ok = silhouette < 6.0
            print(f"{name:28} {i:4} {'bank silhouette difference vs healthy':48} {silhouette:8.3f}% {'PASS' if ok else 'FAIL'}")
            failures += not ok
            key_damage = int(damage_mask(keys).sum())
            bank_damage = int(damage_mask(cells[i]).sum())
            ratio = pct(bank_damage, key_damage)
            ok = ratio > 40.0
            print(f"{name:28} {i:4} {'damage-colour pixels vs key amount':48} {ratio:8.3f}% {'PASS' if ok else 'FAIL'}")
            failures += not ok
            solid = not np.any(np.all(cells[i] == np.array([255, 0, 255], dtype=np.uint8), axis=2))
            print(f"{name:28} {i:4} {'solid RGB / no magenta sentinel':48} {'PASS' if solid else 'FAIL':10} {'PASS' if solid else 'FAIL'}")
            failures += not solid
    print("=" * 112)
    print("semi-transparent-looking dark patch check: N/A (RGB sheets; all pixels opaque)")
    print(f"overall: {'PASS' if failures == 0 else 'FAIL'}")
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
