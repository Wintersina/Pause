#!/usr/bin/env python3
"""Idle-cycle GIFs (cells 0-3, hold ticks 6,3,2,3 at 24 fps, 2x on the jungle tile) into ./gifs/<name>.gif, plus gifs/idle_cycles.png."""
from pathlib import Path
from PIL import Image
HERE = Path(__file__).resolve().parent
OUT = HERE / "gifs"; OUT.mkdir(exist_ok=True)
BG = (22, 48, 30)
names = [p.stem for p in sorted((HERE / "strips").glob("*.png"))]
rows = []
for n in names:
    s = Image.open(HERE / "strips" / f"{n}.png").convert("RGBA"); h = s.height
    fr = []
    for i in range(4):
        c = s.crop((i * h, 0, (i + 1) * h, h)); bg = Image.new("RGBA", c.size, BG + (255,)); bg.alpha_composite(c)
        fr.append(bg.convert("RGB").resize((h * 2, h * 2), Image.NEAREST))
    fr[0].save(OUT / f"{n}.gif", save_all=True, append_images=fr[1:], duration=[250, 125, 83, 125], loop=0)
    rows.append(fr)
W = max(4 * r[0].width for r in rows); H = sum(r[0].height for r in rows)
sh = Image.new("RGB", (W, H), BG); y = 0
for r in rows:
    for i, f in enumerate(r):
        sh.paste(f, (i * f.width, y))
    y += r[0].height
sh.save(OUT / "idle_cycles.png"); print(names)
