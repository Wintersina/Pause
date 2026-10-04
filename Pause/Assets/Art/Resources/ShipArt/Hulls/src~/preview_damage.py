"""Review sheet for the damage progressions (not used by the game).

    python3 preview_damage.py <out_dir> [--zoom 1]

  damage_states.png   every ship (rows) x intact / damaged / critical, rest
                      frame on the Space lane colour, the damage-FX emitters
                      of each state marked (colour per kind, new ones ringed)
"""
import os
import sys

from PIL import Image, ImageDraw

from build import CELL, OUT_DIR, ZOOM, load
from damage import CONCEPTS
from preview import LANE, font
from ships import ORDER

KIND_COL = {"sparks": (255, 180, 60), "arc": (110, 242, 238), "smoke": (163, 180, 204),
            "flame": (255, 91, 69), "leak": (46, 230, 166)}


def main():
    out = sys.argv[1]
    z = 1
    if "--zoom" in sys.argv:
        z = int(sys.argv[sys.argv.index("--zoom") + 1])
    c = CELL * z
    left, top, gap = 190, 40, 12
    img = Image.new("RGBA", (left + 3 * (c + gap) + 700, top + len(ORDER) * (c + gap)), LANE)
    d = ImageDraw.Draw(img)
    f, fs = font(18), font(12)
    for s, head in enumerate(["intact - 3 lives", "damaged - 2 lives", "critical - last life"]):
        d.text((left + s * (c + gap) + 6, 10), head, font=f, fill=(110, 242, 238))
    for r, key in enumerate(ORDER):
        ship = load(key)
        sheet = Image.open(os.path.join(OUT_DIR, key + ".png")).convert("RGBA")
        y = top + r * (c + gap)
        d.text((8, y + c // 2 - 10), f"{r + 1:2d} {key}", font=f, fill=(244, 234, 212))
        for s in range(3):
            x = left + s * (c + gap)
            frame = sheet.crop((0, s * CELL, CELL, (s + 1) * CELL))
            if z != 1:
                frame = frame.resize((c, c), Image.NEAREST)
            img.alpha_composite(frame, (x, y))
            for st in range(1, s + 1):
                for kind, u, v in ship.emitters[st]:
                    px, py = x + u * ZOOM * z, y + v * ZOOM * z
                    col = KIND_COL[kind]
                    rr = 4 * z
                    d.ellipse((px - rr, py - rr, px + rr, py + rr), fill=col, outline=(20, 12, 20))
                    if st == s:
                        d.ellipse((px - rr - 3, py - rr - 3, px + rr + 3, py + rr + 3), outline=col, width=2)
        tx = left + 3 * (c + gap) + 4
        d.text((tx, y + 20), "1: " + CONCEPTS[key][0], font=fs, fill=(244, 234, 212))
        d.text((tx, y + 44), "2: " + CONCEPTS[key][1], font=fs, fill=(244, 234, 212))
    ly = 8
    for k, (kind, col) in enumerate(KIND_COL.items()):
        lx = left + 3 * (c + gap) + 10 + k * 100
        d.ellipse((lx, ly + 4, lx + 10, ly + 14), fill=col)
        d.text((lx + 14, ly), kind, font=fs, fill=col)
    os.makedirs(out, exist_ok=True)
    img.save(os.path.join(out, "damage_states.png"))


if __name__ == "__main__":
    main()
