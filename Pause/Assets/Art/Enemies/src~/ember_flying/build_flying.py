#!/usr/bin/env python3
"""Ember flying redesigns of the two ground-looking units (Cinder Fang, Scorch): Codex concept sheets -> seven-cell strips.

Reuses the motion / tell / hit machinery of ../ember_refresh/build.py, but keeps the Codex colours as painted (no temper, no pale rim,
no wear lift: those steered everything to one orange/ash palette). Column 1 of each *_flying_candidates.png is used.

    python3 build_flying.py        # writes ./strips/ember_chaser.png and ember_fighter_2.png (1344x192)
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "ember_refresh"))
import build as B

SPEC = {"ember_chaser": (1, 156), "ember_fighter_2": (1, 148)}
OUT = HERE / "strips"


def source(name, col):
    im = Image.open(HERE / f"{name}_flying_candidates.png").convert("RGBA")
    w = im.width // 3
    im = im.crop((col * w, 0, (col + 1) * w, im.height))
    # the neighbouring concept's wing tips can bleed into this column: keep only the main (dilated) connected island
    from PIL import ImageFilter
    near = Image.fromarray(((np.asarray(im.getchannel("A")) > 20) * 255).astype("uint8")).filter(ImageFilter.MaxFilter(9))
    keep = B.largest(np.asarray(near) > 0)
    arr = np.array(im)
    arr[~keep, 3] = 0
    im = Image.fromarray(arr, "RGBA")
    a = np.asarray(im.getchannel("A"))
    yy, xx = np.where(a > 128)
    return im.crop((max(0, xx.min() - 3), max(0, yy.min() - 3), min(im.width, xx.max() + 4), min(im.height, yy.max() + 4)))


def key_pose(name, col, target):
    im = source(name, col)
    f = target / max(im.size)
    im = im.resize(tuple(max(1, round(v * f)) for v in im.size), Image.Resampling.NEAREST)
    a = np.array(im)
    a[:, :, 3] = np.where(a[:, :, 3] > 110, 255, 0)
    im = Image.fromarray(a, "RGBA")
    canvas = Image.new("RGBA", (192, 192))
    canvas.alpha_composite(im, ((192 - im.width) // 2, (192 - im.height) // 2))
    return canvas


def main():
    OUT.mkdir(exist_ok=True)
    for name, (col, target) in SPEC.items():
        key = key_pose(name, col, target)
        frames = [B.cycle(B.motion(key, name, p, i), i, name) for i, p in enumerate(B.PHASE)]
        frames.extend((B.tell(key, name, 4), B.tell(key, name, 5), B.hit(key, name)))
        sheet = Image.new("RGBA", (192 * 7, 192))
        for i, f in enumerate(frames):
            sheet.alpha_composite(B.nick_flat_edges(f), (i * 192, 0))
        sheet.save(OUT / f"{name}.png", optimize=True)
        print(name, sheet.size)


if __name__ == "__main__":
    main()
