#!/usr/bin/env python3
"""Turn the Frost mine's six beam cells (row 0, 128 x 384 each) end for end, once.

The painted icicle beam had its shard splash at the cell's bottom (the muzzle) and its point at the top (the far
rail), so in game it read as a beam striking the mine. Flipped: the thin bright core leaves the mine and the shard
splash lands on the far rail. Run on a freshly built atlas (build_minebeam.py) only; running twice undoes it.

    python3 flip_beam_cells.py <atlas.png> [...]
"""
import sys
from PIL import Image

for path in sys.argv[1:]:
    im = Image.open(path).convert("RGBA")
    for i in range(6):
        box = (i * 128, 0, i * 128 + 128, 384)
        im.paste(im.crop(box).transpose(Image.FLIP_TOP_BOTTOM), box[:2])
    im.save(path, optimize=True)
    print("flipped", path)
