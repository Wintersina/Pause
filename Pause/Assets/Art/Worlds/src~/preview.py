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

from bgkit import RESOURCES, BACKDROP_RESOURCES, ART_WORLDS

PPU = 100
SW, SH = 5.7, 12.3


class Composer:
    def __init__(self, world):
        self.root = os.path.join(BACKDROP_RESOURCES, world, "Backdrop")
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
    ex, ey = min(im.width, out.width - px), min(im.height, out.height - py)
    if cx >= ex or cy >= ey:
        return
    out.alpha_composite(im.crop((cx, cy, ex, ey)), (max(0, px), max(0, py)))


WORLDS = ["Space", "Frost", "Verdant", "Ember"]
WALL_X, WALL_W = 3.21, 1.43            # gameS1 leftPipe / rightPipe quads (centre, width)
RAIL_X = 2.35                          # RailMineMount.WorldRailX on a 2.85 half-width view


def wall_textures(world):
    assets = os.path.dirname(ART_WORLDS)
    if world == "Space":
        return [os.path.join(assets, n) for n in ("left.png", "right.png")]
    return [os.path.join(RESOURCES, world, n) for n in ("wallLeft.png", "wallRight.png")]


def draw_walls(img, world, scroll=0.0):
    """The side walls as the game maps them: one texture repeat per quad
    height (10.8 u), UV-scrolled, only the inner strip on screen."""
    W, H = img.size
    for side, path in zip((-1, 1), wall_textures(world)):
        tex = Image.open(path).convert("RGBA")
        qw = int(WALL_W * PPU)
        qh = int(10.8 * PPU)
        t = tex.resize((qw, qh), Image.NEAREST)
        x0 = int(W / 2 + (side * WALL_X - WALL_W / 2) * PPU)
        off = int((scroll * PPU) % qh)
        y = -qh + off
        while y < H:
            _paste_clipped(img, t, x0, y)
            y += qh
    return img


def gameplay_overlay(img, world="Space", scroll=0.0, enemies=True):
    """Walls, the player, the world's enemies and its rail mines, for
    judging contrast the way the game stacks them."""
    assets = os.path.dirname(ART_WORLDS)
    res = os.path.join(assets, "Resources")
    W, H = img.size
    draw_walls(img, world, scroll)

    def put(im, wu, x, y):
        im = im.resize((int(wu * PPU), int(im.height * wu * PPU / im.width)), Image.BILINEAR)
        _paste_clipped(img, im, int(W / 2 + x * PPU - im.width / 2), int(H / 2 - y * PPU - im.height / 2))

    if enemies:
        key = world.lower()
        cast = [("fighter_1", 0.85, -1.3, 3.6), ("fighter_2", 0.85, 0.2, 4.4), ("fighter_3", 0.85, 1.4, 2.6),
                ("alien", 0.8, -0.4, 1.6), ("chaser", 0.8, 1.2, 0.4), ("big", 1.25, -1.0, -0.6),
                ("rock_" + {"space": "", "frost": "shard", "verdant": "pod", "ember": "cinder"}[key], 0.6, 0.9, -2.2),
                ("fighter_4", 0.85, -1.5, -3.2)]
        for name, wu, x, y in cast:
            p = os.path.join(res, "Enemies", f"{key}_{name}.png")
            if not os.path.exists(p):
                continue
            strip = Image.open(p).convert("RGBA")
            put(strip.crop((0, 0, strip.height, strip.height)), wu, x, y)
        mines = Image.open(os.path.join(res, "Vfx", "rail_bomb_themes_atlas.png")).convert("RGBA")
        cell = mines.width // 4
        row = WORLDS.index(world)
        m = mines.crop((0, row * cell, cell, (row + 1) * cell))
        put(m, 0.8, -RAIL_X, 4.8)
        put(m, 0.8, RAIL_X, -1.6)
    hull = Image.open(os.path.join(res, "ShipArt", "Hulls", "CrimsonHalo.png")).convert("RGBA")
    put(hull.crop((0, 0, 256, 256)), 0.62, 0.0, -4.4)
    return img


def main():
    world = sys.argv[1]
    outdir = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ART_WORLDS, world, "src~", ".preview")
    os.makedirs(outdir, exist_ok=True)
    mod = importlib.import_module(world.lower())
    comp = Composer(world)
    still = comp.frame(mod.preview(6.0))
    still.convert("RGB").save(os.path.join(outdir, f"{world}_still.png"))
    gameplay_overlay(still.copy(), world).convert("RGB").save(os.path.join(outdir, f"{world}_with_gameplay.png"))
    frames = [comp.frame(mod.preview(2.0 + i * 0.25)).convert("RGB").resize((285, 615)) for i in range(48)]
    frames[0].save(os.path.join(outdir, f"{world}_motion.gif"), save_all=True, append_images=frames[1:],
                   duration=125, loop=0)
    strip = Image.new("RGB", (285 * 6, 615))
    for i in range(6):
        strip.paste(frames[i * 8], (i * 285, 0))
    strip.save(os.path.join(outdir, f"{world}_strip.png"))


if __name__ == "__main__":
    main()
