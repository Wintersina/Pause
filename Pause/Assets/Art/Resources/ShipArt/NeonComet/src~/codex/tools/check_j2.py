#!/usr/bin/env python3
"""Validate J2 flame sheets and render a compact review preview."""

from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SHEETS = (ROOT / "flame_cruise.png", ROOT / "flame_boost.png")
PREVIEW = ROOT / "preview_j2.png"
FRAME_COUNT = 6
HOLD_TICKS = (2, 2, 1, 2, 2, 2)
PALETTE = {
    (0x86, 0x12, 0x1F): "deep red",
    (0xD8, 0x23, 0x2C): "red",
    (0xF2, 0x86, 0x2B): "orange",
    (0xF4, 0xEA, 0xD4): "bone-white",
    (0x6E, 0xF2, 0xEE): "cyan",
    (0x00, 0xFF, 0x00): "key green",
}
PREVIEW_BG = (0x0B, 0x0B, 0x1A)


def inspect(path: Path) -> tuple[Image.Image, bool]:
    image = Image.open(path).convert("RGB")
    width, height = image.size
    equal_cells = width % FRAME_COUNT == 0 and height == (width // FRAME_COUNT) * 4
    flattened = image.get_flattened_data() if hasattr(image, "get_flattened_data") else image.getdata()
    colors = set(flattened)
    allowed = colors <= set(PALETTE)
    cell_w = width // FRAME_COUNT if width % FRAME_COUNT == 0 else 0
    cell_ratio = f"{cell_w}:{height}" if cell_w else "invalid"
    print(
        f"{path.name:18} {width:4}x{height:<4} "
        f"cells={FRAME_COUNT if equal_cells else 'INVALID':>7} "
        f"cell={cell_ratio:>9} colors={len(colors):2} "
        f"palette={'OK' if allowed else 'INVALID'}"
    )
    if not allowed:
        unknown = sorted(colors - set(PALETTE))
        print(f"  unknown colors: {unknown[:12]}")
    return image, equal_cells and allowed


def save_preview(images: list[Image.Image]) -> None:
    scale = 2
    margin = 24
    preview_w = 6 * 256 // scale + margin * 2
    sheet_h = 1024 // scale
    preview_h = sheet_h * len(images) + margin * (len(images) + 1)
    preview = Image.new("RGB", (preview_w, preview_h), PREVIEW_BG)
    for index, image in enumerate(images):
        image = image.resize((image.width // scale, image.height // scale), Image.Resampling.NEAREST)
        x = margin
        y = margin + index * (sheet_h + margin)
        preview.paste(image, (x, y))
    preview.save(PREVIEW, format="PNG", optimize=False)


def main() -> int:
    print("J2 flame sheet audit")
    print(f"hold ticks         {','.join(map(str, HOLD_TICKS))}")
    print("file               size        cells     cell      colors palette")
    images = []
    valid = True
    for path in SHEETS:
        if not path.exists():
            print(f"{path.name:18} MISSING")
            valid = False
            continue
        image, ok = inspect(path)
        images.append(image)
        valid = valid and ok
    if len(images) == len(SHEETS):
        save_preview(images)
        print(f"preview            {PREVIEW.name}")
    return 0 if valid else 1


if __name__ == "__main__":
    raise SystemExit(main())
