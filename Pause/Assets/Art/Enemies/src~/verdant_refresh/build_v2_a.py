#!/usr/bin/env python3
"""Build painted Verdant enemy strips from the selected image-generated concepts.

All resizing and local motion use nearest-neighbour sampling.  The source paint
remains the hull; code only tempers palette, moves anatomy, and draws small cues.
"""
from pathlib import Path
import colorsys
import math
import numpy as np
from PIL import Image, ImageDraw, ImageOps

HERE = Path(__file__).resolve().parent
SIDE = 192
BG = (11, 11, 26, 255)
INK = (12, 11, 8, 255)
PALE = (241, 246, 200, 255)
LIME = (233, 255, 184, 255)
EDGE = (182, 221, 108, 255)
LEAF = (78, 136, 49, 255)
BARK = (94, 75, 44, 255)
PHASE = (0.0, 1.0, -1.0, 0.48)
SPEC = {
    'verdant_chaser': {'column': 1, 'size': (82, 160), 'core': (96, 103), 'tip': (96, 172)},
    'verdant_fighter_1': {'column': 0, 'size': (166, 150), 'core': (96, 100), 'tip': (96, 159)},
}


def selected(name, spec):
    src = Image.open(HERE / f'{name}_candidates.png').convert('RGBA')
    cw = src.width // 3
    im = src.crop((spec['column'] * cw, 0, (spec['column'] + 1) * cw, src.height))
    alpha = np.asarray(im.getchannel('A'))
    yy, xx = np.where(alpha > 128)
    if len(xx) == 0:
        raise ValueError(f'{name}: generated sheet has no alpha in selected column')
    # The source concepts can overlap a column by a few pixels. Trim the
    # selected complete concept to its designed bounds before reducing it.
    if name == 'verdant_fighter_1':
        # Adjacent concepts touch its right leaf. Mirror the fully painted
        # left half at the iris seam to retain an uncut, symmetric wing.
        half = src.crop((50, 20, 403, 665))
        whole = Image.new('RGBA', (half.width * 2, half.height))
        whole.paste(half, (0, 0))
        whole.paste(ImageOps.mirror(half), (half.width, 0))
        return whole
    return im.crop((max(0, xx.min() - 2), max(0, yy.min() - 2),
                    min(cw, xx.max() + 3), min(src.height, yy.max() + 3)))


def hsv_arrays(rgb):
    f = rgb.astype(np.float32) / 255.0
    mx, mn = f.max(2), f.min(2)
    delta = mx - mn
    sat = np.divide(delta, mx, out=np.zeros_like(delta), where=mx > 0)
    hue = np.zeros_like(mx)
    r = (delta > 0) & (mx == f[:, :, 0])
    g = (delta > 0) & (mx == f[:, :, 1]) & ~r
    b = (delta > 0) & ~r & ~g
    hue[r] = (60 * (f[:, :, 1][r] - f[:, :, 2][r]) / delta[r]) % 360
    hue[g] = 60 * (f[:, :, 2][g] - f[:, :, 0][g]) / delta[g] + 120
    hue[b] = 60 * (f[:, :, 0][b] - f[:, :, 1][b]) / delta[b] + 240
    return hue, sat, mx


def temper(im):
    a = np.array(im)
    h, s, v = hsv_arrays(a[:, :, :3])
    visible = a[:, :, 3] > 8
    # Older Verdant hues and stray generated warm/cool fringes become bark or leaf.
    brown = visible & (s > .12) & ((h < 50) | (h >= 300))
    h[brown] = 28
    s[brown] = np.minimum(s[brown], .43)
    green = visible & (s > .22) & (h >= 50) & (h < 100)
    h[green] = 72
    leafy = visible & (s > .22) & (h >= 100) & (h <= 150)
    h[leafy] = 112
    stray = visible & (s > .22) & (h > 150) & (h < 300)
    h[stray] = 112
    s[stray] = np.minimum(s[stray], .42)
    # Preserve sunlit source planes; lift their midtones against green canopy.
    material = visible & (h >= 16) & (h < 54) & (s > .10) & (v > .25)
    v[material] = np.minimum(1, v[material] * 1.11 + .025)
    # HSV round-trip keeps the source's many subtle painted tones.
    flat = np.stack((h / 360, s, v), axis=2).reshape(-1, 3)
    out = np.array([colorsys.hsv_to_rgb(*x) for x in flat], dtype=np.float32)
    a[:, :, :3] = np.clip(np.rint(out.reshape(a.shape[0], a.shape[1], 3) * 255), 0, 255).astype('uint8')
    a[:, :, 3] = np.where(a[:, :, 3] < 128, 0, 255)
    # Quantising near-black browns can otherwise make (3,2,2) read as red.
    h2, s2, _ = hsv_arrays(a[:, :, :3])
    redblack = (a[:, :, 3] > 0) & ((h2 <= 15) | (h2 >= 345)) & (s2 > .18)
    value = a[:, :, :3].max(2)
    a[:, :, 0][redblack] = value[redblack]
    a[:, :, 1][redblack] = np.maximum(1, np.rint(value[redblack] * .88)).astype('uint8')
    a[:, :, 2][redblack] = np.rint(value[redblack] * .65).astype('uint8')
    return Image.fromarray(a, 'RGBA')


def finish_edges(im):
    a = np.array(im)
    solid = a[:, :, 3] > 128
    up = np.zeros_like(solid); up[1:] = solid[:-1]
    down = np.zeros_like(solid); down[:-1] = solid[1:]
    left = np.zeros_like(solid); left[:, 1:] = solid[:, :-1]
    right = np.zeros_like(solid); right[:, :-1] = solid[:, 1:]
    boundary = solid & ~(up & down & left & right)
    a[boundary, :3] = INK[:3]
    # Pale two-pixel bevel round the whole silhouette; upper-left is strongest.
    inner = solid & ~boundary
    iu = np.zeros_like(inner); iu[1:] = inner[:-1]
    idn = np.zeros_like(inner); idn[:-1] = inner[1:]
    il = np.zeros_like(inner); il[:, 1:] = inner[:, :-1]
    ir = np.zeros_like(inner); ir[:, :-1] = inner[:, 1:]
    bevel = inner & ~(iu & idn & il & ir)
    inner2 = inner & ~bevel
    u2 = np.zeros_like(inner2); u2[1:] = inner2[:-1]
    d2 = np.zeros_like(inner2); d2[:-1] = inner2[1:]
    l2 = np.zeros_like(inner2); l2[:, 1:] = inner2[:, :-1]
    r2 = np.zeros_like(inner2); r2[:, :-1] = inner2[:, 1:]
    bevel2 = inner2 & ~(u2 & d2 & l2 & r2)
    old = a[bevel, :3].astype(np.float32)
    a[bevel, :3] = np.rint(old * .22 + np.array(PALE[:3]) * .78).astype('uint8')
    old = a[bevel2, :3].astype(np.float32)
    a[bevel2, :3] = np.rint(old * .54 + np.array(PALE[:3]) * .46).astype('uint8')
    return Image.fromarray(a, 'RGBA')


def key_pose(name, spec):
    crop = selected(name, spec)
    crop = crop.resize(spec['size'], Image.Resampling.NEAREST)
    crop = temper(crop)
    base = Image.new('RGBA', (SIDE, SIDE))
    base.alpha_composite(crop, ((SIDE - crop.width) // 2, (SIDE - crop.height) // 2))
    base = finish_edges(base)
    base.save(HERE / f'{name}_key.png', optimize=True)
    return base


def move_parts(key, name, phase):
    a = np.asarray(key)
    yy, xx = np.indices((SIDE, SIDE), dtype=np.float32)
    dx = np.zeros_like(xx)
    dy = np.zeros_like(yy)
    if name == 'verdant_chaser':
        # Flex the two curled leaf blades; kick the tiny lower thorn legs.
        fin = np.clip((np.abs(xx - 96) - 14) / 17, 0, 1)
        fin *= np.clip((yy - 63) / 25, 0, 1) * np.clip((143 - yy) / 25, 0, 1)
        dx += phase * 1.7 * np.sign(xx - 96) * fin
        dy += phase * 1.1 * fin * np.sign(xx - 96) * np.sin(yy * .11)
        legs = np.clip((yy - 111) / 25, 0, 1) * np.clip((np.abs(xx - 96) - 9) / 12, 0, 1)
        dx += phase * 1.0 * np.sign(xx - 96) * legs
        dy -= phase * .8 * legs
    else:
        # Pulse only the leaf wings and ribbed iris; the heavy seed pod stays put.
        wing = np.clip((np.abs(xx - 96) - 27) / 22, 0, 1)
        wing *= np.clip((yy - 54) / 24, 0, 1) * np.clip((151 - yy) / 23, 0, 1)
        dx += phase * 2.8 * np.sign(xx - 96) * wing
        dy += phase * 1.8 * wing * np.sin((yy - 58) * .07)
        radial = np.hypot(xx - 96, yy - 99)
        iris = np.clip((28 - radial) / 8, 0, 1) * np.clip((radial - 9) / 8, 0, 1)
        angle = phase * .055 * iris
        dx += angle * (yy - 99)
        dy -= angle * (xx - 96)
        tendril = np.clip((yy - 122) / 18, 0, 1) * np.clip((np.abs(xx - 96) - 18) / 20, 0, 1)
        dx += phase * 1.2 * tendril
    sx = np.clip(np.rint(xx - dx).astype(int), 0, SIDE - 1)
    sy = np.clip(np.rint(yy - dy).astype(int), 0, SIDE - 1)
    return Image.fromarray(a[sy, sx], 'RGBA')


def core_pulse(frame, name, index, spec):
    a = np.array(frame)
    yy, xx = np.indices((SIDE, SIDE))
    x, y = spec['core']
    radius = 4 if name == 'verdant_chaser' else 9
    mask = ((xx - x) ** 2 + (yy - y) ** 2 <= radius ** 2) & (a[:, :, 3] > 128)
    adjustment = (0, 14, -13, 6)[index]
    v = a[:, :, :3].astype(np.int16)
    greenish = mask & (v[:, :, 1] >= v[:, :, 0])
    v[greenish] = np.clip(v[greenish] + adjustment, 0, 255)
    a[:, :, :3] = v.astype('uint8')
    return Image.fromarray(a, 'RGBA')


def tell_pose(key, name, spec, discharge):
    frame = move_parts(key, name, -1 if discharge else 1)
    draw = ImageDraw.Draw(frame)
    x, y = spec['core']
    r = 8 if name == 'verdant_chaser' else 15
    if discharge: r += 3
    draw.arc((x-r, y-r, x+r, y+r), 205, 333, fill=EDGE, width=2)
    draw.arc((x-r, y-r, x+r, y+r), 20, 145, fill=EDGE, width=2)
    draw.ellipse((x-4, y-4, x+4, y+4), fill=LIME, outline=EDGE, width=1)
    if discharge:
        tx, ty = spec['tip']
        draw.line([(x, y+5), (x+1, (y+ty)//2), (tx, ty-5)], fill=LEAF, width=1)
        draw.ellipse((tx-3, ty-3, tx+3, ty+3), fill=LIME, outline=EDGE, width=1)
        for ox, oy in ((-7, -2), (7, 2), (-5, 7)):
            draw.point((tx+ox, ty+oy), fill=EDGE)
    return frame


def hit_pose(key, name, spec):
    frame = key.copy()
    a = np.array(frame)
    solid = a[:, :, 3] > 128
    v = a[:, :, :3].astype(np.int16)
    # One brief bright body flash retains all of the painted armour planes.
    v[solid] = np.clip(v[solid] * 1.17 + 8, 0, 225)
    a[:, :, :3] = v.astype('uint8')
    h, s, _ = hsv_arrays(a[:, :, :3])
    redblack = solid & ((h <= 15) | (h >= 345)) & (s > .18)
    a[:, :, 1][redblack] = np.rint(a[:, :, 0][redblack] * .88).astype('uint8')
    a[:, :, 2][redblack] = np.rint(a[:, :, 0][redblack] * .65).astype('uint8')
    frame = Image.fromarray(a, 'RGBA')
    draw = ImageDraw.Draw(frame)
    x, y = spec['core']
    crack = [(x-12,y-18),(x-6,y-10),(x-9,y-4),(x+1,y+4),(x-2,y+13)]
    draw.line(crack, fill=INK, width=3)
    draw.line([(px-1,py-1) for px,py in crack], fill=PALE, width=1)
    draw.ellipse((x-5,y-5,x+5,y+5), fill=LIME, outline=EDGE, width=1)
    chips = ((-23,-12),(23,-16),(-27,17),(29,21)) if name == 'verdant_chaser' else ((-41,-18),(43,-23),(-45,27),(48,31))
    for ox, oy in chips:
        px, py = x+ox, y+oy
        draw.polygon(((px,py-3),(px+3,py),(px,py+4),(px-3,py)), fill=BARK, outline=INK)
        draw.point((px-1,py-1), fill=PALE)
    return frame


def main():
    strips = HERE / 'strips'
    strips.mkdir(exist_ok=True)
    previews = []
    for name, spec in SPEC.items():
        key = key_pose(name, spec)
        frames = [core_pulse(move_parts(key, name, p), name, i, spec) for i,p in enumerate(PHASE)]
        frames += [tell_pose(key, name, spec, False), tell_pose(key, name, spec, True), hit_pose(key, name, spec)]
        strip = Image.new('RGBA', (SIDE * 7, SIDE))
        for i, frame in enumerate(frames):
            strip.alpha_composite(frame, (i * SIDE, 0))
        strip.save(strips / f'{name}.png', optimize=True)
        row = Image.new('RGBA', strip.size, BG)
        row.alpha_composite(strip)
        previews.append(row.resize((SIDE * 14, SIDE * 2), Image.Resampling.NEAREST))
        print(name, strip.size)
    preview = Image.new('RGB', (SIDE * 14, SIDE * 4), BG[:3])
    for i, row in enumerate(previews):
        preview.paste(row.convert('RGB'), (0, i * SIDE * 2))
    preview.save(HERE / 'preview.png', optimize=True)


if __name__ == '__main__':
    main()
