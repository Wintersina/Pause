"""Build the three staged Verdant extras from image-generated painted sources.

Run from the repository root: python3 Pause/Assets/Art/Attacks/Verdant~/src~/extras/build_extras.py
Only creates the three requested atlases; source files are read-only.
"""
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

HERE = Path(__file__).resolve().parent
OUT = HERE.parent.parent
ROOT = OUT.parents[1]
INK = (11, 11, 26, 255)
PINK = np.array([255, 79, 216], dtype=np.uint8)
PALE = np.array([255, 224, 248], dtype=np.uint8)
NEAR = Image.Resampling.NEAREST


def rgba(name):
    return Image.open(HERE / name).convert('RGBA')


def hsv_arrays(rgb):
    c = rgb.astype(np.float32) / 255
    mx, mn = c.max(2), c.min(2)
    d = mx - mn
    h = np.zeros_like(mx)
    r, g, b = c[:, :, 0], c[:, :, 1], c[:, :, 2]
    nz = d > 1e-6
    q = (mx == r) & nz
    h[q] = ((g[q] - b[q]) / d[q]) % 6
    q = (mx == g) & nz
    h[q] = (b[q] - r[q]) / d[q] + 2
    q = (mx == b) & nz
    h[q] = (r[q] - g[q]) / d[q] + 4
    h *= 60
    s = np.divide(d, mx, out=np.zeros_like(mx), where=mx > 0)
    return h, s, mx


def color_clean(im, mine=False):
    a = np.array(im.convert('RGBA'))
    rgb = a[:, :, :3]
    hue, sat, val = hsv_arrays(rgb)
    live = a[:, :, 3] > 24
    # Generator edge artifacts can be pure red. Make them the hostile pink cue.
    red = live & (sat > .42) & ((hue >= 345) | (hue <= 15))
    rgb[red] = np.where((val[red, None] > .65), PALE, PINK)
    # Magenta and violet glows converge on the game's pink family.
    purple = live & (sat > .25) & (hue >= 255) & (hue < 345)
    bright = purple & (val > .84)
    middle = purple & (val > .42) & ~bright
    rgb[bright] = PALE
    rgb[middle] = PINK
    dark = purple & ~bright & ~middle
    rgb[dark] = [135, 26, 107]
    hue, sat, val = hsv_arrays(rgb)
    # Old Verdant mine lime lamps and halo become pink light, dark leaf remains dark.
    lime = live & (hue >= 55) & (hue <= 155) & (sat > .35) & (val > .55)
    if mine:
        rgb[lime & (val > .83)] = PALE
        rgb[lime & (val <= .83)] = PINK
    else:
        rgb[lime] = [74, 122, 68]
    # Warm sap is kept muted; avoid red-orange and amber pickup-like highlights.
    hue, sat, val = hsv_arrays(rgb)
    amber = live & (hue >= 17) & (hue <= 57) & (sat > .6) & (val > .55)
    rgb[amber] = [138, 106, 42]
    a[:, :, :3] = rgb
    a[a[:, :, 3] <= 24] = [0, 0, 0, 0]
    return Image.fromarray(a, 'RGBA')


def bbox_alpha(im, threshold=70):
    a = np.asarray(im.getchannel('A'))
    yy, xx = np.where(a > threshold)
    if not len(xx):
        raise ValueError('empty source candidate')
    return (int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1)


def extract(im, box):
    cell = im.crop(box)
    return cell.crop(bbox_alpha(cell))


def fit_native(source, size, rotate=0):
    w, h = size
    painted = source.resize((w, h), NEAR)
    if rotate:
        painted = painted.rotate(rotate, NEAR, expand=True)
    # Hard pixel-art body; the exterior ink keyline is one native pixel.
    arr = np.array(painted)
    arr[:, :, 3] = np.where(arr[:, :, 3] > 70, 255, 0).astype(np.uint8)
    painted = Image.fromarray(arr, 'RGBA')
    ink = painted.getchannel('A').filter(ImageFilter.MaxFilter(3))
    outlined = Image.new('RGBA', painted.size)
    outlined.paste(INK, (0, 0, painted.width, painted.height), ink)
    outlined.alpha_composite(painted)
    cell = Image.new('RGBA', (64, 64))
    cell.alpha_composite(outlined, ((64 - outlined.width) // 2, (64 - outlined.height) // 2))
    return color_clean(cell).resize((128, 128), NEAR)


def shots():
    candidates = [rgba('shots_candidate_a.png'), rgba('shots_candidate_b.png')]
    # Width and height are native 64-pixel units; cells 10/11 are rotated scythes.
    dims = [(16, 50), (16, 50), (36, 46), (28, 46),
            (30, 51), (30, 51), (43, 45), (43, 45),
            (54, 40), (54, 40), (58, 26), (57, 27),
            (40, 51), (40, 51), (49, 48), (50, 49)]
    atlas = Image.new('RGBA', (1024, 256))
    for i in range(16):
        source = candidates[i % 2]
        # The source contact sheets have 8 unique painted designs; alternate
        # candidates supply the two animation beats for each requested design.
        design = i // 2
        col, row = design % 4, design // 4
        sw, sh = source.size
        art = extract(source, (col * sw // 4, row * sh // 2,
                               (col + 1) * sw // 4, (row + 1) * sh // 2))
        if design == 5:
            hero = rgba('crescent_candidate_a.png' if i % 2 == 0 else 'crescent_candidate_b.png')
            art = hero.crop(bbox_alpha(hero))
            cell = fit_native(art, (58, 18) if i % 2 == 0 else (55, 18),
                              rotate=0 if i % 2 == 0 else -8)
        else:
            cell = fit_native(art, dims[i])
        atlas.alpha_composite(cell, ((i % 8) * 128, (i // 8) * 128))
    atlas.save(OUT / 'verdant_attack_shots.png')


MINE_RECTS = [(0, 320), (320, 293), (613, 309), (922, 332)]
GEN_RECTS = [(0, 520), (520, 1010), (1010, 1515), (1515, 2172)]


def mine():
    original = rgba('verdant_mine_row.png')
    candidates = [rgba('mine_candidate_b.png'), rgba('mine_candidate_b.png'),
                  rgba('mine_candidate_a.png'), rgba('mine_candidate_a.png')]
    atlas = Image.new('RGBA', (1254, 298))
    for i, ((x, w), (gx0, gx1)) in enumerate(zip(MINE_RECTS, GEN_RECTS)):
        base = original.crop((x, 0, x + w, 298))
        src = extract(candidates[i], (gx0, 0, gx1, candidates[i].height))
        target_box = bbox_alpha(base, 24)
        target = Image.new('RGBA', base.size)
        target.alpha_composite(src.resize((target_box[2] - target_box[0],
                                           target_box[3] - target_box[1]), NEAR), target_box[:2])
        painted = np.array(color_clean(target, mine=True))
        original_array = np.array(base)
        # Original alpha is the alignment template: exact mine/clamp silhouette.
        missing = painted[:, :, 3] < 32
        fallback = np.array(color_clean(base, mine=True))
        painted[missing, :3] = fallback[missing, :3]
        # Image generation sometimes draws a second clamp at a frame seam.
        # The left-side rail hardware is copied from the spatial template.
        painted[:, :125, :3] = fallback[:, :125, :3]
        painted[:, :, 3] = original_array[:, :, 3]
        atlas.alpha_composite(Image.fromarray(painted, 'RGBA'), (x, 0))
    atlas.save(OUT / 'verdant_lit_mines.png')


def boss():
    original = Image.open(ROOT / 'Resources/Bosses/Verdant_shots.png').convert('RGBA')
    generated = rgba('boss_candidate_a.png')
    result = Image.new('RGBA', (1024, 128))
    for i in range(8):
        cell = original.crop((128 * i, 0, 128 * (i + 1), 128))
        if i in (4, 5, 6):
            result.alpha_composite(cell, (128 * i, 0))
            continue
        source = extract(generated, (i * generated.width // 8, 0,
                                     (i + 1) * generated.width // 8, generated.height))
        box = bbox_alpha(cell, 24)
        # Paint into the old gameplay silhouette and anchor. Its mask also
        # preserves the tiny spore satellites in the starburst frames.
        paint = Image.new('RGBA', (128, 128))
        paint.alpha_composite(source.resize((box[2] - box[0], box[3] - box[1]), NEAR), box[:2])
        arr = np.array(color_clean(paint))
        fallback = np.array(color_clean(cell))
        blank = arr[:, :, 3] < 32
        arr[blank, :3] = fallback[blank, :3]
        arr[:, :, 3] = np.array(cell)[:, :, 3]
        pixel_cell = Image.fromarray(arr, 'RGBA').resize((64, 64), NEAR).resize((128, 128), NEAR)
        result.alpha_composite(pixel_cell, (128 * i, 0))
    result.save(OUT / 'Verdant_shots_bright.png')


if __name__ == '__main__':
    shots()
    mine()
    boss()
