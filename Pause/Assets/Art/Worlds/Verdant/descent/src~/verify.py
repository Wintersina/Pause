"""Verify Verdant descent dimensions, registration, wrap seams, and sprite safety."""
from pathlib import Path
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

for name in ('verdant_cloud_deck.png', 'verdant_cloud_deck_dark.png'):
    a = np.asarray(Image.open(ROOT / name), dtype=np.int16)
    assert np.array_equal(a[:, 0], a[:, -1]), (name, 'X wrap')
    assert np.array_equal(a[0], a[-1]), (name, 'Y wrap')
    assert np.abs(a[:, 0] - a[:, -2]).mean() < 10, (name, 'X seam jump')
    assert np.abs(a[0] - a[-2]).mean() < 10, (name, 'Y seam jump')

streaks = np.asarray(Image.open(ROOT / 'verdant_entry_streaks.png'))
assert np.array_equal(streaks[0], streaks[-1]), 'streak Y wrap'
assert np.count_nonzero(streaks[:, :, 3]) > 70000, 'sparse streak layer'

planet = np.asarray(Image.open(ROOT / 'verdant_planet.png'))
alpha = planet[:, :, 3]
ys, xs = np.where(alpha > 0)
assert min(xs.min(), ys.min(), 1023 - xs.max(), 1023 - ys.max()) >= 24, 'planet margin'
spec = manifest['files']['verdant_planet.png']
glow_top = int(np.flatnonzero(alpha[:512, 512] > 0)[0])
rgb = planet[:, 512, :3]
inner = np.flatnonzero((rgb[glow_top + 5:130, 0] < 110) & (rgb[glow_top + 5:130, 1] < 185))[0]
assert spec['atmosphere_outer_radius_px'] == 512 - glow_top
assert spec['disc_radius_px'] == 512 - int(glow_top + 5 + inner)
assert spec['disc_radius_px'] < spec['atmosphere_outer_radius_px']

limb = np.asarray(Image.open(ROOT / 'verdant_planet_limb.png'))
for x in (0, 512, 1024, 1536, 2047):
    horizon = round(355 + .00035 * (x - 1024) ** 2)
    assert limb[:horizon - 21, x, 3].max() == 0, ('limb space', x)
    assert limb[horizon + 22:, x, 3].min() == 255, ('limb terrain', x)
assert manifest['files']['verdant_planet_limb.png']['horizon_y_at_edges_px'] == 722

for name, count, width in (('verdant_entry_fx.png', 6, 512), ('verdant_breakthrough.png', 5, 1024)):
    a = np.asarray(Image.open(ROOT / name))[:, :, 3]
    for frame in range(count):
        cell = a[:, frame * width:(frame + 1) * width]
        assert cell.max() > 0, (name, frame, 'empty')
        assert max(cell[0].max(), cell[-1].max(), cell[:, 0].max(), cell[:, -1].max()) == 0, (name, frame, 'cut off')

entry = np.asarray(Image.open(ROOT / 'verdant_entry_fx.png'))[:, :, 3]
spec = manifest['files']['verdant_entry_fx.png']
frames = []
for frame in range(6):
    a = entry[:, frame * 512:(frame + 1) * 512]
    frames.append(a)
    assert a[338, 256] == 0, ('ship opening', frame)
    # The same tall, centered socket must be clear in all six frames.
    assert a[260:416, 246:266].max() == 0, ('socket core', frame)
    width, height = spec['hole_size_px_per_frame'][frame]
    assert 140 <= width <= 160 and 165 <= height <= 170, ('hole size', frame, width, height)
    assert spec['hole_center_px_per_frame'][frame] == [256, 338]
assert len({frame.tobytes() for frame in frames}) == 6, 'static entry loop'

print('Verdant descent verified: sizes, RGBA, pivots, disc/rim, horizon, X/Y tile seams, clear cells, ship sockets, animation.')
