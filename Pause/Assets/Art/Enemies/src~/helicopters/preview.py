"""preview.py <key> [outdir]: 1x / 3x contact sheet + idle-loop GIF of a built strip (out/<key>.png)."""
import sys
from pathlib import Path
from PIL import Image
HERE = Path(__file__).resolve().parent
key = sys.argv[1]
dst = Path(sys.argv[2]) if len(sys.argv) > 2 else HERE / "out"
dst.mkdir(parents=True, exist_ok=True)
strip = Image.open(HERE / "out" / f"{key}.png").convert("RGBA")
C = strip.height
cells = [strip.crop((i * C, 0, i * C + C, C)) for i in range(7)]
BG = (24, 26, 40, 255)
sheet = Image.new("RGBA", (C * 7, C + C * 3 // 1), BG)
sheet = Image.new("RGBA", (C * 7, C * 4), BG)
for i, c in enumerate(cells):
    sheet.alpha_composite(c, (i * C, 0))                                   # 1x
for i, c in enumerate(cells[:7]):
    big = c.resize((C * 3 // 2, C * 3 // 2), Image.NEAREST)
sheet3 = Image.new("RGBA", (C * 3 * 4, C * 3 * 2), BG)
for i, c in enumerate(cells):
    sheet3.alpha_composite(c.resize((C * 3, C * 3), Image.NEAREST), ((i % 4) * C * 3, (i // 4) * C * 3))
sheet.crop((0, 0, C * 7, C)).save(dst / f"{key}_sheet_1x.png")
sheet3.save(dst / f"{key}_sheet_3x.png")
frames = []
hold = [6, 3, 2, 3]
for k in range(4):
    f = Image.new("RGBA", (C * 3, C * 3), BG)
    f.alpha_composite(cells[k].resize((C * 3, C * 3), Image.NEAREST))
    frames.append(f.convert("P", palette=Image.ADAPTIVE))
dur = [int(h * 1000 / 24) * 2 for h in hold]
frames[0].save(dst / f"{key}_idle.gif", save_all=True, append_images=frames[1:], duration=dur, loop=0, disposal=2)
print("ok", dst)
