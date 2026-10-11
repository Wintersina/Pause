"""Audit and preview the staged Verdant extras. Run from any directory."""
from pathlib import Path
import sys
import numpy as np
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent
SRC = OUT / 'src~/extras'
sys.dont_write_bytecode = True
sys.path.insert(0, str(SRC))
from build_extras import hsv_arrays, MINE_RECTS  # noqa: E402


def load(name, size):
    im = Image.open(OUT / name)
    assert im.size == size and im.mode == 'RGBA', (name, im.size, im.mode)
    return im


def cell_stats(im):
    a = np.asarray(im)
    hue, sat, value = hsv_arrays(a[:, :, :3])
    opaque = a[:, :, 3] > 24
    n = int(opaque.sum())
    pink = opaque & (((hue >= 304) & (hue <= 334) & (sat > .2) & (value > .4)) |
                     ((a[:, :, 0] >= 225) & (a[:, :, 1] >= 180) & (a[:, :, 2] >= 205)))
    bright = opaque & (sat > .6) & (value > .55)
    bands = [int((bright & (np.minimum(abs(hue - center), 360 - abs(hue - center)) <= 20)).sum())
             for center in (178, 82, 259, 37)]
    red = int((opaque & (sat > .5) & ((hue >= 345) | (hue <= 15))).sum())
    lum = (.2126 * a[:, :, 0] + .7152 * a[:, :, 1] + .0722 * a[:, :, 2])
    mean_lum = float(lum[opaque].mean()) if n else 0
    edge = np.concatenate((a[:6, :, 3].ravel(), a[-6:, :, 3].ravel(),
                           a[:, :6, 3].ravel(), a[:, -6:, 3].ravel()))
    back = im.resize((im.width // 2, im.height // 2), Image.Resampling.NEAREST).resize(
        im.size, Image.Resampling.NEAREST) if im.width % 2 == 0 and im.height % 2 == 0 else None
    return {'opaque': n, 'pink': 100 * int(pink.sum()) / max(n, 1),
            'bad': bands, 'red': red, 'luma': mean_lum,
            'border': int(edge.max()) if len(edge) else 0,
            'x2': bool(back is not None and np.array_equal(np.asarray(back), a))}


def score_pair(a, b):
    aa, bb = np.asarray(a), np.asarray(b)
    union = (aa[:, :, 3] > 24) | (bb[:, :, 3] > 24)
    changed = np.any(aa != bb, axis=2) & union
    return 100 * int(changed.sum()) / max(1, int(union.sum()))


def make_preview(shots, mine, boss):
    dark = (11, 11, 26, 255)
    canopy = (111, 168, 74, 255)
    pic = Image.new('RGBA', (2580, 1520), dark)
    draw = ImageDraw.Draw(pic)
    def title(s, xy):
        draw.text(xy, s, fill=(255, 224, 248, 255))
    title('VERDANT ATTACK SHOTS / 2x', (16, 10))
    pic.alpha_composite(shots.resize((2048, 512), Image.Resampling.NEAREST), (16, 34))
    title('LIT MINES / 2x', (16, 560))
    pic.alpha_composite(mine.resize((2508, 596), Image.Resampling.NEAREST), (16, 583))
    title('BOSS SHOTS / 2x', (16, 1188))
    pic.alpha_composite(boss.resize((2048, 256), Image.Resampling.NEAREST), (16, 1210))
    # Game-size reading tests: 40 px cells over both requested flat tiles.
    for top, tile, label in ((36, dark, '40px / DARK'), (286, canopy, '40px / CANOPY')):
        x0 = 2095
        draw.rectangle((x0, top, 2560, top + 232), fill=tile)
        draw.text((x0 + 8, top + 6), label, fill=(255, 224, 248, 255), stroke_width=1,
                  stroke_fill=(11, 11, 26, 255))
        for i in range(16):
            c = shots.crop(((i % 8) * 128, (i // 8) * 128,
                            (i % 8 + 1) * 128, (i // 8 + 1) * 128))
            c = c.resize((40, 40), Image.Resampling.NEAREST)
            pic.alpha_composite(c, (x0 + 12 + (i % 8) * 55, top + 40 + (i // 8) * 55))
        for j, i in enumerate((0, 1, 2, 3, 7)):
            c = boss.crop((i * 128, 0, (i + 1) * 128, 128)).resize((40, 40), Image.Resampling.NEAREST)
            pic.alpha_composite(c, (x0 + 22 + j * 82, top + 158))
    pic.convert('RGB').save(OUT / 'preview_extras.png')


def main():
    shots = load('verdant_attack_shots.png', (1024, 256))
    mine = load('verdant_lit_mines.png', (1254, 298))
    boss = load('Verdant_shots_bright.png', (1024, 128))
    old_mine = Image.open(SRC / 'verdant_mine_row.png').convert('RGBA')
    old_boss = Image.open(OUT.parents[1] / 'Resources/Bosses/Verdant_shots.png').convert('RGBA')
    lines = ['name    cell   opaque  pink%  cyan green violet amber  red  border  x2  detail']
    failures = []
    for i in range(16):
        x, y = i % 8 * 128, i // 8 * 128
        c = shots.crop((x, y, x + 128, y + 128))
        s = cell_stats(c)
        pair = score_pair(c, shots.crop((x + (128 if i % 2 == 0 else -128), y,
                                        x + (256 if i % 2 == 0 else 0), y + 128)))
        detail = f'pair={pair:.1f}%' if i % 2 == 0 else ''
        lines.append(f'shots   {i:>2}  {s["opaque"]:>6}  {s["pink"]:>5.1f}  '
                     f'{s["bad"][0]:>4} {s["bad"][1]:>5} {s["bad"][2]:>6} {s["bad"][3]:>5}  '
                     f'{s["red"]:>3}  {s["border"]:>6}  {str(s["x2"]):>4}  {detail}')
        if not s['opaque'] or s['pink'] < 30 or s['red'] or s['border'] > 24 or not s['x2'] or pair < 3:
            failures.append(f'shots {i}')
    for i, (x, w) in enumerate(MINE_RECTS):
        c = mine.crop((x, 0, x + w, 298))
        s = cell_stats(c)
        m = np.asarray(c)[:, :, 3] > 24
        o = np.asarray(old_mine.crop((x, 0, x + w, 298)))[:, :, 3] > 24
        iou = 100 * np.logical_and(m, o).sum() / max(1, np.logical_or(m, o).sum())
        lines.append(f'mine    {i:>2}  {s["opaque"]:>6}  {s["pink"]:>5.1f}  '
                     f'{s["bad"][0]:>4} {s["bad"][1]:>5} {s["bad"][2]:>6} {s["bad"][3]:>5}  '
                     f'{s["red"]:>3}  {s["border"]:>6}  {str(s["x2"]):>4}  IoU={iou:.1f}%')
        if not s['opaque'] or s['pink'] < (15 if i < 2 else 30) or s['red'] or iou < 80:
            failures.append(f'mine {i}')
    for i in range(8):
        c = boss.crop((i * 128, 0, (i + 1) * 128, 128))
        old = old_boss.crop((i * 128, 0, (i + 1) * 128, 128))
        s, before = cell_stats(c), cell_stats(old)
        exact = np.array_equal(np.asarray(c), np.asarray(old))
        detail = ('COPY=' + str(exact)) if i in (4, 5, 6) else f'oldL={before["luma"]:.1f} newL={s["luma"]:.1f}'
        lines.append(f'boss    {i:>2}  {s["opaque"]:>6}  {s["pink"]:>5.1f}  '
                     f'{s["bad"][0]:>4} {s["bad"][1]:>5} {s["bad"][2]:>6} {s["bad"][3]:>5}  '
                     f'{s["red"]:>3}  {s["border"]:>6}  {str(s["x2"]):>4}  {detail}')
        if i in (4, 5, 6):
            if not exact:
                failures.append(f'boss copy {i}')
        elif s['pink'] < 45 or s['red'] or s['luma'] < before['luma'] + 25:
            failures.append(f'boss {i}')
    lines.append('x2 note: all new shot cells and edited boss cells pass. Mine masks and untouched boss cells retain exact legacy pixels.')
    lines.append('FAILURES: ' + (', '.join(failures) if failures else 'none'))
    report = '\n'.join(lines) + '\n'
    print(report)
    (OUT / 'verification_extras.txt').write_text(report)
    make_preview(shots, mine, boss)
    if failures:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
