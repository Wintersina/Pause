"""Assemble painted Tempest Engine source art into registered Unity atlases."""
from __future__ import annotations

import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

SRC = Path(__file__).resolve().parent
OUT = SRC.parent
CELL = 384
BG = '#0b0b1a'
WHITE = (242, 246, 255, 255)
YELLOW = (255, 243, 160, 255)
BLUE = (151, 188, 219, 220)


def clean(im: Image.Image, threshold: int = 48) -> Image.Image:
    """Remove generation dust and restrain stray hues without quantizing texture."""
    im = im.convert('RGBA')
    rgba = np.array(im)
    hsv = np.array(im.convert('HSV'))
    h, s, v = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]
    visible = rgba[:, :, 3] > threshold
    bad = visible & (s > 55) & ((h < 17) | (h > 238) | ((h >= 67) & (h < 134)) | ((h >= 176) & (h < 218)))
    h[bad] = 151
    s[bad] = np.minimum(s[bad], 75)
    orange = visible & (s > 55) & (h >= 17) & (h < 35)
    s[orange] = np.minimum(s[orange], 36)
    cyan = visible & (s > 100) & (h >= 134) & (h < 150)
    s[cyan] = 95
    rgba[:, :, :3] = np.asarray(Image.fromarray(hsv, 'HSV').convert('RGB'))
    rgba[:, :, 3][rgba[:, :, 3] < threshold] = 0
    rgba[:, :, :3][rgba[:, :, 3] == 0] = 0
    return Image.fromarray(rgba, 'RGBA')


def content_box(im: Image.Image, threshold: int = 72) -> tuple[int, int, int, int]:
    a = np.asarray(im.getchannel('A'))
    ys, xs = np.where(a > threshold)
    return int(xs.min()), int(ys.min()), int(xs.max()+1), int(ys.max()+1)


def fit(source: Image.Image, box: tuple[int, int, int, int], threshold: int = 72) -> Image.Image:
    im = clean(source)
    im = im.crop(content_box(im, threshold))
    x0, y0, x1, y1 = box
    im = im.resize((x1-x0, y1-y0), Image.Resampling.NEAREST)
    cell = Image.new('RGBA', (CELL, CELL))
    cell.alpha_composite(im, (x0, y0))
    return cell


def variant(body: Image.Image, stage: int) -> Image.Image:
    out = body.copy()
    d = ImageDraw.Draw(out, 'RGBA')
    # A loose cable changes pose, while the failing core flickers.
    x = 289 - stage*3
    y = 147 + stage*5
    d.line([(x, y), (x+3, y+8), (x-1, y+17)], fill=(13, 18, 28, 255), width=3)
    d.line([(x+1, y), (x+4, y+8), (x, y+17)], fill=(134, 153, 170, 255), width=1)
    d.point((x, y+18), fill=YELLOW)
    d.ellipse((185, 235, 199, 250), fill=(114, 158, 199, 120))
    d.ellipse((189, 239, 195, 245), fill=WHITE)
    return out


def smoke_source(i: int) -> Image.Image:
    atlas = Image.open(SRC / 'smoke_candidate_a.png').convert('RGBA')
    w = atlas.width // 3
    return clean(atlas.crop((i*w+4, 0, (i+1)*w-4, atlas.height)), 60)


def smoke_frame(k: int) -> Image.Image:
    cell = Image.new('RGBA', (CELL, CELL))
    motion = [-3, -1, 1, 3, 1, -1][k]
    # Place the smoke over breaches and around, rather than over, the eye.
    for index, size, pos in [(0, (91, 101), (75, 59)),
                             (2, (99, 113), (229, 61)),
                             (1, (46, 75), (286, 156))]:
        source = smoke_source(index)
        source = source.crop(content_box(source, 82)).resize(size, Image.Resampling.NEAREST)
        cell.alpha_composite(source, (pos[0] + motion*(1 if index != 2 else -1),
                                      pos[1] + [1, 0, -1, 0, 1, 2][k]))
    return inside(cell)


def electric_source(i: int) -> Image.Image:
    atlas = Image.open(SRC / 'electric_candidate_a.png').convert('RGBA')
    w = atlas.width // 4
    crop = atlas.crop((i*w+2, 0, (i+1)*w-2, atlas.height))
    a = np.array(crop)
    brightness = np.max(a[:, :, :3], axis=2)
    # The generated blue matte is subdued; retain the painted lightning veins.
    a[:, :, 3] = np.where(brightness > 222, a[:, :, 3],
                           np.where(brightness > 181, a[:, :, 3]//3, 0))
    return clean(Image.fromarray(a, 'RGBA'), 22)


def electric_frame(k: int) -> Image.Image:
    cell = Image.new('RGBA', (CELL, CELL))
    positions = [((56, 107), (90, 103)), ((246, 92), (92, 112)),
                 ((71, 199), (94, 87)), ((244, 193), (91, 94))]
    for n in range(2 + k % 2):
        idx = (k+n) % 4
        src = electric_source(idx)
        src = src.crop(content_box(src, 35)).resize(positions[idx][1], Image.Resampling.NEAREST)
        x, y = positions[idx][0]
        cell.alpha_composite(src, (x + [-2, 1, 3, 0, -1, 2][k], y))
    d = ImageDraw.Draw(cell, 'RGBA')
    rng = random.Random(7137+k)
    anchors = [((99, 164), (134, 117)), ((259, 119), (312, 179)),
               ((76, 232), (143, 273)), ((254, 273), (314, 236)),
               ((127, 75), (178, 140)), ((210, 135), (265, 95))]
    for n in range(3+k%2):
        (x0, y0), (x1, y1) = anchors[(k+n*2) % 6]
        pts = [(x0, y0)]
        for q in range(1, 7):
            f = q/7
            pts.append((round(x0+(x1-x0)*f+rng.randrange(-7, 8)),
                        round(y0+(y1-y0)*f+rng.randrange(-7, 8))))
        pts.append((x1, y1))
        d.line(pts, fill=(94, 149, 192, 130), width=6)
        d.line(pts, fill=WHITE, width=3)
        d.line(pts, fill=YELLOW if n == 0 else (220, 239, 255, 255), width=1)
    return inside(cell)


def inside(cell: Image.Image) -> Image.Image:
    a = np.array(cell)
    a[:7, :, :] = a[-7:, :, :] = 0
    a[:, :7, :] = a[:, -7:, :] = 0
    return Image.fromarray(a, 'RGBA')


def death_source(name: str, margin: int) -> Image.Image:
    im = clean(Image.open(SRC / name), 58)
    im = im.crop(content_box(im, 84))
    extent = CELL-2*margin
    scale = min(extent/im.width, extent/im.height)
    size = (round(im.width*scale), round(im.height*scale))
    im = im.resize(size, Image.Resampling.NEAREST)
    cell = Image.new('RGBA', (CELL, CELL))
    cell.alpha_composite(im, ((CELL-size[0])//2, (CELL-size[1])//2))
    return cell


def flash(body: Image.Image) -> Image.Image:
    out = body.copy()
    d = ImageDraw.Draw(out, 'RGBA')
    for x, y in [(192, 239), (143, 212), (233, 190), (112, 166), (268, 152), (166, 104), (221, 99)]:
        d.ellipse((x-7, y-7, x+7, y+7), fill=(151, 188, 219, 170))
        d.ellipse((x-4, y-4, x+4, y+4), fill=WHITE)
        d.line((x-11, y, x+11, y), fill=YELLOW, width=2)
    return out


def on_bg(im: Image.Image) -> Image.Image:
    bg = Image.new('RGBA', (CELL, CELL), BG)
    bg.alpha_composite(im)
    return bg.convert('RGB')


def main() -> None:
    pristine = Image.open(SRC / 'pristine_cell.png').convert('RGBA')
    names = [f'stage{k}_candidate_a.png' for k in range(1, 5)]
    bodies = [fit(Image.open(SRC / name), (20, 20, 364, 360)) for name in names]
    body_sheet = Image.new('RGBA', (CELL*2, CELL*4))
    for row, body in enumerate(bodies):
        body_sheet.alpha_composite(body, (0, row*CELL))
        body_sheet.alpha_composite(variant(body, row+1), (CELL, row*CELL))
    body_sheet.save(OUT / 'Storm_damage.png')

    smoke = [smoke_frame(k) for k in range(6)]
    electric = [electric_frame(k) for k in range(6)]
    fx_sheet = Image.new('RGBA', (CELL*6, CELL*2))
    for k in range(6):
        fx_sheet.alpha_composite(smoke[k], (k*CELL, 0))
        fx_sheet.alpha_composite(electric[k], (k*CELL, CELL))
    fx_sheet.save(OUT / 'Storm_damage_fx.png')

    burst = death_source('explosion_candidate_a.png', 12)
    peak = death_source('explosion_peak_candidate_b.png', 3)
    shock = death_source('shockwave_candidate_a.png', 12)
    fade = death_source('shockwave_candidate_a.png', 37)
    fade_a = np.array(fade)
    fade_a[:, :, 3] = (fade_a[:, :, 3].astype(np.uint16)*135//255).astype(np.uint8)
    fade = Image.fromarray(fade_a, 'RGBA')
    death = [flash(bodies[-1]), death_source('death_split_candidate_a.png', 12),
             burst, peak, shock, fade]
    death_sheet = Image.new('RGBA', (CELL*6, CELL))
    for k, frame in enumerate(death):
        death_sheet.alpha_composite(frame, (k*CELL, 0))
    death_sheet.save(OUT / 'Storm_death.png')

    preview = Image.new('RGB', (CELL*2*5, CELL*2), BG)
    for k, frame in enumerate([pristine]+bodies):
        view = frame.copy()
        if k:
            view.alpha_composite(smoke[2])
            view.alpha_composite(electric[3])
        preview.paste(on_bg(view).resize((CELL*2, CELL*2), Image.Resampling.NEAREST),
                      (k*CELL*2, 0))
    preview.save(OUT / 'preview.png')
    gif = []
    for k in range(12):
        view = bodies[-1].copy() if k < 6 else variant(bodies[-1], 4)
        view.alpha_composite(smoke[k % 6])
        view.alpha_composite(electric[k % 6])
        gif.append(on_bg(view).resize((CELL*2, CELL*2), Image.Resampling.NEAREST))
    gif[0].save(OUT / 'preview.gif', save_all=True, append_images=gif[1:],
                duration=100, loop=0, optimize=False, disposal=2)
    death_preview = Image.new('RGB', (CELL*2*6, CELL*2), BG)
    for k, frame in enumerate(death):
        death_preview.paste(on_bg(frame).resize((CELL*2, CELL*2), Image.Resampling.NEAREST),
                            (k*CELL*2, 0))
    death_preview.save(OUT / 'Storm_death_preview.png')
    print('Built Storm damage, FX, death and previews')


if __name__ == '__main__':
    main()
