"""Re-cell selected image-generated J3 paintings with nearest-neighbour pixels only."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
CELL = 256
BASE_PALETTE = (
    "05060c", "140c14", "0b1026", "152043", "1e3261", "2d4a83", "4271a9", "79a1c8",
    "10121e", "232838", "383e4e", "596171", "89919d", "c4cad1",
    "081b70", "103ab0", "1265d9", "188ff2", "43bbfa", "8de6ff",
    "075373", "068eaa", "0bb7d9", "19e5fa", "7af6fc", "b9fbff", "f4ffff",
    "f4ead4", "fffdf1",
)
RED_PALETTE = ("6e101b", "a51924", "d8232c", "ed3040")


def clean_palette(image: Image.Image, *, allow_red: bool = False) -> Image.Image:
    """Snap painted colours to hard material/energy ramps, without dithering."""
    rgb = np.asarray(image.convert("RGB")).copy()
    palette_hex = BASE_PALETTE + (RED_PALETTE if allow_red else ())
    palette = np.array([tuple(bytes.fromhex(c)) for c in palette_hex], dtype=np.int32)
    for top in range(0, rgb.shape[0], 64):
        block = rgb[top:top + 64]
        mask = np.any(block != GREEN, axis=2)
        if not mask.any():
            continue
        pixels = block[mask].astype(np.int32)
        d2 = ((pixels[:, None, :] - palette[None, :, :]) ** 2).sum(axis=2)
        block[mask] = palette[d2.argmin(axis=1)].astype(np.uint8)
    return Image.fromarray(rgb, "RGB")


def fit_painted_motif(tile: Image.Image, extent: int) -> Image.Image:
    """Resize a generated pose within its cell using nearest-neighbour only."""
    a = np.asarray(tile)
    ys, xs = np.where(np.any(a != GREEN, axis=2))
    if not len(xs):
        return tile
    motif = tile.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
    scale = extent / max(motif.size)
    size = (max(1, round(motif.width * scale)), max(1, round(motif.height * scale)))
    motif = motif.resize(size, Image.Resampling.NEAREST)
    out = Image.new("RGB", tile.size, GREEN)
    out.paste(motif, ((tile.width - size[0]) // 2, (tile.height - size[1]) // 2))
    return out


def key_green(image: Image.Image) -> Image.Image:
    a = np.asarray(image.convert("RGB")).copy()
    r, g, b = [a[:, :, k].astype(np.int16) for k in range(3)]
    # Image generation paints tiny variations of chroma green. This hard key
    # also removes its green edge fringe without touching blue/cyan energy.
    keyed = (g > 75) & (g > r * 1.65) & (g > b * 1.35)
    a[keyed] = GREEN
    return Image.fromarray(a, "RGB")


def recell(source: str, columns: int, rows: int, *, inset: int = 2,
           rotate_up: tuple[int, ...] = (), empty: tuple[int, ...] = (),
           extents: dict[int, int] | None = None) -> Image.Image:
    image = Image.open(ROOT / "tools" / source).convert("RGB")
    out = Image.new("RGB", (columns * CELL, rows * CELL), GREEN)
    w, h = image.size
    for index in range(columns * rows):
        if index in empty:
            continue
        col, row = index % columns, index // columns
        left = round(col * w / columns) + inset
        top = round(row * h / rows) + inset
        right = round((col + 1) * w / columns) - inset
        bottom = round((row + 1) * h / rows) - inset
        tile = image.crop((left, top, right, bottom))
        if index in rotate_up:
            tile = tile.transpose(Image.Transpose.ROTATE_270)
        tile = key_green(tile.resize((CELL - 12, CELL - 12), Image.Resampling.NEAREST))
        tile = clean_palette(tile)
        if extents and index in extents:
            tile = fit_painted_motif(tile, extents[index])
        # Keep all animation poses on a common cell scale. Translate only to
        # centre the painted motif, while preserving its generated pixels.
        a = np.asarray(tile)
        ys, xs = np.where(np.any(a != GREEN, axis=2))
        if len(xs):
            dx = round((CELL - 12 - 1 - xs.min() - xs.max()) / 2)
            dy = round((CELL - 12 - 1 - ys.min() - ys.max()) / 2)
            shifted = Image.new("RGB", tile.size, GREEN)
            shifted.paste(tile, (dx, dy))
            tile = shifted
        out.paste(tile, (col * CELL + 6, row * CELL + 6))
    return out


def gun_tile() -> Image.Image:
    source = Image.open(ROOT / "tools" / "j3_painted_gun.png").convert("RGB")
    # Generated painting has square framing. Normalize only its canvas size.
    image = key_green(source.resize((1024, 1024), Image.Resampling.NEAREST))
    return clean_palette(image, allow_red=True)


def preview(assets: dict[str, Image.Image]) -> Image.Image:
    bg = (11, 11, 26)
    board = Image.new("RGB", (1664, 1120), bg)
    places = {
        "charge_16.png": (32, 32),
        "ready_shot_impact.png": (576, 32),
        "fx_16.png": (1120, 32),
        "muzzle_release_trail.png": (32, 576),
        "gun_tile.png": (576, 576),
    }
    for name, (x, y) in places.items():
        im = assets[name]
        im = im.resize((im.width // 2, im.height // 2), Image.Resampling.NEAREST)
        a = np.asarray(im)
        mask = Image.fromarray(np.uint8(np.any(a != GREEN, axis=2)) * 255, "L")
        board.paste(im, (x, y), mask)
    return board


def main() -> None:
    assets = {
        "charge_16.png": recell("j3_painted_charge.png", 4, 4),
        "ready_shot_impact.png": recell(
            "j3_painted_shot.png", 4, 4, inset=8,
            extents={4: 64, 5: 64, 6: 64, 7: 64, 8: 80, 9: 80,
                     10: 136, 11: 165, 12: 180, 13: 155, 14: 100, 15: 75}
        ),
        "muzzle_release_trail.png": recell(
            "j3_painted_muzzle.png", 4, 3, rotate_up=(0, 1, 3), empty=(10, 11)
        ),
        "fx_16.png": recell("j3_painted_fx.png", 4, 4),
        "gun_tile.png": gun_tile(),
    }
    for name, image in assets.items():
        image.save(ROOT / name)
    preview(assets).save(ROOT / "preview_j3.png")


if __name__ == "__main__":
    main()
