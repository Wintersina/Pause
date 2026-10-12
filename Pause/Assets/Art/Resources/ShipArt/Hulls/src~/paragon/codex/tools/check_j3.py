"""Validate Paragon J3 sprite sheets and print a concise inspection table."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = np.array((0, 255, 0), dtype=np.uint8)
SHEETS = (
    ("charge_16.png", 4, 4, ()),
    ("ready_shot_impact.png", 4, 4, ()),
    ("muzzle_release_trail.png", 4, 3, (10, 11)),
    ("fx_16.png", 4, 4, ()),
)


def span(mask: np.ndarray) -> tuple[int, int]:
    ys, xs = np.where(mask)
    return int(xs.max() - xs.min() + 1), int(ys.max() - ys.min() + 1)


def check_colours(a: np.ndarray, name: str, *, allow_red: bool = False) -> None:
    r, g, b = (a[:, :, k].astype(np.int16) for k in range(3))
    assert np.all(a[0, 0] == GREEN), f"{name}: background is not pure #00FF00"
    assert len(np.unique(a.reshape(-1, 3), axis=0)) <= 40, \
        f"{name}: too many colours for stepped pixel-art ramps"
    pink = (r > g * 1.3) & (b > g * 1.3) & (r > 100) & (b > 100)
    assert not pink.any(), f"{name}: pink or magenta pixels found"
    if not allow_red:
        red = (r > g * 1.3) & (r > b * 1.3) & (r > 100)
        assert not red.any(), f"{name}: red pixels found in blue weapon FX"


def inspect_sheet(name: str, cols: int, rows: int,
                  empty: tuple[int, ...]) -> tuple[str, str, str, str, str]:
    path = ROOT / name
    assert path.is_file(), f"missing {name}"
    image = Image.open(path)
    assert image.mode == "RGB", f"{name}: expected flat RGB chroma-key image"
    width, height = image.size
    assert width % cols == height % rows == 0, f"{name}: uneven grid"
    cell = width // cols
    assert cell == 256, f"{name}: expected 256 px square cells"
    assert (width, height) == (cols * cell, rows * cell)
    a = np.asarray(image)
    check_colours(a, name)
    present = 0
    bounds = []
    for frame in range(cols * rows):
        x, y = (frame % cols) * cell, (frame // cols) * cell
        tile = a[y:y + cell, x:x + cell]
        mask = np.any(tile != GREEN, axis=2)
        pixels = int(mask.sum())
        if frame in empty:
            assert pixels == 0, f"{name} frame {frame}: spare cell has content"
        else:
            assert pixels > 100, f"{name} frame {frame}: missing art"
            assert not (mask[:4].any() or mask[-4:].any() or
                        mask[:, :4].any() or mask[:, -4:].any()), \
                f"{name} frame {frame}: content touches cell edge"
            present += 1
            bounds.append(span(mask))
    if name == "ready_shot_impact.png":
        assert all(55 <= bounds[f][1] <= 70 for f in range(4, 8)), \
            "shot flight should occupy about 25% of cell height"
        assert all(72 <= bounds[f][1] <= 88 for f in (8, 9)), \
            "smear should be about 25% longer than shot"
        assert 170 <= max(bounds[12]) <= 190, \
            "largest impact should occupy about 70% of cell"
    return (name, f"{width}x{height}", f"{cols}x{rows} @ {cell}",
            f"{present}/{cols * rows}", ",".join(map(str, empty)) or "—")


def inspect_gun() -> tuple[str, str, str, str, str]:
    path = ROOT / "gun_tile.png"
    assert path.is_file(), "missing gun_tile.png"
    image = Image.open(path)
    assert image.mode == "RGB" and image.size == (1024, 1024)
    a = np.asarray(image)
    check_colours(a, "gun_tile.png", allow_red=True)
    mask = np.any(a != GREEN, axis=2)
    assert mask.sum() > 100_000, "gun tile is empty"
    assert not (mask[:12].any() or mask[-12:].any() or
                mask[:, :12].any() or mask[:, -12:].any()), \
        "gun touches canvas edge"
    return ("gun_tile.png", "1024x1024", "single tile", "1/1", "—")


def main() -> None:
    rows = [inspect_sheet(*spec) for spec in SHEETS]
    rows.append(inspect_gun())
    preview = ROOT / "preview_j3.png"
    assert preview.is_file(), "missing preview_j3.png"
    assert Image.open(preview).size == (1664, 1120)
    print(f"{'FILE':<27} {'SIZE':<11} {'GRID':<13} {'FILLED':<8} EMPTY")
    print("-" * 72)
    for row in rows:
        print(f"{row[0]:<27} {row[1]:<11} {row[2]:<13} {row[3]:<8} {row[4]}")
    print("Preview: 1664x1120 on #0b0b1a — PASS")


if __name__ == "__main__":
    main()
