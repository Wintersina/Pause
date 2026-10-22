"""Build the Sodium IonLancer sheets from the approved flipbook.

The Sodium paint pass was selected from image-generated repaint candidates, then
cleaned here against the approved source at native resolution.  The cleanup is
deliberately palette-aware rather than a geometric transform: source placement,
silhouette, panel texture, damage, lights, glass, engines, and the hit-flash cell
stay locked, while blue armour receives a stepped sodium-orange paint ramp.
"""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def hsv_arrays(rgb: np.ndarray):
    r, g, b = (rgb[..., i].astype(np.float32) for i in range(3))
    mx = rgb.max(axis=2).astype(np.float32)
    mn = rgb.min(axis=2).astype(np.float32)
    delta = mx - mn
    hue = np.zeros_like(mx)
    nz = delta > 0
    rr, gg, bb, mm, dd = r[nz], g[nz], b[nz], mx[nz], delta[nz]
    hue[nz] = np.where(
        mm == rr,
        (gg - bb) / dd,
        np.where(mm == gg, 2 + (bb - rr) / dd, 4 + (rr - gg) / dd),
    ) * 60.0
    hue %= 360.0
    saturation = np.divide(delta, mx, out=np.zeros_like(delta), where=mx > 0)
    value = mx / 255.0
    return hue, saturation, value, r, g, b


def armour_mask(rgb: np.ndarray):
    hue, sat, value, r, g, b = hsv_arrays(rgb)

    # Teal canopy glass is intentionally excluded from the armour pass.
    blue_glass = (
        (hue >= 165)
        & (hue <= 215)
        & (sat > 0.45)
        & (g > 20)
        & ((g / np.maximum(b, 1)) > 0.58)
        & ((g / np.maximum(b, 1)) < 1.08)
    )
    armour = (
        (hue >= 190)
        & (hue <= 250)
        & (sat > 0.38)
        & (value > 0.18)
        & (b > g * 1.08)
        & ~blue_glass
        # Dark navy/black damage and soot are not blue plate material.
        & ~((r < 20) & (g < 22) & (b < 45))
    )
    return armour, blue_glass, value, r, g, b


def repaint(source: Image.Image) -> Image.Image:
    rgb = np.asarray(source.convert("RGB"), dtype=np.uint8)
    out = rgb.copy()
    armour, _blue_glass, value, r, g, b = armour_mask(rgb)

    # Sodium ramp chosen from the generated repaint candidates and quantised to
    # preserve the source's hand-placed hard bands and panel texture.
    sodium_ramp = np.array(
        [
            (169, 72, 26),    # shadow #A9481A
            (207, 104, 31),
            (242, 134, 43),   # base #F2862B
            (248, 155, 47),
            (255, 180, 60),   # highlight #FFB43C
        ],
        dtype=np.uint8,
    )
    step = np.select(
        [value < 0.28, value < 0.42, value < 0.58, value < 0.74],
        [0, 1, 2, 3],
        default=4,
    )
    out[armour] = sodium_ramp[step[armour]]

    # Preserve the source's sparse lit-edge placement, but make those armour
    # pixels pale sodium-hot instead of blue/cyan.
    up = np.roll(armour, 1, axis=0)
    left = np.roll(armour, 1, axis=1)
    upper_left_edge = armour & (~up | ~left)
    hot_edge = upper_left_edge & (value > 0.52)
    out[hot_edge] = (255, 242, 188)

    # The bottom-right pose is the approved bone-white hit flash. Keep its
    # silhouette and dark ink while retaining a bone-white material ramp.
    hit = np.zeros(rgb.shape[:2], dtype=bool)
    hit[1024:1536, 1024:1536] = True
    neutral_bone = (
        hit
        & (r > 80)
        & (g > 70)
        & (b > 60)
        & ((np.maximum(np.maximum(r, g), b) - np.minimum(np.minimum(r, g), b)) < 80)
    )
    bone = np.empty((*value.shape, 3), dtype=np.uint8)
    bone[:] = (255, 251, 233)
    bone[value < 0.90] = (254, 249, 231)
    bone[value < 0.75] = (243, 233, 211)
    bone[value < 0.55] = (174, 163, 150)
    out[neutral_bone] = bone[neutral_bone]

    # The approved sheets contain occasional contaminated purple edge pixels;
    # Sodium forbids pink/magenta, so fold them into the local orange/bone ramp.
    hue, saturation, _value, _r, _g, _b = hsv_arrays(rgb)
    forbidden_magenta = (hue > 280) & (hue < 345) & (saturation > 0.30)
    out[forbidden_magenta & ~hit] = sodium_ramp[step[forbidden_magenta & ~hit]]
    out[forbidden_magenta & hit] = bone[forbidden_magenta & hit]

    # Avoid roll-based edge detection changing the sheet boundary.
    out[0, :, :] = rgb[0, :, :]
    out[-1, :, :] = rgb[-1, :, :]
    out[:, 0, :] = rgb[:, 0, :]
    out[:, -1, :] = rgb[:, -1, :]

    enlarged = Image.fromarray(out, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    return enlarged


def main() -> None:
    for state in range(3):
        source_path = ROOT / f"skinref_state{state}.png"
        target_path = ROOT / f"skin_Sodium_state{state}.png"
        repaint(Image.open(source_path)).save(target_path, format="PNG", compress_level=9)
        print(target_path)


if __name__ == "__main__":
    main()
