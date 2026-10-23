#!/usr/bin/env python3
"""usage: preview_sheet.py <sheet.png|bytes> <out.png>  -> sheet on dark #0e1424."""
import sys
from PIL import Image
im = Image.open(sys.argv[1]).convert("RGBA")
bg = Image.new("RGBA", im.size, (14, 20, 36, 255))
bg.alpha_composite(im)
bg.convert("RGB").save(sys.argv[2])
