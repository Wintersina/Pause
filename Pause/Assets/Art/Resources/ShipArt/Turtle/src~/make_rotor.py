#!/usr/bin/env python3
"""Builds ../Turtle_rotor.png (256x128: left cell = 3-blade rotor, right cell = static faint motion-blur disc)
from the Codex painting codex/rotor_raw.png (one 3-blade rotor on flat #FF00FF, prompt in codex/rotor_prompt.txt).
Steps: key the magenta (and erode for fringe), find the exact hub centre (the cyan gem's centroid), cut ONE blade
(the top one) and paste it at 0/120/240 degrees around that centre so the three blades are identical and the hub
is dead centre, put the hub on top, scale to a 96 px rotor (radius 48 hull-cell px), threshold alpha (crisp pixels,
no smear). The blur disc is scripted from the blade palette: stepped translucent rings, alpha <= 0.25.
Usage: python3 make_rotor.py   (needs PIL + numpy)"""
import math, os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
CELL, R = 128, 48


def erode(m):
    p = np.pad(m, 1)
    out = m.copy()
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            out &= p[1 + dy:1 + dy + m.shape[0], 1 + dx:1 + dx + m.shape[1]]
    return out


def main():
    rgb = np.asarray(Image.open(os.path.join(HERE, "codex", "rotor_raw.png")).convert("RGB")).astype(int)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mag = (r > 140) & (b > 140) & (g < 130) & (abs(r - b) < 100)
    fg = erode(erode(~mag))
    cyan = (b > 200) & (r < 90) & (g > 150) & fg
    gem = np.zeros_like(cyan)
    gem[500:620, 440:590] = True                            # the hub gem only, not the cyan blade-root stubs
    ys, xs = np.nonzero(cyan & gem)
    cx, cy = xs.mean(), ys.mean()
    print("hub centre", round(cx, 1), round(cy, 1))
    S = 1024
    full = Image.fromarray(np.dstack([rgb.astype(np.uint8), (fg * 255).astype(np.uint8)]), "RGBA")
    full = full.transform((S, S), Image.AFFINE, (1, 0, cx - S / 2, 0, 1, cy - S / 2), Image.NEAREST)
    arr = np.asarray(full)
    yy, xx = np.mgrid[0:S, 0:S]
    dx, dy = xx - S / 2 + .5, yy - S / 2 + .5
    ang = np.degrees(np.arctan2(dx, -dy))                   # 0 = straight up
    rad = np.hypot(dx, dy)
    one = Image.fromarray(np.where((abs(ang) <= 40)[..., None], arr, 0).astype(np.uint8), "RGBA")
    hub = Image.fromarray(np.where((rad <= 118)[..., None], arr, 0).astype(np.uint8), "RGBA")
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    for k in range(3):
        out.alpha_composite(one.rotate(-120 * k, resample=Image.BICUBIC, center=(S / 2, S / 2)))
    out.alpha_composite(hub)

    a = np.asarray(out)[..., 3] > 0
    tip = np.hypot(*(np.argwhere(a) - (S / 2 - .5)).T).max()
    px = int(round(S * R / tip))
    px += px % 2
    # premultiplied resize so the keyed edge does not bleed magenta
    pm = np.asarray(out).astype(np.float32)
    pm[..., :3] *= pm[..., 3:] / 255
    chans = [np.asarray(Image.fromarray(pm[..., i], "F").resize((px, px), Image.BOX)) for i in range(4)]
    al = chans[3]
    rgbm = np.dstack(chans[:3])
    col = np.where(al[..., None] > 1, rgbm / np.maximum(al[..., None], 1) * 255, 0)
    res = np.dstack([np.clip(col, 0, 255), np.where(al >= 128, 255, 0)]).astype(np.uint8)
    res[res[..., 3] == 0] = 0
    small = Image.fromarray(res, "RGBA")

    sheet = Image.new("RGBA", (CELL * 2, CELL), (0, 0, 0, 0))
    o = (CELL - px) // 2
    sheet.alpha_composite(small, (o, o))

    c = CELL / 2                                            # blur disc: stepped rings, faint
    rings = [(R - .5, (140, 156, 178, 40)), (R * .72, (92, 106, 126, 52)), (R * .42, (214, 222, 232, 64))]
    for py in range(CELL):
        for pxx in range(CELL):
            d = math.hypot(pxx + .5 - c, py + .5 - c)
            hit = None
            for rr, rc in rings:
                if d <= rr:
                    hit = rc
            if hit:
                sheet.putpixel((CELL + pxx, py), hit)
    path = os.path.normpath(os.path.join(HERE, "..", "Turtle_rotor.png"))
    sheet.save(path)
    print("wrote", path, "rotor px", px)


if __name__ == "__main__":
    main()
