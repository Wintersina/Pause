#!/usr/bin/env python3
"""Installs a repainted 256 x 256 ground drawing (e.g. Codex's landfall viaduct / causeway) into
its cell of a world's atlas, matched to the atlas so it sits with the drawings around it:

  - alpha kept binary (>= 128 opaque), as every ground cell;
  - its luminance distribution matched to the cell it replaces (Codex tends to paint brighter
    highlights), hue kept;
  - every colour snapped to the atlas's own ramp (the colours the OTHER cells already use;
    weighted nearest, as the build scripts do); with --ramp-max-sat N, colours more saturated
    than N (a world's neon lamp accents) are left out of the ramp and only cyan / magenta lamp
    pixels of the painting are mapped onto them.

    python3 install_cell.py <atlas.png> <cell name> <new cell.png> [--ramp-max-sat 60] [--keep-old <path>]
"""
import json
import sys

import numpy as np
from PIL import Image

LUM = np.array([.299, .587, .114], np.float32)
WTS = np.array([.9, 1.1, 1.15], np.float32)


def nearest(want, pal):
    out = np.empty((len(want), 3), np.float32)
    for i in range(0, len(want), 8192):
        ch = want[i:i + 8192]
        d = (((ch[:, None, :] - pal[None, :, :]) ** 2) * WTS).sum(2)
        out[i:i + 8192] = pal[d.argmin(1)]
    return out


def main():
    atlas_path, name, new_path = sys.argv[1], sys.argv[2], sys.argv[3]
    max_sat = float(sys.argv[sys.argv.index('--ramp-max-sat') + 1]) if '--ramp-max-sat' in sys.argv else None
    keep_old = sys.argv[sys.argv.index('--keep-old') + 1] if '--keep-old' in sys.argv else None
    atlas = np.asarray(Image.open(atlas_path).convert('RGBA')).copy()
    cells = json.load(open(atlas_path[:-4] + '.json'))['sprites']
    s = next(c for c in cells if c['n'] == name)
    top = atlas.shape[0] - s['y'] - s['h']
    sl = (slice(top, top + s['h']), slice(s['x'], s['x'] + s['w']))
    old = atlas[sl].copy()
    if keep_old:
        Image.fromarray(old, 'RGBA').save(keep_old)
    others = atlas.copy()
    others[sl] = 0
    pal = np.unique(others[others[..., 3] > 0][:, :3], axis=0).astype(np.float32)
    accents = np.zeros((0, 3), np.float32)
    if max_sat is not None:
        sat = pal.max(1) - pal.min(1)
        accents, pal = pal[sat > max_sat], pal[sat <= max_sat]

    new = np.asarray(Image.open(new_path).convert('RGBA')).astype(np.float32)
    alpha = np.where(new[..., 3] >= 128, 255, 0).astype(np.uint8)
    on = alpha > 0
    rgb = new[..., :3].copy()
    lamp_c = on & (rgb[..., 2] > rgb[..., 0] * 1.6) & (rgb[..., 1] > rgb[..., 0] * 1.3) & (rgb.max(2) > 120)
    lamp_m = on & (rgb[..., 0] > rgb[..., 1] * 1.4) & (rgb[..., 2] > rgb[..., 1] * 1.25) & (rgb.max(2) > 110)
    ref = old[old[..., 3] > 0][:, :3].astype(np.float32) @ LUM
    if len(ref) == 0:
        ref = others[others[..., 3] > 0][:, :3].astype(np.float32) @ LUM
    lum = rgb @ LUM
    order = np.argsort(np.argsort(lum[on]))
    target = np.sort(ref)[(order * (len(ref) - 1) / max(1, on.sum() - 1)).astype(int)]
    k = np.ones_like(lum)
    k[on] = target / np.maximum(lum[on], 1e-3)
    rgb = rgb * k[..., None]
    out = np.zeros(rgb.shape, np.uint8)
    out[on] = nearest(rgb[on], pal).astype(np.uint8)
    if len(accents):
        for mask, want in ((lamp_c, (46, 108, 152)), (lamp_m, (117, 75, 134))):
            out[mask] = accents[np.argmin(((accents - np.array(want, np.float32)) ** 2).sum(1))].astype(np.uint8)
    out[~on] = 0
    atlas[sl] = np.dstack((out, alpha))
    Image.fromarray(atlas, 'RGBA').save(atlas_path)
    print('installed %s at x %d, row %d (ramp %d colours, %d accents)' % (name, s['x'], top, len(pal), len(accents)))


if __name__ == '__main__':
    main()
