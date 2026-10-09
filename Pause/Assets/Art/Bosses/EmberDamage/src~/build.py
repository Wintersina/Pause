"""Build registered Ember damage and death sheets from generated pixel-art sources.

Run from anywhere with Pillow installed. All resizing uses nearest-neighbour.
The stage and effect candidate PNGs beside this file are AI-generated paintings.
"""
from __future__ import annotations

from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFilter
import random

ROOT = Path(__file__).resolve().parent.parent
SRC = Path(__file__).resolve().parent
CELL = 384
BG = '#0b0b1a'
ORANGE = (252, 122, 8, 255)
YELLOW = (253, 240, 106, 255)
WHITE = (255, 251, 224, 255)
INK = (5, 6, 12, 255)


def load(name: str) -> Image.Image:
    im = Image.open(SRC / f'{name}.png').convert('RGBA')
    if im.size != (CELL, CELL):
        im = im.resize((CELL, CELL), Image.Resampling.NEAREST)
    return im


def polygon_mask(points):
    mask = Image.new('L', (CELL, CELL))
    ImageDraw.Draw(mask).polygon(points, fill=255)
    return mask


def replace(base, other, points):
    """Use another painting along a natural plate seam, including its cutout alpha."""
    result = base.copy()
    result.paste(other, (0, 0), polygon_mask(points))
    return result


def multiply_alpha(im, factor):
    out = im.copy()
    out.putalpha(out.getchannel('A').point(lambda a: round(a * factor)))
    return out


def offset(im, dx, dy):
    out = Image.new('RGBA', (CELL, CELL))
    out.alpha_composite(im, (dx, dy))
    return out


def centered_scale(im, factor, y_shift=0):
    edge = round(CELL * factor)
    small = im.resize((edge, edge), Image.Resampling.NEAREST)
    out = Image.new('RGBA', (CELL, CELL))
    out.alpha_composite(small, ((CELL - edge) // 2, y_shift))
    return out


def clip_to_footprint(im, footprint):
    out = im.copy()
    out.putalpha(ImageChops.multiply(im.getchannel('A'), footprint))
    return out


def guard_cell(im):
    """Keep the outermost pixel transparent so atlas neighbors never touch."""
    out = im.copy()
    alpha = out.getchannel('A')
    d = ImageDraw.Draw(alpha)
    d.rectangle((0, 0, CELL - 1, 0), fill=0)
    d.rectangle((0, CELL - 1, CELL - 1, CELL - 1), fill=0)
    d.rectangle((0, 0, 0, CELL - 1), fill=0)
    d.rectangle((CELL - 1, 0, CELL - 1, CELL - 1), fill=0)
    out.putalpha(alpha)
    return out


def remove_baked_exhaust(im):
    """Strip the grey chimney plumes from generated body paintings."""
    out = im.copy()
    alpha = out.getchannel('A')
    d = ImageDraw.Draw(alpha)
    d.polygon([(128, 22), (140, 18), (154, 27), (164, 44),
               (157, 58), (151, 65), (135, 60), (122, 47)], fill=0)
    d.polygon([(236, 20), (250, 18), (267, 29), (274, 44),
               (266, 58), (256, 66), (239, 62), (226, 48)], fill=0)
    d.rectangle((265, 0, 280, 40), fill=0)
    out.putalpha(alpha)
    px = out.load()
    for left, right in ((119, 166), (220, 273)):
        for y in range(0, 75):
            for x in range(left, right):
                r, g, b, a = px[x, y]
                if a and max(r, g, b) - min(r, g, b) < 54 and max(r, g, b) < 183:
                    px[x, y] = (0, 0, 0, 0)
    return out


def structural_details(im, stage, variant):
    """Repair generated seams and give the ruin crisp, legible broken internals."""
    out = im.copy()
    d = ImageDraw.Draw(out, 'RGBA')
    if stage <= 2:
        # The starboard eye has lost power; its surviving pixel pulses in idle B.
        d.polygon([(214, 140), (223, 138), (229, 143), (226, 151),
                   (218, 151), (213, 146)], fill=(55, 15, 34, 255))
        d.rectangle((220, 142, 223 if variant == 0 else 221, 144),
                    fill=(231, 51, 156, 255) if variant == 0 else (133, 32, 91, 255))
    if stage >= 2:
        # Sheared left stack and two exposed magenta pressure runs.
        d.polygon([(131, 59), (139, 63), (146, 82), (139, 96), (132, 91)], fill=INK)
        d.line([(130, 87), (135, 91), (138, 99)], fill=(149, 112, 96, 255), width=2)
        d.line([(86, 172), (95, 179), (102, 197), (97, 211)], fill=INK, width=5)
        d.line([(86, 172), (95, 179), (102, 197), (97, 211)], fill=(229, 48, 158, 255), width=2)
        d.line([(108, 205), (112, 215), (109, 223)], fill=(157, 109, 62, 255), width=3)
    if stage >= 3:
        # The generated painting already contains the torn, lit-through void.
        d.line([(314, 185), (319, 197), (312, 215), (323, 226)], fill=INK, width=5)
        d.line([(314, 185), (319, 197), (312, 215), (323, 226)], fill=(134, 91, 67, 255), width=2)
        # Shattered right eye and its dead lower corner.
        d.polygon([(214, 139), (222, 136), (230, 141), (227, 150), (219, 152), (214, 147)], fill=(18, 9, 13, 255))
        d.line([(215, 140), (222, 144), (218, 149), (225, 152)], fill=(117, 85, 78, 255), width=1)
        d.point((220, 141), fill=(232, 59, 153, 255) if variant == 0 else (89, 32, 59, 255))
    if stage >= 4:
        # Open a second front breach and break the long jaw into two plate tips.
        d.polygon([(105, 168), (115, 155), (128, 161), (136, 176), (131, 190),
                   (123, 203), (109, 195), (101, 181)], fill=INK)
        d.polygon([(110, 170), (122, 163), (129, 174), (125, 190), (114, 191)], fill=(106, 36, 4, 255))
        d.line([(113, 186), (119, 172), (126, 177), (123, 189)], fill=ORANGE, width=4)
        d.line([(119, 173), (125, 177)], fill=WHITE, width=2)
        d.line([(108, 184), (102, 206), (115, 219), (109, 237)], fill=INK, width=6)
        d.line([(108, 184), (102, 206), (115, 219), (109, 237)], fill=(150, 111, 71, 255), width=2)
        d.line([(157, 274), (165, 296), (178, 313), (184, 326)], fill=INK, width=5)
        d.line([(207, 269), (211, 289), (202, 309), (199, 321)], fill=INK, width=5)
        d.line([(173, 294), (188, 305), (202, 296)], fill=ORANGE, width=2)
        d.line([(183, 205), (171, 218), (178, 230), (167, 242), (178, 255)], fill=ORANGE, width=3)
        d.line([(184, 206), (175, 218), (181, 230)], fill=YELLOW, width=1)
        # The core gutters between idle frames.
        core_color = WHITE if variant == 0 else ORANGE
        d.rectangle((185, 201, 192, 206), fill=core_color)
    elif variant:
        # Small, local idle flicker; the hull remains registered.
        d.rectangle((183, 207, 188, 210), fill=(194, 74, 6, 255))
    return out


def build_bodies():
    pristine = Image.open(ROOT / 'ref_idle_cell.png').convert('RGBA')
    # High-alpha silhouette plus the six-pixel registration allowance.
    structural = pristine.getchannel('A').point(lambda a: 255 if a >= 96 else 0)
    footprint = structural.filter(ImageFilter.MaxFilter(13))
    candidates = {name: load(name) for name in (
        'stage1_a', 'stage1_b', 'stage2_a', 'stage2_b',
        'stage3_a', 'stage3_b', 'stage4_a', 'stage4_b')}

    stages = []
    s1 = replace(pristine, candidates['stage1_a'],
                 [(238, 89), (277, 83), (326, 70), (383, 64), (383, 281),
                  (338, 262), (298, 229), (259, 209), (235, 176)])
    s1 = replace(s1, candidates['stage1_b'],
                 [(32, 111), (100, 106), (120, 124), (121, 155),
                  (94, 175), (44, 170), (10, 156)])
    stages.append(s1)

    s2 = candidates['stage2_a'].copy()
    s2 = replace(s2, candidates['stage2_b'],
                 [(251, 101), (287, 87), (342, 79), (383, 70), (383, 291),
                  (347, 262), (308, 225), (269, 207), (251, 175)])
    stages.append(s2)

    s3 = candidates['stage3_a'].copy()
    s3 = replace(s3, candidates['stage3_b'],
                 [(0, 73), (82, 75), (111, 103), (129, 139), (140, 187),
                  (150, 243), (118, 290), (55, 283), (0, 289)])
    stages.append(s3)

    s4 = candidates['stage4_a'].copy()
    s4 = replace(s4, candidates['stage4_b'],
                 [(215, 152), (238, 126), (276, 116), (326, 96), (383, 89),
                  (383, 292), (323, 288), (285, 258), (243, 219)])
    stages.append(s4)

    sheet = Image.new('RGBA', (768, 1536))
    frames = []
    for stage, base in enumerate(stages, 1):
        for variant in range(2):
            frame = base.copy()
            if variant:
                # A painted loose fragment shifts only one pixel in the alternate frame.
                piece = frame.crop((104, 199, 116, 216))
                frame.paste((0, 0, 0, 0), (104, 199, 116, 216))
                frame.alpha_composite(piece, (105, 200))
            frame = structural_details(frame, stage, variant)
            frame = guard_cell(clip_to_footprint(remove_baked_exhaust(frame), footprint))
            sheet.alpha_composite(frame, ((variant * CELL), ((stage - 1) * CELL)))
            frames.append(frame)
    sheet.save(ROOT / 'Ember_damage.png')
    return pristine, frames


def smoke_frame(k):
    # The broad, heavy billows are generated imagery. The three exhaust columns
    # and pale steam are a second generated candidate.
    heavy = centered_scale(load('smoke_a'), 0.72, -8)
    columns = centered_scale(load('smoke_b'), 0.72, -8)
    dx = [0, 2, 4, 2, -1, -2][k]
    dy = [0, -2, -4, -3, -1, 1][k]
    heavy = offset(heavy, dx, dy)
    columns = offset(columns, -dx, dy)
    mask = Image.new('L', (CELL, CELL))
    md = ImageDraw.Draw(mask)
    md.rectangle((0, 0, 143, 235), fill=255)
    md.rectangle((235, 0, 383, 235), fill=255)
    md.rectangle((135, 0, 250, 105), fill=255)
    heavy.putalpha(ImageChops.multiply(heavy.getchannel('A'), mask))
    out = multiply_alpha(heavy, 0.87)
    mid = Image.new('L', (CELL, CELL))
    ImageDraw.Draw(mid).rectangle((138, 0, 250, 106), fill=200)
    columns.putalpha(ImageChops.multiply(columns.getchannel('A'), mid))
    out.alpha_composite(columns)
    d = ImageDraw.Draw(out, 'RGBA')
    rng = random.Random(700 + k)
    for origin in ((95, 159), (279, 153), (145, 80)):
        for _ in range(8):
            x = origin[0] + rng.randrange(-21, 22)
            y = origin[1] + rng.randrange(-56, 17)
            size = rng.choice((1, 1, 2, 3))
            d.rectangle((x, y, x+size, y+size), fill=(252, 122, 8, rng.randrange(130, 240)))
    # Never veil the eye line or jaw centre.
    a = out.getchannel('A')
    ad = ImageDraw.Draw(a)
    ad.polygon([(140, 127), (236, 127), (242, 167), (211, 187),
                (165, 187), (134, 166)], fill=0)
    ad.polygon([(154, 188), (225, 188), (221, 318), (172, 332), (153, 273)], fill=0)
    out.putalpha(a)
    return guard_cell(out)


def electric_frame(k):
    source = centered_scale(load('electric_a' if k % 2 == 0 else 'electric_b'), 0.76, 16)
    source = offset(source, [0, 3, -2, 2, -3, 1][k], [0, -2, 2, 1, -1, 0][k])
    mask = Image.new('L', (CELL, CELL))
    md = ImageDraw.Draw(mask)
    md.polygon([(17, 85), (112, 73), (168, 104), (221, 92), (281, 73),
                (372, 87), (383, 247), (318, 260), (279, 222), (234, 204),
                (198, 178), (163, 205), (97, 236), (0, 248)], fill=255)
    source.putalpha(ImageChops.multiply(source.getchannel('A'), mask))
    out = multiply_alpha(source, 0.80)
    d = ImageDraw.Draw(out, 'RGBA')
    rng = random.Random(1400 + k)
    anchors = [(101, 147), (280, 139), (128, 191), (274, 205), (166, 87), (236, 89)]
    for n in range(1 + k % 2):
        sx, sy = anchors[(n + k) % len(anchors)]
        ex = max(12, min(370, sx + rng.choice((-1, 1)) * rng.randrange(28, 53)))
        ey = max(17, min(263, sy + rng.randrange(-34, 34)))
        pts = [(sx, sy)]
        for q in range(1, 5):
            t = q / 5
            pts.append((round(sx + (ex-sx)*t + rng.randrange(-8, 9)),
                        round(sy + (ey-sy)*t + rng.randrange(-7, 8))))
        pts.append((ex, ey))
        d.line(pts, fill=(236, 49, 155, 205), width=6, joint='curve')
        d.line(pts, fill=ORANGE, width=4, joint='curve')
        d.line(pts, fill=WHITE, width=2 + (k == 3), joint='curve')
        for _ in range(5):
            x = ex + rng.randrange(-10, 11)
            y = ey + rng.randrange(-10, 11)
            d.line((x-2, y, x+2, y), fill=YELLOW, width=1)
            d.line((x, y-2, x, y+2), fill=WHITE, width=1)
    if k in (1, 4):
        x, y = anchors[k]
        d.ellipse((x-8, y-8, x+8, y+8), outline=ORANGE, width=2)
        d.line((x-11, y, x+11, y), fill=WHITE, width=2)
        d.line((x, y-11, x, y+11), fill=WHITE, width=2)
    alpha = out.getchannel('A')
    ad = ImageDraw.Draw(alpha)
    ad.ellipse((151, 123, 182, 158), fill=0)
    ad.ellipse((208, 123, 239, 158), fill=0)
    ad.polygon([(164, 204), (216, 204), (221, 320), (172, 320)], fill=0)
    out.putalpha(alpha)
    return guard_cell(out)


def build_fx():
    sheet = Image.new('RGBA', (CELL * 6, CELL * 2))
    smoke, electric = [], []
    for k in range(6):
        sm, el = smoke_frame(k), electric_frame(k)
        smoke.append(sm)
        electric.append(el)
        sheet.alpha_composite(sm, (k * CELL, 0))
        sheet.alpha_composite(el, (k * CELL, CELL))
    sheet.save(ROOT / 'Ember_damage_fx.png')
    return smoke, electric


def death_frame_zero(stage4):
    out = stage4.copy()
    d = ImageDraw.Draw(out, 'RGBA')
    cracks = [
        [(190, 207), (173, 190), (156, 180), (146, 157), (126, 147)],
        [(192, 205), (209, 184), (233, 167), (259, 147), (285, 144)],
        [(189, 207), (190, 179), (184, 151), (192, 119), (189, 87)],
        [(192, 207), (176, 226), (162, 247), (147, 256)],
        [(193, 207), (213, 224), (228, 240), (245, 251)],
    ]
    for pts in cracks:
        d.line(pts, fill=ORANGE, width=8, joint='curve')
        d.line(pts, fill=YELLOW, width=5, joint='curve')
        d.line(pts, fill=WHITE, width=2, joint='curve')
    d.ellipse((177, 190, 208, 224), fill=ORANGE)
    d.ellipse((183, 196, 202, 217), fill=WHITE)
    return out


def build_death(stage4):
    frames = [death_frame_zero(stage4), load('death_b'), load('death_a'),
              load('death_peak'), load('death_ring'), load('death_ash')]
    sheet = Image.new('RGBA', (CELL * 6, CELL))
    preview = Image.new('RGB', (CELL * 2 * 6, CELL * 2), BG)
    for i, frame in enumerate(frames):
        frame = guard_cell(frame)
        sheet.alpha_composite(frame, (i * CELL, 0))
        preview.paste(on_bg(frame).resize((CELL * 2, CELL * 2), Image.Resampling.NEAREST),
                      (i * CELL * 2, 0))
    sheet.save(ROOT / 'Ember_death.png')
    preview.save(ROOT / 'Ember_death_preview.png')


def on_bg(im):
    bg = Image.new('RGBA', (CELL, CELL), BG)
    bg.alpha_composite(im)
    return bg.convert('RGB')


def build_preview(pristine, frames, smoke, electric):
    preview = Image.new('RGB', (CELL * 2 * 5, CELL * 2), BG)
    for i in range(5):
        cell = pristine.copy() if i == 0 else frames[(i-1) * 2].copy()
        if i:
            cell.alpha_composite(smoke[2])
            cell.alpha_composite(electric[2])
        preview.paste(on_bg(cell).resize((CELL * 2, CELL * 2), Image.Resampling.NEAREST),
                      (i * CELL * 2, 0))
    preview.save(ROOT / 'preview.png')
    gifs = []
    for k in range(12):
        cell = frames[6 + (k // 6)].copy()
        cell.alpha_composite(smoke[k % 6])
        cell.alpha_composite(electric[k % 6])
        gifs.append(on_bg(cell).resize((CELL * 2, CELL * 2), Image.Resampling.NEAREST))
    gifs[0].save(ROOT / 'preview.gif', save_all=True, append_images=gifs[1:],
                 duration=100, loop=0, optimize=False, disposal=2)


def main():
    pristine, bodies = build_bodies()
    smoke, electric = build_fx()
    build_death(bodies[6])
    build_preview(pristine, bodies, smoke, electric)
    print('Built Ember damage, FX, death, and previews')


if __name__ == '__main__':
    main()
