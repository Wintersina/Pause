#!/usr/bin/env python3
"""Build painted Desert key poses and seven-cell enemy strips.

The eleven candidate sheets are image-generated paintings. This script selects a
painted concept, makes a nearest-neighbour game-size crop, cleans forbidden hues,
and moves local parts for four anchored idle drawings. No blurred resampling.
"""
from pathlib import Path
import math
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
OUT = HERE / "strips"
SPEC = {
    "desert_alien": (1, 168),
    "desert_chaser": (0, 164),
    "desert_big": (2, 230),
    "desert_fighter_1": (1, 161),
    "desert_fighter_2": (2, 170),
    "desert_fighter_3": (0, 168),
    "desert_fighter_4": (2, 174),
    "desert_rock_dune": (0, 162),
    "desert_rock_spire": (2, 163),
    "desert_rock_husk": (0, 161),
    "desert_rock_slab": (1, 168),
}
INK = (8, 10, 19, 255)
BONE = (230, 220, 192, 255)
SAND = (217, 203, 160, 255)
WHITE = (255, 241, 200, 255)
PALE = (255, 246, 220, 255)
COPPER = (142, 97, 69, 255)
PHASE = (0.0, 1.0, -1.0, .55)


def palette_clean(im):
    """Keep painted value variation while bringing accidental generated hues home."""
    hsv = np.array(im.convert("RGB").convert("HSV"), dtype=np.uint8)
    h = hsv[:, :, 0].astype(np.float32) * (360 / 255)
    s = hsv[:, :, 1].astype(np.float32) / 255
    v = hsv[:, :, 2].astype(np.float32) / 255
    warm_pickup = (h >= 30) & (h <= 48) & (s > .45)
    s[warm_pickup] = .30
    red = ((h >= 345) | (h <= 15)) & (s > .50)
    h[red], s[red], v[red] = 25, np.minimum(s[red], .55), np.minimum(v[red], .58)
    pink_violet = (h >= 250) & (h <= 345) & (s > .12)
    h[pink_violet], s[pink_violet] = 238, np.minimum(s[pink_violet], .38)
    yellow = (h > 48) & (h < 75) & (s > .36)
    h[yellow], s[yellow] = 45, .28
    green = (h >= 75) & (h < 155) & (s > .32)
    h[green], s[green] = 148, .24
    cyan = (h >= 155) & (h < 225) & (s > .25)
    h[cyan], s[cyan] = 166, .20
    copper = (h >= 20) & (h <= 30) & (s > .35)
    v[copper] = np.minimum(v[copper], .60)
    hsv[:, :, 0] = np.round(h * (255 / 360)).astype(np.uint8)
    hsv[:, :, 1] = np.round(s * 255).astype(np.uint8)
    hsv[:, :, 2] = np.round(v * 255).astype(np.uint8)
    rgb = np.array(Image.fromarray(hsv, "HSV").convert("RGB"))
    alpha = np.array(im.getchannel("A"))
    alpha = np.where(alpha >= 104, 255, 0).astype(np.uint8)
    return Image.fromarray(np.dstack((rgb, alpha)), "RGBA")


def selected_source(name, column):
    sheet = Image.open(HERE / f"{name}_candidates.png").convert("RGBA")
    x0 = round(column * sheet.width / 3)
    x1 = round((column + 1) * sheet.width / 3)
    src = sheet.crop((x0, 0, x1, sheet.height))
    alpha = np.asarray(src.getchannel("A"))
    yy, xx = np.nonzero(alpha >= 104)
    if not len(xx):
        raise RuntimeError(f"empty selected source: {name}")
    return src.crop((max(0, int(xx.min()) - 2), max(0, int(yy.min()) - 2),
                     min(src.width, int(xx.max()) + 3), min(src.height, int(yy.max()) + 3)))


def rim(im):
    a = np.array(im)
    solid = a[:, :, 3] > 128
    up = np.zeros_like(solid); up[1:] = solid[:-1]
    left = np.zeros_like(solid); left[:, 1:] = solid[:, :-1]
    edge = solid & (~up | ~left)
    yy, xx = np.indices(solid.shape)
    edge &= (xx < im.width * .54) | (yy < im.height * .34)
    value = a[:, :, :3].max(axis=2)
    edge &= value > 72
    a[edge, :3] = np.maximum(a[edge, :3], np.array(BONE[:3], np.uint8))
    return Image.fromarray(a, "RGBA")


def key_pose(name, column, target):
    cell = 256 if name == "desert_big" else 192
    src = selected_source(name, column)
    factor = target / max(src.size)
    size = (max(1, round(src.width * factor)), max(1, round(src.height * factor)))
    src = palette_clean(src.resize(size, Image.Resampling.NEAREST))
    canvas = Image.new("RGBA", (cell, cell))
    canvas.alpha_composite(src, ((cell - size[0]) // 2, (cell - size[1]) // 2))
    if name == "desert_fighter_4":
        # The generated third concept crossed its sheet column at the left.
        # Its tiny, detached cut-off sand ribbon is not part of the tank.
        aa = np.array(canvas)
        aa[90:136, 0:45] = 0
        canvas = Image.fromarray(aa, "RGBA")
    canvas = rim(canvas)
    if name == "desert_chaser":
        # Pale wear on the elytra separates this matte copper insect from dunes.
        d = ImageDraw.Draw(canvas)
        d.line(((76, 52), (69, 61), (66, 75), (68, 93), (76, 107)), fill=BONE, width=2)
        d.line(((115, 52), (123, 62), (126, 78), (123, 94), (115, 107)), fill=BONE, width=2)
        d.line(((78, 55), (86, 51)), fill=SAND, width=2)
        d.line(((113, 55), (106, 51)), fill=SAND, width=2)
    return canvas


def warp(key, name, phase):
    a = np.array(key)
    h, w = a.shape[:2]
    yy, xx = np.indices((h, w), dtype=np.float32)
    cx = (w - 1) / 2
    cy = (h - 1) / 2
    xn = (xx - cx) / (w * .43)
    yn = (yy - cy) / (h * .43)
    side = np.clip((np.abs(xn) - .26) / .45, 0, 1)
    top = np.clip((.30 - yn) / .70, 0, 1)
    bottom = np.clip((yn + .2) / .70, 0, 1)
    dx = np.zeros((h, w), np.float32)
    dy = np.zeros((h, w), np.float32)
    if name == "desert_rock_dune":
        # The strata tumble about one fixed centre; this also changes the edge.
        angle = phase * .048
        dx = angle * (yy - cy)
        dy = -angle * (xx - cx)
    elif name == "desert_rock_husk":
        angle = phase * .040
        dx = angle * (yy - cy) + phase * 1.0 * side
        dy = -angle * (xx - cx)
    elif name == "desert_rock_spire":
        dx = phase * (3.6 * top + 1.5 * side * np.sin(yy * .07))
        dy = phase * 1.1 * side
    elif name == "desert_rock_slab":
        dx = phase * (2.7 * bottom * np.sin(yy * .065) + 1.2 * side)
        dy = phase * (1.7 * bottom + .8 * top * np.sin(xx * .07))
    elif name == "desert_alien":
        dx = phase * (3.6 * side * np.sin(yy * .075) + 2.3 * bottom * np.sin(yy * .09))
        dy = phase * (1.7 * top + 1.5 * bottom * np.sin(xx * .06))
    elif name == "desert_chaser":
        dx = phase * (2.7 * side * np.sign(xn) + 2.1 * bottom * np.sin(yy * .08))
        dy = phase * (2.1 * side * bottom + .8 * top)
    elif name == "desert_big":
        dx = phase * (2.5 * side * np.sign(xn) + 2.9 * bottom * np.sin(xx * .065))
        dy = phase * (2.0 * bottom * np.sin(xx * .075) + 1.1 * top * side)
    elif name == "desert_fighter_4":
        dx = phase * (3.1 * side * np.sign(xn) + 2.5 * top * np.sin(yy * .08))
        dy = phase * (2.2 * side * np.sin(xx * .06) + 1.2 * bottom)
    else:
        dx = phase * (3.2 * side * np.sign(xn) + 2.7 * top * np.sin(yy * .075))
        dy = phase * (1.8 * side * np.sin(xx * .06) + 1.2 * bottom)
    sx = np.clip(np.rint(xx - dx).astype(int), 0, w - 1)
    sy = np.clip(np.rint(yy - dy).astype(int), 0, h - 1)
    return Image.fromarray(a[sy, sx], "RGBA")


def light_pulse(im, strength):
    arr = np.array(im)
    rgb = arr[:, :, :3].astype(np.int16)
    v = rgb.max(axis=2)
    s = v - rgb.min(axis=2)
    lens = (arr[:, :, 3] > 128) & (v > 195) & (s < 85)
    for c, scale in enumerate((1.0, .94, .80)):
        rgb[:, :, c][lens] = np.clip(rgb[:, :, c][lens] + strength * scale, 0, 255)
    arr[:, :, :3] = rgb.astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


def focal_point(key, name):
    if name == "desert_big": return (key.width // 2, int(key.height * .72))
    if name == "desert_fighter_3": return (key.width // 2, int(key.height * .39))
    if name == "desert_alien": return (key.width // 2, int(key.height * .49))
    if name.startswith("desert_rock_"): return (key.width // 2, key.height // 2)
    return (key.width // 2, int(key.height * .58))


def tell(key, name, stage):
    frame = light_pulse(warp(key, name, .32 if stage == 4 else -.25), 18 if stage == 4 else 35)
    d = ImageDraw.Draw(frame)
    x, y = focal_point(key, name)
    r = (5 if stage == 4 else 8) * (key.width // 192)
    d.ellipse((x-r-2, y-r-2, x+r+2, y+r+2), outline=INK, width=2)
    d.ellipse((x-r, y-r, x+r, y+r), fill=WHITE if stage == 5 else SAND, outline=BONE, width=1)
    d.ellipse((x-2, y-2, x+2, y+2), fill=PALE)
    d.line((x-r-4, y, x-r, y), fill=WHITE, width=1)
    d.line((x+r, y, x+r+4, y), fill=WHITE, width=1)
    if stage == 5:
        # Focused pale tell pips: no amber and no projectile painted into the strip.
        for a in (-.90, -.35, .35, .90):
            px = x + round(math.sin(a) * key.width * .19)
            py = y + round(math.cos(a) * key.height * .17)
            d.ellipse((px-2, py-2, px+2, py+2), fill=WHITE, outline=INK)
            d.point((px, py-1), fill=PALE)
    return frame


def hit(key, name):
    frame = light_pulse(key, 28)
    d = ImageDraw.Draw(frame)
    x, y = focal_point(key, name)
    mul = key.width / 192
    q = lambda n: round(n * mul)
    crack = [(x-q(19), y-q(19)), (x-q(7), y-q(7)), (x-q(2), y-q(13)),
             (x+q(4), y+q(1)), (x-q(1), y+q(13)), (x+q(13), y+q(21))]
    d.line(crack, fill=INK, width=q(3))
    d.line([(xx-1, yy-1) for xx, yy in crack], fill=WHITE, width=1)
    d.line((x+q(3), y, x+q(17), y-q(8), x+q(26), y-q(5)), fill=BONE, width=2)
    d.ellipse((x-q(5), y-q(5), x+q(5), y+q(5)), fill=WHITE, outline=BONE)
    d.ellipse((x-q(2), y-q(2), x+q(2), y+q(2)), fill=PALE)
    for ox, oy in ((-27,-20),(23,-26),(-24,24),(27,17)):
        px, py = x+q(ox), y+q(oy)
        d.polygon(((px,py-q(3)),(px+q(3),py),(px,py+q(4)),(px-q(3),py)), fill=COPPER, outline=INK)
        d.point((px-1,py-1),fill=BONE)
    return frame


def break_straight_edges(im):
    """Nick exceptionally straight silhouette runs like worn crystal edges."""
    a = np.array(im)
    solid = a[:, :, 3] > 128
    h, w = solid.shape
    for side in (-1, 1):
        for x in range(1, w - 1):
            edge = solid[:, x] & ~solid[:, x + side]
            starts = np.flatnonzero(edge & ~np.r_[False, edge[:-1]])
            ends = np.flatnonzero(edge & ~np.r_[edge[1:], False])
            for start, end in zip(starts, ends):
                if end - start + 1 < 27:
                    continue
                for y in range(int(start) + 17, int(end), 18):
                    if solid[y, x - side]:
                        a[y, x] = 0
                        solid[y, x] = False
    return Image.fromarray(a, "RGBA")


def final_palette(im):
    """Catch a few boundary hues produced by RGB rounding and light animation."""
    a = np.array(im)
    hsv = np.asarray(im.convert("RGB").convert("HSV")).astype(np.float32)
    h, s = hsv[:, :, 0] * (360 / 255), hsv[:, :, 1] / 255
    v = a[:, :, :3].max(axis=2).astype(np.float32)
    active = a[:, :, 3] > 0
    red = active & ((h >= 342) | (h <= 18)) & (s > .42)
    a[red, 0] = v[red].astype(np.uint8)
    a[red, 1] = (v[red] * .71).astype(np.uint8)
    a[red, 2] = (v[red] * .53).astype(np.uint8)
    deep_red = red & (v < 32)
    a[deep_red, :3] = INK[:3]
    violet = active & (h >= 250) & (h <= 345) & (s > .07)
    a[violet, 0] = (v[violet] * .66).astype(np.uint8)
    a[violet, 1] = (v[violet] * .68).astype(np.uint8)
    a[violet, 2] = v[violet].astype(np.uint8)
    # Clamp the physical material ramps after RGB conversion. Their value
    # variation and painterly grain remain; only saturation/value are bounded.
    hsv = np.array(Image.fromarray(a, "RGBA").convert("RGB").convert("HSV"))
    hh = hsv[:, :, 0].astype(np.float32) * (360 / 255)
    ss = hsv[:, :, 1].astype(np.float32) / 255
    sand = active & (hh >= 39) & (hh <= 49) & (ss > .32)
    hsv[sand, 1] = round(.30 * 255)
    bright_ochre = active & (hh >= 20) & (hh <= 32) & (ss > .30) & (hsv[:, :, 2] > round(.60 * 255))
    hsv[bright_ochre, 0] = round(44 * 255 / 360)
    hsv[bright_ochre, 1] = round(.29 * 255)
    copper = active & (hh >= 20) & (hh <= 31) & (ss > .30) & ~bright_ochre
    hsv[copper, 1] = np.minimum(hsv[copper, 1], round(.59 * 255))
    hsv[copper, 2] = np.minimum(hsv[copper, 2], round(.58 * 255))
    a[:, :, :3] = np.array(Image.fromarray(hsv, "HSV").convert("RGB"))
    # A few near-black copper texels can round into red; use neutral ink.
    h2 = np.asarray(Image.fromarray(a, "RGBA").convert("RGB").convert("HSV")).astype(np.float32)
    hue2, sat2, val2 = h2[:, :, 0] * (360 / 255), h2[:, :, 1] / 255, h2[:, :, 2] / 255
    copper_over = active & (hue2 >= 22) & (hue2 <= 30) & (sat2 > .30) & ((sat2 > .62) | (val2 > .60))
    value = a[:, :, :3].max(axis=2).astype(np.float32)
    a[copper_over, 0] = value[copper_over].astype(np.uint8)
    a[copper_over, 1] = (value[copper_over] * .93).astype(np.uint8)
    a[copper_over, 2] = (value[copper_over] * .74).astype(np.uint8)
    near_red = active & ((hue2 >= 345) | (hue2 <= 15)) & (sat2 > .5)
    a[near_red, :3] = INK[:3]
    return Image.fromarray(a, "RGBA")


def main():
    OUT.mkdir(exist_ok=True)
    for name, (column, size) in SPEC.items():
        key = final_palette(break_straight_edges(key_pose(name, column, size)))
        key.save(HERE / f"{name}_key.png", optimize=True)
        phases = (0.0, .52, -.52, .28) if name in {
            "desert_fighter_2", "desert_fighter_3", "desert_fighter_4"} else PHASE
        frames = [break_straight_edges(light_pulse(warp(key, name, phase), pulse))
                  for phase, pulse in zip(phases, (0, 8, -5, 14))]
        frames += [break_straight_edges(tell(key, name, 4)),
                   break_straight_edges(tell(key, name, 5)),
                   break_straight_edges(hit(key, name))]
        frames = [final_palette(frame) for frame in frames]
        cell = key.width
        strip = Image.new("RGBA", (cell * 7, cell))
        for i, frame in enumerate(frames):
            strip.alpha_composite(frame, (i * cell, 0))
        strip.save(OUT / f"{name}.png", optimize=True)
        print(f"{name:22s} selected {column}  {strip.width}x{strip.height}")


if __name__ == "__main__":
    main()
