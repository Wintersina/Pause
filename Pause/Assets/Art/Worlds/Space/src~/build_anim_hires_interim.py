#!/usr/bin/env python3
"""INTERIM 2x upscale of the Space planet sheet (anim.png) -> anim_hires.{png,json}.

    python3 build_anim_hires_interim.py [--out DIR]     (needs Pillow + numpy)

NOT new art: no detail is added. Lanczos 2x on premultiplied colour (so the
transparent surround can't fringe the rim dark) and a mild unsharp mask, so
the BackdropPlanet shader magnifies a smooth 2x image instead of bilinearly
stretching the 1x one. Codex's real high-resolution render replaces both
files (docs/art-production-queue.md, "Space giant planets + moon rotation
frames at high resolution"); delete this script then.

The manifest is anim.json's rects x2 with "pixelScale": 2 and the sheet size,
which BackdropAtlas reads so sprites keep the 1x bounds (same size on screen).
"""
import argparse
import json
import os

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
BACKDROP = os.path.join(HERE, "..", "..", "..", "Backgrounds", "Resources", "Worlds", "Space", "Backdrop")
SCALE = 2
UNSHARP = (1.2, 60, 2)      # radius, percent, threshold


def upscale(im):
    a = np.asarray(im.convert("RGBA")).astype(np.float64) / 255.0
    pm = a.copy()
    pm[..., :3] *= pm[..., 3:4]
    big = Image.fromarray((pm * 255.0 + 0.5).astype(np.uint8), "RGBA").resize(
        (im.width * SCALE, im.height * SCALE), Image.LANCZOS)
    rgb = big.convert("RGB").filter(ImageFilter.UnsharpMask(*UNSHARP))
    b = np.asarray(Image.merge("RGBA", (*rgb.split(), big.split()[3]))).astype(np.float64) / 255.0
    al = b[..., 3:4]
    b[..., :3] = np.where(al > 0, b[..., :3] / np.maximum(al, 1e-6), 0.0)
    b = np.clip(b, 0.0, 1.0)
    return Image.fromarray((b * 255.0 + 0.5).astype(np.uint8), "RGBA")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=BACKDROP)
    args = ap.parse_args()
    src = Image.open(os.path.join(BACKDROP, "anim.png"))
    with open(os.path.join(BACKDROP, "anim.json")) as f:
        manifest = json.load(f)
    out = upscale(src)
    os.makedirs(args.out, exist_ok=True)
    out.save(os.path.join(args.out, "anim_hires.png"), optimize=True)
    lines = ['{', '  "pixelScale": %d,' % SCALE,
             '  "sheetW": %d, "sheetH": %d,' % out.size, '  "sprites": [']
    rows = []
    for r in manifest["sprites"]:
        rows.append('    {"n":"%s","x":%d,"y":%d,"w":%d,"h":%d}' %
                    (r["n"], r["x"] * SCALE, r["y"] * SCALE, r["w"] * SCALE, r["h"] * SCALE))
    lines.append(",\n".join(rows))
    lines += ['  ]', '}', '']
    with open(os.path.join(args.out, "anim_hires.json"), "w") as f:
        f.write("\n".join(lines))
    print("anim_hires", out.size, len(rows), "cells ->", args.out)


if __name__ == "__main__":
    main()
