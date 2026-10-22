from pathlib import Path
import colorsys

from PIL import Image
from PIL import ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT
MAGENTA = (255, 0, 255)
PREVIEW_BG = (11, 11, 26)
SIZE = (1024, 1024)


def is_keyed(pixel):
    r, g, b = pixel[:3]
    # Remove the generated chroma-key fringe as well as exact background.
    hue, saturation, _ = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    purple_fringe = saturation > 0.2 and 280.0 / 360.0 <= hue <= 345.0 / 360.0
    return purple_fringe or (r >= 90 and b >= 90 and abs(r - b) <= 80 and g <= 120)


def normalize(path):
    image = Image.open(path).convert("RGB")
    if image.size != SIZE:
        image = image.resize(SIZE, Image.Resampling.NEAREST)
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            if is_keyed(pixels[x, y]):
                pixels[x, y] = MAGENTA
    return image


def write_locked(base, variant, path):
    base_pixels = base.load()
    variant_pixels = variant.load()
    result = Image.new("RGB", SIZE, MAGENTA)
    result_pixels = result.load()
    for y in range(SIZE[1]):
        for x in range(SIZE[0]):
            base_pixel = base_pixels[x, y]
            if base_pixel == MAGENTA:
                continue
            candidate = variant_pixels[x, y]
            result_pixels[x, y] = base_pixel if candidate == MAGENTA else candidate
    result.save(path, format="PNG", optimize=False)


def damage_mask(kind):
    mask = Image.new("L", SIZE, 0)
    draw = ImageDraw.Draw(mask)
    if kind == "moderate":
        # Generated paint is used in these bounded cavities/scorch fields;
        # the intact armor remains pixel-for-pixel from state 0 elsewhere.
        draw.polygon([(300, 235), (492, 250), (520, 465), (365, 520), (285, 420)], fill=255)
        draw.ellipse((610, 630, 835, 825), fill=255)
        draw.polygon([(42, 405), (250, 430), (252, 805), (40, 805)], fill=255)
        draw.polygon([(830, 425), (980, 430), (980, 735), (830, 735)], fill=255)
        draw.line([(225, 180), (320, 820)], fill=255, width=8)
        draw.line([(705, 185), (790, 820)], fill=255, width=8)
    else:
        # Broad bilateral wreck fields, with a separate canopy crack region.
        draw.polygon([(48, 210), (270, 215), (285, 820), (45, 820)], fill=255)
        draw.polygon([(275, 205), (530, 210), (585, 475), (455, 845), (270, 800)], fill=255)
        draw.polygon([(505, 225), (770, 205), (855, 820), (570, 845), (600, 465)], fill=255)
        draw.polygon([(760, 210), (980, 210), (980, 820), (760, 820)], fill=255)
        draw.polygon([(500, 425), (735, 425), (750, 755), (500, 755)], fill=255)
    return mask


def write_damage(base, variant, path, kind):
    base_pixels = base.load()
    variant_pixels = variant.load()
    damage_pixels = damage_mask(kind).load()
    result = Image.new("RGB", SIZE, MAGENTA)
    result_pixels = result.load()
    for y in range(SIZE[1]):
        for x in range(SIZE[0]):
            base_pixel = base_pixels[x, y]
            if base_pixel == MAGENTA:
                continue
            candidate = variant_pixels[x, y]
            if damage_pixels[x, y] and candidate != MAGENTA:
                result_pixels[x, y] = candidate
            else:
                result_pixels[x, y] = base_pixel
    result.save(path, format="PNG", optimize=False)


def make_preview():
    canvas = Image.new("RGB", (1024 * 3 + 32, 1024), PREVIEW_BG)
    for index, name in enumerate(("key_state0.png", "key_state1.png", "key_state2.png")):
        image = normalize(OUT / name)
        image_pixels = image.load()
        for y in range(1024):
            for x in range(1024):
                if image_pixels[x, y] == MAGENTA:
                    image_pixels[x, y] = PREVIEW_BG
        canvas.paste(image, (index * 1024 + index * 16, 0))
    canvas.save(OUT / "preview_j1.png", format="PNG", optimize=False)


def main():
    base = normalize(OUT / "key_state0_gen.png")
    moderate = normalize(OUT / "damage_state1_gen.png")
    wreck = normalize(OUT / "damage_state2_gen.png")
    base.save(OUT / "key_state0.png", format="PNG", optimize=False)
    write_damage(base, moderate, OUT / "key_state1.png", "moderate")
    write_damage(base, wreck, OUT / "key_state2.png", "heavy")
    make_preview()


if __name__ == "__main__":
    main()
