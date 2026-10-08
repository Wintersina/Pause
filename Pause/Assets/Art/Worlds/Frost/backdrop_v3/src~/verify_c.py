"""Validate run C atlas contracts, transparency, and cyclic frame continuity."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
FAMILIES = {
    "smoke": {"smoke_a": 8, "smoke_b": 8},
    "steam": {"steam_vent": 8, "geyser": 8},
    "fire_lights": {"flare": 8, "searchlight": 8},
    "beacons": {"beacon_magenta": 4, "beacon_cyan": 4,
                "strobe_white": 4, "window_lights": 4},
    "aurora": {"aurora": 16},
}
ALPHA_CAP = {"smoke_a": 150, "smoke_b": 135, "steam_vent": 150,
             "geyser": 170, "searchlight": 175, "aurora": 40}


def check() -> None:
    manifest = json.loads((ROOT / "manifest.json").read_text())
    assert all(k in manifest for k in ("world", "view", "files", "run_b", "run_c"))
    listed = {v["name"]: v for v in manifest["run_c"]["loops"]}
    for family, expected in FAMILIES.items():
        path = ROOT / f"{family}.png"
        im = Image.open(path)
        assert im.mode == "RGBA" and im.size == (1024, 1024), path
        a = np.asarray(im)
        assert (a[:, :, 3] == 0).any() and (a[:, :, 3] > 0).any(), family
        rects = json.loads((ROOT / f"{family}.json").read_text())["sprites"]
        assert len(rects) == 16 and len({r["n"] for r in rects}) == 16, family
        covered = np.zeros((4, 4), bool)
        loops: dict[str, list[np.ndarray]] = {key: [] for key in expected}
        for i, rect in enumerate(rects):
            assert set(rect) == {"n", "x", "y", "w", "h"}
            assert rect["w"] == rect["h"] == 256
            x, y = rect["x"], rect["y"]
            assert 0 <= x <= 768 and 0 <= y <= 768 and x % 256 == 0 and y % 256 == 0
            row, col = (1024-y-256)//256, x//256
            assert not covered[row, col], (family, i)
            covered[row, col] = True
            assert (row, col) == divmod(i, 4), (family, i)
            frame = a[row*256:(row+1)*256, col*256:(col+1)*256]
            ys, xs = np.where(frame[:, :, 3] > 0)
            assert len(xs), rect["n"]
            margin = min(xs.min(), ys.min(), 255-xs.max(), 255-ys.max())
            assert margin >= 6, (rect["n"], margin)
            name, suffix = rect["n"].rsplit("_", 1)
            assert name in expected and suffix == f"{len(loops[name]):02d}", rect["n"]
            assert frame[:, :, 3].max() <= ALPHA_CAP.get(name, 255), rect["n"]
            loops[name].append(frame)
        assert covered.all(), family
        for name, count in expected.items():
            frames = loops[name]
            assert len(frames) == count
            colors = np.unique(np.concatenate([f[:, :, :3][f[:, :, 3] > 0] for f in frames]), axis=0)
            assert len(colors) <= 16, (name, len(colors))
            diffs = np.array([np.abs(frames[i].astype(np.int16)-frames[(i+1)%count].astype(np.int16)).mean()
                              for i in range(count)])
            assert (diffs > .015).all(), (name, diffs)
            assert diffs[-1] <= 2.15*np.median(diffs[:-1]), (name, diffs)
            assert listed[name]["frames"] == [f"{name}_{i:02d}" for i in range(count)]
            assert listed[name]["anchor_px_from_top"] == ([128, 128] if name == "aurora" else [128, 236])
            assert 0 < listed[name]["recommended_draw_alpha"] <= 1
            print(f"{name:17} {count:2} frames, {len(colors):2} RGB tones, "
                  f"wrap/median {diffs[-1]/np.median(diffs[:-1]):.2f}")
    assert Image.open(ROOT / "preview_c.png").size == (2048, 3384)
    gif = Image.open(ROOT / "preview_c.gif")
    assert gif.size == (600, 1300) and gif.n_frames == 16
    print("All run C atlas, loop, preview and manifest checks passed.")


if __name__ == "__main__":
    check()
