#!/usr/bin/env python3
"""Reference images for the Dove restyle: old cells (4x nearest on green) + old flame strip crop + overview."""
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
CODEX = os.path.dirname(HERE)
HULLS = os.path.abspath(os.path.join(CODEX, "..", "..", ".."))
OLD = os.environ.get("DOVE_OLD_SHEET", os.path.join(HULLS, "Dove.png"))
sheet = Image.open(OLD).convert("RGBA")
for st in range(3):
    cell = sheet.crop((0, st * 256, 256, (st + 1) * 256)).resize((1024, 1024), Image.NEAREST)
    bg = Image.new("RGBA", (1024, 1024), (0, 255, 0, 255))
    bg.alpha_composite(cell)
    bg.convert("RGB").save(os.path.join(CODEX, "ref_old_state%d.png" % st))
atlas = Image.open(os.path.join(HULLS, "..", "Exhaust", "exhaust_atlas.png")).convert("RGBA")
H = atlas.height
for name, x, y in (("ref_old_flame_cruise.png", 1400, 698), ("ref_old_flame_boost.png", 0, 504)):
    crop = atlas.crop((x, H - y - 192, x + 7 * 50, H - y))
    crop = crop.resize((crop.width * 3, crop.height * 3), Image.NEAREST)
    bg = Image.new("RGBA", crop.size, (0, 255, 0, 255))
    bg.alpha_composite(crop)
    bg.convert("RGB").save(os.path.join(CODEX, name))
# dark overview of the old sheet
dark = Image.new("RGBA", sheet.size, (14, 20, 36, 255))
dark.alpha_composite(sheet)
dark.convert("RGB").save(os.path.join(CODEX, "old_sheet_dark.png"))
print("ok")
