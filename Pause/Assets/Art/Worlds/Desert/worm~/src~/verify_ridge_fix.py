"""Verify the ridge-only revision against the preserved first-pass atlases."""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image

from verify import diff, hsv

ROOT = Path(__file__).resolve().parents[1]
OLD = ROOT / "src~" / "originals_ridge"


def cell_of(a: np.ndarray, cell: int, col: int, row: int) -> np.ndarray:
    return a[row * cell:(row + 1) * cell, col * cell:(col + 1) * cell]


def main() -> None:
    manifest = json.loads((ROOT / "manifest.json").read_text())
    measurements = {}
    changed_total = 0
    untouched_total = 0
    print("scale  fixed  unchanged  hump_width%  hump_height%  wake_px  sand_s95  amber%  shadow_tile%  loop_min  seam/adj  anchor")
    for scale, filename, cell, row, count in (
        ("s", "worm.png", 256, 0, 8),
        ("m", "worm.png", 256, 4, 8),
        ("l", "worm_giant.png", 512, 0, 4),
    ):
        now = np.asarray(Image.open(ROOT / filename).convert("RGBA"))
        old = np.asarray(Image.open(OLD / filename).convert("RGBA"))
        assert now.shape == old.shape == (2048, 2048, 4)
        changed = 0
        unchanged = 0
        frames = []
        rows = range(0, 4) if scale == "s" else range(4, 8) if scale == "m" else range(0, 4)
        for r in rows:
            for col in range(2048 // cell):
                before = cell_of(old, cell, col, r)
                after = cell_of(now, cell, col, r)
                target = r == row and col < count
                if target:
                    assert not np.array_equal(before, after), (scale, col, "ridge did not change")
                    frames.append(after)
                    changed += 1
                else:
                    assert before.tobytes() == after.tobytes(), (scale, col, r, "untouched cell changed")
                    unchanged += 1
        changed_total += changed
        untouched_total += unchanged
        # The mound has its own painted footprint; the wake extends below it.
        top, height = ((72, 86) if scale == "s" else (67, 91) if scale == "m" else (137, 176))
        widths, heights, wake_counts, anchor_offsets = [], [], [], []
        for frame in frames:
            opaque = frame[top:top + height, :, 3] > 64
            yy, xx = np.where(opaque)
            assert len(xx) > 0
            widths.append(int(xx.max() - xx.min() + 1))
            heights.append(int(yy.max() - yy.min() + 1))
            wake_counts.append(int((frame[top + height:cell - 15, :, 3] > 32).sum()))
            contact_x = np.where(frame[cell - 23:cell - 16, :, 3] > 96)[1]
            anchor_offsets.append(round(float(np.median(contact_x))) - cell // 2)
        width_pct = 100 * float(np.median(widths)) / cell
        height_pct = 100 * float(np.median(heights)) / cell
        assert 53 <= width_pct <= 68, (scale, "mound width", width_pct)
        assert 29 <= height_pct <= 41, (scale, "mound height", height_pct)
        assert min(wake_counts) >= 15, (scale, "wake too sparse", wake_counts)
        assert all(offset == 0 for offset in anchor_offsets), (scale, "anchor", anchor_offsets)
        steps = [diff(frames[i], frames[(i + 1) % count]) for i in range(count)]
        seam_ratio = steps[-1] / float(np.median(steps[:-1]))
        handoff_change = diff(frames[-1], cell_of(now, cell, 0, row + 1))
        assert min(steps) >= .04 and seam_ratio <= 2.25, (scale, "loop", steps)
        rgb = np.concatenate([f[..., :3].reshape(-1, 3) for f in frames])
        alpha = np.concatenate([f[..., 3].reshape(-1) for f in frames])
        h, s, v = hsv(rgb.reshape(-1, 1, 3))
        h, s, v = h[:, 0], s[:, 0], v[:, 0]
        mask = alpha > 32
        sand = mask & (h >= 40) & (h <= 48) & (s < .45)
        amber = mask & (h >= 30) & (h <= 48) & (s > .45)
        red = mask & ((h >= 345) | (h <= 15)) & (s > .5)
        pink = mask & (h >= 312) & (h <= 326) & (s > .25)
        violet = mask & (h >= 250) & (h <= 300) & (s > .2)
        green_cyan = mask & (h >= 70) & (h <= 205) & (s > .4)
        shadow = mask & (h >= 232) & (h <= 248) & (s > .20)
        sand95 = float(np.quantile(s[sand], .95))
        amber_pct = 100 * int(amber.sum()) / max(1, int(mask.sum()))
        shadow_tile_pct = 100 * int(shadow.sum()) / (count * cell * cell)
        assert sand95 <= .33 and amber_pct <= .3
        assert not (red.any() or pink.any() or violet.any() or green_cyan.any())
        assert shadow_tile_pct <= 25
        assert float(v[shadow].max()) <= .30 and float(s[shadow].max()) <= .45
        key = {"s": "small", "m": "medium", "l": "giant"}[scale]
        measurements[key] = {
            "changed_cells": changed,
            "untouched_cells_byte_identical": unchanged,
            "hump_width_percent_median": round(width_pct, 2),
            "hump_height_percent_median": round(height_pct, 2),
            "wake_pixels_min": min(wake_counts),
            "sand_saturation_p95": round(sand95, 4),
            "forbidden_amber_percent": round(amber_pct, 4),
            "shadow_percent_of_cell": round(shadow_tile_pct, 3),
            "red_pixels": int(red.sum()), "pink_pixels": int(pink.sum()),
            "violet_pixels": int(violet.sum()), "saturated_green_cyan_pixels": int(green_cyan.sum()),
            "loop_change_min": round(min(steps), 4),
            "loop_seam_to_adjacent_ratio": round(seam_ratio, 3),
            "ridge_to_preserved_emerge_change": round(handoff_change, 4),
            "anchor_offset_max": max(map(abs, anchor_offsets)),
        }
        print(f"{scale:5} {changed:6} {unchanged:10} {width_pct:11.1f} {height_pct:12.1f} {min(wake_counts):8} {sand95:9.3f} {amber_pct:7.3f} {shadow_tile_pct:12.2f} {min(steps):9.3f} {seam_ratio:9.2f} {max(map(abs, anchor_offsets)):7}")
        print(f"      ridge-to-preserved-emerge visual change: {handoff_change:.3f}")
    for name in ("worm.json", "worm_giant.json"):
        assert (ROOT / name).read_bytes() == (OLD / name).read_bytes(), name
    manifest["ridge_fix"]["measurements"] = measurements
    manifest["ridge_fix"]["untouched_cells_byte_identical"] = untouched_total
    manifest["ridge_fix"]["changed_cells"] = changed_total
    manifest["ridge_fix"]["caveat"] = "The preserved emergence start still contains the first-pass pointed ridge, so the sequence handoff is visually abrupt."
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"PASS: {changed_total} ridge cells changed; {untouched_total} other cells byte-identical; JSON mappings byte-identical")


if __name__ == "__main__":
    main()
