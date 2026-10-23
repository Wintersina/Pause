#!/usr/bin/env python3
"""Build the J2 Ninja spin-effect flipbooks on a native pixel grid.

The art is deliberately painted at 128 px per cell, then enlarged 4x with
nearest-neighbour.  That keeps the bands crisp and makes the palette exact.
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SCALE = 4
GRID = 128
CELL = GRID * SCALE
FRAMES = 8

GREEN = (0, 255, 0)
INDIGO = (0x4A, 0x58, 0xC8)
LAVENDER = (0x9F, 0xA8, 0xF0)
BONE = (0xF4, 0xEA, 0xD4)
NAVY = (0x23, 0x2A, 0x6E)
RED = (0xD8, 0x23, 0x2C)
EFFECT_PALETTE = (INDIGO, LAVENDER, BONE, NAVY, RED)


def point(cx: float, cy: float, radius: float, angle: float) -> tuple[float, float]:
    return cx + math.cos(angle) * radius, cy + math.sin(angle) * radius


def arc_points(cx: float, cy: float, radius: float, start: float, end: float, count: int = 24) -> list[tuple[float, float]]:
    return [point(cx, cy, radius, start + (end - start) * i / (count - 1)) for i in range(count)]


def ribbon_arc(
    draw: ImageDraw.ImageDraw,
    cx: float,
    cy: float,
    radius: float,
    start: float,
    end: float,
    outer_width: float,
    inner_width: float,
    colour: tuple[int, int, int],
    count: int = 24,
) -> None:
    """Fill a tapered curved ribbon, with pointed ends and no antialiasing."""
    points: list[list[tuple[float, float]]] = []
    for i in range(count):
        t = i / (count - 1)
        angle = start + (end - start) * t
        # Tapers are strongest at the points; the middle stays full and chunky.
        taper = min(1.0, t / 0.17, (1.0 - t) / 0.17)
        width = inner_width + (outer_width - inner_width) * taper
        ro = radius + width * 0.5
        ri = radius - width * 0.5
        points.append([point(cx, cy, ro, angle), point(cx, cy, ri, angle)])
    for a, b in zip(points, points[1:]):
        draw.polygon([a[0], b[0], b[1], a[1]], fill=colour)


def arc_band(
    draw: ImageDraw.ImageDraw,
    cx: float,
    cy: float,
    radius: float,
    start: float,
    end: float,
    boost: bool,
    smear: bool,
) -> None:
    """Draw a blade afterimage as dark edge, indigo, lavender, then hot core."""
    if boost:
        widths = (10.0, 8.0, 5.6, 2.8)
    else:
        widths = (8.0, 6.5, 4.0, 1.8)
    if smear:
        widths = tuple(w * 0.88 for w in widths)

    # The outer-to-inner stack makes the bone stripe hug the empty centre.
    ribbon_arc(draw, cx, cy, radius + 1.2, start, end, widths[0], 0.0, NAVY, 30)
    ribbon_arc(draw, cx, cy, radius + 0.2, start, end, widths[1], 0.0, INDIGO, 30)
    ribbon_arc(draw, cx, cy, radius - 1.2, start, end, widths[2], 0.0, LAVENDER, 30)
    ribbon_arc(draw, cx, cy, radius - 2.6, start, end, widths[3], 0.0, BONE, 30)


def tick(draw: ImageDraw.ImageDraw, cx: float, cy: float, radius: float, angle: float, boost: bool, extra: bool = False) -> None:
    """Paint the little red leading-edge identity mark."""
    length = 4.2 if boost else 3.0
    if extra:
        length = 5.0
    x, y = point(cx, cy, radius + 4.0, angle)
    tangent = angle + math.pi / 2.0
    a = (x - math.cos(tangent) * length * 0.5, y - math.sin(tangent) * length * 0.5)
    b = (x + math.cos(tangent) * length * 0.5, y + math.sin(tangent) * length * 0.5)
    draw.line([a, b], fill=RED, width=2 if boost or extra else 1)


def spark(draw: ImageDraw.ImageDraw, x: float, y: float, size: int = 1) -> None:
    draw.point((round(x), round(y)), fill=BONE)
    if size > 1:
        draw.line([(round(x - size), round(y)), (round(x + size), round(y))], fill=LAVENDER, width=1)
        draw.point((round(x), round(y - size)), fill=RED)


def snap_to_palette(image: Image.Image) -> Image.Image:
    """Snap painted pixels to the five effect colours; preserve the green key."""
    pixels = [image.getpixel((x, y)) for y in range(image.height) for x in range(image.width)]
    snapped = []
    for pixel in pixels:
        if pixel == GREEN:
            snapped.append(GREEN)
            continue
        snapped.append(min(EFFECT_PALETTE, key=lambda colour: sum((pixel[i] - colour[i]) ** 2 for i in range(3))))
    result = Image.new("RGB", image.size, GREEN)
    result.putdata(snapped)
    return result


def make_ring(frame: int, boost: bool) -> Image.Image:
    image = Image.new("RGB", (GRID, GRID), GREEN)
    draw = ImageDraw.Draw(image)
    cx = cy = 64.0
    smear = frame == 2
    radius = 54.0 if boost else 53.2
    # The 45-degree phase is the turn of the four-bladed shuriken.
    phase = math.radians(frame * 45.0)
    if smear:
        # Four broad arcs with pinprick gaps: a one-tick near-ring smear.
        span = math.radians(87.0 if boost else 84.0)
    else:
        span = math.radians(63.0 + (3.0 if (frame % 3 == 1) else -2.0 if (frame % 3 == 0) else 0.0))
        if boost:
            span += math.radians(8.0)

    for blade in range(4):
        centre = phase + math.radians(90.0 * blade)
        start = centre - span * 0.5
        end = centre + span * 0.5
        arc_band(draw, cx, cy, radius, start, end, boost, smear)
        # Leading tips point in the direction of travel; smear frames carry
        # an extra tick to make the one-tick hold read as motion.
        tick(draw, cx, cy, radius, end, boost, extra=smear)
        if boost and frame in (1, 4, 7):
            tick(draw, cx, cy, radius, start + span * 0.18, True, extra=True)

    # Detached hot flecks are sparse and stay outside the empty centre.
    if boost:
        flecks = [
            (phase + 0.35, 59.5, 1),
            (phase + math.pi * 0.5 + 0.15, 60.5, 1),
            (phase + math.pi + 0.65, 58.5, 2),
        ]
        if frame in (0, 3, 6):
            flecks.append((phase + 4.9, 57.0, 1))
        for angle, r, size in flecks:
            x, y = point(cx, cy, r, angle)
            spark(draw, x, y, size)

    return snap_to_palette(image).resize((CELL, CELL), Image.Resampling.NEAREST)


def lerp(a: tuple[float, float], b: tuple[float, float], t: float) -> tuple[float, float]:
    return a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t


def cubic(a: tuple[float, float], b: tuple[float, float], c: tuple[float, float], d: tuple[float, float], t: float) -> tuple[float, float]:
    u = 1.0 - t
    return (
        u * u * u * a[0] + 3 * u * u * t * b[0] + 3 * u * t * t * c[0] + t * t * t * d[0],
        u * u * u * a[1] + 3 * u * u * t * b[1] + 3 * u * t * t * c[1] + t * t * t * d[1],
    )


def path_points(points: list[tuple[float, float]], count: int = 22) -> list[tuple[float, float]]:
    # Three cubic sections give the hand-wavering, shuriken-cut motion.
    if len(points) == 4:
        return [cubic(*points, i / (count - 1)) for i in range(count)]
    out: list[tuple[float, float]] = []
    for index in range(len(points) - 1):
        out.append(lerp(points[index], points[index + 1], index / max(1, len(points) - 1)))
    out.append(points[-1])
    return out


def path_band(draw: ImageDraw.ImageDraw, points: list[tuple[float, float]], colour: tuple[int, int, int], width: int) -> None:
    draw.line([(round(x), round(y)) for x, y in points], fill=colour, width=width, joint="curve")


def spiral(draw: ImageDraw.ImageDraw, cx: float, cy: float, radius: float, clockwise: bool, boost: bool) -> None:
    points = []
    turns = 1.45
    direction = 1.0 if clockwise else -1.0
    for i in range(16):
        t = i / 15.0
        angle = direction * turns * math.pi * t
        r = radius * (1.0 - 0.60 * t)
        points.append((cx + math.cos(angle) * r, cy + math.sin(angle) * r))
    path_band(draw, points, NAVY, 5 if boost else 4)
    path_band(draw, points, INDIGO, 3)
    path_band(draw, points, LAVENDER, 2)
    path_band(draw, points[2:], BONE, 1)


def streak(draw: ImageDraw.ImageDraw, points: list[tuple[float, float]], boost: bool) -> None:
    samples = path_points(points, 28)
    path_band(draw, samples, NAVY, 8 if boost else 6)
    path_band(draw, samples, INDIGO, 6 if boost else 4)
    path_band(draw, samples, LAVENDER, 4 if boost else 3)
    path_band(draw, samples, BONE, 2 if boost else 1)


def make_wake(frame: int, boost: bool) -> Image.Image:
    image = Image.new("RGB", (GRID, GRID), GREEN)
    draw = ImageDraw.Draw(image)
    smear = frame == 2
    phase = frame * 0.40
    tail = 124.0 if boost else 96.0

    # A four-sided hot head anchors the wake at the top of the cell.
    head_y = 4
    draw.polygon([(64, head_y), (69, 11), (64, 18), (59, 11)], fill=NAVY)
    draw.polygon([(64, 5), (67, 11), (64, 16), (61, 11)], fill=INDIGO)
    draw.polygon([(64, 7), (65, 11), (64, 14), (63, 11)], fill=LAVENDER)
    draw.point((64, 10), fill=BONE)
    draw.line([(60, 7), (64, 4), (68, 7)], fill=RED, width=1)

    if smear:
        # The one-tick smear is narrow, nearly solid, and runs farther than
        # cruise while preserving a few separated edge cuts.
        path_band(draw, [(64, 10), (63, 40), (64, tail)], NAVY, 7 if boost else 5)
        path_band(draw, [(64, 10), (64, 43), (64, tail)], INDIGO, 5 if boost else 4)
        path_band(draw, [(64, 10), (64, 44), (64, tail)], LAVENDER, 3)
        path_band(draw, [(64, 10), (64, 44), (64, tail - 4)], BONE, 2)
        path_band(draw, [(60, 13), (57, 48), (59, tail - 7)], LAVENDER, 2)
        path_band(draw, [(68, 13), (71, 48), (69, tail - 7)], LAVENDER, 2)
    else:
        wobble = math.sin(phase) * 3.0
        streak(draw, [(64, 11), (64 + wobble, 30), (61 - wobble * 0.4, 62), (63 + wobble, tail)], boost)
        streak(draw, [(61, 14), (55 - wobble, 33), (48 + wobble, 59), (38 + wobble * 0.7, tail - 5)], boost)
        streak(draw, [(67, 14), (73 + wobble, 35), (80 - wobble, 62), (90 - wobble * 0.7, tail - 4)], boost)
        if boost or frame % 2 == 1:
            streak(draw, [(63, 17), (68 - wobble, 42), (66 + wobble, 76), (72 + wobble, tail - 10)], boost)

        spiral(draw, 38 + wobble * 0.7, tail - 4, 7 if boost else 6, clockwise=frame % 2 == 0, boost=boost)
        spiral(draw, 90 - wobble * 0.7, tail - 5, 7 if boost else 6, clockwise=frame % 2 == 1, boost=boost)
        if boost:
            spiral(draw, 72 + wobble, tail - 10, 5, clockwise=True, boost=True)

    # Red ticks are directional sparks along the cut edges.
    tick_y = [24, 45, 67, int(tail - 9)]
    for index, y in enumerate(tick_y):
        x = int(64 + math.sin(phase + index * 1.7) * (5 + index * 3))
        draw.line([(x - 2, y), (x + 2, y - 2)], fill=RED, width=2 if boost else 1)
    if boost:
        for x, y in [(51, 28), (78, 53), (55, 79), (76, 93)]:
            spark(draw, x + int(math.sin(phase + y) * 2), y, 1)

    return snap_to_palette(image).resize((CELL, CELL), Image.Resampling.NEAREST)


def sheet(frames: list[Image.Image]) -> Image.Image:
    result = Image.new("RGB", (CELL * FRAMES, CELL), GREEN)
    for index, frame in enumerate(frames):
        result.paste(frame, (index * CELL, 0))
    return result


def main() -> None:
    outputs = {
        "ring_cruise.png": [make_ring(frame, boost=False) for frame in range(FRAMES)],
        "ring_boost.png": [make_ring(frame, boost=True) for frame in range(FRAMES)],
        "wake_cruise.png": [make_wake(frame, boost=False) for frame in range(FRAMES)],
        "wake_boost.png": [make_wake(frame, boost=True) for frame in range(FRAMES)],
    }
    for name, frames in outputs.items():
        sheet(frames).save(ROOT / name, format="PNG", optimize=False)
    print("wrote", ", ".join(outputs))


if __name__ == "__main__":
    main()
