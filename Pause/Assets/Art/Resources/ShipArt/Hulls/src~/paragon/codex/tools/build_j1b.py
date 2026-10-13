"""Assemble J1b from approved keys and separately generated painted bank poses.

All scale changes and compositing use hard masks and nearest-neighbour sampling.
The glint geometry is hand placed on the approved canopy pixels.
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
CELL = 1024
GREEN = np.array((0, 255, 0), dtype=np.uint8)


def key(state):
    rgb = np.asarray(Image.open(HERE / f"j1b_ref_state{state}.png").convert("RGB")).copy()
    rgb[~painted_hull(rgb)] = GREEN
    return rgb


def painted_hull(rgb):
    v = rgb.astype(np.int16)
    r, g, b = v[:, :, 0], v[:, :, 1], v[:, :, 2]
    return ~((g > 95) & (g > r * 1.5) & (g > b * 1.38) & (g - r > 60) & (g - b > 60))


def clean_palette(rgb, mask):
    """Key the green and fold occasional generated pink pixels into red/blue."""
    out = rgb.copy()
    v = out.astype(np.int16)
    r, g, b = v[:, :, 0], v[:, :, 1], v[:, :, 2]
    pink = mask & (r > 55) & (b > 55) & (r > g * 1.15) & (b > g * 1.15)
    red = pink & (r >= b)
    blue = pink & ~red
    out[:, :, 2][red] = np.minimum(b[red], g[red]).astype(np.uint8)
    out[:, :, 0][blue] = np.minimum(r[blue], g[blue]).astype(np.uint8)
    out[~mask] = GREEN
    return out


def bank(state, direction):
    source = np.asarray(Image.open(HERE / f"j1b_bank_state{state}_{direction}.png").convert("RGB"))
    mask = painted_hull(source)
    ys, xs = np.where(mask)
    box = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
    crop = source[box[1]:box[3], box[0]:box[2]]
    crop_mask = mask[box[1]:box[3], box[0]:box[2]]

    # The generated paintings vary in framing. Restore the key's 971 x 777
    # footprint, moving the physical centre line 1% toward the rolled side.
    size = (971, 777)
    art = np.asarray(Image.fromarray(crop).resize(size, Image.Resampling.NEAREST)).copy()
    silhouette = np.asarray(Image.fromarray(crop_mask.astype("uint8") * 255).resize(size, Image.Resampling.NEAREST)) > 0
    art = clean_palette(art, silhouette)
    frame = np.empty((CELL, CELL, 3), dtype=np.uint8)
    frame[:] = GREEN
    x0 = 15 if direction == "left" else 35
    y0 = 124
    region = frame[y0:y0 + size[1], x0:x0 + size[0]]
    region[silhouette] = art[silhouette]

    # Image generation exaggerated the nose lean. Align only the painted
    # candidate's upper fuselage so its centre line moves about 1% of a cell;
    # the lower wing and pod painting remains the generated perspective art.
    nose = np.any(frame[124:200] != GREEN, axis=2)
    _, nose_x = np.where(nose)
    desired = 501 if direction == "left" else 521
    correction = desired - round(float(nose_x.mean()))
    aligned = np.empty_like(frame)
    aligned[:] = GREEN
    for y in range(CELL):
        factor = 1.0 if y <= 320 else max(0.0, (650 - y) / 330)
        dx = round(correction * factor)
        if dx >= 0:
            aligned[y, dx:] = frame[y, :CELL - dx]
        else:
            aligned[y, :dx] = frame[y, -dx:]
    return aligned


def canopy_mask(base):
    yy, xx = np.indices((CELL, CELL))
    v = base.astype(np.int16)
    blue = (v[:, :, 2] > v[:, :, 0] * 1.35) & (v[:, :, 1] > v[:, :, 0] * 1.1)
    glass = (((xx - 512) / 68) ** 2 + ((yy - 420) / 123) ** 2 < 1.0)
    return glass & blue & (v[:, :, 2] > 65)


def lights_off(base, intact):
    out = base.copy()
    yy, xx = np.indices((CELL, CELL))
    a = intact.astype(np.int16)
    b = base.astype(np.int16)
    wing = (((xx < 405) | (xx > 619)) & (yy >= 445) & (yy <= 785))
    neutral = (a.min(axis=2) > 158) & ((a.max(axis=2) - a.min(axis=2)) < 112)
    unchanged = np.abs(a - b).sum(axis=2) < 75
    lamps = wing & neutral & unchanged
    # A local dark-red lens on armour; navy for small cyan panel lamps.
    red = lamps & (a[:, :, 0] >= a[:, :, 2])
    blue = lamps & ~red
    out[red] = (55, 14, 23)
    out[blue] = (11, 25, 51)

    glass = canopy_mask(base)
    # One restrained dimming step across the saturated glass facets.
    out[glass] = np.maximum((base[glass].astype(np.float32) * 0.72).astype(np.uint8),
                            np.array((3, 12, 28), dtype=np.uint8))
    return out


def stroke_mask(points, width):
    # Manual points are placed on a 4-pixel art grid; this produces hard steps.
    low = Image.new("L", (CELL // 4, CELL // 4))
    draw = ImageDraw.Draw(low)
    draw.line([(x // 4, y // 4) for x, y in points], fill=255, width=width // 4, joint="curve")
    return np.asarray(low.resize((CELL, CELL), Image.Resampling.NEAREST)) > 0


def sparkle(cx, cy, radius):
    layer = Image.new("L", (CELL, CELL))
    d = ImageDraw.Draw(layer)
    d.polygon([(cx, cy-radius), (cx+4, cy-5), (cx+radius, cy),
               (cx+4, cy+5), (cx, cy+radius), (cx-4, cy+5),
               (cx-radius, cy), (cx-4, cy-5)], fill=255)
    return np.asarray(layer) > 0


def glint(base, side):
    out = base.copy()
    glass = canopy_mask(base)
    if side == "left":
        path = [(465, 510), (472, 479), (485, 448), (496, 416), (506, 385), (519, 348)]
        widths = (76, 28, 12)
        colours = (None, (83, 232, 254), (251, 253, 245))
        tip = (519, 348)
    else:
        path = [(515, 504), (526, 472), (536, 439), (545, 407), (554, 376)]
        widths = (28, 15, 7)
        colours = ((16, 126, 210), (72, 194, 240), (176, 228, 247))
        tip = (554, 376)
    for width, colour in zip(widths, colours):
        m = stroke_mask(path, width) & glass
        if colour is None:
            # Reflected cyan across the nearby glass facets, retaining their
            # original shading. The solid white line stays only 12 px wide.
            out[m] = np.minimum(base[m].astype(np.int16) + (42, 45, 16), 255).astype(np.uint8)
        else:
            out[m] = colour
    if side == "left":
        m = sparkle(*tip, 21) & glass
        out[m] = (255, 253, 239)
    return out


def main():
    sheets = []
    intact = key(0)
    for state in range(3):
        base = key(state)
        cells = [lights_off(base, intact), glint(base, "left"),
                 glint(lights_off(base, intact), "right"),
                 bank(state, "left"), bank(state, "right")]
        sheet = Image.new("RGB", (CELL * 3, CELL * 2), (0, 255, 0))
        for i, frame in enumerate(cells):
            sheet.paste(Image.fromarray(frame, "RGB"), ((i % 3) * CELL, (i // 3) * CELL))
        path = OUT / f"hull_redo_state{state}.png"
        sheet.save(path, optimize=True)
        print(path)
        sheets.append(sheet)

    margin = 18
    scale = 4
    w, h = CELL * 3 // scale, CELL * 2 // scale
    preview = Image.new("RGB", (w * 3 + margin * 4, h + margin * 2), "#0b0b1a")
    for i, sheet in enumerate(sheets):
        small = sheet.resize((w, h), Image.Resampling.NEAREST)
        # Only the green is transparent in the dark presentation preview.
        arr = np.asarray(small)
        alpha = Image.fromarray((np.any(arr != GREEN, axis=2) * 255).astype("uint8"))
        preview.paste(small, (margin + i * (w + margin), margin), alpha)
    path = OUT / "preview_j1b.png"
    preview.save(path, optimize=True)
    print(path)


if __name__ == "__main__":
    main()
