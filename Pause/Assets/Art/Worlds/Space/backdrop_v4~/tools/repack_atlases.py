#!/usr/bin/env python3
"""Repack Codex's Space backdrop atlases (backdrop_v4~/{landmarks,ambient}) into the smaller sheets the
game loads (phone texture budget). Usage: repack_atlases.py <backdrop_v4~ dir> <Resources/.../Space/Backdrop dir>"""
import json, sys
from PIL import Image

def load(src, name):
    im = Image.open(f"{src}/{name}.png").convert("RGBA")
    d = json.load(open(f"{src}/{name}.json"))
    H = im.height
    out = []
    for s in d["sprites"]:
        box = (s["x"], H - s["y"] - s["h"], s["x"] + s["w"], H - s["y"])
        out.append((s["n"], im.crop(box)))
    return out

def pack(sprites, scale, cols, cw, ch, name, dst, pixel_scale, full=False):
    rows = (len(sprites) + cols - 1) // cols
    W, H = cols * cw, rows * ch
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    meta = []
    for i, (n, im) in enumerate(sprites):
        if scale != 1:
            im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)
        assert im.width <= cw - 4 and im.height <= ch - 4, (n, im.size)
        cx, cy = (i % cols) * cw + (cw - im.width) // 2, (i // cols) * ch + (ch - im.height) // 2
        sheet.alpha_composite(im, (cx, cy))
        if full:    # the whole cell is the sprite (a flipbook must not change size between frames)
            meta.append({"n": n, "x": (i % cols) * cw, "y": H - ((i // cols) + 1) * ch, "w": cw, "h": ch})
        else:
            meta.append({"n": n, "x": cx, "y": H - cy - im.height, "w": im.width, "h": im.height})
    sheet.save(f"{dst}/{name}.png", optimize=True)
    json.dump({"pixelScale": pixel_scale, "sheetW": W, "sheetH": H, "sprites": meta}, open(f"{dst}/{name}.json", "w"), indent=1)
    print(name, W, H)

if __name__ == "__main__":
    v4, dst = sys.argv[1], sys.argv[2]
    pack(load(v4 + "/landmarks", "wrecks"), 0.5, 4, 176, 500, "wrecks", dst, 1.0)
    pack(load(v4 + "/ambient", "debris"), 0.375, 8, 96, 96, "debris", dst, 0.75)
    tw = [(n.rsplit("_", 1)[0] + "_" + int(n.rsplit("_", 1)[1]).__format__("02d"), im) for n, im in load(v4 + "/ambient", "twinkle")]
    pack(tw, 1, 8, 32, 32, "twinkle", dst, 2, full=True)
    pack(load(v4 + "/ambient", "nebula"), 0.375, 4, 192, 192, "nebula", dst, 0.75)
