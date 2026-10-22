from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
FILES = [ROOT / "key_state0.png", ROOT / "key_state1.png", ROOT / "key_state2.png"]


def load(path):
    image = Image.open(path).convert("RGB")
    if image.size != (1024, 1024):
        raise AssertionError(f"{path.name}: expected 1024x1024, got {image.size}")
    return image


def mask(image):
    return [pixel != GREEN for pixel in image.getdata()]


def bbox(bits):
    points = [(i % 1024, i // 1024) for i, value in enumerate(bits) if value]
    if not points:
        return None
    xs = [point[0] for point in points]
    ys = [point[1] for point in points]
    return min(xs), min(ys), max(xs), max(ys)


def silhouette_diff(base, other):
    changed = sum(a != b for a, b in zip(base, other))
    union = sum(a or b for a, b in zip(base, other))
    return 100.0 * changed / union if union else 0.0


def color_diff(base_image, other_image):
    base = list(base_image.getdata())
    other = list(other_image.getdata())
    return 100.0 * sum(a != b for a, b in zip(base, other)) / len(base)


def main():
    images = [load(path) for path in FILES]
    masks = [mask(image) for image in images]
    print("file          size       non-green bbox              silhouette pixels")
    for path, bits in zip(FILES, masks):
        print(f"{path.name:<13} 1024x1024  {bbox(bits)!s:<28} {sum(bits):>8}")
    print("\ncomparison    silhouette diff     colour diff")
    failures = []
    for index, minimum_colour in ((1, 3.0), (2, 8.0)):
        silhouette = silhouette_diff(masks[0], masks[index])
        colour = color_diff(images[0], images[index])
        print(f"state0 vs {index}    {silhouette:>8.4f}%          {colour:>8.4f}%")
        if silhouette >= 1.5:
            failures.append(f"state0 vs state{index}: silhouette diff must be < 1.5%")
        if colour <= minimum_colour:
            failures.append(f"state0 vs state{index}: colour diff must be > {minimum_colour:g}%")
    if failures:
        raise SystemExit("\nFAIL\n" + "\n".join(failures))
    print("\nPASS")


if __name__ == "__main__":
    main()
