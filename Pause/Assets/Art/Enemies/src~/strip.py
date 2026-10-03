"""Butts each enemy's rendered frames into one horizontal strip PNG.

    python3 strip.py <frame dir> <out dir> [key prefix]
"""
import os
import re
import sys

from PIL import Image

from common import FRAME_COUNT


def main(frames, out, prefix=""):
    keys = sorted({re.sub(r"_\d+\.png$", "", n) for n in os.listdir(frames)
                   if n.endswith(".png") and n.startswith(prefix)})
    for key in keys:
        cells = [Image.open(os.path.join(frames, f"{key}_{i}.png")).convert("RGBA") for i in range(FRAME_COUNT)]
        w, h = cells[0].size
        strip = Image.new("RGBA", (w * FRAME_COUNT, h), (0, 0, 0, 0))
        for i, c in enumerate(cells):
            strip.paste(c, (i * w, 0))
        strip.save(os.path.join(out, key + ".png"), optimize=True)
    print(f"wrote {len(keys)} strips into {out}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else "")
