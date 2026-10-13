"""Build Spectre sheets from image-generated silver paintings and approved poses.

The paintings provide the new material texture. The reference sheets supply the
precise sprite geometry, blue components, ink, damage, and chroma background.
Every resize uses nearest-neighbour sampling; there is no blur or smoothing.
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
SILVER_RAMP = np.array(
    [
        [77, 99, 118],    # deep blue-grey shadow
        [111, 141, 163],  # shadow
        [143, 176, 200],  # #8FB0C8
        [183, 206, 223],  # soft pearlescent plane
        [220, 238, 246],  # #DCEEF6
        [255, 255, 255],  # #FFFFFF
    ],
    dtype=np.uint8,
)


def green_mask(rgb):
    r, g, b = rgb.astype(np.int16).transpose(2, 0, 1)
    return (g > r * 1.7) & (g > b * 1.7) & (g > 120)


def warm_mask(rgb):
    r, g, b = rgb.astype(np.int16).transpose(2, 0, 1)
    return (r > g * 1.25) & (r > b * 1.15) & (r > 50) & (g < 205)


def make_sheet(state):
    reference = np.asarray(
        Image.open(HERE / f"Spectre_reference_state{state}.png").convert("RGB")
    ).copy()
    paint = np.asarray(
        Image.open(HERE / f"Spectre_painted_state{state}.png")
        .convert("RGB")
        .resize((1536, 1536), Image.Resampling.NEAREST)
    )
    assert reference.shape == paint.shape == (1536, 1536, 3)

    src = reference.astype(np.int16)
    generated = paint.astype(np.int16)
    r, g, b = src.transpose(2, 0, 1)
    pr, pg, pb = generated.transpose(2, 0, 1)
    background = green_mask(reference)
    warm = warm_mask(reference) & ~background

    # A little of the generated pearlescent plating surrounding the former red
    # planes adds its own painted pixel texture and covers orange edge highlights.
    near_warm = np.asarray(
        Image.fromarray((warm * 255).astype(np.uint8)).filter(
            ImageFilter.MaxFilter(7)
        )
    ) > 0
    painted_silver = (
        (pr > 90)
        & (pg > 85)
        & (pb > 100)
        & (pb >= pr * 0.9)
        & ((generated.max(2) - generated.min(2)) < 110)
    )
    blue = (b > r * 1.25) & (b > g * 0.85) & (b > 35)
    ink = src.max(2) < 65
    white_lights = src.min(2) > 210
    armour = (warm | (near_warm & painted_silver))
    armour &= ~background & ~blue & ~ink & ~white_lights

    if state:
        # The generated paintings retain the local orange damage effects. Use
        # those painted marks to locate sparks/tears, then take their precise
        # original pixels from the approved reference sheets.
        painted_orange = (
            (pr > pg * 1.18)
            & (pg > pb * 1.15)
            & (pr > 110)
            & (pg > 50)
            & (pb < 140)
        )
        effect_neighbourhood = np.asarray(
            Image.fromarray((painted_orange * 255).astype(np.uint8)).filter(
                ImageFilter.MaxFilter(3)
            )
        ) > 0
        yy, xx = np.indices((1536, 1536))
        cell_x, cell_y = xx % 512, yy % 512
        damage_area = ((cell_x < 125) & (cell_y > 300)) | (
            (cell_x > 205) & (cell_x < 310) & (cell_y > 335)
        )
        if state == 2:
            damage_area |= (cell_x > 310) & (cell_y > 185)
        armour &= ~(warm & effect_neighbourhood & damage_area)

    # Quantize the model's painted material with the original armour's lighting
    # structure. This retains the source's tiny plate facets while the generated
    # painting contributes the new pearlescent texture. No resampling is smooth.
    paint_value = generated.mean(2) / 255.0
    original_value = src.max(2) / 255.0
    tone = 0.58 * paint_value + 0.42 * original_value
    steps = np.digitize(tone, [0.28, 0.42, 0.56, 0.69, 0.83])
    result = reference.copy()
    result[armour] = SILVER_RAMP[steps[armour]]
    # Keep the approved outline's few green-tinted edge pixels. The actual
    # background pixels in the reference are already pure chroma green.
    # The flash has no red armour and must remain the approved bone-white pose.
    result[1024:1536, 1024:1536] = reference[1024:1536, 1024:1536]

    # A tiny number of the approved source's dark red-violet edge pixels would
    # otherwise read as magenta in Spectre. Remap only those pixels to local
    # dark navy blue, retaining their exact positions and value hierarchy.
    hsv = np.asarray(Image.fromarray(result).convert("HSV"))
    hue, saturation, value = hsv.transpose(2, 0, 1)
    violet = (hue >= 202) & (hue <= 249) & (saturation > 70) & (value > 60)
    violet[1024:1536, 1024:1536] = False
    old = result[violet].astype(np.int16)
    navy = old.copy()
    navy[:, 0] = np.minimum(old[:, 0], old[:, 1] + (old[:, 2] - old[:, 1]) // 10)
    navy[:, 2] = np.maximum(old[:, 2], old[:, 1] + 12)
    result[violet] = np.clip(navy, 0, 255).astype(np.uint8)

    final = Image.fromarray(result, "RGB").resize(
        (3072, 3072), Image.Resampling.NEAREST
    )
    path = OUT / f"skin_Spectre_state{state}.png"
    final.save(path, optimize=True)
    print(f"{path.name}: {armour.sum():,} painted pixels at source scale")


if __name__ == "__main__":
    for state_index in range(3):
        make_sheet(state_index)
