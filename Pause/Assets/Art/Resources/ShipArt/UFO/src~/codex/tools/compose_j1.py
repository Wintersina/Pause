#!/usr/bin/env python3
"""Compose J1 damage from generated paint passes using localized nearest-pixel patches."""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw


GREEN = (0, 255, 0)
W = H = 1024


def is_green(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    return g > 105 and g > r * 1.16 and g > b * 1.16


def normalize(path: Path) -> Image.Image:
    return Image.open(path).convert("RGB").resize((W, H), Image.Resampling.NEAREST)


def apply_patch(
    base: Image.Image,
    generated: Image.Image,
    box: tuple[int, int, int, int],
    shift: tuple[int, int] = (0, 0),
    polygon: list[tuple[int, int]] | None = None,
) -> None:
    """Copy only generated non-green pixels in a shaped damage window."""
    bp = base.load()
    gp = generated.load()
    x0, y0, x1, y1 = box
    dx, dy = shift
    shape = Image.new("1", (W, H), 0)
    if polygon:
        ImageDraw.Draw(shape).polygon(polygon, fill=1)
    sp = shape.load()
    for y in range(max(0, y0), min(H, y1)):
        for x in range(max(0, x0), min(W, x1)):
            if polygon and not sp[x, y]:
                continue
            sx, sy = x - dx, y - dy
            if not (0 <= sx < W and 0 <= sy < H):
                continue
            if bp[x, y] == GREEN:
                continue
            if is_green(gp[sx, sy]):
                continue
            bp[x, y] = gp[sx, sy]


def save_preview(states: list[Image.Image], out: Path) -> None:
    preview = Image.new("RGB", (3 * W + 4 * 16, H + 2 * 16), (11, 11, 26))
    for i, state in enumerate(states):
        preview.paste(state, (16 + i * (W + 16), 16))
    preview.save(out, format="PNG", optimize=False)


def main() -> None:
    if len(sys.argv) != 5:
        raise SystemExit("usage: compose_j1.py STATE0 DAMAGE1 DAMAGE2 OUTPUT_DIR")
    state0_path, damage1_path, damage2_path, out_dir = map(Path, sys.argv[1:])
    out_dir.mkdir(parents=True, exist_ok=True)

    state0 = Image.open(state0_path).convert("RGB")
    if state0.size != (W, H):
        raise SystemExit("STATE0 must be 1024x1024")
    damage1 = normalize(damage1_path)
    damage2 = normalize(damage2_path)

    # State 1: generated left cavity is shifted up/right to the requested
    # x=205,y=585 zone; the lower-right generated scorch already lines up.
    state1 = state0.copy()
    apply_patch(
        state1,
        damage1,
        (85, 465, 310, 710),
        shift=(20, -95),
        polygon=[
            (95, 505), (135, 485), (185, 500), (230, 510), (275, 535),
            (300, 580), (275, 620), (290, 655), (250, 690), (210, 680),
            (175, 705), (135, 680), (110, 640), (90, 590),
        ],
    )
    apply_patch(
        state1,
        damage1,
        (610, 680, 815, 850),
        polygon=[
            (650, 690), (730, 685), (785, 710), (810, 750),
            (790, 800), (755, 835), (690, 840), (645, 810), (620, 755),
        ],
    )

    # State 2 starts from state 1 and adds only the three new requested zones.
    state2 = state1.copy()
    apply_patch(
        state2,
        damage2,
        (745, 175, 985, 465),
        polygon=[
            (760, 195), (830, 180), (895, 195), (950, 230), (980, 290),
            (970, 355), (945, 420), (895, 450), (845, 420), (805, 375),
            (775, 325),
        ],
    )                                                               # upper-right tear
    apply_patch(
        state2,
        damage2,
        (210, 720, 445, 930),
        polygon=[
            (225, 745), (285, 715), (350, 730), (405, 770), (430, 825),
            (405, 875), (360, 915), (295, 900), (240, 855), (210, 800),
        ],
    )                                                               # lower-left breach
    apply_patch(
        state2,
        damage2,
        (385, 375, 650, 675),
        polygon=[
            (515, 380), (600, 405), (640, 455), (650, 535), (615, 615),
            (555, 660), (485, 665), (420, 625), (390, 540), (415, 445),
        ],
    )                                                               # cracked centre

    state1.save(out_dir / "key_state1.png", format="PNG", optimize=False)
    state2.save(out_dir / "key_state2.png", format="PNG", optimize=False)
    save_preview([state0, state1, state2], out_dir / "preview_j1.png")


if __name__ == "__main__":
    main()
