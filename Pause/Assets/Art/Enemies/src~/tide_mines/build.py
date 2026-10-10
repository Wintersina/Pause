#!/usr/bin/env python3
"""Recell the image-painted Tide limpet mine; nearest-neighbour resampling only."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
SOURCE = HERE / "limpet_mine_painted_source.png"
OUTPUT = HERE / "rail_mines_neon_tide.png"
EDGES = (0, 313, 627, 940, 1254)


def clean_palette(im):
    a = np.array(im.convert("RGBA"))
    # Preserve the painted ramps; remap only explicit off-world hue breaches.
    hsv = np.array(im.convert("HSV"))
    h, s, v = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]
    active = a[:, :, 3] > 16
    red = active & (s > 115) & ((h < 11) | (h >= 244))
    lime = active & (s > 115) & (h >= 50) & (h < 93)
    cyan = active & (s > 120) & (h >= 129) & (h < 151)
    hsv[red, 0] = 18
    hsv[red, 1] = np.minimum(hsv[red, 1], 110)
    hsv[lime, 0] = 108
    hsv[cyan, 0] = 111
    rgb = np.array(Image.fromarray(hsv, "HSV").convert("RGB"))
    a[:, :, :3] = rgb
    a[a[:, :, 3] < 16, 3] = 0
    return Image.fromarray(a, "RGBA")


def make():
    source = Image.open(SOURCE).convert("RGBA")
    if source.size != (2172, 724):
        raise ValueError(f"Unexpected painted mine sheet: {source.size}")
    sheet = Image.new("RGBA", (1254, 314))
    for i in range(4):
        panel = source.crop((543*i, 0, 543*(i+1), 724))
        panel = clean_palette(panel)
        panel = panel.resize((299, 398), Image.Resampling.NEAREST)
        cell = Image.new("RGBA", (EDGES[i+1]-EDGES[i], 314))
        cell.paste(panel, (34 + (0, 0, -9, -17)[i], -42))
        # Stay clear of the cell outline without shrinking the authored body.
        arr = np.array(cell)
        arr[:6, :, 3] = 0; arr[-6:, :, 3] = 0
        arr[:, :6, 3] = 0; arr[:, -6:, 3] = 0
        sheet.alpha_composite(Image.fromarray(arr, "RGBA"), (EDGES[i], 0))
    sheet.save(OUTPUT)
    preview = Image.new("RGBA", (1254, 314), (7, 20, 25, 255))
    preview.alpha_composite(sheet)
    preview.convert("RGB").save(HERE / "preview.png")
    return OUTPUT


if __name__ == "__main__":
    print(make())
