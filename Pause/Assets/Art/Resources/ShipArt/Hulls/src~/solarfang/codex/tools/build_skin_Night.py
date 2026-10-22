from colorsys import rgb_to_hsv
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
TOOLS = Path(__file__).resolve().parent
GREEN = (0, 255, 0)
NIGHT_SHADOW = (26, 31, 69)
NIGHT_BASE = (42, 46, 107)
NIGHT_CYAN = (110, 242, 238)
PALE_BLUE_WHITE = (226, 250, 250)


def hsv(pixel):
    r, g, b = pixel
    return rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)


def is_green(pixel):
    r, g, b = pixel
    return pixel == GREEN or (g > r * 1.05 and g > b * 1.05)


def is_warm_armour(pixel):
    h, s, v = hsv(pixel)
    # Orange/yellow diffuse plating and the reactor's gold ramp. Red lights and
    # orange damage are left alone unless the generated repaint agrees they are armour.
    return 0.035 <= h <= 0.18 and s >= 0.22 and v >= 0.14


def is_night_candidate(pixel):
    if is_green(pixel):
        return False
    h, s, v = hsv(pixel)
    return (0.48 <= h <= 0.70 and s >= 0.18 and v >= 0.12) or (0.42 <= h < 0.48 and s >= 0.28 and v >= 0.35)


def is_blue_preserve(pixel):
    r, g, b = pixel
    return not is_green(pixel) and b >= r * 1.08 and b >= g * 0.92 and b > 42


def is_magenta(pixel):
    h, s, _ = hsv(pixel)
    return s > 0.5 and 285.0 / 360.0 <= h <= 345.0 / 360.0


def luma(pixel):
    r, g, b = pixel
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def night_paint(source, candidate, state):
    """Apply a painted palette decision while preserving the reference raster geometry.

    The generated Night candidate supplies the semantic paint cue (whether an orange
    pixel belongs to repainted armour or to a retained warm damage effect). The final
    pixel geometry, green key, canopy, lights, ink, and damage remain sourced from the
    approved sheet.
    """
    source = source.convert("RGB")
    candidate = candidate.convert("RGB").resize(source.size, Image.Resampling.NEAREST)
    out = Image.new("RGB", source.size, GREEN)
    src = source.load()
    cand = candidate.load()
    dst = out.load()

    for y in range(source.height):
        for x in range(source.width):
            p = src[x, y]
            if is_green(p):
                dst[x, y] = GREEN
                continue

            q = cand[x, y]
            warm_repaint = is_warm_armour(p) and (
                is_night_candidate(q) or (state == 2 and y // 512 == 2 and x // 512 in (0, 1))
            )
            if warm_repaint:
                source_light = luma(p)
                _, _, cand_value = hsv(q)
                if source_light >= 240 and cand_value >= 0.94:
                    dst[x, y] = PALE_BLUE_WHITE
                elif source_light >= 135 or cand_value >= 0.66:
                    dst[x, y] = NIGHT_CYAN
                else:
                    t = max(0.0, min(1.0, (source_light - 32.0) / 150.0))
                    dst[x, y] = tuple(round(a + (b - a) * t) for a, b in zip(NIGHT_SHADOW, NIGHT_BASE))
            elif is_night_candidate(q) and not is_blue_preserve(p):
                # The generated repaint also identifies dark indigo armour shadows
                # that were neutral soot/low-value pixels in the source ramp.
                value = luma(p)
                t = max(0.0, min(1.0, (value - 8.0) / 130.0))
                dst[x, y] = tuple(round(a + (b - a) * t) for a, b in zip(NIGHT_SHADOW, NIGHT_BASE))
            else:
                dst[x, y] = p

    # Remove near-black purple quantisation residue from the source ink ramp.
    # Red player lights are outside this hue range and remain untouched.
    pixels = out.load()
    for y in range(out.height):
        for x in range(out.width):
            if is_magenta(pixels[x, y]):
                pixels[x, y] = (5, 6, 12)
    return out


def night_hit_flash(image):
    """Keep the hit flash bone-white while giving its dark structure the Night ramp."""
    out = image.copy()
    px = out.load()
    left, top = 2 * 512, 2 * 512
    for y in range(top, top + 512):
        for x in range(left, left + 512):
            p = px[x, y]
            if is_green(p):
                px[x, y] = GREEN
                continue
            if is_blue_preserve(p):
                continue
            r, g, b = p
            if max(p) < 210 and max(p) - min(p) < 90:
                value = luma(p)
                t = max(0.0, min(1.0, (value - 10.0) / 175.0))
                px[x, y] = tuple(round(a + (b - a) * t) for a, b in zip(NIGHT_SHADOW, NIGHT_BASE))
            elif 170 <= max(p) < 240 and b >= r - 8:
                px[x, y] = (115, 140, 172)
    return out


def main():
    for state in range(3):
        reference = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
        candidate = Image.open(TOOLS / f"_night_candidate_state{state}.png")
        painted = night_paint(reference, candidate, state)
        painted = night_hit_flash(painted)
        final = painted.resize((3072, 3072), Image.Resampling.NEAREST)
        final.save(ROOT / f"skin_Night_state{state}.png")
        print(f"wrote skin_Night_state{state}.png")


if __name__ == "__main__":
    main()
