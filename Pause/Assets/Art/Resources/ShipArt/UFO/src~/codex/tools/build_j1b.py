#!/usr/bin/env python3
"""Build the J1b UFO pose sheets without touching the approved key art."""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
PREVIEW_BG = (11, 11, 26)
SIZE = 1024


def load_key(state: int) -> np.ndarray:
    return np.array(Image.open(ROOT / f"key_state{state}.png").convert("RGB"))


def light_mask(key0: np.ndarray) -> np.ndarray:
    """Return only the emissive pixels of the eight intact rim lights."""
    yy, xx = np.indices((SIZE, SIZE))
    r, g, b = [key0[..., i].astype(np.int16) for i in range(3)]
    mask = np.zeros((SIZE, SIZE), dtype=bool)
    centers = (
        (512, 128), (760, 264), (884, 512), (760, 760),
        (512, 856), (264, 760), (140, 512), (264, 264),
    )
    gold = (r > 150) & (g > 70) & (b < 150)
    white_hot = (r > 180) & (g > 180) & (b > 140)
    red = (r > 70) & (r > g * 1.25) & (r > b * 1.2) & (g < 160)
    for cx, cy in centers:
        box = (np.abs(xx - cx) <= 78) & (np.abs(yy - cy) <= 78)
        mask |= box & (gold | white_hot | red)
    return mask


def canopy_mask() -> np.ndarray:
    m = Image.new("1", (SIZE, SIZE), 0)
    ImageDraw.Draw(m).polygon(
        [(512, 418), (590, 464), (590, 560),
         (512, 606), (434, 560), (434, 464)],
        fill=1,
    )
    return np.array(m, dtype=bool)


def darken_rgb(rgb: np.ndarray, amount: float = 0.72) -> np.ndarray:
    out = np.rint(rgb.astype(np.float32) * amount).astype(np.uint8)
    return out


def make_lights_off(key: np.ndarray, mask: np.ndarray, protected: np.ndarray) -> np.ndarray:
    out = key.copy()
    # Keep the key's exact hull and use existing dark-violet/navy values for the lamps.
    mask = mask & ~protected
    r, g, b = [key[..., i].astype(np.int16) for i in range(3)]
    dark = np.empty_like(key)
    dark[:] = (26, 20, 68)
    mid = (r + g + b > 350) & mask
    dark[mid] = (49, 39, 112)
    dark[mask & (r < 120)] = (18, 18, 52)
    out[mask] = dark[mask]

    # Dim the cyan hub ring, using stepped palette values rather than blur.
    yy, xx = np.indices((SIZE, SIZE))
    radius = np.hypot(xx - 512, yy - 512)
    rr, gg, bb = [key[..., i].astype(np.int16) for i in range(3)]
    hub = (radius >= 128) & (radius <= 205) & (gg > 90) & (bb > 100) & (rr < 150) & ~protected
    out[hub] = darken_rgb(key[hub], 0.70)

    # Dim the teal glass slightly, preserving its exact polygon/silhouette.
    glass = canopy_mask()
    teal = glass & (gg > 90) & (bb > 100) & (rr < 150) & ~protected
    out[teal] = darken_rgb(key[teal], 0.78)
    return out


def draw_glint(base: np.ndarray, side: str, protected: np.ndarray) -> np.ndarray:
    """Paint a stepped glint inside the glass only; all hull pixels remain from base."""
    out = Image.fromarray(base, "RGB")
    d = ImageDraw.Draw(out)
    glass = canopy_mask()

    # Stepped neon bands: the narrow white core is 3–4 game-pixel steps wide;
    # the surrounding cyan bands are discrete, not blurred.
    if side == "left":
        start, end = (446, 555), (505, 462)
        sparkle = (505, 462)
    else:
        start, end = (519, 555), (579, 478)
        sparkle = (579, 478)
    if side == "left":
        d.line([start, end], fill=(21, 216, 252), width=280)
        d.line([start, end], fill=(122, 246, 252), width=48)
        d.line([start, end], fill=(245, 253, 253), width=16)
        sparkle_color, sparkle_radius = (255, 251, 224), 15
    else:
        d.line([start, end], fill=(11, 145, 205), width=120)
        d.line([start, end], fill=(70, 190, 225), width=28)
        d.line([start, end], fill=(190, 235, 240), width=8)
        sparkle_color, sparkle_radius = (225, 235, 220), 8

    # Bone-white, hard-pixel tip sparkle.
    sx, sy = sparkle
    d.polygon(
        [(sx, sy - sparkle_radius), (sx + sparkle_radius // 2, sy - sparkle_radius // 2),
         (sx + sparkle_radius, sy), (sx + sparkle_radius // 2, sy + sparkle_radius // 2),
         (sx, sy + sparkle_radius), (sx - sparkle_radius // 2, sy + sparkle_radius // 2),
         (sx - sparkle_radius, sy), (sx - sparkle_radius // 2, sy - sparkle_radius // 2)],
        fill=sparkle_color,
    )

    arr = np.array(out)
    # Clip the painted content back to the teal-glass silhouette: no streak escapes the canopy.
    clipped = base.copy()
    paint = glass & ~protected
    clipped[paint] = arr[paint]
    return clipped


def build_sheet(state: int, key: np.ndarray, rim_mask: np.ndarray, protected: np.ndarray) -> Image.Image:
    cell0 = make_lights_off(key, rim_mask, protected)
    if state == 0:
        cell1 = draw_glint(key, "left", protected)
        cell2 = draw_glint(cell0, "right", protected)
    elif state == 1:
        cell1 = draw_glint(key, "left", protected)
        cell2 = draw_glint(make_lights_off(key, rim_mask, protected), "right", protected)
    else:
        cell1 = draw_glint(key, "left", protected)
        cell2 = draw_glint(make_lights_off(key, rim_mask, protected), "right", protected)

    sheet = Image.new("RGB", (SIZE * 3, SIZE * 2), GREEN)
    for index, cell in enumerate((cell0, cell1, cell2)):
        sheet.paste(Image.fromarray(cell), ((index % 3) * SIZE, (index // 3) * SIZE))
    return sheet


def main() -> None:
    keys = [load_key(i) for i in range(3)]
    rim = light_mask(keys[0])
    sheets = []
    for state, key in enumerate(keys):
        protected = np.zeros((SIZE, SIZE), dtype=bool)
        if state > 0:
            protected = np.abs(key.astype(np.int16) - keys[0].astype(np.int16)).sum(axis=2) > 60
        sheet = build_sheet(state, key, rim, protected)
        path = ROOT / f"hull_redo_state{state}.png"
        sheet.save(path)
        sheets.append(sheet)

    # A compact review strip: three sheets side by side, nearest-neighbour only.
    thumb_size = (SIZE * 3 // 2, SIZE)
    pad = 24
    preview = Image.new("RGB", (thumb_size[0] * 3 + pad * 4, thumb_size[1] + pad * 2), PREVIEW_BG)
    for index, sheet in enumerate(sheets):
        thumb = sheet.resize(thumb_size, Image.Resampling.NEAREST)
        preview.paste(thumb, (pad + index * (thumb_size[0] + pad), pad))
    preview.save(ROOT / "preview_j1b.png")


if __name__ == "__main__":
    main()
