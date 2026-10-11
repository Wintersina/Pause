#!/usr/bin/env python3
"""Build two Verdant enemy strips from the selected image-generated paintings.

All resampling is nearest-neighbour. The central hull stays locked while wing,
blade, antenna and drill regions move locally. Run from any working directory.
"""
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
SIDE = 192
INK = (15, 17, 9, 255)
BARK = (91, 69, 37, 255)
PALE = (241, 246, 200, 255)
LEAF = (112, 165, 62, 255)
LIME = (233, 255, 184, 255)
HOT = (249, 255, 224, 255)
SPEC = {
    'verdant_fighter_2': {'column': 0, 'size': (139, 162), 'eye': (97, 50), 'weapon': (96, 159)},
    'verdant_fighter_3': {'column': 2, 'size': (161, 158), 'eye': (96, 58), 'weapon': (96, 109)},
}


def components(mask):
    """Eight-connected opaque islands, largest first."""
    seen = np.zeros(mask.shape, bool)
    found = []
    h, w = mask.shape
    for y, x in zip(*np.where(mask)):
        if seen[y, x]:
            continue
        todo = [(int(y), int(x))]
        seen[y, x] = True
        group = []
        while todo:
            py, px = todo.pop()
            group.append((py, px))
            for ny in range(max(0, py-1), min(h, py+2)):
                for nx in range(max(0, px-1), min(w, px+2)):
                    if mask[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        todo.append((ny, nx))
        found.append(group)
    return sorted(found, key=len, reverse=True)


def keep_body(im):
    a = np.asarray(im.convert('RGBA')).copy()
    comps = components(a[..., 3] > 96)
    if not comps:
        raise ValueError('empty generated painting')
    keep = np.zeros(a.shape[:2], bool)
    yy, xx = zip(*comps[0])
    keep[yy, xx] = True
    a[~keep] = 0
    return Image.fromarray(a, 'RGBA')


def clean_palette(im):
    """Keep painted colour variation while removing generated red/amber noise."""
    a = np.asarray(im.convert('RGBA')).copy()
    hsv = np.asarray(im.convert('RGB').convert('HSV')).copy()
    h = hsv[..., 0].astype(float) * (360.0 / 255)
    s = hsv[..., 1].astype(float) / 255
    # Paint sometimes invents scarlet chromatic fringes. Convert them to muted brass.
    warm = ((h < 50) | (h >= 345)) & (s > .24)
    hsv[..., 0][warm] = round(27 / 360 * 255)
    hsv[..., 1][warm] = np.minimum(hsv[..., 1][warm], 104)
    # Narrow, muted brass and olive highlights; no saturated amber pickups.
    amber = (h >= 30) & (h <= 48) & (s > .45)
    hsv[..., 0][amber] = round(27 / 360 * 255)
    hsv[..., 1][amber] = np.minimum(hsv[..., 1][amber], 108)
    green = (h > 48) & (h < 98) & (s > .43)
    deep_vein = green & (s > .60) & (hsv[..., 2] < 177)
    hsv[..., 0][green] = round(70 / 360 * 255)
    # Broad membranes stay luminous through value and pale veins. Saturated
    # Verdant green is reserved for the tiny eye and a handful of vein marks.
    hsv[..., 1][green] = np.minimum(hsv[..., 1][green], 112)
    hsv[..., 0][deep_vein] = round(108 / 360 * 255)
    hsv[..., 1][deep_vein] = 151
    leafy = (h >= 98) & (h <= 125) & (s > .45)
    hsv[..., 1][leafy] = np.minimum(hsv[..., 1][leafy], 112)
    # Alien colour fringes become the local deep olive-brown ramp.
    alien = (h >= 175) & (h < 345) & (s > .22)
    hsv[..., 0][alien] = round(27 / 360 * 255)
    hsv[..., 1][alien] = np.minimum(hsv[..., 1][alien], 105)
    rgb = np.asarray(Image.fromarray(hsv, 'HSV').convert('RGB'))
    a[..., :3] = rgb
    # Almost-black generator pixels can report a spurious red hue; make ink neutral.
    near_ink = a[..., :3].max(axis=2) < 16
    a[near_ink, :3] = (5, 7, 4)
    a[..., 3] = np.where(a[..., 3] >= 96, 255, 0).astype('uint8')
    a[a[..., 3] == 0, :3] = 0
    return Image.fromarray(a, 'RGBA')


def selected_key(name):
    im = Image.open(HERE / f'{name}_candidates.png').convert('RGBA')
    cw = im.width // 3
    col = SPEC[name]['column']
    im = keep_body(im.crop((col*cw, 0, (col+1)*cw, im.height)))
    alpha = np.asarray(im.getchannel('A')) > 96
    yy, xx = np.where(alpha)
    im = im.crop((max(0, xx.min()-3), max(0, yy.min()-3),
                  min(cw, xx.max()+4), min(im.height, yy.max()+4)))
    size = SPEC[name]['size']
    im = clean_palette(im.resize(size, Image.Resampling.NEAREST))
    key = Image.new('RGBA', (SIDE, SIDE))
    key.alpha_composite(im, ((SIDE-size[0])//2, (SIDE-size[1])//2))
    return rim_and_outline(keep_body(key))


def rim_and_outline(im):
    a = np.asarray(im).copy()
    solid = a[..., 3] > 128
    mask = Image.fromarray(solid.astype('uint8') * 255)
    inner = np.asarray(mask.filter(ImageFilter.MinFilter(5))) > 0
    outer = np.asarray(mask.filter(ImageFilter.MaxFilter(3))) > 0
    rim = solid & ~inner
    yy, xx = np.indices(solid.shape)
    # A warm pale contour on all sides, strongest where the upper-left sun hits.
    a[rim, :3] = np.maximum(a[rim, :3], np.array((205, 192, 132), np.uint8))
    light = rim & ((xx < 100) | (yy < 82))
    a[light, :3] = np.maximum(a[light, :3], np.array(PALE[:3], np.uint8))
    a[outer & ~solid] = INK
    return Image.fromarray(a, 'RGBA')


def local_motion(key, name, idx, tell=False):
    """Inverse-sample only anatomy outside the central locked hull."""
    a = np.asarray(key)
    y, x = np.indices((SIDE, SIDE), dtype=np.float32)
    cx = 95.5
    phase = [0., 1., -1., .55][idx] if not tell else 1.
    dx = np.zeros_like(x)
    dy = np.zeros_like(y)
    ax = np.abs(x-cx)
    side = np.sign(x-cx)
    if name == 'verdant_fighter_2':
        # Veined wing tips buzz 2-3 px; the brass spine does not translate.
        wing = np.clip((ax-15)/34, 0, 1) * np.clip((y-72)/22, 0, 1)
        dx += side * wing * (phase*2.7 if not tell else -8.0)
        dy += wing * (phase*1.9 if not tell else 8.0)
        # Drill collar revolves locally without changing its outside anchor.
        drill = np.clip((y-128)/12, 0, 1) * np.clip((13-ax)/7, 0, 1)
        dx += drill * phase * np.sin(y*.40) * 1.9
    else:
        # Both scythes twitch together symmetrically; tell spreads them wide.
        blade = np.clip((ax-15)/31, 0, 1) * np.clip((y-67)/20, 0, 1)
        dx += side * blade * (phase*3.0 if not tell else 6.0)
        dy += blade * (phase*1.5 if not tell else -3.0)
        antenna = np.clip((67-y)/16, 0, 1) * np.clip((ax-8)/18, 0, 1)
        dx += antenna * phase * 2.2
    sx = np.clip(np.rint(x-dx).astype(int), 0, SIDE-1)
    sy = np.clip(np.rint(y-dy).astype(int), 0, SIDE-1)
    return Image.fromarray(a[sy, sx], 'RGBA')


def rotate_drill_marks(frame, idx):
    if idx == 0:
        return frame
    d = ImageDraw.Draw(frame)
    y = 146 + (idx % 2)
    d.line([(92+(idx%3), y-5), (98+(idx%3), y-1)], fill=INK, width=1)
    d.line([(92+(idx%3), y-6), (96+(idx%3), y-4)], fill=PALE, width=1)
    return frame


def eye_mark(frame, name, idx):
    x, y = SPEC[name]['eye']
    d = ImageDraw.Draw(frame)
    if idx == 3:
        d.point((x, y), fill=PALE)
    else:
        d.ellipse((x-2, y-2, x+2, y+2), fill=LEAF)
        d.point((x, y), fill=LIME if idx != 2 else HOT)
    return frame


def tell_frame(key, name, discharge):
    frame = local_motion(key, name, 1, tell=True)
    d = ImageDraw.Draw(frame)
    wx, wy = SPEC[name]['weapon']
    if name == 'verdant_fighter_2':
        # A compact drill-tip charge and one tiny outward spark.
        wy -= 3
    else:
        wy -= 4
    r = 3 if not discharge else 5
    d.ellipse((wx-r-1, wy-r-1, wx+r+1, wy+r+1), fill=LEAF)
    d.ellipse((wx-r, wy-r, wx+r, wy+r), fill=LIME)
    d.point((wx, wy), fill=HOT)
    if discharge:
        for sign in (-1, 1):
            d.line([(wx+sign*7, wy), (wx+sign*10, wy+5)], fill=PALE, width=1)
            d.point((wx+sign*11, wy+7), fill=LIME)
        d.line([(wx, wy+6), (wx, wy+11)], fill=LIME, width=2)
    return frame


def hit_frame(key, name):
    a = np.asarray(key).copy()
    solid = a[..., 3] > 128
    # Short warm-pale flash of existing upper facets, retaining dark material.
    v = a[..., :3].max(axis=2)
    flash = solid & (v > 88)
    a[flash, :3] = np.clip(a[flash, :3].astype(np.int16)*1.16+10, 0, 255).astype('uint8')
    frame = Image.fromarray(a, 'RGBA')
    d = ImageDraw.Draw(frame)
    crack = [(88, 88), (92, 94), (88, 99), (95, 105), (92, 112)]
    d.line(crack, fill=INK, width=2)
    d.line([(x-1,y-1) for x,y in crack[:3]], fill=PALE, width=1)
    for x,y in [(57,91),(134,103),(70,119),(123,79)]:
        d.polygon([(x,y-2),(x+3,y),(x,y+3),(x-2,y)], fill=BARK, outline=INK)
        d.point((x-1,y-1), fill=PALE)
    wx, wy = SPEC[name]['weapon']
    d.ellipse((wx-3,wy-3,wx+3,wy+3),fill=LIME)
    d.point((wx,wy),fill=HOT)
    return frame


def main():
    for name in SPEC:
        key = selected_key(name)
        key.save(HERE / f'{name}_key.png', optimize=True)
        frames = []
        for i in range(4):
            f = local_motion(key, name, i)
            if name == 'verdant_fighter_2':
                f = rotate_drill_marks(f, i)
            frames.append(keep_body(eye_mark(f, name, i)))
        frames.extend([tell_frame(key, name, False), tell_frame(key, name, True), hit_frame(key, name)])
        strip = Image.new('RGBA', (7*SIDE, SIDE))
        for i, frame in enumerate(frames):
            strip.alpha_composite(frame, (i*SIDE, 0))
        strip.save(HERE / 'strips' / f'{name}.png', optimize=True)
    # A single review sheet: both strips at 2x on the requested dark ground.
    bg = Image.new('RGB', (7*SIDE*2, SIDE*2*2), (11, 11, 26))
    for row, name in enumerate(SPEC):
        im = Image.open(HERE / 'strips' / f'{name}.png').convert('RGBA')
        big = im.resize((im.width*2, im.height*2), Image.Resampling.NEAREST)
        bg.paste(big, (0, row*SIDE*2), big)
    bg.save(HERE / 'preview.png', optimize=True)


if __name__ == '__main__':
    main()
