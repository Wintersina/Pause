"""Verify exported Ember atlas contracts and registration. Run after build.py."""
from pathlib import Path
from PIL import Image
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
CELL = 384


def rgba(name, size):
    image = Image.open(ROOT / name)
    assert image.mode == 'RGBA' and image.size == size, (name, image.mode, image.size)
    return np.array(image)


def bounds(alpha, threshold=96):
    ys, xs = np.where(alpha >= threshold)
    assert len(xs)
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def assert_cell_edges_clear(cell):
    alpha = cell[:, :, 3]
    assert not (alpha[0].any() or alpha[-1].any() or alpha[:, 0].any() or alpha[:, -1].any())


ref = np.array(Image.open(ROOT / 'ref_idle_cell.png').convert('RGBA'))
l, t, r, b = bounds(ref[:, :, 3])
allowed = (l - 6, t - 6, r + 6, b + 6)
body = rgba('Ember_damage.png', (768, 1536))
fx = rgba('Ember_damage_fx.png', (2304, 768))
death = rgba('Ember_death.png', (2304, 384))
frames = []
for row in range(4):
    for col in range(2):
        cell = body[row*CELL:(row+1)*CELL, col*CELL:(col+1)*CELL]
        bbox = bounds(cell[:, :, 3])
        assert bbox[0] >= allowed[0] and bbox[1] >= allowed[1]
        assert bbox[2] <= allowed[2] and bbox[3] <= allowed[3], (row, col, bbox, allowed)
        assert_cell_edges_clear(cell)
        frames.append(cell)
        print(f'stage {row+1}, idle {col}: bbox {bbox}')

for row in range(3):
    a = frames[row*2].astype(np.int16)
    b = frames[(row+1)*2].astype(np.int16)
    changed = int(np.count_nonzero(np.any(np.abs(a-b) > 30, axis=2)))
    assert changed > 10000, (row, changed)
    print(f'stage {row+1} to {row+2}: {changed} changed pixels')

for row in range(4):
    a = frames[row*2].astype(np.int16)
    b = frames[row*2+1].astype(np.int16)
    changed = int(np.count_nonzero(np.any(np.abs(a-b) > 10, axis=2)))
    assert changed > 50, (row, changed)

for atlas, rows, cols in ((fx, 2, 6), (death, 1, 6)):
    for row in range(rows):
        for col in range(cols):
            cell = atlas[row*CELL:(row+1)*CELL, col*CELL:(col+1)*CELL]
            assert_cell_edges_clear(cell)
            assert np.count_nonzero(cell[:, :, 3]) > 100

assert Image.open(ROOT / 'preview.png').size == (3840, 768)
assert Image.open(ROOT / 'Ember_death_preview.png').size == (4608, 768)
gif = Image.open(ROOT / 'preview.gif')
assert gif.size == (768, 768) and gif.n_frames == 12
print('RGBA, dimensions, registration, frame differences, transparent cell borders, and previews pass')
