"""Build the Verdant planetfall sprites from the selected painted sources.

Run from anywhere: python3 Pause/Assets/Art/Worlds/Verdant/descent/src~/generate.py
Every resize uses nearest-neighbour sampling; color indexing uses no dithering.
"""
from pathlib import Path
import json
import math
import random

import numpy as np
from PIL import Image, ImageDraw

SRC = Path(__file__).resolve().parent
OUT = SRC.parent
NEAREST = Image.Resampling.NEAREST
LIME = (214, 255, 72)
WHITE = (250, 255, 211)
TEAL = (31, 179, 137)
PINK = (255, 75, 171)
INK = (9, 34, 39)


def indexed(image, colors=64):
    """Keep the painted clusters while reducing accidental color noise."""
    rgba = image.convert('RGBA')
    alpha = rgba.getchannel('A')
    rgb = rgba.convert('RGB').quantize(colors=colors, dither=Image.Dither.NONE).convert('RGB')
    rgb.putalpha(alpha)
    return rgb


def clean_alpha(image):
    a = np.asarray(image.convert('RGBA'), dtype=np.uint8).copy()
    choices = np.array([0, 80, 160, 224, 255], dtype=np.int16)
    aa = a[:, :, 3].astype(np.int16)
    a[:, :, 3] = choices[np.abs(aa[:, :, None] - choices).argmin(axis=2)]
    a[a[:, :, 3] == 0, :3] = 0
    return Image.fromarray(a, 'RGBA')


def save(image, name):
    image.save(OUT / name, optimize=True)
    return image


def planet():
    # The chosen source places its globe near (626, 614); registration preserves
    # that center while retaining the outer root/leaf trellis and a safe margin.
    art = indexed(Image.open(SRC / 'planet_source.png'), 80).resize((976, 976), NEAREST)
    out = Image.new('RGBA', (1024, 1024))
    out.alpha_composite(art, (24, 34))
    return save(clean_alpha(out), 'verdant_planet.png')


def limb():
    terrain = np.asarray(indexed(Image.open(SRC / 'limb_surface_source.png'), 80).convert('RGB'))
    w, h = 2048, 1024
    x = np.arange(w, dtype=np.float32)
    y = np.arange(h, dtype=np.float32)[:, None]
    horizon = np.rint(355 + .00035 * (x - 1024) ** 2).astype(np.int32)
    depth = y - horizon[None, :]
    sx = np.minimum(terrain.shape[1] - 1, (x * terrain.shape[1] / w).astype(np.int32))
    available = (h - 1 - horizon)[None, :]
    sy = np.clip((np.maximum(depth, 0) * (terrain.shape[0] - 1) / available).astype(np.int32), 0, terrain.shape[0] - 1)
    a = np.zeros((h, w, 4), dtype=np.uint8)
    a[:, :, :3] = terrain[sy, sx[None, :]]
    a[:, :, 3] = np.where(depth >= 0, 255, 0).astype(np.uint8)
    # Stepped aerial extinction leaves the near horizon luminous and the near
    # foreground deep teal without any spatial blur.
    shade = np.clip(depth / np.maximum(available, 1), 0, 1)
    mult = np.where(shade < .34, 1., np.where(shade < .59, .87, np.where(shade < .79, .73, .59)))
    a[:, :, :3] = (a[:, :, :3].astype(np.float32) * mult[:, :, None]).astype(np.uint8)
    for lo, hi, color, opacity in [
        (-20, -14, (15, 72, 69), 55),
        (-14, -9, (31, 124, 91), 100),
        (-9, -5, (69, 191, 88), 160),
        (-5, -2, (164, 247, 93), 215),
        (-2, 1, (225, 255, 143), 255),
    ]:
        mask = (depth >= lo) & (depth < hi)
        a[mask, :3] = color
        a[mask, 3] = opacity
    out = Image.fromarray(a, 'RGBA')
    draw = ImageDraw.Draw(out)
    for px, offset in [(141, 68), (307, 95), (661, 72), (945, 111), (1167, 88), (1399, 113), (1719, 78), (1909, 106)]:
        py = int(horizon[px] + offset)
        draw.line((px, py - 16, px, py + 5), fill=(*INK, 255), width=2)
        draw.rectangle((px - 2, py - 19, px + 2, py - 15), fill=(*(PINK if px % 3 else LIME), 255))
    # Sparse distant pollen lights across the generated districts.
    rng = random.Random(758)
    for _ in range(220):
        px = rng.randrange(12, w - 12)
        py = int(horizon[px] + rng.randrange(38, max(39, (h - horizon[px]) // 2)))
        draw.point((px, py), fill=(*(PINK if rng.randrange(5) == 0 else LIME), 220))
    return save(out, 'verdant_planet_limb.png')


def cloud(source_name, out_name, dark=False):
    art = indexed(Image.open(SRC / source_name), 56 if dark else 64).convert('RGB')
    if dark:
        crop = art.crop((0, 313, art.width, 940))
    else:
        crop = art.crop((0, 128, art.width, 896))
    a = np.asarray(crop.resize((2048, 1024), NEAREST), dtype=np.uint8).copy()
    # Stitch opposing edge bands. Exact edge pixels are copied again after the
    # final palette pass, making the PNG toroidal on both axes.
    for k in range(192):
        left = a[:, k].astype(np.float32)
        right = a[:, -1 - k].astype(np.float32)
        t = .5 + .5 * k / 191
        a[:, k] = np.rint(t * left + (1 - t) * right).astype(np.uint8)
        a[:, -1 - k] = np.rint(t * right + (1 - t) * left).astype(np.uint8)
    for k in range(128):
        top = a[k].astype(np.float32)
        bottom = a[-1 - k].astype(np.float32)
        t = .5 + .5 * k / 127
        a[k] = np.rint(t * top + (1 - t) * bottom).astype(np.uint8)
        a[-1 - k] = np.rint(t * bottom + (1 - t) * top).astype(np.uint8)
    out = Image.fromarray(a, 'RGB').quantize(colors=56 if dark else 64, dither=Image.Dither.NONE).convert('RGBA')
    draw = ImageDraw.Draw(out)
    rng = random.Random(322 if dark else 321)
    for _ in range(110 if dark else 170):
        px, py = rng.randrange(4, 2044), rng.randrange(4, 1020)
        r, g, b, _ = out.getpixel((px, py))
        if g < (100 if dark else 150) or g <= r:
            continue
        color = (*PINK, 255) if dark and rng.randrange(3) == 0 else (*WHITE, 255)
        draw.point((px, py), fill=color)
        if rng.random() < .22:
            draw.line((px - 1, py, px + 1, py), fill=color, width=1)
    a = np.asarray(out, dtype=np.uint8).copy()
    a[:, -1] = a[:, 0]
    a[-1] = a[0]
    return save(Image.fromarray(a, 'RGBA'), out_name)


def entry():
    src = indexed(Image.open(SRC / 'entry_source.png'), 64).resize((380, 768), NEAREST)
    src = np.asarray(src, dtype=np.uint8)
    sheet = Image.new('RGBA', (3072, 1024))
    frames = []
    for f in range(6):
        shifted = np.zeros_like(src)
        for py in range(768):
            move = int(round((4 + py / 42) * math.sin(py / 37 + f * math.tau / 6)))
            if move > 0:
                shifted[py, move:] = src[py, :-move]
            elif move < 0:
                shifted[py, :move] = src[py, -move:]
            else:
                shifted[py] = src[py]
        cell = Image.new('RGBA', (512, 1024))
        cell.alpha_composite(Image.fromarray(shifted, 'RGBA'), (66, 190))
        draw = ImageDraw.Draw(cell)
        rng = random.Random(9042 + f)
        # Six distinct tongues and independently moving seed/pollen particles.
        for j in range(23):
            py = rng.randrange(345, 895)
            side = rng.choice((-1, 1))
            px = 256 + side * rng.randrange(48, 147)
            length = rng.randrange(15, 66)
            color = (*(PINK if j % 5 == 0 else TEAL if j % 3 == 0 else LIME), rng.randrange(120, 230))
            draw.line([(px, py), (px + side * 12, py + length // 2), (px + side * 3, py + length)], fill=color, width=rng.choice((1, 2, 3)))
        for j in range(115):
            px, py = rng.randrange(48, 464), rng.randrange(230, 955)
            if rng.randrange(7) == 0:
                color = (*PINK, 220)
            else:
                color = (*(WHITE if j % 3 == 0 else LIME), rng.randrange(95, 240))
            if j % 9 == 0:
                draw.line((px, py, px + rng.randrange(-3, 4), py + rng.randrange(4, 11)), fill=color, width=1)
            else:
                draw.point((px, py), fill=color)
        # Fixed socket for the same ship-fit logic on every loop frame.
        draw.ellipse((183, 255, 329, 421), fill=(0, 0, 0, 0))
        draw.rectangle((0, 0, 11, 1023), fill=(0, 0, 0, 0))
        draw.rectangle((500, 0, 511, 1023), fill=(0, 0, 0, 0))
        draw.rectangle((0, 0, 511, 11), fill=(0, 0, 0, 0))
        draw.rectangle((0, 1008, 511, 1023), fill=(0, 0, 0, 0))
        cell = clean_alpha(cell)
        sheet.alpha_composite(cell, (f * 512, 0))
        frames.append(cell)
    save(sheet, 'verdant_entry_fx.png')
    return frames


def breakthrough():
    source = indexed(Image.open(SRC / 'breakthrough_source.png'), 64)
    alpha = source.getchannel('A').point(lambda v: 255 if v > 35 else 0)
    source = source.crop(alpha.getbbox())
    sheet = Image.new('RGBA', (5120, 1024))
    frames = []
    for f, size in enumerate((400, 565, 720, 850, 940)):
        art = source.resize((size, size), NEAREST)
        if f >= 3:
            art.putalpha(art.getchannel('A').point(lambda v: int(v * (.77 if f == 3 else .53))))
        cell = Image.new('RGBA', (1024, 1024))
        cell.alpha_composite(art, ((1024 - size) // 2, (1024 - size) // 2))
        if f <= 2:
            draw = ImageDraw.Draw(cell)
            radius = (34, 58, 72)[f]
            draw.ellipse((512 - radius, 512 - radius, 512 + radius, 512 + radius), fill=(*WHITE, 255))
            if f == 0:
                for j in range(20):
                    angle = j * math.tau / 20
                    draw.line((512 + math.cos(angle) * 39, 512 + math.sin(angle) * 39,
                               512 + math.cos(angle) * 96, 512 + math.sin(angle) * 96), fill=(*LIME, 224), width=2)
        cell = clean_alpha(cell)
        sheet.alpha_composite(cell, (f * 1024, 0))
        frames.append(cell)
    save(sheet, 'verdant_breakthrough.png')
    return frames


def streaks():
    w, h = 1024, 2048
    out = Image.new('RGBA', (w, h))
    draw = ImageDraw.Draw(out)
    rng = random.Random(49172)
    colors = [(15, 83, 75), (26, 150, 119), (120, 237, 116), (236, 255, 179), (255, 89, 176)]
    for j in range(42):
        px, py = rng.randrange(26, w - 26), rng.randrange(h)
        length = rng.randrange(410, 1010)
        bend = rng.randrange(-25, 26)
        points = [(px + int(math.sin(t * 2.6 + j) * (6 + j % 9) + bend * t), py + int(length * t)) for t in (0, .2, .4, .6, .8, 1)]
        for wrap in (-h, 0, h):
            shifted = [(xx, yy + wrap) for xx, yy in points]
            draw.line(shifted, fill=(36, 180, 113, 58), width=9 if j % 3 else 13)
            draw.line(shifted, fill=(196, 255, 151, 130), width=2)
    for j in range(760):
        px, py = rng.randrange(12, w - 12), rng.randrange(h)
        length = rng.randrange(70, 470) if j % 4 else rng.randrange(400, 1030)
        slant = rng.randrange(-17, 18)
        color = colors[rng.choices(range(5), weights=(22, 31, 30, 13, 4))[0]]
        opacity = rng.randrange(40, 150) if j % 6 else rng.randrange(145, 225)
        for wrap in (-h, 0, h):
            draw.line((px, py + wrap, px + slant, py + length + wrap), fill=(*color, opacity), width=1 if j % 11 else 2)
            if j % 23 == 0:
                draw.point((px + 3, py + 40 + wrap), fill=(*WHITE, opacity))
    a = np.asarray(out, dtype=np.uint8).copy()
    a[-1] = a[0]
    return save(Image.fromarray(a, 'RGBA'), 'verdant_entry_streaks.png')


def preview(art, frames):
    canvas = Image.new('RGBA', (1600, 1530), '#071913')
    draw = ImageDraw.Draw(canvas)
    items = [
        ('PLANET', art['planet'], (20, 30, 520, 530)),
        ('CLOSE APPROACH', art['limb'], (545, 30, 1580, 530)),
        ('SPORE CLOUD DECK', art['cloud'], (20, 570, 770, 930)),
        ('DEEP CLOUD', art['dark'], (810, 570, 1560, 930)),
        ('ENTRY PLASMA · SIX FRAMES', Image.open(OUT / 'verdant_entry_fx.png'), (20, 970, 930, 1320)),
        ('BREAKTHROUGH · FIVE FRAMES', Image.open(OUT / 'verdant_breakthrough.png'), (950, 970, 1580, 1260)),
        ('SPEED STREAKS', art['streaks'], (1150, 1290, 1550, 1510)),
    ]
    for label, img, (x0, y0, x1, y1) in items:
        draw.text((x0, y0 - 16), label, fill='#d5ff92')
        thumb = img.copy()
        thumb.thumbnail((x1 - x0, y1 - y0), NEAREST)
        canvas.alpha_composite(thumb, (x0 + (x1 - x0 - thumb.width) // 2, y0))
    canvas.save(OUT / 'preview.png', optimize=True)
    gif = []
    for frame in frames:
        bg = Image.new('RGB', frame.size, '#071913')
        bg.paste(frame, (0, 0), frame)
        gif.append(bg.resize((256, 512), NEAREST))
    gif[0].save(OUT / 'preview.gif', save_all=True, append_images=gif[1:], duration=85, loop=0, optimize=False)


def make_manifest(planet_art):
    # Scan the unobscured top cardinal of the final globe. The central shell
    # has full alpha; the fainter outer pollen/atmosphere starts earlier.
    alpha = np.asarray(planet_art)[:, :, 3]
    glow_top = int(np.flatnonzero(alpha[:512, 512] > 0)[0])
    # The narrow yellow-green shell ends where dark canopy first crosses the
    # cardinal scan. This gives the visible disc radius, not the root arc span.
    rgb = np.asarray(planet_art)[:, 512, :3]
    inner_candidates = np.flatnonzero((rgb[glow_top + 5:130, 0] < 110) & (rgb[glow_top + 5:130, 1] < 185))
    disc_top = int(glow_top + 5 + inner_candidates[0])
    sheet = np.asarray(Image.open(OUT / 'verdant_entry_fx.png'))[:, :, 3]
    measured_holes = []
    def clear_span(values, center):
        lo = hi = center
        while lo > 0 and values[lo - 1] == 0:
            lo -= 1
        while hi + 1 < len(values) and values[hi + 1] == 0:
            hi += 1
        return hi - lo + 1
    for frame in range(6):
        cell = sheet[:, frame * 512:(frame + 1) * 512]
        measured_holes.append([clear_span(cell[338], 256), clear_span(cell[:, 256], 338)])
    files = {}
    def add(name, size, grid, count, use, pivot, **extra):
        files[name] = dict(size=list(size), cell_grid=list(grid), frame_count=count, intended_use=use, pivot_px=list(pivot), **extra)
    add('verdant_planet.png', (1024, 1024), (1, 1), 1, 'Space backdrop approach', (512, 512),
        disc_radius_px=512 - disc_top, atmosphere_outer_radius_px=512 - glow_top,
        measurement='top cardinal alpha and palette scan at x=512; root trellis excluded')
    add('verdant_planet_limb.png', (2048, 1024), (1, 1), 1, 'Close approach curved horizon', (1024, 355),
        horizon_y_at_center_px=355, horizon_y_at_edges_px=722)
    add('verdant_cloud_deck.png', (2048, 1024), (1, 1), 1, 'Fast scrolling upper cloud layer; wrap X/Y', (1024, 512), wrap_x=True, wrap_y=True)
    add('verdant_cloud_deck_dark.png', (2048, 1024), (1, 1), 1, 'Deep cloud parallax; wrap X/Y', (1024, 512), wrap_x=True, wrap_y=True)
    add('verdant_entry_fx.png', (3072, 1024), (6, 1), 6, 'Looping ship-centered entry wake', (256, 300),
        cell_size=[512, 1024], ship_clear_radius_px=44, hole_center_px=[256, 338],
        hole_size_px=[146, 166], hole_center_px_per_frame=[[256, 338]] * 6,
        hole_size_px_per_frame=measured_holes)
    add('verdant_breakthrough.png', (5120, 1024), (5, 1), 5, 'One-shot cloud break shockwave', (512, 512), cell_size=[1024, 1024])
    add('verdant_entry_streaks.png', (1024, 2048), (1, 1), 1, 'Vertically repeating dive streak overlay', (512, 1024), wrap_y=True)
    add('preview.png', (1600, 1530), (1, 1), 1, 'Contact sheet', (800, 765))
    add('preview.gif', (256, 512), (6, 1), 6, 'Entry wake loop preview', (128, 150))
    manifest = {'palette': 'Verdant corroded copper and emerald/teal canopy; lime-gold rivers and spores, magenta-pink night lights', 'files': files}
    (OUT / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')


def main():
    art = {
        'planet': planet(), 'limb': limb(),
        'cloud': cloud('cloud_source.png', 'verdant_cloud_deck.png'),
        'dark': cloud('cloud_dark_source.png', 'verdant_cloud_deck_dark.png', True),
    }
    frames = entry()
    breakthrough()
    art['streaks'] = streaks()
    preview(art, frames)
    make_manifest(art['planet'])


if __name__ == '__main__':
    main()
