"""Build staged, pixel-clean boss death strips from selected imagegen poses.

Inputs in this directory are preserved for review. Run with Pillow and NumPy:
    python3 build_death_strips.py
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
BOSSES = OUT.parents[1] / "Resources" / "Bosses"
CELL = 384
SOURCE_CELL = 362
SOURCE_HEIGHT = 724
N = Image.Resampling.NEAREST

CHOICES = {
    "Space": ["A", "A", "A", "A", "A", "B"],
    "Frost": ["B", "B", "B", "B", "B", "A"],
    "Verdant": ["A", "A", "B", "B", "B", "B"],
}


def alpha_bbox(im, threshold=72):
    a = np.asarray(im.getchannel("A"))
    yy, xx = np.where(a >= threshold)
    if len(xx) == 0:
        raise ValueError("Empty generated pose")
    return (int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1)


def centroid(im, threshold=72):
    a = np.asarray(im.getchannel("A"))
    yy, xx = np.where(a >= threshold)
    return np.array((float(xx.mean()), float(yy.mean())))


def threshold_alpha(im):
    a = np.asarray(im, dtype=np.uint8).copy()
    a[a[:, :, 3] < 48, 3] = 0
    a[a[:, :, 3] == 0, :3] = 0
    return Image.fromarray(a, "RGBA")


def generated_pose(boss, candidate, index):
    strip = Image.open(HERE / f"{boss}_candidate_{candidate}.png").convert("RGBA")
    assert strip.size == (SOURCE_CELL * 6, SOURCE_HEIGHT)
    im = strip.crop((index * SOURCE_CELL, 0, (index + 1) * SOURCE_CELL, SOURCE_HEIGHT))
    im = threshold_alpha(im)
    x0, y0, x1, y1 = alpha_bbox(im)
    return im.crop((max(0, x0 - 2), max(0, y0 - 2), min(SOURCE_CELL, x1 + 2), min(SOURCE_HEIGHT, y1 + 2)))


def fit_on_cell(im, width, height, center, stretch=False):
    if stretch:
        dims = (width, height)
    else:
        scale = min(width / im.width, height / im.height)
        dims = (max(1, round(im.width * scale)), max(1, round(im.height * scale)))
    im = im.resize(dims, N)
    frame = Image.new("RGBA", (CELL, CELL))
    x = round(center[0] - im.width / 2)
    y = round(center[1] - im.height / 2)
    frame.paste(im, (x, y), im)
    return frame


def clip_frame(im, rect):
    a = np.asarray(im, dtype=np.uint8).copy()
    x0, y0, x1, y1 = rect
    x0, y0 = max(6, x0), max(6, y0)
    x1, y1 = min(378, x1), min(378, y1)
    a[:y0] = 0
    a[y1:] = 0
    a[:, :x0] = 0
    a[:, x1:] = 0
    return Image.fromarray(a, "RGBA")


def shift_to_centroid(im, target):
    for _ in range(2):
        delta = np.rint(target - centroid(im)).astype(int)
        if max(abs(delta)) == 0:
            break
        shifted = Image.new("RGBA", (CELL, CELL))
        shifted.paste(im, (int(delta[0]), int(delta[1])))
        im = shifted
    return im


def stage_four(boss):
    damage = Image.open(BOSSES / f"{boss}_damage.png").convert("RGBA")
    return damage.crop((0, 3 * CELL, CELL, 4 * CELL))


def wreck_frame(boss, idle):
    idle_box = alpha_bbox(idle)
    target_center = centroid(idle)
    allowed = (idle_box[0] - 6, idle_box[1] - 6, idle_box[2] + 6, idle_box[3] + 6)
    generated = generated_pose(boss, CHOICES[boss][0], 0)
    if boss == "Verdant":
        # The generated wreck carries torn leaf plates and the opened pod.
        frame = fit_on_cell(generated, 348, 337, target_center, stretch=True)
    else:
        # Preserve exact original plate, cannon, eye, and engine geometry.
        frame = stage_four(boss)
        frame = shift_to_centroid(frame, target_center)
        light = fit_on_cell(generated, 348, 342, target_center)
        data = np.asarray(light, dtype=np.uint8).copy()
        rgb = data[:, :, :3].astype(np.float32)
        lum = rgb.max(axis=2)
        bright = lum > (204 if boss == "Space" else 210)
        data[~bright, 3] = 0
        data[bright, 3] = (data[bright, 3].astype(np.float32) * 0.86).astype(np.uint8)
        frame.alpha_composite(Image.fromarray(data, "RGBA"))
    frame = clip_frame(frame, allowed)
    frame = shift_to_centroid(frame, target_center)
    return clip_frame(frame, allowed)


def color_clean(im, boss):
    # Generated sheets and some source sprites carry crimson fringe pixels.
    # Shift only the prohibited saturated red hue band into each world palette.
    rgba = np.asarray(im.convert("RGBA"), dtype=np.uint8).copy()
    rgb = Image.fromarray(rgba[:, :, :3], "RGB")
    hsv = np.asarray(rgb.convert("HSV"), dtype=np.uint8).copy()
    h, s = hsv[:, :, 0], hsv[:, :, 1]
    red = ((h <= 11) | (h >= 244)) & (s >= 128) & (rgba[:, :, 3] >= 48)
    hsv[:, :, 0][red] = {"Space": 225, "Frost": 156, "Verdant": 29}[boss]
    if boss == "Frost":
        cyan = (h >= 120) & (h <= 147) & (s > 160) & (rgba[:, :, 3] > 0)
        hsv[:, :, 0][cyan] = 155
        hsv[:, :, 1][cyan] = np.minimum(hsv[:, :, 1][cyan], 155)
    cleaned = np.asarray(Image.fromarray(hsv, "HSV").convert("RGB"), dtype=np.uint8)
    rgba[:, :, :3] = cleaned
    rgba[rgba[:, :, 3] == 0, :3] = 0
    return Image.fromarray(rgba, "RGBA")


def pixel_clean(im, boss):
    im = color_clean(threshold_alpha(im), boss)
    # Median-cut selects a compact set of colors without interpolation or dithering.
    pal = im.convert("RGB").quantize(colors=96, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    rgb = pal.convert("RGB")
    rgb.putalpha(im.getchannel("A"))
    return color_clean(rgb, boss)


def build_boss(boss):
    idle = Image.open(HERE / f"{boss}_idle.png").convert("RGBA")
    assert idle.size == (CELL, CELL)
    frames = [wreck_frame(boss, idle)]
    targets = {
        1: (354, 350, (192, 191)),
        2: (356, 356, (192, 192)),
        3: (356, 356, (192, 192)),
        4: (354, 354, (192, 193)),
        5: (345, 330, (192, 205)),
    }
    for i in range(1, 6):
        generated = generated_pose(boss, CHOICES[boss][i], i)
        w, h, center = targets[i]
        frame = fit_on_cell(generated, w, h, center)
        # The raw sheets have occasional overlap at the panel seams. An 18 px
        # transparent gutter keeps every pose independent in the final strip.
        left = 76 if i == 5 and boss in ("Space", "Frost") else (30 if i == 5 else 18)
        frames.append(clip_frame(frame, (left, 18, 366, 366)))
    frames = [pixel_clean(f, boss) for f in frames]
    strip = Image.new("RGBA", (CELL * 6, CELL))
    for i, frame in enumerate(frames):
        strip.paste(frame, (i * CELL, 0))
    strip.save(OUT / f"{boss}_death.png", optimize=True)
    return idle, frames


def previews(assets):
    bg = (11, 11, 26, 255)
    scale = 2
    margin, gutter, heading = 24, 12, 36
    pitch = CELL * scale + gutter
    sheet = Image.new("RGBA", (margin * 2 + pitch * 7 - gutter,
                                 margin * 2 + (heading + pitch) * 3 - gutter), bg)
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default()
    for row, (boss, (idle, frames)) in enumerate(assets.items()):
        y = margin + row * (heading + pitch)
        draw.text((margin, y), f"{boss.upper()}  |  IDLE  /  DEATH 0-5", fill=(218, 222, 232), font=font)
        for col, frame in enumerate([idle] + frames):
            sheet.alpha_composite(frame.resize((CELL * scale, CELL * scale), N),
                                  (margin + col * pitch, y + heading))
    sheet.save(OUT / "preview.png", optimize=True)

    gif_frames = []
    for i in range(6):
        canvas = Image.new("RGBA", (CELL * 3, CELL), bg)
        for col, (_, (_, frames)) in enumerate(assets.items()):
            canvas.alpha_composite(frames[i], (col * CELL, 0))
        gif_frames.append(canvas.convert("RGB").quantize(colors=255, dither=Image.Dither.NONE))
    gif_frames[0].save(OUT / "preview.gif", save_all=True, append_images=gif_frames[1:],
                       duration=[120] * 6, loop=0, disposal=2, optimize=False)


if __name__ == "__main__":
    assets = {boss: build_boss(boss) for boss in CHOICES}
    previews(assets)
