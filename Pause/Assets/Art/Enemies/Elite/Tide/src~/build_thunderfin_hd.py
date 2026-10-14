"""Thunderfin at true high resolution (staged, not wired): the same painted master (thunderfin_master.png, 1254 px), the same seven
poses and hardpoints, built on a 384 px cell (Z = 2 x the 192 strip) with an area-averaging (Lanczos) downsample instead of the
nearest-neighbour decimation of build_tide_elites.py, which threw away ~7 of 8 source texels (the noisy, blotchy look). Everything
else (hue bend away from red, mint rim glints, bank warp, exhaust, core dot, damage crack, death frames) is the original recipe with
its coordinates x Z. Writes ../thunderfin_hd~/ : tide_elite_thunderfin.png (2688 x 384), tide_elite_thunderfin_death.png (1152 x 384),
tide_elite_thunderfin.json (the def with cellPixels 384 and the points x Z), before_after.png.
    python3 build_thunderfin_hd.py
"""
from __future__ import annotations
import colorsys, json, math, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[2] / 'src~' / 'palette_unify'))   # the Tide palette unification (brass / olive -> deep teal): the live strips went through it
import remap
OUT = HERE.parent / "thunderfin_hd~"
Z = 2
SIZE = 192 * Z
MINT, DEEP, BONE, VIOLET = (124, 242, 192), (24, 76, 70), (229, 239, 216), (104, 108, 184)
W, H, CENTER_Y = 112 * Z, 127 * Z, 96 * Z
NOZZLES = ((89 * Z, 151 * Z), (103 * Z, 151 * Z))
CORE = (96 * Z, 71 * Z)


def paint_master() -> Image.Image:
    original = Image.open(HERE / "thunderfin_master.png").convert("RGBA")
    bbox = original.getchannel("A").point(lambda a: 255 if a >= 32 else 0).getbbox()
    cropped = original.crop(bbox)
    # premultiplied area-average downsample (no dark fringes), then the original's alpha clean-up
    arr = np.asarray(cropped).astype(np.float32)
    pre = arr.copy(); pre[:, :, :3] *= arr[:, :, 3:4] / 255.0
    small = Image.fromarray(np.clip(pre, 0, 255).astype(np.uint8), "RGBA").resize((W, H), Image.Resampling.LANCZOS)
    p = np.asarray(small).astype(np.float32)
    a = p[:, :, 3:4]
    rgb = np.where(a > 0, p[:, :, :3] * 255.0 / np.maximum(a, 1), 0)
    pixels = np.concatenate([np.clip(rgb, 0, 255), a], axis=2).astype(np.uint8)
    h, w = pixels.shape[:2]
    for y in range(h):
        for x in range(w):
            r, g, b, al = map(int, pixels[y, x])
            if al < 24:
                pixels[y, x] = (0, 0, 0, 0); continue
            hue, sat, val = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            deg = hue * 360
            if sat > .15 and (deg >= 345 or deg < 16): deg = 21          # never red: bend to the Tide bronze side
            if 15 <= deg <= 42 and sat > .45: sat = .45
            nr, ng, nb = colorsys.hsv_to_rgb(deg / 360, sat, val)
            pixels[y, x, :3] = (round(nr * 255), round(ng * 255), round(nb * 255))
            if al >= 240: pixels[y, x, 3] = 255
    sprite = Image.new("RGBA", (SIZE, SIZE))
    sprite.alpha_composite(Image.fromarray(pixels), ((SIZE - W) // 2, CENTER_Y - H // 2))
    # mint rim glints on the upper and left-facing edges (2 px x Z), sparse so the painting stays painted
    arr = np.asarray(sprite).copy()
    solid = arr[:, :, 3] >= 180
    rim = np.zeros_like(solid)
    for y in range(19 * Z, 154 * Z):
        xs = np.flatnonzero(solid[y])
        if len(xs):
            for x in range(int(xs[0]) + 1, int(xs[0]) + 1 + 2 * Z):
                if x < SIZE and solid[y, x]: rim[y, x] = True
    for x in range(30 * Z, 163 * Z):
        ys = np.flatnonzero(solid[:, x])
        if len(ys):
            for y in range(int(ys[0]) + 1, int(ys[0]) + 1 + 2 * Z):
                if y < SIZE and solid[y, x]: rim[y, x] = True
    yy, xx = np.indices(solid.shape)
    rim &= (((xx // Z) * 3 + (yy // Z) * 5) % 7 != 0)
    lit = arr[:, :, :3].astype(np.float32)
    lit[rim] = lit[rim] * .48 + np.array((174, 230, 203)) * .52
    arr[:, :, :3] = np.clip(lit, 0, 255).astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


def warp_parts(im, state):
    if state not in (0, 2, 3, 4, 5): return im
    arr = np.asarray(im)
    yy, xx = np.indices((SIZE, SIZE))
    side = np.sign(xx - 96 * Z)
    outer = np.clip((np.abs(xx - 96 * Z) - 17 * Z) / (43 * Z), 0, 1)
    region = (yy > 56 * Z) & (yy < 123 * Z)
    amount = {0: -3.0, 2: 1.0, 3: 1.5, 4: 8.0, 5: 8.0}[state] * Z
    if state == 4:
        dy = -amount * outer * (side < 0) + amount * outer * (side > 0) * .45; dx = -1.4 * Z * outer
    elif state == 5:
        dy = amount * outer * (side < 0) * .45 - amount * outer * (side > 0); dx = 1.4 * Z * outer
    else:
        dy = amount * outer * (1 if state != 0 else -1); dx = amount * outer * side * .45
    sy = np.clip(np.rint(yy - dy * region).astype(int), 0, SIZE - 1)
    sx = np.clip(np.rint(xx - dx * region).astype(int), 0, SIZE - 1)
    warped = arr[sy, sx].copy()
    if state in (4, 5):
        sign = -1 if state == 4 else 1
        shift = np.rint(sign * 2 * Z * (96 * Z - yy) / (96 * Z)).astype(int)
        warped = warped[yy, np.clip(xx - shift, 0, SIZE - 1)]
    return Image.fromarray(warped, "RGBA")


def dim_mint(im, factor):
    if factor == 1: return im
    arr = np.asarray(im).copy()
    for y, x in zip(*np.where(arr[:, :, 3] > 0)):
        r, g, b = map(int, arr[y, x, :3])
        hue, sat, val = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        if 125 <= hue * 360 <= 190 and sat > .15 and val > .35:
            rr, gg, bb = colorsys.hsv_to_rgb(hue, sat, min(1, val * factor))
            arr[y, x, :3] = (round(rr * 255), round(gg * 255), round(bb * 255))
    return Image.fromarray(arr, "RGBA")


def dot(d, p, strength):
    x, y = p
    if strength <= 0: return
    r = 4 * Z
    d.ellipse((x - r, y - r, x + r, y + r), fill=(*DEEP, 85 if strength == 1 else 120))
    d.ellipse((x - r // 2, y - r // 2, x + r // 2, y + r // 2), fill=(*MINT, 200))
    d.ellipse((x - 1, y - 1, x + 1, y + 1), fill=(243, 255, 239, 255))


def draw_exhaust(frame, state):
    if state < 2: return
    layer = Image.new("RGBA", frame.size); d = ImageDraw.Draw(layer)
    length = (11 if state == 2 else 6) * Z
    for i, (x, y) in enumerate(NOZZLES):
        tip = min(175 * Z, y + length + ((state + i) % 2) * Z)
        d.polygon(((x - 4 * Z, y - 2 * Z), (x + 4 * Z, y - 2 * Z), (x + 3 * Z, y + 2 * Z), (x, tip), (x - 3 * Z, y + 2 * Z)), fill=(33, 125, 111, 105))
        d.polygon(((x - 2 * Z, y), (x + 2 * Z, y), (x, max(y + Z, tip - 3 * Z))), fill=(124, 242, 192, 230))
        d.ellipse((x - 1, y + Z - 1, x + 1, y + Z + 1), fill=(245, 255, 243, 255))
    frame.alpha_composite(layer)


def details(base, state):
    frame = base.copy()
    if state in (0, 1): frame = dim_mint(frame, .42 if state == 0 else .7)
    elif state == 2: frame = dim_mint(frame, 1.15)
    elif state == 6: frame = dim_mint(frame, .65)
    frame = warp_parts(frame, state)
    draw_exhaust(frame, state)
    marks = Image.new("RGBA", frame.size); d = ImageDraw.Draw(marks)
    if state > 0 and state != 6: dot(d, CORE, 1 if state == 1 else 2)
    if state in (3, 4, 5):
        flip = -1 if state == 4 else 1
        P = lambda *pts: [(x * Z, y * Z) for x, y in pts]
        d.line(P((67, 75), (72, 70 + flip), (76, 74), (81, 67)), fill=(*MINT, 230), width=Z)
        d.line(P((125, 75), (120, 70 - flip), (116, 74), (111, 67)), fill=(*VIOLET, 230), width=Z)
    if state == 6:
        cx, cy = CORE
        jag = [(cx + a * Z, cy + b * Z) for a, b in ((-12, -9), (-6, -3), (-10, 4), (1, 8), (5, 17))]
        d.line(jag, fill=(3, 16, 17, 255), width=5 * Z)
        d.line([(x - 2 * Z, y - Z) for x, y in jag], fill=(*BONE, 255), width=Z)
        d.line(((cx + 5 * Z, cy + 17 * Z), (cx + 12 * Z, cy + 10 * Z), (cx + 15 * Z, cy + 3 * Z)), fill=(*MINT, 255), width=Z)
        for ox, oy in ((-16, -15), (19, 14), (6, 22), (-22, 9)):
            d.rectangle((cx + ox * Z, cy + oy * Z, cx + ox * Z + Z - 1, cy + oy * Z + Z - 1), fill=(*MINT, 255))
    frame.alpha_composite(marks)
    return frame


def death_frames(damaged):
    cx, cy = CORE
    out = []
    flash = damaged.copy(); a = np.asarray(flash).copy()
    solid = a[:, :, 3] > 100; light = a[:, :, :3].astype(np.float32)
    light[solid] = light[solid] * .52 + np.array((194, 246, 220), np.float32) * .48
    a[:, :, :3] = np.clip(light, 0, 255).astype(np.uint8)
    flash = Image.fromarray(a, "RGBA"); d = ImageDraw.Draw(flash)
    d.ellipse((cx - 14 * Z, cy - 14 * Z, cx + 14 * Z, cy + 14 * Z), fill=(*MINT, 145))
    d.ellipse((cx - 5 * Z, cy - 5 * Z, cx + 5 * Z, cy + 5 * Z), fill=(245, 255, 238, 255))
    for ang in range(0, 360, 45):
        r = math.radians(ang)
        d.line(((cx + int(7 * Z * math.cos(r)), cy + int(7 * Z * math.sin(r))), (cx + int(23 * Z * math.cos(r)), cy + int(23 * Z * math.sin(r)))), fill=(*MINT, 240), width=2 * Z)
    out.append(flash)
    rupture = Image.new("RGBA", (SIZE, SIZE))
    rupture.alpha_composite(damaged.crop((0, 0, 96 * Z, SIZE)), (-7 * Z, -3 * Z))
    rupture.alpha_composite(damaged.crop((96 * Z, 0, SIZE, SIZE)), (103 * Z, 3 * Z))
    d = ImageDraw.Draw(rupture)
    d.polygon(((cx, cy - 22 * Z), (cx + 15 * Z, cy), (cx, cy + 24 * Z), (cx - 15 * Z, cy)), fill=(*DEEP, 235))
    d.ellipse((cx - 9 * Z, cy - 9 * Z, cx + 9 * Z, cy + 9 * Z), fill=(*MINT, 255))
    d.ellipse((cx - 3 * Z, cy - 3 * Z, cx + 3 * Z, cy + 3 * Z), fill=(245, 255, 237, 255))
    for ang in range(15, 360, 45):
        r = math.radians(ang)
        d.line(((cx + int(18 * Z * math.cos(r)), cy + int(18 * Z * math.sin(r))), (cx + int(39 * Z * math.cos(r)), cy + int(39 * Z * math.sin(r)))), fill=(*BONE, 225), width=Z)
    out.append(rupture)
    debris = Image.new("RGBA", (SIZE, SIZE)); d = ImageDraw.Draw(debris)
    for radius, alpha in ((23, 120), (31, 80), (40, 42)):
        rr = radius * Z
        d.arc((cx - rr, cy - rr, cx + rr, cy + rr), 24, 158, fill=(*MINT, alpha), width=2 * Z)
        d.arc((cx - rr, cy - rr, cx + rr, cy + rr), 204, 334, fill=(*MINT, alpha), width=2 * Z)
    sample = np.asarray(damaged); rng = np.random.default_rng(sum(ord(c) for c in "thunderfin"))
    opaque = np.argwhere(sample[:, :, 3] > 230)
    for _ in range(26):
        sy, sx = opaque[rng.integers(len(opaque))]
        col = tuple(int(v) for v in sample[sy, sx, :3]) + (240,)
        ang, rad = rng.random() * math.tau, rng.uniform(16, 63) * Z
        x, y = int(cx + rad * math.cos(ang)), int(cy + rad * math.sin(ang))
        if 17 * Z <= x < 175 * Z and 17 * Z <= y < 175 * Z:
            d.polygon(((x, y), (x + 3 * Z, y + Z), (x + Z, y + 4 * Z)), fill=col)
    d.ellipse((cx - 3 * Z, cy - 3 * Z, cx + 3 * Z, cy + 3 * Z), fill=(*MINT, 190))
    out.append(debris)
    return out


def main():
    OUT.mkdir(exist_ok=True)
    base = paint_master()
    frames = [details(base, i) for i in range(7)]
    strip = Image.new("RGBA", (SIZE * 7, SIZE))
    for i, f in enumerate(frames): strip.alpha_composite(f, (i * SIZE, 0))
    strip = Image.fromarray(remap.remap_rgba(np.asarray(strip), "tide"), "RGBA")
    strip.save(OUT / "tide_elite_thunderfin.png", optimize=True)
    death = Image.new("RGBA", (SIZE * 3, SIZE))
    for i, f in enumerate(death_frames(frames[6])): death.alpha_composite(f, (i * SIZE, 0))
    death = Image.fromarray(remap.remap_rgba(np.asarray(death), "tide"), "RGBA")
    death.save(OUT / "tide_elite_thunderfin_death.png", optimize=True)
    defp = HERE.parents[3] / "Resources/Elites/Defs/tide_elite_thunderfin.json"
    d = json.load(open(defp))
    d["cellPixels"] = SIZE
    for key in ("muzzles", "nozzles"):
        for p in d[key]:
            p["x"] = round(p["x"] * Z); p["y"] = round(p["y"] * Z)
    json.dump(d, open(OUT / "tide_elite_thunderfin.json", "w"), indent=2); open(OUT / "tide_elite_thunderfin.json", "a").write("\n")
    # before / after at the same on-screen scale (1080-wide phone: 145 px/u) on a mid-dark backdrop
    old = Image.open(HERE.parent / "tide_elite_thunderfin.png").convert("RGBA")
    cw = d["cellWorldSize"]; px = int(round(cw * 1080 / 7.44))
    sheet = Image.new("RGBA", (2 * (SIZE + px + 30) + 20, SIZE + 60), (64, 60, 104, 255)); dr = ImageDraw.Draw(sheet)
    dr.text((10, 6), "BEFORE (192 px cell, nearest-decimated)", fill=(255, 255, 255, 255)); dr.text((SIZE + px + 50, 6), "AFTER (384 px cell, area-averaged master)", fill=(255, 255, 255, 255))
    for n, (src, cell) in enumerate(((old, 192), (strip, SIZE))):
        c = src.crop((3 * cell, 0, 4 * cell, cell))
        sheet.alpha_composite(c.resize((SIZE, SIZE), Image.Resampling.NEAREST), (n * (SIZE + px + 50) + 10, 30))
        sheet.alpha_composite(c.resize((px, px), Image.Resampling.LANCZOS), (n * (SIZE + px + 50) + SIZE + 20, 30 + (SIZE - px) // 2))
    sheet.convert("RGB").save(OUT / "before_after.png", optimize=True)
    print("ok", strip.size, death.size)


if __name__ == "__main__":
    main()
