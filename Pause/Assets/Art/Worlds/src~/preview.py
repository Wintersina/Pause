"""Offline composer: renders a world's layers the way WorldBackdrop places
them, to judge composition, contrast and motion before touching Unity.

    python3 preview.py Space [outdir]      -> still, frame strip and GIF

Each world module exposes `preview(t)` returning a list of draw commands in
world units (screen is 5.7 x 12.3 units, origin at the centre, +y up):
    ("tile", name, scroll_units, alpha, tint)
    ("sprite", atlas, sprite, x, y, width_units, rot_deg, tint_rgba, flip)
"""
import importlib
import json
import math
import os
import sys

from PIL import Image, ImageChops

from bgkit import RESOURCES, ART_WORLDS

PPU = 100
SW, SH = 5.7, 12.3


class Composer:
    def __init__(self, world):
        self.root = os.path.join(RESOURCES, world, "Backdrop")
        self.cache = {}
        self.atlases = {}

    def img(self, name):
        if name not in self.cache:
            self.cache[name] = Image.open(os.path.join(self.root, name + ".png")).convert("RGBA")
        return self.cache[name]

    def sprite(self, atlas, name):
        key = atlas + "/" + name
        if key not in self.cache:
            if atlas not in self.atlases:
                with open(os.path.join(self.root, atlas + ".json")) as f:
                    self.atlases[atlas] = {s["n"]: s for s in json.load(f)["sprites"]}
            s = self.atlases[atlas][name]
            sheet = self.img(atlas)
            top = sheet.height - s["y"] - s["h"]
            self.cache[key] = sheet.crop((s["x"], top, s["x"] + s["w"], top + s["h"]))
        return self.cache[key]

    @staticmethod
    def tinted(im, tint):
        r, g, b, a = tint
        if (r, g, b, a) == (1, 1, 1, 1):
            return im
        layer = Image.new("RGBA", im.size, (int(r * 255), int(g * 255), int(b * 255), int(a * 255)))
        return ImageChops.multiply(im, layer)

    def frame(self, cmds):
        W, H = int(SW * PPU), int(SH * PPU)
        out = Image.new("RGBA", (W, H), (0, 0, 0, 255))
        for c in cmds:
            if c[0] == "strip":
                _, name, scroll, frac, alpha = c
                tile = self.img(name)
                sw = int(W * frac)
                th = int(tile.height * sw / tile.width)
                t = tile.resize((sw, th), Image.BILINEAR)
                if alpha < 1:
                    t = self.tinted(t, (1, 1, 1, alpha))
                off = int((scroll * PPU) % th)
                y = -th + off
                x0 = (W - sw) // 2
                while y < H:
                    if y >= 0:
                        out.alpha_composite(t, (x0, y))
                    else:
                        out.alpha_composite(t.crop((0, -y, sw, th)), (x0, 0))
                    y += th
            elif c[0] == "tile":
                _, name, scroll, alpha, tint = c
                tile = self.img(name)
                scale = W / tile.width
                th = int(tile.height * scale)
                t = self.tinted(tile.resize((W, th), Image.BILINEAR), tuple(tint[:3]) + (tint[3] * alpha,))
                off = int((scroll * PPU) % th)
                y = -th + off
                while y < H:
                    out.alpha_composite(t, (0, y)) if y >= 0 else out.alpha_composite(t.crop((0, -y, W, th)), (0, 0))
                    y += th
            else:
                _, atlas, name, x, y, wu, rot, tint, flip = c
                im = self.sprite(atlas, name)
                if flip:
                    im = im.transpose(Image.FLIP_LEFT_RIGHT)
                w = max(1, int(wu * PPU))
                h = max(1, int(im.height * w / im.width))
                im = self.tinted(im.resize((w, h), Image.BILINEAR), tint)
                if rot:
                    im = im.rotate(rot, Image.BILINEAR, expand=True)
                px = int(W / 2 + x * PPU - im.width / 2)
                py = int(H / 2 - y * PPU - im.height / 2)
                out.alpha_composite(im, (px, py)) if px >= 0 and py >= 0 else _paste_clipped(out, im, px, py)
        return out


def _paste_clipped(out, im, px, py):
    cx, cy = max(0, -px), max(0, -py)
    if cx >= im.width or cy >= im.height:
        return
    out.alpha_composite(im.crop((cx, cy, im.width, im.height)), (max(0, px), max(0, py)))


def gameplay_overlay(img):
    """Pipes, the ship and a few hazards, for judging contrast."""
    assets = os.path.dirname(ART_WORLDS)
    def load(p, w):
        im = Image.open(os.path.join(assets, p)).convert("RGBA")
        return im.resize((int(w * PPU), int(im.height * w * PPU / im.width)), Image.BILINEAR)
    W, H = img.size
    for p, w, x, y in [("Atoms/atom3a.png", 0.7, -1.2, 2.5), ("Atoms/atom3a.png", 0.7, 1.4, -0.5),
                       ("Aestroids/aestroid_brown_1.png", 0.9, 0.9, 3.8),
                       ("Aestroids/aestroid_dark_1.png", 0.8, -1.5, -2.8),
                       ("invader32x32x4.png", 0.6, 0.2, 1.0)]:
        try:
            im = load(p, w)
        except FileNotFoundError:
            continue
        if p.startswith("invader"):
            im = im.crop((0, 0, im.width // 4 if im.width > im.height else im.width, im.height))
        img.alpha_composite(im, (int(W / 2 + x * PPU - im.width / 2), int(H / 2 - y * PPU - im.height / 2)))
    pipe = Image.new("RGBA", (int((SW / 2 - 2.5) * PPU), H), (40, 40, 60, 255))
    img.alpha_composite(pipe, (0, 0))
    img.alpha_composite(pipe, (W - pipe.width, 0))
    return img


def main():
    world = sys.argv[1]
    outdir = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ART_WORLDS, world, "src~", ".preview")
    os.makedirs(outdir, exist_ok=True)
    mod = importlib.import_module(world.lower())
    comp = Composer(world)
    still = comp.frame(mod.preview(6.0))
    still.convert("RGB").save(os.path.join(outdir, f"{world}_still.png"))
    gameplay_overlay(still.copy()).convert("RGB").save(os.path.join(outdir, f"{world}_with_gameplay.png"))
    frames = [comp.frame(mod.preview(2.0 + i * 0.25)).convert("RGB").resize((285, 615)) for i in range(48)]
    frames[0].save(os.path.join(outdir, f"{world}_motion.gif"), save_all=True, append_images=frames[1:],
                   duration=125, loop=0)
    strip = Image.new("RGB", (285 * 6, 615))
    for i in range(6):
        strip.paste(frames[i * 8], (i * 285, 0))
    strip.save(os.path.join(outdir, f"{world}_strip.png"))


if __name__ == "__main__":
    main()
