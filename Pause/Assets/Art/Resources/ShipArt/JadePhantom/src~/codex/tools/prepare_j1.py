"""Prepare the generated JadePhantom J1 states without smoothing.

This helper is intentionally small and deterministic: it only uses nearest-neighbour
resampling, a chroma-green cleanup, and the intact-state foreground mask to keep the
three exported silhouettes identical.
"""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT
TMP = Path("/Users/sina/.codex/generated_images/01a15ffd-146b-7123-8c9b-000a99325370")
OLD_REFERENCE = ROOT / "ref_old_state0.png"

SOURCE = {
    "key_state0.png": TMP / "exec-4fe0897b-63e8-4a5f-984b-b98fa877d6f6.png",
    "key_state1.png": TMP / "exec-f18b5813-3ebe-4a6e-829e-ee2917119fb4.png",
    "key_state2.png": TMP / "exec-2084f6a7-d570-4707-94c4-b67689f294fc.png",
}

GREEN = (0, 255, 0)
PREVIEW_BG = (11, 11, 26)


def is_background(rgb):
    """Recognise the generator's noisy green backdrop, not jade hull pixels."""
    r, g, b = rgb
    return g > 170 and r < 100 and b < 100 and g > r * 1.16 + 20 and g > b * 1.12 + 20


def clean(path):
    image = Image.open(path).convert("RGB").resize((1024, 1024), Image.Resampling.NEAREST)
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            if is_background(pixels[x, y]):
                pixels[x, y] = GREEN
    return image


def foreground_bbox(image):
    points = [
        (x, y)
        for y in range(image.height)
        for x in range(image.width)
        if image.getpixel((x, y)) != GREEN
    ]
    return (min(x for x, _ in points), min(y for _, y in points),
            max(x for x, _ in points) + 1, max(y for _, y in points) + 1)


def fit_to_box(image, source_box, target_box):
    crop = image.crop(source_box)
    target_size = (target_box[2] - target_box[0], target_box[3] - target_box[1])
    crop = crop.resize(target_size, Image.Resampling.NEAREST)
    fitted = Image.new("RGB", (1024, 1024), GREEN)
    fitted.paste(crop, (target_box[0], target_box[1]))
    return fitted


def main():
    old = clean(OLD_REFERENCE)
    target_box = foreground_bbox(old)
    base_raw = clean(SOURCE["key_state0.png"])
    source_box = foreground_bbox(base_raw)
    base = fit_to_box(base_raw, source_box, target_box)
    base_pixels = base.load()
    mask = [[base_pixels[x, y] != GREEN for x in range(1024)] for y in range(1024)]
    boundary = []
    for y in range(1024):
        for x in range(1024):
            if not mask[y][x]:
                boundary.append(False)
                continue
            boundary.append(any(
                not mask[ny][nx]
                for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1))
                if 0 <= nx < 1024 and 0 <= ny < 1024
            ))

    states = [base]
    for name in ("key_state1.png", "key_state2.png"):
        damaged = fit_to_box(clean(SOURCE[name]), source_box, target_box)
        out = Image.new("RGB", (1024, 1024), GREEN)
        out_pixels = out.load()
        damaged_pixels = damaged.load()
        for y in range(1024):
            for x in range(1024):
                if mask[y][x]:
                    # Generated damage is allowed to repaint the interior, but a
                    # generated green hole falls back to the intact painted pixel.
                    index = y * 1024 + x
                    out_pixels[x, y] = base_pixels[x, y] if boundary[index] else (
                        damaged_pixels[x, y]
                        if damaged_pixels[x, y] != GREEN
                        else base_pixels[x, y]
                    )
        states.append(out)

    for name, image in zip(SOURCE, states):
        image.save(OUT / name, format="PNG", optimize=False)

    gap = 16
    preview = Image.new("RGB", (1024 * 3 + gap * 2, 1024), PREVIEW_BG)
    preview_mask = Image.new("L", (1024, 1024), 0)
    preview_mask.putdata([255 if value else 0 for row in mask for value in row])
    for index, image in enumerate(states):
        # The deliverables retain chroma green; the review board puts the ship
        # cutouts on the requested dark backdrop for easier visual inspection.
        preview.paste(image, (index * (1024 + gap), 0), preview_mask)
    preview.save(OUT / "preview_j1.png", format="PNG", optimize=False)


if __name__ == "__main__":
    main()
