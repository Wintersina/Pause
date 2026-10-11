"""Verify Tempest Engine damage, effect, death and preview contracts."""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
CELL = 384


def require(ok: bool, message: str) -> None:
    if not ok:
        raise AssertionError(message)


def sheet(name: str, size: tuple[int, int]) -> Image.Image:
    im = Image.open(ROOT / name)
    require(im.size == size and im.mode == 'RGBA',
            f'{name}: expected RGBA {size}, got {im.mode} {im.size}')
    return im


def cells(im: Image.Image, cols: int, rows: int) -> list[Image.Image]:
    return [im.crop((x*CELL, y*CELL, (x+1)*CELL, (y+1)*CELL))
            for y in range(rows) for x in range(cols)]


def stats(im: Image.Image) -> tuple[tuple[int, int, int, int], int, int, float, int]:
    a = np.asarray(im)
    visible = a[:, :, 3] > 24
    yy, xx = np.where(visible)
    require(len(xx) > 0, 'empty animation cell')
    box = (int(xx.min()), int(yy.min()), int(xx.max()+1), int(yy.max()+1))
    colours = len(np.unique(a[visible, :3], axis=0))
    edge = np.zeros((CELL, CELL), dtype=bool)
    edge[:7] = edge[-7:] = True
    edge[:, :7] = edge[:, -7:] = True
    border = int(np.count_nonzero(visible & edge))
    hsv = np.asarray(im.convert('HSV'))
    h, s = hsv[:, :, 0], hsv[:, :, 1]
    red = visible & (s > 102) & ((h < 11) | (h >= 245))
    red_share = float(np.count_nonzero(red) / np.count_nonzero(visible))
    return box, colours, border, red_share, int(np.count_nonzero(visible))


def changed(a: Image.Image, b: Image.Image) -> float:
    aa = np.asarray(a).astype(np.int16)
    bb = np.asarray(b).astype(np.int16)
    return float(np.mean(np.any(np.abs(aa-bb) > 32, axis=2)))


def main() -> None:
    damage = cells(sheet('Storm_damage.png', (768, 1536)), 2, 4)
    effects = cells(sheet('Storm_damage_fx.png', (2304, 768)), 6, 2)
    death = cells(sheet('Storm_death.png', (2304, 384)), 6, 1)
    pristine = Image.open(ROOT / '../Storm~/Storm.png').convert('RGBA').crop((0, 0, CELL, CELL))
    pbox = pristine.getchannel('A').getbbox()
    allowed = (pbox[0]-6, pbox[1]-6, pbox[2]+6, pbox[3]+6)
    print('sheet / cell  bbox                colours  red %  border px  alpha px')
    for i, cell in enumerate(damage):
        box, colours, border, red, count = stats(cell)
        require(box[0] >= allowed[0] and box[1] >= allowed[1] and
                box[2] <= allowed[2] and box[3] <= allowed[3],
                f'damage {i}: bbox {box} outside {allowed}')
        require(border == 0, f'damage {i}: {border} border pixels')
        require(colours >= 3000, f'damage {i}: only {colours} colours')
        require(red <= .02, f'damage {i}: red {red:.2%}')
        print(f'damage {i//2+1}{"AB"[i%2]:<3} {str(box):<19} {colours:>7}  {red*100:>5.2f}  {border:>9}  {count:>8}')

    cumulative = [changed(pristine, damage[2*i]) for i in range(4)]
    adjacent = [changed(damage[2*i], damage[2*i+2]) for i in range(3)]
    require(all(a < b for a, b in zip(cumulative, cumulative[1:])),
            f'damage not cumulative: {cumulative}')
    require(min(adjacent) >= .15, f'adjacent stages too similar: {adjacent}')
    require(cumulative[0] >= .20, 'first stage too subtle')
    for i in range(4):
        require(changed(damage[i*2], damage[i*2+1]) > .0001,
                f'stage {i+1}: A and B identical')

    for i, cell in enumerate(effects):
        box, colours, border, red, count = stats(cell)
        require(border == 0, f'FX {i}: {border} border pixels')
        require(count >= 300, f'FX {i}: too faint')
        require(red <= .02, f'FX {i}: red {red:.2%}')
        print(f'FX     {i:<4} {str(box):<19} {colours:>7}  {red*100:>5.2f}  {border:>9}  {count:>8}')

    for i, cell in enumerate(death):
        box, colours, border, red, count = stats(cell)
        require(border == 0 or i == 3, f'death {i}: {border} border pixels')
        require(colours >= 3000, f'death {i}: only {colours} colours')
        require(red <= .02, f'death {i}: red {red:.2%}')
        print(f'death  {i:<4} {str(box):<19} {colours:>7}  {red*100:>5.2f}  {border:>9}  {count:>8}')

    preview = Image.open(ROOT / 'preview.png')
    death_preview = Image.open(ROOT / 'Storm_death_preview.png')
    gif = Image.open(ROOT / 'preview.gif')
    require(preview.size == (3840, 768), f'preview size {preview.size}')
    require(death_preview.size == (4608, 768), f'death preview size {death_preview.size}')
    require(gif.size == (768, 768) and gif.n_frames == 12,
            f'GIF {gif.size}, {gif.n_frames} frames')
    print('cumulative changed: ' + ', '.join(f'{v*100:.1f}%' for v in cumulative))
    print('adjacent changed:   ' + ', '.join(f'{v*100:.1f}%' for v in adjacent))
    print('PASS: sizes, RGBA, registration, increasing damage, loop variants, borders, detail, red share, previews')


if __name__ == '__main__':
    try:
        main()
    except AssertionError as error:
        print('FAIL:', error, file=sys.stderr)
        raise SystemExit(1)
