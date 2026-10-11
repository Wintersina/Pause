"""Convert the selected painted Cryo Siren concept to its seven-cell game strip.

All source sampling, sprite rotations, and preview scaling use nearest neighbour.
Run from anywhere: python3 src~/convert.py
"""

from __future__ import annotations

import colorsys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "candidate_c.png"
OUTPUT = ROOT / "frost_elite_cryo_siren.png"
CELL = 192
CX = 96
SRC_CX = 429
INK = (5, 10, 19, 255)
PALE = (187, 224, 240, 255)
ICE = (152, 209, 231, 255)
CYAN = (103, 211, 244, 255)
HOT = (243, 251, 252, 255)

# Destination y -> painted source y. The dish keeps a near-circular 68 px
# diameter, while the long stern and needle prow meet the gameplay anchors.
Y_POINTS = ((17, 25), (45, 500), (79, 800), (113, 1100),
            (155, 1580), (163, 1650))


def source_y(y: int) -> int:
    for (dy0, sy0), (dy1, sy1) in zip(Y_POINTS, Y_POINTS[1:]):
        if y <= dy1:
            return round(sy0 + (y - dy0) * (sy1 - sy0) / (dy1 - dy0))
    return Y_POINTS[-1][1]


def frost_colour(rgb: np.ndarray) -> tuple[int, int, int]:
    """Confine the generated painting to steel/ice/coolant/brass without quantizing."""
    r, g, b = (int(v) / 255 for v in rgb[:3])
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    if s < .12:
        # Neutral highlights retain the original painterly value differences.
        h, s = .575, min(.11, s)
    elif .055 <= h <= .165 and s > .18:
        # Restrained dull brass, structural only.
        h, s, v = .105, min(s, .57), min(v, .75)
    elif .49 <= h <= .585:
        # White-cyan coolant is deliberately blue of pure cyan.
        h = .553 + (h - .553) * .13
        s = min(s, .82)
    else:
        # Any stray chroma from image generation becomes blue steel.
        h = .602 + (h - .602) * .13 if .585 < h < .75 else .605
        s = min(s, .56)
    return tuple(round(c * 255) for c in colorsys.hsv_to_rgb(h, s, v))


def painted_hull() -> Image.Image:
    src = np.asarray(Image.open(SOURCE).convert("RGBA"))
    out = np.zeros((CELL, CELL, 4), dtype=np.uint8)
    for y in range(17, 164):
        sy = source_y(y)
        for x in range(54, 138):
            sx = round(SRC_CX + (x - CX) * 8)
            if sx < 0 or sx >= src.shape[1]:
                continue
            pixel = src[sy, sx]
            if pixel[3] >= 190:
                out[y, x, :3] = frost_colour(pixel)
                out[y, x, 3] = 255

    # Exact one-pixel, four-connected exterior ink. Generated anti-aliasing is
    # thresholded away so the actual ship never has soft edge alpha.
    body = out[:, :, 3] == 255
    around = np.zeros_like(body)
    around[1:] |= body[:-1]
    around[:-1] |= body[1:]
    around[:, 1:] |= body[:, :-1]
    around[:, :-1] |= body[:, 1:]
    edge = around & ~body
    out[edge] = INK

    # A sparse cool rim inside the lower-right contour. Preserve the source's
    # surface marks and its dark panel seams elsewhere.
    inward = np.zeros_like(body)
    inward[:-1] |= ~body[1:]
    inward[:, :-1] |= ~body[:, 1:]
    rim = body & inward & (np.indices(body.shape)[0] > 42)
    for y, x in zip(*np.nonzero(rim)):
        if (x + 2 * y) % 4 == 0:
            rgb = out[y, x, :3].astype(float)
            out[y, x, :3] = np.minimum(255, rgb * .62 + np.array(ICE[:3]) * .38)

    return Image.fromarray(out, "RGBA")


def forward_outlet(im: Image.Image) -> None:
    # The source painting places a small orb further up the prow. This second,
    # game-facing cryo outlet sits at the existing elite muzzle anchor.
    d = ImageDraw.Draw(im)
    d.ellipse((91, 57, 101, 67), fill=INK)
    d.ellipse((92, 58, 100, 66), fill=(36, 78, 104, 255))
    d.ellipse((93, 59, 99, 65), fill=(82, 158, 190, 255))
    d.ellipse((94, 60, 98, 64), fill=(175, 234, 245, 255))
    d.point((96, 62), fill=HOT)


def exhaust(im: Image.Image, state: str) -> None:
    d = ImageDraw.Draw(im)
    if state == "off":
        for x in (90, 102):
            d.ellipse((x - 3, 152, x + 3, 157), fill=INK)
            d.ellipse((x - 2, 153, x + 2, 156), fill=(21, 37, 56, 255))
        return

    if state == "idle":
        length, width = 3, 2
    elif state == "ignition":
        length, width = 19, 4
    else:
        length, width = 12, 3
    for x in (90, 102):
        d.ellipse((x - 3, 152, x + 3, 158), fill=INK)
        d.ellipse((x - 2, 153, x + 2, 157), fill=(41, 110, 150, 255))
        d.point((x, 155), fill=HOT)
        if state == "idle":
            d.line((x, 157, x, 158), fill=CYAN)
            continue
        tip = min(175, 157 + length)
        d.polygon(((x - width, 157), (x + width, 157), (x + 2, tip - 4),
                   (x, tip), (x - 2, tip - 4)), fill=(32, 145, 207, 255))
        d.polygon(((x - 2, 157), (x + 2, 157), (x + 1, tip - 5),
                   (x, tip - 2), (x - 1, tip - 5)), fill=CYAN)
        d.polygon(((x - 1, 157), (x + 1, 157), (x, tip - 7)), fill=HOT)


def damaged(im: Image.Image) -> None:
    d = ImageDraw.Draw(im)
    # Broken ice slabs, dark fractures, and a few exposed pale edges.
    for points in [((112, 51), (107, 58), (113, 65), (108, 73)),
                   ((124, 78), (117, 82), (121, 91), (114, 98)),
                   ((76, 102), (82, 105), (79, 112)),
                   ((105, 119), (101, 126), (107, 133))]:
        d.line(points, fill=(10, 23, 38, 255), width=2, joint="curve")
        d.line(tuple((x - 1, y - 1) for x, y in points),
               fill=PALE, width=1, joint="curve")
    # Starboard engine still runs, but intermittently spits cold sparks.
    d.polygon(((100, 158), (104, 158), (103, 165), (101, 163)),
              fill=(77, 186, 227, 255))
    for x, y in ((107, 158), (111, 161), (109, 167), (104, 169)):
        d.point((x, y), fill=HOT)
    d.line((107, 158, 110, 156), fill=CYAN)


def make_strip() -> None:
    hull = painted_hull()
    forward_outlet(hull)
    cells = []
    for state in ("off", "idle", "ignition", "hover", "hover", "hover", "hover"):
        cell = hull.copy()
        exhaust(cell, state)
        cells.append(cell)
    cells[4] = cells[4].rotate(8, resample=Image.Resampling.NEAREST,
                                center=(96, 96), expand=False)
    cells[5] = cells[5].rotate(-8, resample=Image.Resampling.NEAREST,
                                center=(96, 96), expand=False)
    damaged(cells[6])
    strip = Image.new("RGBA", (CELL * 7, CELL))
    for i, cell in enumerate(cells):
        strip.paste(cell, (i * CELL, 0))
    strip.save(OUTPUT)
    contact_sheet(strip)
    preview(cells)
    print(OUTPUT)


def contact_sheet(strip: Image.Image) -> None:
    refs = Path(__file__).resolve().parents[5] / "Resources" / "Elites"
    paths = (None,
             refs / "Frost" / "frost_elite_cryo_siren.png",
             refs / "Frost" / "frost_elite_floe_harrower.png",
             refs / "Storm" / "storm_elite_rod_dancer.png")
    labels = ("NEW CRYO SIREN", "OLD CRYO SIREN", "FLOE HARROWER", "STORM ROD DANCER")
    ground = (11, 11, 26, 255)
    row_h = 408
    sheet = Image.new("RGBA", (CELL * 7 * 2, row_h * 4), ground)
    d = ImageDraw.Draw(sheet)
    for row, (path, label) in enumerate(zip(paths, labels)):
        src = strip if path is None else Image.open(path).convert("RGBA")
        zoom = src.resize((src.width * 2, src.height * 2), Image.Resampling.NEAREST)
        sheet.alpha_composite(zoom, (0, row * row_h + 24))
        d.text((5, row * row_h + 5), label, fill=(178, 200, 216, 255))
    sheet.save(ROOT / "contact_sheet.png")


def preview(cells: list[Image.Image]) -> None:
    frames = []
    for i in (3, 4, 3, 5):
        frame = Image.new("RGBA", (384, 384), (11, 11, 26, 255))
        frame.alpha_composite(cells[i].resize((384, 384), Image.Resampling.NEAREST))
        frames.append(frame.convert("RGB"))
    frames[0].save(ROOT / "preview.gif", save_all=True, append_images=frames[1:],
                   duration=(180, 180, 180, 180), loop=0, disposal=2,
                   optimize=False)


if __name__ == "__main__":
    make_strip()
