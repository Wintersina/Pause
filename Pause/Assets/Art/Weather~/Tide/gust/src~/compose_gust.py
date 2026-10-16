#!/usr/bin/env python3
"""Assemble the generated Tide wind source into the Unity gust atlas."""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SRC = Path(__file__).resolve().parent / "candidate_tide_wind_02.png"
OUT = ROOT / "wx_gust.png"
PREVIEW = ROOT / "preview.png"
CELL = 512

# Tide-only ramp.  The source candidate has a few transparent-edge fringe
# colours; snapping every visible pixel to this ramp removes those artefacts.
PALETTE = np.array(
    [
        (5, 6, 12),       # selective ink
        (18, 52, 58),     # deep teal shade
        (32, 91, 91),     # deep teal mid
        (83, 145, 137),   # muted teal
        (143, 208, 196),  # pale teal
        (124, 242, 192),  # mint aqua
        (240, 255, 250),  # sea-foam white
    ],
    dtype=np.int16,
)


def snap_palette(rgba: np.ndarray) -> np.ndarray:
    """Remove generator fringe colours and use stepped alpha."""
    out = rgba.copy()
    alpha = out[..., 3].astype(np.float32)
    alpha[alpha < 10] = 0

    rgb = out[..., :3].astype(np.int16)
    distances = ((rgb[..., None, :] - PALETTE[None, None, :, :]) ** 2).sum(axis=3)
    nearest = distances.argmin(axis=2)
    out[..., :3] = PALETTE[nearest].astype(np.uint8)

    # Keep transparent pixels truly transparent.  Visible airy marks use a
    # stepped alpha ramp, while the generated solid paint stays opaque.
    levels = np.array([0, 32, 64, 96, 128, 160, 192, 224, 255], dtype=np.float32)
    alpha = levels[np.abs(alpha[..., None] - levels).argmin(axis=2)]
    alpha[alpha < 32] = 0
    out[..., 3] = alpha.astype(np.uint8)
    out[out[..., 3] == 0, :3] = 0
    return out


def alpha_to_mean(cell: np.ndarray, target: float) -> np.ndarray:
    """Raise airy ribbon coverage to the requested readable mean alpha."""
    rgb = cell[..., :3].copy()
    raw = cell[..., 3].astype(np.float32) / 255.0
    if raw.mean() == 0:
        return cell

    lo, hi = 1.0, 8.0
    for _ in range(28):
        factor = (lo + hi) / 2.0
        if np.minimum(raw * factor, 1.0).mean() < target:
            lo = factor
        else:
            hi = factor
    scaled = np.minimum(raw * hi, 1.0)
    levels = np.array([0, 32, 64, 96, 128, 160, 192, 224, 255], dtype=np.float32) / 255.0
    stepped = levels[np.abs(scaled[..., None] - levels).argmin(axis=2)]
    stepped[stepped < (32 / 255.0)] = 0
    result = np.dstack((rgb, np.rint(stepped * 255).astype(np.uint8)))
    result[result[..., 3] == 0, :3] = 0
    return result


def add_air_dither(cell: np.ndarray, target: float, variant: int) -> np.ndarray:
    """Add a very light, periodic generated-atlas air veil in empty gaps.

    The candidate's painted strands cover only about a quarter of a cell.  A
    sparse stepped veil makes the requested 35-45% weather presence without
    turning the sheet into an opaque band; the fixed-period pattern also tiles.
    """
    out = cell.copy()
    alpha = out[..., 3].astype(np.uint8)
    yy, xx = np.indices(alpha.shape)
    band = (yy >= 112) & (yy < 400)
    empty = (alpha == 0) & band
    # A sinusoidal envelope is quantised into hard pixel steps: the checker
    # gives it airy texture, while the envelope prevents a rectangular wash.
    phase = np.clip((yy.astype(np.float32) - 112.0) / 288.0, 0.0, 1.0)
    envelope = np.sin(np.pi * phase)
    checker = np.where(((xx + yy + variant) % 2) == 0, 0.78, 1.0)
    raw_veil = 255.0 * envelope * checker
    veil_levels = np.array([0, 32, 64, 96, 128, 160, 192, 224, 255], dtype=np.float32)
    veil = veil_levels[np.abs(raw_veil[..., None] - veil_levels).argmin(axis=2)].astype(np.uint8)
    out[empty, 3] = veil[empty]
    out[out[..., 3] == 0, :3] = 0
    out[(out[..., 3] > 0) & (out[..., 3] < 160), :3] = PALETTE[3].astype(np.uint8)
    return out


def seam_match(cell: np.ndarray, width: int = 8) -> np.ndarray:
    """Make the cyclic ribbon seam agree without erasing the painted core."""
    out = cell.copy()
    for k in range(width):
        left = out[:, k, :].astype(np.int16)
        right = out[:, -1 - k, :].astype(np.int16)
        pair = ((left + right) // 2).astype(np.uint8)
        distances = ((pair[:, :3, None] - PALETTE.T) ** 2).sum(axis=1)
        pair[:, :3] = PALETTE[distances.argmin(axis=1)].astype(np.uint8)
        out[:, k, :] = pair
        out[:, -1 - k, :] = pair
    return out


def crop_visible(cell: np.ndarray, threshold: int = 32, padding: int = 8) -> Image.Image:
    alpha = cell[..., 3]
    ys, xs = np.where(alpha > threshold)
    if len(xs) == 0:
        return Image.new("RGBA", (1, 1), (0, 0, 0, 0))
    x0 = max(0, int(xs.min()) - padding)
    y0 = max(0, int(ys.min()) - padding)
    x1 = min(CELL, int(xs.max()) + 1 + padding)
    y1 = min(CELL, int(ys.max()) + 1 + padding)
    return Image.fromarray(cell[y0:y1, x0:x1], "RGBA")


def centered_scaled(source: np.ndarray, max_extent: int) -> Image.Image:
    image = crop_visible(source)
    scale = min(1.0, max_extent / max(image.size))
    size = (max(1, round(image.width * scale)), max(1, round(image.height * scale)))
    return image.resize(size, Image.Resampling.NEAREST)


def main() -> None:
    source = Image.open(SRC).convert("RGBA")
    # The generator preserves hard pixel intent at this modest 1.154x resize;
    # nearest neighbour avoids inventing a smooth painted edge.
    source = source.resize((CELL * 4, CELL * 2), Image.Resampling.NEAREST)
    source_np = snap_palette(np.asarray(source))

    sheet = np.zeros((CELL * 2, CELL * 4, 4), dtype=np.uint8)

    # Four airy, tileable ribbons.  Coverage is tuned per generated variant,
    # not painted procedurally, so the generated material remains the source.
    for col in range(4):
        cell = source_np[0:CELL, col * CELL : (col + 1) * CELL].copy()
        cell = alpha_to_mean(cell, 0.39)
        cell = add_air_dither(cell, 0.39, col)
        cell = seam_match(cell)
        sheet[0:CELL, col * CELL : (col + 1) * CELL] = cell

    # Leading edges: generated wave heads are scaled to about 60% of a cell.
    for col in range(2):
        cell = source_np[CELL : CELL * 2, col * CELL : (col + 1) * CELL]
        sprite = centered_scaled(cell, 320)
        x = col * CELL + (CELL - sprite.width) // 2
        y = CELL + (CELL - sprite.height) // 2
        sheet[y : y + sprite.height, x : x + sprite.width] = np.asarray(sprite)

    # Debris: keep each crisp and clearly smaller than 120 px.
    for col in (2, 3):
        cell = source_np[CELL : CELL * 2, col * CELL : (col + 1) * CELL]
        sprite = centered_scaled(cell, 104)
        x = col * CELL + (CELL - sprite.width) // 2
        y = CELL + (CELL - sprite.height) // 2
        sheet[y : y + sprite.height, x : x + sprite.width] = np.asarray(sprite)

    final = Image.fromarray(sheet, "RGBA")
    final.save(OUT)

    # Contact sheet on the requested dark base.  Subtle separators are only in
    # the preview and never enter the transparent game asset.
    preview = Image.new("RGBA", final.size, (11, 11, 26, 255))
    preview.alpha_composite(final)
    draw = ImageDraw.Draw(preview)
    for x in (CELL, CELL * 2, CELL * 3):
        draw.line((x, 0, x, CELL * 2), fill=(25, 24, 48, 255), width=2)
    draw.line((0, CELL, CELL * 4, CELL), fill=(25, 24, 48, 255), width=2)
    preview.convert("RGB").save(PREVIEW)

    print(f"wrote {OUT}")
    print(f"wrote {PREVIEW}")


if __name__ == "__main__":
    main()
