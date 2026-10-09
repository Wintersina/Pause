"""Verify Ember descent dimensions, registration, seams, color and sprite safety."""
from pathlib import Path
import colorsys
import json

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
manifest = json.loads((ROOT / 'manifest.json').read_text())

for name, spec in manifest['files'].items():
    image = Image.open(ROOT / name)
    assert list(image.size) == spec['size'], (name, image.size)
    if name.endswith('.png'):
        assert image.mode == 'RGBA', (name, image.mode)
    if name == 'preview.gif':
        assert image.n_frames == 6
    assert spec['frame_count'] == spec['cell_grid'][0] * spec['cell_grid'][1]

for name in ('ember_cloud_deck.png', 'ember_cloud_deck_dark.png'):
    a = np.asarray(Image.open(ROOT / name), dtype=np.int16)
    assert np.array_equal(a[:, 0], a[:, -1]), (name, 'X wrap')
    assert np.array_equal(a[0], a[-1]), (name, 'Y wrap')
    assert np.abs(a[:, 0] - a[:, -2]).mean() < 10, (name, 'X seam jump')
    assert np.abs(a[0] - a[-2]).mean() < 10, (name, 'Y seam jump')

streaks = np.asarray(Image.open(ROOT / 'ember_entry_streaks.png'))
assert np.array_equal(streaks[0], streaks[-1]), 'streak Y wrap'
assert np.count_nonzero(streaks[:, :, 3]) > 70000, 'sparse streak layer'

planet = np.asarray(Image.open(ROOT / 'ember_planet.png'))
ys, xs = np.where(planet[:, :, 3] > 0)
assert min(xs.min(), ys.min(), 1023 - xs.max(), 1023 - ys.max()) >= 18, 'planet margin'
spec = manifest['files']['ember_planet.png']
col = planet[:, 512]
bright = (col[:, 0] >= 220) & (col[:, 1] >= 105) & (col[:, 3] >= 160)
rim_top = int(next(y for y in range(85, 180) if bright[y:y + 5].all()))
disc_top = int(next(y for y in range(rim_top + 20, 180)
                    if col[y, 0] < 100 and col[y, 1] < 60 and col[y, 3] >= 160))
assert spec['atmosphere_outer_radius_px'] == 512 - rim_top
assert spec['disc_radius_px'] == 512 - disc_top
assert 365 <= spec['disc_radius_px'] < spec['atmosphere_outer_radius_px'] <= 420

limb = np.asarray(Image.open(ROOT / 'ember_planet_limb.png'))
for x in (0, 512, 1024, 1536, 2047):
    horizon = round(355 + .00035 * (x - 1024) ** 2)
    assert limb[:horizon - 21, x, 3].max() == 0, ('limb space', x)
    assert limb[horizon + 22:, x, 3].min() == 255, ('limb terrain', x)
assert manifest['files']['ember_planet_limb.png']['horizon_y_at_edges_px'] == 722

for name, count, width in (('ember_entry_fx.png', 6, 512), ('ember_breakthrough.png', 5, 1024)):
    a = np.asarray(Image.open(ROOT / name))[:, :, 3]
    for frame in range(count):
        cell = a[:, frame * width:(frame + 1) * width]
        assert cell.max() > 0, (name, frame, 'empty')
        assert max(cell[0].max(), cell[-1].max(), cell[:, 0].max(), cell[:, -1].max()) == 0, (name, frame, 'cut off')

entry = np.asarray(Image.open(ROOT / 'ember_entry_fx.png'))[:, :, 3]
spec = manifest['files']['ember_entry_fx.png']
frames = []
def clear_span(values, center):
    lo = hi = center
    while lo > 0 and values[lo - 1] == 0:
        lo -= 1
    while hi + 1 < len(values) and values[hi + 1] == 0:
        hi += 1
    return hi - lo + 1

for frame in range(6):
    a = entry[:, frame * 512:(frame + 1) * 512]
    frames.append(a)
    assert a[338, 256] == 0, ('ship opening', frame)
    assert a[260:416, 246:266].max() == 0, ('socket core', frame)
    measured = [clear_span(a[338], 256), clear_span(a[:, 256], 338)]
    assert measured == spec['hole_size_px_per_frame'][frame], (frame, measured)
    assert 140 <= measured[0] <= 160 and 165 <= measured[1] <= 170
    assert spec['hole_center_px_per_frame'][frame] == [256, 338]
assert len({frame.tobytes() for frame in frames}) == 6, 'static entry loop'

for name in manifest['files']:
    if not name.startswith('ember_') or not name.endswith('.png'):
        continue
    a = np.asarray(Image.open(ROOT / name))
    colors = np.unique(a[a[:, :, 3] > 0, :3], axis=0)
    for color in colors:
        h, s, v = colorsys.rgb_to_hsv(*(float(c) / 255 for c in color))
        distance = abs((h * 360 - 354.5 + 180) % 360 - 180)
        assert s <= .20 or v <= .10 or distance >= 22, (name, tuple(color), distance)

print('Ember descent verified: sizes, RGBA, disc/rim, horizon, seams, cell guards, ship sockets, animation, red separation.')
