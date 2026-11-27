"""Generate the medium star-dust pickup and a local inspection preview.

The art is authored on a small logical pixel grid and enlarged with nearest
neighbour sampling.  This keeps every edge deliberately stepped and avoids
introducing anti-aliased pixels.
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
ART_ROOT = HERE.parent.parent
PICKUP_ROOT = ART_ROOT / "Resources" / "Pickups" / "Atoms"
PREVIEW_PATH = HERE / "dust_medium_preview.png"

# Palette copied from the existing dust pickup family.
INK = (20, 12, 20, 255)
DEEP = (169, 72, 26, 255)
ORANGE = (242, 134, 43, 255)
AMBER = (255, 180, 60, 255)
PALE = (244, 234, 212, 255)
PREVIEW_BG = (20, 12, 20, 255)

SCALE = 4
# The star is drawn on a 25x25 grid (centre cell 12, so it mirrors exactly)
# plus one transparent cell of padding on every side.
STAR_CELLS = 25
PAD = 1
IDLE_LOGICAL = STAR_CELLS + 2 * PAD
IDLE_SIZE = IDLE_LOGICAL * SCALE
BURST_LOGICAL = 53   # odd, so the centre cell (26) is the true middle
BURST_SIZE = BURST_LOGICAL * SCALE


def point(x: float, y: float) -> tuple[int, int]:
    return round(x), round(y)


def transform_polygon(points: list[tuple[float, float]], sx: float, sy: float) -> list[tuple[int, int]]:
    cx = cy = 12
    return [point(PAD + cx + (x - cx) * sx, PAD + cy + (y - cy) * sy) for x, y in points]


def draw_idle_layer(draw: ImageDraw.ImageDraw, points: list[tuple[float, float]], color, sx: float, sy: float) -> None:
    draw.polygon(transform_polygon(points, sx, sy), fill=color)


def _mirrored_outline(quarter: list[tuple[int, int]]) -> list[tuple[int, int]]:
    """Full outline of a star from its top-right quarter (clockwise from the
    top tip to the right tip), mirrored across both axes of the 25-cell grid
    (centre cell 12) so the silhouette is exactly symmetric."""

    right = quarter + [(x, 24 - y) for x, y in reversed(quarter)]
    left = [(24 - x, y) for x, y in reversed(right)]
    return right + left


# Every tip is three cells wide, like the top tip, and sits on the centre
# row/column (the old outline had 4-row side tips one row off the centre and
# ran one cell past the canvas, which clipped the right side and made the left
# arm look chopped).
OUTER_STAR = _mirrored_outline([
    (13, 0), (13, 2), (14, 2), (14, 4), (16, 6), (18, 7), (18, 8),
    (21, 9), (21, 11), (24, 11),
])

INNER_STAR = _mirrored_outline([
    (12, 2), (13, 5), (15, 8), (18, 10), (22, 12),
])


def make_idle(frame: int) -> Image.Image:
    """Make one medium idle frame on a 24x24 logical grid."""

    # The loop narrows the horizontal arms to an edge-on vertical glint and
    # then opens them again, matching the rhythm of the two existing sizes.
    sx = [1.00, 0.78, 1.02, 0.62, 0.18, 0.58, 1.00, 0.58, 0.18, 0.58][frame]
    sy = [1.00, 0.82, 1.00, 0.98, 1.00, 0.98, 1.00, 0.98, 1.00, 0.98][frame]
    image = Image.new("RGBA", (IDLE_LOGICAL, IDLE_LOGICAL), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    draw_idle_layer(draw, OUTER_STAR, INK, sx, sy)
    ink_only = image.copy()
    draw_idle_layer(draw, INNER_STAR, ORANGE, sx, sy)
    # The colour fill: wings and core are clipped to this, so the ink outline
    # stays an unbroken border all the way round (the left arm's amber wing
    # used to run straight over the ink and out to the edge).
    fill_mask = Image.new("L", image.size, 0)
    ImageDraw.Draw(fill_mask).polygon(transform_polygon(INNER_STAR, sx, sy), fill=255)

    # Chunky complementary wing colors preserve the family silhouette while
    # making this larger than the plus-shaped small pickup.
    upper_left = [(0, 11), (6, 9), (9, 7), (12, 8), (12, 12), (5, 12), (0, 13)]
    lower_right = [(12, 11), (15, 12), (18, 12), (22, 11), (18, 14), (15, 16), (12, 16)]
    if frame in (5, 6, 7, 9):
        upper_left, lower_right = lower_right, upper_left
    draw_idle_layer(draw, upper_left, AMBER, sx, sy)
    draw_idle_layer(draw, lower_right, DEEP, sx, sy)

    # A small cross-shaped pale core is the visual bridge between the small
    # plus pickup and the large four-point star.
    core = [(11, 9), (13, 9), (13, 11), (14, 11), (14, 13), (13, 13),
            (13, 15), (11, 15), (11, 13), (10, 13), (10, 11), (11, 11)]
    draw_idle_layer(draw, core, PALE, sx, sy)

    # Wings and core never leave the colour fill; everything outside it is
    # the ink outline.
    image = Image.composite(image, ink_only, fill_mask)
    draw = ImageDraw.Draw(image)

    # Larger pickups can carry the two little twinkle pixels seen in the
    # reference loop without making the medium read as a separate effect.
    if frame == 2:
        draw.rectangle((3, 17, 4, 20), fill=PALE)
        draw.rectangle((2, 18, 5, 19), fill=PALE)
        draw.rectangle((20, 4, 21, 7), fill=PALE)
        draw.rectangle((19, 5, 22, 6), fill=PALE)
    elif frame == 3:
        draw.rectangle((4, 3, 5, 6), fill=PALE)
        draw.rectangle((3, 4, 6, 5), fill=PALE)
        draw.rectangle((19, 17, 20, 20), fill=PALE)
        draw.rectangle((18, 18, 21, 19), fill=PALE)

    return image.resize((IDLE_SIZE, IDLE_SIZE), Image.Resampling.NEAREST)


def four_point(draw: ImageDraw.ImageDraw, cx: int, cy: int, radius: int, color, inner: int | None = None) -> None:
    """Draw a stepped four-point star; all coordinates are logical pixels."""

    shoulder = max(2, radius // 3)
    points = [
        (cx - 1, cy - radius), (cx + 1, cy - radius),
        (cx + shoulder, cy - shoulder), (cx + radius, cy - 1),
        (cx + radius, cy + 1), (cx + shoulder, cy + shoulder),
        (cx + 1, cy + radius), (cx - 1, cy + radius),
        (cx - shoulder, cy + shoulder), (cx - radius, cy + 1),
        (cx - radius, cy - 1), (cx - shoulder, cy - shoulder),
    ]
    draw.polygon(points, fill=color)
    if inner is not None:
        four_point(draw, cx, cy, inner, PALE)


def jagged_burst(draw: ImageDraw.ImageDraw, cx: int, cy: int, radius: int, color) -> None:
    points: list[tuple[int, int]] = []
    for i in range(32):
        angle = -math.pi / 2 + i * math.pi / 16
        r = radius if i % 2 == 0 else max(4, radius - 4)
        points.append(point(cx + math.cos(angle) * r, cy + math.sin(angle) * r))
    draw.polygon(points, fill=color)


def cross(draw: ImageDraw.ImageDraw, cx: int, cy: int, arm: int, color) -> None:
    draw.rectangle((cx - 1, cy - arm, cx + 1, cy + arm), fill=color)
    draw.rectangle((cx - arm, cy - 1, cx + arm, cy + 1), fill=color)


def radial_shard(draw: ImageDraw.ImageDraw, cx: int, cy: int, angle: float, length: int, color, width: int = 2) -> None:
    ux, uy = math.cos(angle), math.sin(angle)
    vx, vy = -uy, ux
    start = length // 2
    end = length
    a = point(cx + ux * start + vx * width, cy + uy * start + vy * width)
    b = point(cx + ux * end + vx * width, cy + uy * end + vy * width)
    c = point(cx + ux * end - vx * width, cy + uy * end - vy * width)
    d = point(cx + ux * start - vx * width, cy + uy * start - vy * width)
    draw.polygon([a, b, c, d], fill=color)


def make_burst(frame: int) -> Image.Image:
    """Make one 208x208 medium collect-burst frame."""

    image = Image.new("RGBA", (BURST_LOGICAL, BURST_LOGICAL), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    cx = cy = BURST_LOGICAL // 2

    if frame == 0:
        four_point(draw, cx, cy, 17, INK)
        four_point(draw, cx, cy, 15, PALE)
    elif frame == 1:
        jagged_burst(draw, cx, cy, 18, INK)
        jagged_burst(draw, cx, cy, 16, ORANGE)
        draw.polygon([(cx - 16, cy), (cx - 5, cy - 14), (cx + 2, cy - 2),
                      (cx - 2, cy + 7), (cx - 11, cy + 15)], fill=AMBER)
        draw.polygon([(cx + 1, cy - 1), (cx + 15, cy - 11), (cx + 16, cy + 10),
                      (cx + 4, cy + 15), (cx - 1, cy + 5)], fill=ORANGE)
        four_point(draw, cx, cy, 8, PALE)
    elif frame == 2:
        draw.ellipse((13, 13, 39, 39), outline=INK, width=3)
        draw.ellipse((14, 14, 38, 38), outline=AMBER, width=2)
        draw.ellipse((24, 24, 28, 28), fill=PALE)
        for dx, dy in ((-16, -16), (16, -16), (-16, 16), (16, 16)):
            cross(draw, cx + dx, cy + dy, 2, PALE)
    elif frame == 3:
        for i in range(8):
            angle = i * math.pi / 4
            radial_shard(draw, cx, cy, angle, 22, INK, 2)
            radial_shard(draw, cx, cy, angle, 20, AMBER, 1)
        for i in range(4):
            angle = math.pi / 4 + i * math.pi / 2
            radial_shard(draw, cx, cy, angle, 26, INK, 2)
            radial_shard(draw, cx, cy, angle, 24, ORANGE, 1)
        for dx, dy in ((-18, -18), (18, -18), (-18, 18), (18, 18)):
            cross(draw, cx + dx, cy + dy, 2, AMBER)
    elif frame == 4:
        for i in range(8):
            angle = i * math.pi / 4
            radial_shard(draw, cx, cy, angle, 23, INK, 2)
            radial_shard(draw, cx, cy, angle, 21, ORANGE, 1)
        for dx, dy in ((-20, -20), (20, -20), (-20, 20), (20, 20)):
            cross(draw, cx + dx, cy + dy, 1, AMBER)
    else:
        for i in range(8):
            angle = i * math.pi / 4
            radial_shard(draw, cx, cy, angle, 23, ORANGE, 1)
        for dx, dy in ((-22, -22), (22, -22), (-22, 22), (22, 22)):
            cross(draw, cx + dx, cy + dy, 1, PALE if frame == 5 else AMBER)

    return image.resize((BURST_SIZE, BURST_SIZE), Image.Resampling.NEAREST)


def paste_centered(canvas: Image.Image, image: Image.Image, x: int, y: int) -> None:
    canvas.alpha_composite(image, (x - image.width // 2, y - image.height // 2))


def make_preview() -> Image.Image:
    """Assemble a true-size comparison and the full medium animation strips."""

    width = 6 * BURST_SIZE + 5 * 12 + 32
    height = 44 + 24 + IDLE_SIZE + 32 + 2 * BURST_SIZE + 40
    preview = Image.new("RGBA", (width, height), PREVIEW_BG)

    # Top comparison: actual 44, 96, and 180 px frame-0 sizes.
    small = Image.open(PICKUP_ROOT / "dustsm_idle_0.png").convert("RGBA")
    large = Image.open(PICKUP_ROOT / "dust_idle_0.png").convert("RGBA")
    y = 100
    centers = [110, 250, 430]
    paste_centered(preview, small, centers[0], y)
    paste_centered(preview, make_idle(0), centers[1], y)
    paste_centered(preview, large, centers[2], y)

    # Medium idle row at 1:1 size.
    idle_y = 235
    for i in range(10):
        preview.alpha_composite(make_idle(i), (16 + i * (IDLE_SIZE + 8), idle_y))

    # Burst row at 1:1 size. It is intentionally allowed to be wider than
    # the idle row so each frame can be inspected without shrinking.
    burst_y = 380
    for i in range(6):
        preview.alpha_composite(make_burst(i), (16 + i * (BURST_SIZE + 12), burst_y))

    return preview


def main() -> None:
    PICKUP_ROOT.mkdir(parents=True, exist_ok=True)
    HERE.mkdir(parents=True, exist_ok=True)
    for i in range(10):
        make_idle(i).save(PICKUP_ROOT / f"dustmd_idle_{i}.png")
    for i in range(6):
        make_burst(i).save(PICKUP_ROOT / f"dustmd_burst_{i}.png")
    make_preview().save(PREVIEW_PATH)


if __name__ == "__main__":
    main()
