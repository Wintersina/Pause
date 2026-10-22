"""view_sheet.py <sheet.png> <out.png> [scale]: a 2304x768 sheet over #0e1424, scale default 0.8."""
import sys
from PIL import Image

sh = Image.open(sys.argv[1]).convert("RGBA")
bg = Image.new("RGBA", sh.size, (14, 20, 36, 255))
bg.alpha_composite(sh)
s = float(sys.argv[3]) if len(sys.argv) > 3 else 0.8
bg.convert("RGB").resize((int(sh.width * s), int(sh.height * s)), Image.NEAREST).save(sys.argv[2])
