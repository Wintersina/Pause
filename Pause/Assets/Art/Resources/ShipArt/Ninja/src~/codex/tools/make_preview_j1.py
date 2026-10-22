#!/usr/bin/env python3
"""Make a dark, keyed-out side-by-side preview of the three poses."""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
BG = (11, 11, 26)
CANVAS = Image.new("RGB", (3072, 1024), BG)

for index, name in enumerate(("key_state0.png", "key_state1.png", "key_state2.png")):
    image = Image.open(ROOT / name).convert("RGB")
    pixels = image.load()
    for y in range(1024):
        for x in range(1024):
            if pixels[x, y] == (0, 255, 0):
                pixels[x, y] = BG
    CANVAS.paste(image, (index * 1024, 0))

CANVAS.save(ROOT / "preview_j1.png", format="PNG", optimize=False)
