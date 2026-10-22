#!/usr/bin/env python3
"""Build the J1b GoldWarden pose sheets from approved keys and painted bank candidates."""

from collections import deque
from pathlib import Path

from PIL import Image, ImageDraw
import numpy as np


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)
CANVAS = (1024, 1024)


def load(path: Path) -> np.ndarray:
    with Image.open(path) as image:
        return np.array(image.convert("RGB"))


def save(path: Path, array: np.ndarray) -> None:
    Image.fromarray(array.astype(np.uint8), "RGB").save(path)


def clean_candidate(path: Path) -> np.ndarray:
    """Nearest-neighbour fit plus exact-green flood cleanup; no smoothing or blur."""
    with Image.open(path) as image:
        image = image.convert("RGB").resize(CANVAS, Image.Resampling.NEAREST)
        array = np.array(image)

    red, green, blue = [array[:, :, i].astype(np.int16) for i in range(3)]
    likely_bg = (green > 80) & (green > red + 30) & (green > blue + 30)
    seen = np.zeros(likely_bg.shape, dtype=bool)
    queue: deque[tuple[int, int]] = deque()
    h, w = likely_bg.shape
    for x in range(w):
        if likely_bg[0, x]:
            queue.append((0, x))
        if likely_bg[h - 1, x]:
            queue.append((h - 1, x))
    for y in range(h):
        if likely_bg[y, 0]:
            queue.append((y, 0))
        if likely_bg[y, w - 1]:
            queue.append((y, w - 1))
    while queue:
        y, x = queue.popleft()
        if seen[y, x] or not likely_bg[y, x]:
            continue
        seen[y, x] = True
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                ny, nx = y + dy, x + dx
                if 0 <= ny < h and 0 <= nx < w and not seen[ny, nx] and likely_bg[ny, nx]:
                    queue.append((ny, nx))
    array[seen] = GREEN
    return array


def polygon_mask(points: list[tuple[int, int]]) -> np.ndarray:
    mask = Image.new("1", CANVAS, 0)
    ImageDraw.Draw(mask).polygon(points, fill=1)
    return np.array(mask, dtype=bool)


CANOPY = polygon_mask([
    (512, 178), (480, 216), (468, 350), (484, 399),
    (512, 424), (540, 404), (562, 350), (553, 216),
])

LIGHT_REGIONS = [
    polygon_mask([(84, 637), (114, 607), (146, 637), (114, 669)]),
    polygon_mask([(878, 637), (908, 607), (940, 637), (908, 669)]),
    polygon_mask([(474, 606), (550, 606), (512, 671)]),
    polygon_mask([(474, 674), (550, 674), (512, 740)]),
]


def canopy_teal(array: np.ndarray) -> np.ndarray:
    r, g, b = [array[:, :, i].astype(np.int16) for i in range(3)]
    return CANOPY & (b > r + 25) & (g > r + 15) & (b > 100)


def dim_canopy(array: np.ndarray) -> None:
    mask = canopy_teal(array)
    values = array[mask].astype(np.float32) * 0.86
    array[mask] = np.clip(values, 0, 255).astype(np.uint8)


def lights_off(array: np.ndarray) -> None:
    for region in LIGHT_REGIONS:
        r, g, b = [array[:, :, i] for i in range(3)]
        bright = region & ((r.astype(np.int16) + g.astype(np.int16) + b.astype(np.int16)) > 140)
        maroon = (r.astype(np.int16) > b.astype(np.int16))
        array[bright & maroon] = np.array([58, 18, 28], dtype=np.uint8)
        array[bright & ~maroon] = np.array([16, 25, 43], dtype=np.uint8)
    dim_canopy(array)


def remove_greenish_ship_pixels(array: np.ndarray) -> None:
    """Remove small green/olive fringe pixels while leaving the flat green backdrop intact."""
    r, g, b = [array[:, :, i].astype(np.int16) for i in range(3)]
    fringe = np.any(array != GREEN, axis=2) & (g > r + 35) & (g > b + 35)
    array[fringe] = np.array([24, 32, 48], dtype=np.uint8)


def line_distance(points: np.ndarray, a: tuple[int, int], b: tuple[int, int]) -> np.ndarray:
    ax, ay = a
    bx, by = b
    vx, vy = bx - ax, by - ay
    denom = float(vx * vx + vy * vy)
    px, py = points[:, 1] - ax, points[:, 0] - ay
    t = np.clip((px * vx + py * vy) / denom, 0.0, 1.0)
    dx = px - t * vx
    dy = py - t * vy
    return np.sqrt(dx * dx + dy * dy)


def add_glint(array: np.ndarray, side: str, strong: bool) -> None:
    yy, xx = np.indices(CANVAS)
    points = np.stack([yy.ravel(), xx.ravel()], axis=1)
    if side == "left":
        a, b = (476, 374), (516, 235)
    else:
        a, b = (541, 365), (558, 286)
    distance = line_distance(points, a, b).reshape(CANVAS)
    if strong:
        # Broad stepped cyan bloom plus a narrow white-hot core, all clipped to glass.
        glow = CANOPY & (distance <= 48)
        array[glow] = np.array([44, 190, 226], dtype=np.uint8)
        array[CANOPY & (distance <= 25)] = np.array([102, 232, 243], dtype=np.uint8)
        array[CANOPY & (distance <= 4)] = np.array([245, 253, 253], dtype=np.uint8)
        tip = polygon_mask([(508, 228), (516, 218), (523, 229), (516, 242)])
        array[tip & CANOPY] = np.array([255, 251, 224], dtype=np.uint8)
        for dx, dy in ((-10, 0), (10, 0), (0, -10), (0, 10)):
            sparkle = polygon_mask([(516 + dx - 2, 230 + dy), (516 + dx + 2, 230 + dy),
                                    (516 + dx, 230 + dy - 2), (516 + dx, 230 + dy + 2)])
            array[sparkle & CANOPY] = np.array([255, 251, 224], dtype=np.uint8)
    else:
        glow = CANOPY & (distance <= 13)
        array[glow] = np.array([29, 145, 187], dtype=np.uint8)
        array[CANOPY & (distance <= 2)] = np.array([205, 244, 244], dtype=np.uint8)


def build_state(index: int, left_bank: np.ndarray, right_bank: np.ndarray) -> None:
    key = load(ROOT / f"key_state{index}.png")
    cell0 = key.copy()
    lights_off(cell0)
    cell1 = key.copy()
    add_glint(cell1, "left", strong=True)
    cell2 = key.copy()
    lights_off(cell2)
    add_glint(cell2, "right", strong=False)
    remove_greenish_ship_pixels(cell0)
    remove_greenish_ship_pixels(cell1)
    remove_greenish_ship_pixels(cell2)
    remove_greenish_ship_pixels(left_bank)
    remove_greenish_ship_pixels(right_bank)
    empty = np.empty_like(key)
    empty[:] = GREEN

    sheet = np.empty((2048, 3072, 3), dtype=np.uint8)
    for col, cell in enumerate((cell0, cell1, cell2)):
        sheet[0:1024, col * 1024:(col + 1) * 1024] = cell
    for col, cell in enumerate((left_bank, right_bank, empty)):
        sheet[1024:2048, col * 1024:(col + 1) * 1024] = cell
    save(ROOT / f"hull_redo_state{index}.png", sheet)


def shape_bank(key: np.ndarray, candidate: np.ndarray, side: str, state: int) -> np.ndarray:
    """Keep generated paint, while using the approved key as a clean silhouette guardrail."""
    key_mask = np.any(key != GREEN, axis=2)
    candidate_mask = np.any(candidate != GREEN, axis=2)
    yy, xx = np.indices(CANVAS)
    key_row = key_mask[560]
    left_width = 512 - np.where(key_row[:512])[0].min()
    right_width = np.where(key_row[513:])[0].max() + 1
    profile = np.clip(1.0 - np.abs(yy.astype(np.float32) - 560.0) / 220.0, 0.0, 1.0)
    if side == "left":
        left_ratio = 0.85 if state == 2 else 0.80
        boundary = 512 - left_width * (1.0 - (1.0 - left_ratio) * profile)
        cut = xx < boundary
    else:
        right_ratio = 0.72 if state == 2 else 0.71
        boundary = 512 + right_width * (1.0 - (1.0 - right_ratio) * profile)
        cut = xx > boundary
    target = key_mask & ~cut
    result = np.empty_like(key)
    result[:] = GREEN
    use_candidate = target & candidate_mask
    result[use_candidate] = candidate[use_candidate]
    result[target & ~candidate_mask] = key[target & ~candidate_mask]
    return result


def main() -> None:
    banks = {}
    for state in range(3):
        key = load(ROOT / f"key_state{state}.png")
        left = clean_candidate(ROOT / "generated_candidates" / f"state{state}_bankL.png")
        right = clean_candidate(ROOT / "generated_candidates" / f"state{state}_bankR.png")
        banks[state] = (
            shape_bank(key, left, "left", state),
            shape_bank(key, right, "right", state),
        )
    for state in range(3):
        build_state(state, *banks[state])

    # Compact dark preview: one sheet per row, preserving the delivered sheet layout.
    preview = Image.new("RGB", (1536, 1024), (11, 11, 26))
    for i in range(3):
        with Image.open(ROOT / f"hull_redo_state{i}.png") as image:
            image = image.resize((480, 320), Image.Resampling.NEAREST)
            preview.paste(image, (16 + i * 512, 352))
    preview.save(ROOT / "preview_j1b.png")


if __name__ == "__main__":
    main()
