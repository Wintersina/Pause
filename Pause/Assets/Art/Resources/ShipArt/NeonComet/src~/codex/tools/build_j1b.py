#!/usr/bin/env python3
"""Build the J1b NeonComet pose sheets from approved keys and painted bank passes."""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
PREVIEW_BG = (0x0B, 0x0B, 0x1A)
SHEET_SIZE = (3072, 2048)
KEY_SIZE = (1024, 1024)


def is_green(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    return g > 40 and g > 1.20 * r and g > 1.20 * b


def ship_mask(image: Image.Image) -> Image.Image:
    rgb = image.convert("RGB")
    mask = Image.new("1", rgb.size, 0)
    src = rgb.load()
    dst = mask.load()
    for y in range(rgb.height):
        for x in range(rgb.width):
            dst[x, y] = not is_green(src[x, y])
    return mask


def key_bbox(image: Image.Image) -> tuple[int, int, int, int]:
    bbox = ship_mask(image).getbbox()
    if bbox is None:
        raise ValueError("key has no ship pixels")
    return bbox


def polygon_mask(size: tuple[int, int], points: list[tuple[int, int]]) -> Image.Image:
    mask = Image.new("1", size, 0)
    ImageDraw.Draw(mask).polygon(points, fill=1)
    return mask


def dim_running_lights(key: Image.Image) -> Image.Image:
    """Paint only the specified running lights and their small glow regions dark."""
    image = key.copy().convert("RGB")
    pixels = image.load()
    regions = [
        polygon_mask(KEY_SIZE, [(202, 620), (282, 592), (322, 745), (270, 760), (232, 740)]),
        polygon_mask(KEY_SIZE, [(742, 592), (822, 620), (792, 760), (730, 742)]),
        polygon_mask(KEY_SIZE, [(48, 724), (113, 722), (119, 803), (48, 804)]),
        polygon_mask(KEY_SIZE, [(910, 722), (977, 724), (976, 804), (905, 803)]),
    ]
    for region in regions:
        region_pixels = region.load()
        for y in range(1024):
            for x in range(1024):
                if not region_pixels[x, y]:
                    continue
                r, g, b = pixels[x, y]
                if max(r, g, b) < 70:
                    continue
                # Navy on the cool edge; dark maroon on the warm panel edge.
                pixels[x, y] = (24, 28, 44) if b >= r else (58, 17, 26)

    # Dim the gold star without touching its ink boundary or surrounding red panels.
    for y in range(493, 596):
        for x in range(438, 586):
            r, g, b = pixels[x, y]
            if r > 105 and g > 60 and b < 105 and r > b * 1.35:
                pixels[x, y] = (93, 57, 13)

    # Slightly lower the cyan canopy glow, preserving the dark glass outline.
    for y in range(230, 472):
        for x in range(442, 585):
            r, g, b = pixels[x, y]
            if b > r + 25 and g > r + 18 and b > 80:
                pixels[x, y] = (max(0, int(r * 0.82)), max(0, int(g * 0.82)), max(0, int(b * 0.82)))
    return image


def canopy_glint(key: Image.Image, strong: bool, lights_off: bool = False) -> Image.Image:
    image = dim_running_lights(key) if lights_off else key.copy().convert("RGB")
    pixels = image.load()
    canopy = polygon_mask(KEY_SIZE, [(480, 245), (546, 245), (582, 286), (580, 420), (548, 470), (486, 470), (450, 420), (451, 285)])
    canopy_pixels = canopy.load()

    # A broad, stepped one-ramp reflection makes the glint readable at sheet scale;
    # the actual hot streak remains 3-4 pixels wide.
    if strong:
        for y in range(250, 465):
            for x in range(450, 555):
                if not canopy_pixels[x, y]:
                    continue
                r, g, b = pixels[x, y]
                if b > r + 20 and g > r + 12:
                    lift = 64 if x <= 520 else 32
                    pixels[x, y] = (min(255, r + lift), min(255, g + lift), min(255, b + lift))
        draw = ImageDraw.Draw(image)
        draw.line([(467, 420), (509, 296)], fill=(245, 253, 253), width=4)
        draw.line([(469, 420), (511, 296)], fill=(255, 251, 224), width=1)
        tip = (509, 296)
        draw.line([(tip[0] - 8, tip[1]), (tip[0] + 8, tip[1])], fill=(255, 251, 224), width=1)
        draw.line([(tip[0], tip[1] - 8), (tip[0], tip[1] + 8)], fill=(255, 251, 224), width=1)
    else:
        for y in range(270, 460):
            for x in range(470, 580):
                if not canopy_pixels[x, y] or x < 515:
                    continue
                r, g, b = pixels[x, y]
                if b > r + 20 and g > r + 12:
                    pixels[x, y] = (min(220, r + 3), min(250, g + 3), min(250, b + 3))
        draw = ImageDraw.Draw(image)
        draw.line([(518, 424), (558, 334)], fill=(160, 235, 242), width=2)
        draw.point((558, 334), fill=(245, 253, 253))
    return image


def normalize_bank(source_path: Path, key: Image.Image) -> Image.Image:
    source = Image.open(source_path).convert("RGB")
    source_mask = ship_mask(source)
    bbox = source_mask.getbbox()
    if bbox is None:
        raise ValueError(f"no painted ship found in {source_path}")
    source = source.crop(bbox)
    source_mask = source_mask.crop(bbox)
    key_box = key_bbox(key)
    target_height = key_box[3] - key_box[1]
    scale = target_height / source.height
    target_width = max(1, round(source.width * scale))
    source = source.resize((target_width, target_height), Image.Resampling.NEAREST)
    source_mask = source_mask.resize((target_width, target_height), Image.Resampling.NEAREST)

    out = Image.new("RGB", KEY_SIZE, GREEN)
    x = round((1024 - target_width) / 2)
    y = key_box[1]
    out.paste(source, (x, y), source_mask)
    return out


def make_sheet(key: Image.Image, left_path: Path, right_path: Path) -> Image.Image:
    cell0 = dim_running_lights(key)
    cell1 = canopy_glint(key, strong=True)
    cell2 = canopy_glint(key, strong=False, lights_off=True)
    cell3 = normalize_bank(left_path, key)
    cell4 = normalize_bank(right_path, key)
    cells = [cell0, cell1, cell2, cell3, cell4, Image.new("RGB", KEY_SIZE, GREEN)]
    sheet = Image.new("RGB", SHEET_SIZE, GREEN)
    for index, cell in enumerate(cells):
        sheet.paste(cell, ((index % 3) * 1024, (index // 3) * 1024))
    return sheet


def make_preview(sheets: list[Image.Image], path: Path) -> None:
    preview = Image.new("RGB", (3072 * len(sheets), 2048), PREVIEW_BG)
    for index, sheet in enumerate(sheets):
        keyed = Image.new("RGB", sheet.size, PREVIEW_BG)
        mask = Image.new("1", sheet.size, 0)
        src = sheet.load()
        dst = mask.load()
        for y in range(sheet.height):
            for x in range(sheet.width):
                dst[x, y] = src[x, y] != GREEN
        keyed.paste(sheet, (0, 0), mask)
        preview.paste(keyed, (index * 3072, 0))
    preview.save(path, format="PNG", optimize=False)


def main() -> None:
    states = []
    for index in range(3):
        key = Image.open(ROOT / f"key_state{index}.png").convert("RGB")
        sheet = make_sheet(
            key,
            ROOT / f"painted_s{index}_left.png",
            ROOT / f"painted_s{index}_right.png",
        )
        out = ROOT / f"hull_redo_state{index}.png"
        sheet.save(out, format="PNG", optimize=False)
        states.append(sheet)
    make_preview(states, ROOT / "preview_j1b.png")


if __name__ == "__main__":
    main()
