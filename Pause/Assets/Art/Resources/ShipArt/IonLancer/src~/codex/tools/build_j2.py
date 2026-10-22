from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GENERATED = Path(
    "/Users/sina/.codex/generated_images/01a16037-a23e-7cb1-8cad-d956970b9f6f/"
    "exec-3b94a2c0-5de7-48a6-af63-142db8802066.png"
)

GREEN = (0, 255, 0)
PALETTE = [
    (0x1C, 0x2A, 0x80),
    (0x3A, 0x5B, 0xE0),
    (0x6E, 0xF2, 0xEE),
    (0xF4, 0xEA, 0xD4),
    (0x9A, 0xB0, 0xFF),
]
CELL = (256, 1024)
NATIVE = (128, 512)


def generated_mask(image: Image.Image) -> Image.Image:
    """Keep paint, rejecting the generator's green-screen background."""
    rgb = image.convert("RGB")
    mask = Image.new("1", rgb.size, 0)
    dst = mask.load()
    src = rgb.load()
    for y in range(rgb.height):
        for x in range(rgb.width):
            r, g, b = src[x, y]
            dst[x, y] = not (g > 120 and g > r * 1.20 and g > b * 1.12)
    return mask


def bbox(mask: Image.Image) -> tuple[int, int, int, int]:
    return mask.getbbox() or (0, 0, 1, 1)


def nearest_colour(rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    return min(PALETTE, key=lambda c: sum((a - b) ** 2 for a, b in zip(rgb, c)))


def snap_to_palette(image: Image.Image) -> Image.Image:
    src = image.convert("RGB")
    out = Image.new("RGB", src.size, GREEN)
    sp = src.load()
    dp = out.load()
    for y in range(src.height):
        for x in range(src.width):
            value = sp[x, y]
            if value != GREEN:
                dp[x, y] = nearest_colour(value)
    return out


def source_frames() -> list[Image.Image]:
    source = Image.open(GENERATED).convert("RGB")
    mask = generated_mask(source)
    frames: list[Image.Image] = []
    for index in range(6):
        left = round(index * source.width / 6)
        right = round((index + 1) * source.width / 6)
        frame = source.crop((left, 0, right, source.height))
        frame_mask = mask.crop((left, 0, right, source.height))
        box = bbox(frame_mask)
        frames.append(frame.crop(box))
    return frames


def fit_frame(source: Image.Image, tail_fraction: float, width_scale: float = 1.0) -> Image.Image:
    """Fit generated paint into a native pixel canvas, then enlarge with nearest-neighbour."""
    source = source.convert("RGB")
    native_w, native_h = NATIVE
    target_h = max(8, round(tail_fraction * native_h) - 15)
    target_w = max(5, round(source.width * target_h / max(1, source.height) * width_scale))
    # The generator's source is already stepped; this reduction creates a crisp game-pixel
    # grid before the final 2x nearest-neighbour export.
    scaled = source.resize((target_w, target_h), Image.Resampling.NEAREST)
    scaled = snap_to_palette(scaled)
    canvas = Image.new("RGB", (native_w, native_h), GREEN)
    x = (native_w - target_w) // 2
    y = 15
    canvas.paste(scaled, (x, y))
    return canvas.resize(CELL, Image.Resampling.NEAREST)


def widen_boost_core(image: Image.Image) -> Image.Image:
    """Make the existing generated white-hot core read hotter without adding colours."""
    pix = image.load()
    bone = (0xF4, 0xEA, 0xD4)
    # Only expand bone pixels in the central 42% of the cell; side sparks stay untouched.
    left, right = image.width * 29 // 100, image.width * 71 // 100
    updates: list[tuple[int, int]] = []
    for y in range(20, image.height - 10):
        for x in range(left, right):
            if pix[x, y] == bone:
                if x > left and pix[x - 1, y] in ((0x6E, 0xF2, 0xEE), (0x3A, 0x5B, 0xE0)):
                    updates.append((x - 1, y))
                if x + 1 < right and pix[x + 1, y] in ((0x6E, 0xF2, 0xEE), (0x3A, 0x5B, 0xE0)):
                    updates.append((x + 1, y))
    for x, y in updates:
        pix[x, y] = bone
    return image


def make_sheet(frames: list[Image.Image], boost: bool) -> Image.Image:
    # Cruise ends at roughly 80% of the cell. Boost ends at roughly 97%.
    normal = [0.80, 0.84, 0.92, 0.78, 0.83, 0.81]
    boost_tail = [0.96, 0.98, 0.99, 0.95, 0.97, 0.96]
    tails = boost_tail if boost else normal
    sheet = Image.new("RGB", (CELL[0] * 6, CELL[1]), GREEN)
    for index, source in enumerate(frames):
        width_scale = 1.15 if boost else 1.0
        # Frame 2 is the one-tick near-solid speed smear: longer and narrower.
        if index == 2:
            width_scale = 0.78 if boost else 0.82
        frame = fit_frame(source, tails[index], width_scale)
        if boost:
            frame = widen_boost_core(frame)
        sheet.paste(frame, (index * CELL[0], 0))
    return sheet


def main() -> None:
    frames = source_frames()
    make_sheet(frames, boost=False).save(ROOT / "flame_cruise.png")
    make_sheet(frames, boost=True).save(ROOT / "flame_boost.png")


if __name__ == "__main__":
    main()
