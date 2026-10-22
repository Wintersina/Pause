"""Build the Night UFO sheets from the approved flipbook references.

The generated Night candidates establish the repaint direction. This cleanup pass keeps the
approved drawing contract exact: reference geometry and all non-armour pixels are preserved,
then the violet armour ramp is repainted and the result is enlarged 2x with nearest-neighbour.
"""
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def hsv(rgb):
    rgb = rgb.astype(np.float32) / 255.0
    mx = rgb.max(axis=-1)
    mn = rgb.min(axis=-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d != 0
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    sel = nz & (mx == r)
    h[sel] = ((g[sel] - b[sel]) / d[sel]) % 6
    sel = nz & (mx == g)
    h[sel] = (b[sel] - r[sel]) / d[sel] + 2
    sel = nz & (mx == b)
    h[sel] = (r[sel] - g[sel]) / d[sel] + 4
    h /= 6.0
    s = np.zeros_like(mx)
    s[mx != 0] = d[mx != 0] / mx[mx != 0]
    return h, s, mx


def repaint_armour(rgb, row, col):
    out = rgb.copy()
    h, s, v = hsv(rgb)

    # The approved hit-flash is bone-white and must remain untouched.
    if row == 2 and col == 2:
        return out

    green = np.all(rgb == GREEN, axis=-1)
    # Violet armour has a blue-dominant, high-saturation ramp in the reference. The red/green
    # relationship excludes the gunmetal hub and the teal canopy while retaining the dark ramp.
    armour = (
        ~green
        & (h >= 235.0 / 360.0)
        & (h <= 285.0 / 360.0)
        & (s > 0.28)
        & (v > 0.12)
        & (rgb[..., 2] > rgb[..., 0] + 18)
        & (rgb[..., 0] >= rgb[..., 1] * 0.78)
    )

    # Hard, stepped Night ramp. Bright source pixels are kept as navy highlights unless they
    # sit on an upper-left plate edge; those pixels become the requested cyan neon rim.
    ramp = np.zeros_like(rgb)
    ramp[v < 0.30] = (26, 31, 69)       # #1A1F45 shadow
    ramp[(v >= 0.30) & (v < 0.48)] = (32, 38, 82)
    ramp[(v >= 0.48) & (v < 0.70)] = (42, 46, 107)  # #2A2E6B base
    ramp[(v >= 0.70) & (v < 0.88)] = (57, 69, 128)
    ramp[(v >= 0.88) & (v < 0.96)] = (75, 91, 157)
    ramp[v >= 0.96] = (78, 96, 158)

    # Detect short upper-left-facing highlight runs without changing the silhouette. A source
    # armour pixel is a rim candidate when its upper-left side is exposed and its lower-right
    # side remains part of a plate.
    padded = np.pad(armour, 1, constant_values=False)
    ul_exposed = (
        (~padded[:-2, :-2])
        | (~padded[:-2, 1:-1])
        | (~padded[1:-1, :-2])
    )
    dr_supported = padded[2:, 2:] | padded[2:, 1:-1] | padded[1:-1, 2:]
    cyan_rim = armour & ul_exposed & dr_supported & (v >= 0.72) & (s >= 0.42)
    ramp[cyan_rim] = (110, 242, 238)  # #6EF2EE
    yy, xx = np.indices(v.shape)
    pale = cyan_rim & (v >= 0.92) & (((xx + 2 * yy) % 11) == 0)
    ramp[pale] = (207, 248, 248)

    out[armour] = ramp[armour]
    return out


def build(state):
    ref_path = ROOT / f"skinref_state{state}.png"
    ref = np.array(Image.open(ref_path).convert("RGB"), dtype=np.uint8)
    canvas = np.zeros((3072, 3072, 3), dtype=np.uint8)
    for row in range(3):
        for col in range(3):
            y0, y1 = row * 512, (row + 1) * 512
            x0, x1 = col * 512, (col + 1) * 512
            painted = repaint_armour(ref[y0:y1, x0:x1], row, col)
            # Native reference pixels are the authoritative drawing; this is the required
            # nearest-neighbour 2x export to 1024px cells.
            scaled = Image.fromarray(painted, "RGB").resize((1024, 1024), Image.Resampling.NEAREST)
            canvas[row * 1024:(row + 1) * 1024, col * 1024:(col + 1) * 1024] = np.array(scaled)
    out_path = ROOT / f"skin_Night_state{state}.png"
    Image.fromarray(canvas, "RGB").save(out_path, optimize=False)
    print(out_path)


if __name__ == "__main__":
    for state in range(3):
        build(state)
