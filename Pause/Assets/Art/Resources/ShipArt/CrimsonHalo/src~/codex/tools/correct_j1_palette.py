from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "tools"
GREEN = (0, 255, 0)


def is_armour_pixel(rgb):
    r, g, b = rgb
    return r > 70 and r > g * 1.45 and r > b * 1.12 and g < 80


def correct(path):
    image = Image.open(path).convert("RGB")
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            rgb = pixels[x, y]
            if is_armour_pixel(rgb):
                r, g, b = rgb
                # Keep the generated ramps and hard pixel edges, but push the
                # diffuse red ramp into the requested blue-biased raspberry.
                pixels[x, y] = (r, g, min(132, max(60, b + 10)))
    image.save(path)


for name in ("candidate_intact.png", "candidate_damaged.png", "candidate_wreck.png"):
    correct(TOOLS / name)
