#!/usr/bin/env python3
"""Validate J2 flame sheets, palette purity, margins, and boost coverage."""

from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
W, H, FRAMES = 256, 1024, 7
GREEN = (0, 255, 0)
ALLOWED = {
    GREEN,
    (142, 132, 20),
    (238, 220, 50),
    (110, 242, 238),
    (244, 234, 212),
    (242, 134, 43),
}


def load(name: str) -> Image.Image:
    path = ROOT / name
    if not path.exists():
        raise AssertionError(f"missing {path}")
    im = Image.open(path).convert("RGB")
    if im.size != (W * FRAMES, H):
        raise AssertionError(f"{name}: expected {W * FRAMES}x{H}, got {im.width}x{im.height}")
    return im


def stats(im: Image.Image) -> list[dict[str, int | float]]:
    px = im.load()
    bad = {px[x, y] for y in range(H) for x in range(W * FRAMES)} - ALLOWED
    if bad:
        raise AssertionError(f"non-palette colors: {sorted(bad)[:8]}")
    result = []
    side = max(1, round(W * 0.08))
    for frame in range(FRAMES):
        x0 = frame * W
        lit = 0
        ys: list[int] = []
        for y in range(H):
            for x in range(W):
                c = px[x0 + x, y]
                if c != GREEN:
                    lit += 1
                    ys.append(y)
                    if x < side or x >= W - side:
                        raise AssertionError(f"frame {frame}: non-green pixel in outer 8% margin at x={x}")
        result.append({
            "lit": lit,
            "top": min(ys) if ys else -1,
            "bottom": max(ys) if ys else -1,
            "length_pct": ((max(ys) + 1) / H * 100.0) if ys else 0.0,
        })
    return result


def main() -> None:
    cruise = load("flame_cruise.png")
    boost = load("flame_boost.png")
    cruise_stats = stats(cruise)
    boost_stats = stats(boost)

    cruise_lit = sum(int(s["lit"]) for s in cruise_stats)
    boost_lit = sum(int(s["lit"]) for s in boost_stats)
    cruise_len = max(float(s["length_pct"]) for s in cruise_stats)
    boost_len = max(float(s["length_pct"]) for s in boost_stats)
    if boost_lit <= cruise_lit:
        raise AssertionError(f"boost lit-pixel count {boost_lit} is not greater than cruise {cruise_lit}")
    if boost_len <= cruise_len:
        raise AssertionError(f"boost length {boost_len:.2f}% is not greater than cruise {cruise_len:.2f}%")

    print("file          frame  lit-pixels  top  bottom  lit-length")
    for name, rows in (("cruise", cruise_stats), ("boost", boost_stats)):
        for i, row in enumerate(rows):
            print(f"{name:12} {i:5d} {int(row['lit']):11d} {int(row['top']):4d} {int(row['bottom']):7d} {float(row['length_pct']):8.2f}%")
    print(f"totals        cruise={cruise_lit} boost={boost_lit} | max length cruise={cruise_len:.2f}% boost={boost_len:.2f}%")
    print("PASS")


if __name__ == "__main__":
    main()
