#!/usr/bin/env python3
"""Build the IonLancer damaged and wreck pose sheets from approved references.

The bank cells start from the approved healthy bank pixels. Generated damage
rasters are projected onto those cells and clipped to the healthy silhouette.
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
GREEN = np.array([0, 255, 0], dtype=np.uint8)
INK = (5, 6, 12)
NAVY = (12, 24, 52)
WHITE_HOT = (245, 253, 253)
WHITE_WARM = (255, 251, 224)

GENERATED = Path("/Users/sina/.codex/generated_images/01a160c4-5e2f-7833-9d3c-d8bd29a92166")
GEN_STATE1 = GENERATED / "exec-b3271999-67ec-44c0-b09d-4225c368d0ba.png"
GEN_STATE2 = GENERATED / "exec-481cfd75-08b1-46b4-8af2-dbe1fa5ab9e0.png"


def rgb(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.uint8)


def exact_green(a: np.ndarray) -> np.ndarray:
    out = a.copy()
    r, g, b = [out[:, :, i].astype(np.int16) for i in range(3)]
    bg = (g > 160) & (g > r * 1.35) & (g > b * 1.35)
    out[bg] = GREEN
    return out


def polygon_mask(points: list[tuple[int, int]]) -> np.ndarray:
    mask = Image.new("1", (1024, 1024), 0)
    ImageDraw.Draw(mask).polygon(points, fill=1)
    return np.asarray(mask, dtype=bool)


def darken_running_lights(im: np.ndarray) -> np.ndarray:
    out = im.copy()
    yy, xx = np.mgrid[0:1024, 0:1024]
    slabs = polygon_mask([(249, 695), (294, 668), (302, 721), (258, 750)]).copy()
    slabs |= polygon_mask([(722, 669), (774, 695), (766, 749), (720, 721)])
    tips = polygon_mask([(137, 864), (151, 879), (137, 895), (123, 879)]).copy()
    tips |= polygon_mask([(886, 864), (900, 879), (886, 895), (872, 879)])
    lights = slabs | tips
    out[lights] = np.array(NAVY, dtype=np.uint8)
    out[slabs & (((xx + yy) % 17) < 2)] = (24, 42, 78)
    out[tips & (((xx + yy) % 7) < 2)] = (20, 36, 66)
    top = (yy >= 84) & (yy <= 125) & (xx >= 497) & (xx <= 527)
    bright = top & (out[:, :, 0] > 145) & (out[:, :, 1] > 145) & (out[:, :, 2] > 145)
    out[bright] = np.clip(out[bright].astype(np.float32) * 0.58, 0, 255).astype(np.uint8)
    return out


def canopy_mask() -> np.ndarray:
    yy, xx = np.mgrid[0:1024, 0:1024]
    return (((xx - 512.0) / 58.0) ** 2 + ((yy - 337.0) / 132.0) ** 2) <= 1.0


def dim_canopy(im: np.ndarray) -> np.ndarray:
    out = im.copy()
    m = canopy_mask()
    out[m] = np.clip(out[m].astype(np.float32) * np.array((0.72, 0.83, 0.86)), 0, 255).astype(np.uint8)
    return out


def brighten_canopy(im: np.ndarray) -> np.ndarray:
    out = im.copy()
    m = canopy_mask()
    out[m] = np.clip(out[m].astype(np.int16) + np.array((18, 38, 34)), 0, 255).astype(np.uint8)
    return out


def add_glint(im: np.ndarray, side: str, strong: bool) -> np.ndarray:
    out = Image.fromarray(im, "RGB")
    d = ImageDraw.Draw(out)
    if side == "left":
        pts = [(470 + round(i * 0.25), 414 - i) for i in range(155)]
        d.line(pts, fill=WHITE_HOT if strong else (175, 225, 230), width=4 if strong else 2)
        if strong:
            d.line([(x - 1, y) for x, y in pts], fill=WHITE_WARM, width=2)
            d.rectangle((506, 257, 512, 261), fill=WHITE_WARM)
            d.rectangle((509, 254, 511, 264), fill=WHITE_WARM)
            d.point((510, 259), fill=(255, 255, 255))
    else:
        pts = [(520 + round(i * 0.18), 405 - i) for i in range(140)]
        d.line(pts, fill=(145, 205, 215), width=2)
        d.point((545, 268), fill=(220, 235, 220))
    return np.asarray(out, dtype=np.uint8)


def front_cells(key: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    cell0 = darken_running_lights(dim_canopy(key))
    cell1 = add_glint(darken_running_lights(brighten_canopy(key)), "left", True)
    cell2 = add_glint(darken_running_lights(dim_canopy(key)), "right", False)
    return cell0, cell1, cell2


def generated_damage_mask(a: np.ndarray, wreck: bool) -> np.ndarray:
    r, g, b = [a[:, :, i].astype(np.int16) for i in range(3)]
    orange = (r > 100) & (g > 20) & (g < 190) & (b < 100) & (r > g + 35)
    dark = (r < 70) & (g < 70) & (b < 70)
    if not wreck:
        roi = np.zeros(dark.shape, dtype=bool)
        roi[570:1160, 110:470] = True
        dark &= roi
        orange &= roi
    return orange | dark


def project_damage(base: np.ndarray, generated_path: Path, bank: int, wreck: bool) -> np.ndarray:
    source = exact_green(rgb(generated_path))
    source_im = Image.fromarray(source, "RGB").resize((1024, 1024), Image.Resampling.NEAREST)
    source = np.asarray(source_im, dtype=np.uint8)
    damage = generated_damage_mask(source, wreck)

    # Project the generated front-pose damage into the approved bank camera.
    # The final paint is clipped to the healthy bank mask; geometry is never
    # taken from the generated raster.
    cx = 470 if bank == 3 else 645
    sx = 0.74
    sy = 0.88
    yoff = 36
    yy, xx = np.mgrid[0:1024, 0:1024]
    src_x = np.rint(512.0 + (xx - cx) / sx).astype(np.int32)
    src_y = np.rint((yy - yoff) / sy).astype(np.int32)
    valid = (src_x >= 0) & (src_x < 1024) & (src_y >= 0) & (src_y < 1024)
    projected = np.zeros(valid.shape, dtype=bool)
    projected[valid] = damage[src_y[valid], src_x[valid]]

    healthy_mask = np.any(base != GREEN, axis=2)
    # Keep the bank's outer silhouette and all base pixels intact.
    paint = projected & healthy_mask
    out = base.copy()
    out[paint] = source[src_y[paint], src_x[paint]]
    return out


def build_sheet(state: int) -> np.ndarray:
    key = exact_green(rgb(ROOT / f"key_state{state}.png"))
    healthy = exact_green(rgb(ROOT / "hull_redo_state0.png"))
    cells = list(front_cells(key))
    for bank, x in ((3, 0), (4, 1024)):
        base = healthy[1024:2048, x:x + 1024].copy()
        gen = GEN_STATE1 if state == 1 else GEN_STATE2
        cells.append(project_damage(base, gen, bank, state == 2))

    sheet = np.zeros((2048, 3072, 3), dtype=np.uint8)
    sheet[:] = GREEN
    for i, cell in enumerate(cells):
        sheet[(i // 3) * 1024:(i // 3 + 1) * 1024, (i % 3) * 1024:(i % 3 + 1) * 1024] = cell
    Image.fromarray(sheet, "RGB").save(ROOT / f"hull_redo_state{state}_v2.png", format="PNG", optimize=False)
    return sheet


def make_preview(sheets: list[np.ndarray]) -> None:
    preview = Image.new("RGB", (3072, 1024), (11, 11, 26))
    for i, sheet in enumerate(sheets):
        im = Image.fromarray(sheet, "RGB").resize((1536, 1024), Image.Resampling.NEAREST)
        arr = np.asarray(im).copy()
        arr[np.all(arr == GREEN, axis=2)] = (11, 11, 26)
        preview.paste(Image.fromarray(arr, "RGB"), (i * 1536, 0))
    preview.save(ROOT / "preview_redo12.png", format="PNG", optimize=False)


if __name__ == "__main__":
    make_preview([build_sheet(1), build_sheet(2)])
