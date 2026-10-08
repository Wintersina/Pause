"""Check Frost descent sprite contract and animation safety margins."""
from pathlib import Path
import json

import numpy as np
from PIL import Image

SRC = Path(__file__).resolve().parent
ROOT = SRC.parents[3] / 'Backgrounds' / 'Resources' / 'Worlds' / 'Frost' / 'Planetfall'
manifest = json.loads((SRC / 'manifest.json').read_text())

for name, spec in manifest['files'].items():
    image = Image.open((SRC if name.startswith('preview') else ROOT) / name)
    assert list(image.size) == spec['size'], (name, image.size, spec['size'])
    if name.endswith('.png'):
        assert image.mode == 'RGBA', (name, image.mode)
    if name == 'preview.gif':
        assert image.n_frames == 6

for name in ('frost_cloud_deck.png', 'frost_cloud_deck_dark.png'):
    a = np.asarray(Image.open(ROOT / name))
    assert np.array_equal(a[:, 0], a[:, -1]), name + ' X edge'
    assert np.array_equal(a[0], a[-1]), name + ' Y edge'

streaks = np.asarray(Image.open(ROOT / 'frost_entry_streaks.png'))
assert np.array_equal(streaks[0], streaks[-1]), 'streak Y edge'

planet = Image.open(ROOT / 'frost_planet.png')
bbox = planet.getchannel('A').getbbox()
assert bbox[0] >= 24 and bbox[1] >= 24 and bbox[2] <= 1000 and bbox[3] <= 1000, bbox

limb = np.asarray(Image.open(ROOT / 'frost_planet_limb.png'))
for x in (0, 512, 1024, 1536, 2047):
    horizon = round(355 + .00035 * (x - 1024) ** 2)
    assert limb[:horizon - 19, x, 3].max() == 0, ('limb space', x)
    assert limb[horizon + 22:, x, 3].min() == 255, ('limb surface', x)

for name, count, width in (('frost_entry_fx.png', 6, 512), ('frost_breakthrough.png', 5, 1024)):
    alpha = np.asarray(Image.open(ROOT / name))[:, :, 3]
    for i in range(count):
        cell = alpha[:, i * width:(i + 1) * width]
        assert cell.max() > 0, (name, i, 'empty')
        assert max(cell[0].max(), cell[-1].max(), cell[:, 0].max(), cell[:, -1].max()) == 0, (name, i, 'cut-off')

entry = np.asarray(Image.open(ROOT / 'frost_entry_fx.png'))[:, :, 3]
frames = [entry[:, i * 512:(i + 1) * 512] for i in range(6)]
assert all(frame[300, 256] == 0 for frame in frames), 'ship socket'
assert len({frame.tobytes() for frame in frames}) == 6, 'static entry loop'

print('Frost descent verified: manifest, RGBA, X/Y tiles, margins, horizons, and clear animated cells.')
