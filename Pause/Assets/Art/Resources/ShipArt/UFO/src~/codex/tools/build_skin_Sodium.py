"""Build the Sodium UFO colourway from the approved flipbook references.

The generated Sodium repaint candidates were used as the visual direction. This cleanup pass
keeps the approved drawing contract exact: the source cell supplies geometry, ink, canopy,
damage, lights, and effects; only the violet armour ramp is repainted, then enlarged 2x with
nearest-neighbour into the requested 3072px sheets.
"""
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def hsv(rgb):
    """Return hue in [0, 1], saturation, and value for an RGB image."""
    rgbf = rgb.astype(np.float32) / 255.0
    mx = rgbf.max(axis=-1)
    mn = rgbf.min(axis=-1)
    delta = mx - mn
    hue = np.zeros_like(mx)
    nz = delta != 0
    r, g, b = rgbf[..., 0], rgbf[..., 1], rgbf[..., 2]
    sel = nz & (mx == r)
    hue[sel] = ((g[sel] - b[sel]) / delta[sel]) % 6
    sel = nz & (mx == g)
    hue[sel] = (b[sel] - r[sel]) / delta[sel] + 2
    sel = nz & (mx == b)
    hue[sel] = (r[sel] - g[sel]) / delta[sel] + 4
    hue /= 6.0
    saturation = np.zeros_like(mx)
    nonblack = mx != 0
    saturation[nonblack] = delta[nonblack] / mx[nonblack]
    return hue, saturation, mx


def armour_mask(rgb):
    """Select violet armour while excluding ink, canopy, hub, and emissive lights."""
    hue, saturation, value = hsv(rgb)
    green = np.all(rgb == GREEN, axis=-1)
    return (
        ~green
        & (hue >= 235.0 / 360.0)
        & (hue <= 285.0 / 360.0)
        & (saturation > 0.28)
        & (value > 0.12)
        & (rgb[..., 2] > rgb[..., 0] + 18)
        & (rgb[..., 0] >= rgb[..., 1] * 0.78)
    )


def sodium_ramp(rgb, mask):
    """Paint the selected violet pixels with a stepped sodium-lamp metal ramp.

    The source value controls the ramp step, preserving every painted facet, seam, rivet and
    scratch location. A sparse deterministic highlight selection supplies the pale cream glints
    visible in the generated Sodium direction without changing the source geometry.
    """
    out = rgb.copy()
    _, _, value = hsv(rgb)
    painted = np.zeros_like(rgb)

    # The requested sodium metal ramp, plus two dark ramp steps for the same pixel-art depth.
    painted[value < 0.26] = (92, 34, 15)       # deep orange shadow
    painted[(value >= 0.26) & (value < 0.43)] = (169, 72, 26)  # #A9481A
    painted[(value >= 0.43) & (value < 0.62)] = (210, 96, 24)
    painted[(value >= 0.62) & (value < 0.78)] = (242, 134, 43)  # #F2862B
    painted[(value >= 0.78) & (value < 0.92)] = (255, 180, 60)  # #FFB43C
    painted[value >= 0.92] = (255, 196, 86)

    yy, xx = np.indices(value.shape)
    pale = mask & (value >= 0.87) & (((xx + 3 * yy) % 17) == 0)
    painted[pale] = (255, 239, 194)

    out[mask] = painted[mask]
    return out


def build(state):
    ref_path = ROOT / f"skinref_state{state}.png"
    ref = np.array(Image.open(ref_path).convert("RGB"), dtype=np.uint8)
    if ref.shape != (1536, 1536, 3):
        raise ValueError(f"{ref_path.name}: expected 1536x1536 RGB, got {ref.shape}")

    canvas = np.zeros((3072, 3072, 3), dtype=np.uint8)
    canvas[...] = GREEN
    for row in range(3):
        for col in range(3):
            y0, y1 = row * 512, (row + 1) * 512
            x0, x1 = col * 512, (col + 1) * 512
            source = ref[y0:y1, x0:x1]
            # The approved hit flash stays bone-white, byte-for-byte, before scaling.
            painted = source.copy() if (row == 2 and col == 2) else sodium_ramp(source, armour_mask(source))
            scaled = Image.fromarray(painted, "RGB").resize((1024, 1024), Image.Resampling.NEAREST)
            canvas[row * 1024:(row + 1) * 1024, col * 1024:(col + 1) * 1024] = np.array(scaled)

    out_path = ROOT / f"skin_Sodium_state{state}.png"
    Image.fromarray(canvas, "RGB").save(out_path, optimize=False)
    print(out_path)


if __name__ == "__main__":
    for state in range(3):
        build(state)
