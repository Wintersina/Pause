#!/usr/bin/env python3
"""Rebuild Desert's five ground atlases (landmarks, pipes, fires, sites, weather) at TRUE 256 px-per-cell
resolution. build_b.py authored each sprite at half size (nearest) and doubled it; here the same
colour cleaning / cropping / centring is kept but the source is resampled once (Lanczos on
premultiplied colour) straight to the final 220 / 224 px size.

    python3 build_hires.py <out dir>
"""
import importlib.util, sys
from pathlib import Path
import numpy as np
from PIL import Image

here = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("build_b", here / "build_b.py")
b = importlib.util.module_from_spec(spec); spec.loader.exec_module(b)


def resample(img, size):
    a = np.asarray(img.convert("RGBA")).astype(np.float32)
    a[..., :3] *= a[..., 3:4] / 255
    o = np.stack([np.asarray(Image.fromarray(a[..., i], "F").resize(size, Image.Resampling.LANCZOS)) for i in range(4)], -1)
    alpha = np.clip(o[..., 3], 0, 255)
    rgb = np.where(alpha[..., None] > 0, o[..., :3] * 255 / np.maximum(alpha[..., None], 1), 0)
    out = np.dstack((np.clip(rgb, 0, 255), alpha)).round().astype(np.uint8)
    out[out[..., 3] < 6] = 0
    return Image.fromarray(out, "RGBA")


def extract_cell(source, atlas, name, index, rows):
    w, h = source.size
    col, row = index % 4, index // 4
    box = (round(col * w / 4), round(row * h / rows), round((col + 1) * w / 4), round((row + 1) * h / rows))
    piece = b.clean_colors(source.crop(box), atlas, name)
    arr = np.asarray(piece).copy()
    hh, ww = arr.shape[:2]
    yy, xx = np.indices((hh, ww))
    edge = np.minimum.reduce((xx, yy, ww - 1 - xx, hh - 1 - yy))
    if atlas == "weather":
        arr[:, :, 3] = (np.floor(arr[:, :, 3] * np.clip(edge / 13.0, 0, 1) / 8) * 8).astype(np.uint8)
    else:
        arr[:, :, 3][edge < 2] = 0
    piece = Image.fromarray(arr, "RGBA")
    bbox = piece.getchannel("A").getbbox()
    piece = piece.crop(bbox)
    target = 224 if atlas == "weather" else 220          # was 112 / 110 doubled
    scale = min(target / piece.width, target / piece.height)
    sprite = resample(piece, (max(1, round(piece.width * scale)), max(1, round(piece.height * scale))))
    cell = Image.new("RGBA", (b.CELL, b.CELL))
    cell.alpha_composite(sprite, ((b.CELL - sprite.width) // 2, (b.CELL - sprite.height) // 2))
    return cell


if __name__ == "__main__":
    out = Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
    for atlas, (rows, names) in b.SPECS.items():
        source = Image.open(b.SRC / f"{b.SOURCE[atlas]}_source_a.png").convert("RGBA")
        canvas = Image.new("RGBA", (1024, 1024))
        for i, name in enumerate(names):
            canvas.alpha_composite(extract_cell(source, atlas, name, i, rows), ((i % 4) * 256, (i // 4) * 256))
        canvas.save(out / f"{atlas}.png")
        print("built", atlas)
