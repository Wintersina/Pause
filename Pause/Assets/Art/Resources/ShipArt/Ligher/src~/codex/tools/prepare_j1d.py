#!/usr/bin/env python3
"""Key a generated J1d wreck to the approved Ligher silhouette."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


GREEN = np.array([0, 255, 0], dtype=np.uint8)
CAVITY = np.array([0x1A, 0x0E, 0x0A], dtype=np.uint8)


def generated_mask(rgb: np.ndarray) -> np.ndarray:
    """Chroma-key the generator's green backdrop, including its near-green drift."""
    r, g, b = rgb[..., 0].astype(np.int16), rgb[..., 1].astype(np.int16), rgb[..., 2].astype(np.int16)
    return ~((g > 150) & (g > r * 1.35) & (g > b * 1.35))


def green_like(rgb: np.ndarray) -> np.ndarray:
    """Catch exact and near-green chroma-key spill inside the subject."""
    r, g, b = rgb[..., 0].astype(np.int16), rgb[..., 1].astype(np.int16), rgb[..., 2].astype(np.int16)
    return (g > 90) & (g > r * 1.35) & (g > b * 1.35)


def bbox(mask: np.ndarray) -> tuple[int, int, int, int]:
    ys, xs = np.where(mask)
    if not len(xs):
        raise ValueError("generated candidate has no keyed subject")
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--intact", type=Path, default=Path(__file__).resolve().parents[1] / "key_state0.png")
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "key_state2.png")
    args = parser.parse_args()

    intact = np.asarray(Image.open(args.intact).convert("RGB"), dtype=np.uint8)
    silhouette = np.any(intact != GREEN, axis=2)
    source = np.asarray(Image.open(args.candidate).convert("RGB"), dtype=np.uint8)
    subject_box = bbox(generated_mask(source))
    subject = Image.fromarray(source, "RGB").crop(subject_box)

    ys, xs = np.where(silhouette)
    target_box = (int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1))
    target_size = (target_box[2] - target_box[0], target_box[3] - target_box[1])
    subject = subject.resize(target_size, Image.Resampling.NEAREST)

    canvas = np.full_like(intact, GREEN)
    canvas[target_box[1] : target_box[3], target_box[0] : target_box[2]] = np.asarray(subject)

    # The approved key is the gameplay silhouette contract. Nothing outside it survives.
    canvas[~silhouette] = GREEN
    canvas[silhouette & green_like(canvas)] = CAVITY
    Image.fromarray(canvas, "RGB").save(args.output)


if __name__ == "__main__":
    main()
