"""Build the J2 Turtle flame sheets from generated source paintings.

This helper deliberately keeps the generated source outside the project. It
only writes the requested J2 PNGs and preview into the codex output folder.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


OUT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
PALETTE = (
    (31, 181, 185),   # teal
    (110, 242, 238),  # light cyan
    (244, 234, 212),  # warm bone-white
    (90, 58, 24),     # dark umber
    (226, 190, 132),  # sand
)
KEY = Image.new("RGB", (256, 1024), GREEN)


def fit_source(path: Path, target_height: int, horizontal_scale: float = 1.0) -> Image.Image:
    """Fit a generated RGBA portrait into one 256x1024 frame with nearest pixels."""
    source = Image.open(path).convert("RGBA")
    source = source.resize((256, target_height), Image.Resampling.NEAREST)
    if horizontal_scale != 1.0:
        scaled_width = max(1, round(256 * horizontal_scale))
        source = source.resize((scaled_width, target_height), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (256, 1024), (0, 0, 0, 0))
    x = (256 - source.width) // 2
    y = 2 if source.height < 1024 else 0
    visible = source.crop((0, 0, source.width, min(source.height, 1024 - y)))
    canvas.alpha_composite(visible, (x, y))
    return canvas


def shift(image: Image.Image, dx: int, dy: int) -> Image.Image:
    moved = Image.new("RGBA", image.size, (0, 0, 0, 0))
    moved.paste(image, (dx, dy), image)
    return moved


def snap(image: Image.Image) -> Image.Image:
    """Remove alpha/background and map every visible pixel to the five colors."""
    rgba = image.load()
    result = Image.new("RGB", image.size, GREEN)
    dst = result.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = rgba[x, y]
            if a < 32:
                continue
            colour = min(PALETTE, key=lambda c: (c[0] - r) ** 2 + (c[1] - g) ** 2 + (c[2] - b) ** 2)
            dst[x, y] = colour
    return result


def normal_frames(source: Path, target_height: int, shifts: tuple[int, ...], scales: tuple[float, ...]) -> list[Image.Image]:
    frames: list[Image.Image] = []
    for dx, scale in zip(shifts, scales):
        frame = fit_source(source, round(target_height * scale), 1.0 + (dx % 3 - 1) * 0.01)
        frames.append(snap(shift(frame, dx, 0)))
    return frames


def smear_frame(source: Path, target_height: int) -> Image.Image:
    """Create frame 2: longer, narrower, and dominated by a white-hot streak."""
    frame = snap(fit_source(source, target_height, 0.72))
    pixels = frame.load()
    for y in range(frame.height):
        for x in range(frame.width):
            if pixels[x, y] == GREEN and not (104 <= x <= 151):
                continue
            if pixels[x, y] != GREEN and 103 <= x <= 152 and pixels[x, y] != (226, 190, 132):
                pixels[x, y] = (244, 234, 212)
    return frame


def make_sheet(source: Path, boost: bool) -> Image.Image:
    if boost:
        normal = normal_frames(source, 1000, (-3, 2, 0, -2, 3, 1), (0.97, 1.00, 1.02, 0.98, 1.01, 1.00))
        frames = [normal[0], normal[1], smear_frame(source, 1170), normal[2], normal[3], normal[4], normal[5]]
    else:
        normal = normal_frames(source, 940, (-3, 2, 0, -2, 3, 1), (0.97, 1.02, 1.04, 0.98, 1.03, 1.00))
        frames = [normal[0], normal[1], smear_frame(source, 1080), normal[2], normal[3], normal[4], normal[5]]
    sheet = Image.new("RGB", (1792, 1024), GREEN)
    for index, frame in enumerate(frames):
        sheet.paste(frame, (index * 256, 0))
    return sheet


def make_preview(cruise: Image.Image, boost: Image.Image) -> Image.Image:
    width = 1792
    height = 2048 + 48
    preview = Image.new("RGB", (width, height), (11, 11, 26))
    for y_offset, sheet in ((16, cruise), (1040, boost)):
        keyed = Image.new("RGB", sheet.size, (11, 11, 26))
        source = sheet.load()
        target = keyed.load()
        for y in range(sheet.height):
            for x in range(sheet.width):
                if source[x, y] != GREEN:
                    target[x, y] = source[x, y]
        preview.paste(keyed, (0, y_offset))
    return preview


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("cruise_source", type=Path)
    parser.add_argument("boost_source", type=Path)
    args = parser.parse_args()

    cruise = make_sheet(args.cruise_source, boost=False)
    boost = make_sheet(args.boost_source, boost=True)
    cruise.save(OUT / "flame_cruise.png")
    boost.save(OUT / "flame_boost.png")
    make_preview(cruise, boost).save(OUT / "preview_j2.png")


if __name__ == "__main__":
    main()
