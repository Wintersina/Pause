#!/usr/bin/env python3
"""Create the Bone repaint sheets from the approved Ligher references.

The source sheet is treated as the authority for layout and drawing.  The
material pass is intentionally nearest-neighbour and keeps the exact green
key background and the cyan canopy pixels intact.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)

# Warm ceramic ramp.  The middle steps are the requested Bone base and shadow;
# the additional steps are the fresh pixel-art edge/shadow treatment.
BONE_RAMP = np.array(
    [
        [0x6D, 0x66, 0x5C],  # deep warm seam / socket edge
        [0x94, 0x8A, 0x7A],
        [0xB9, 0xAE, 0x98],  # requested shadow
        [0xD0, 0xC5, 0xB1],
        [0xF4, 0xEA, 0xD4],  # requested base
        [0xFF, 0xF3, 0xDE],
        [0xFF, 0xFD, 0xF2],  # near-white ceramic highlight
    ],
    dtype=np.uint8,
)
RED_RAMP = np.array(
    [
        [0x7D, 0x12, 0x1B],
        [0xD8, 0x23, 0x2C],  # requested red
        [0xFF, 0x5B, 0x45],  # requested hot core
    ],
    dtype=np.uint8,
)


def repaint(source_path: Path, output_path: Path) -> None:
    source = Image.open(source_path).convert("RGB")
    if source.size != (1536, 1536):
        raise ValueError(f"expected 1536x1536 reference, got {source.size}")

    a = np.asarray(source, dtype=np.uint8)
    work = a.copy()
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]

    for cy in range(3):
        for cx in range(3):
            ys = slice(cy * 512, (cy + 1) * 512)
            xs = slice(cx * 512, (cx + 1) * 512)
            z = a[ys, xs].astype(np.int16)
            out = work[ys, xs]
            r, g, b = [z[..., i] for i in range(3)]

            non_green = np.any(z != GREEN, axis=2)

            # Protect the actual cyan/blue glass, not the nearly-black ink
            # around it.  This leaves canopy facets, glints, and their exact
            # pixel pattern untouched.
            blue_glass = (
                (g > 50)
                & (b > 50)
                & (b > r * 1.10)
                & (g > r * 1.10)
            )

            # Bright red emissive seeds identify the slot and fin lights.  A
            # tiny neutral-core expansion turns their white centers into the
            # requested hot red without touching the surrounding armour.
            red_seed = (
                # The source armour contains dark red-orange copper shadows;
                # the true emissive lights are much purer red and brighter.
                (r > 220)
                & (g < 80)
                & (b < 80)
                & (r > g * 3.5)
                & (r > b * 3.5)
            )
            red_dilated = np.asarray(
                Image.fromarray((red_seed * 255).astype(np.uint8), mode="L")
                .filter(ImageFilter.MaxFilter(9))
            ) > 0
            maxc = z.max(axis=2)
            minc = z.min(axis=2)
            saturation = (maxc - minc) / np.maximum(maxc, 1)
            neutral_core = red_dilated & (saturation < 0.18) & (maxc > 130)
            red_light = red_seed | neutral_core

            # The amber nozzle is an emissive element and must remain as in
            # the reference.  It sits at the bottom-centre of every cell,
            # including the banked poses.
            local_y = yy[ys, xs] - cy * 512
            local_x = xx[ys, xs] - cx * 512
            nozzle_keep = (local_y > 430) & (np.abs(local_x - 256) < 58)

            # Repaint the visible material, including darker interior plate
            # seams.  Very dark outer ink and the protected canopy remain
            # exact; this keeps the drawing readable while giving the ceramic
            # a genuinely new ramp instead of a hue shift.
            value = maxc / 255.0
            bone_mask = (
                non_green
                & ~blue_glass
                & ~red_light
                & ~nozzle_keep
                # Include the very dark interior seam pixels as warm ceramic
                # sockets; true black outer ink (maxc == 0) remains untouched.
                & (maxc > 0)
            )

            lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0
            ramp_index = np.select(
                [lum < 0.12, lum < 0.25, lum < 0.40, lum < 0.56, lum < 0.75, lum < 0.90],
                [0, 1, 2, 3, 4, 5],
                default=6,
            )
            out[bone_mask] = BONE_RAMP[ramp_index[bone_mask]]

            red_index = np.select(
                [value < 0.45, value < 0.78],
                [0, 1],
                default=2,
            )
            out[red_light] = RED_RAMP[red_index[red_light]]

            # Remove the source's saturated purple shadow tint.  Keep the
            # cyan glass protected; every other affected pixel becomes a
            # dark warm seam so the final sheet contains no magenta/purple
            # material while retaining the same drawing and damage shapes.
            magenta_shadow = (
                non_green
                & ~blue_glass
                & (r > g)
                & (b > g)
                & (maxc > 0)
            )
            out[magenta_shadow] = BONE_RAMP[0]

            # Reassert the exact key colour and protected canopy after all
            # operations.  This also guards against accidental dtype changes.
            out[~non_green] = GREEN
            out[blue_glass] = z[blue_glass].astype(np.uint8)
            work[ys, xs] = out

    # Upscale only after repainting so every native pixel becomes one exact
    # 2x2 block.  No filtering or antialiasing is introduced.
    final = Image.fromarray(work, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    final.save(output_path, format="PNG", optimize=False)


def main() -> None:
    for state in range(3):
        source = ROOT / f"skinref_state{state}.png"
        output = ROOT / f"skin_Bone_state{state}.png"
        repaint(source, output)
        print(output)


if __name__ == "__main__":
    main()
