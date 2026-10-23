#!/usr/bin/env python3
"""Build and audit J1c damage poses.

The build step only resizes generated candidates with nearest-neighbour, copies
generated pixels whose colour change is above a candidate-specific threshold,
and clips the result to the approved source mask. It does not draw damage.
"""

from __future__ import annotations

import argparse
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


GREEN = np.array([0, 255, 0], dtype=np.uint8)
ROOT = Path(__file__).resolve().parent.parent
BASE_PATH = ROOT / "key_state0.png"
STATE1_PATH = ROOT / "key_state1.png"
STATE2_PATH = ROOT / "key_state2.png"
PREVIEW_PATH = ROOT / "preview_j1.png"


def read_rgb(path: Path) -> np.ndarray:
    image = Image.open(path).convert("RGB")
    if image.size != (1024, 1024):
        image = image.resize((1024, 1024), Image.Resampling.NEAREST)
    return np.asarray(image, dtype=np.uint8)


def build_pose(base: np.ndarray, candidate_path: Path, threshold: int) -> np.ndarray:
    candidate = read_rgb(candidate_path)
    source_ship = np.any(base != GREEN, axis=2)
    delta = np.max(np.abs(candidate.astype(np.int16) - base.astype(np.int16)), axis=2)
    candidate_green = (
        (candidate[:, :, 1] > 180)
        & ((candidate[:, :, 1].astype(np.int16) - candidate[:, :, 0].astype(np.int16)) > 100)
        & ((candidate[:, :, 1].astype(np.int16) - candidate[:, :, 2].astype(np.int16)) > 100)
    )
    generated_damage = (delta > threshold) & source_ship & ~candidate_green

    result = base.copy()
    result[generated_damage] = candidate[generated_damage]
    result[~source_ship] = GREEN
    return result


def write_rgb(path: Path, pixels: np.ndarray) -> None:
    Image.fromarray(pixels, mode="RGB").save(path)


def connected_regions(mask: np.ndarray, minimum: int = 100) -> list[np.ndarray]:
    height, width = mask.shape
    seen = np.zeros_like(mask, dtype=bool)
    regions: list[np.ndarray] = []
    for y, x in zip(*np.where(mask & ~seen)):
        if seen[y, x]:
            continue
        queue = deque([(int(y), int(x))])
        seen[y, x] = True
        points: list[tuple[int, int]] = []
        while queue:
            cy, cx = queue.popleft()
            points.append((cy, cx))
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    if not (dy or dx):
                        continue
                    ny, nx = cy + dy, cx + dx
                    if 0 <= ny < height and 0 <= nx < width and mask[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        queue.append((ny, nx))
        if len(points) >= minimum:
            regions.append(np.asarray(points, dtype=np.int32))
    return sorted(regions, key=len, reverse=True)


def percent(value: int, denominator: int) -> float:
    return 100.0 * value / max(1, denominator)


def audit(base: np.ndarray, state1: np.ndarray, state2: np.ndarray) -> bool:
    source_ship = np.any(base != GREEN, axis=2)
    source_bg = ~source_ship
    y, x = np.indices(source_ship.shape)

    def row(label: str, image: np.ndarray) -> tuple[np.ndarray, float]:
        ship = np.any(image != GREEN, axis=2)
        silhouette = np.logical_xor(source_ship, ship)
        changed = np.any(image != base, axis=2) & source_ship
        print(
            f"{label:12} silhouette={percent(int(silhouette.sum()), int(source_ship.sum())):6.3f}% "
            f"changed={percent(int(changed.sum()), int(source_ship.sum())):6.3f}% "
            f"left={percent(int((changed & (x < 512)).sum()), int(source_ship.sum())):6.3f}% "
            f"right={percent(int((changed & (x >= 512)).sum()), int(source_ship.sum())):6.3f}%"
        )
        return changed, percent(int(changed.sum()), int(source_ship.sum()))

    print("J1c validation")
    print("pose         silhouette  changed  left    right")
    changed1, change1 = row("state 1", state1)
    changed2, change2 = row("state 2", state2)
    pose_delta = np.any(state2 != state1, axis=2) & source_ship
    print(f"state2-v-s1  changed={percent(int(pose_delta.sum()), int(source_ship.sum())):6.3f}%")

    regions_ok = True
    print("distinct colours in changed regions (8-connected, >=100 px):")
    for label, image, changed in (("state 1", state1, changed1), ("state 2", state2, changed2)):
        regions = connected_regions(changed)
        counts = []
        for region in regions:
            colours = np.unique(image[region[:, 0], region[:, 1]], axis=0)
            counts.append(len(colours))
        if counts:
            print(f"  {label}: regions={len(counts)} min={min(counts)} max={max(counts)}")
        else:
            print(f"  {label}: none")
        regions_ok &= all(count > 20 for count in counts)

    silhouette1 = np.logical_xor(source_ship, np.any(state1 != GREEN, axis=2)).sum()
    silhouette2 = np.logical_xor(source_ship, np.any(state2 != GREEN, axis=2)).sum()
    checks = {
        "state1 silhouette < 0.5%": percent(int(silhouette1), int(source_ship.sum())) < 0.5,
        "state2 silhouette < 0.5%": percent(int(silhouette2), int(source_ship.sum())) < 0.5,
        "state1 4-14%": 4.0 < change1 < 14.0,
        "state2 12-45%": 12.0 < change2 < 45.0,
        "state2 vs state1 > 8%": percent(int(pose_delta.sum()), int(source_ship.sum())) > 8.0,
        "state2 left > 3%": percent(int((changed2 & (x < 512)).sum()), int(source_ship.sum())) > 3.0,
        "state2 right > 3%": percent(int((changed2 & (x >= 512)).sum()), int(source_ship.sum())) > 3.0,
        "state1 left > 1.5%": percent(int((changed1 & (x < 512)).sum()), int(source_ship.sum())) > 1.5,
        "state1 right > 1.5%": percent(int((changed1 & (x >= 512)).sum()), int(source_ship.sum())) > 1.5,
        "damage regions > 20 colours": regions_ok,
        "background exact #00FF00": bool(np.all(state1[source_bg] == GREEN) and np.all(state2[source_bg] == GREEN)),
        "no chroma-green on ship": all(
            not np.any(
                source_ship
                & (image[:, :, 1] > 180)
                & ((image[:, :, 1].astype(np.int16) - image[:, :, 0].astype(np.int16)) > 100)
                & ((image[:, :, 1].astype(np.int16) - image[:, :, 2].astype(np.int16)) > 100)
            )
            for image in (state1, state2)
        ),
    }
    print("checks")
    passed = True
    for name, result in checks.items():
        print(f"  {'PASS' if result else 'FAIL'}  {name}")
        passed &= result
    print("RESULT:", "PASS" if passed else "FAIL")
    return passed


def make_preview(base: np.ndarray, state1: np.ndarray, state2: np.ndarray) -> None:
    canvas = Image.new("RGB", (1536, 512), (11, 11, 26))
    for index, pixels in enumerate((base, state1, state2)):
        preview_pixels = pixels.copy()
        preview_pixels[np.all(preview_pixels == GREEN, axis=2)] = np.array([11, 11, 26], dtype=np.uint8)
        image = Image.fromarray(preview_pixels, mode="RGB").resize((512, 512), Image.Resampling.NEAREST)
        canvas.paste(image, (index * 512, 0))
    draw = ImageDraw.Draw(canvas)
    for index, label in enumerate(("KEY 0", "KEY 1", "KEY 2")):
        draw.text((16 + index * 512, 16), label, fill=(245, 253, 253))
    canvas.save(PREVIEW_PATH)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--build", action="store_true")
    parser.add_argument("--state1-candidate", type=Path)
    parser.add_argument("--state2-candidate", type=Path)
    parser.add_argument("--state1-threshold", type=int, default=64)
    parser.add_argument("--state2-threshold", type=int, default=192)
    parser.add_argument("--preview", action="store_true")
    args = parser.parse_args()

    base = read_rgb(BASE_PATH)
    if args.build:
        if not args.state1_candidate or not args.state2_candidate:
            parser.error("--build requires both candidate paths")
        state1 = build_pose(base, args.state1_candidate, args.state1_threshold)
        state2 = build_pose(base, args.state2_candidate, args.state2_threshold)
        write_rgb(STATE1_PATH, state1)
        write_rgb(STATE2_PATH, state2)
    else:
        state1 = read_rgb(STATE1_PATH)
        state2 = read_rgb(STATE2_PATH)

    if args.preview or args.build:
        make_preview(base, state1, state2)
    return 0 if audit(base, state1, state2) else 1


if __name__ == "__main__":
    raise SystemExit(main())
