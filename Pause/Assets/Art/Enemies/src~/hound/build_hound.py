"""Assemble image-generated Steel Hound key poses at the shipped cell size.

Inputs are the retained painted image-generation candidates. All resampling is
nearest-neighbour; the original strip is retained separately as original.png.
"""
from __future__ import annotations

import colorsys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
LIVE = HERE.parents[2] / "Resources/Enemies/space_chaser.png"
CELL = 192


def remove_player_red(image: Image.Image) -> Image.Image:
    """Map stray generated crimson to magenta or warm copper, preserving value."""
    a = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    rgb = a[:, :, :3].astype(np.float32) / 255
    mx = rgb.max(2)
    mn = rgb.min(2)
    active = (a[:, :, 3] > 0) & (mx > .18) & ((mx - mn) / np.maximum(mx, 1e-6) > .5)
    ys, xs = np.where(active)
    for y, x in zip(ys, xs):
        h, s, v = colorsys.rgb_to_hsv(*rgb[y, x])
        if h < 18 / 360 or h > 342 / 360:
            # Red in a neon flare becomes magenta; rusty metal becomes copper.
            target = 320 / 360 if rgb[y, x, 2] > rgb[y, x, 1] else 25 / 360
            a[y, x, :3] = np.asarray(colorsys.hsv_to_rgb(target, s, v)) * 255
    return Image.fromarray(a, "RGBA")


def frame(filename: str, index: int, count: int, scale: float, center_y: float) -> Image.Image:
    source = Image.open(HERE / filename).convert("RGBA")
    sw, sh = source.size
    x0 = round(sw * index / count)
    x1 = round(sw * (index + 1) / count)
    source = source.crop((x0, 0, x1, sh))
    # Some generated candidates have barely visible ghost pixels far from the
    # painted sprite. Drop them, then quantize only alpha for crisp pixel rings.
    data = np.asarray(source).copy()
    alpha = data[:, :, 3]
    alpha[alpha < 32] = 0
    alpha[(alpha >= 32) & (alpha < 96)] = 64
    alpha[(alpha >= 96) & (alpha < 160)] = 128
    alpha[(alpha >= 160) & (alpha < 224)] = 192
    alpha[alpha >= 224] = 255
    data[:, :, 3] = alpha
    source = remove_player_red(Image.fromarray(data, "RGBA"))
    scaled = source.resize((round(source.width * scale), round(source.height * scale)), Image.Resampling.NEAREST)
    cell = Image.new("RGBA", (CELL, CELL))
    cx = source.width / 2
    x = round(CELL / 2 - cx * scale)
    y = round(CELL / 2 - center_y * scale)
    cell.alpha_composite(scaled, (x, y))
    return cell


def main() -> None:
    frames = [frame("candidate_idle_b.png", i, 4, .33, 350) for i in range(4)]
    # The generator varied some tiny steel scuffs between drawings. Hold its
    # painted central armor from the first key, while leaving all animated
    # pods, pipe runs, claws, and belly cores from their respective key poses.
    hull_mask = Image.new("L", (CELL, CELL))
    ImageDraw.Draw(hull_mask).polygon(
        [(87, 24), (104, 24), (116, 46), (122, 76), (117, 112),
         (106, 137), (95, 146), (84, 136), (72, 112), (69, 76), (76, 46)],
        fill=255,
    )
    for i in range(1, 4):
        moving = frames[i].copy()
        frames[i].paste(frames[0], (0, 0), hull_mask)
        # Retain the image-generated cyan visor scan on top of the fixed plate.
        a = np.asarray(moving)
        scan = ((a[:, :, 2] > 135) & (a[:, :, 1] > 120) &
                (a[:, :, 2] > a[:, :, 0] * 1.25) & (a[:, :, 3] > 190))
        scan &= np.asarray(hull_mask) > 0
        frames[i].paste(moving, (0, 0), Image.fromarray((scan * 255).astype(np.uint8), "L"))
    # Asymmetric hot vent sectors make the painted radial fans visibly advance
    # one quarter-turn per idle key; the two counter-rotate like pursuit jets.
    phases = [(0, -9), (9, 0), (0, 9), (-9, 0)]
    for i, f in enumerate(frames):
        draw = ImageDraw.Draw(f)
        for cx, direction in ((37, 1), (155, -1)):
            dx, dy = phases[(i * direction) % 4]
            x, y = cx + dx, 62 + dy
            draw.rectangle((x - 1, y - 1, x + 1, y + 1), fill="#f57af9")
            draw.point((x, y), fill="#fff0ff")
    frames += [frame("candidate_tell_a.png", i, 3, .26, 362) for i in range(3)]
    sheet = Image.new("RGBA", (CELL * 7, CELL))
    for i, f in enumerate(frames):
        sheet.alpha_composite(f, (CELL * i, 0))
    sheet.save(LIVE)

    bg = Image.new("RGB", (CELL * 7 * 2, CELL * 2 * 2 + 52), "#0b0b1a")
    draw = ImageDraw.Draw(bg)
    labels = ["idle 0", "idle 1", "idle 2", "idle 3", "tell A", "tell B", "hit"]
    for i, f in enumerate(frames):
        tile = Image.new("RGBA", (CELL, CELL), "#0b0b1a")
        tile.alpha_composite(f)
        bg.paste(tile.convert("RGB").resize((CELL * 2, CELL * 2), Image.Resampling.NEAREST), (i * CELL * 2, 24))
        draw.text((i * CELL * 2 + 8, 5), labels[i], fill="#d6e7ef")
    for i in range(3):
        a = np.asarray(frames[i].convert("RGBA"), dtype=np.int16)
        b = np.asarray(frames[i + 1].convert("RGBA"), dtype=np.int16)
        magnitude = np.max(np.abs(a - b), axis=2).astype(np.uint8)
        diff = np.zeros((CELL, CELL, 3), dtype=np.uint8)
        diff[:, :, 0] = magnitude
        diff[:, :, 2] = np.minimum(magnitude.astype(np.uint16) * 2, 255).astype(np.uint8)
        diff[:, :, 1] = np.minimum(magnitude.astype(np.uint16) // 3, 255).astype(np.uint8)
        d = Image.fromarray(diff, "RGB").resize((CELL * 2, CELL * 2), Image.Resampling.NEAREST)
        bg.paste(d, (i * CELL * 2, CELL * 2 + 52))
        draw.text((i * CELL * 2 + 8, CELL * 2 + 31), f"difference {i} → {i+1}", fill="#d6e7ef")
    bg.save(HERE / "preview.png")

    gif = [f.copy() for f in frames]
    # GIF stores time in 10 ms units, so 80/90 ms holds average about 12 fps.
    gif[0].save(HERE / "preview.gif", save_all=True, append_images=gif[1:] + gif[3:0:-1],
                duration=[80, 80, 90, 80, 120, 90, 120, 80, 90, 80], loop=0, disposal=2)


if __name__ == "__main__":
    main()
