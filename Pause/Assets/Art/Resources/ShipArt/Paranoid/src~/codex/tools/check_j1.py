from pathlib import Path
import colorsys
import warnings

from PIL import Image

warnings.filterwarnings("ignore", category=DeprecationWarning)


ROOT = Path(__file__).resolve().parents[1]
MAGENTA = (255, 0, 255)
SIZE = (1024, 1024)


def is_background(pixel):
    return pixel == MAGENTA


def load(name):
    image = Image.open(ROOT / name).convert("RGB")
    if image.size != SIZE:
        raise AssertionError(f"{name}: expected 1024x1024, got {image.size}")
    return image


def mask(image):
    return [pixel != MAGENTA for pixel in image.getdata()]


def bbox(image):
    points = []
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            if not is_background(pixels[x, y]):
                points.append((x, y))
    if not points:
        return None
    return min(x for x, _ in points), min(y for _, y in points), max(x for x, _ in points), max(y for _, y in points)


def percent(value, total):
    return 100.0 * value / total if total else 0.0


def magenta_like_pixels(image):
    count = 0
    for r, g, b in image.getdata():
        if (r, g, b) == MAGENTA:
            continue
        hue, saturation, _ = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
        if saturation > 0.5 and 280.0 / 360.0 <= hue <= 345.0 / 360.0:
            count += 1
    return count


def changed_pixels(base, other, allowed):
    base_pixels = list(base.getdata())
    other_pixels = list(other.getdata())
    return sum(1 for a, b, use in zip(base_pixels, other_pixels, allowed) if use and a != b)


def mask_difference(base, other):
    base_mask = mask(base)
    other_mask = mask(other)
    return sum(a != b for a, b in zip(base_mask, other_mask))


def main():
    state0 = load("key_state0.png")
    state1 = load("key_state1.png")
    state2 = load("key_state2.png")
    images = (state0, state1, state2)
    silhouette = mask(state0)
    silhouette_size = sum(silhouette)

    print("file          size       non-magenta bbox")
    for name, image in zip(("key_state0.png", "key_state1.png", "key_state2.png"), images):
        print(f"{name:14} {image.size[0]}x{image.size[1]}    {bbox(image)}")
    print("\nmagenta-like ship pixels       count")
    for name, image in zip(("key_state0.png", "key_state1.png", "key_state2.png"), images):
        print(f"{name:28} {magenta_like_pixels(image)}")

    print("\ncomparison                     changed %")
    for name, image in (("state1 vs state0", state1), ("state2 vs state0", state2)):
        silhouette_diff = mask_difference(state0, image)
        colour_diff = changed_pixels(state0, image, silhouette)
        print(f"{name:28} silhouette {percent(silhouette_diff, silhouette_size):6.3f}%   colour {percent(colour_diff, silhouette_size):6.3f}%")

    print("\nstate2 damage spread by hull part")
    state2_pixels = list(state2.getdata())
    base_pixels = list(state0.getdata())
    parts = {
        "left pod": lambda x, y: x < 260,
        "disc": lambda x, y: 260 <= x <= 763,
        "right pod": lambda x, y: x > 763,
    }
    for name, selector in parts.items():
        region = []
        changed = 0
        for y in range(1024):
            for x in range(1024):
                index = y * 1024 + x
                if silhouette[index] and selector(x, y):
                    region.append(index)
                    if base_pixels[index] != state2_pixels[index]:
                        changed += 1
        print(f"{name:10} {percent(changed, len(region)):6.3f}% changed ({changed}/{len(region)})")

    assert all(image.size == SIZE for image in images)
    assert all(bbox(image) is not None for image in images)
    assert all(magenta_like_pixels(image) == 0 for image in images)
    assert percent(mask_difference(state0, state1), silhouette_size) < 1.5
    assert percent(mask_difference(state0, state2), silhouette_size) < 1.5
    assert percent(changed_pixels(state0, state1, silhouette), silhouette_size) > 5.0
    assert percent(changed_pixels(state0, state2, silhouette), silhouette_size) > 15.0
    for selector in parts.values():
        region = [y * 1024 + x for y in range(1024) for x in range(1024) if silhouette[y * 1024 + x] and selector(x, y)]
        changed = sum(1 for index in region if base_pixels[index] != state2_pixels[index])
        assert percent(changed, len(region)) > 8.0
    print("\nPASS")


if __name__ == "__main__":
    main()
