"""Assemble generated Frost damage paintings into registered Unity sprite sheets.

Run from any directory: python3 build_damage.py
All source paintings are in candidates/. Resampling is nearest-neighbour only.
"""

from __future__ import annotations

from pathlib import Path
import math

from PIL import Image, ImageChops, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SRC = Path(__file__).resolve().parent / "candidates"
CELL = 384
INK = (11, 11, 26, 255)
NEAREST = Image.Resampling.NEAREST

STAGES = ["stage1_a.png", "stage2_a.png", "stage3_a.png", "stage4_a.png"]
SMOKES = ["smoke_a.png", "smoke_b.png"]
ELECTRIC = ["electric_a.png", "electric_b.png"]


def src(name: str) -> Image.Image:
    return Image.open(SRC / name).convert("RGBA")


def clean_alpha(image: Image.Image, cutoff: int = 32) -> Image.Image:
    image = image.copy()
    a = image.getchannel("A").point(lambda v: 0 if v < cutoff else 255 if v >= 224 else v)
    image.putalpha(a)
    return image


def registered_body(name: str) -> Image.Image:
    # The paintings use a 1254px canvas. Fixed scaling retains the shared
    # eye, jaw, and chimney anchors across every stage; they are not fit
    # independently to their evolving silhouette.
    image = clean_alpha(src(name), 32)
    image = image.resize((369, 369), NEAREST)
    cell = Image.new("RGBA", (CELL, CELL))
    cell.alpha_composite(image, (9, 18))
    # The pristine alpha>=128 body starts at x=42,y=28. A 6px bbox allowance
    # permits x>=36,y>=22. This also clips overgrown stage-4 debris.
    mask = Image.new("L", (CELL, CELL))
    ImageDraw.Draw(mask).rectangle((36, 22, 380, 380), fill=255)
    a = ImageChops.multiply(cell.getchannel("A"), mask)
    cell.putalpha(a)
    return cell


def idle_b(a: Image.Image, stage: int) -> Image.Image:
    """Small light shifts plus a pixel of sway in the loose lower bracket."""
    b = a.copy()
    p = b.load()
    # The surviving eye and cryo leak breathe by one to two palette steps.
    zones = [(245, 216, 310, 271)] if stage < 2 else [(244, 143, 330, 259)]
    zones += [(166, 281, 223, 345)]
    for x0, y0, x1, y1 in zones:
        for y in range(y0, y1):
            for x in range(x0, x1):
                r, g, bl, alpha = p[x, y]
                if alpha > 128 and bl > r * 1.28 and g > r * 1.23 and g > 80:
                    if (x + y + stage) % 4 == 0:
                        p[x, y] = (min(255, int(r * 0.83)),
                                   min(255, int(g * 0.83)),
                                   min(255, int(bl * 0.88)), alpha)
    # A hanging brass tongue on the lower left shifts by a pixel. Transfer
    # only warm metal pixels so the surrounding hull stays registered.
    if stage >= 1:
        moved = []
        for y in range(264, 301):
            for x in range(171, 198):
                r, g, bl, alpha = p[x, y]
                if alpha > 200 and r > g * 1.12 and g > bl * 1.20:
                    moved.append((x, y, (r, g, bl, alpha)))
        for x, y, _ in moved:
            r, g, bl, alpha = p[x, y]
            p[x, y] = (max(4, int(r * .42)), max(7, int(g * .52)),
                       max(15, int(bl * .80)), alpha)
        for x, y, color in moved:
            if x + 1 < CELL:
                p[x + 1, y + 1] = color
    return b


def quadrant(image: Image.Image, box: tuple[int, int, int, int]) -> Image.Image:
    q = clean_alpha(image.crop(box), 42)
    a = q.getchannel("A").point(lambda v: 255 if v >= 42 else 0)
    bounds = a.getbbox()
    return q.crop(bounds) if bounds else q


def place(image: Image.Image, piece: Image.Image,
          box: tuple[int, int, int, int], opacity: float = 1.0) -> None:
    x0, y0, x1, y1 = box
    piece = piece.resize((x1 - x0, y1 - y0), NEAREST)
    if opacity < 1:
        piece.putalpha(piece.getchannel("A").point(lambda v: int(v * opacity)))
    image.alpha_composite(piece, (x0, y0))


def smoke_frame(i: int, smoke_sources: list[Image.Image]) -> Image.Image:
    frame = Image.new("RGBA", (CELL, CELL))
    phase = 2 * math.pi * i / 6
    boxes = [(0, 0, 650, 630), (650, 0, 1254, 630),
             (0, 590, 650, 1254), (650, 590, 1254, 1254)]
    targets = [(51, 29, 139, 160), (222, 24, 330, 157),
               (39, 110, 152, 231), (238, 108, 362, 231)]
    for j, (source_box, target) in enumerate(zip(boxes, targets)):
        source = smoke_sources[j % 2]
        part = quadrant(source, source_box)
        dx = round(3 * math.sin(phase + j * 1.7))
        dy = round(4 * math.cos(phase + j * 1.3))
        # Small size modulation gives the smoke a billowing, cyclic motion.
        swell = round(2 * math.sin(phase + j * 1.5))
        x0, y0, x1, y1 = target
        place(frame, part, (x0 + dx - swell, y0 + dy - swell,
                            x1 + dx + swell, y1 + dy), .94)
    # Keep the facial read clear. The plume origins are above and to the
    # sides of these protected regions.
    protected = Image.new("L", (CELL, CELL), 255)
    d = ImageDraw.Draw(protected)
    d.ellipse((124, 215, 191, 270), fill=0)
    d.ellipse((242, 215, 309, 270), fill=0)
    d.ellipse((153, 276, 235, 367), fill=0)
    frame.putalpha(ImageChops.multiply(frame.getchannel("A"), protected))
    return frame


def electric_frame(i: int, electric_sources: list[Image.Image]) -> Image.Image:
    frame = Image.new("RGBA", (CELL, CELL))
    source = electric_sources[i % 2]
    # Three generated painted arc groups are arranged around the face.
    crops = [(25, 20, 880, 820), (845, 170, 1254, 1085),
             (210, 865, 1080, 1254)]
    targets = [(73, 155, 235, 234), (263, 167, 354, 289),
               (113, 291, 281, 342)]
    for j, (crop_box, target) in enumerate(zip(crops, targets)):
        if (i + j) % 6 == 4 and j == 2:
            continue  # one gap in the discharge is part of its erratic beat
        part = quadrant(source, crop_box)
        if (i + j) % 3 == 2:
            part = part.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        x0, y0, x1, y1 = target
        dx = [-3, 3, 0, -1, 4, -2][(i + j) % 6]
        dy = [0, -4, 3, 0, -2, 4][(i + j * 2) % 6]
        place(frame, part, (x0 + dx, y0 + dy, x1 + dx, y1 + dy),
              1 if i in (1, 4) else .84)
    # Do not allow an arc to touch or cross the cell edge.
    edge = Image.new("L", (CELL, CELL))
    ImageDraw.Draw(edge).rectangle((4, 4, 379, 379), fill=255)
    frame.putalpha(ImageChops.multiply(frame.getchannel("A"), edge))
    return frame


def preview(body: list[Image.Image], smoke: list[Image.Image],
            electric: list[Image.Image]) -> None:
    pristine = Image.open(ROOT / "ref_idle_cell.png").convert("RGBA")
    frames = [pristine]
    for s, b in enumerate(body):
        f = b.copy()
        f.alpha_composite(smoke[(s + 1) % 6])
        f.alpha_composite(electric[(s + 2) % 6])
        frames.append(f)
    sheet = Image.new("RGBA", (5 * CELL * 2, CELL * 2 + 56), INK)
    d = ImageDraw.Draw(sheet)
    names = ["PRISTINE", "4 HEARTS", "3 HEARTS", "2 HEARTS", "1 HEART"]
    for i, frame in enumerate(frames):
        sheet.alpha_composite(frame.resize((CELL * 2, CELL * 2), NEAREST),
                              (i * CELL * 2, 56))
        d.text((i * CELL * 2 + 22, 18), names[i], fill=(182, 229, 246, 255))
    sheet.save(ROOT / "preview.png")

    gif_frames = []
    for i in range(6):
        f = Image.new("RGBA", (CELL, CELL), INK)
        f.alpha_composite(body[3] if i % 2 == 0 else idle_b(body[3], 3))
        f.alpha_composite(smoke[i])
        f.alpha_composite(electric[i])
        gif_frames.append(f.resize((CELL * 2, CELL * 2), NEAREST).convert("RGB"))
    gif_frames[0].save(ROOT / "preview.gif", save_all=True,
                       append_images=gif_frames[1:], duration=110, loop=0,
                       optimize=False, disposal=2)


def main() -> None:
    body = [registered_body(name) for name in STAGES]
    damage = Image.new("RGBA", (CELL * 2, CELL * 4))
    for row, image in enumerate(body):
        damage.alpha_composite(image, (0, row * CELL))
        damage.alpha_composite(idle_b(image, row), (CELL, row * CELL))
    damage.save(ROOT / "Frost_damage.png")

    smokes = [src(name) for name in SMOKES]
    electric_sources = [src(name) for name in ELECTRIC]
    smoke = [smoke_frame(i, smokes) for i in range(6)]
    electricity = [electric_frame(i, electric_sources) for i in range(6)]
    fx = Image.new("RGBA", (CELL * 6, CELL * 2))
    for i in range(6):
        fx.alpha_composite(smoke[i], (i * CELL, 0))
        fx.alpha_composite(electricity[i], (i * CELL, CELL))
    fx.save(ROOT / "Frost_damage_fx.png")
    preview(body, smoke, electricity)


if __name__ == "__main__":
    main()
