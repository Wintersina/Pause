"""Build only the new Sodium damaged/wreck sheets.

The approved 1536px sheets provide the locked drawing.  Colour is sampled from
the finished Sodium intact sheet at matching pixels, then extended to blue
damage-era armour pixels with the same stepped orange paint ramp.  The export
is a nearest-neighbour 2x copy, so no pose, silhouette, or pixel placement is
resampled or regenerated.
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
    return hue, saturation, value


def sodium_armour_mask(rgb: np.ndarray) -> np.ndarray:
    hue, sat, value = hsv_arrays(rgb)
    # Keep the teal canopy out of the armour pass.  Damage soot, gunmetal,
    # lights, and the bone hit-flash are intentionally left alone.
    canopy = (
        (hue >= 165)
        & (hue <= 215)
        & (sat > 0.45)
        & (rgb[..., 1] > 20)
        & ((rgb[..., 1] / np.maximum(rgb[..., 2], 1)) > 0.58)
        & ((rgb[..., 1] / np.maximum(rgb[..., 2], 1)) < 1.08)
    )
    return (
        (hue >= 190)
        & (hue <= 250)
        & (sat > 0.30)
        & (value > 0.12)
        & (rgb[..., 2] > rgb[..., 1] * 1.08)
        & ~canopy
        & (rgb[..., 0] < 120)
    )


def canopy_mask(rgb: np.ndarray) -> np.ndarray:
    hue, sat, _value = hsv_arrays(rgb)
    return (
        (hue >= 165)
        & (hue <= 215)
        & (sat > 0.45)
        & (rgb[..., 1] > 20)
        & ((rgb[..., 1] / np.maximum(rgb[..., 2], 1)) > 0.58)
        & ((rgb[..., 1] / np.maximum(rgb[..., 2], 1)) < 1.08)
    )


def learn_pixel_map(source: np.ndarray, sodium: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Learn a compact sorted exact source-to-Sodium paint lookup."""
    source_codes = (
        (source[..., 0].astype(np.uint32) << 16)
        | (source[..., 1].astype(np.uint32) << 8)
        | source[..., 2].astype(np.uint32)
    ).reshape(-1)
    first = np.unique(source_codes, return_index=True)
    keys, first_indices = first[0], first[1]
    return keys, sodium.reshape(-1, 3)[first_indices]


def nearest_ramp_colour(pixel: np.ndarray, ramp: np.ndarray) -> np.ndarray:
    """Choose a hard orange ramp tone by luminance, retaining stepped texture."""
    luminance = float(pixel[0]) * 0.22 + float(pixel[1]) * 0.70 + float(pixel[2]) * 0.08
    ramp_luma = ramp[:, 0] * 0.22 + ramp[:, 1] * 0.70 + ramp[:, 2] * 0.08
    return ramp[int(np.argmin(np.abs(ramp_luma - luminance)))]


def repaint(
    reference: np.ndarray,
    pixel_keys: np.ndarray,
    pixel_values: np.ndarray,
    base_armour: np.ndarray,
    state: int,
) -> np.ndarray:
    out = reference.copy()
    armour = sodium_armour_mask(reference)

    # First use the exact finished-sheet paint correspondence.  This keeps
    # the source's hand-placed panel texture and ramps instead of hue-shifting.
    codes = (
        (reference[..., 0].astype(np.uint32) << 16)
        | (reference[..., 1].astype(np.uint32) << 8)
        | reference[..., 2].astype(np.uint32)
    )
    indices = np.searchsorted(pixel_keys, codes)
    valid = indices < pixel_keys.size
    valid &= np.where(valid, pixel_keys[np.minimum(indices, pixel_keys.size - 1)] == codes, False)
    mapped = pixel_values[np.minimum(indices, pixel_values.shape[0] - 1)]
    exact = armour & valid
    out[exact] = mapped[exact]

    # Wreck damage introduces extra blue pixels absent from the intact sheet;
    # paint only those pixels with the Sodium stepped ramp.
    ramp = np.array(
        [
            (169, 72, 26),   # shadow #A9481A
            (207, 104, 31),
            (242, 134, 43),  # base #F2862B
            (248, 155, 47),
            (255, 180, 60),  # highlight #FFB43C
            (255, 242, 188), # pale yellow-white hot edge
        ],
        dtype=np.uint8,
    )
    remaining = armour & np.all(out == reference, axis=2)
    ys, xs = np.where(remaining)
    for y, x in zip(ys.tolist(), xs.tolist()):
        out[y, x] = nearest_ramp_colour(reference[y, x], ramp)

    # Wreck-era blue plates are often hidden under soot and warm damage pixels,
    # so use the intact sheet's plate footprint to repaint the still-visible
    # plate surface.  Keep bright embers/tears, canopy, and deep soot intact.
    if state == 2:
        warm_hue, warm_sat, warm_value = hsv_arrays(reference)
        surface = (
            base_armour
            & np.any(reference != GREEN, axis=2)
            & ~canopy_mask(reference)
            & np.any(reference > 45, axis=2)
        )
        bright_damage = (
            ((warm_hue < 65) | (warm_hue > 330))
            & (warm_sat > 0.20)
            & (warm_value > 0.65)
        )
        surface &= ~bright_damage
        ys, xs = np.where(surface)
        for y, x in zip(ys.tolist(), xs.tolist()):
            out[y, x] = nearest_ramp_colour(reference[y, x], ramp)

    # The bottom-right hit-flash remains bone-white, but is repainted through
    # a warm bone ramp so it is a genuine Sodium colourway rather than a copied
    # cell.  Its silhouette and all pixel positions stay locked.
    hit = np.zeros(reference.shape[:2], dtype=bool)
    hit[1024:1536, 1024:1536] = True
    hit &= np.any(reference != GREEN, axis=2)
    value = reference.max(axis=2).astype(np.float32) / 255.0
    bone_ramp = np.array(
        [(54, 50, 47), (125, 117, 106), (180, 169, 153), (243, 233, 211), (255, 251, 233)],
        dtype=np.uint8,
    )
    bone_step = np.select(
        [value < 0.18, value < 0.38, value < 0.62, value < 0.84],
        [0, 1, 2, 3],
        default=4,
    )
    out[hit] = bone_ramp[bone_step[hit]]

    # The approved references contain a few purple edge contaminants.  Sodium
    # forbids pink/magenta, so fold those pixels into the local material ramp.
    hue, saturation, _value = hsv_arrays(reference)
    forbidden_magenta = (hue > 280) & (hue < 345) & (saturation > 0.30)
    ys, xs = np.where(forbidden_magenta & ~hit)
    for y, x in zip(ys.tolist(), xs.tolist()):
        out[y, x] = nearest_ramp_colour(reference[y, x], ramp)
    out[np.all(reference == GREEN, axis=2)] = GREEN
    return out


def main() -> None:
    source0 = np.asarray(Image.open(ROOT / "skinref_state0.png").convert("RGB"), dtype=np.uint8)
    sodium0_big = np.asarray(Image.open(ROOT / "skin_Sodium_state0.png").convert("RGB"), dtype=np.uint8)
    sodium0 = sodium0_big[::2, ::2]
    if source0.shape != (1536, 1536, 3) or sodium0.shape != source0.shape:
        raise SystemExit("reference and Sodium intact sheet must reduce to 1536x1536 RGB")

    pixel_keys, pixel_values = learn_pixel_map(source0, sodium0)
    base_armour = sodium_armour_mask(source0)
    for state in (1, 2):
        source = np.asarray(Image.open(ROOT / f"skinref_state{state}.png").convert("RGB"), dtype=np.uint8)
        painted = repaint(source, pixel_keys, pixel_values, base_armour, state)
        target = Image.fromarray(painted, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
        path = ROOT / f"skin_Sodium_state{state}_v2.png"
        target.save(path, format="PNG", compress_level=9)
        print(path)


if __name__ == "__main__":
    main()
