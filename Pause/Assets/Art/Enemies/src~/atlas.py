"""Packs the restyled rail mine into the legacy 4x4 atlas layout.

    python3 atlas.py <frame dir> <out png>

Row = world (space, frost, verdant, ember), column = dormant, lit, arming,
burst (flipbook frames 0, 1, 4, 5). 1254 px square, 313 px cells, the same
geometry RailBombSprites slices.
"""
import os
import sys

from PIL import Image

WORLDS = ("space", "frost", "verdant", "ember")
FRAMES = (0, 1, 4, 5)
SIZE = 1254


def main(frames, out):
    cell = SIZE / 4.0
    atlas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    for r, w in enumerate(WORLDS):
        for c, k in enumerate(FRAMES):
            im = Image.open(os.path.join(frames, f"atlas_{w}_{k}.png")).convert("RGBA")
            atlas.paste(im, (int(round(c * cell)), int(round(r * cell))))
    atlas.save(out, optimize=True)
    print("wrote", out)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
