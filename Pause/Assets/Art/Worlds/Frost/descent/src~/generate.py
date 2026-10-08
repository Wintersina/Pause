"""Build Frost descent sprites from selected image-generated pixel-art sources.

All resampling is nearest-neighbour. No procedural noise is used for hero art.
Run: python3 Pause/Assets/Art/Worlds/Frost/descent/src~/generate.py
"""
from pathlib import Path
import json
import math
import random

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SRC = Path(__file__).resolve().parent
NEAREST = Image.Resampling.NEAREST
INK = (5, 11, 28)
CYAN = (91, 231, 255)
WHITE = (230, 253, 255)
MAGENTA = (255, 73, 193)


def source(name, colors=64):
    original = Image.open(SRC / name).convert('RGBA')
    alpha = original.getchannel('A')
    rgb = original.convert('RGB').quantize(colors=colors, dither=Image.Dither.NONE).convert('RGB')
    rgb.putalpha(alpha)
    return rgb


def stepped_alpha(image, levels=(0, 72, 144, 216, 255)):
    a = np.asarray(image, dtype=np.uint8).copy()
    choices = np.array(levels, dtype=np.int16)
    a[:, :, 3] = choices[np.argmin(abs(a[:, :, 3, None].astype(np.int16) - choices), axis=2)]
    return Image.fromarray(a, 'RGBA')


def planet():
    art = source('planet_source.png', 72)
    bbox = art.getchannel('A').point(lambda x: 255 if x > 28 else 0).getbbox()
    art = art.crop(bbox)
    scale = min(976 / art.width, 976 / art.height)
    art = art.resize((int(art.width * scale), int(art.height * scale)), NEAREST)
    out = Image.new('RGBA', (1024, 1024))
    out.alpha_composite(art, ((1024 - art.width) // 2, (1024 - art.height) // 2))
    out = stepped_alpha(out)
    out.save(ROOT / 'frost_planet.png', optimize=True)
    return out


def limb():
    """Project generated glacial terrain under an exact curved atmospheric limb."""
    terrain = np.asarray(source('limb_surface_source.png', 80).convert('RGB'))
    h, w = 1024, 2048
    x = np.arange(w, dtype=np.float32)
    y = np.arange(h, dtype=np.float32)[:, None]
    horizon = np.rint(355 + 0.00035 * (x - 1024) ** 2).astype(np.int32)
    depth = y - horizon[None, :]
    sample_x = np.minimum(terrain.shape[1] - 1, (x * terrain.shape[1] / w).astype(np.int32))
    available = (h - 1 - horizon)[None, :]
    sample_y = np.clip((np.maximum(depth, 0) * (terrain.shape[0] - 1) / available).astype(np.int32), 0, terrain.shape[0] - 1)
    a = np.zeros((h, w, 4), dtype=np.uint8)
    ground = depth >= 0
    a[:, :, :3] = terrain[sample_y, sample_x[None, :]]
    a[:, :, 3] = np.where(ground, 255, 0).astype(np.uint8)
    # Broad stepped extinction adds depth without a geometric seam.
    shade = np.clip(depth / np.maximum(available, 1), 0, 1)
    mult = np.where(shade < .40, 1., np.where(shade < .64, .91, np.where(shade < .82, .80, .69)))
    a[:, :, :3] = (a[:, :, :3].astype(np.float32) * mult[:, :, None]).astype(np.uint8)
    # Cyan atmospheric bands follow the same continuously curved horizon.
    for lo, hi, color, opacity in [(-18, -13, (11, 58, 133), 58), (-13, -8, (18, 125, 202), 100),
                                   (-8, -4, (29, 201, 247), 170), (-4, -1, (106, 242, 255), 225),
                                   (-1, 1, (227, 254, 255), 255)]:
        mask = (depth >= lo) & (depth < hi)
        a[mask, :3] = color
        a[mask, 3] = opacity
    out = Image.fromarray(a, 'RGBA')
    d = ImageDraw.Draw(out)
    # Separated atmospheric glints and tower beacons on the generated terrain.
    for px, offset in [(147, 79), (311, 112), (689, 92), (944, 152), (1143, 77), (1380, 128), (1758, 95), (1901, 121)]:
        py = int(horizon[px] + offset)
        d.line((px, py - 14, px, py + 5), fill=(*INK, 255), width=2)
        d.rectangle((px - 2, py - 16, px + 2, py - 13), fill=(*(MAGENTA if px % 3 else CYAN), 255))
    out.save(ROOT / 'frost_planet_limb.png', optimize=True)
    return out


def cloud(name, output, dark=False):
    art = source(name, 48 if dark else 56).convert('RGB')
    # Preserve the asymmetric generated billows. Stitch corresponding border
    # pixels directly, then re-index to a small flat palette; no spatial blur.
    crop = art.crop((0, 250, art.width, 875))
    a = np.asarray(crop.resize((2048, 1024), NEAREST), dtype=np.uint8).copy()
    bw = 192
    for k in range(bw):
        left = a[:, k].astype(np.float32)
        right = a[:, -1 - k].astype(np.float32)
        t = .5 + .5 * k / (bw - 1)
        a[:, k] = np.rint(t * left + (1 - t) * right).astype(np.uint8)
        a[:, -1 - k] = np.rint(t * right + (1 - t) * left).astype(np.uint8)
    bh = 128
    for k in range(bh):
        top = a[k].astype(np.float32)
        bottom = a[-1 - k].astype(np.float32)
        t = .5 + .5 * k / (bh - 1)
        a[k] = np.rint(t * top + (1 - t) * bottom).astype(np.uint8)
        a[-1 - k] = np.rint(t * bottom + (1 - t) * top).astype(np.uint8)
    a[:, -1] = a[:, 0]
    a[-1] = a[0]
    a = np.asarray(Image.fromarray(a, 'RGB').quantize(colors=48 if dark else 56, dither=Image.Dither.NONE).convert('RGB'), dtype=np.uint8).copy()
    # A few discrete lit crystals sit on actual cloud crests.
    out = Image.fromarray(a, 'RGB').convert('RGBA')
    d = ImageDraw.Draw(out)
    rng = random.Random(441 if dark else 442)
    for _ in range(90 if dark else 150):
        px = rng.randrange(4, 2044)
        py = rng.randrange(4, 1020)
        r, g, b = a[py, px]
        if max(r, g, b) < (105 if dark else 170):
            continue
        c = (125, 215, 253, 255) if dark else (234, 253, 255, 255)
        d.point((px, py), fill=c)
        if rng.random() < .24:
            d.line((px - 2, py, px + 2, py), fill=c, width=1)
    a = np.asarray(out, dtype=np.uint8).copy()
    a[:, -1] = a[:, 0]
    a[-1] = a[0]
    out = Image.fromarray(a, 'RGBA')
    out.save(ROOT / output, optimize=True)
    return out


def entry():
    base = source('entry_source.png', 48).resize((512, 768), NEAREST)
    sheet = Image.new('RGBA', (3072, 1024))
    frames = []
    for f in range(6):
        cell = Image.new('RGBA', (512, 1024))
        # Row-wise integer shifts animate the generated tongues without blur.
        src = np.asarray(base, dtype=np.uint8)
        shifted = np.zeros_like(src)
        for py in range(768):
            move = int(round((3 + py / 26) * math.sin(py / 43 + f * math.pi / 3)))
            if move > 0:
                shifted[py, move:] = src[py, :512 - move]
            elif move < 0:
                shifted[py, :move] = src[py, -move:]
            else:
                shifted[py] = src[py]
        cell.alpha_composite(Image.fromarray(shifted, 'RGBA'), (0, 170))
        d = ImageDraw.Draw(cell)
        rng = random.Random(904 + f)
        # Asymmetric additional broken tongues and ice motes, placed per frame.
        for j in range(14):
            py = rng.randrange(390, 920)
            side = rng.choice((-1, 1))
            px = 256 + side * rng.randrange(55, 165)
            length = rng.randrange(12, 58)
            c = (*((70, 213, 255) if j % 3 else (255, 77, 195)), rng.randrange(110, 230))
            d.line([(px, py), (px + side * 9, py + length // 2), (px + side * 3, py + length)], fill=c, width=rng.choice((1, 2, 3)))
        for j in range(90):
            px = rng.randrange(37, 475)
            py = rng.randrange(255, 962)
            if rng.random() < .5:
                d.point((px, py), fill=(*WHITE, rng.randrange(90, 255)))
            else:
                d.line((px, py, px + rng.choice((-1, 1)) * 2, py + rng.randrange(2, 8)), fill=(*CYAN, 130), width=1)
        # Keep the ship socket clean and prevent cut-off at cell borders.
        d.ellipse((212, 256, 300, 344), fill=(0, 0, 0, 0))
        d.rectangle((0, 0, 11, 1023), fill=(0, 0, 0, 0))
        d.rectangle((500, 0, 511, 1023), fill=(0, 0, 0, 0))
        d.rectangle((0, 0, 511, 8), fill=(0, 0, 0, 0))
        d.rectangle((0, 1008, 511, 1023), fill=(0, 0, 0, 0))
        cell = stepped_alpha(cell)
        frames.append(cell)
        sheet.alpha_composite(cell, (f * 512, 0))
    sheet.save(ROOT / 'frost_entry_fx.png', optimize=True)
    return frames


def breakthrough():
    src = source('breakthrough_source.png', 56)
    bbox = src.getchannel('A').point(lambda x: 255 if x > 20 else 0).getbbox()
    src = src.crop(bbox)
    sheet = Image.new('RGBA', (5120, 1024))
    frames = []
    for f, size in enumerate((410, 575, 730, 850, 940)):
        cell = Image.new('RGBA', (1024, 1024))
        art = src.resize((size, size), NEAREST)
        if f >= 2:
            a = art.getchannel('A').point(lambda v: int(v * (1.0 if f == 2 else .78 if f == 3 else .54)))
            art.putalpha(a)
        cell.alpha_composite(art, ((1024 - size) // 2, (1024 - size) // 2))
        if f <= 1:
            d = ImageDraw.Draw(cell)
            r = 39 if f == 0 else 61
            d.ellipse((512 - r, 512 - r, 512 + r, 512 + r), fill=(*WHITE, 255))
            for j in range(20):
                angle = j * math.tau / 20
                inner = r + 2
                outer = r + (43 if f == 0 else 59) + (j % 3) * 11
                p = (512 + math.cos(angle) * inner, 512 + math.sin(angle) * inner,
                     512 + math.cos(angle) * outer, 512 + math.sin(angle) * outer)
                d.line(p, fill=(*CYAN, 220), width=2)
        cell = stepped_alpha(cell)
        frames.append(cell)
        sheet.alpha_composite(cell, (f * 1024, 0))
    sheet.save(ROOT / 'frost_breakthrough.png', optimize=True)
    return frames


def streaks():
    w, h = 1024, 2048
    out = Image.new('RGBA', (w, h))
    d = ImageDraw.Draw(out)
    rng = random.Random(40717)
    colors = [(17, 82, 176), (30, 133, 218), (66, 209, 251), (177, 245, 255), (218, 83, 180)]
    for j in range(34):
        px = rng.randrange(28, w - 28)
        py = rng.randrange(h)
        length = rng.randrange(330, 900)
        bend = rng.randrange(-17, 18)
        points = [(px + int(math.sin(t * 2.4 + j) * (7 + j % 8) + bend * t), py + int(length * t))
                  for t in (0, .2, .4, .6, .8, 1)]
        for wrap in (-h, 0, h):
            shifted = [(x, y + wrap) for x, y in points]
            d.line(shifted, fill=(48, 157, 226, 54), width=8 if j % 3 else 11)
            d.line(shifted, fill=(150, 232, 255, 118), width=2)
    for j in range(570):
        px = rng.randrange(12, w - 12)
        py = rng.randrange(h)
        length = rng.randrange(65, 620) if j % 4 else rng.randrange(400, 950)
        slant = rng.randrange(-14, 15)
        c = colors[rng.choices(range(5), weights=(24, 32, 30, 10, 4))[0]]
        alpha = rng.randrange(45, 155) if j % 6 else rng.randrange(140, 225)
        width = 1 if j % 11 else rng.choice((2, 3))
        for wrap in (-h, 0, h):
            d.line((px, py + wrap, px + slant, py + length + wrap), fill=(*c, alpha), width=width)
            if j % 17 == 0:
                d.line((px + 3, py + 35 + wrap, px + slant + 3, py + length - 20 + wrap), fill=(*CYAN, alpha // 3), width=1)
    a = np.asarray(out, dtype=np.uint8).copy()
    a[-1] = a[0]
    out = Image.fromarray(a, 'RGBA')
    out.save(ROOT / 'frost_entry_streaks.png', optimize=True)
    return out


def preview(planet_art, limb_art, cloud_art, dark_art, entry_frames, burst_frames, streak_art):
    canvas = Image.new('RGBA', (1600, 1530), '#07101f')
    d = ImageDraw.Draw(canvas)
    items = [
        ('PLANET', planet_art, (20, 30, 520, 530)),
        ('CLOSE APPROACH', limb_art, (545, 30, 1580, 530)),
        ('CLOUD DECK', cloud_art, (20, 570, 770, 930)),
        ('DEEP CLOUD', dark_art, (810, 570, 1560, 930)),
        ('ENTRY HEAT · SIX FRAMES', Image.open(ROOT / 'frost_entry_fx.png'), (20, 970, 930, 1320)),
        ('BREAKTHROUGH · FIVE FRAMES', Image.open(ROOT / 'frost_breakthrough.png'), (950, 970, 1580, 1260)),
        ('SPEED STREAKS', streak_art, (1150, 1290, 1550, 1510)),
    ]
    for label, art, (x0, y0, x1, y1) in items:
        d.text((x0, y0 - 16), label, fill='#aaf2fb')
        thumb = art.copy()
        thumb.thumbnail((x1 - x0, y1 - y0), NEAREST)
        canvas.alpha_composite(thumb, (x0 + (x1 - x0 - thumb.width) // 2, y0))
    canvas.save(ROOT / 'preview.png', optimize=True)
    gif = []
    for frame in entry_frames:
        bg = Image.new('RGB', frame.size, '#07101f')
        bg.paste(frame, (0, 0), frame)
        gif.append(bg.resize((256, 512), NEAREST))
    gif[0].save(ROOT / 'preview.gif', save_all=True, append_images=gif[1:], duration=85, loop=0, optimize=False)


def main():
    p = planet()
    l = limb()
    c = cloud('cloud_source.png', 'frost_cloud_deck.png')
    cd = cloud('cloud_dark_source.png', 'frost_cloud_deck_dark.png', True)
    e = entry()
    b = breakthrough()
    s = streaks()
    preview(p, l, c, cd, e, b, s)
    manifest_path = ROOT / 'manifest.json'
    manifest = json.loads(manifest_path.read_text())
    manifest['palette'] = 'Frost icebreaker steel and ice-white/cyan glaciers; indigo-violet shadow, magenta and copper relay lights'
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')


if __name__ == '__main__':
    main()
