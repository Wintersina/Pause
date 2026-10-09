"""Give clipped boss effects a transparent, pixel-stepped cell margin.

Run from the repository root. The archived PNGs are the only input, so reruns
do not accumulate alpha loss. RGB and pixel positions are never resampled.
"""

from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[6]
ORIGINALS = Path(__file__).resolve().parent.parent / "cell_margin_originals"
OUTPUT = Path(__file__).resolve().parent
LIVE = ROOT / "Pause/Assets/Art/Resources/Bosses"
SIZE = 384
NAMES = ("Space", "Frost")
BAYER = np.array(
    [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]],
    dtype=np.float32,
)


def largest_opaque_component(alpha):
    """The connected opaque ship/central fragment, excluding loose debris."""
    opaque = alpha > 200
    seen = np.zeros((SIZE, SIZE), dtype=bool)
    largest = []
    for y, x in zip(*np.nonzero(opaque)):
        if seen[y, x]:
            continue
        seen[y, x] = True
        queue = deque([(int(y), int(x))])
        component = []
        while queue:
            cy, cx = queue.popleft()
            component.append((cy, cx))
            for ny, nx in ((cy - 1, cx), (cy + 1, cx), (cy, cx - 1), (cy, cx + 1)):
                if 0 <= ny < SIZE and 0 <= nx < SIZE and opaque[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    queue.append((ny, nx))
        if len(component) > len(largest):
            largest = component
    mask = np.zeros((SIZE, SIZE), dtype=bool)
    if largest:
        ys, xs = zip(*largest)
        mask[ys, xs] = True
    return mask


def touched_sides(alpha):
    strips = {
        "L": alpha[:, :7],
        "R": alpha[:, -7:],
        "T": alpha[:7, :],
        "B": alpha[-7:, :],
    }
    return "".join(side for side, strip in strips.items() if int(strip.max()) > 24)


def border_max(alpha):
    return max(
        int(alpha[:, :7].max()), int(alpha[:, -7:].max()),
        int(alpha[:7, :].max()), int(alpha[-7:, :].max()),
    )


def fix_cell(before, sides, body, bright_width):
    yy, xx = np.indices((SIZE, SIZE))
    distance = {"L": xx, "R": SIZE - 1 - xx, "T": yy, "B": SIZE - 1 - yy}
    rgb = before[:, :, :3].astype(np.float32)
    bright_effect = (rgb.mean(axis=2) > 115) & (rgb.max(axis=2) > 170)
    # Dense hull pixels get a short five-pixel taper. Soft effects and detached
    # debris get a broad stepped falloff, whose source silhouette remains intact.
    factor = np.ones((SIZE, SIZE), dtype=np.float32)
    for side in sides:
        d = distance[side].astype(np.float32)
        hull = np.clip((d - 6) / 5, 0, 1)
        t = np.clip((d - 6) / 30, 0, 1)
        soft = t * t * (3 - 2 * t)
        bright_t = np.clip((d - 6) / max(bright_width - 6, 1), 0, 1)
        bright = bright_t * bright_t * (3 - 2 * bright_t)
        body_factor = np.where(bright_effect & (bright_width > 0), bright, hull)
        one_side = np.where(body, body_factor, soft)
        factor = np.minimum(factor, one_side)
    # Quantize the multiplier, not the artwork. Bayer steps keep diagonal smoke
    # edges and tiny flames from becoming a translucent, blurred-looking band.
    yy4 = yy % 4
    xx4 = xx % 4
    levels = np.floor(factor * 16 + BAYER[yy4, xx4] / 16).astype(np.uint8)
    levels = np.clip(levels, 0, 16)
    result = before.copy()
    result[:, :, 3] = ((before[:, :, 3].astype(np.uint16) * levels + 8) // 16).astype(np.uint8)
    result[:7, :, 3] = 0
    result[-7:, :, 3] = 0
    result[:, :7, 3] = 0
    result[:, -7:, 3] = 0
    return result


def iou(before, after):
    union = int(np.logical_or(before, after).sum())
    return float(np.logical_and(before, after).sum() / union) if union else 1.0


def composite(cell, color):
    ground = Image.new("RGBA", (SIZE, SIZE), color + (255,))
    ground.alpha_composite(Image.fromarray(cell, "RGBA"))
    return ground.convert("RGB")


def panel(before, after, caption):
    out = Image.new("RGB", (SIZE * 2, SIZE * 2 + 28), (22, 25, 40))
    draw = ImageDraw.Draw(out)
    draw.text((8, 8), caption + "  |  BEFORE", fill=(240, 242, 250))
    draw.text((SIZE + 8, 8), "AFTER", fill=(240, 242, 250))
    for y, color in ((28, (23, 27, 44)), (28 + SIZE, (173, 183, 197))):
        out.paste(composite(before, color), (0, y))
        out.paste(composite(after, color), (SIZE, y))
    return out


def main():
    originals, finished, changed = {}, {}, []
    for name in NAMES:
        src = np.array(Image.open(ORIGINALS / f"{name}.png").convert("RGBA"))
        dst = src.copy()
        assert src.shape[1] == 1920 and src.shape[0] % SIZE == 0
        originals[name] = src
        for row in range(src.shape[0] // SIZE):
            for col in range(5):
                ys = slice(row * SIZE, (row + 1) * SIZE)
                xs = slice(col * SIZE, (col + 1) * SIZE)
                before = src[ys, xs]
                sides = touched_sides(before[:, :, 3])
                if not sides:
                    continue
                body = largest_opaque_component(before[:, :, 3])
                for bright_width in (36, 30, 26, 22, 18, 16, 14, 12, 11, 0):
                    after = fix_cell(before, sides, body, bright_width)
                    if iou(body, body & (after[:, :, 3] > 200)) >= 0.9801:
                        break
                dst[ys, xs] = after
                changed.append((name, row, col, sides, before, after, body))
        finished[name] = dst
        Image.fromarray(dst, "RGBA").save(LIVE / f"{name}.png", optimize=True)

    # At native pixel scale, each panel shows dark and light game backgrounds.
    columns = 5
    rows = (len(changed) + columns - 1) // columns
    preview = Image.new("RGB", (columns * SIZE * 2, rows * (SIZE * 2 + 28)), (17, 20, 34))
    for index, (name, row, col, sides, before, after, _) in enumerate(changed):
        preview.paste(panel(before, after, f"{name}  r{row} c{col}  {sides}"),
                      ((index % columns) * SIZE * 2, (index // columns) * (SIZE * 2 + 28)))
    preview.save(OUTPUT / "preview.png", optimize=True)

    # Flat frame indices: 4 hit, 12 death 0, 19 portrait.
    gif_frames = []
    for name in NAMES:
        for index, label in ((4, "hit"), (12, "death 0"), (19, "portrait")):
            row, col = divmod(index, 5)
            ys = slice(row * SIZE, (row + 1) * SIZE)
            xs = slice(col * SIZE, (col + 1) * SIZE)
            gif_frames.append(panel(originals[name][ys, xs], finished[name][ys, xs],
                                    f"{name}  {label}  frame {index}"))
    gif_frames[0].save(OUTPUT / "preview.gif", save_all=True, append_images=gif_frames[1:],
                       duration=1100, loop=0, optimize=False)

    lines = ["Atlas  Cell  Sides  Max alpha within 6 px  Body IoU  All opaque IoU",
             "Body = largest 4-connected component of original alpha > 200; detached debris excluded."]
    touched = {(name, row, col) for name, row, col, *_ in changed}
    for name, row, col, sides, before, after, body in changed:
        body_after = body & (after[:, :, 3] > 200)
        raw_before = before[:, :, 3] > 200
        raw_after = after[:, :, 3] > 200
        lines.append(f"{name:5}  r{row}c{col}  {sides:4}  {border_max(after[:, :, 3]):3}"
                     f"                  {iou(body, body_after):.4f}    {iou(raw_before, raw_after):.4f}")
    for name in NAMES:
        a, b = originals[name], finished[name]
        for row in range(a.shape[0] // SIZE):
            for col in range(5):
                ys, xs = slice(row*SIZE, (row+1)*SIZE), slice(col*SIZE, (col+1)*SIZE)
                if (name, row, col) not in touched:
                    assert np.array_equal(a[ys, xs], b[ys, xs]), (name, row, col)
                assert border_max(b[ys, xs, 3]) <= 24, (name, row, col)
    lines.append(f"Fixed {len(changed)} cells; untouched cells are byte-identical at the pixel level.")
    (OUTPUT / "verification.txt").write_text("\n".join(lines) + "\n")
    print("\n".join(lines))


if __name__ == "__main__":
    main()
