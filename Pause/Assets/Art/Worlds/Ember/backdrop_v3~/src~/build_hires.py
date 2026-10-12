#!/usr/bin/env python3
"""Rebuild Ember's four ground atlases (landmarks, pipes, fires, sites) at TRUE 256 px-per-cell resolution.

The run-B build (build_b.py) cut each painting to half size with nearest-neighbour and doubled it back,
so the shipped 1024 atlases were 512-native pixel doubles. This keeps its cropping, fitting, palette
quantising, hue guard and layout, but resamples the painting once (area/Lanczos on premultiplied
colour) straight to the final size and does not double.

    python3 build_hires.py <out dir>        # writes landmarks/pipes/fires/sites .png + .json there
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
    chans = [Image.fromarray(a[..., i], "F").resize(size, Image.Resampling.LANCZOS) for i in range(4)]
    o = np.stack([np.asarray(c) for c in chans], -1)
    alpha = np.clip(o[..., 3], 0, 255)
    rgb = np.where(alpha[..., None] > 0, o[..., :3] * 255 / np.maximum(alpha[..., None], 1), 0)
    return Image.fromarray(np.dstack((np.clip(rgb, 0, 255), alpha)).round().astype(np.uint8), "RGBA")


def fitted_sprite(source, family, name, bounds=(256, 256), common_scale=None):
    alpha = np.asarray(source.getchannel("A"))
    yy, xx = np.where(alpha >= (28 if family == "weather" else 95))
    cut = source.crop((int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1))
    bw, bh = bounds
    scale = common_scale or min((bw - 28) / cut.width, (bh - 28) / cut.height)
    size = (max(1, int(cut.width * scale)), max(1, int(cut.height * scale)))
    painted = b.pixel_clean(resample(cut, size), family, name)
    canvas = Image.new("RGBA", bounds)
    canvas.alpha_composite(painted, ((bw - painted.width) // 2, (bh - painted.height) // 2))
    return canvas


if __name__ == "__main__":
    out = Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
    b.ROOT = out
    b.fitted_sprite = fitted_sprite
    for fam in ("landmarks", "pipes", "fires", "sites", "weather"):
        b.build_atlas(fam)
        print("built", fam)
