"""Review helper: the hull key in the 384 cell at 3x with a 32 px coordinate grid."""
import sys
from PIL import Image, ImageDraw
from common import *

name = sys.argv[1] if len(sys.argv) > 1 else "hull_key.png"
cell = to_cell(Image.open(SRC / name))
bg = Image.new("RGBA", (C, C), (30, 30, 44, 255))
bg.alpha_composite(cell)
big = bg.resize((C * 3, C * 3), Image.Resampling.NEAREST)
d = ImageDraw.Draw(big)
for v in range(0, C, 32):
    d.line([(v * 3, 0), (v * 3, C * 3)], fill=(255, 255, 255, 60))
    d.line([(0, v * 3), (C * 3, v * 3)], fill=(255, 255, 255, 60))
    d.text((v * 3 + 2, 2), str(v), fill=(255, 255, 0))
    d.text((2, v * 3 + 2), str(v), fill=(255, 255, 0))
big.convert("RGB").save(sys.argv[2] if len(sys.argv) > 2 else "/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/grid.png")
