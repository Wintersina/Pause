"""Desert elites at true high resolution: build.py's recipe on a 384 px cell (Z = 2) with a premultiplied area-averaging downsample of
the same painted masters (the old build used NEAREST: ~7 of 8 source texels dropped), soft anti-aliased silhouette edges instead of a
1-bit alpha, then the Desert palette unification. Drawing is the old 192-space code through elite_hd.ZDraw; warps run on elite_hd.zgrid.
Writes ../hd~/ (the Glass Cannon has no master and is not rebuilt).
    python3 build_desert_hd.py [dune_stalker caravan_hauler lens_matriarch scorpion_walker mirror_bastion]
"""
from __future__ import annotations
import math, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageChops, ImageDraw

HERE = Path(__file__).resolve().parent
ART = HERE.parents[3]
sys.path.insert(0, str(ART / "Enemies" / "src~" / "palette_unify")); sys.path.insert(0, str(HERE.parent.parent / "src~"))
import remap
import elite_hd as E
from elite_hd import Z, CELL, ZDraw

OUT = HERE.parent / "hd~"
INK = (5, 8, 14); SUN = (255, 241, 200); PALE = (230, 220, 192); SAND = (217, 203, 160); DUST = (143, 129, 96)

# key: (w, h, core, nozzles, detail crop)
SHIPS = {
    "dune_stalker": (112, 140, (96, 90), ((88, 158), (104, 158)), (66, 24, 126, 84)),
    "caravan_hauler": (115, 109, (96, 86), ((64, 145), (128, 145)), (60, 60, 132, 132)),
    "lens_matriarch": (103, 143, (96, 83), ((90, 163), (102, 163)), (60, 40, 132, 112)),
    "scorpion_walker": (113, 137, (96, 99), ((88, 160), (104, 160)), (66, 20, 126, 80)),
    "mirror_bastion": (116, 128, (96, 78), ((82, 158), (96, 158), (110, 158)), (60, 40, 132, 112)),
}


def palette_clean(im, lift=0.0):
    alpha = np.asarray(im.getchannel("A"))
    hsv = np.asarray(im.convert("RGB").convert("HSV")).copy()
    h = hsv[:, :, 0].astype(float) * 360 / 255; s = hsv[:, :, 1].astype(float) / 255; v = hsv[:, :, 2].astype(float) / 255
    neutral = s < .095
    copper = (~neutral) & ((h < 34) | (h >= 325)); sand = (~neutral) & (h >= 34) & (h < 64)
    glass = (~neutral) & (h >= 64) & (h < 210); indigo = (~neutral) & (h >= 210) & (h < 325)
    h[copper] = 26; s[copper] = np.minimum(s[copper], .58); v[copper] = np.minimum(v[copper], .60)
    h[sand] = 44; s[sand] = np.minimum(s[sand], .325)
    h[glass] = 166; s[glass] = np.minimum(s[glass], .20)
    h[indigo] = 240; s[indigo] = np.minimum(s[indigo], .42); v[indigo] = np.minimum(v[indigo], .30)
    dark = (v < .39) & (alpha > 127) & (~indigo)
    v[dark] = np.minimum(.48, v[dark] * 1.16 + .015)
    if lift:
        material = (alpha > 127) & (~indigo)
        v[material] = np.minimum(1, v[material] + lift); v[copper] = np.minimum(v[copper], .60)
    bn = neutral & (v > .80)
    h[bn] = 44; s[bn] = .17
    hsv[:, :, 0] = np.rint(h * 255 / 360).astype(np.uint8); hsv[:, :, 1] = np.rint(s * 255).astype(np.uint8); hsv[:, :, 2] = np.rint(v * 255).astype(np.uint8)
    rgb = Image.fromarray(hsv, "HSV").convert("RGB")
    rgb.putalpha(Image.fromarray(alpha, "L"))
    return rgb


def make_base(key):
    w, h, *_ = SHIPS[key]
    original = Image.open(HERE / f"{key}_master.png").convert("RGBA")
    crop = E.fit_to_master(original, 128, (w, h), key)
    crop = palette_clean(crop, lift=.09 if key == "scorpion_walker" else 0)
    pixels = np.asarray(crop).copy()
    pixels[:, :, 3] = np.where(pixels[:, :, 3] >= 240, 255, np.where(pixels[:, :, 3] < 24, 0, pixels[:, :, 3])).astype(np.uint8)
    pixels[pixels[:, :, 3] == 0, :3] = 0
    base = Image.new("RGBA", (CELL, CELL))
    base.alpha_composite(Image.fromarray(pixels, "RGBA"), ((CELL - w * Z) // 2, (CELL - h * Z) // 2))
    arr = np.asarray(base).copy()
    solid = arr[:, :, 3] >= 128
    ms = E.shift_mask
    edge = solid & (~ms(solid, 0, Z) | ~ms(solid, 0, -Z) | ~ms(solid, Z, 0) | ~ms(solid, -Z, 0))
    inner = solid & ~edge
    rim = inner & (ms(edge, 0, Z) | ms(edge, Z, 0))
    rgb = arr[:, :, :3].astype(float)
    rgb[edge] = rgb[edge] * .22 + np.array(INK) * .78
    rgb[rim] = rgb[rim] * .42 + np.array(PALE) * .58
    arr[:, :, :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    rendered = Image.fromarray(arr, "RGBA")
    hsv = np.asarray(rendered.convert("RGB").convert("HSV")).copy()
    hue = hsv[:, :, 0].astype(float) * 360 / 255; sat = hsv[:, :, 1].astype(float) / 255
    stray = (hue >= 250) & (hue <= 300) & (sat > .2) & solid
    if np.any(stray):
        hsv[:, :, 0][stray] = round(236 / 360 * 255); hsv[:, :, 1][stray] = np.minimum(hsv[:, :, 1][stray], round(.38 * 255))
        fixed = np.asarray(Image.fromarray(hsv, "HSV").convert("RGB"))
        arr[stray, :3] = fixed[stray]
    return Image.fromarray(arr, "RGBA")


def moving_parts(base, key, state):
    if state in (1, 6): return base.copy()
    a = np.asarray(base)
    yy, xx = E.zgrid()
    outer = np.clip((np.abs(xx - 96) - 12) / 38, 0, 1)
    if key == "lens_matriarch": region = (yy < 106) & (np.abs(xx - 96) > 12)
    elif key == "dune_stalker": region = (yy > 68) & (np.abs(xx - 96) > 16)
    elif key == "caravan_hauler": region = (yy > 72) & (yy < 151) & (np.abs(xx - 96) > 19)
    elif key == "scorpion_walker": region = (yy > 76) & (np.abs(xx - 96) > 15)
    else: region = (yy < 145) & (np.abs(xx - 96) > 20)
    if state == 4: dy = np.where(xx < 96, -5., 4.) * outer; dx = (-2.0 - np.where(xx < 96, 1.0, 0.0)) * outer
    elif state == 5: dy = np.where(xx < 96, 4., -5.) * outer; dx = (2.0 + np.where(xx > 96, 1.0, 0.0)) * outer
    else:
        phase = {0: -2.0, 2: 1.5, 3: 2.2}.get(state, 0)
        dy = phase * outer; dx = np.sign(xx - 96) * phase * .75 * outer
    warped = E.zsample(a, yy - dy * region, xx - dx * region).copy()
    if state in (4, 5):
        sign = -1 if state == 4 else 1
        warped = E.zsample(warped, yy, xx - sign * 1.3 * (96 - yy) / 96)
    return Image.fromarray(warped, "RGBA")


def flight_frame(base, key, state):
    _, _, core, nozzles, _ = SHIPS[key]
    frame = moving_parts(base, key, state)
    pix = np.asarray(frame).copy()
    if state in (0, 1, 6):
        lamp = (pix[:, :, :3].max(axis=2) > 210) & (pix[:, :, 3] > 0)
        pix[lamp, :3] = np.rint(pix[lamp, :3] * (.73 if state == 0 else .88 if state == 1 else .78)).astype(np.uint8)
        frame = Image.fromarray(pix, "RGBA")
    marks = Image.new("RGBA", (CELL, CELL)); d = ZDraw(marks)
    cx, cy = core
    if state in (1, 2, 3, 4, 5):
        if state == 1: d.point((cx, cy), fill=(*SAND, 255))
        else:
            d.ellipse((cx - 2, cy - 2, cx + 2, cy + 2), fill=(*PALE, 220)); d.point((cx, cy), fill=(*SUN, 255))
    if 2 <= state <= 5:
        for i, (x, y) in enumerate(nozzles):
            length = (12 if state == 2 else 6) + (i + state) % 2
            d.polygon(((x - 2, y), (x + 2, y), (x, min(174, y + length))), fill=(*PALE, 170))
            d.line(((x, y), (x, min(173, y + length - 2))), fill=(*SUN, 255), width=1)
        m = -2 if state == 4 else 2 if state == 5 else 0
        if key == "dune_stalker":
            for x, y in ((50 + m, 117), (142 + m, 117)):
                d.line(((x, y), (x + (-3 if x < 96 else 3), y + 5)), fill=(*PALE, 230), width=1)
        elif key == "caravan_hauler":
            d.arc((81 + m, 113, 111 + m, 139), 20, 165, fill=(*PALE, 220), width=1); d.point((96 + m, 135), fill=(*SUN, 255))
        elif key == "lens_matriarch":
            for x, y in ((66 + m, 62), (126 + m, 62), (73 + m, 78), (119 + m, 78)): d.point((x, y), fill=(*SUN, 255))
        elif key == "scorpion_walker":
            d.line(((62 + m, 128), (57 + m, 134)), fill=(*PALE, 245), width=1); d.line(((130 + m, 128), (135 + m, 134)), fill=(*PALE, 245), width=1)
            d.point((105 + m, 36), fill=(255, 235, 236, 255)); d.point((106 + m, 36), fill=(255, 235, 236, 255))
        else:
            for x, y in ((48 + m, 82), (144 + m, 82), (62 + m, 51), (130 + m, 51)): d.point((x, y), fill=(*PALE, 245))
    if state == 6:
        crack = [(cx - 15, cy - 16), (cx - 4, cy - 8), (cx - 9, cy + 1), (cx + 4, cy + 8), (cx + 12, cy + 21)]
        d.line(crack, fill=(*INK, 255), width=4); d.line([(x - 2, y - 1) for x, y in crack], fill=(*PALE, 255), width=1)
        for ox, oy in ((-17, -16), (18, 10), (5, 23), (-24, 7)):
            d.point((cx + ox, cy + oy), fill=(*SUN, 255)); d.point((cx + ox + 1, cy + oy - 1), fill=(*SAND, 255))
    frame.alpha_composite(marks)
    return frame


def death_frames(key, hover, damaged):
    cx, cy = SHIPS[key][2]
    flash = hover.copy(); d = ZDraw(flash)
    d.ellipse((cx - 8, cy - 8, cx + 8, cy + 8), fill=(*SUN, 255))
    for deg in range(0, 360, 45):
        t = math.radians(deg)
        d.line(((cx + round(8 * math.cos(t)), cy + round(8 * math.sin(t))), (cx + round(26 * math.cos(t)), cy + round(26 * math.sin(t)))), fill=(*PALE, 235), width=2)
    rupture = Image.new("RGBA", (CELL, CELL))
    rupture.alpha_composite(damaged.crop((0, 0, cx * Z, CELL)), (-7 * Z, -4 * Z)); rupture.alpha_composite(damaged.crop((cx * Z, 0, CELL, CELL)), ((cx + 7) * Z, 4 * Z))
    d = ZDraw(rupture)
    for poly in (((cx - 37, cy - 29), (cx - 11, cy - 20), (cx - 23, cy + 18), (cx - 48, cy + 11)), ((cx + 13, cy - 5), (cx + 40, cy - 19), (cx + 45, cy + 25), (cx + 22, cy + 31)), ((cx - 7, cy - 34), (cx + 6, cy - 20), (cx + 1, cy + 25), (cx - 10, cy + 31))):
        d.polygon(poly, fill=(0, 0, 0, 0))
    d.polygon(((cx, cy - 19), (cx + 13, cy), (cx, cy + 20), (cx - 13, cy)), fill=(*PALE, 230)); d.ellipse((cx - 5, cy - 5, cx + 5, cy + 5), fill=(*SUN, 255))
    for x, y in ((cx - 31, cy - 20), (cx + 36, cy + 12), (cx - 18, cy + 28), (cx + 19, cy - 25)):
        d.line(((x - 3, y), (x + 3, y - 2)), fill=(*SUN, 220), width=1)
    debris = Image.new("RGBA", (CELL, CELL))
    arr = np.asarray(hover)
    pieces = ((59, 57, 14, 18, -22, -21), (119, 62, 14, 18, 22, -18), (74, 113, 13, 17, -20, 18), (107, 111, 13, 16, 21, 20), (91, 77, 11, 13, 0, -27), (51, 96, 15, 16, -27, 1),
              (126, 96, 15, 16, 27, -2), (81, 95, 12, 15, -10, -15), (102, 93, 12, 15, 11, -13), (88, 126, 11, 15, -4, 17), (111, 48, 10, 14, 16, -18), (73, 49, 10, 14, -15, -17))
    for i, (bx, by, bw, bh, dx, dy) in enumerate(pieces):
        part = Image.fromarray(arr[by * Z:(by + bh) * Z, bx * Z:(bx + bw) * Z].copy(), "RGBA")
        W_, H_ = bw * Z, bh * Z
        mask = Image.new("L", (W_, H_)); md = ImageDraw.Draw(mask)
        if i % 2: md.polygon(((0, 0), (W_ - 1, 0), (W_ // 2, H_ - 1), (0, H_ - 4 * Z)), fill=255)
        else: md.polygon(((W_ // 2, 0), (W_ - 1, H_ // 3), (W_ - 1, H_ - 1), (0, H_ - 1)), fill=255)
        part.putalpha(ImageChops.multiply(part.getchannel("A"), mask))
        debris.alpha_composite(part, ((bx + dx) * Z, (by + dy) * Z))
    d = ZDraw(debris)
    for deg in range(0, 360, 30):
        t = math.radians(deg); x = cx + round(30 * math.cos(t)); y = cy + round(30 * math.sin(t))
        d.point((x, y), fill=(*SUN, 255)); d.point((x + 1, y + 1), fill=(*SAND, 220))
    for x, y, r in ((58, 83, 4), (139, 75, 4), (68, 133, 5), (126, 124, 5), (80, 58, 3), (112, 145, 3), (96, 96, 6)):
        d.ellipse((x - r, y - r // 2, x + r, y + r // 2), fill=(*DUST, 130))
    return [flash, rupture, debris]


def build(key):
    name = f"desert_elite_{key}"
    base = make_base(key)
    frames = [flight_frame(base, key, n) for n in range(7)]
    strip = E.paste_strip(frames)
    death = E.paste_strip(death_frames(key, frames[3], frames[6]))
    E.emit(OUT, name, strip, death, ART / "Resources/Elites/Defs", HERE.parent / f"{name}.png", SHIPS[key][4], lambda a: remap.remap_rgba(a, "desert"))


if __name__ == "__main__":
    OUT.mkdir(exist_ok=True)
    for n in (sys.argv[1:] or list(SHIPS)): build(n)
