"""Build the Iron Kraken damage/death sheets from the selected painted keys.

All resampling is nearest-neighbour. Run from any working directory with Pillow
and NumPy installed. Source paintings are kept alongside this script.
"""
from __future__ import annotations

import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

SRC = Path(__file__).resolve().parent
OUT = SRC.parent
CELL = 384
BG = '#0b0b1a'
MINT = (124, 242, 192, 255)
WHITE = (239, 255, 247, 255)
VIOLET = (114, 118, 255, 255)


def clean(im: Image.Image) -> Image.Image:
    """Remove imagegen's near-zero alpha dust and keep the world palette."""
    im = im.convert('RGBA')
    arr = np.asarray(im).copy()
    arr[:, :, 3][arr[:, :, 3] < 72] = 0
    # HSV hue edits apply only to strongly saturated texels. This keeps the
    # weathered brown material while excluding the player-red band.
    hsv = np.asarray(im.convert('HSV')).copy()
    h, s, v = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]
    visible = arr[:, :, 3] > 24
    red = visible & (s > 105) & ((h < 11) | (h >= 245))
    h[red] = 18  # 25 degrees: rust-brown, not red
    lime = visible & (s > 155) & (v > 125) & (h >= 48) & (h < 100)
    h[lime] = 110  # 155 degrees: mint
    corrected = np.asarray(Image.fromarray(hsv, 'HSV').convert('RGB'))
    arr[:, :, :3] = corrected
    arr[:, :, :3][arr[:, :, 3] == 0] = 0
    return Image.fromarray(arr, 'RGBA')


def content_box(im: Image.Image, threshold: int = 128) -> tuple[int, int, int, int]:
    a = np.asarray(im.getchannel('A'))
    yy, xx = np.where(a > threshold)
    return int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1


def register_body(name: str, eye_y: int) -> Image.Image:
    """Pin the silhouette and eye to the pristine cell's game-space anchor."""
    im = clean(Image.open(SRC / name))
    x0, y0, x1, y1 = content_box(im)
    im = im.crop((x0, y0, x1, y1))
    split = max(1, min(im.height - 1, eye_y - y0))
    # The generated paintings place the larger exposed dome above the eye.
    # Separate nearest-neighbour stretches keep the eye at y=126 in all four.
    top = im.crop((0, 0, im.width, split)).resize((348, 106), Image.Resampling.NEAREST)
    bottom = im.crop((0, split, im.width, im.height)).resize((348, 236), Image.Resampling.NEAREST)
    cell = Image.new('RGBA', (CELL, CELL))
    cell.alpha_composite(top, (18, 20))
    cell.alpha_composite(bottom, (18, 126))
    return cell


def body_variant(src: Image.Image, stage: int, variant: int) -> Image.Image:
    out = src.copy()
    if not variant:
        return out
    d = ImageDraw.Draw(out, 'RGBA')
    # The loose cable swings by two pixels; a stressed lamp and reactor flicker.
    cable_x = 299 - 2 * stage
    cable_y = 258 + stage * 3
    d.line([(cable_x, cable_y), (cable_x + 4, cable_y + 8),
            (cable_x + 1, cable_y + 14)], fill=(10, 15, 23, 255), width=3)
    d.line([(cable_x, cable_y), (cable_x + 5, cable_y + 8),
            (cable_x + 3, cable_y + 14)], fill=(109, 101, 112, 255), width=1)
    d.point((cable_x + 3, cable_y + 14), fill=MINT)
    d.ellipse((186, 119, 194, 127), fill=(22, 52, 47, 122))
    d.point((189, 121), fill=WHITE if stage < 3 else (99, 189, 156, 255))
    d.line((168, 91, 171, 94), fill=(136, 237, 194, 145), width=1)
    return out


def smoke_frame(k: int) -> Image.Image:
    """Animate three separately registered plumes from the painted FX key."""
    im = Image.new('RGBA', (CELL, CELL))
    source = clean(Image.open(SRC / 'smoke_key.png'))
    specs = [((40, 40, 570, 700), (82, 104), (82, 39), 1),
             ((550, 30, 850, 420), (47, 70), (191, 16), -1),
             ((790, 40, 1230, 700), (82, 104), (225, 39), -1)]
    dx = [-2, -1, 0, 1, 2, 0][k]
    dy = [2, 1, 0, 1, 2, 3][k]
    for crop_box, size, (tx, ty), direction in specs:
        piece = source.crop(crop_box)
        x0, y0, x1, y1 = content_box(piece)
        piece = piece.crop((x0, y0, x1, y1)).resize(size, Image.Resampling.NEAREST)
        im.alpha_composite(piece, (tx + direction*dx, ty + dy))
    d = ImageDraw.Draw(im, 'RGBA')
    rng = random.Random(18300 + k)
    for ox, oy in [(130, 133), (263, 133)]:
        for q in range(4):
            bx = ox + rng.randrange(-8, 9)
            by = oy - 18 - ((q*13+k*5) % 39)
            d.ellipse((bx-2, by-2, bx+2, by+2), outline=(171, 243, 210, 210), width=1)
    return im


def electric_frame(k: int) -> Image.Image:
    im = Image.new('RGBA', (CELL, CELL))
    d = ImageDraw.Draw(im, 'RGBA')
    rng = random.Random(4910 + k)
    pairs = [((110, 176), (151, 83)), ((245, 78), (323, 167)),
             ((86, 240), (152, 188)), ((239, 188), (323, 259)),
             ((138, 109), (222, 74)), ((93, 280), (159, 261))]
    for n in range(3 + (k % 2)):
        (x0, y0), (x1, y1) = pairs[(n + k) % len(pairs)]
        pts = [(x0, y0)]
        for t in range(1, 8):
            f = t / 8
            pts.append((round(x0+(x1-x0)*f+rng.randrange(-7, 8)),
                        round(y0+(y1-y0)*f+rng.randrange(-7, 8))))
        pts.append((x1, y1))
        d.line(pts, fill=(72, 65, 145, 128), width=7, joint='curve')
        d.line(pts, fill=(116, 114, 255, 244), width=3, joint='curve')
        d.line(pts, fill=(230, 255, 249, 255), width=1, joint='curve')
        d.ellipse((x0-3, y0-3, x0+3, y0+3), fill=MINT)
        for q in range(3):
            px = x1+rng.randrange(-11, 12)
            py = y1+rng.randrange(-11, 12)
            d.line((px-3, py, px+3, py), fill=(124, 242, 192, 235), width=1)
            d.line((px, py-3, px, py+3), fill=WHITE, width=1)
    if k in (1, 4):
        x, y = (115, 174) if k == 1 else (264, 201)
        d.ellipse((x-9, y-9, x+9, y+9), outline=(184, 170, 255, 230), width=2)
        d.line((x-12, y, x+12, y), fill=WHITE, width=2)
        d.line((x, y-12, x, y+12), fill=WHITE, width=2)
    return im


def register_death(name: str, margin: int = 8) -> Image.Image:
    im = clean(Image.open(SRC / name))
    x0, y0, x1, y1 = content_box(im)
    im = im.crop((x0, y0, x1, y1))
    inner = CELL - 2 * margin
    scale = min(inner / im.width, inner / im.height)
    size = (max(1, round(im.width * scale)), max(1, round(im.height * scale)))
    im = im.resize(size, Image.Resampling.NEAREST)
    out = Image.new('RGBA', (CELL, CELL))
    out.alpha_composite(im, ((CELL-size[0])//2, (CELL-size[1])//2))
    return out


def flash_body(body: Image.Image) -> Image.Image:
    im = body.copy()
    d = ImageDraw.Draw(im, 'RGBA')
    anchors = [(192, 122), (169, 82), (234, 91), (141, 167),
               (268, 175), (113, 234), (279, 247)]
    for x, y in anchors:
        d.ellipse((x-8, y-8, x+8, y+8), fill=(99, 238, 185, 150))
        d.ellipse((x-4, y-4, x+4, y+4), fill=WHITE)
        d.line((x-14, y, x+14, y), fill=(195, 255, 231, 230), width=2)
        d.line((x, y-14, x, y+14), fill=(195, 255, 231, 230), width=2)
    d.ellipse((174, 105, 210, 141), fill=(124, 242, 192, 190))
    d.ellipse((183, 114, 201, 132), fill=WHITE)
    return im


def on_bg(im: Image.Image) -> Image.Image:
    bg = Image.new('RGBA', (CELL, CELL), BG)
    bg.alpha_composite(im)
    return bg.convert('RGB')


def main() -> None:
    pristine = Image.open(OUT / '../Tide~/Tide.png').convert('RGBA').crop((0, 0, CELL, CELL))
    picks = [('stage1_b.png', 418), ('stage2_b.png', 423),
             ('stage3_a.png', 480), ('stage4_a.png', 425)]
    bodies = [register_body(name, eye_y) for name, eye_y in picks]
    body_sheet = Image.new('RGBA', (CELL*2, CELL*4))
    for stage, body in enumerate(bodies, 1):
        for variant in range(2):
            body_sheet.alpha_composite(body_variant(body, stage, variant),
                                       (variant*CELL, (stage-1)*CELL))
    body_sheet.save(OUT / 'Tide_damage.png')

    smoke = [smoke_frame(k) for k in range(6)]
    electric = [electric_frame(k) for k in range(6)]
    fx_sheet = Image.new('RGBA', (CELL*6, CELL*2))
    for k in range(6):
        fx_sheet.alpha_composite(smoke[k], (k*CELL, 0))
        fx_sheet.alpha_composite(electric[k], (k*CELL, CELL))
    fx_sheet.save(OUT / 'Tide_damage_fx.png')

    death = [flash_body(bodies[-1]), register_death('death_split.png', 12),
             register_death('death_overload.png', 7),
             register_death('death_peak.png', 7),
             register_death('death_shockwave.png', 8),
             register_death('death_fade.png', 12)]
    death_sheet = Image.new('RGBA', (CELL*6, CELL))
    for k, frame in enumerate(death):
        death_sheet.alpha_composite(frame, (k*CELL, 0))
    death_sheet.save(OUT / 'Tide_death.png')

    preview = Image.new('RGB', (CELL*2*5, CELL*2), BG)
    for k, frame in enumerate([pristine] + bodies):
        view = frame.copy()
        if k:
            view.alpha_composite(smoke[2])
            view.alpha_composite(electric[3])
        preview.paste(on_bg(view).resize((CELL*2, CELL*2), Image.Resampling.NEAREST),
                      (k*CELL*2, 0))
    preview.save(OUT / 'preview.png')

    gif_frames = []
    for k in range(12):
        frame = body_variant(bodies[-1], 4, k // 6)
        frame.alpha_composite(smoke[k % 6])
        frame.alpha_composite(electric[k % 6])
        gif_frames.append(on_bg(frame).resize((CELL*2, CELL*2), Image.Resampling.NEAREST))
    gif_frames[0].save(OUT / 'preview.gif', save_all=True,
                       append_images=gif_frames[1:], duration=100, loop=0,
                       optimize=False, disposal=2)

    death_preview = Image.new('RGB', (CELL*2*6, CELL*2), BG)
    for k, frame in enumerate(death):
        death_preview.paste(on_bg(frame).resize((CELL*2, CELL*2), Image.Resampling.NEAREST),
                            (k*CELL*2, 0))
    death_preview.save(OUT / 'Tide_death_preview.png')
    print('Built Tide damage, effects, death, and previews')


if __name__ == '__main__':
    main()
