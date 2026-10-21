#!/usr/bin/env python3
"""Build the Kaneda racing-livery sheets from generated repaint guides.

The approved flipbook remains the geometry lock.  The generated guide decides
where the racing stripe is painted and supplies its local bone ramp; all other
reference pixels are copied unchanged.  The final export is nearest-neighbour
upscaled to the requested 1024 px cells.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GUIDES = {
    0: Path("/Users/sina/.codex/generated_images/01a15f82-109d-7a91-814d-49047cde7c5d/exec-f6793ad6-07c6-4042-bccb-c6a156725d57.png"),
    1: Path("/Users/sina/.codex/generated_images/01a15f82-109d-7a91-814d-49047cde7c5d/exec-33207963-66b5-4bf8-b1f3-9c4521183e8d.png"),
    2: Path("/Users/sina/.codex/generated_images/01a15f82-109d-7a91-814d-49047cde7c5d/exec-ba0b3200-1242-40c4-9207-42f54accf338.png"),
}

CELL = 512
OUT_CELL = 1024
GREEN = (0, 255, 0)
BONE = (244, 234, 212)
BONE_SHADOW = (185, 174, 152)
CRIMSON = (216, 35, 44)


def near_bone(px: tuple[int, int, int]) -> bool:
    r, g, b = px
    return r >= 115 and g >= 100 and b >= 80 and max(px) - min(px) <= 70


def guide_red(px: tuple[int, int, int]) -> bool:
    r, g, b = px
    return r >= 125 and r > g * 1.18 and r > b * 1.12 and g < 165


def source_crimson(px: tuple[int, int, int]) -> bool:
    r, g, b = px
    return r > 5 and r > g * 1.03 and r > b * 1.08 and g < 180


def source_light(px: tuple[int, int, int]) -> bool:
    r, g, b = px
    return r >= 150 and g >= 140 and b >= 120 and max(px) - min(px) <= 65


def bone_ramp(guide_px: tuple[int, int, int]) -> tuple[int, int, int]:
    r, g, b = guide_px
    value = 0.2126 * r + 0.7152 * g + 0.0722 * b
    if value < 120:
        return BONE_SHADOW
    if value < 190:
        return (212, 199, 174)
    return BONE


def guide_cell(guide: Image.Image, row: int, col: int) -> Image.Image:
    w, h = guide.size
    x0, x1 = round(col * w / 3), round((col + 1) * w / 3)
    y0, y1 = round(row * h / 3), round((row + 1) * h / 3)
    return guide.crop((x0, y0, x1, y1)).resize((CELL, CELL), Image.Resampling.NEAREST)


def build_sheet(state: int) -> None:
    ref = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    guide = Image.open(GUIDES[state]).convert("RGB")
    out = Image.new("RGB", (OUT_CELL * 3, OUT_CELL * 3), GREEN)

    for row in range(3):
        for col in range(3):
            ref_cell = ref.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))
            gcell = guide_cell(guide, row, col)
            src = ref_cell.load()
            gsrc = gcell.load()
            painted = Image.new("RGB", (CELL, CELL), GREEN)
            dst = painted.load()
            hit_flash = (row, col) == (2, 2)

            # The generated guide is intentionally conservative at the stripe
            # boundary.  A small nearest-neighbour dilation makes the livery a
            # genuinely wide racing stripe while still requiring both the
            # guide's bone paint and the reference's crimson armour.
            candidate_rows = []
            for y in range(CELL):
                xs = [
                    x
                    for x in range(144, 369)
                    if near_bone(gsrc[x, y]) and source_crimson(src[x, y])
                ]
                candidate_rows.append(xs)

            stripe_center = []
            previous = CELL // 2
            for xs in candidate_rows:
                if xs:
                    previous = sorted(xs)[len(xs) // 2]
                stripe_center.append(previous)

            stripe_seed = {
                (x, y)
                for y, xs in enumerate(candidate_rows)
                for x in xs
                if abs(x - stripe_center[y]) <= 30
            }

            def in_stripe(x: int, y: int) -> bool:
                radius = 8
                for sy in range(max(0, y - radius), min(CELL, y + radius + 1)):
                    for sx in range(max(0, x - radius), min(CELL, x + radius + 1)):
                        if (sx, sy) in stripe_seed and abs(x - stripe_center[y]) <= 42:
                            return True
                return False

            for y in range(CELL):
                for x in range(CELL):
                    sp = src[x, y]
                    gp = gsrc[x, y]
                    if sp == GREEN:
                        continue

                    # The guide's red inset is used only for the hit-flash,
                    # where the approved sheet has no red material to retain.
                    if hit_flash and 150 <= x <= 365 and guide_red(gp) and source_light(sp):
                        dst[x, y] = CRIMSON
                    elif (near_bone(gp) or in_stripe(x, y)) and (source_crimson(sp) or (hit_flash and source_light(sp))):
                        dst[x, y] = bone_ramp(gp)
                    else:
                        dst[x, y] = sp

            out.paste(painted.resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST), (col * OUT_CELL, row * OUT_CELL))

    out.save(ROOT / f"skin_Kaneda_state{state}.png", format="PNG", optimize=False)


if __name__ == "__main__":
    for state in range(3):
        build_sheet(state)
        print(f"built skin_Kaneda_state{state}.png")
