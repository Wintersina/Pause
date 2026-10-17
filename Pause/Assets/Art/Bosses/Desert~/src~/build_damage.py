#!/usr/bin/env python3
"""Build the Dune-Eater v2 damage hulls, fx overlay and death strip.

    python3 Pause/Assets/Art/Bosses/Desert~/src~/build_damage.py   (after build.py)

Outputs next to Desert.png (Desert~/):
  Desert_damage.png     2 x 4 cells: row = damage stage 1..4, cols = idle loop A,B (registered to idle cell 20)
  Desert_damage_fx.png  6 x 2 cells: smoke loop (row 0), electrical arcs loop (row 1)
  Desert_death.png      6 cells: flash on the stage-4 hull, then the painted death keys

d1..d4 / smoke_key / death_*.png are painted edits of hull_key.png. Anything missing falls back to a
procedural version so the strip is always complete.
"""
from __future__ import annotations

import math
import random

import numpy as np
from PIL import Image

from build import BASE, beat, fireball, glow, idle, pulse, registered
from common import *

BG = (11, 11, 26, 255)


def damage_body(stage: int) -> Image.Image:
    pa = registered(f"d{stage}", (1.0,))
    assert pa is not None, f"d{stage}.png (the painted damage key) is missing"
    return pa


def variant(body: Image.Image, stage: int, v: int) -> Image.Image:
    if not v:
        return body
    # the loop B: wing tips drift a hair and the molten cracks breathe
    return pulse(beat(body, 1.6, 0), .6)


# ------------------------------------------------------------------ fx
def smoke_frame(k: int) -> Image.Image:
    pk = SRC / "smoke_key.png"
    im = rgba()
    if pk.exists():
        key = fix_hue(Image.open(pk))
        w, h = key.size
        # the key holds three plumes left / centre / right; each is cut out, scaled to the cell and bobbed
        thirds = [(0, w // 3), (w // 3, 2 * w // 3), (2 * w // 3, w)]
        for n, (x0, x1) in enumerate(thirds):
            piece = key.crop((x0, 0, x1, h))
            bbox = piece.getchannel("A").point(lambda v: 255 if v > 40 else 0).getbbox()
            if not bbox:
                continue
            piece = piece.crop(bbox)
            sz = (round(piece.width * 96 * K / piece.height), round(96 * K))
            piece = piece.resize(sz, Image.Resampling.LANCZOS)
            tx = round([112, 190, 268][n] * K) - sz[0] // 2 + round([-2, -1, 0, 1, 2, 0][k] * K) * (1 if n != 2 else -1)
            ty = round([150, 132, 150][n] * K) - sz[1] + round([2, 1, 0, 1, 2, 3][k] * K)
            im.alpha_composite(piece, (tx, max(0, ty)))
        return im
    rng = random.Random(900 + k)
    d = SD(im)
    for sx, sy in [(112, 150), (268, 150), (190, 132)]:
        for q in range(7):
            r = 8 + q * 2
            ox = int(math.sin(k * 1.1 + q * 1.7 + sx) * (4 + q))
            y = sy - q * 13 - ((k * 3 + q * 2) % 5)
            col = (52 + q * 6, 50 + q * 6, 56 + q * 6, max(40, 200 - q * 26))
            d.ellipse((sx + ox - r, y - r, sx + ox + r, y + r), fill=col)
        d.ellipse((sx - 6, sy - 6, sx + 6, sy + 6), fill=(*ORANGE, 150))
    for _ in range(10):
        bx, by = rng.randint(100, 284), rng.randint(40, 120)
        d.rectangle((bx, by, bx + 1, by + 1), fill=(*AMBER, 220))
    return im


def electric_frame(k: int) -> Image.Image:
    im = rgba()
    d = SD(im)
    rng = random.Random(4910 + k)
    pairs = [((60, 230), (104, 196)), ((330, 230), (280, 196)), ((140, 200), (190, 150)),
             ((240, 210), (190, 250)), ((120, 290), (160, 262)), ((260, 290), (222, 262))]
    for n in range(3 + (k % 2)):
        (x0, y0), (x1, y1) = pairs[(n + k) % len(pairs)]
        pts = [(x0, y0)]
        for t in range(1, 8):
            f = t / 8
            pts.append((round(x0 + (x1 - x0) * f + rng.randrange(-7, 8)), round(y0 + (y1 - y0) * f + rng.randrange(-7, 8))))
        pts.append((x1, y1))
        d.line(pts, fill=(40, 150, 160, 130), width=7, joint="curve")
        d.line(pts, fill=(*TEAL, 240), width=3, joint="curve")
        d.line(pts, fill=(*WHITE, 255), width=1, joint="curve")
        for _ in range(3):
            px, py = x1 + rng.randrange(-11, 12), y1 + rng.randrange(-11, 12)
            d.line((px - 3, py, px + 3, py), fill=(*TEAL, 235), width=1)
            d.line((px, py - 3, px, py + 3), fill=WHITE, width=1)
    return im


# ------------------------------------------------------------------ death strip
def flash_body(body: Image.Image) -> Image.Image:
    # the stage-4 hull's drooping wings reach past the idle art's width: pull it in 3 percent about the centre
    w = round(C * .97)
    sm = body.resize((w, w), Image.Resampling.NEAREST)
    im = rgba()
    im.paste(sm, ((C - w) // 2, (C - w) // 2))
    layer = rgba()
    for x, y in [(190, 93), (95, 196), (285, 196), (190, 253), (108, 294), (268, 294), (334, 142)]:
        glow(layer, (x, y), 8, YELLOW, 1.1)
        d = SD(layer)
        d.line((x - 15, y, x + 15, y), fill=(*WHITE, 230), width=2)
        d.line((x, y - 15, x, y + 15), fill=(*WHITE, 230), width=2)
    im.alpha_composite(layer)
    return im


def death_key(name: str, margin: int, fallback) -> Image.Image:
    p = SRC / f"{name}.png"
    if not p.exists():
        return fallback()
    im = fix_hue(Image.open(p))
    a = np.asarray(im).copy(); a[a[..., 3] < 72] = 0
    im = Image.fromarray(a, "RGBA")
    box = im.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox()
    im = im.crop(box)
    margin = round(margin * K)
    inner = C - 2 * margin
    s = min(inner / im.width, inner / im.height)
    im = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.Resampling.LANCZOS)
    out = rgba(); out.alpha_composite(im, ((C - im.width) // 2, (C - im.height) // 2))
    return out


def fb_fire(r, power=1.0):
    def f():
        layer = rgba(); fireball(layer, 190, 190, r, power); return layer
    return f


def on_bg(im):
    bg = Image.new("RGBA", (C, C), BG); bg.alpha_composite(im); return bg.convert("RGB")


def main():
    bodies = [damage_body(s) for s in range(1, 5)]
    body_sheet = Image.new("RGBA", (C * 2, C * 4))
    for s, body in enumerate(bodies, 1):
        for v in range(2):
            body_sheet.alpha_composite(variant(body, s, v), (v * C, (s - 1) * C))
    body_sheet.save(ROOT / "Desert_damage.png")

    smoke = [smoke_frame(k) for k in range(6)]
    arcs = [electric_frame(k) for k in range(6)]
    fx = Image.new("RGBA", (C * 6, C * 2))
    for k in range(6):
        fx.alpha_composite(smoke[k], (k * C, 0)); fx.alpha_composite(arcs[k], (k * C, C))
    fx.save(ROOT / "Desert_damage_fx.png")

    death = [flash_body(bodies[-1]),
             death_key("death_split", 14, fb_fire(60, .8)), death_key("death_overload", 14, fb_fire(105)),
             death_key("death_peak", 14, fb_fire(120)), death_key("death_shockwave", 14, fb_fire(120, .5)),
             death_key("death_fade", 14, fb_fire(90, .25))]
    strip = Image.new("RGBA", (C * 6, C))
    for k, f in enumerate(death):
        strip.alpha_composite(f, (k * C, 0))
    strip.save(ROOT / "Desert_death.png")

    pristine = BASE
    prev = Image.new("RGB", (REF * 2 * 5, REF * 2), BG[:3])
    for k, frame in enumerate([pristine] + bodies):
        view = frame.copy()
        if k:
            view.alpha_composite(smoke[2]); view.alpha_composite(arcs[3])
        prev.paste(on_bg(view).resize((REF * 2, REF * 2), Image.Resampling.NEAREST), (k * REF * 2, 0))
    prev.save(ROOT / "damage_preview.png")
    dp = Image.new("RGB", (REF * 2 * 6, REF * 2), BG[:3])
    for k, f in enumerate(death):
        dp.paste(on_bg(f).resize((REF * 2, REF * 2), Image.Resampling.NEAREST), (k * REF * 2, 0))
    dp.save(ROOT / "Desert_death_preview.png")
    print("built Desert damage, fx, death")


if __name__ == "__main__":
    main()
