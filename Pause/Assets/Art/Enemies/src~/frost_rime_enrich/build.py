"""Restore the painted rime cutter detail inside its approved seven-cell mask.

Run from any directory with Python, Pillow, and NumPy installed. The two saved
originals in this directory are the only input poses; the painted candidate is
the same drawing used to make the approved, heavily quantized redraw.
"""

from pathlib import Path
import importlib.util
import colorsys

import numpy as np
from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
ART = HERE.parents[2] / "Resources" / "Enemies"
SOURCE = HERE.parent / "frost_fixes" / "rime_candidate_selected.png"
DEATH_BUILD = HERE.parent / "frost_death_rime" / "build.py"
SIDE = 192
ORIGINAL = HERE / "original_frost_rock_rime.png"
ORIGINAL_DEATH = HERE / "original_death_frost_rock_rime.png"


def cell(strip, index):
    return np.array(strip.crop((index * SIDE, 0, (index + 1) * SIDE, SIDE)).convert("RGBA"))


def painted_cell():
    source = Image.open(SOURCE).convert("RGBA")
    alpha = np.array(source.getchannel("A"))
    ys, xs = np.where(alpha > 32)
    cropped = source.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1,
                           int(ys.max()) + 1))
    # These are the exact placement values from frost_fixes/build.py.
    fitted = cropped.resize((138, 176), Image.Resampling.NEAREST)
    result = np.zeros((SIDE, SIDE, 4), dtype=np.uint8)
    result[8:184, 27:165] = np.array(fitted)
    return result


def enrich_key_pose(approved, painting):
    mask = approved[:, :, 3] > 128
    assert np.array_equal(mask, painting[:, :, 3] > 128), "source mask changed"
    old = approved[:, :, :3].astype(float)
    raw = painting[:, :, :3].astype(float)
    # The same painted pixels were used in the approved frame. Bring back their
    # fine plate, ice, frost, and patina values while anchoring hue to that frame.
    color = raw * .84 + old * .16
    copper = (old[:, :, 0] > old[:, :, 1] * 1.15) & \
             (old[:, :, 1] > old[:, :, 2] * 1.06) & \
             (old[:, :, 0] > 40) & mask
    color[:, :, 1][copper] = np.maximum(color[:, :, 1][copper],
                                       color[:, :, 0][copper] * .53)
    color[:, :, 2][copper] = np.minimum(color[:, :, 2][copper],
                                       color[:, :, 1][copper] * .82)
    reddish = (color[:, :, 0] > color[:, :, 1] * 1.2) & \
              (color[:, :, 0] > color[:, :, 2] * 1.12) & ~copper & mask
    color[reddish] = old[reddish]

    # A four-pixel ordered screen breaks the source's soft color transitions
    # into discrete, slightly hue-shifted 12-value material ramps.
    bayer = np.array([[0, 8, 2, 10], [12, 4, 14, 6],
                      [3, 11, 1, 9], [15, 7, 13, 5]], dtype=float)
    yy, xx = np.indices((SIDE, SIDE))
    screen = (bayer[yy % 4, xx % 4] / 16 - .5) * 12
    quantized = np.clip(np.floor((color + screen[:, :, None]) / 12 + .5) * 12,
                        0, 255).astype(np.uint8)
    # Keep the approved dark one-pixel contour and all transparent pixels exact.
    outline = (old.max(axis=2) < 26) & mask
    quantized[outline] = approved[:, :, :3][outline]
    enriched = approved.copy()
    enriched[mask, :3] = quantized[mask]
    enriched[~mask, :3] = 0
    return enriched


def apply_pose(approved_key, enriched_key, pose):
    assert pose.shape == enriched_key.shape
    out = pose.copy()
    key_alpha = approved_key[:, :, 3] > 128
    pose_alpha = pose[:, :, 3] > 128
    common = key_alpha & pose_alpha
    delta = pose[:, :, :3].astype(np.int16) - approved_key[:, :, :3].astype(np.int16)
    textured = np.clip(enriched_key[:, :, :3].astype(np.int16) + delta,
                       0, 255).astype(np.uint8)
    out[common, :3] = textured[common]
    # The bright cyan/white shapes and tiny detached shards define animation
    # timing, so preserve their approved colors and alpha exactly.
    strong = (np.max(np.abs(delta), axis=2) > 60) & common
    out[strong, :3] = pose[strong, :3]
    out[~pose_alpha, :3] = 0
    return clean_hues(out)


def clean_hues(pixels):
    """Keep old red/purple fringe pixels within the copper or blue steel hues."""
    out = pixels.copy()
    mask = out[:, :, 3] > 128
    rgb = out[:, :, :3]
    for color in np.unique(rgb[mask], axis=0):
        r, g, b = map(int, color)
        hue, saturation, value = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        hue *= 360
        replacement = None
        if saturation > .2 and value > .13 and 260 < hue < 345:
            replacement = (min(r, round(g * .8)), g, max(b, round(g * 1.18)))
        elif saturation > .35 and value > .13 and (hue < 18 or hue >= 345):
            if b > g:  # reddish purple fringe becomes blue steel
                replacement = (min(r, round(g * .8)), g, max(b, round(g * 1.18)))
            else:  # warm patina becomes orange copper
                new_g = max(g, b + round((r - b) * .38))
                replacement = (r, new_g, min(b, round(new_g * .82)))
        if replacement is not None:
            same = np.all(rgb == color, axis=2) & mask
            rgb[same] = np.clip(replacement, 0, 255)
    return out


def make_strip(cells):
    image = Image.new("RGBA", (len(cells) * SIDE, SIDE))
    for index, pixels in enumerate(cells):
        image.paste(Image.fromarray(pixels, "RGBA"), (index * SIDE, 0))
    return image


def make_death(new_strip):
    spec = importlib.util.spec_from_file_location("frost_death_rime_build", DEATH_BUILD)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    idle_three = Image.fromarray(cell(new_strip, 3), "RGBA")
    flash = module.flash(idle_three)
    burst = module.rupture()
    vapour = module.aftermath(burst)
    return make_strip([np.array(flash), np.array(burst), np.array(vapour)])


def preview(before, after, death_before, death_after):
    # All sprites are doubled using nearest-neighbour; rows are original/new.
    scale = 2
    pad = 18
    row_h = SIDE * scale + 26
    sheet = Image.new("RGB", (7 * SIDE * scale + 2 * pad,
                              4 * row_h + 2 * pad), "#0e1724")
    draw = ImageDraw.Draw(sheet)
    rows = (("rime before", before, 7), ("rime enriched", after, 7),
            ("death before", death_before, 3), ("death rebuilt", death_after, 3))
    for row, (label, strip, count) in enumerate(rows):
        y = pad + row * row_h
        draw.text((pad, y), label, fill="#bfe6ff")
        for index in range(count):
            sprite = strip.crop((index * SIDE, 0, (index + 1) * SIDE, SIDE))
            sprite = sprite.resize((SIDE * scale, SIDE * scale), Image.Resampling.NEAREST)
            sheet.paste(sprite, (pad + index * SIDE * scale, y + 20), sprite)
    sheet.save(HERE / "preview.png")


def main():
    before = Image.open(ORIGINAL).convert("RGBA")
    death_before = Image.open(ORIGINAL_DEATH).convert("RGBA")
    assert before.size == (7 * SIDE, SIDE)
    assert death_before.size == (3 * SIDE, SIDE)
    original_cells = [cell(before, index) for index in range(7)]
    enriched_key = enrich_key_pose(original_cells[0], painted_cell())
    new_cells = [apply_pose(original_cells[0], enriched_key, pose)
                 for pose in original_cells]
    new_strip = make_strip(new_cells)
    death = make_death(new_strip)
    new_strip.save(ART / "frost_rock_rime.png")
    death.save(ART / "Death" / "frost_rock_rime.png")
    preview(before, new_strip, death_before, death)


if __name__ == "__main__":
    main()
