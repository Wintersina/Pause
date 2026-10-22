#!/usr/bin/env python3
"""Validate the J1d wreck against the approved Ligher silhouette contract."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


GREEN = np.array([0, 255, 0], dtype=np.uint8)


def load(path: Path) -> tuple[np.ndarray, np.ndarray]:
    image = Image.open(path).convert("RGB")
    array = np.asarray(image)
    return array, np.any(array != GREEN, axis=2)


def changed_percent(a: np.ndarray, b: np.ndarray, region: np.ndarray) -> float:
    changed = np.any(a != b, axis=2)
    return float(changed[region].mean() * 100.0)


def side_region(silhouette: np.ndarray, left: bool) -> np.ndarray:
    height, width = silhouette.shape
    region = silhouette.copy()
    if left:
        region[:, width // 2 :] = False
    else:
        region[:, : width // 2] = False
    return region


def green_like(rgb: np.ndarray) -> np.ndarray:
    r, g, b = rgb[..., 0].astype(np.int16), rgb[..., 1].astype(np.int16), rgb[..., 2].astype(np.int16)
    return (g > 90) & (g > r * 1.35) & (g > b * 1.35)


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    paths = {
        "state0": root / "key_state0.png",
        "approved_state1": root / "approved_key_state1.png",
        "state2": root / "key_state2.png",
    }
    arrays = {}
    masks = {}
    for name, path in paths.items():
        arrays[name], masks[name] = load(path)
        if arrays[name].shape != (1024, 1024, 3):
            raise SystemExit(f"{name}: expected 1024x1024 RGB, got {arrays[name].shape}")

    m0 = masks["state0"]
    m2 = masks["state2"]
    silhouette_diff = float(np.logical_xor(m0, m2).mean() * 100.0)
    changed_vs_state0 = changed_percent(arrays["state0"], arrays["state2"], m0)
    changed_vs_approved = changed_percent(arrays["approved_state1"], arrays["state2"], m0)
    left = changed_percent(arrays["state0"], arrays["state2"], side_region(m0, left=True))
    right = changed_percent(arrays["state0"], arrays["state2"], side_region(m0, left=False))
    outside_is_green = bool(np.all(arrays["state2"][~m0] == GREEN))
    green_inside = bool(np.any(green_like(arrays["state2"]) & m0))

    print("J1d validation")
    print(f"silhouette diff vs key_state0        {silhouette_diff:7.3f}%")
    print(f"silhouette pixels changed vs state0  {changed_vs_state0:7.3f}%")
    print(f"silhouette pixels changed vs state1  {changed_vs_approved:7.3f}%")
    print(f"left-half changed vs state0           {left:7.3f}%")
    print(f"right-half changed vs state0          {right:7.3f}%")
    print(f"outside silhouette is #00FF00         {outside_is_green}")
    print(f"green-like pixels inside silhouette   {green_inside}")

    ok = (
        silhouette_diff == 0.0
        and changed_vs_state0 > 18.0
        and changed_vs_approved > 8.0
        and left > 8.0
        and right > 8.0
        and outside_is_green
        and not green_inside
    )
    if not ok:
        raise SystemExit("J1d validation: FAIL")
    print("J1d validation: PASS")


if __name__ == "__main__":
    main()
