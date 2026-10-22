#!/usr/bin/env python3
"""Bake the generated J1c damage passes into the approved VoltViper silhouette."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


GREEN = np.array([0, 255, 0], dtype=np.uint8)
SIZE = (1024, 1024)


def read_rgb(path: Path) -> np.ndarray:
    return np.array(Image.open(path).convert("RGB"))


def greenish(image: np.ndarray) -> np.ndarray:
    r, g, b = image.astype(np.int16).transpose(2, 0, 1)
    return (g > 100) & (g > 1.55 * r) & (g > 1.40 * b)


def nearest_to_canvas(path: Path) -> np.ndarray:
    image = Image.open(path).convert("RGB").resize(SIZE, Image.Resampling.NEAREST)
    return np.array(image)


def polygon_mask(points: list[tuple[int, int]]) -> np.ndarray:
    layer = Image.new("1", SIZE, 0)
    ImageDraw.Draw(layer).polygon(points, fill=1)
    return np.array(layer, dtype=bool)


def line_mask(lines: list[list[tuple[int, int]]], width: int) -> np.ndarray:
    layer = Image.new("1", SIZE, 0)
    draw = ImageDraw.Draw(layer)
    for line in lines:
        draw.line(line, fill=1, width=width, joint="curve")
    return np.array(layer, dtype=bool)


def state1_region() -> np.ndarray:
    region = polygon_mask([
        (565, 286), (622, 277), (690, 298), (754, 365),
        (760, 470), (731, 575), (683, 644), (610, 623),
        (573, 557), (556, 453),
    ])
    region |= polygon_mask([
        (315, 732), (388, 735), (445, 785), (443, 885),
        (362, 892), (324, 847),
    ])
    region |= line_mask([
        [(431, 382), (467, 415), (500, 449)],
        [(527, 557), (554, 593), (591, 624)],
        [(459, 659), (493, 684), (505, 719)],
    ], 15)
    return region


def state2_region() -> np.ndarray:
    region = state1_region()
    region |= polygon_mask([
        (42, 479), (95, 448), (168, 466), (223, 526),
        (210, 605), (146, 640), (74, 614), (40, 560),
    ])
    region |= polygon_mask([
        (705, 394), (791, 405), (891, 451), (975, 521),
        (971, 598), (901, 620), (810, 573), (743, 517),
    ])
    region |= polygon_mask([
        (561, 747), (656, 748), (696, 817), (675, 891),
        (597, 892), (562, 839),
    ])
    region |= polygon_mask([
        (454, 270), (578, 260), (643, 320), (635, 505),
        (472, 505), (438, 424),
    ])
    region |= polygon_mask([
        (248, 458), (391, 435), (456, 506), (440, 616),
        (278, 620), (233, 548),
    ])
    region |= polygon_mask([
        (390, 620), (658, 620), (676, 766), (615, 833),
        (430, 832), (375, 744),
    ])
    return region


def bake(base: np.ndarray, generated: np.ndarray, region: np.ndarray) -> np.ndarray:
    silhouette = np.any(base != GREEN, axis=2)
    source_ok = ~greenish(generated)
    use = silhouette & region & source_ok
    output = base.copy()
    output[use] = generated[use]
    output[~silhouette] = GREEN
    return output


def save_preview(images: list[np.ndarray], path: Path) -> None:
    preview = np.full((1024, 3 * 1024, 3), (11, 11, 26), dtype=np.uint8)
    for index, image in enumerate(images):
        tile = image.copy()
        tile[np.all(tile == GREEN, axis=2)] = (11, 11, 26)
        preview[:, index * 1024:(index + 1) * 1024] = tile
    Image.fromarray(preview, "RGB").save(path)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("state1_generated", type=Path)
    parser.add_argument("state2_generated", type=Path)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()

    root = args.root
    base = read_rgb(root / "key_state0.png")
    if base.shape != (1024, 1024, 3):
        raise ValueError(f"key_state0.png must be 1024x1024 RGB, got {base.shape}")

    generated1 = nearest_to_canvas(args.state1_generated)
    generated2 = nearest_to_canvas(args.state2_generated)
    state1 = bake(base, generated1, state1_region())
    state2 = bake(base, generated2, state2_region())

    Image.fromarray(state1, "RGB").save(root / "key_state1.png")
    Image.fromarray(state2, "RGB").save(root / "key_state2.png")
    save_preview([base, state1, state2], root / "preview_j1c.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
