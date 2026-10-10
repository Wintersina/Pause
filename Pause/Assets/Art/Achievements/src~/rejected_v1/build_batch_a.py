"""Assemble Batch A from painted subject sheets and the four painted tier frames.

All inputs and outputs live under the two approved achievement art directories.
Run from anywhere: python3 Pause/Assets/Art/Achievements/src~/build_batch_a.py
"""

from pathlib import Path
from PIL import Image, ImageCms, ImageDraw, ImageFont
import math


HERE = Path(__file__).resolve().parent
LIVE = HERE.parent.parent / "Resources" / "Achievements"
BLACK = (7, 7, 15)
SIZE = 1024
SRGB = ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB")).tobytes()

# The source sheets are ordered left-to-right, top-to-bottom. The last sheet
# carries three painted candidates for each of its three subjects.
BADGES = [
    ("meta_first_flight", "Bronze", 1, 0),
    ("meta_logged_on", "Bronze", 1, 1),
    ("world_frost_reached", "Bronze", 1, 2),
    ("world_verdant_reached", "Silver", 1, 3),
    ("world_ember_reached", "Silver", 1, 4),
    ("world_tide_reached", "Silver", 1, 5),
    ("loop_1", "Gold", 1, 6),
    ("loop_2", "Gold", 1, 7),
    ("loop_5", "Platinum", 1, 8),
    ("boss_space", "Silver", 2, 0),
    ("boss_frost", "Silver", 2, 1),
    ("boss_verdant", "Silver", 2, 2),
    ("boss_ember", "Gold", 2, 3),
    ("boss_tide", "Gold", 2, 4),
    ("boss_no_hit", "Gold", 2, 5),
    ("boss_all", "Platinum", 2, 6),
    ("elite_first", "Bronze", 2, 7),
    ("elite_10", "Silver", 2, 8),
    ("elite_50", "Gold", 3, 0),
    ("elite_space_all", "Silver", 3, 1),
    ("elite_frost_all", "Silver", 3, 2),
    ("elite_verdant_all", "Silver", 3, 3),
    ("elite_ember_all", "Gold", 3, 4),
    ("elite_blink", "Silver", 3, 5),
    ("kills_100", "Bronze", 3, 6),
    ("kills_1000", "Silver", 3, 7),
    ("kills_5000", "Gold", 3, 8),
    ("rocks_500", "Bronze", 4, 6),
    ("mines_25", "Bronze", 4, 4),
    ("chain_10", "Silver", 4, 2),
]


def circle_alpha(radius, feather=0):
    mask = Image.new("L", (SIZE, SIZE))
    px = mask.load()
    for y in range(SIZE):
        for x in range(SIZE):
            d = math.hypot(x - 511.5, y - 511.5)
            px[x, y] = round(255 * max(0, min(1, (radius - d) / feather))) if feather else (255 if d <= radius else 0)
    return mask


def crop_cell(sheet, columns, rows, index):
    w, h = sheet.size
    col, row = index % columns, index // columns
    return sheet.crop((round(col*w/columns), round(row*h/rows),
                       round((col+1)*w/columns), round((row+1)*h/rows)))


def make_frames():
    atlas = Image.open(HERE / "frames" / "tier_frames_painted_atlas.png").convert("RGB")
    result = {}
    for tier, index in (("Bronze", 0), ("Silver", 1), ("Gold", 2), ("Platinum", 3)):
        frame = crop_cell(atlas, 2, 2, index).resize((SIZE, SIZE), Image.Resampling.LANCZOS)
        frame.paste(BLACK, (0, 0, SIZE, SIZE), Image.eval(circle_alpha(460), lambda a: 255-a))
        frame.save(HERE / "frames" / f"{tier.lower()}_frame_1024.png")
        result[tier] = frame
    return result


def make_icon(badge, frames, sheets):
    name, tier, sheet_num, index = badge
    subject = crop_cell(sheets[sheet_num], 3, 3, index).convert("RGB")
    subject = subject.resize((680, 680), Image.Resampling.LANCZOS)
    base = Image.new("RGB", (SIZE, SIZE), BLACK)
    base.paste(subject, (172, 172), circle_alpha(335, 25).crop((172, 172, 852, 852)))

    # Keep the exact same painted frame per tier. The frame's black aperture is
    # replaced by the subject, while its physical iron/metal ring remains above.
    frame_mask = Image.eval(circle_alpha(329, 24), lambda a: 255-a)
    base.paste(frames[tier], (0, 0), frame_mask)
    outer = Image.eval(circle_alpha(460), lambda a: 255-a)
    base.paste(BLACK, (0, 0, SIZE, SIZE), outer)
    base.save(HERE / f"{name}_1024.png", icc_profile=SRGB)

    # BOX is area downsampling; it retains the tiny painted pixel clusters at
    # 128 better than nearest on this high-resolution generated source.
    icon = base.resize((128, 128), Image.Resampling.BOX).convert("RGBA")
    alpha = Image.new("L", (128, 128))
    a = alpha.load()
    for y in range(128):
        for x in range(128):
            a[x, y] = 255 if math.hypot(x-63.5, y-63.5) <= 57.5 else 0
    icon.putalpha(alpha)
    icon.save(LIVE / f"{name}.png", icc_profile=SRGB)


def make_sheet(size):
    cols, rows = 6, 5
    gutter, label = max(8, size//18), max(15, size//7)
    cell_w, cell_h = size + gutter, size + label + gutter
    sheet = Image.new("RGB", (cols*cell_w+gutter, rows*cell_h+gutter), (16, 18, 26))
    draw = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", max(11, size//16))
    except OSError:
        font = ImageFont.load_default()
    for n, (name, _, _, _) in enumerate(BADGES):
        x = gutter + (n % cols)*cell_w
        y = gutter + (n // cols)*cell_h
        path = HERE / f"{name}_1024.png" if size == 1024 else LIVE / f"{name}.png"
        im = Image.open(path).convert("RGBA").resize((size, size), Image.Resampling.BOX)
        sheet.paste(im, (x, y), im if size != 1024 else None)
        draw.text((x, y+size+2), name, fill=(230, 226, 211), font=font)
    sheet.save(HERE / f"sheet_batch_a_{size}.png")


def main():
    LIVE.mkdir(parents=True, exist_ok=True)
    frames = make_frames()
    sheets = {i: Image.open(HERE / "candidates" / filename) for i, filename in {
        1: "batch_a_subjects_01_painted.png",
        2: "batch_a_subjects_02_painted.png",
        3: "batch_a_subjects_03_painted.png",
        4: "batch_a_subjects_04_variants_painted.png",
    }.items()}
    for badge in BADGES:
        make_icon(badge, frames, sheets)
    for size in (1024, 128, 48):
        make_sheet(size)
    print(f"Built {len(BADGES)} masters and {len(BADGES)} game icons (BOX area downscale).")


if __name__ == "__main__":
    main()
