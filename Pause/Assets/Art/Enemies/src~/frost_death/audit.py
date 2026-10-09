"""Check the exported Frost death strips and preview timing."""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2] / "Resources" / "Enemies"
DEATH = ART / "Death"
KEYS = [
    "frost_alien", "frost_chaser", "frost_fighter_1", "frost_fighter_2",
    "frost_fighter_3", "frost_fighter_4", "frost_big",
    "frost_rock_chunk", "frost_rock_shard",
]


def centroid(rgba: np.ndarray) -> np.ndarray:
    weight = rgba[:, :, 3].astype(np.float64) / 255
    yy, xx = np.indices(weight.shape)
    return np.array([(xx * weight).sum(), (yy * weight).sum()]) / weight.sum()


def red_magenta_fraction(rgba: np.ndarray) -> float:
    rgb = rgba[:, :, :3].astype(np.float32) / 255
    mx, mn = rgb.max(axis=2), rgb.min(axis=2)
    diff = mx - mn
    sat = diff / np.maximum(mx, 1e-6)
    r, g, b = np.moveaxis(rgb, 2, 0)
    hue = np.zeros(mx.shape, dtype=np.float32)
    nz = diff > 1e-5
    rm = nz & (mx == r)
    gm = nz & (mx == g) & ~rm
    bm = nz & ~rm & ~gm
    hue[rm] = ((g[rm] - b[rm]) / diff[rm]) % 6
    hue[gm] = (b[gm] - r[gm]) / diff[gm] + 2
    hue[bm] = (r[bm] - g[bm]) / diff[bm] + 4
    hue *= 60
    visible = (rgba[:, :, 3] >= 128) & (sat >= .5) & (mx >= .2)
    forbidden = ((hue <= 15) | (hue >= 285)) & visible
    return float(forbidden.sum() / max(1, visible.sum()))


def main() -> int:
    failures = []
    for key in KEYS:
        idle = Image.open(ART / f"{key}.png").convert("RGBA")
        side = idle.height
        exported = Image.open(DEATH / f"{key}.png")
        expected = (side * 3, side)
        if exported.size != expected or exported.mode != "RGBA":
            failures.append(f"{key}: size/mode {exported.size} {exported.mode}")
            continue
        cells = [np.array(exported.crop((i*side, 0, (i+1)*side, side)))
                 for i in range(3)]
        old = np.array(idle.crop((3*side, 0, 4*side, side)))
        offset = float(np.linalg.norm(centroid(old) - centroid(cells[0])))
        if offset > 3:
            failures.append(f"{key}: frame-0 centroid drift {offset:.2f}px")
        border_hits = []
        for cell in cells:
            bright = (cell[:, :, :3].max(axis=2) >= 178) & (cell[:, :, 3] >= 96)
            border = np.zeros((side, side), dtype=bool)
            border[:6, :] = border[-6:, :] = True
            border[:, :6] = border[:, -6:] = True
            border_hits.append(int((bright & border).sum()))
        if any(border_hits):
            failures.append(f"{key}: bright pixels in six-pixel margin {border_hits}")
        hue_fraction = red_magenta_fraction(np.concatenate(cells, axis=1))
        if hue_fraction >= .05:
            failures.append(f"{key}: red/magenta {hue_fraction:.1%} of saturated pixels")
        print(f"{key:20} {expected[0]}x{side} RGBA  "
              f"anchor {offset:.2f}px  edge {border_hits}  "
              f"red/magenta {hue_fraction:.2%}")
    preview = Image.open(HERE / "preview.png")
    if preview.width < 8*256 or preview.height < 9*2*192:
        failures.append(f"preview.png too small: {preview.size}")
    gif = Image.open(HERE / "preview.gif")
    durations = []
    for i in range(gif.n_frames):
        gif.seek(i)
        durations.append(gif.info.get("duration"))
    if durations != [80, 110, 200]:
        failures.append(f"preview.gif durations {durations}")
    if failures:
        print("\nFAIL:\n" + "\n".join(failures), file=sys.stderr)
        return 1
    print(f"PASS: {len(KEYS)} strips; preview.png {preview.size}; "
          f"preview.gif {durations} ms")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
