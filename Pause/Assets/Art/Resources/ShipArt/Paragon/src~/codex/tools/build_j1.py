"""Assemble the Paragon J1 flipbooks from three image-generated painted hulls.

Only hard masks and nearest-neighbour sampling are used. The damaged paintings
are clipped to the intact painted silhouette, including its dark edge.
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
CELL = 1024
GREEN = np.array([0, 255, 0], dtype=np.uint8)
PAINTINGS = (
    "j1_painted_intact.png",
    "j1_painted_damaged.png",
    "j1_painted_critical.png",
)


def is_green(rgb):
    v = rgb.astype(np.int16)
    r, g, b = v[:, :, 0], v[:, :, 1], v[:, :, 2]
    return (g > 100) & (g > r * 1.55) & (g > b * 1.4) & (g - r > 65) & (g - b > 65)


def resize_painting(filename):
    im = Image.open(HERE / filename).convert("RGB")
    return np.asarray(im.resize((CELL, CELL), Image.Resampling.NEAREST)).copy()


def remove_magenta(rgb, hull):
    # The generator occasionally places a few violet edge pixels. Send those
    # to the existing dark blue ramp without altering red/orange armour.
    v = rgb.astype(np.int16)
    r, g, b = v[:, :, 0], v[:, :, 1], v[:, :, 2]
    violet = hull & (b > 45) & (r > 35) & (b > r * 0.63) & (r > g * 1.28) & (b > g * 1.42)
    rgb[:, :, 0][violet] = np.clip((b[violet] * 0.19 + g[violet] * 0.25), 0, 255).astype(np.uint8)
    # Red-dominant crimson fringes belong to the red armour ramp.
    crimson = hull & (r > 80) & (r >= b) & (b > r * 0.18) & (g < r * 0.35) & (b > g * 1.3)
    rgb[:, :, 2][crimson] = np.minimum(b[crimson], r[crimson] * 0.12).astype(np.uint8)
    v = rgb.astype(np.int16)
    r, g, b = v[:, :, 0], v[:, :, 1], v[:, :, 2]
    pink = hull & (r > g * 1.15) & (b > g * 1.15) & (np.maximum(r, b) > 75) & (np.minimum(r, b) > 45)
    red_side = pink & (r >= b)
    blue_side = pink & (b > r)
    rgb[:, :, 2][red_side] = g[red_side].astype(np.uint8)
    rgb[:, :, 0][blue_side] = g[blue_side].astype(np.uint8)


def master_states():
    painted = [resize_painting(name) for name in PAINTINGS]
    hull = ~is_green(painted[0])
    # Keep the master ink edge even where an edited painting shifted it.
    interior = hull.copy()
    for step in (1, 2, 3):
        interior &= np.roll(hull, step, axis=0)
        interior &= np.roll(hull, -step, axis=0)
        interior &= np.roll(hull, step, axis=1)
        interior &= np.roll(hull, -step, axis=1)
    states = []
    for i, source in enumerate(painted):
        frame = np.empty_like(source)
        frame[:] = GREEN
        frame[hull] = painted[0][hull]
        if i:
            keep = interior & ~is_green(source)
            frame[keep] = source[keep]
        remove_magenta(frame, hull)
        states.append(frame)
    return states


def shift(frame, dy):
    result = np.empty_like(frame)
    result[:] = GREEN
    if dy < 0:
        result[:dy] = frame[-dy:]
    elif dy > 0:
        result[dy:] = frame[:-dy]
    else:
        result[:] = frame
    return result


def lights_off(frame, intact):
    out = frame.copy()
    y, x = np.indices((CELL, CELL))
    wing = (((x < 390) | (x > 634)) & (y > 430) & (y < 820))
    a = intact.astype(np.int16)
    b = frame.astype(np.int16)
    neutral_bright = (a.min(axis=2) > 157) & ((a.max(axis=2) - a.min(axis=2)) < 105)
    same_light = (np.abs(a - b).max(axis=2) < 38)
    target = wing & neutral_bright & same_light
    out[target] = (np.maximum(b[target] * np.array([0.22, 0.35, 0.57]),
                              np.array([10, 18, 35]))).astype(np.uint8)
    return out


def canopy_glint(frame, which):
    out = Image.fromarray(frame, "RGB")
    layer = Image.new("RGB", (CELL, CELL), (0, 255, 0))
    draw = ImageDraw.Draw(layer)
    if which == "left":
        points = [(472, 366), (483, 385), (496, 407), (512, 430)]
    else:
        points = [(512, 364), (525, 384), (538, 405), (551, 427)]
    draw.line(points, fill=(24, 199, 252), width=9, joint="curve")
    draw.line(points, fill=(246, 253, 251), width=4, joint="curve")
    glint = np.asarray(layer)
    out_data = np.asarray(out).copy()
    yy, xx = np.indices((CELL, CELL))
    canopy = ((xx - 512) / 66) ** 2 + ((yy - 423) / 106) ** 2 < 0.80
    present = np.any(glint != GREEN, axis=2)
    mask = canopy & present & ~is_green(out_data)
    out_data[mask] = glint[mask]
    return out_data


def bank(frame, direction):
    yy, xx = np.indices((CELL, CELL))
    # Nose leans about 1.5% of the cell; left and right halves receive
    # opposite two-percent compression/expansion, all sampled by nearest.
    lean = direction * 15 * np.clip((840 - yy) / 700, 0, 1)
    x0 = xx - lean
    scale = np.where(x0 < 512, 1 + direction * 0.02, 1 - direction * 0.02)
    source_x = np.rint(512 + (x0 - 512) / scale).astype(np.int32)
    source_x = np.clip(source_x, 0, CELL - 1)
    return frame[yy, source_x]


def hit_flash(frame):
    result = np.empty_like(frame)
    result[:] = GREEN
    hull = ~is_green(frame)
    peak = frame.max(axis=2)
    result[hull] = (244, 234, 212)
    result[hull & (peak < 45)] = (6, 8, 20)
    result[hull & (peak >= 45) & (peak < 85)] = (92, 88, 91)
    result[hull & (peak >= 85) & (peak < 135)] = (175, 164, 151)
    result[hull & (peak > 235)] = (255, 250, 232)
    return result


def poses(base, intact):
    up = shift(base, -round(CELL * 0.02))
    return [
        base,
        up,
        lights_off(up, shift(intact, -round(CELL * 0.02))),
        canopy_glint(base, "left"),
        lights_off(canopy_glint(base, "right"), intact),
        shift(base, round(CELL * 0.02)),
        bank(base, -1),
        bank(base, +1),
        hit_flash(base),
    ]


def main():
    states = master_states()
    sheets = []
    for index, base in enumerate(states):
        sheet = Image.new("RGB", (CELL * 3, CELL * 3), tuple(GREEN))
        for pose, data in enumerate(poses(base, states[0])):
            sheet.paste(Image.fromarray(data, "RGB"), ((pose % 3) * CELL, (pose // 3) * CELL))
        path = OUT / f"hull_state{index}.png"
        sheet.save(path, optimize=True)
        sheets.append(sheet)
        print(f"wrote {path}")

    scale = 4
    margin = 20
    side = CELL * 3 // scale
    preview = Image.new("RGB", (side * 3 + margin * 4, side + margin * 2), "#0b0b1a")
    for index, sheet in enumerate(sheets):
        small = sheet.resize((side, side), Image.Resampling.NEAREST)
        preview.paste(small, (margin + index * (side + margin), margin))
    preview.save(OUT / "preview_j1.png", optimize=True)
    print(f"wrote {OUT / 'preview_j1.png'}")


if __name__ == "__main__":
    main()
