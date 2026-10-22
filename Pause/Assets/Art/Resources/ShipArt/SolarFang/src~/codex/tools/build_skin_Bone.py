from colorsys import rgb_to_hsv
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
TOOLS = Path(__file__).resolve().parent
GREEN = (0, 255, 0)

# Bone ceramic ramp. The generated candidates are used as per-pixel paint cues;
# the approved sheets remain the geometry and non-armour colour source.
BONE_DARK = (145, 137, 121)
BONE_SHADOW = (185, 174, 152)  # #B9AE98
BONE_MID = (214, 204, 186)
BONE_BASE = (244, 234, 212)  # #F4EAD4
BONE_LIGHT = (255, 248, 232)
BONE_HOT = (255, 255, 244)


def hsv(pixel):
    r, g, b = pixel
    return rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)


def luma(pixel):
    r, g, b = pixel
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def is_green(pixel):
    r, g, b = pixel
    return pixel == GREEN or (g > r * 1.05 and g > b * 1.05)


def is_blue_region(pixel):
    r, g, b = pixel
    return not is_green(pixel) and b >= r * 1.08 and b >= g * 0.92 and b > 42


def is_red_light(pixel):
    r, g, b = pixel
    return r > g * 1.45 and r > b * 1.35 and r > 55


def is_warm_armour(pixel):
    h, s, v = hsv(pixel)
    # Excludes the red friendly lights while retaining orange plate ramps and
    # warm damage candidates. Gold is separated by the generated paint cue.
    return 0.035 <= h <= 0.15 and s >= 0.18 and v >= 0.12


def is_bone_candidate(pixel):
    if is_green(pixel):
        return False
    h, s, v = hsv(pixel)
    # Bone candidate paint is warm and comparatively desaturated. This keeps
    # the generated candidate's gold ring, red lights, blue canopy, soot, and
    # orange tears out of the armour transfer.
    return 0.04 <= h <= 0.18 and s <= 0.42 and v >= 0.22


def is_pale_armour_highlight(pixel, cue_pixel):
    # Some approved orange plates use almost-white rim pixels. They have no
    # orange hue left for is_warm_armour, so accept them only when the painted
    # candidate independently identifies the same location as Bone ceramic.
    return (
        not is_green(pixel)
        and not is_blue_region(pixel)
        and not is_red_light(pixel)
        and min(pixel) >= 150
        and max(pixel) - min(pixel) <= 110
        and is_bone_candidate(cue_pixel)
    )


def is_warm_effect_candidate(pixel):
    if is_green(pixel):
        return False
    h, s, v = hsv(pixel)
    return 0.025 <= h <= 0.18 and s > 0.35 and v > 0.15


def bone_from_cue(source_pixel, cue_pixel):
    # The generated repaint supplies the light grouping; the reference luma
    # gently stabilises tiny candidate misalignments before quantisation.
    light = 0.82 * luma(cue_pixel) + 0.18 * luma(source_pixel)
    if light < 92:
        return BONE_DARK
    if light < 142:
        return BONE_SHADOW
    if light < 190:
        return BONE_MID
    if light < 232:
        return BONE_BASE
    if light < 249:
        return BONE_LIGHT
    return BONE_HOT


def repaint_sheet(reference, generated_candidate, state):
    reference = reference.convert("RGB")
    cue = generated_candidate.convert("RGB").resize(reference.size, Image.Resampling.NEAREST)
    out = Image.new("RGB", reference.size, GREEN)
    src = reference.load()
    paint = cue.load()
    dst = out.load()

    for y in range(reference.height):
        for x in range(reference.width):
            p = src[x, y]
            if is_green(p):
                dst[x, y] = GREEN
                continue

            q = paint[x, y]
            if (is_warm_armour(p) or is_pale_armour_highlight(p, q)) and is_bone_candidate(q):
                dst[x, y] = bone_from_cue(p, q)
            elif (
                state == 2
                and (y // 512, x // 512) in {(0, 0), (0, 1), (1, 0), (1, 2), (2, 0), (2, 1)}
                and is_warm_armour(p)
                and is_warm_effect_candidate(q)
            ):
                # The wreck candidate keeps ambiguous flattened damage warm;
                # preserve that orange/gold-family effect rather than turning
                # it into ceramic, while still making the generated repaint
                # visibly different from the approved source raster.
                dst[x, y] = q
            else:
                dst[x, y] = p

    # The approved hit-flash is already a white flash. Re-ramp its light
    # ceramic faces into the warm Bone family while leaving canopy blue,
    # gunmetal shadow, red lights, and the ink edge untouched.
    hit_left, hit_top = 2 * 512, 2 * 512
    for y in range(hit_top, hit_top + 512):
        for x in range(hit_left, hit_left + 512):
            p = src[x, y]
            if is_green(p) or is_blue_region(p) or is_red_light(p):
                continue
            if min(p) >= 120 and max(p) - min(p) <= 105 and luma(p) >= 145:
                dst[x, y] = bone_from_cue(p, paint[x, y])

    # Keep the key green exactly flat and prevent accidental keyed-colour
    # contamination from any generated paint cue.
    for y in range(out.height):
        for x in range(out.width):
            if is_green(src[x, y]):
                out.putpixel((x, y), GREEN)

    return out


def main():
    for state in range(3):
        reference = Image.open(ROOT / f"skinref_state{state}.png")
        candidate = Image.open(TOOLS / f"_bone_candidate_state{state}.png")
        painted = repaint_sheet(reference, candidate, state)
        final = painted.resize((3072, 3072), Image.Resampling.NEAREST)
        final.save(ROOT / f"skin_Bone_state{state}.png")
        print(f"wrote skin_Bone_state{state}.png")


if __name__ == "__main__":
    main()
