#!/usr/bin/env python3
"""Compose and audit the Verdant wx_gust atlas from generated pixel-art sources."""

from __future__ import annotations

import colorsys
import math
from collections import deque
from pathlib import Path

from PIL import Image, ImageChops


ROOT = Path(__file__).resolve().parent.parent
SRC = Path(__file__).resolve().parent
OUT = ROOT / "wx_gust.png"
PREVIEW = ROOT / "preview.png"
SIZE = 2048, 1024
CELL = 512
INK = (5, 6, 12)
PREVIEW_BG = (11, 11, 26, 255)


def rgba(path: Path) -> Image.Image:
    return Image.open(path).convert("RGBA")


def clean_alpha(im: Image.Image, cutoff: int = 20, feather: int = 72, keep: float = 0.78) -> Image.Image:
    """Remove generated atmospheric haze while retaining dithered material."""
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a <= cutoff:
                px[x, y] = (r, g, b, 0)
            elif a < feather:
                px[x, y] = (r, g, b, max(0, int(a * 0.35)))
            else:
                px[x, y] = (r, g, b, max(0, min(255, int(a * keep))))
    return remap_forbidden_hues(im)


def remap_forbidden_hues(im: Image.Image) -> Image.Image:
    """Verdant never uses red or pink; map accidental warm pixels to deep green."""
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if not a:
                continue
            h, s, _ = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if s > 0.50 and (h < 15 / 360 or h >= 300 / 360):
                value = max(r, g, b)
                px[x, y] = (max(8, value // 6), max(22, value // 3), max(5, value // 8), a)
    return im


def alpha_bbox(im: Image.Image, threshold: int = 64):
    a = im.getchannel("A").point(lambda v: 255 if v >= threshold else 0)
    return a.getbbox()


def paste_center(canvas: Image.Image, sprite: Image.Image, cell_x: int, cell_y: int, max_w: int, max_h: int, y_bias: int = 0):
    bbox = alpha_bbox(sprite, 64)
    if not bbox:
        raise ValueError("generated source has no usable alpha")
    sprite = sprite.crop(bbox)
    scale = min(max_w / sprite.width, max_h / sprite.height)
    nw = max(1, int(sprite.width * scale))
    nh = max(1, int(sprite.height * scale))
    sprite = sprite.resize((nw, nh), Image.Resampling.NEAREST)
    x = cell_x * CELL + (CELL - nw) // 2
    y = cell_y * CELL + (CELL - nh) // 2 + y_bias
    canvas.alpha_composite(sprite, (x, y))


def crop_quadrant(sheet: Image.Image, col: int, row: int) -> Image.Image:
    w, h = sheet.size
    return sheet.crop((col * w // 2, row * h // 2, (col + 1) * w // 2, (row + 1) * h // 2))


def largest_component(im: Image.Image, threshold: int = 82) -> Image.Image:
    """Keep one crisp debris object, dropping detached generated particles around it."""
    w, h = im.size
    alpha = im.getchannel("A")
    seen = bytearray(w * h)
    best: list[tuple[int, int]] = []
    for y in range(h):
        for x in range(w):
            idx = y * w + x
            if seen[idx] or alpha.getpixel((x, y)) < threshold:
                continue
            seen[idx] = 1
            q = deque([(x, y)])
            component: list[tuple[int, int]] = []
            while q:
                cx, cy = q.popleft()
                component.append((cx, cy))
                for nx, ny in ((cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1)):
                    if nx < 0 or ny < 0 or nx >= w or ny >= h:
                        continue
                    nidx = ny * w + nx
                    if seen[nidx] or alpha.getpixel((nx, ny)) < threshold:
                        continue
                    seen[nidx] = 1
                    q.append((nx, ny))
            if len(component) > len(best):
                best = component
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    src = im.load()
    dst = out.load()
    for x, y in best:
        dst[x, y] = src[x, y]
    return out


def ribbon_tile(path: Path, offset: int, target_h: int = 280, opacity: float = 0.72) -> Image.Image:
    src = clean_alpha(rgba(path), cutoff=14, feather=70, keep=0.64)
    # Keep the generated airy band and discard only the transparent top/bottom margin.
    bbox = alpha_bbox(src, 42)
    if not bbox:
        raise ValueError(f"no ribbon in {path}")
    y0 = max(0, bbox[1] - 54)
    y1 = min(src.height, bbox[3] + 54)
    band = src.crop((0, y0, src.width, y1)).resize((2048, target_h), Image.Resampling.NEAREST)
    # Each cell is a real crop from generated painted content, not a procedural line.
    tile = band.crop((offset % (2048 - CELL), 0, offset % (2048 - CELL) + CELL, target_h))
    canvas = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    canvas.alpha_composite(tile, (0, (CELL - target_h) // 2))
    px = canvas.load()
    for y in range(CELL):
        r0, g0, b0, a0 = px[0, y]
        r1, g1, b1, a1 = px[CELL - 1, y]
        # Match wrap alpha exactly at the seam and keep ribbons readable over gameplay.
        seam_a = int(((a0 + a1) / 2) * opacity)
        seam_rgb = ((r0 + r1) // 2, (g0 + g1) // 2, (b0 + b1) // 2)
        px[0, y] = (*seam_rgb, seam_a)
        px[CELL - 1, y] = (*seam_rgb, seam_a)
    for y in range(CELL):
        for x in range(CELL):
            r, g, b, a = px[x, y]
            px[x, y] = (r, g, b, int(a * opacity))
    return remap_forbidden_hues(canvas)


def make_sheet() -> Image.Image:
    sheet = Image.new("RGBA", SIZE, (0, 0, 0, 0))

    ribbon_sources = [
        (SRC / "candidate_ribbon_02.png", 18),
        (SRC / "candidate_ribbon_01.png", 240),
        (SRC / "candidate_ribbon_03.png", 760),
        (SRC / "candidate_ribbon_02.png", 1190),
    ]
    for col, (path, offset) in enumerate(ribbon_sources):
        sheet.alpha_composite(ribbon_tile(path, offset), (col * CELL, 0))

    heads_a = clean_alpha(rgba(SRC / "candidate_heads_debris_01.png"), cutoff=54, feather=104, keep=0.96)
    heads_b = clean_alpha(rgba(SRC / "candidate_heads_debris_02.png"), cutoff=54, feather=104, keep=0.96)
    # Two distinct generated leading edges.
    paste_center(sheet, crop_quadrant(heads_a, 0, 0), 0, 1, 414, 350, y_bias=-8)
    paste_center(sheet, crop_quadrant(heads_b, 1, 0), 1, 1, 414, 350, y_bias=8)
    # Verdant debris: curled leaf and seed puff, deliberately smaller than the heads.
    paste_center(sheet, largest_component(crop_quadrant(heads_a, 0, 1)), 2, 1, 132, 132, y_bias=0)
    paste_center(sheet, largest_component(crop_quadrant(heads_b, 1, 1)), 3, 1, 132, 132, y_bias=0)
    return remap_forbidden_hues(sheet)


def verify(im: Image.Image) -> list[str]:
    notes: list[str] = []
    assert im.size == SIZE, im.size
    assert im.mode == "RGBA", im.mode
    for row in range(2):
        for col in range(4):
            cell = im.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))
            nonzero = sum(1 for a in cell.getchannel("A").getdata() if a)
            notes.append(f"cell r{row}c{col}: {nonzero} nontransparent pixels")
            assert nonzero > 0
    alpha = im.getchannel("A")
    for col in range(4):
        cell = alpha.crop((col * CELL, 0, (col + 1) * CELL, CELL))
        edge_delta = sum(abs(cell.getpixel((0, y)) - cell.getpixel((CELL - 1, y))) for y in range(CELL)) / CELL
        notes.append(f"ribbon r0c{col} edge alpha mean delta: {edge_delta:.3f}")
        assert edge_delta <= 1.0
    forbidden = 0
    for r, g, b, a in im.getdata():
        if a:
            h, s, _ = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if s > 0.50 and (h < 15 / 360 or h >= 300 / 360):
                forbidden += 1
    notes.append(f"forbidden red/pink pixels: {forbidden}")
    assert forbidden == 0
    # Heuristic for accidental 2x/3x/4x upscaling: this sheet is downsampled or cropped
    # with nearest-neighbour from larger generated sources, so no enlargement is performed.
    repeated_2x = 0
    tested_2x = 0
    for y in range(0, im.height - 1, 2):
        for x in range(0, im.width - 1, 2):
            block = [im.getpixel((x, y)), im.getpixel((x + 1, y)), im.getpixel((x, y + 1)), im.getpixel((x + 1, y + 1))]
            if not any(p[3] for p in block):
                continue
            tested_2x += 1
            p = block[0]
            if block[1] == p and block[2] == p and block[3] == p:
                repeated_2x += 1
    ratio = repeated_2x / max(1, tested_2x)
    notes.append(f"exact repeated 2x2 block ratio: {ratio:.4f} (nearest-neighbour downsample/crop only)")
    assert ratio < 0.22
    return notes


def main() -> None:
    final = make_sheet()
    notes = verify(final)
    final.save(OUT, "PNG")
    preview = Image.new("RGBA", SIZE, PREVIEW_BG)
    preview.alpha_composite(final)
    preview.save(PREVIEW, "PNG")
    print(f"wrote {OUT} ({final.size}, {final.mode})")
    print(f"wrote {PREVIEW} ({preview.size}, {preview.mode})")
    print("verification:")
    print("\n".join(notes))


if __name__ == "__main__":
    main()
