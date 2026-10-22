"""Register image-generated Umbra paintings to the approved Paragon pixels.

The image-generated sheets supply the violet material's value variation.  The
approved flipbook supplies the exact silhouette, plate boundaries, blue parts,
damage marks, glints, chroma key and flash.  All resizing is nearest-neighbour.
"""

from pathlib import Path

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
GREEN = np.array([0, 255, 0], dtype=np.uint8)
VIOLET = np.array(
    [
        [46, 9, 105],     # recess shadow
        [79, 18, 179],    # #4F12B3
        [100, 35, 195],
        [122, 60, 216],   # #7A3CD8
        [151, 90, 235],
        [183, 140, 255],  # #B78CFF
    ],
    dtype=np.uint8,
)


def hsv(rgb):
    a = rgb.astype(np.float32) / 255
    hi, lo = a.max(axis=2), a.min(axis=2)
    span = hi - lo
    safe = np.maximum(span, 1e-8)
    r, g, b = a.transpose(2, 0, 1)
    h = np.zeros_like(hi)
    use = (span > 0) & (hi == r)
    h[use] = ((g[use] - b[use]) / safe[use]) % 6
    use = (span > 0) & (hi == g)
    h[use] = (b[use] - r[use]) / safe[use] + 2
    use = (span > 0) & (hi == b)
    h[use] = (r[use] - g[use]) / safe[use] + 4
    return h * 60, span / np.maximum(hi, 1e-8), hi


def build(state):
    ref = np.asarray(Image.open(HERE / f"skinref_state{state}.png").convert("RGB"))
    painted = np.asarray(
        Image.open(HERE / f"skin_Umbra_painted_state{state}.png")
        .convert("RGB")
        .resize((1536, 1536), Image.Resampling.NEAREST)
    )
    assert ref.shape == painted.shape == (1536, 1536, 3)

    h, s, v = hsv(ref)
    ph, ps, pv = hsv(painted)
    r, g, b = ref.astype(np.float32).transpose(2, 0, 1)
    ship = np.any(ref != GREEN, axis=2)
    blue = (b > r * 1.18) & (b > g * .78) & (b > 38)
    ink = ref.max(axis=2) < 42
    white = (ref.min(axis=2) > 215) & (s < .27)
    old_red = ((h <= 34) | (h >= 350)) & (s > .40) & (v > .19)
    warm_plate = (r > g * 1.05) & (r > b * 1.05) & (r > 50)
    painted_violet = (ph >= 248) & (ph <= 290) & (ps > .22) & (pv > .16)
    armour = ship & (old_red | warm_plate | painted_violet)
    armour &= ~blue & ~ink & ~white

    if state:
        yy, xx = np.indices((1536, 1536))
        lx, ly = xx % 512, yy % 512
        damage_area = (lx < 125) & (ly > 175)
        if state == 2:
            damage_area |= (lx > 310) & (ly > 175)
        # Preserve the approved hot cracks and sparks, even where their hue
        # overlaps the old armour's orange highlight ramp.
        hot = (h >= 15) & (h <= 54) & (s > .53) & (v > .34)
        armour &= ~(damage_area & hot)

    # Pixel-by-pixel tone selection uses the generated painting's material
    # texture while following the reference's original plate lighting.  The
    # palette itself is restricted to purple, never pink/magenta.
    original_value = ref.max(axis=2).astype(np.float32) / 255
    painted_value = painted.max(axis=2).astype(np.float32) / 255
    tone = .58 * original_value + .42 * painted_value
    levels = np.digitize(tone, [.23, .42, .66, .96, .99])
    result = ref.copy()
    result[armour] = VIOLET[levels[armour]]

    # Reference edge pixels sometimes carry an old purple-red shadow.  Move
    # those in the armour to the darkest Umbra paint tone; keep blue and ink.
    rh, rs, rv = hsv(result)
    fringe = ship & ~blue & ~ink & (rh > 290) & (rh < 350) & (rs > .4) & (rv > .25)
    result[fringe] = VIOLET[1]

    # A handful of pre-existing blue-panel edge pixels read as magenta.  Move
    # only their red channel into the original navy-blue family.
    rh, rs, rv = hsv(result)
    magenta_blue = ship & blue & (rh > 290) & (rh < 345) & (rs > .4) & (rv > .25)
    result[:, :, 0][magenta_blue] = np.minimum(
        result[:, :, 0][magenta_blue], result[:, :, 2][magenta_blue] // 2
    )

    # The hit flash is the approved bone-white drawing in every colourway.
    result[1024:1536, 1024:1536] = ref[1024:1536, 1024:1536]
    result[~ship] = GREEN
    path = OUT / f"skin_Umbra_state{state}.png"
    Image.fromarray(result, "RGB").resize(
        (3072, 3072), Image.Resampling.NEAREST
    ).save(path, optimize=True)
    print(f"{path.name}: {np.count_nonzero(armour):,} plated pixels at reference scale")


if __name__ == "__main__":
    for state_number in range(3):
        build(state_number)
