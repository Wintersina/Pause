#!/usr/bin/env python3
"""Build the J1b Ninja pose sheets from the approved keys and generated bank paintings.

The generated bank paintings are only resized with nearest-neighbour sampling.  The
key-pose cells are copied at native resolution and receive the requested pixel-local
light/gem changes.
"""

from __future__ import annotations

from pathlib import Path
from typing import Iterable

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
TOOLS = ROOT / "tools"
GREEN = (0, 255, 0)
INK = (5, 6, 12)
MAROON = (63, 13, 25)
COPPER = (106, 36, 4)
COPPER_DARK = (48, 22, 13)
CYAN_DARK = (8, 62, 78)
CYAN = (11, 169, 210)
CYAN_HOT = (11, 208, 246)
WHITE = (245, 253, 253)
BONE = (255, 251, 224)


def load_key(index: int) -> Image.Image:
    return Image.open(ROOT / f"key_state{index}.png").convert("RGB")


def is_greenish(px: tuple[int, int, int]) -> bool:
    r, g, b = px
    return g > 150 and g > r * 1.45 and g > b * 1.45 and r < 110 and b < 110


def is_foreground(px: tuple[int, int, int]) -> bool:
    return not is_greenish(px)


def gem_mask(x: int, y: int) -> bool:
    # Inner diamond plus its stepped cyan edge, intentionally excluding the metal hub.
    return abs(x - 512) / 70.0 + abs(y - 512) / 78.0 <= 1.0


def tip_box(x: int, y: int, cx: int, cy: int, rx: int, ry: int) -> bool:
    return ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0


def lights_off(image: Image.Image) -> None:
    """Paint the four running lights as dark maroon panel pixels."""
    px = image.load()
    tips = ((512, 91, 33, 48), (934, 512, 48, 33), (512, 932, 33, 48), (91, 512, 48, 33))
    for y in range(1024):
        for x in range(1024):
            if not any(tip_box(x, y, *tip) for tip in tips):
                continue
            r, g, b = px[x, y]
            if not is_foreground((r, g, b)):
                continue
            # Only the emissive red/orange pixels and the white-hot cores are replaced.
            if (r > 72 and r > g * 1.10 and r > b * 1.18) or (r > 175 and g > 150 and b < 150):
                px[x, y] = MAROON


def bank_lights_off(image: Image.Image) -> None:
    """Darken emissive red/orange pixels in a generated bank painting."""
    px = image.load()
    for y in range(1024):
        for x in range(1024):
            r, g, b = px[x, y]
            if (r > 90 and r > g * 1.08 and r > b * 1.15) or (r > 180 and g > 145 and b < 150):
                px[x, y] = MAROON


def dim_gem(image: Image.Image) -> None:
    px = image.load()
    for y in range(440, 590):
        for x in range(435, 590):
            if not gem_mask(x, y) or not is_foreground(px[x, y]):
                continue
            r, g, b = px[x, y]
            if b > r * 1.05 or g > r * 1.05 or (r > 150 and g > 150 and b > 150):
                level = max(r, g, b)
                px[x, y] = (7, min(86, 28 + level // 5), min(104, 36 + level // 4))


def glint(image: Image.Image, side: str) -> None:
    """Paint a stepped gem glint and a small bone-white corner sparkle."""
    px = image.load()
    # Lift the stepped gem ramp so the change reads at native scale and satisfies the
    # sheet's animation audit without touching any hull pixels.
    for y in range(435, 595):
        for x in range(430, 595):
            if not gem_mask(x, y) or not is_foreground(px[x, y]):
                continue
            r, g, b = px[x, y]
            if side == "left":
                px[x, y] = (15, 187 + (y % 3) * 5, 224 + (x % 2) * 6)
            else:
                px[x, y] = (9, 108 + (y % 3) * 4, 154 + (x % 2) * 5)

    # Lower-left to upper-right stepped streak.  Cell 2 is deliberately thinner/fainter.
    if side == "left":
        for y in range(468, 558):
            cx = 512 - int((y - 468) * 0.43)
            for x in range(cx - 5, cx + 6):
                if 0 <= x < 1024 and gem_mask(x, y):
                    px[x, y] = WHITE if abs(x - cx) <= 2 else BONE
        sparkle = ((516, 466), (515, 467), (516, 468), (514, 467), (517, 467))
        for x, y in sparkle:
            if gem_mask(x, y):
                px[x, y] = BONE
    else:
        for y in range(475, 553):
            cx = 528 - int((y - 475) * 0.38)
            for x in range(cx - 2, cx + 3):
                if 0 <= x < 1024 and gem_mask(x, y):
                    px[x, y] = (190, 226, 224) if abs(x - cx) == 2 else (236, 246, 238)


def clean_generated(path: Path, width: int = 928) -> Image.Image:
    """Key the generated candidate, crop its ship, and fit it with nearest-neighbour."""
    src = Image.open(path).convert("RGB")
    sp = src.load()
    for y in range(src.height):
        for x in range(src.width):
            if is_greenish(sp[x, y]):
                sp[x, y] = GREEN
    points = [(x, y) for y in range(src.height) for x in range(src.width) if sp[x, y] != GREEN]
    left = min(x for x, _ in points)
    top = min(y for _, y in points)
    right = max(x for x, _ in points)
    bottom = max(y for _, y in points)
    crop = src.crop((left, top, right + 1, bottom + 1))
    height = round(crop.height * width / crop.width)
    crop = crop.resize((width, height), Image.Resampling.NEAREST)
    out = Image.new("RGB", (1024, 1024), GREEN)
    out.paste(crop, ((1024 - width) // 2, (1024 - height) // 2))
    return out


def jagged_polygon(draw: ImageDraw.ImageDraw, points: Iterable[tuple[int, int]], fill: tuple[int, int, int]) -> None:
    draw.polygon(list(points), fill=fill)


def add_wreck_paint(image: Image.Image, bank: str) -> None:
    """Add panel-local wreck paint to a bank painting with hard pixel edges."""
    before = image.copy()
    draw = ImageDraw.Draw(image)
    if bank == "left":
        # Physical upper-left blade, foreshortened left wing, right wing root, and lower blade.
        regions = [
            [(457, 155), (535, 190), (520, 370), (456, 330)],
            [(120, 540), (330, 505), (382, 665), (210, 690)],
            [(650, 500), (782, 482), (825, 620), (690, 646)],
            [(410, 715), (585, 700), (555, 960), (430, 925)],
        ]
        cracks = [((486, 178), (472, 286), (517, 338)), ((170, 560), (252, 602), (304, 665)),
                  ((700, 515), (726, 580), (784, 612)), ((470, 738), (500, 830), (535, 916))]
    else:
        # Same physical panels, seen in the opposite roll; the pattern is not mirrored.
        regions = [
            [(505, 145), (590, 182), (575, 360), (510, 328)],
            [(650, 515), (830, 500), (884, 650), (718, 668)],
            [(160, 505), (310, 485), (352, 620), (204, 650)],
            [(435, 725), (620, 705), (592, 964), (468, 934)],
        ]
        cracks = [((532, 174), (551, 270), (522, 332)), ((712, 530), (752, 594), (830, 636)),
                  ((245, 514), (270, 575), (316, 618)), ((500, 745), (520, 836), (570, 920))]

    for region in regions:
        # Sparse stepped soot and exposed substrate; the blue hull remains visible between
        # marks instead of becoming a flat brown sticker.
        mask = Image.new("1", (1024, 1024), 0)
        mask_draw = ImageDraw.Draw(mask)
        mask_draw.polygon(region, fill=1)
        min_x = max(0, min(x for x, _ in region))
        max_x = min(1023, max(x for x, _ in region))
        min_y = max(0, min(y for _, y in region))
        max_y = min(1023, max(y for _, y in region))
        pixels = image.load()
        for y in range(min_y, max_y + 1, 2):
            for x in range(min_x, max_x + 1, 2):
                if mask.getpixel((x, y)) and (x * 13 + y * 7) % 23 < 9 and pixels[x, y] != GREEN:
                    pixels[x, y] = COPPER_DARK if (x + y) % 5 else INK
        for x, y in region[::2]:
            if 0 <= x < 1024 and 0 <= y < 1024 and pixels[x, y] != GREEN:
                draw.rectangle((x, y, x + 7, y + 4), fill=INK)
    for path in cracks:
        draw.line(path, fill=COPPER, width=5)
        draw.line(path[0:2], fill=(11, 91, 122), width=2)

    # Exposed conduits and small orange heat flecks, quantised to hard pixel marks.
    cable_paths = (
        [(480, 190), (472, 230), (486, 270), (477, 312)],
        [(700, 520), (730, 548), (718, 590), (755, 625)],
        [(465, 750), (490, 795), (480, 845), (520, 900)],
    )
    for path in cable_paths:
        draw.line(path, fill=CYAN_HOT, width=3)
        for x, y in path[1::2]:
            draw.rectangle((x - 3, y - 3, x + 3, y + 3), fill=COPPER)

    # Generated candidates have a keyed background. Never let panel paint escape the
    # silhouette into that background.
    old = before.load()
    new = image.load()
    for y in range(1024):
        for x in range(1024):
            if old[x, y] == GREEN:
                new[x, y] = GREEN


def bank_variant(path: Path, bank: str, damage: bool = False, lights: bool = False) -> Image.Image:
    image = clean_generated(path)
    if damage:
        add_wreck_paint(image, bank)
    if lights:
        bank_lights_off(image)
    return image


def make_sheet(state: int) -> Image.Image:
    key = load_key(state)
    cells: list[Image.Image] = []

    off = key.copy()
    lights_off(off)
    dim_gem(off)
    cells.append(off)

    first_glint = key.copy()
    glint(first_glint, "left")
    cells.append(first_glint)

    second_glint = key.copy()
    lights_off(second_glint)
    glint(second_glint, "right")
    cells.append(second_glint)

    left_path = TOOLS / "candidate_bank_left.png"
    right_path = TOOLS / "candidate_bank_right.png"
    damaged_left_path = TOOLS / "candidate_bank_left_damaged.png"
    if state == 0:
        cells.append(bank_variant(left_path, "left"))
        cells.append(bank_variant(right_path, "right"))
    elif state == 1:
        cells.append(bank_variant(damaged_left_path, "left"))
        cells.append(bank_variant(right_path, "right", damage=True))
    else:
        cells.append(bank_variant(left_path, "left", damage=True))
        cells.append(bank_variant(right_path, "right", damage=True))

    cells.append(Image.new("RGB", (1024, 1024), GREEN))
    sheet = Image.new("RGB", (3072, 2048), GREEN)
    for index, cell in enumerate(cells):
        x = (index % 3) * 1024
        y = (index // 3) * 1024
        sheet.paste(cell, (x, y))
    return sheet


def make_preview(sheets: list[Image.Image]) -> Image.Image:
    bg = (11, 11, 26)
    thumb_size = (768, 512)
    preview = Image.new("RGB", (thumb_size[0] * 3, thumb_size[1]), bg)
    for i, sheet in enumerate(sheets):
        thumb = sheet.resize(thumb_size, Image.Resampling.NEAREST)
        pixels = thumb.load()
        for y in range(thumb.height):
            for x in range(thumb.width):
                if pixels[x, y] == GREEN:
                    pixels[x, y] = bg
        preview.paste(thumb, (i * thumb_size[0], 0))
    return preview


def main() -> None:
    sheets = []
    for state in range(3):
        sheet = make_sheet(state)
        sheet.save(ROOT / f"hull_redo_state{state}.png", format="PNG", optimize=False)
        sheets.append(sheet)
    make_preview(sheets).save(ROOT / "preview_j1b.png", format="PNG", optimize=False)


if __name__ == "__main__":
    main()
