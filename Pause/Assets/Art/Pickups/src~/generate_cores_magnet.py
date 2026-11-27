#!/usr/bin/env python3
"""Generate the drone-core and magnet pickup flipbooks.

The artwork is authored on the same 49x49 logical pixel grid as the existing
pickup atoms and enlarged with nearest-neighbour sampling.  No Unity metadata
is emitted: these PNGs are deliberately standalone art sources.
"""

from __future__ import annotations

import math
import shutil
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[5]
ATOM_DIR = ROOT / "Pause/Assets/Art/Resources/Pickups/Atoms"
CORE_DIR = ROOT / "Pause/Assets/Art/Resources/Pickups/Cores"
OUT_DIR = Path(__file__).resolve().parent   # previews + the old-magnet reference land beside this script (git-ignored)

GRID = 49
SCALE = 4
SIZE = GRID * SCALE
INK = (8, 10, 28, 255)
INK2 = (13, 18, 39, 255)
SPACE = (14, 20, 36, 255)
METAL_SH = (34, 48, 73, 255)
METAL = (86, 111, 142, 255)
METAL_HI = (175, 202, 219, 255)
WHITE = (246, 240, 216, 255)


CORE_PALETTES = {
    "straight": ((44, 142, 166, 255), (52, 230, 250, 255), (165, 255, 250, 255)),
    "sweep": ((166, 89, 28, 255), (255, 177, 48, 255), (255, 239, 137, 255)),
    "aimed": ((51, 148, 36, 255), (143, 255, 71, 255), (224, 255, 151, 255)),
    "reward": ((174, 112, 24, 255), (255, 205, 62, 255), (255, 243, 151, 255)),
}


def logical() -> Image.Image:
    return Image.new("RGBA", (GRID, GRID), (0, 0, 0, 0))


def rgba(img: Image.Image) -> Image.Image:
    return img.resize((SIZE, SIZE), Image.Resampling.NEAREST)


def polygon(draw: ImageDraw.ImageDraw, pts, fill):
    draw.polygon([(int(round(x)), int(round(y))) for x, y in pts], fill=fill)


def line(draw: ImageDraw.ImageDraw, pts, fill, width=1):
    draw.line([(int(round(x)), int(round(y))) for x, y in pts], fill=fill, width=width, joint="curve")


def octagon(cx, cy, r):
    return [(cx + math.cos(math.radians(-90 + i * 45)) * r,
             cy + math.sin(math.radians(-90 + i * 45)) * r) for i in range(8)]


def point(cx, cy, radius, angle):
    a = math.radians(angle)
    return cx + math.cos(a) * radius, cy + math.sin(a) * radius


def tint_alpha(img: Image.Image, amount: float) -> Image.Image:
    out = img.copy()
    alpha = out.getchannel("A").point(lambda a: int(a * amount))
    out.putalpha(alpha)
    return out


def draw_glint(draw, x, y, color, size=1):
    draw.rectangle((int(x - size), int(y - size), int(x + size), int(y + size)), fill=color)


def draw_core_casing(draw, accent, accent_hi, phase, pulse):
    """Mechanical housing shared by all four cores."""
    cx = cy = 24

    # Detached orbit brackets and the four small corner bolts make the cores
    # read as hardware, rather than as ordinary atom badges.
    start = phase * 360.0
    for offset in (0, 90, 180, 270):
        a0 = start + offset + 14
        a1 = a0 + 40
        box = (5, 5, 43, 43)
        draw.arc(box, int(a0), int(a1), fill=accent, width=1)
    for a in (45, 135, 225, 315):
        x, y = point(cx, cy, 18.8, a)
        draw.rectangle((round(x - 1), round(y - 1), round(x + 1), round(y + 1)), fill=INK)
        draw.point((round(x), round(y)), fill=accent_hi)

    # Outer octagonal shell and shaded inset plates.
    polygon(draw, octagon(cx, cy, 18.2), INK)
    polygon(draw, octagon(cx, cy, 16.3), METAL_SH)
    polygon(draw, [(cx - 11, cy - 14), (cx + 11, cy - 14), (cx + 7, cy - 9),
                   (cx - 7, cy - 9)], METAL_HI)
    polygon(draw, [(cx + 14, cy - 11), (cx + 14, cy + 11), (cx + 9, cy + 7),
                   (cx + 9, cy - 7)], METAL)
    polygon(draw, [(cx - 14, cy + 11), (cx + 14, cy + 11), (cx + 7, cy + 7),
                   (cx - 7, cy + 7)], METAL_SH)
    polygon(draw, [(cx - 14, cy - 11), (cx - 14, cy + 11), (cx - 9, cy + 7),
                   (cx - 9, cy - 7)], METAL)

    # Four articulated fins, all inked and stepped.
    for pts in (
        [(19, 9), (22, 5), (27, 5), (29, 9), (26, 11), (22, 11)],
        [(39, 19), (44, 22), (44, 27), (39, 29), (37, 26), (37, 22)],
        [(19, 39), (22, 37), (27, 37), (29, 39), (27, 44), (22, 44)],
        [(9, 19), (11, 22), (11, 26), (9, 29), (5, 27), (5, 22)],
    ):
        polygon(draw, pts, INK)
    draw.rectangle((22, 6, 26, 9), fill=METAL)
    draw.rectangle((39, 22, 42, 26), fill=METAL_HI)
    draw.rectangle((22, 39, 26, 42), fill=METAL_SH)
    draw.rectangle((6, 22, 9, 26), fill=METAL)

    # Four lit service LEDs provide a shared pulse and reinforce the family.
    led = accent_hi if pulse > 0 else accent
    for x, y in ((14, 14), (34, 14), (34, 34), (14, 34)):
        draw.rectangle((x - 1, y - 1, x + 1, y + 1), fill=INK)
        draw.point((x, y), fill=led)


def draw_lens(draw, accent, accent_hi, pulse, reward=False):
    cx = cy = 24
    draw.ellipse((14, 14, 34, 34), fill=INK)
    draw.ellipse((16, 16, 32, 32), fill=METAL)
    draw.ellipse((18, 18, 30, 30), fill=INK2)
    r = 5 + (1 if pulse > 0 else 0)
    draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=accent)
    draw.rectangle((cx - 2, cy - 2, cx + 1, cy + 1), fill=accent_hi)
    draw.point((cx - 2, cy - 2), fill=WHITE)
    if reward:
        draw.point((cx + 2, cy + 2), fill=accent_hi)


def arrow_up(draw, x, y, color, hi):
    line(draw, [(x, y + 6), (x, y)], color, 2)
    polygon(draw, [(x - 2, y + 2), (x, y - 1), (x + 2, y + 2)], hi)


def draw_straight_symbol(draw, accent, accent_hi):
    # Two forward lanes, deliberately outside the lens so they survive the
    # tiny in-game rendering size.
    arrow_up(draw, 19, 7, accent, accent_hi)
    arrow_up(draw, 29, 7, accent, accent_hi)
    line(draw, [(19, 31), (19, 36)], accent, 2)
    line(draw, [(29, 31), (29, 36)], accent, 2)


def draw_sweep_symbol(draw, accent, accent_hi):
    left = [(16, 10), (11, 15), (16, 20), (11, 25), (16, 30), (12, 34)]
    right = [(32, 10), (37, 15), (32, 20), (37, 25), (32, 30), (36, 34)]
    for pts in (left, right):
        line(draw, pts, INK, 4)
        line(draw, pts, accent, 2)
    polygon(draw, [(12, 34), (9, 34), (12, 31)], accent_hi)
    polygon(draw, [(36, 34), (39, 34), (36, 31)], accent_hi)
    draw.point((16, 10), fill=accent_hi)
    draw.point((32, 10), fill=accent_hi)


def draw_aimed_symbol(draw, accent, accent_hi, phase):
    # Four broken target brackets plus a compact crosshair.
    draw.arc((8, 8, 40, 40), int(phase * 360 + 8), int(phase * 360 + 84), fill=accent, width=2)
    draw.arc((8, 8, 40, 40), int(phase * 360 + 188), int(phase * 360 + 264), fill=accent, width=2)
    for x, y, sx, sy in ((12, 12, 1, 1), (36, 12, -1, 1), (12, 36, 1, -1), (36, 36, -1, -1)):
        line(draw, [(x, y), (x + 5 * sx, y)], accent_hi, 2)
        line(draw, [(x, y), (x, y + 5 * sy)], accent, 2)
    line(draw, [(24, 8), (24, 14)], accent_hi, 1)
    line(draw, [(24, 34), (24, 40)], accent, 1)
    line(draw, [(8, 24), (14, 24)], accent, 1)
    line(draw, [(34, 24), (40, 24)], accent_hi, 1)


def draw_reward_symbol(draw, accent, accent_hi):
    # A tiny front-facing drone silhouette, unmistakable at 48px.
    polygon(draw, [(20, 10), (24, 7), (28, 10), (27, 15), (21, 15)], INK)
    polygon(draw, [(21, 10), (24, 8), (27, 10), (26, 14), (22, 14)], accent)
    polygon(draw, [(20, 11), (16, 13), (16, 16), (21, 14)], INK)
    polygon(draw, [(28, 11), (32, 13), (32, 16), (27, 14)], INK)
    polygon(draw, [(20, 12), (17, 14), (20, 14)], accent_hi)
    polygon(draw, [(28, 12), (31, 14), (28, 14)], accent_hi)
    draw.rectangle((23, 8, 24, 10), fill=accent_hi)
    line(draw, [(23, 15), (23, 18)], accent, 2)
    line(draw, [(25, 15), (25, 18)], accent, 2)


def core_frame(kind: str, frame: int) -> Image.Image:
    img = logical()
    draw = ImageDraw.Draw(img)
    accent_shadow, accent, accent_hi = CORE_PALETTES[kind]
    phase = frame / 14.0
    pulse = 1 if frame in (3, 4, 5, 10, 11) else 0

    draw_core_casing(draw, accent, accent_hi, phase, pulse)
    if kind == "straight":
        draw_straight_symbol(draw, accent, accent_hi)
    elif kind == "sweep":
        draw_sweep_symbol(draw, accent, accent_hi)
    elif kind == "aimed":
        draw_aimed_symbol(draw, accent, accent_hi, phase)
    else:
        draw_reward_symbol(draw, accent, accent_hi)
    draw_lens(draw, accent_shadow if pulse == 0 else accent, accent_hi, pulse, kind == "reward")

    # One hot pixel rotates around the casing; this keeps the 14-frame loop
    # alive without wobbling the emblem or breaking pixel registration.
    x, y = point(24, 24, 20.1, -90 + frame * (360 / 14))
    draw_glint(draw, round(x), round(y), WHITE, 1 if frame in (2, 3, 9, 10) else 0)
    return rgba(img)


def burst_frame(kind: str, frame: int) -> Image.Image:
    """Six-frame compact collect burst, kept at the requested 196px size."""
    base = core_frame(kind, min(frame * 2, 13)).convert("RGBA")
    if frame == 0:
        return base

    accent_shadow, accent, accent_hi = CORE_PALETTES[kind]
    img = logical()
    draw = ImageDraw.Draw(img)
    cx = cy = 24
    radius = 11 + frame * 3
    thickness = max(1, 3 - frame // 2)
    box = (cx - radius, cy - radius, cx + radius, cy + radius)
    draw.arc(box, 18, 142, fill=accent, width=thickness)
    draw.arc(box, 198, 322, fill=accent_hi, width=thickness)
    for a in (0, 45, 90, 135, 180, 225, 270, 315):
        inner = radius - 2
        outer = radius + 3 + frame // 2
        p0 = point(cx, cy, inner, a)
        p1 = point(cx, cy, outer, a)
        line(draw, [p0, p1], accent_hi if a in (45, 225) else accent_shadow, 1 if frame < 4 else 2)
    # Keep a small lens in the first half, then let the ring own the burst.
    if frame < 3:
        draw.ellipse((cx - 4, cy - 4, cx + 4, cy + 4), fill=accent)
        draw.rectangle((cx - 1, cy - 1, cx + 1, cy + 1), fill=WHITE)
    overlay = rgba(img)
    if frame <= 2:
        base = Image.alpha_composite(overlay, base)
    else:
        base = overlay
    return tint_alpha(base, (6 - frame) / 6.0)


def draw_orbit_badge(draw, phase, pulse):
    cx = cy = 24
    # Two crossed orbital ellipses, broken into arcs like the established atoms.
    for box, start in [((5, 12, 43, 36), 24), ((12, 5, 36, 43), 112)]:
        draw.arc(box, int(start + phase * 360), int(start + phase * 360 + 118), fill=(62, 171, 190, 255), width=1)
        draw.arc(box, int(start + phase * 360 + 190), int(start + phase * 360 + 282), fill=(40, 108, 134, 255), width=1)
    # A strong round badge under the magnet is the key family cue: it gives
    # the symbol the same orbital-emblem silhouette as cooldown and pause.
    draw.ellipse((7, 7, 41, 41), outline=INK, width=3)
    draw.ellipse((8, 8, 40, 40), outline=INK, width=2)
    draw.ellipse((10, 10, 38, 38), outline=(36, 66, 92, 255), width=1)
    draw.arc((8, 8, 40, 40), int(18 + phase * 360), int(118 + phase * 360),
             fill=(112, 225, 232, 255), width=2)
    draw.arc((8, 8, 40, 40), int(198 + phase * 360), int(292 + phase * 360),
             fill=(47, 112, 144, 255), width=2)
    # Small electrons on the orbit, one of them hot on alternating frames.
    for i, a in enumerate((-42, 138, 222)):
        x, y = point(cx, cy, 17.2, a + phase * 360)
        color = WHITE if i == (frame_index_from_phase(phase) // 4) % 3 else (82, 211, 225, 255)
        draw.rectangle((round(x) - 1, round(y) - 1, round(x) + 1, round(y) + 1), fill=INK)
        draw.point((round(x), round(y)), fill=color)


def frame_index_from_phase(phase):
    return int(round(phase * 14)) % 14


def magnet_symbol(draw, pulse):
    # Inked horseshoe first, then shaded steel, then red/blue pole caps.
    path = [(15, 14), (15, 24), (18, 30), (24, 33), (30, 30), (35, 24), (35, 14)]
    line(draw, path, INK, 8)
    line(draw, path, METAL, 5)
    line(draw, [(17, 20), (18, 25), (21, 29), (24, 30)], METAL_HI, 2)
    line(draw, [(24, 30), (28, 28), (32, 24)], METAL_SH, 2)
    # Pole blocks are squared-off and deliberately asymmetrical in hue.
    draw.rectangle((11, 11, 18, 16), fill=INK)
    draw.rectangle((12, 12, 17, 15), fill=(210, 41, 48, 255))
    draw.rectangle((13, 12, 17, 13), fill=(255, 91, 78, 255))
    draw.rectangle((32, 11, 39, 16), fill=INK)
    draw.rectangle((33, 12, 38, 15), fill=(45, 103, 225, 255))
    draw.rectangle((33, 12, 37, 13), fill=(106, 171, 255, 255))

    # Magnetic field lines echo the orbital atom language and remain readable
    # at the small in-game size.
    field = (118, 232, 235, 255)
    field_hi = (225, 255, 244, 255)
    line(draw, [(10, 8), (17, 5), (24, 5), (31, 6), (39, 10)], INK, 3)
    line(draw, [(10, 8), (17, 6), (24, 6), (31, 7), (39, 10)], field, 1)
    line(draw, [(10, 34), (16, 39), (24, 41), (32, 39), (39, 34)], INK, 3)
    line(draw, [(10, 34), (16, 38), (24, 40), (32, 38), (39, 34)], field, 1)
    draw.point((24, 5 if pulse == 0 else 6), fill=field_hi)


def magnet_frame(frame: int) -> Image.Image:
    img = logical()
    draw = ImageDraw.Draw(img)
    phase = frame / 14.0
    pulse = 1 if frame in (4, 5, 11, 12) else 0
    draw_orbit_badge(draw, phase, pulse)
    magnet_symbol(draw, pulse)
    # A moving glint travels the outer orbital badge.
    x, y = point(24, 24, 19.5, -70 + frame * (360 / 14))
    draw_glint(draw, round(x), round(y), WHITE, 1 if frame in (1, 2, 8, 9) else 0)
    return rgba(img)


def magnet_burst_frame(frame: int) -> Image.Image:
    # Existing magnet burst files use a 388px canvas. Preserve that contract
    # while keeping the newly drawn symbol in the same centered composition.
    base = magnet_frame(0).resize((388, 388), Image.Resampling.NEAREST)
    if frame == 0:
        return base
    img = Image.new("RGBA", (388, 388), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    cx = cy = 194
    radius = 62 + frame * 20
    draw.arc((cx - radius, cy - radius, cx + radius, cy + radius), 20, 148,
             fill=(118, 232, 235, 255), width=max(3, 12 - frame))
    draw.arc((cx - radius, cy - radius, cx + radius, cy + radius), 202, 330,
             fill=(225, 255, 244, 255), width=max(3, 12 - frame))
    for a in range(0, 360, 45):
        p0 = point(cx, cy, radius - 8, a)
        p1 = point(cx, cy, radius + 20, a)
        line(draw, [p0, p1], (75, 176, 205, 255), max(2, 9 - frame))
    if frame < 3:
        img.alpha_composite(base)
    return tint_alpha(img, (6 - frame) / 6.0)


def save_frames():
    CORE_DIR.mkdir(parents=True, exist_ok=True)
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    old_path = OUT_DIR / "old_magnet_idle_0_reference.png"
    source_old = ATOM_DIR / "magnet_idle_0.png"
    # The contact sheet shows the previous magnet beside the new one. Keep a copy of whatever is on disk the
    # first time (before this run overwrites it); without one the tile is left blank.
    if not old_path.exists() and source_old.exists():
        shutil.copy2(source_old, old_path)
    old_magnet = Image.open(old_path).convert("RGBA") if old_path.exists() else Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

    for kind in CORE_PALETTES:
        for frame in range(14):
            core_frame(kind, frame).save(CORE_DIR / f"core_{kind}_idle_{frame}.png", optimize=True)
        for frame in range(6):
            burst_frame(kind, frame).save(CORE_DIR / f"core_{kind}_burst_{frame}.png", optimize=True)

    for frame in range(14):
        magnet_frame(frame).save(ATOM_DIR / f"magnet_idle_{frame}.png", optimize=True)
    for frame in range(6):
        magnet_burst_frame(frame).save(ATOM_DIR / f"magnet_burst_{frame}.png", optimize=True)

    write_contact_sheet(old_magnet)


def paste_scaled(sheet, image, x, y, size):
    sheet.alpha_composite(image.resize((size, size), Image.Resampling.NEAREST), (int(x), int(y)))


def write_contact_sheet(old_magnet):
    bg = Image.new("RGBA", (1100, 610), SPACE)
    # Row 1: established atoms, old magnet, and the replacement.
    row1 = [
        Image.open(ATOM_DIR / "cooldown_idle_0.png").convert("RGBA"),
        Image.open(ATOM_DIR / "shield_idle_0.png").convert("RGBA"),
        Image.open(ATOM_DIR / "pause_idle_0.png").convert("RGBA"),
        old_magnet,
        Image.open(ATOM_DIR / "magnet_idle_0.png").convert("RGBA"),
    ]
    for i, image in enumerate(row1):
        paste_scaled(bg, image, 8 + i * 218, 8, 196)

    # Row 2: the four core family frame-0s, centered in the same five slots.
    kinds = ("straight", "sweep", "aimed", "reward")
    for i, kind in enumerate(kinds):
        paste_scaled(bg, Image.open(CORE_DIR / f"core_{kind}_idle_0.png").convert("RGBA"),
                     117 + i * 218, 218, 196)

    # Row 3: all straight-core idle frames, with a 48px in-game readability
    # strip directly below the larger frame tiles.
    for i in range(14):
        image = Image.open(CORE_DIR / f"core_straight_idle_{i}.png").convert("RGBA")
        x = 8 + i * 78
        paste_scaled(bg, image, x, 428, 64)
        paste_scaled(bg, image, x + 8, 500, 48)

    bg.save(OUT_DIR / "cores_magnet_preview.png", optimize=True)


if __name__ == "__main__":
    save_frames()
