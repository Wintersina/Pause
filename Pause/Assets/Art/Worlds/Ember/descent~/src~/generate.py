"""Build Ember planetfall art from the selected painted sources.

Run: python3 Pause/Assets/Art/Worlds/Ember/descent/src~/generate.py
All spatial resampling is nearest-neighbour. No blur or spatial dithering.
"""
from pathlib import Path
import colorsys
import json
import math
import random

import numpy as np
from PIL import Image, ImageDraw

SRC = Path(__file__).resolve().parent
OUT = SRC.parent
NEAREST = Image.Resampling.NEAREST
GOLD = (255, 184, 45)
WHITE = (255, 250, 206)
AMBER = (238, 136, 31)
INK = (24, 17, 22)


def indexed(image, colors=72):
    """Index generated clusters and keep saturated hues away from ship red."""
    rgba = image.convert('RGBA')
    alpha = rgba.getchannel('A')
    rgb = rgba.convert('RGB').quantize(colors=colors, dither=Image.Dither.NONE).convert('RGB')
    palette = np.asarray(rgb, dtype=np.uint8).copy()
    unique, inverse = np.unique(palette.reshape(-1, 3), axis=0, return_inverse=True)
    for i, color in enumerate(unique):
        h, s, v = colorsys.rgb_to_hsv(*(float(c) / 255 for c in color))
        if s > .20 and v > .10:
            deg = h * 360
            if deg < 18 or deg > 332:
                h = (20 if deg < 18 else 326) / 360
                unique[i] = [round(c * 255) for c in colorsys.hsv_to_rgb(h, s, v)]
    result = Image.fromarray(unique[inverse].reshape(palette.shape), 'RGB')
    result.putalpha(alpha)
    return result


def clean_alpha(image):
    a = np.asarray(image.convert('RGBA'), dtype=np.uint8).copy()
    levels = np.array([0, 80, 160, 224, 255], dtype=np.int16)
    a[:, :, 3] = levels[np.abs(a[:, :, 3, None].astype(np.int16) - levels).argmin(axis=2)]
    a[a[:, :, 3] == 0, :3] = 0
    return Image.fromarray(a, 'RGBA')


def save(image, name):
    if name in ('ember_planet_limb.png', 'ember_cloud_deck.png', 'ember_cloud_deck_dark.png'):
        image = guard_hue(image)
    image.save(OUT / name, optimize=True)
    return image


def guard_hue(image):
    """Keep dark ash and blended edge colors clear of the player's red."""
    a = np.asarray(image.convert('RGBA'), dtype=np.uint8).copy()
    rgb = a[:, :, :3]
    unique, inverse = np.unique(rgb.reshape(-1, 3), axis=0, return_inverse=True)
    for i, color in enumerate(unique):
        h, s, v = colorsys.rgb_to_hsv(*(float(c) / 255 for c in color))
        deg = h * 360
        if s > .20 and v > .10 and (deg < 18 or deg > 332):
            # Shadow stays violet; lava and atmospheric light stay amber.
            target = 285 if v < .30 and color[2] >= color[1] else 20
            unique[i] = [round(c * 255) for c in colorsys.hsv_to_rgb(target / 360, s, v)]
    a[:, :, :3] = unique[inverse].reshape(rgb.shape)
    a[a[:, :, 3] == 0, :3] = 0
    return Image.fromarray(a, 'RGBA')


def planet():
    # A uniform scale leaves the fragmented ring inside safe transparent margins.
    source = indexed(Image.open(SRC / 'planet_source.png'), 88)
    art = source.resize((928, 964), NEAREST)
    out = Image.new('RGBA', (1024, 1024))
    out.alpha_composite(art, (48, 30))
    return save(clean_alpha(out), 'ember_planet.png')


def limb():
    terrain = np.asarray(indexed(Image.open(SRC / 'limb_surface_source.png'), 88).convert('RGB'))
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
    shade = np.clip(depth / np.maximum(available, 1), 0, 1)
    mult = np.where(shade < .34, 1., np.where(shade < .59, .89, np.where(shade < .79, .78, .67)))
    a[:, :, :3] = (a[:, :, :3].astype(np.float32) * mult[:, :, None]).astype(np.uint8)
    for lo, hi, color, opacity in [
        (-20, -14, (85, 45, 22), 55),
        (-14, -9, (150, 62, 19), 100),
        (-9, -5, (216, 104, 20), 160),
        (-5, -2, (255, 173, 45), 215),
        (-2, 1, (255, 237, 152), 255),
    ]:
        mask = (depth >= lo) & (depth < hi)
        a[mask, :3] = color
        a[mask, 3] = opacity
    out = Image.fromarray(a, 'RGBA')
    draw = ImageDraw.Draw(out)
    for px, offset in [(141, 68), (307, 95), (661, 72), (945, 111), (1167, 88), (1399, 113), (1719, 78), (1909, 106)]:
        py = int(horizon[px] + offset)
        draw.line((px, py - 16, px, py + 5), fill=(*INK, 255), width=2)
        draw.rectangle((px - 2, py - 19, px + 2, py - 15), fill=(*(GOLD if px % 3 else WHITE), 255))
    rng = random.Random(827)
    for _ in range(240):
        px = rng.randrange(12, w - 12)
        py = int(horizon[px] + rng.randrange(38, max(39, (h - horizon[px]) // 2)))
        draw.point((px, py), fill=(*(WHITE if rng.randrange(6) == 0 else GOLD), 255))
    return save(out, 'ember_planet_limb.png')


def cloud(source_name, out_name, dark=False):
    art = indexed(Image.open(SRC / source_name), 56 if dark else 64).convert('RGB')
    if dark:
        crop = art.crop((0, 120, art.width, 1130))
    else:
        crop = art.crop((0, 80, art.width, 944))
    a = np.asarray(crop.resize((2048, 1024), NEAREST), dtype=np.uint8).copy()
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
    rng = random.Random(607 if dark else 608)
    for _ in range(140 if dark else 230):
        px, py = rng.randrange(4, 2044), rng.randrange(4, 1020)
        r, g, b, _ = out.getpixel((px, py))
        if r < (70 if dark else 130) or r <= b:
            continue
        c = (*GOLD, 255) if dark else (*WHITE, 255)
        draw.point((px, py), fill=c)
        if rng.random() < .20:
            draw.line((px - 1, py, px + 1, py), fill=c, width=1)
    a = np.asarray(out, dtype=np.uint8).copy()
    a[:, -1] = a[:, 0]
    a[-1] = a[0]
    return save(Image.fromarray(a, 'RGBA'), out_name)


def entry():
    source = np.asarray(indexed(Image.open(SRC / 'entry_source.png'), 64).resize((400, 640), NEAREST), dtype=np.uint8)
    sheet = Image.new('RGBA', (3072, 1024))
    frames = []
    for f in range(6):
        shifted = np.zeros_like(source)
        for py in range(640):
            move = int(round((3 + max(0, py - 280) / 19) * math.sin(py / 31 + f * math.tau / 6)))
            if move > 0:
                shifted[py, move:] = source[py, :-move]
            elif move < 0:
                shifted[py, :move] = source[py, -move:]
            else:
                shifted[py] = source[py]
        cell = Image.new('RGBA', (512, 1024))
        cell.alpha_composite(Image.fromarray(shifted, 'RGBA'), (56, 180))
        draw = ImageDraw.Draw(cell)
        rng = random.Random(9341 + f)
        # Per-frame flame tongues and flyaway sparks give six distinct silhouettes.
        for j in range(32):
            py = rng.randrange(420, 900)
            side = rng.choice((-1, 1))
            px = 256 + side * rng.randrange(62, 165)
            length = rng.randrange(17, 78)
            color = (*(WHITE if j % 9 == 0 else GOLD if j % 3 == 0 else AMBER), rng.randrange(125, 230))
            draw.line([(px, py), (px + side * 13, py + length // 2), (px + side * 4, py + length)], fill=color, width=rng.choice((1, 2, 3)))
        for j in range(170):
            px, py = rng.randrange(35, 477), rng.randrange(205, 959)
            color = (*(WHITE if j % 5 == 0 else GOLD), rng.randrange(95, 240))
            if j % 7 == 0:
                draw.line((px, py, px + rng.randrange(-4, 5), py + rng.randrange(4, 13)), fill=color, width=1)
            else:
                draw.point((px, py), fill=color)
        # Fixed ellipse matches the Verdant socket. Clear boundary guards follow.
        draw.ellipse((183, 255, 329, 421), fill=(0, 0, 0, 0))
        draw.rectangle((0, 0, 11, 1023), fill=(0, 0, 0, 0))
        draw.rectangle((500, 0, 511, 1023), fill=(0, 0, 0, 0))
        draw.rectangle((0, 0, 511, 11), fill=(0, 0, 0, 0))
        draw.rectangle((0, 1008, 511, 1023), fill=(0, 0, 0, 0))
        phase = (1.00, .91, 1.08, .84, 1.05, .95)[f]
        pixels = np.asarray(cell, dtype=np.uint8).copy()
        pixels[:, :, :3] = np.clip(pixels[:, :, :3].astype(np.float32) * phase, 0, 255).astype(np.uint8)
        cell = Image.fromarray(pixels, 'RGBA')
        cell = clean_alpha(cell)
        sheet.alpha_composite(cell, (f * 512, 0))
        frames.append(cell)
    save(sheet, 'ember_entry_fx.png')
    return frames


def breakthrough():
    source = indexed(Image.open(SRC / 'breakthrough_source.png'), 72)
    alpha = source.getchannel('A').point(lambda v: 255 if v > 35 else 0)
    bbox = alpha.getbbox()
    source = source.crop(bbox)
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
            radius = (38, 59, 71)[f]
            draw.ellipse((512 - radius, 512 - radius, 512 + radius, 512 + radius), fill=(*WHITE, 255))
            if f == 0:
                for j in range(24):
                    angle = j * math.tau / 24
                    draw.line((512 + math.cos(angle) * 40, 512 + math.sin(angle) * 40,
                               512 + math.cos(angle) * 105, 512 + math.sin(angle) * 105), fill=(*GOLD, 224), width=2)
        cell = clean_alpha(cell)
        sheet.alpha_composite(cell, (f * 1024, 0))
        frames.append(cell)
    save(sheet, 'ember_breakthrough.png')
    return frames


def streaks():
    w, h = 1024, 2048
    out = Image.new('RGBA', (w, h))
    draw = ImageDraw.Draw(out)
    rng = random.Random(73148)
    colors = [(91, 48, 28), (169, 81, 24), (235, 139, 33), (255, 211, 111), WHITE]
    for j in range(48):
        px, py = rng.randrange(26, w - 26), rng.randrange(h)
        length = rng.randrange(440, 1050)
        bend = rng.randrange(-27, 28)
        points = [(px + int(math.sin(t * 2.7 + j) * (6 + j % 9) + bend * t), py + int(length * t)) for t in (0, .2, .4, .6, .8, 1)]
        for wrap in (-h, 0, h):
            shifted = [(xx, yy + wrap) for xx, yy in points]
            draw.line(shifted, fill=(180, 78, 25, 55), width=10 if j % 3 else 14)
            draw.line(shifted, fill=(255, 182, 61, 130), width=2)
    for j in range(850):
        px, py = rng.randrange(12, w - 12), rng.randrange(h)
        length = rng.randrange(75, 510) if j % 4 else rng.randrange(430, 1080)
        slant = rng.randrange(-19, 20)
        color = colors[rng.choices(range(5), weights=(19, 29, 32, 16, 4))[0]]
        opacity = rng.randrange(43, 154) if j % 6 else rng.randrange(145, 225)
        for wrap in (-h, 0, h):
            draw.line((px, py + wrap, px + slant, py + length + wrap), fill=(*color, opacity), width=1 if j % 11 else 2)
            if j % 23 == 0:
                draw.point((px + 3, py + 40 + wrap), fill=(*WHITE, opacity))
    a = np.asarray(out, dtype=np.uint8).copy()
    a[-1] = a[0]
    return save(Image.fromarray(a, 'RGBA'), 'ember_entry_streaks.png')


def preview(art, frames):
    canvas = Image.new('RGBA', (1600, 1530), '#16151a')
    draw = ImageDraw.Draw(canvas)
    items = [
        ('PLANET', art['planet'], (20, 30, 520, 530)),
        ('CLOSE APPROACH', art['limb'], (545, 30, 1580, 530)),
        ('ASH CLOUD DECK', art['cloud'], (20, 570, 770, 930)),
        ('DEEP CLOUD', art['dark'], (810, 570, 1560, 930)),
        ('ENTRY PLASMA · SIX FRAMES', Image.open(OUT / 'ember_entry_fx.png'), (20, 970, 930, 1320)),
        ('BREAKTHROUGH · FIVE FRAMES', Image.open(OUT / 'ember_breakthrough.png'), (950, 970, 1580, 1260)),
        ('SPEED STREAKS', art['streaks'], (1150, 1290, 1550, 1510)),
    ]
    for label, img, (x0, y0, x1, y1) in items:
        draw.text((x0, y0 - 16), label, fill='#ffce79')
        thumb = img.copy()
        thumb.thumbnail((x1 - x0, y1 - y0), NEAREST)
        canvas.alpha_composite(thumb, (x0 + (x1 - x0 - thumb.width) // 2, y0))
    save(canvas, 'preview.png')
    gif = []
    for frame in frames:
        bg = Image.new('RGB', frame.size, '#16151a')
        bg.paste(frame, (0, 0), frame)
        gif.append(bg.resize((256, 512), NEAREST))
    gif[0].save(OUT / 'preview.gif', save_all=True, append_images=gif[1:], duration=85, loop=0, optimize=False)


def clear_span(values, center):
    lo = hi = center
    while lo > 0 and values[lo - 1] == 0:
        lo -= 1
    while hi + 1 < len(values) and values[hi + 1] == 0:
        hi += 1
    return hi - lo + 1


def make_manifest(planet_art):
    # Scan the finished globe's top cardinal. Smoke reaches above its rim;
    # sustained bright amber marks the shell and dark basalt its inner edge.
    rgba = np.asarray(planet_art)
    col = rgba[:, 512]
    alpha = col[:, 3]
    bright = (col[:, 0] >= 220) & (col[:, 1] >= 105) & (alpha >= 160)
    rim_top = int(next(y for y in range(85, 180) if bright[y:y + 5].all()))
    disc_top = int(next(y for y in range(rim_top + 20, 180)
                        if col[y, 0] < 100 and col[y, 1] < 60 and alpha[y] >= 160))
    sheet = np.asarray(Image.open(OUT / 'ember_entry_fx.png'))[:, :, 3]
    holes = []
    for frame in range(6):
        cell = sheet[:, frame * 512:(frame + 1) * 512]
        holes.append([clear_span(cell[338], 256), clear_span(cell[:, 256], 338)])
    files = {}
    def add(name, size, grid, count, use, pivot, **extra):
        files[name] = dict(size=list(size), cell_grid=list(grid), frame_count=count, intended_use=use, pivot_px=list(pivot), **extra)
    add('ember_planet.png', (1024, 1024), (1, 1), 1, 'Space backdrop approach', (512, 512),
        disc_radius_px=512 - disc_top, atmosphere_outer_radius_px=512 - rim_top,
        measurement='top cardinal sustained bright rim and first dark basalt at x=512; ash plume and brass ring excluded')
    add('ember_planet_limb.png', (2048, 1024), (1, 1), 1, 'Close approach curved horizon', (1024, 355),
        horizon_y_at_center_px=355, horizon_y_at_edges_px=722)
    add('ember_cloud_deck.png', (2048, 1024), (1, 1), 1, 'Fast scrolling upper cloud layer; wrap X/Y', (1024, 512), wrap_x=True, wrap_y=True)
    add('ember_cloud_deck_dark.png', (2048, 1024), (1, 1), 1, 'Deep cloud parallax; wrap X/Y', (1024, 512), wrap_x=True, wrap_y=True)
    add('ember_entry_fx.png', (3072, 1024), (6, 1), 6, 'Looping ship-centered entry wake', (256, 300),
        cell_size=[512, 1024], ship_clear_radius_px=44, hole_center_px=[256, 338],
        hole_size_px=[146, 166], hole_center_px_per_frame=[[256, 338]] * 6, hole_size_px_per_frame=holes)
    add('ember_breakthrough.png', (5120, 1024), (5, 1), 5, 'One-shot cloud break shockwave', (512, 512), cell_size=[1024, 1024])
    add('ember_entry_streaks.png', (1024, 2048), (1, 1), 1, 'Vertically repeating dive streak overlay', (512, 1024), wrap_y=True)
    add('preview.png', (1600, 1530), (1, 1), 1, 'Contact sheet', (800, 765))
    add('preview.gif', (256, 512), (6, 1), 6, 'Entry wake loop preview', (128, 150))
    manifest = {'palette': 'Ember basalt/obsidian and brass; amber-white lava, charcoal-violet ash, gold forge lights', 'files': files}
    (OUT / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')


def main():
    art = {
        'planet': planet(), 'limb': limb(),
        'cloud': cloud('cloud_source.png', 'ember_cloud_deck.png'),
        'dark': cloud('cloud_dark_source.png', 'ember_cloud_deck_dark.png', True),
    }
    frames = entry()
    breakthrough()
    art['streaks'] = streaks()
    preview(art, frames)
    make_manifest(art['planet'])


if __name__ == '__main__':
    main()
