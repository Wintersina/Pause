#!/usr/bin/env python3
"""Compose and audit the Frost wx_gust sheet from generated RGBA source art."""

from __future__ import annotations

import argparse
import math
from pathlib import Path

from PIL import Image, ImageChops, ImageStat


CELL = 512
SHEET = (2048, 1024)
PALETTE = {
    "ice": (240, 250, 255),
    "blue": (159, 216, 255),
    "shade": (5, 12, 28),
    "cyan": (64, 224, 255),
    "red": (255, 0, 0),
    "pink": (255, 20, 147),
}


def alpha_bbox(image: Image.Image, extra: int = 0):
    bbox = image.getchannel("A").getbbox()
    if bbox is None:
        raise ValueError("generated source has no visible pixels")
    x0, y0, x1, y1 = bbox
    return (
        max(0, x0 - extra),
        max(0, y0 - extra),
        min(image.width, x1 + extra),
        min(image.height, y1 + extra),
    )


def crop_alpha(image: Image.Image, box=None, extra: int = 0) -> Image.Image:
    image = image.convert("RGBA")
    if box is None:
        box = alpha_bbox(image, extra)
    else:
        part = image.crop(box)
        box = alpha_bbox(part, extra)
        image = part
    return image.crop(box)


def resize_nearest(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    return image.resize(size, Image.Resampling.NEAREST)


def opacity(image: Image.Image, factor: float) -> Image.Image:
    image = image.copy().convert("RGBA")
    alpha = image.getchannel("A").point(lambda p: max(0, min(255, round(p * factor))))
    image.putalpha(alpha)
    return image


def alpha_lift(image: Image.Image, gamma: float = 0.52, factor: float = 0.92) -> Image.Image:
    """Make generated airy paint readable without flattening zero-alpha pixels."""
    image = image.copy().convert("RGBA")
    alpha = image.getchannel("A")
    alpha = alpha.point(lambda p: 0 if p == 0 else min(255, round(255 * (p / 255.0) ** gamma * factor)))
    image.putalpha(alpha)
    return image


def frostify(image: Image.Image) -> Image.Image:
    """Remove tiny warm model artifacts while preserving generated pixel texture."""
    image = image.copy().convert("RGBA")
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = pixels[x, y]
            if a and r > g * 1.04 and r > b * 1.04:
                lum = round(0.45 * r + 0.35 * g + 0.20 * b)
                pixels[x, y] = (min(255, lum + 14), min(255, lum + 24), min(255, lum + 38), a)
    return image


def paste_center(dst: Image.Image, item: Image.Image, cell_x: int, cell_y: int):
    x = cell_x * CELL + (CELL - item.width) // 2
    y = cell_y * CELL + (CELL - item.height) // 2
    dst.alpha_composite(item, (x, y))


def force_wrap_edges(tile: Image.Image, edge: int = 4) -> Image.Image:
    """Make the first/last columns agree without softening the painted body."""
    tile = tile.copy().convert("RGBA")
    px = tile.load()
    for y in range(tile.height):
        for x in range(edge):
            left = px[x, y]
            right = px[tile.width - edge + x, y]
            seam = tuple(round((left[c] + right[c]) / 2) for c in range(4))
            px[x, y] = seam
            px[tile.width - edge + x, y] = seam
    return tile


def prepare_ribbon(source: Path, index: int) -> Image.Image:
    image = frostify(Image.open(source))
    # The model gave us wide painted ribbons. A fixed 512x220 nearest-neighbour
    # reduction keeps the painterly pixel clusters without a block upscale.
    art = crop_alpha(image)
    art = resize_nearest(art, (CELL, 220))
    # Keep the average opacity of the painted ribbon in the requested airy
    # band; solid cores stay bright while the dithered wake remains readable.
    art = opacity(art, (0.64, 0.64, 0.72, 0.64)[index])
    art = force_wrap_edges(art)
    tile = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    tile.alpha_composite(art, (0, (CELL - art.height) // 2))
    return tile


def prepare_head(source: Image.Image, side: int) -> Image.Image:
    source = frostify(source)
    half = source.crop((side * source.width // 2, 0, (side + 1) * source.width // 2, source.height))
    art = crop_alpha(half)
    scale = min(430 / art.width, 390 / art.height)
    art = resize_nearest(art, (max(1, round(art.width * scale)), max(1, round(art.height * scale))))
    art = opacity(art, 0.78)
    tile = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    paste_center(tile, art, 0, 0)
    return tile


def prepare_debris(source: Image.Image, slot: int) -> Image.Image:
    source = frostify(source)
    # Four generated pieces are spaced across the source sheet. Use a snowflake
    # and an ice shard, then keep them small enough to read as carried debris.
    quarter = source.crop((slot * source.width // 4, 0, (slot + 1) * source.width // 4, source.height))
    art = crop_alpha(quarter)
    scale = min(120 / art.width, 120 / art.height)
    art = resize_nearest(art, (max(1, round(art.width * scale)), max(1, round(art.height * scale))))
    tile = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    paste_center(tile, art, 0, 0)
    return tile


def mean_alpha(image: Image.Image, box=None) -> float:
    alpha = image.getchannel("A")
    if box is not None:
        alpha = alpha.crop(box)
    return ImageStat.Stat(alpha).mean[0] / 255.0


def max_channel_distance_from_palette(image: Image.Image) -> tuple[int, tuple[int, int, int]]:
    # The generated art is intentionally painterly, so audit for forbidden hues
    # rather than requiring every pixel to be one of the named swatches.
    rgb = image.convert("RGB")
    max_red = 0
    offender = (0, 0, 0)
    for r, g, b in rgb.getdata():
        if r > max_red and r > g * 1.04 and r > b * 1.04:
            max_red, offender = r, (r, g, b)
    return max_red, offender


def upscale_reproduction_error(image: Image.Image, factor: int) -> float:
    # Ignore the transparent canvas; a whole-image 2x/3x/4x upscale would have
    # near-zero error on visible pixels after nearest-neighbour reduction.
    if image.width % factor or image.height % factor:
        return 1.0
    small = image.resize((image.width // factor, image.height // factor), Image.Resampling.NEAREST)
    back = small.resize(image.size, Image.Resampling.NEAREST)
    a = image.convert("RGBA")
    b = back.convert("RGBA")
    visible = a.getchannel("A").point(lambda p: 255 if p > 8 else 0)
    diff = ImageChops.difference(a, b)
    diff = ImageChops.multiply(diff, visible.convert("RGBA"))
    stat = ImageStat.Stat(diff)
    return sum(stat.mean) / (255 * 4)


def verify(sheet: Image.Image) -> list[str]:
    errors: list[str] = []
    if sheet.size != SHEET:
        errors.append(f"size {sheet.size} != {SHEET}")
    for row in range(2):
        for col in range(4):
            cell = sheet.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))
            if cell.getchannel("A").getbbox() is None:
                errors.append(f"empty cell row={row} col={col}")
    for col in range(4):
        cell = sheet.crop((col * CELL, 0, (col + 1) * CELL, CELL))
        left = ImageStat.Stat(cell.getchannel("A").crop((0, 0, 4, CELL))).mean[0]
        right = ImageStat.Stat(cell.getchannel("A").crop((CELL - 4, 0, CELL, CELL))).mean[0]
        if abs(left - right) > 3.0:
            errors.append(f"row0 tile seam alpha delta col={col}: {abs(left-right):.2f}")
    red, rgb = max_channel_distance_from_palette(sheet)
    if red:
        errors.append(f"red pixel candidate {rgb}")
    for factor in (2, 3, 4):
        if upscale_reproduction_error(sheet, factor) < 0.002:
            errors.append(f"looks like an exact {factor}x block upscale")
    return errors


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args()
    root = args.root
    src = root / "src~"
    out = root / "wx_gust.png"
    preview = root / "preview.png"

    sheet = Image.new("RGBA", SHEET, (0, 0, 0, 0))
    ribbons = sorted(src.glob("candidate_ribbon_*.png"))
    if len(ribbons) != 4:
        raise SystemExit(f"expected 4 ribbon candidates, found {len(ribbons)}")
    for col, source in enumerate(ribbons):
        sheet.alpha_composite(prepare_ribbon(source, col), (col * CELL, 0))

    heads = Image.open(src / "candidate_heads.png").convert("RGBA")
    sheet.alpha_composite(prepare_head(heads, 0), (0, CELL))
    sheet.alpha_composite(prepare_head(heads, 1), (CELL, CELL))

    debris = Image.open(src / "candidate_debris.png").convert("RGBA")
    sheet.alpha_composite(prepare_debris(debris, 0), (2 * CELL, CELL))
    sheet.alpha_composite(prepare_debris(debris, 1), (3 * CELL, CELL))

    problems = verify(sheet)
    if problems:
        raise SystemExit("verification failed:\n- " + "\n- ".join(problems))
    sheet.save(out, "PNG")
    dark = Image.new("RGBA", SHEET, (11, 11, 26, 255))
    dark.alpha_composite(sheet)
    dark.save(preview, "PNG")

    cell_means = []
    ribbon_visible_means = []
    for row in range(2):
        for col in range(4):
            cell = sheet.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))
            cell_means.append(mean_alpha(cell))
            if row == 0:
                alpha_values = list(cell.getchannel("A").getdata())
                visible = [value for value in alpha_values if value > 0]
                ribbon_visible_means.append(sum(visible) / max(1, len(visible)) / 255.0)
    print(f"wrote {out} ({sheet.width}x{sheet.height} RGBA)")
    print(f"wrote {preview} on #0b0b1a")
    print("cell mean alpha: " + ", ".join(f"{x:.3f}" for x in cell_means))
    print("row0 visible-ribbon mean alpha: " + ", ".join(f"{x:.3f}" for x in ribbon_visible_means))
    print("row0 edge alpha deltas: " + ", ".join(
        f"{abs(ImageStat.Stat(sheet.crop((c*CELL,0,c*CELL+4,CELL)).getchannel('A')).mean[0] - ImageStat.Stat(sheet.crop(((c+1)*CELL-4,0,(c+1)*CELL,CELL)).getchannel('A')).mean[0]):.2f}"
        for c in range(4)
    ))
    print("block-upscale reproduction errors: " + ", ".join(
        f"{factor}x={upscale_reproduction_error(sheet, factor):.4f}" for factor in (2, 3, 4)
    ))
    print("forbidden red pixels: none")


if __name__ == "__main__":
    main()
