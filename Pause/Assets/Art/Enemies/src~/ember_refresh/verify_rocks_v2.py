#!/usr/bin/env python3
"""Audit the two revised Ember rocks and print native frame-zero rock IoUs."""
from pathlib import Path
import sys
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
STAGE = HERE.parent
ART = STAGE.parents[2]
sys.path.insert(0, str(STAGE))
import verify as common

NAMES = ('ember_rock_magma', 'ember_rock_cinder')


def silhouette(path):
    a = np.asarray(Image.open(path).convert('RGBA'))
    assert a.shape[0] == 192 and a.shape[1] >= 192, (path, a.shape)
    return a[:192, :192, 3] > 128


def detail(c):
    op = c[:, :, 3] > 128
    rgb = (c[:, :, 0].astype(np.int32) << 16) | (c[:, :, 1].astype(np.int32) << 8) | c[:, :, 2]
    tones = len(np.unique(rgb[op]))
    right = (rgb[:, :-1] != rgb[:, 1:]) & op[:, :-1] & op[:, 1:]
    down = (rgb[:-1, :] != rgb[1:, :]) & op[:-1, :] & op[1:, :]
    edge = np.zeros_like(op)
    edge[:, :-1] |= right
    edge[:-1, :] |= down
    return tones, int(edge.sum())


def preview(sheets, tiles):
    cell = 384
    out = Image.new('RGB', (7 * cell, 4 * cell + 36), common.BG)
    for row, name in enumerate(NAMES):
        strip = sheets[name]
        y = row * (2 * cell + 12)
        up = strip.resize((strip.width * 2, strip.height * 2), Image.Resampling.NEAREST)
        out.paste(up, (0, y), up)
        key = strip.crop((0, 0, 192, 192))
        for i, tile in enumerate(tiles):
            x = max(0, min(tile.width - 192, (tile.width - 192) // 3 + i * 27))
            ty = max(0, min(tile.height - 192, (tile.height - 192) // 3 + i * 91))
            patch = tile.crop((x, ty, x + 192, ty + 192)).convert('RGB')
            patch.paste(key, (0, 0), key)
            out.paste(patch.resize((cell, cell), Image.Resampling.NEAREST),
                      (i * cell, y + cell))
    out.save(STAGE / 'rock_v2_preview.png', optimize=True)


def main():
    tiles = [Image.open(common.TILES / f'v{i}/mid.png').convert('RGB')
             for i in range(1, 5)]
    means = [float(common.luminance(np.asarray(t)).mean()) for t in tiles]
    print('Ember mid mean luminance:', '/'.join(f'{v:.1f}' for v in means))
    print('name                  tones bounds cut drift area range  idle motion %                 margin red pink amber other contrast % v1/v2/v3/v4')
    sheets = {}
    fails = []
    for name in NAMES:
        im = Image.open(HERE / f'{name}.png').convert('RGBA')
        sheets[name] = im
        a = np.asarray(im)
        cells = [a[:, i * 192:(i + 1) * 192] for i in range(7)]
        key = cells[0]
        tones, bounds = detail(key)
        center, area0 = common.centroid_area(key)
        drift = max(float(np.linalg.norm(common.centroid_area(c)[0] - center))
                    for c in cells[1:4])
        areas = [common.centroid_area(c)[1] / area0 for c in cells[:4]]
        motions = [common.motion(cells[i], cells[(i + 1) % 4]) for i in range(4)]
        margins = [common.margin(c) for c in cells]
        cut = max(common.straight_cut(c) for c in cells)
        p = common.palette(a)
        lum = common.luminance(key)[key[:, :, 3] > 128]
        contrast = [100 * np.mean(lum >= 3 * m) for m in means]
        checks = {'dimensions': im.size == (1344, 192), 'colours': tones >= 300,
                  'boundaries': bounds >= 3000, 'cut': cut < 29,
                  'drift': drift <= 3, 'area': all(.96 <= v <= 1.04 for v in areas),
                  'motion': all(v >= 3 for v in motions), 'margin': min(margins) >= 6,
                  'red': p['red'] == 0, 'pink': p['pink'] <= .2,
                  'amber': p['amber'] <= 1, 'other': p['other'] <= .5,
                  'contrast': all(v >= 10 for v in contrast)}
        fails.extend(f'{name}: {k}' for k, good in checks.items() if not good)
        print(f'{name:21s} {tones:5d} {bounds:6d} {cut:3d} {drift:4.1f} '
              f'{min(areas):.3f}-{max(areas):.3f} '
              f'{"/".join(f"{v:.1f}" for v in motions):>27s} '
              f'{min(margins):3d} {p["red"]:.2f} {p["pink"]:.2f} '
              f'{p["amber"]:.2f} {p["other"]:.2f} '
              f'{"/".join(f"{v:.1f}" for v in contrast)}')
    preview(sheets, tiles)

    others = sorted((ART / 'Resources/Enemies').glob('*_rock_*.png'))
    others += sorted((STAGE / 'strips').glob('ember_rock_*.png'))
    print('\nNative 192x192 frame-0 alpha IoU (no resize or recenter):')
    print('comparison'.ljust(51), *[n.rjust(18) for n in NAMES])
    masks = {n: silhouette(HERE / f'{n}.png') for n in NAMES}
    max_iou = {n: 0.0 for n in NAMES}
    for other in others:
        b = silhouette(other)
        values = []
        for name in NAMES:
            a = masks[name]
            iou = np.logical_and(a, b).sum() / np.logical_or(a, b).sum()
            values.append(iou)
            max_iou[name] = max(max_iou[name], iou)
            if iou >= .70:
                fails.append(f'{name} vs {other}: IoU {iou:.3f}')
        print(f'{other.parent.name}/{other.name}'.ljust(51),
              *[f'{v:18.3f}' for v in values])
    print('MAX'.ljust(51), *[f'{max_iou[n]:18.3f}' for n in NAMES])
    for failure in fails:
        print('FAIL', failure)
    print('RESULT:', 'FAIL' if fails else 'PASS', f'({len(fails)} failures)')
    raise SystemExit(bool(fails))


if __name__ == '__main__':
    main()
