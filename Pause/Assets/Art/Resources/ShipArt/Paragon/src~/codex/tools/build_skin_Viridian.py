"""Register image-generated Viridian paint to the approved Paragon flipbook.

The image-generated sheets supply emerald material texture and light placement.
The approved sheets supply exact pose geometry, panel edges, blue equipment,
damage marks, background, and hit-flash artwork. All resizing is nearest-neighbour.
"""

from pathlib import Path

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
CHROMA = np.array((0, 255, 0), dtype=np.uint8)
RAMP = np.array(
    [
        (14, 90, 68),    # #0E5A44, deepest emerald shadow
        (19, 116, 86),
        (27, 149, 105),
        (31, 181, 122),  # #1FB57A, base
        (77, 212, 156),
        (142, 240, 196), # #8EF0C4, edge light
    ],
    dtype=np.uint8,
)


def hsv(rgb):
    a = rgb.astype(np.float32) / 255.0
    top, bottom = a.max(2), a.min(2)
    span = top - bottom
    safe = np.maximum(span, 1e-8)
    r, g, b = a.transpose(2, 0, 1)
    h = np.zeros_like(top)
    m = (span > 0) & (top == r)
    h[m] = ((g[m] - b[m]) / safe[m]) % 6
    m = (span > 0) & (top == g)
    h[m] = (b[m] - r[m]) / safe[m] + 2
    m = (span > 0) & (top == b)
    h[m] = (r[m] - g[m]) / safe[m] + 4
    return h * 60, span / np.maximum(top, 1e-8), top


def build(state):
    ref = np.asarray(Image.open(HERE / f"skinref_state{state}.png").convert("RGB"))
    painted = np.asarray(
        Image.open(HERE / f"skin_Viridian_painted_state{state}.png")
        .convert("RGB")
        .resize((1536, 1536), Image.Resampling.NEAREST)
    )
    rh, rs, rv = hsv(ref)
    ph, ps, pv = hsv(painted)
    ship = np.any(ref != CHROMA, axis=2)
    old_red = ((rh <= 34) | (rh >= 350)) & (rs > .40) & (rv > .19)
    ref_blue = (ref[:, :, 2].astype(np.float32) > ref[:, :, 0] * 1.18) & (
        ref[:, :, 2].astype(np.float32) > ref[:, :, 1] * .78) & (ref[:, :, 2] > 38)
    ink = ref.max(2) < 42
    white = (ref.min(2) > 215) & (rs < .27)
    paint_emerald = (ph >= 138) & (ph <= 180) & (ps > .30) & (pv > .17)

    # In damaged sheets these hot marks are effects, not red paint. Keep their
    # exact orange reference pixels even when the generated paint overlaps.
    yy, xx = np.indices((1536, 1536))
    lx, ly = xx % 512, yy % 512
    damage_area = ((state == 1) & (lx < 112) & (ly > 180)) | (
        (state == 2) & (lx > 310) & (ly > 175))
    hot_crack = (rh < 26) & (rs > .72) & (rv > .48)
    painted_damage = (ph >= 5) & (ph <= 38) & (ps > .55) & (pv > .38)
    protect = ~ship | ref_blue | ink | white | (
        damage_area & hot_crack & painted_damage & ~paint_emerald)

    # Take the generated green material where it follows the original plates;
    # fill its small registration gaps with the same generated paint ramp. The
    # reference brightness keeps the existing tiny facets and panel pattern.
    armour = ship & ~protect & (old_red | paint_emerald)
    armour &= ~((ly >= 0) & (lx >= 0) & (xx >= 1024) & (yy >= 1024))
    source_light = ref.astype(np.float32).max(2) / 255.0
    painted_light = painted.astype(np.float32).max(2) / 255.0
    light = np.where(paint_emerald, painted_light, .70 * source_light)
    steps = np.digitize(light, [0.30, 0.45, 0.68, 0.92, 0.99])
    result = ref.copy()
    result[armour] = RAMP[steps[armour]]

    # Remove old red-violet flecks. A few sit inside blue equipment, where they
    # become blue navy rather than emerald so the equipment stays blue.
    h, s, v = hsv(result)
    magenta = ship & ~ink & (h > 278) & (h < 350) & (s > .30) & (v > .14)
    magenta[1024:1536, 1024:1536] = False
    result[magenta & ~ref_blue] = RAMP[0]
    violet_blue = magenta & ref_blue
    navy = result[violet_blue].astype(np.int16)
    navy[:, 0] = np.minimum(navy[:, 0], navy[:, 1])
    navy[:, 2] = np.maximum(navy[:, 2], navy[:, 1] + 12)
    result[violet_blue] = np.clip(navy, 0, 255).astype(np.uint8)
    result[1024:1536, 1024:1536] = ref[1024:1536, 1024:1536]
    result[~ship] = CHROMA
    path = OUT / f"skin_Viridian_state{state}.png"
    Image.fromarray(result, "RGB").resize(
        (3072, 3072), Image.Resampling.NEAREST
    ).save(path, optimize=True)
    print(path.name, "armour", round(armour.mean() * 100, 2), "%")


if __name__ == "__main__":
    for state_index in range(3):
        build(state_index)
