#!/usr/bin/env python3
"""Write the J1d three-state contact preview over #0b0b1a."""

from pathlib import Path

import numpy as np
from PIL import Image


GREEN = np.array([0, 255, 0], dtype=np.uint8)
BACKGROUND = (11, 11, 26)


def key_to_dark(path: Path) -> Image.Image:
    image = Image.open(path).convert("RGB")
    array = np.asarray(image).copy()
    array[np.all(array == GREEN, axis=2)] = BACKGROUND
    return Image.fromarray(array, "RGB")


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    names = ("key_state0.png", "approved_key_state1.png", "key_state2.png")
    canvas = Image.new("RGB", (1000, 372), BACKGROUND)
    for index, name in enumerate(names):
        thumb = key_to_dark(root / name).resize((320, 320), Image.Resampling.NEAREST)
        canvas.paste(thumb, (10 + index * 330, 26))
    canvas.save(root / "preview_j1.png")


if __name__ == "__main__":
    main()
