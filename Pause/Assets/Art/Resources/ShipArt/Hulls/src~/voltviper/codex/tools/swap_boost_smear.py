"""Codex drew the boost smear streak in frame 1; the hold-tick table (2,2,1,2,2,2,2) wants it in frame 2 like the cruise strip. Swap cells 1 and 2."""
import sys
from PIL import Image

p = sys.argv[1]
im = Image.open(p).convert("RGB")
w = im.width // 7
c1 = im.crop((w, 0, 2 * w, im.height))
c2 = im.crop((2 * w, 0, 3 * w, im.height))
im.paste(c2, (w, 0))
im.paste(c1, (2 * w, 0))
im.save(p)
print("swapped boost cells 1 and 2")
