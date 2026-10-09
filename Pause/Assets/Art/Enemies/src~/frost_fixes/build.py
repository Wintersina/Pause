"""Rebuild the authorized Frost cells from selected painted imagegen candidates.

Run from the repository root with ``python3 Pause/Assets/Art/Enemies/src~/frost_fixes/build.py``.
Every untouched cell is copied directly from ``original``.
"""

from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np
import colorsys

ROOT = Path(__file__).resolve().parent
LIVE = ROOT.parents[2] / "Resources" / "Enemies"
ORIG = ROOT / "original"
R = Image.Resampling.NEAREST
NAMES = ("frost_rock_rime", "frost_chaser", "frost_fighter_1",
         "frost_fighter_2", "frost_rock_shard", "frost_big")


def cell(strip, index):
    h = strip.height
    return strip.crop((index * h, 0, (index + 1) * h, h))


def set_cell(strip, index, image):
    strip.paste(image, (index * strip.height, 0))


def ref_palette(names, count=72):
    samples = []
    for name in names:
        source = ORIG / (name + ".png")
        if not source.exists():
            source = LIVE / (name + ".png")
        image = Image.open(source).convert("RGBA")
        if name == "frost_big":
            # The original hit is blown out; sample only approved colored idles.
            image = image.crop((0, 0, 4 * image.height, image.height))
        a = np.asarray(image)
        pixels = a[a[:, :, 3] >= 240, :3]
        samples.append(pixels[::max(1, len(pixels) // 40000)])
    rgb = np.concatenate(samples)
    return Image.fromarray(rgb.reshape((1, len(rgb), 3)), "RGB").quantize(
        colors=count, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)


def fit_generated(path, canvas, box, palette):
    image = Image.open(path).convert("RGBA")
    a = np.asarray(image)
    yy, xx = np.where(a[:, :, 3] > 32)
    cropped = image.crop((int(xx.min()), int(yy.min()), int(xx.max()) + 1,
                          int(yy.max()) + 1))
    x, y, w, h = box
    scaled = cropped.resize((w, h), R)
    pixels = np.asarray(scaled).copy()
    # Hard alpha is important here: the selected paintings contain intermediate
    # alpha along enlarged pixel edges, which would soften the game-pixel outline.
    alpha = np.where(pixels[:, :, 3] >= 128, 255, 0).astype("uint8")
    colors = scaled.convert("RGB").quantize(palette=palette,
                                             dither=Image.Dither.NONE).convert("RGB")
    result = Image.new("RGBA", (canvas, canvas))
    colors.putalpha(Image.fromarray(alpha, "L"))
    result.paste(colors, (x, y), colors)
    return result


def clean_frost_hues(image):
    """Move source-palette reddish shadows to blue ink or copper (hue >= 18)."""
    a = np.asarray(image).copy()
    rgb = a[:, :, :3]
    colors = np.unique(rgb[a[:, :, 3] > 0].reshape(-1, 3), axis=0)
    for color in colors:
        h, s, v = colorsys.rgb_to_hsv(*(float(c) / 255 for c in color))
        hue = h * 360
        replacement = None
        if (hue < 18 or hue > 345) and s > .5:
            if v < .14:
                replacement = (7, 13, 22)
            elif v < .25:
                replacement = (61, 43, 32)
            elif v < .43:
                replacement = (120, 78, 44)
            else:
                replacement = (159, 103, 56)
        elif 260 < hue < 330 and s > .2:
            replacement = (7, 13, 22) if v < .15 else (42, 58, 80)
        if replacement is not None:
            mask = (rgb == color).all(axis=2) & (a[:, :, 3] > 0)
            rgb[mask] = replacement
    return Image.fromarray(a, "RGBA")


def pulse(frame, roi, strength):
    a = np.asarray(frame).copy()
    x0, y0, x1, y1 = roi
    block = a[y0:y1, x0:x1]
    rgb = block[:, :, :3].astype("int16")
    blue = (rgb[:, :, 2] > rgb[:, :, 0] * 1.18) & (rgb[:, :, 1] > rgb[:, :, 0] * 1.08)
    bright = (rgb[:, :, 1] > 85) & blue & (block[:, :, 3] > 0)
    if strength < 0:
        rgb[bright] = np.maximum(0, rgb[bright] + np.array([0, -32, -39]))
    else:
        rgb[bright] = np.minimum(255, rgb[bright] + np.array([9, 26, 26]))
    block[:, :, :3] = rgb.astype("uint8")
    return Image.fromarray(a, "RGBA")


def rime_frame(base, index):
    image = base.copy()
    if index == 1:
        image = pulse(image, (79, 80, 113, 114), -1)
    if index in (2, 4, 5, 6):
        image = pulse(image, (79, 80, 113, 114), 1)
    d = ImageDraw.Draw(image)
    if index == 2:
        d.line(((46, 48), (51, 44)), fill="#f2fcff", width=1)
    if index == 3:
        d.line(((139, 49), (143, 45)), fill="#bfe6ff", width=1)
    if index == 4:
        # A stepped visor/core flare and fine mist at four blade tips.
        d.polygon(((96, 85), (101, 96), (96, 106), (91, 96)), fill="#f2fcff")
        for x, y in ((96, 15), (96, 177), (29, 67), (162, 67)):
            d.point((x, y), fill="#bfe6ff")
            d.point((x + 2, y - 2), fill="#4fe6ff")
    if index == 5:
        d.polygon(((96, 82), (107, 96), (96, 110), (85, 96)), fill="#f2fcff")
        # Discrete cyan ice shards form a ring without touching any cell edge.
        for x, y in ((96, 16), (136, 27), (169, 68), (169, 121),
                     (136, 165), (96, 176), (56, 165), (23, 121),
                     (23, 68), (56, 27)):
            d.polygon(((x, y - 3), (x + 2, y), (x, y + 3), (x - 2, y)),
                      fill="#4fe6ff")
            d.point((x, y - 1), fill="#f2fcff")
    if index == 6:
        d.polygon(((96, 77), (114, 96), (96, 116), (77, 96)), fill="#4fe6ff")
        d.polygon(((96, 84), (107, 96), (96, 108), (85, 96)), fill="#f2fcff")
        # A crack in the upper blade and three small detached chips.
        d.line(((104, 34), (98, 41), (103, 47), (96, 53)), fill="#162637", width=1)
        d.line(((105, 34), (99, 41), (104, 47)), fill="#f2fcff", width=1)
        for x, y in ((35, 37), (159, 41), (148, 145)):
            d.polygon(((x, y - 2), (x + 2, y), (x, y + 2), (x - 2, y)),
                      fill="#bfe6ff")
    return image


def build():
    original = {n: Image.open(ORIG / (n + ".png")).convert("RGBA") for n in NAMES}
    output = {n: im.copy() for n, im in original.items()}

    rime_palette = ref_palette(("frost_rock_chunk", "frost_rock_shard"), 72)
    rime = clean_frost_hues(fit_generated(ROOT / "rime_candidate_selected.png", 192,
                                          (27, 8, 138, 176), rime_palette))
    for i in range(7):
        set_cell(output["frost_rock_rime"], i, rime_frame(rime, i))

    chaser = original["frost_chaser"]
    set_cell(output["frost_chaser"], 2,
             pulse(cell(chaser, 0), (78, 66, 133, 126), 1))
    set_cell(output["frost_chaser"], 3,
             pulse(cell(chaser, 1), (78, 66, 133, 126), -1))

    fighter1 = original["frost_fighter_1"]
    # The source second-flap frame has the intended wing arrangement and scale.
    f1 = pulse(cell(fighter1, 1), (79, 72, 125, 124), 1)
    set_cell(output["frost_fighter_1"], 3, f1)

    fighter2 = original["frost_fighter_2"]
    set_cell(output["frost_fighter_2"], 3,
             pulse(cell(fighter2, 0), (76, 65, 121, 116), -1))

    shard = original["frost_rock_shard"]
    set_cell(output["frost_rock_shard"], 1,
             pulse(cell(shard, 0), (77, 79, 116, 117), 1))

    big_palette = ref_palette(("frost_big",), 112)
    big_hit = clean_frost_hues(fit_generated(ROOT / "big_hit_candidate_selected.png", 256,
                                             (25, 24, 212, 220), big_palette))
    set_cell(output["frost_big"], 6, big_hit)

    for name in NAMES:
        output[name].save(LIVE / (name + ".png"))
    preview(original, output)


def preview(before, after):
    # Before/after rows per strip, both at exact nearest-neighbour 2x.
    gap = 28
    width = 7 * 256 * 2 + 60
    heights = [2 * 2 * before[n].height + gap + 30 for n in NAMES]
    sheet = Image.new("RGB", (width, sum(heights) + 24), "#0e1724")
    d = ImageDraw.Draw(sheet)
    y = 12
    for name, group_h in zip(NAMES, heights):
        h = before[name].height
        d.text((12, y), name + "  before", fill="#bfe6ff")
        row_y = y + 18
        for i in range(7):
            sprite = cell(before[name], i).resize((h * 2, h * 2), R)
            sheet.paste(sprite, (30 + i * h * 2, row_y), sprite)
        d.text((12, row_y + h * 2 + 5), name + "  after", fill="#4fe6ff")
        row_y += h * 2 + 23
        for i in range(7):
            sprite = cell(after[name], i).resize((h * 2, h * 2), R)
            sheet.paste(sprite, (30 + i * h * 2, row_y), sprite)
        y += group_h
    sheet.save(ROOT / "preview.png")

    frames = []
    for i in range(4):
        frame = Image.new("RGB", (192 * 2 + 20, 192 * 4 + 38), "#0e1724")
        fd = ImageDraw.Draw(frame)
        fd.text((8, 3), "rime idle", fill="#bfe6ff")
        fd.text((8, 192 * 2 + 22), "chaser idle", fill="#bfe6ff")
        for row, name in enumerate(("frost_rock_rime", "frost_chaser")):
            pose = cell(after[name], i).resize((384, 384), R)
            frame.paste(pose, (10, 18 + row * (384 + 20)), pose)
        frames.append(frame)
    frames[0].save(ROOT / "preview.gif", save_all=True,
                   append_images=frames[1:], duration=[210, 125, 85, 125],
                   loop=0, disposal=2, optimize=False)


if __name__ == "__main__":
    build()
