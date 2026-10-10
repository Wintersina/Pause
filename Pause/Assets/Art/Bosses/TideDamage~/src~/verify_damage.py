"""Verify the staged Iron Kraken battle damage and death atlas contract."""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
CELL = 384


def fail(message: str) -> None:
    raise AssertionError(message)


def load(name: str, size: tuple[int, int]) -> Image.Image:
    im = Image.open(ROOT / name)
    if im.size != size or im.mode != 'RGBA':
        fail(f'{name}: expected RGBA {size}, got {im.mode} {im.size}')
    return im


def cells(im: Image.Image, cols: int, rows: int) -> list[Image.Image]:
    return [im.crop((c*CELL, r*CELL, (c+1)*CELL, (r+1)*CELL))
            for r in range(rows) for c in range(cols)]


def bbox(cell: Image.Image) -> tuple[int, int, int, int]:
    a = np.asarray(cell.getchannel('A'))
    yy, xx = np.where(a > 24)
    if not len(xx):
        fail('empty animation cell')
    return int(xx.min()), int(yy.min()), int(xx.max())+1, int(yy.max())+1


def border_count(cell: Image.Image) -> int:
    a = np.asarray(cell.getchannel('A')) > 24
    edge = np.zeros((CELL, CELL), dtype=bool)
    edge[:7] = edge[-7:] = True
    edge[:, :7] = edge[:, -7:] = True
    return int(np.count_nonzero(a & edge))


def red_share(cell: Image.Image) -> float:
    rgba = np.asarray(cell)
    hsv = np.asarray(cell.convert('HSV'))
    visible = rgba[:, :, 3] > 24
    red = visible & (hsv[:, :, 1] > 102) & (
        (hsv[:, :, 0] < 11) | (hsv[:, :, 0] >= 245))
    return float(np.count_nonzero(red) / max(1, np.count_nonzero(visible)))


def colours(cell: Image.Image) -> int:
    a = np.asarray(cell)
    return len(np.unique(a[a[:, :, 3] > 24, :3], axis=0))


def changed(a: Image.Image, b: Image.Image) -> float:
    aa = np.asarray(a).astype(np.int16)
    bb = np.asarray(b).astype(np.int16)
    return float(np.mean(np.any(np.abs(aa-bb) > 32, axis=2)))


def main() -> None:
    damage = cells(load('Tide_damage.png', (768, 1536)), 2, 4)
    fx = cells(load('Tide_damage_fx.png', (2304, 768)), 6, 2)
    death = cells(load('Tide_death.png', (2304, 384)), 6, 1)
    pristine = Image.open(ROOT / '../Tide~/Tide.png').convert('RGBA').crop((0, 0, CELL, CELL))
    pbox = pristine.getchannel('A').getbbox()
    allowed = (pbox[0]-6, pbox[1]-6, pbox[2]+6, pbox[3]+6)

    print('sheet / stage   bbox             colours  red %  border px')
    for index, cell in enumerate(damage):
        box = bbox(cell)
        if not (box[0] >= allowed[0] and box[1] >= allowed[1]
                and box[2] <= allowed[2] and box[3] <= allowed[3]):
            fail(f'damage cell {index}: bbox {box} outside {allowed}')
        n_border = border_count(cell)
        if n_border:
            fail(f'damage cell {index}: {n_border} border texels')
        n_colours = colours(cell)
        if n_colours < 3000:
            fail(f'damage cell {index}: only {n_colours} colours')
        red = red_share(cell)
        if red > .02:
            fail(f'damage cell {index}: red-band share {red:.2%}')
        print(f'damage {index//2+1}{"AB"[index%2]:<4} {str(box):<16} {n_colours:>7}  {red*100:>5.2f}  {n_border:>9}')

    differences = [changed(damage[2*i], damage[2*i+2]) for i in range(3)]
    for i, diff in enumerate(differences):
        if diff < .25:
            fail(f'stage {i+1}->{i+2}: only {diff:.1%} materially changed texels')
    if not (differences[0] < differences[1] < differences[2]):
        fail(f'stage differences do not increase: {differences}')
    if changed(pristine, damage[0]) < .25:
        fail('stage 1 too similar to pristine')
    for i in range(4):
        if changed(damage[2*i], damage[2*i+1]) < .0003:
            fail(f'stage {i+1}: idle A and B are identical')

    for index, cell in enumerate(fx):
        if border_count(cell):
            fail(f'FX cell {index}: alpha enters the 6 px border margin')
        if red_share(cell) > .02:
            fail(f'FX cell {index}: red-band share exceeds 2%')
        if np.count_nonzero(np.asarray(cell.getchannel('A')) > 24) < 300:
            fail(f'FX cell {index}: effect too faint')
    for index, cell in enumerate(death):
        n_border = border_count(cell)
        if n_border and index != 3:
            fail(f'death cell {index}: {n_border} border texels')
        if red_share(cell) > .02:
            fail(f'death cell {index}: red-band share exceeds 2%')
        n_colours = colours(cell)
        if n_colours < 3000:
            fail(f'death cell {index}: only {n_colours} colours')
        print(f'death  {index:<5} {str(bbox(cell)):<16} {n_colours:>7}  {red_share(cell)*100:>5.2f}  {n_border:>9}')

    print('stage step changes: ' + ', '.join(f'{v*100:.1f}%' for v in differences))
    print('PASS: dimensions, RGBA, registration, changes, borders, colour detail, palette')


if __name__ == '__main__':
    try:
        main()
    except AssertionError as exc:
        print('FAIL:', exc, file=sys.stderr)
        raise SystemExit(1)
