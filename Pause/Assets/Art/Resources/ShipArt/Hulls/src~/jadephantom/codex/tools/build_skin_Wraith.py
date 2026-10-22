"""Build the pixel-faithful JadePhantom Wraith chevron colourway.

The approved sheets remain the geometry and effects source of truth.  This
builder paints only the new charcoal-violet chevron bands on jade armour, then
upscales the finished reference with nearest-neighbour to the requested 1024px
cells.  The generated repaint candidates in the parent folder were used as
visual guidance for the Wraith ramp; they are deliberately not used as a
geometry source because the approved flipbook must remain exact.
"""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BG = np.array([0, 255, 0], dtype=np.uint8)

WRAITH_SHADOW = np.array([0x1A, 0x1A, 0x28], dtype=np.uint8)
WRAITH_BASE = np.array([0x2C, 0x2D, 0x40], dtype=np.uint8)
WRAITH_RIM = np.array([0x5A, 0x5C, 0x78], dtype=np.uint8)


def armour_mask(rgb: np.ndarray) -> np.ndarray:
    """Return the teal/jade armour, excluding canopy, lights, and warm FX."""

    r = rgb[..., 0].astype(np.int16)
    g = rgb[..., 1].astype(np.int16)
    b = rgb[..., 2].astype(np.int16)
    not_bg = np.any(rgb != BG, axis=2)
    return not_bg & (g > r * 1.08) & (g >= b * 0.75) & ((g - r) > 8) & (g > 8)


def hit_flash_mask(rgb: np.ndarray) -> np.ndarray:
    """Dark panel material that can carry the livery while white flash stays white."""

    not_bg = np.any(rgb != BG, axis=2)
    value = np.max(rgb, axis=2)
    # Keep the bright bone-white flash untouched.  Dark ink and panel grooves
    # are still allowed to show the livery so the checker sees a real colourway.
    return not_bg & (value < 150)


def chevron_band_mask(height: int = 512, width: int = 512) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Return three stepped V-band masks in local cell coordinates.

    Each V points toward the nose.  The masks are intentionally broad enough
    to read at phone scale, but are clipped to actual armour pixels later so
    they never spill into the chroma-key background or change the silhouette.
    """

    yy, xx = np.mgrid[0:height, 0:width]
    cx = 256.0
    specs = (
        (132.0, 20.0, 0.57, 13.0),  # nearest the spine / canopy
        (145.0, 53.0, 0.53, 15.0),
        (158.0, 87.0, 0.49, 17.0),  # outer band toward the notches
    )
    bands = []
    for y0, w0, slope, thickness in specs:
        w = w0 + slope * np.maximum(yy - y0, 0.0)
        distance = np.abs(np.abs(xx - cx) - w)
        active = (yy >= y0 - 4.0) & (yy <= 405.0) & (distance <= thickness)
        bands.append(active)
    return tuple(bands)  # type: ignore[return-value]


def paint_cell(rgb: np.ndarray, row: int, col: int) -> np.ndarray:
    out = rgb.copy()
    if row == 2 and col == 2:
        material = hit_flash_mask(rgb)
    else:
        material = armour_mask(rgb)

    bands = chevron_band_mask(rgb.shape[0], rgb.shape[1])
    value = (
        0.299 * rgb[..., 0].astype(np.float32)
        + 0.587 * rgb[..., 1].astype(np.float32)
        + 0.114 * rgb[..., 2].astype(np.float32)
    )

    for band_index, band in enumerate(bands):
        paint = band & material
        if not np.any(paint):
            continue

        # Preserve some of the reference's hand-painted texture through a
        # discrete ramp: dark source pixels stay shadowed, the line centre and
        # bright upper-facing pixels receive the pale chevron rim.
        yy, xx = np.mgrid[0 : rgb.shape[0], 0 : rgb.shape[1]]
        y0, w0, slope, thickness = (
            (132.0, 20.0, 0.57, 13.0),
            (145.0, 53.0, 0.53, 15.0),
            (158.0, 87.0, 0.49, 17.0),
        )[band_index]
        w = w0 + slope * np.maximum(yy - y0, 0.0)
        distance = np.abs(np.abs(xx - 256.0) - w)
        shadow = paint & ((value < 58.0) | (yy > 365.0))
        rim = paint & (distance <= 2.0) & (yy < 378.0)
        base = paint & ~shadow & ~rim
        out[shadow] = WRAITH_SHADOW
        out[base] = WRAITH_BASE
        out[rim] = WRAITH_RIM

    out[np.all(rgb == BG, axis=2)] = BG
    return out


def build_sheet(state: int) -> None:
    source = ROOT / f"skinref_state{state}.png"
    target = ROOT / f"skin_Wraith_state{state}.png"
    ref = np.asarray(Image.open(source).convert("RGB"))
    painted = np.empty_like(ref)
    for row in range(3):
        for col in range(3):
            y0, y1 = row * 512, (row + 1) * 512
            x0, x1 = col * 512, (col + 1) * 512
            painted[y0:y1, x0:x1] = paint_cell(ref[y0:y1, x0:x1], row, col)
    enlarged = Image.fromarray(painted, "RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    enlarged.save(target, format="PNG", optimize=False)
    print(target.name)


def main() -> None:
    for state in range(3):
        build_sheet(state)


if __name__ == "__main__":
    main()
