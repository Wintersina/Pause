#!/usr/bin/env python3
"""Colourful, flying Desert roster (art/enemy-colors-desert).

The seven enemy bodies were repainted one ship at a time with Codex (flying_src/<key>_in.png -> <key>_flying.png, prompts in
flying_src/*.prompt): one coherent hull family plus brass/copper/bone trim, glowing
cores and flame jets, and the ground-bound designs (Sandbarge treads, Lens Gunner legs, Dust Devil Tank, Scarab legs) redrawn
as hovering craft. No scripted gradient/zone remap is used. The painting is area-downsampled to game size (alpha re-thresholded so edges stay hard), then animated with the same
warp / light-pulse / tell / hit helpers as build.py. NO added pale outline border, NO palette clamp (final_palette would
desaturate the new colours). Writes strips/<key>.png and <key>_key.png (the old sand keys live in original_sand/).

    python3 build_color.py [key ...]
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image
import build as B

HERE = Path(__file__).resolve().parent

# key -> (selected column 0..2, game-size longest side)
SEL = {
    "desert_alien": (1, 168),
    "desert_chaser": (0, 164),
    "desert_big": (0, 230),
    "desert_fighter_1": (0, 161),
    "desert_fighter_2": (0, 170),
    "desert_fighter_3": (0, 168),
    "desert_fighter_4": (0, 174),
}
SEL_SIZE = {k: v for k, v in B.SPEC.items()}


def color_key(name, column, target):
    """The single Codex repaint flying_src/<key>_flying.png (see flying_src/*.prompt), cropped to its sprite."""
    src = Image.open(HERE / "flying_src" / f"{name}_flying.png").convert("RGBA")
    a = np.asarray(src.getchannel("A"))
    yy, xx = np.nonzero(a >= 104)
    src = src.crop((max(0, xx.min() - 2), max(0, yy.min() - 2), min(src.width, xx.max() + 3), min(src.height, yy.max() + 3)))
    f = target / max(src.size)
    size = (max(1, round(src.width * f)), max(1, round(src.height * f)))
    # premultiplied area average so transparent texels do not bleed black into the edge
    arr = np.asarray(src).astype(np.float32)
    arr[:, :, :3] *= arr[:, :, 3:4] / 255
    pm = Image.fromarray(arr.astype(np.uint8), "RGBA")
    chans = [np.asarray(pm.getchannel(i).resize(size, Image.Resampling.BOX)).astype(np.float32) for i in range(4)]
    al = chans[3]
    rgb = np.stack([np.where(al > 0, c / np.maximum(al, 1) * 255, 0) for c in chans[:3]], axis=2)
    alpha = np.where(al >= 104, 255, 0).astype(np.uint8)
    small = Image.fromarray(np.dstack((np.clip(rgb, 0, 255).astype(np.uint8), alpha)), "RGBA")
    cell = 256 if name == "desert_big" else 192
    canvas = Image.new("RGBA", (cell, cell))
    canvas.alpha_composite(small, ((cell - size[0]) // 2, (cell - size[1]) // 2))
    return canvas


def scrub(im):
    """Keep generated antialiasing out of the reserved hues: pink (310..328) -> violet, player red -> saffron orange."""
    a = np.array(im)
    hsv = np.array(im.convert("RGB").convert("HSV")).astype(np.float32)
    h, sat, v = hsv[:, :, 0] * 360 / 255, hsv[:, :, 1] / 255, hsv[:, :, 2] / 255
    act = a[:, :, 3] > 0
    pink = act & (h >= 304) & (h <= 336) & (sat > .08)
    red = act & ((h >= 338) | (h <= 17)) & (sat > .4)
    hsv[pink, 0] = 278 * 255 / 360
    hsv[red, 0] = 26 * 255 / 360
    a[:, :, :3] = np.array(Image.fromarray(hsv.astype(np.uint8), "HSV").convert("RGB"))
    return Image.fromarray(a, "RGBA")


def main():
    keys = sys.argv[1:] or list(SEL)
    for name in keys:
        column, size = SEL[name]
        key = scrub(B.break_straight_edges(color_key(name, column, size)))
        key.save(HERE / f"{name}_key.png", optimize=True)
        phases = (0.0, .52, -.52, .28) if name in {"desert_fighter_2", "desert_fighter_3", "desert_fighter_4"} else B.PHASE
        frames = [B.break_straight_edges(B.light_pulse(B.warp(key, name, p), pulse)) for p, pulse in zip(phases, (0, 8, -5, 14))]
        frames += [B.break_straight_edges(B.tell(key, name, 4)), B.break_straight_edges(B.tell(key, name, 5)),
                   B.break_straight_edges(B.hit(key, name))]
        frames = [scrub(f) for f in frames]
        cell = key.width
        strip = Image.new("RGBA", (cell * 7, cell))
        for i, fr in enumerate(frames):
            strip.alpha_composite(fr, (i * cell, 0))
        strip.save(B.OUT / f"{name}.png", optimize=True)
        print(f"{name:20s} col {column}  {strip.width}x{strip.height}")


if __name__ == "__main__":
    main()
