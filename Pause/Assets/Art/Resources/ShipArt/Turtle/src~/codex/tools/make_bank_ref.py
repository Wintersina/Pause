"""ref_modest_banks.png: NeonComet's approved idle / bank-left / bank-right cells (row 0), 512 px each on green."""
import sys
from PIL import Image

sheet = Image.open(sys.argv[1]).convert("RGBA")
out = Image.new("RGB", (1536, 512), (0, 255, 0))
for i, c in enumerate((0, 6, 7)):
    cell = sheet.crop((c * 256, 0, c * 256 + 256, 256)).resize((512, 512), Image.NEAREST)
    bg = Image.new("RGBA", (512, 512), (0, 255, 0, 255))
    bg.alpha_composite(cell)
    out.paste(bg.convert("RGB"), (i * 512, 0))
out.save(sys.argv[2])
