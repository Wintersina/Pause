"""Validate the JadePhantom J1 deliverables and print a compact table."""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
FILES = [ROOT / "key_state0.png", ROOT / "key_state1.png", ROOT / "key_state2.png"]


def load(path):
    image = Image.open(path).convert("RGB")
    if image.size != (1024, 1024):
        raise ValueError(f"{path.name}: expected 1024x1024, got {image.size}")
    return image


def mask(image):
    return [pixel != GREEN for pixel in image.getdata()]


def bbox(bits):
    xs = []
    ys = []
    for index, bit in enumerate(bits):
        if bit:
            xs.append(index % 1024)
            ys.append(index // 1024)
    return (min(xs), min(ys), max(xs) + 1, max(ys) + 1) if xs else None


def mask_difference(a, b):
    changed = sum(left != right for left, right in zip(a, b))
    denominator = sum(a)
    return 100.0 * changed / denominator if denominator else 0.0


def colour_difference(a, b):
    changed = sum(left != right for left, right in zip(a.getdata(), b.getdata()))
    return 100.0 * changed / (1024 * 1024)


def main():
    images = [load(path) for path in FILES]
    bits = [mask(image) for image in images]
    print("file          size       non-green bbox             silhouette%  colour-diff%")
    for index, (path, image, image_mask) in enumerate(zip(FILES, images, bits)):
        silhouette = "-" if index == 0 else f"{mask_difference(bits[0], image_mask):.3f}"
        colour = "-" if index == 0 else f"{colour_difference(images[0], image):.3f}"
        print(f"{path.name:<14} 1024x1024  {str(bbox(image_mask)):<25} {silhouette:>10} {colour:>13}")

    if bits[0] != bits[1] or bits[0] != bits[2]:
        raise SystemExit("FAIL: the three non-green silhouettes are not pixel-identical")
    state1_colour = colour_difference(images[0], images[1])
    state2_colour = colour_difference(images[0], images[2])
    if state1_colour <= 3.0 or state2_colour <= 8.0:
        raise SystemExit(
            f"FAIL: colour differences too small (state1={state1_colour:.3f}%, state2={state2_colour:.3f}%)"
        )
    print("PASS: dimensions, identical silhouette masks, and damage thresholds")


if __name__ == "__main__":
    main()
