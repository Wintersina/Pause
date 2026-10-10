#!/usr/bin/env python3
"""Assemble the selected image-generated Tide designs into anchored flipbooks.

All resampling is nearest-neighbour. The source paintings are kept next to this
script; this script only moves local anatomy and adds small hand-placed effects.
"""
from pathlib import Path
import math
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
SPEC = {
    "tide_alien": (0, 154),
    "tide_chaser": (1, 170),
    "tide_big": (1, 219),
    "tide_fighter_1": (0, 163),
    "tide_fighter_2": (1, 174),
    "tide_fighter_3": (-1, 165),
    "tide_fighter_4": (2, 165),
    "tide_rock_brain": (0, 152),
    "tide_rock_staghorn": (0, 157),
    "tide_rock_urchin": (0, 152),
    "tide_rock_islet": (1, 156),
}
MINT = (124, 242, 192, 255)
PALE = (220, 255, 237, 255)
WHITE = (247, 255, 249, 255)
DEEP = (4, 14, 19, 255)
BRASS = (137, 108, 70, 255)
PHASE = [0.0, 1.0, -1.0, 0.62]


def selected_source(name, candidate):
    if candidate < 0:
        im = Image.open(HERE / "tide_fighter_3_selected_source.png").convert("RGBA")
    else:
        im = Image.open(HERE / f"{name}_candidates.png").convert("RGBA")
        width = im.width // 3
        im = im.crop((candidate * width, 0, (candidate + 1) * width, im.height))
    a = np.asarray(im.getchannel("A"))
    ys, xs = np.where(a > 112)
    if not len(xs):
        raise ValueError(name)
    x0, x1 = max(0, xs.min() - 4), min(im.width, xs.max() + 5)
    y0, y1 = max(0, ys.min() - 4), min(im.height, ys.max() + 5)
    return im.crop((x0, y0, x1, y1))


def repair_red(im):
    rgb = np.asarray(im.convert("RGB"), dtype=np.float32)
    a = np.asarray(im.getchannel("A"))
    mx = rgb.max(2); mn = rgb.min(2); delta = mx - mn
    sat = np.divide(delta, mx, out=np.zeros_like(delta), where=mx > 0)
    hue = np.zeros_like(mx)
    ok = delta > 0
    rr = ok & (mx == rgb[:, :, 0]); gg = ok & (mx == rgb[:, :, 1]); bb = ok & (mx == rgb[:, :, 2])
    hue[rr] = (60 * (rgb[:, :, 1][rr] - rgb[:, :, 2][rr]) / delta[rr]) % 360
    hue[gg] = 60 * (rgb[:, :, 2][gg] - rgb[:, :, 0][gg]) / delta[gg] + 120
    hue[bb] = 60 * (rgb[:, :, 0][bb] - rgb[:, :, 1][bb]) / delta[bb] + 240
    bad = (a > 8) & (sat > .45) & ((hue < 15) | (hue >= 345))
    # Brown iron replaces any accidentally red source texels while retaining value variation.
    val = mx[bad]
    rgb[:, :, 0][bad] = val
    rgb[:, :, 1][bad] = val * .69
    rgb[:, :, 2][bad] = val * .57
    lime = (a > 8) & (hue >= 70) & (hue <= 130) & (sat > .42)
    val = mx[lime]
    rgb[:, :, 0][lime] = val * .70
    rgb[:, :, 1][lime] = val
    rgb[:, :, 2][lime] = val * .83
    out = np.dstack((rgb.clip(0, 255).astype(np.uint8), a))
    return Image.fromarray(out, "RGBA")


def key_pose(name, candidate, target):
    cell = 256 if name == "tide_big" else 192
    src = selected_source(name, candidate)
    factor = target / max(src.size)
    size = (max(1, round(src.width * factor)), max(1, round(src.height * factor)))
    src = src.resize(size, Image.Resampling.NEAREST)
    src = repair_red(src)
    if name in {"tide_big", "tide_fighter_2", "tide_fighter_3", "tide_rock_islet"}:
        # Bring a few already-painted midtones above the brightest Tide ground set.
        arr = np.array(src)
        value = arr[:, :, :3].max(2)
        lit = (arr[:, :, 3] > 120) & (value > 68)
        arr[lit, :3] = np.clip(arr[lit, :3].astype(np.float32) * 1.17, 0, 255).astype(np.uint8)
        src = Image.fromarray(arr, "RGBA")
    canvas = Image.new("RGBA", (cell, cell))
    canvas.alpha_composite(src, ((cell - size[0]) // 2, (cell - size[1]) // 2))
    return canvas


def warp_idle(key, name, phase):
    a = np.asarray(key)
    h, w = a.shape[:2]
    yy, xx = np.indices((h, w), dtype=np.float32)
    cx, cy = (w - 1) / 2, (h - 1) / 2
    xn = (xx - cx) / (w * .42)
    yn = (yy - cy) / (h * .42)
    top = np.clip((.48 - yn) / .75, 0, 1)
    bottom = np.clip((yn + .15) / .72, 0, 1)
    sides = np.clip((np.abs(xn) - .28) / .42, 0, 1)
    dx = np.zeros((h, w), np.float32)
    dy = np.zeros((h, w), np.float32)
    if name == "tide_chaser":
        # Head remains anchored at the lower end while the segmented body bends.
        dx = phase * (5.6 * top * np.sin((yy / h) * 7.2 + 1.0) + 1.7 * sides * top)
        dy = phase * 1.1 * sides * top
    elif name == "tide_alien":
        dx = phase * (3.2 * bottom * np.sin(yy * .10) + 1.2 * sides * top * np.sign(xn))
        dy = phase * (-1.9 * top * np.sign(yn + .55) + 1.2 * bottom * np.sin(xx * .09))
    elif name == "tide_big":
        dx = phase * (2.0 * sides * np.sign(xn) + 2.6 * bottom * np.sin(yy * .064 + xx * .026))
        dy = phase * (-1.8 * top + 1.6 * sides * bottom)
    elif name == "tide_rock_brain":
        # The outer stone stays in place. The maze and polyps roll on its surface.
        rad = np.sqrt(xn * xn + yn * yn)
        inner = np.clip((.82 - rad) / .22, 0, 1)
        angle = phase * .072 * inner
        dx = angle * (yy - cy)
        dy = -angle * (xx - cx)
    elif name == "tide_rock_staghorn":
        dx = phase * 3.7 * top * np.sin(yy * .07 + .8)
        dy = phase * .8 * top * sides
    elif name == "tide_rock_urchin":
        rad = np.sqrt(xn * xn + yn * yn)
        spine = np.clip((rad - .43) / .35, 0, 1)
        dx = phase * 1.7 * spine * xn
        dy = phase * 1.7 * spine * yn
    elif name == "tide_rock_islet":
        dx = phase * (4.1 * np.clip((.0 - yn) / .7, 0, 1) + 1.7 * bottom * np.sin(yy * .08))
        dy = phase * 1.0 * bottom
    else:
        # Fins flare while the centre armour stays fixed; dorsal tail bends.
        dx = phase * (3.1 * top * np.sin(yy * .075 + .4) + 2.6 * sides * np.sign(xn) * (1 - top * .4))
        dy = phase * (2.0 * sides * np.sign(xn) * np.sin(xx * .045) + 1.0 * top)
        if name == "tide_fighter_3":
            dx += phase * 2.8 * (xx > cx) * top
        if name == "tide_fighter_4":
            dx += phase * 2.0 * sides * bottom * np.sign(xn)
    sx = np.clip(np.rint(xx - dx).astype(int), 0, w - 1)
    sy = np.clip(np.rint(yy - dy).astype(int), 0, h - 1)
    return Image.fromarray(a[sy, sx], "RGBA")


def pulse_lights(im, strength, name):
    arr = np.array(im)
    rgb = arr[:, :, :3].astype(np.int16)
    a = arr[:, :, 3] > 100
    mintish = (rgb[:, :, 1] > rgb[:, :, 0] * 1.18) & (rgb[:, :, 1] > rgb[:, :, 2] * .96) & (rgb[:, :, 1] > 85)
    mask = a & mintish
    # Preserve fine painterly value differences while changing emissive intensity.
    for c, scale in enumerate((.18, .32, .19)):
        v = rgb[:, :, c]
        v[mask] = np.clip(v[mask] + strength * scale, 0, 255)
        arr[:, :, c] = v.astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


def outer_rim(im):
    arr = np.array(im)
    a = arr[:, :, 3] > 150
    h, w = a.shape
    up = np.zeros_like(a); up[1:] = a[:-1]
    left = np.zeros_like(a); left[:, 1:] = a[:, :-1]
    edge = a & (~up | ~left)
    yy, xx = np.indices(a.shape)
    edge &= (xx < w * .58) | (yy < h * .42)
    # Only light existing pale outlines; keep the dark selective outline on shadow edges.
    value = arr[:, :, :3].max(2)
    edge &= value > 80
    arr[edge, :3] = np.maximum(arr[edge, :3], np.array([169, 219, 196], np.uint8))
    return Image.fromarray(arr, "RGBA")


def fx_pose(key, name, mode):
    cell = key.width
    frame = warp_idle(key, name, .3 if mode == 4 else -.3)
    frame = pulse_lights(frame, 75 if mode == 4 else 132, name)
    d = ImageDraw.Draw(frame)
    cx, cy = cell // 2, cell // 2
    if name == "tide_fighter_3":
        spot = (int(cell * .76), int(cell * .29))
    elif name == "tide_alien":
        spot = (cx, int(cell * .39))
    elif name == "tide_big":
        spot = (cx, int(cell * .75))
    else:
        spot = (cx, int(cell * .68))
    x, y = spot
    rad = (5 if mode == 4 else 9) * (cell / 192)
    r = int(rad)
    d.ellipse((x-r-2,y-r-2,x+r+2,y+r+2),outline=(20,90,74,180),width=2)
    d.ellipse((x-r,y-r,x+r,y+r),fill=WHITE if mode == 5 else MINT,outline=DEEP,width=1)
    d.line((x-r-3,y,x+r+3,y),fill=PALE,width=1)
    d.line((x,y-r-3,x,y+r+3),fill=PALE,width=1)
    if mode == 5:
        if name == "tide_fighter_4":
            points = 8
        elif name == "tide_big":
            points = 5
        else:
            points = 3
        orbit = int(cell * (.34 if name == "tide_fighter_4" else .29))
        for i in range(points):
            if name == "tide_big":
                px = cx + (i-2)*20
                py = cy + 65 + (2-abs(i-2))*6
            else:
                angle = 2 * math.pi * i / points - math.pi / 2
                px = cx + int(orbit * math.cos(angle))
                py = cy + int(orbit * math.sin(angle))
            rr = 3 if name != "tide_big" else 4
            d.ellipse((px-rr-1,py-rr-1,px+rr+1,py+rr+1),fill=DEEP)
            d.ellipse((px-rr,py-rr,px+rr,py+rr),fill=MINT)
            d.point((px-1,py-1),fill=WHITE)
    if name == "tide_big":
        # An opening seam at the maw, while the large shell retains its surface.
        d.arc((cx-23, int(cell*.64), cx+23, int(cell*.91)), 8, 171, fill=PALE if mode == 5 else MINT, width=3)
    if name == "tide_fighter_3":
        d.arc((x-r-3,y-r-3,x+r+3,y+r+3), 200, 330, fill=WHITE,width=2)
    return frame


def hit_pose(key, name):
    frame = key.copy(); d = ImageDraw.Draw(frame)
    cell = key.width; cx, cy = cell//2, cell//2
    # A concentrated flash and branching crack reveal the same painted hull.
    crack = [(cx-17,cy-18),(cx-8,cy-9),(cx-3,cy-14),(cx+3,cy-2),
             (cx-2,cy+9),(cx+13,cy+22)]
    d.line(crack,fill=DEEP,width=4)
    d.line([(x-1,y-1) for x,y in crack],fill=PALE,width=2)
    d.line([(cx+2,cy-1),(cx+14,cy-6),(cx+25,cy-4)],fill=DEEP,width=3)
    d.line([(cx+1,cy-2),(cx+13,cy-7),(cx+24,cy-5)],fill=WHITE,width=1)
    d.line([(cx-2,cy+8),(cx-16,cy+15),(cx-20,cy+26)],fill=DEEP,width=3)
    d.line([(cx-3,cy+7),(cx-17,cy+14),(cx-21,cy+25)],fill=MINT,width=1)
    for dx,dy in [(-22,-19),(18,-27),(25,12),(-18,27)]:
        x,y=cx+dx,cy+dy
        d.polygon([(x,y-3),(x+3,y),(x,y+4),(x-3,y)],fill=BRASS,outline=DEEP)
        d.point((x-1,y-2),fill=PALE)
    d.ellipse((cx-7,cy-7,cx+7,cy+7),fill=MINT,outline=PALE,width=1)
    d.ellipse((cx-4,cy-4,cx+4,cy+4),fill=WHITE)
    d.point((cx-1,cy-1),fill=WHITE)
    return frame


def main():
    for name,(candidate,target) in SPEC.items():
        key = outer_rim(key_pose(name,candidate,target))
        frames = []
        for p,light in zip(PHASE,[0,22,-15,37]):
            frames.append(pulse_lights(warp_idle(key,name,p),light,name))
        frames += [fx_pose(key,name,4),fx_pose(key,name,5),hit_pose(key,name)]
        cell=key.width
        sheet=Image.new("RGBA",(cell*7,cell))
        for i,f in enumerate(frames):sheet.alpha_composite(f,(i*cell,0))
        sheet.save(HERE/(name+".png"),optimize=True)
        key.save(HERE/(name+"_key.png"),optimize=True)
        print(name, sheet.size)

if __name__ == "__main__":main()
