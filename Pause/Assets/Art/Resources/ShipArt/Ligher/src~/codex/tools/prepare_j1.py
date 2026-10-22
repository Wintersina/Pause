#!/usr/bin/env python3
"""Nearest-neighbour normalization of generated J1c candidates.

This helper does not paint damage. It only keys the generated background, fits each
candidate into the approved key_state0 bounds, and enforces the approved silhouette.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


GREEN = np.array([0, 255, 0], dtype=np.uint8)
CAVITY = np.array([0x1A, 0x0E, 0x0A], dtype=np.uint8)


def generated_green(arr: np.ndarray) -> np.ndarray:
    r, g, b = arr[..., 0].astype(np.int16), arr[..., 1].astype(np.int16), arr[..., 2].astype(np.int16)
    return (g >= 150) & (g >= r * 1.35) & (g >= b * 1.35)


def approved_mask(path: Path) -> tuple[np.ndarray, tuple[int, int, int, int]]:
    arr = np.asarray(Image.open(path).convert("RGB"))
    mask = np.any(arr != GREEN, axis=2)
    ys, xs = np.where(mask)
    box = (int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1))
    return mask, box


def normalize(candidate: Path, key0: Path) -> Image.Image:
    source = np.asarray(Image.open(candidate).convert("RGB"))
    subject_mask = ~generated_green(source)
    ys, xs = np.where(subject_mask)
    if not len(xs):
        raise ValueError(f"candidate has no keyed subject: {candidate}")
    source_crop = Image.fromarray(source[ys.min() : ys.max() + 1, xs.min() : xs.max() + 1])

    mask, (x0, y0, x1, y1) = approved_mask(key0)
    fitted = source_crop.resize((x1 - x0, y1 - y0), Image.Resampling.NEAREST)
    fitted_arr = np.asarray(fitted).copy()

    # Green accidentally painted inside the approved silhouette becomes the exact cavity colour.
    fitted_arr[generated_green(fitted_arr)] = CAVITY
    out = np.empty((1024, 1024, 3), dtype=np.uint8)
    out[:] = GREEN
    out[y0:y1, x0:x1] = fitted_arr
    out[~mask] = GREEN
    out[mask & generated_green(out)] = CAVITY
    return Image.fromarray(out, "RGB")


def make_preview(images: list[Image.Image], out_path: Path):
    canvas = Image.new("RGB", (1000, 372), (11, 11, 26))
    for i, image in enumerate(images):
        arr = np.asarray(image).copy()
        arr[generated_green(arr)] = np.array((11, 11, 26), dtype=np.uint8)
        thumb = Image.fromarray(arr).resize((320, 320), Image.Resampling.NEAREST)
        canvas.paste(thumb, (10 + i * 330, 26))
    canvas.save(out_path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--key0", type=Path, required=True)
    ap.add_argument("--state1-source", type=Path, required=True)
    ap.add_argument("--state2-source", type=Path, required=True)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--preview", type=Path, required=True)
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    key0 = Image.open(args.key0).convert("RGB")
    state1 = normalize(args.state1_source, args.key0)
    state2 = normalize(args.state2_source, args.key0)
    state1.save(args.out / "key_state1.png")
    state2.save(args.out / "key_state2.png")
    make_preview([key0, state1, state2], args.preview)


if __name__ == "__main__":
    main()
