"""Audit live Space elite strips against backed-up originals. Exit nonzero on failure."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
LIVE = HERE.parents[2] / "Resources/Elites/Space"
NAMES = ("eventide_bastion", "orbit_reaver", "rift_lancer", "singularity_hauler")
CELL = 192
BG_LUMA = (.2126 * 11 + .7152 * 11 + .0722 * 26) / 255
MAX_STRAIGHT_CUT = 29  # ceil(EnemyRosterTest.CutScar * 192)


def longest_straight_edge(mask: np.ndarray) -> int:
    worst = 0
    for x in range(CELL):
        for neighbour in (x - 1, x + 1):
            cut = mask[:, x] if neighbour < 0 or neighbour >= CELL else (
                mask[:, x] & ~mask[:, neighbour]
            )
            run = 0
            for pixel in cut:
                run = run + 1 if pixel else 0
                worst = max(worst, run)
    return worst


def cell_metrics(before: np.ndarray, after: np.ndarray) -> dict:
    old_mask = before[:, :, 3] >= 64
    new_mask = after[:, :, 3] >= 64
    intersection = np.count_nonzero(old_mask & new_mask)
    union = np.count_nonzero(old_mask | new_mask)
    iou = intersection / union
    old_y, old_x = np.nonzero(old_mask)
    new_y, new_x = np.nonzero(new_mask)
    centroid = float(np.hypot(old_x.mean() - new_x.mean(), old_y.mean() - new_y.mean()))
    ys, xs = np.nonzero(after[:, :, 3] > 0)
    margins = (int(xs.min()), int(191 - xs.max()), int(ys.min()), int(191 - ys.max()))
    pixels = after[:, :, :3][new_mask]
    distinct = len(np.unique(pixels, axis=0))
    changes_right = new_mask[:, :-1] & new_mask[:, 1:] & np.any(
        after[:, :-1, :3] != after[:, 1:, :3], axis=2
    )
    changes_down = new_mask[:-1, :] & new_mask[1:, :] & np.any(
        after[:-1, :, :3] != after[1:, :, :3], axis=2
    )
    boundary_map = np.zeros((CELL, CELL), dtype=bool)
    boundary_map[:, :-1] |= changes_right
    boundary_map[:-1, :] |= changes_down
    boundaries = int(np.count_nonzero(boundary_map))
    rgb = pixels.astype(np.float32)
    r, g, b = rgb[:, 0], rgb[:, 1], rgb[:, 2]
    red = int(np.count_nonzero((r > 80) & (r > g * 1.55) & (r > b * 1.55)
                               & ((b < r * .45) | (b < g * 1.4))))
    luminance = (.2126 * r + .7152 * g + .0722 * b) / 255
    standout = float(np.mean(luminance >= BG_LUMA + .12))
    return {
        "mask_iou": round(float(iou), 5),
        "centroid_px": round(centroid, 3),
        "margins_px_lrtb": margins,
        "distinct_rgb": distinct,
        "colour_boundaries": boundaries,
        "longest_straight_edge_px": longest_straight_edge(new_mask),
        "standout_share": round(standout, 4),
        "red_pixels": red,
        "alpha_identical": bool(np.array_equal(before[:, :, 3], after[:, :, 3])),
    }


def main() -> None:
    report = {"backdrop": "#0b0b1a", "standout_luma_delta": 0.12, "ships": {}}
    problems = []
    for name in NAMES:
        filename = f"space_elite_{name}.png"
        old_image = Image.open(HERE / "original" / filename).convert("RGBA")
        new_image = Image.open(LIVE / filename).convert("RGBA")
        if old_image.size != (1344, 192) or new_image.size != (1344, 192):
            problems.append(f"{name}: strip size")
            continue
        metrics = []
        for pose in range(7):
            box = (pose * CELL, 0, (pose + 1) * CELL, CELL)
            old = np.asarray(old_image.crop(box))
            new = np.asarray(new_image.crop(box))
            m = cell_metrics(old, new)
            metrics.append(m)
            label = f"{name} cell {pose}"
            if m["mask_iou"] < .93 or m["centroid_px"] > 2 or not m["alpha_identical"]:
                problems.append(f"{label}: geometry/alpha")
            if min(m["margins_px_lrtb"]) < 6:
                problems.append(f"{label}: margin")
            if m["distinct_rgb"] < 300:
                problems.append(f"{label}: too few colours")
            if m["colour_boundaries"] < 3000:
                problems.append(f"{label}: too few colour boundaries")
            if m["longest_straight_edge_px"] >= MAX_STRAIGHT_CUT:
                problems.append(f"{label}: straight edge")
            if m["standout_share"] < .12:
                problems.append(f"{label}: too little contrast")
            if m["red_pixels"]:
                problems.append(f"{label}: red pixels")
        report["ships"][name] = metrics
    report["problems"] = problems
    (HERE / "audit.json").write_text(json.dumps(report, indent=2) + "\n")
    for name, cells in report["ships"].items():
        print(f"{name}: min IoU={min(c['mask_iou'] for c in cells):.3f}, "
              f"max drift={max(c['centroid_px'] for c in cells):.2f}px, "
              f"min colours={min(c['distinct_rgb'] for c in cells)}, "
              f"min boundaries={min(c['colour_boundaries'] for c in cells)}, "
              f"max straight edge={max(c['longest_straight_edge_px'] for c in cells)}px, "
              f"min standout={min(c['standout_share'] for c in cells):.1%}, "
              f"red={sum(c['red_pixels'] for c in cells)}")
    if problems:
        raise SystemExit("Audit failed: " + "; ".join(problems))
    print("All 28 cells passed.")


if __name__ == "__main__":
    main()
