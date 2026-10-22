from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)


def is_green(pixel):
    return tuple(pixel[:3]) == GREEN


def load(name):
    path = ROOT / name
    image = Image.open(path).convert("RGB")
    if image.size != (1024, 1024):
        raise ValueError(f"{name}: expected 1024x1024, got {image.size}")
    return image


def bbox(image):
    pixels = image.load()
    xs = []
    ys = []
    for y in range(image.height):
        for x in range(image.width):
            if not is_green(pixels[x, y]):
                xs.append(x)
                ys.append(y)
    if not xs:
        return None
    return min(xs), min(ys), max(xs), max(ys)


def stats(base, other):
    base_pixels = base.load()
    other_pixels = other.load()
    non_green = 0
    silhouette_changes = 0
    colour_changes = 0
    for y in range(1024):
        for x in range(1024):
            a = base_pixels[x, y]
            b = other_pixels[x, y]
            base_solid = not is_green(a)
            other_solid = not is_green(b)
            if base_solid:
                non_green += 1
                if base_solid != other_solid:
                    silhouette_changes += 1
            if a != b:
                colour_changes += 1
    silhouette_pct = 100.0 * silhouette_changes / max(1, non_green)
    colour_pct = 100.0 * colour_changes / (1024 * 1024)
    return silhouette_pct, colour_pct


def main():
    images = {name: load(name) for name in ("key_state0.png", "key_state1.png", "key_state2.png")}
    print("file          size        non-green bbox              silhouette px")
    for name, image in images.items():
        print(f"{name:14} {image.width}x{image.height}   {bbox(image)}")
    print("\ncomparison                         silhouette-diff     colour-diff")
    for name in ("key_state1.png", "key_state2.png"):
        silhouette_pct, colour_pct = stats(images["key_state0.png"], images[name])
        print(f"state0 -> {name:10}                  {silhouette_pct:7.3f}%       {colour_pct:7.3f}%")

    s1, c1 = stats(images["key_state0.png"], images["key_state1.png"])
    s2, c2 = stats(images["key_state0.png"], images["key_state2.png"])
    ok = (
        s1 < 1.5 and s2 < 1.5 and
        3.0 <= c1 <= 12.0 and 8.0 <= c2 <= 25.0 and
        c2 > c1
    )
    print(f"\nPASS: {ok}")
    if not ok:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
