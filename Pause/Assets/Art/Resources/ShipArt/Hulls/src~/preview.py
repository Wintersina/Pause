"""Preview sheets for review (not used by the game).

    python3 preview.py <out_dir> [old_art_dir]

  roster.png          all 15 rest frames, on the Space lane colour
  before_after.png    old art (from old_art_dir, a checkout of the old PNGs) over the new
  idle_<Key>.png      the idle strip (6 drawings + bank L/R + hit) x 3 damage states
  idle_<Key>.gif      the idle loop played with its tick table at 24 fps
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

from build import CELL, COLUMNS, IDLE_SEQUENCE, OUT_DIR, STATES
from hullkit import REPO
from ships import HUE_NAMES, OLD_RECTS, ORDER, SPINNERS

LANE = (14, 20, 36, 255)
FONT = os.path.join(REPO, "Pause/Assets/Art/Orbitron/Orbitron-Bold.ttf")


def font(size):
    try:
        return ImageFont.truetype(FONT, size)
    except OSError:
        return ImageFont.load_default()


def sheet(key):
    return Image.open(os.path.join(OUT_DIR, key + ".png")).convert("RGBA")


def cell(img, state, col):
    return img.crop((col * CELL, state * CELL, (col + 1) * CELL, (state + 1) * CELL))


OLD_CROPS = {
    "NeonComet": (16, 18, 32, 29), "VoltViper": (18, 21, 28, 23), "SolarFang": (17, 18, 29, 28),
    "CrimsonHalo": (8, 4, 46, 57), "IonLancer": (9, 6, 47, 55), "JadePhantom": (4, 6, 56, 55),
    "GoldWarden": (7, 6, 51, 55), "Lightning": (2, 4, 28, 27), "Ligher": (6, 2, 20, 29),
    "Paranoid": (1, 5, 30, 22), "Ninja": (1, 1, 30, 30), "Saboteur": (4, 2, 24, 30), "UFO": (3, 3, 26, 26),
    "Dove": (6, 2, 20, 24), "Turtle": (5, 2, 22, 29),
}


def old_art(old_dir, key):
    retro = ORDER.index(key) < 7
    p = os.path.join(old_dir, "Prefabs/Ships/Retro80s/%s_intact.png" % key) if retro else \
        os.path.join(old_dir, "ShipArt/Originals/%s.png" % key)
    im = Image.open(p).convert("RGBA")
    x, y, w, h = OLD_CROPS[key]
    return im.crop((x, im.height - y - h, x + w, im.height - y))


def fit_box(im, size, nearest=False):
    s = size / max(im.size)
    return im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))),
                     Image.NEAREST if nearest else Image.LANCZOS)


def roster(out, old_dir=None):
    tile = 200
    rows = 2 if old_dir else 1
    W, H = tile * 15 + 40, 70 + rows * (tile + 50) + 20
    img = Image.new("RGBA", (W, H), LANE)
    d = ImageDraw.Draw(img)
    d.text((20, 18), "PLAYER HULLS: before (top) / after (bottom)" if old_dir else "PLAYER HULLS", font=font(26),
           fill=(244, 234, 212))
    for i, key in enumerate(ORDER):
        x = 20 + i * tile
        y = 70
        if old_dir:
            o = fit_box(old_art(old_dir, key), 170, nearest=True)
            img.alpha_composite(o, (x + (tile - o.width) // 2, y + (tile - o.height) // 2))
            y += tile + 50
        n = fit_box(cell(sheet(key), 0, 0), 170)
        img.alpha_composite(n, (x + (tile - n.width) // 2, y + (tile - n.height) // 2))
        d.text((x + 8, y + tile + 2), f"{i + 1}. {key}", font=font(15), fill=(244, 234, 212))
        d.text((x + 8, y + tile + 22), HUE_NAMES[key], font=font(12), fill=(110, 242, 238))
    img.save(os.path.join(out, "before_after.png" if old_dir else "roster.png"))


def idle_strip(out, key):
    sh = sheet(key)
    labels = ["0 key", "1 up", "2 blink", "3 glint", "4 glint", "5 down", "bank L", "bank R", "hit"]
    pad = 8
    img = Image.new("RGBA", (COLUMNS * (CELL + pad) + pad, 40 + STATES * (CELL + pad) + pad), LANE)
    d = ImageDraw.Draw(img)
    for c in range(COLUMNS):
        d.text((pad + c * (CELL + pad) + 6, 10), labels[c], font=font(16), fill=(244, 234, 212))
        for s in range(STATES):
            img.alpha_composite(cell(sh, s, c), (pad + c * (CELL + pad), 40 + s * (CELL + pad)))
    img.save(os.path.join(out, f"idle_{key}.png"))
    frames, durations = [], []
    for drawing, ticks in IDLE_SEQUENCE:
        f = Image.new("RGBA", (CELL, CELL), LANE)
        f.alpha_composite(cell(sh, 0, drawing))
        frames.append(f.convert("P", palette=Image.ADAPTIVE))
        durations.append(round(ticks * 1000 / 24))
    frames[0].save(os.path.join(out, f"idle_{key}.gif"), save_all=True, append_images=frames[1:],
                   duration=durations, loop=0, disposal=2)


def rest_grid(out, col=0, state=0, name="rest.png"):
    img = Image.new("RGBA", (5 * CELL, 3 * CELL), LANE)
    for i, key in enumerate(ORDER):
        img.alpha_composite(cell(sheet(key), state, col), ((i % 5) * CELL, (i // 5) * CELL))
    img.save(os.path.join(out, name))


if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    roster(out)
    rest_grid(out)
    if len(sys.argv) > 2:
        roster(out, sys.argv[2])
    for key in ORDER:
        idle_strip(out, key)
    print("previews in", out)
