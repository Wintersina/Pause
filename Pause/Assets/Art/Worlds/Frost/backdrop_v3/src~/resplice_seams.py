#!/usr/bin/env python3
"""Re-splices the INSTALLED Frost ground tiles (v1..v4 sky / far / mid / flow) along
minimum-error cuts instead of the old Bayer-dithered cross-fade, keeping everything else.

Every Frost tile was finished by seam_splice (build.py, variants/src~/build.py): an offset copy
of the painting fills the edges (so the tile wraps) and the intact painting fills a centre cross,
the two cross-faded through ordered (Bayer 8x8) dither. Where the two copies show different
ground, that dither reads in game as two vertical dot-screen stripes (x ~180-215 and ~300-335)
and a horizontal one running down every layer at the same place. The build scripts now cut
between the copies along the path where they differ least (seam_mask); this applies that cut
to the tiles already installed WITHOUT re-quantizing them: only the pixels whose source changes
(old dithered choice != new cut choice) are rewritten, each with the tile's own colour ramp
(weighted nearest colour already in the tile), alpha from the build's alpha_for and the same
cyan / magenta beacon rule. All other pixels -- most of every tile -- stay exactly as approved.

    python3 resplice_seams.py <Resources/Worlds/Frost/Backdrop3> [--dry]
"""
import importlib.util
import os
import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def bayer_mask(m, src):
    W, H = m.W, m.H
    yy, xx = np.indices((H, W))
    dx = np.abs(xx - W // 2)
    dy = np.abs(yy - H // 2)
    wx = np.clip((92 - dx) / 72, 0, 1)
    wy = np.clip((144 - dy) / 112, 0, 1)
    probability = 1 - (1 - wx) * (1 - wy)
    return probability > m.BAYER8[yy % 8, xx % 8]


def accents(painted, layer):
    p = painted.astype(np.float32) / 255
    hi = p.max(axis=2)
    lo = p.min(axis=2)
    sat = (hi - lo) / np.maximum(hi, .001)
    cyan = (p[:, :, 2] > p[:, :, 0] * 1.45) & (p[:, :, 1] > p[:, :, 0] * 1.35) & (hi > .80) & (sat > .48)
    magenta = (p[:, :, 0] > p[:, :, 1] * 1.30) & (p[:, :, 2] > p[:, :, 1] * 1.30) & (hi > .50) & (sat > .35)
    c = (35, 81, 111) if layer == "sky" else (46, 108, 152)
    g = (88, 54, 104) if layer == "sky" else (117, 75, 134)
    return cyan, magenta, c, g


def resplice(m, tile_path, source_name, gain, layer, alpha_fn, dry):
    src = m.load_crop(source_name)
    rolled = np.roll(src, (m.H // 2, m.W // 2), axis=(0, 1))
    old = bayer_mask(m, src)
    new = m.seam_mask(src)
    diff = old != new
    tile = np.asarray(Image.open(tile_path).convert("RGBA")).copy()
    if dry or not diff.any():
        return diff.mean()
    painted = np.where(new[..., None], src, rolled)
    dark = np.clip(np.rint(painted.astype(np.float32) * gain), 0, 255).astype(np.float32)
    rgb = tile[..., :3]
    keep = ~diff
    pal = np.unique(rgb[keep].reshape(-1, 3), axis=0).astype(np.float32)
    sat = pal.max(1) - pal.min(1)
    pal = pal[sat <= 60] if (sat <= 60).sum() >= 4 else pal       # the ramp, not the beacon accents
    want = dark[diff]
    out = np.empty((len(want), 3), np.float32)
    wts = np.array([.9, 1.1, 1.15], np.float32)
    for i in range(0, len(want), 8192):
        ch = want[i:i + 8192]
        d = (((ch[:, None, :] - pal[None, :, :]) ** 2) * wts).sum(2)
        out[i:i + 8192] = pal[d.argmin(1)]
    rgb[diff] = out.astype(np.uint8)
    if layer != "flow":
        cyan, magenta, c, g = accents(painted, layer)
        rgb[diff & cyan] = c
        rgb[diff & magenta] = g
    alpha = alpha_fn(painted)
    tile[..., 3][diff] = alpha[diff]
    tile[..., :3] = rgb
    tile[:, -1] = tile[:, 0]
    tile[-1] = tile[0]
    Image.fromarray(tile, "RGBA").save(tile_path, optimize=True)
    return diff.mean()


def main():
    base = Path(sys.argv[1])
    dry = "--dry" in sys.argv
    v1 = load(HERE / "build.py", "frost_build_v1")
    for layer, cfg in v1.SETTINGS.items():
        share = resplice(v1, base / "v1" / f"{layer}.png", cfg["source"], cfg["gain"], layer,
                         lambda p, layer=layer: v1.alpha_for(layer, p), dry)
        print("v1 %-4s %.3f of the tile re-spliced" % (layer, share))
    vs = load(HERE.parent / "variants" / "src~" / "build.py", "frost_build_variants")
    for variant, conf in vs.SETS.items():
        for layer in vs.ORDER:
            source_name, gain, colors, _ = conf["tiles"][layer]
            share = resplice(vs, base / variant / f"{layer}.png", source_name, gain, layer,
                             lambda p, variant=variant, layer=layer: vs.alpha_for(variant, layer, p), dry)
            print("%s %-4s %.3f of the tile re-spliced" % (variant, layer, share))


if __name__ == "__main__":
    main()
