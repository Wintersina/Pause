#!/usr/bin/env python3
"""Assemble J1b sheets from approved keys and painted generation candidates.

The source candidates are only cropped, uniformly nearest-neighbour resized, and
packed. Key cells remain exact except for the explicitly painted light/glint
patches.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


GREEN = np.array([0, 255, 0], dtype=np.uint8)
DARK = np.array([11, 11, 26], dtype=np.uint8)


def rgb(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"))


def green_mask(a: np.ndarray) -> np.ndarray:
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    return (g > 70) & (g > r * 1.3) & (g > b * 1.3)


def resize_candidate(path: Path) -> np.ndarray:
    im = Image.open(path).convert("RGB")
    return np.asarray(im.resize((1024, 1024), Image.Resampling.NEAREST))


def subject_crop(path: Path) -> tuple[np.ndarray, np.ndarray]:
    a = rgb(path)
    m = ~green_mask(a)
    ys, xs = np.where(m)
    box = (int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1))
    return a[box[1] : box[3], box[0] : box[2]], m[box[1] : box[3], box[0] : box[2]]


def bank_canvas(path: Path, target_width: int = 590) -> np.ndarray:
    subject, mask = subject_crop(path)
    scale = target_width / subject.shape[1]
    target_height = max(1, round(subject.shape[0] * scale))
    subject_im = Image.fromarray(subject).resize((target_width, target_height), Image.Resampling.NEAREST)
    mask_im = Image.fromarray((mask * 255).astype(np.uint8)).resize(
        (target_width, target_height), Image.Resampling.NEAREST
    )
    subject = np.asarray(subject_im)
    mask = np.asarray(mask_im) > 0
    canvas = np.empty((1024, 1024, 3), dtype=np.uint8)
    canvas[:] = GREEN
    x = (1024 - target_width) // 2
    y = (1024 - target_height) // 2
    region = canvas[y : y + target_height, x : x + target_width]
    region[mask] = subject[mask]
    return canvas


def key_canopy_mask(key: np.ndarray) -> np.ndarray:
    r, g, b = key[..., 0], key[..., 1], key[..., 2]
    roi = np.zeros(key.shape[:2], dtype=bool)
    roi[70:530, 410:615] = True
    return roi & (b > 70) & (g > 80) & (g > r * 1.15) & (b > r * 1.15)


def apply_light_patch(key: np.ndarray, generated: np.ndarray) -> np.ndarray:
    out = key.copy()
    r, g, b = key[..., 0], key[..., 1], key[..., 2]
    # The five running-light groups, located from the approved key rather than
    # inferred from the generated hull.
    red = (r > 120) & (r > g * 1.45) & (r > b * 1.35)
    red &= ((np.indices(red.shape)[0] > 590) & (np.indices(red.shape)[0] < 700)) | (
        (np.indices(red.shape)[0] > 760) & (np.indices(red.shape)[0] < 900)
    )
    gold = (r > 150) & (g > 70) & (g < r * 0.9) & (b < 100)
    yy, xx = np.indices(gold.shape)
    gold &= (xx > 475) & (xx < 550) & (yy > 665) & (yy < 755)
    canopy = key_canopy_mask(key)
    patch = red | gold | canopy
    out[patch] = generated[patch]
    return out


def apply_glint_patch(key: np.ndarray, generated: np.ndarray, reference: np.ndarray) -> np.ndarray:
    out = key.copy()
    # The generated edit repaints the canopy's crystal facets around the glint.
    # Include the crystal and its stepped frame, but keep the hull key-exact.
    mask_im = Image.new("1", (1024, 1024), 0)
    draw = ImageDraw.Draw(mask_im)
    draw.polygon([(449, 93), (515, 70), (574, 118), (579, 210), (546, 264), (468, 228), (445, 164)], fill=1)
    draw.polygon([(448, 257), (568, 257), (603, 329), (592, 474), (548, 538), (468, 535), (426, 470), (419, 329)], fill=1)
    patch = np.asarray(mask_im, dtype=bool)
    out[patch] = generated[patch]
    return out


def make_sheet(state: int, key_path: Path, off_path: Path, glint1_path: Path, glint2_path: Path, left_path: Path, right_path: Path) -> Image.Image:
    key = rgb(key_path)
    off = resize_candidate(off_path)
    glint1 = resize_candidate(glint1_path)
    glint2 = resize_candidate(glint2_path)
    reference = rgb(Path(__file__).resolve().parents[1] / "key_state0.png")

    cell0 = apply_light_patch(key, off)
    cell1 = apply_glint_patch(key, glint1, reference)
    cell2 = apply_glint_patch(cell0, glint2, reference)
    empty = np.tile(GREEN, (1024, 1024, 1))
    cells = [cell0, cell1, cell2, bank_canvas(left_path), bank_canvas(right_path), empty]

    sheet = np.empty((2048, 3072, 3), dtype=np.uint8)
    sheet[:] = GREEN
    for i, cell in enumerate(cells):
        row, col = divmod(i, 3)
        sheet[row * 1024 : (row + 1) * 1024, col * 1024 : (col + 1) * 1024] = cell
    return Image.fromarray(sheet, "RGB")


def preview(sheets: list[Image.Image], out_path: Path) -> None:
    thumb_size = (480, 320)
    canvas = Image.new("RGB", (len(sheets) * thumb_size[0] + (len(sheets) + 1) * 12, 344), tuple(DARK.tolist()))
    for i, sheet in enumerate(sheets):
        a = np.asarray(sheet).copy()
        a[green_mask(a)] = DARK
        thumb = Image.fromarray(a).resize(thumb_size, Image.Resampling.NEAREST)
        canvas.paste(thumb, (12 + i * (thumb_size[0] + 12), 12))
    canvas.save(out_path)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--generated", type=Path, required=True, help="directory containing generated candidate PNGs")
    args = ap.parse_args()
    root = Path(__file__).resolve().parents[1]
    gen = args.generated
    files = {
        "off": gen / "lights_off.png",
        "glint1": gen / "glint1.png",
        "glint2": gen / "glint2.png",
        "left0": gen / "bank_left_state0.png",
        "right0": gen / "bank_right_state0.png",
        "left1": gen / "bank_left_state1.png",
        "right1": gen / "bank_right_state1.png",
        "left2": gen / "bank_left_state2.png",
        "right2": gen / "bank_right_state2.png",
    }
    sheets = []
    for state in range(3):
        sheet = make_sheet(
            state,
            root / f"key_state{state}.png",
            files["off"],
            files["glint1"],
            files["glint2"],
            files[f"left{state}"],
            files[f"right{state}"],
        )
        sheet.save(root / f"hull_redo_state{state}.png")
        sheets.append(sheet)
    preview(sheets, root / "preview_j1b.png")


if __name__ == "__main__":
    main()
