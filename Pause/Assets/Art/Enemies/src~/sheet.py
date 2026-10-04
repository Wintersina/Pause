"""Preview sheets for review (not loaded by the game).

    python3 sheet.py <out dir> [key prefix ...]

Writes one roster sheet per world (every enemy's full strip: idle, tell, hit
flash) on that world's lane colour, plus an animated GIF per enemy idle+tell
loop timed from the tick table. Needs the strips from render.sh.
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

import build
from common import FRAME_COUNT

HERE = os.path.dirname(os.path.abspath(__file__))
STRIPS = os.path.join(HERE, "..", "..", "Resources", "Enemies")
LANES = {"space": (14, 20, 36), "frost": (10, 26, 42), "verdant": (11, 31, 28), "ember": (36, 9, 14)}
NAMES = {}   # filled from the roster table in EnemyRoster.cs when available
FONT_PATH = os.path.join(HERE, "..", "..", "Orbitron", "Orbitron-Bold.ttf")


def font(size):
    try:
        return ImageFont.truetype(FONT_PATH, size)
    except Exception:
        return ImageFont.load_default()


def load_names():
    import re
    cs = os.path.join(HERE, "..", "..", "..", "Scripts", "Gameplay", "Enemies", "EnemyRoster.cs")
    if not os.path.exists(cs):
        return
    for m in re.finditer(r'Def\("([a-z_0-9]+)",\s*"([^"]+)"', open(cs).read()):
        NAMES[m.group(1)] = m.group(2)


def world_sheet(world, keys, out):
    cell = 192
    label = 34
    rows = len(keys)
    W = 300 + cell * FRAME_COUNT
    H = 90 + rows * (cell + label)
    img = Image.new("RGBA", (W, H), LANES[world] + (255,))
    d = ImageDraw.Draw(img)
    d.text((24, 24), world.upper() + " ROSTER", font=font(34), fill=(244, 234, 212, 255))
    heads = ["idle 0 key", "idle 1", "idle 2", "idle 3", "tell 4", "tell 5", "hit 6"]
    for i, h in enumerate(heads):
        d.text((300 + i * cell + 8, 70), h, font=font(13), fill=(163, 180, 204, 255))
    for r, key in enumerate(keys):
        y = 90 + r * (cell + label)
        strip = Image.open(os.path.join(STRIPS, key + ".png")).convert("RGBA")
        if strip.size[1] != cell:   # the heavies render at 2x
            strip = strip.resize((cell * FRAME_COUNT, cell), Image.LANCZOS)
        img.alpha_composite(strip, (300, y))
        d.text((16, y + 70), NAMES.get(key, key), font=font(20), fill=(244, 234, 212, 255))
        d.text((16, y + 100), key, font=font(12), fill=(163, 180, 204, 255))
        _, idle, tell = build.roster()[key]
        ticks = list(idle) + list(tell) + [build.HIT_TICKS]
        for i, t in enumerate(ticks):
            d.text((300 + i * cell + 8, y + cell + 6), f"{t} ticks", font=font(12), fill=(110, 242, 238, 255))
    img.convert("RGB").save(os.path.join(out, f"roster_{world}.png"))


def gif(key, out):
    strip = Image.open(os.path.join(STRIPS, key + ".png")).convert("RGBA")
    cell = strip.size[1]
    world = key.split("_")[0]
    _, idle, tell = build.roster()[key]
    seq = [(i, idle[i]) for i in range(4)] * 2 + [(4, tell[0]), (5, tell[1])] * 2 + [(6, build.HIT_TICKS)]
    frames, durs = [], []
    for i, t in seq:
        bg = Image.new("RGBA", (cell, cell), LANES[world] + (255,))
        bg.alpha_composite(strip.crop((i * cell, 0, (i + 1) * cell, cell)))
        frames.append(bg.convert("P", palette=Image.ADAPTIVE))
        durs.append(int(t * 1000 / 24))
    frames[0].save(os.path.join(out, key + ".gif"), save_all=True, append_images=frames[1:], duration=durs, loop=0)


def main(out, prefixes):
    os.makedirs(out, exist_ok=True)
    load_names()
    keys = [k for k in build.roster() if not prefixes or any(k.startswith(p) for p in prefixes)]
    for w in build.WORLDS:
        ks = [k for k in keys if k.startswith(w + "_") and os.path.exists(os.path.join(STRIPS, k + ".png"))]
        if ks:
            world_sheet(w, ks, out)
    gifs = os.path.join(out, "gif")
    os.makedirs(gifs, exist_ok=True)
    for k in keys:
        if os.path.exists(os.path.join(STRIPS, k + ".png")):
            gif(k, gifs)
    print("sheets in", out)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2:])
