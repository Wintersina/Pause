from pathlib import Path
from statistics import median

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
FILES = [ROOT / "key_state0.png", ROOT / "key_state1.png", ROOT / "key_state2.png"]


def read(path):
    image = Image.open(path).convert("RGB")
    if image.size != (1024, 1024):
        raise AssertionError(f"{path.name}: expected 1024x1024, got {image.size}")
    return image


def mask(image):
    px = image.load()
    return [px[x, y] != GREEN for y in range(1024) for x in range(1024)]


def bbox(bits):
    points = [(i % 1024, i // 1024) for i, bit in enumerate(bits) if bit]
    if not points:
        return None
    return (min(x for x, _ in points), min(y for _, y in points),
            max(x for x, _ in points), max(y for _, y in points))


def percent(value, total):
    return 100.0 * value / total


def colour_difference(first, second):
    a = first.load()
    b = second.load()
    return sum(a[x, y] != b[x, y] for y in range(1024) for x in range(1024))


def armour_median(image):
    """Median of red-dominant diffuse armour pixels, excluding green/white lights."""
    pixels = [rgb for rgb in image.getdata()
              if rgb[0] > 70 and rgb[0] > rgb[1] * 1.45
              and rgb[0] > rgb[2] * 1.12 and rgb[1] < 80]
    if not pixels:
        raise AssertionError("no red-dominant armour pixels found")
    return tuple(int(median(channel)) for channel in zip(*pixels))


def main():
    images = [read(path) for path in FILES]
    masks = [mask(image) for image in images]
    print("file          size       non-green bbox                 silhouette pixels")
    for path, bits in zip(FILES, masks):
        print(f"{path.name:<13} 1024x1024  {bbox(bits)!s:<30} {sum(bits)}")

    total_silhouette = sum(masks[0])
    print("\ncomparison    silhouette-mask diff     colour diff (all pixels)")
    failures = []
    for index, limit, minimum in ((1, 1.5, 3.0), (2, 1.5, 8.0)):
        silhouette_diff = sum(a != b for a, b in zip(masks[0], masks[index]))
        colour_diff = colour_difference(images[0], images[index])
        silhouette_pct = percent(silhouette_diff, total_silhouette)
        colour_pct = percent(colour_diff, 1024 * 1024)
        print(f"state0 vs {index}   {silhouette_pct:7.3f}% (must < 1.5%)   {colour_pct:7.3f}% (must > {minimum:.1f}%)")
        if silhouette_pct >= limit:
            failures.append(f"state0/state{index} silhouette diff {silhouette_pct:.3f}%")
        if colour_pct <= minimum:
            failures.append(f"state0/state{index} colour diff {colour_pct:.3f}%")

    armour_rgb = armour_median(images[0])
    print(f"\nstate0 armour median RGB (red-dominant pixels): {armour_rgb}"
          " (must have blue >= 60 and green <= 60)")
    if armour_rgb[2] < 60 or armour_rgb[1] > 60:
        failures.append(f"state0 armour median RGB {armour_rgb} misses blue/green bounds")
    if failures:
        raise SystemExit("FAIL: " + "; ".join(failures))
    print("\nPASS")


if __name__ == "__main__":
    main()
