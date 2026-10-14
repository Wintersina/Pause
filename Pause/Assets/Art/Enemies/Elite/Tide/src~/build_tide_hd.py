"""All Tide elites at true high resolution: the Thunderfin recipe (build_thunderfin_hd.py) for every Tide ship with a painted master.

The same painted masters (*_master.png, 1254 px), the same seven poses / hardpoints / death frames, built on a 384 px cell
(Z = 2 x the 192 strip) with an area-averaging (Lanczos) downsample instead of build_tide_elites.py's nearest-neighbour decimation
(~7 of 8 source texels dropped: the noisy, blotchy look). Everything else (hue bend away from red, mint rim glints, bank warp,
exhaust, core dot, damage crack, death frames) is the original recipe with its coordinates x Z, then the Tide palette unification.
Writes ../hd~/<key>.png (2688 x 384), <key>_death.png (1152 x 384), <key>.json (the def with cellPixels 384, points x Z) and
<key>_before_after.png.
    python3 build_tide_hd.py [riptide bathyscaphe trawler pearl_warden thunderfin]     (default: the four not yet HD)
"""
from __future__ import annotations
import colorsys, json, math, sys
from dataclasses import dataclass
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ART = HERE.parents[3]                                      # Pause/Assets/Art
sys.path.insert(0, str(ART / "Enemies" / "src~" / "palette_unify"))   # the Tide palette unification (brass / olive -> deep teal)
sys.path.insert(0, str(HERE.parent.parent / "src~"))          # elite_hd
import remap
import elite_hd as E
from elite_hd import Z, CELL as SIZE

OUT = HERE.parent / "hd~"
MINT, DEEP, BONE, VIOLET = (124, 242, 192), (24, 76, 70), (229, 239, 216), (104, 108, 184)


@dataclass(frozen=True)
class Ship:
    name: str
    w: int
    h: int
    nozzles: tuple
    core: tuple


SHIPS = {s.name: s for s in (
    Ship("riptide", 103, 148, ((87, 160), (105, 160)), (96, 72)),
    Ship("bathyscaphe", 122, 109, ((64, 142), (128, 142)), (96, 91)),
    Ship("trawler", 124, 111, ((80, 145), (112, 145)), (96, 67)),
    Ship("thunderfin", 112, 127, ((89, 151), (103, 151)), (96, 71)),
    Ship("pearl_warden", 120, 113, ((71, 144), (121, 144)), (96, 77)),
)}
REGIONS = {"riptide": (65, 137), "bathyscaphe": (76, 148), "trawler": (56, 135), "thunderfin": (56, 123), "pearl_warden": (45, 135)}
DETAIL_CROP = {"riptide": (66, 20, 126, 80), "bathyscaphe": (60, 60, 132, 132), "trawler": (60, 50, 132, 122), "thunderfin": (66, 20, 126, 80), "pearl_warden": (60, 50, 132, 110)}


def paint_master(ship: Ship) -> Image.Image:
    original = Image.open(HERE / f"{ship.name}_master.png").convert("RGBA")
    small = E.fit_to_master(original, 32, (ship.w, ship.h), ship.name)
    pixels = np.asarray(small).copy()
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
    sprite.alpha_composite(Image.fromarray(pixels), ((SIZE - w) // 2, 96 * Z - h // 2))
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


def warp_parts(im, ship, state):
    if state not in (0, 2, 3, 4, 5): return im
    arr = np.asarray(im)
    yy, xx = np.indices((SIZE, SIZE))
    side = np.sign(xx - 96 * Z)
    outer = np.clip((np.abs(xx - 96 * Z) - 17 * Z) / (43 * Z), 0, 1)
    y0, y1 = REGIONS[ship.name]
    region = (yy > y0 * Z) & (yy < y1 * Z)
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


def draw_exhaust(frame, ship, state):
    if state < 2: return
    layer = Image.new("RGBA", frame.size); d = ImageDraw.Draw(layer)
    length = (11 if state == 2 else 6) * Z
    for i, (x, y) in enumerate(ship.nozzles):
        x, y = x * Z, y * Z
        tip = min(175 * Z, y + length + ((state + i) % 2) * Z)
        d.polygon(((x - 4 * Z, y - 2 * Z), (x + 4 * Z, y - 2 * Z), (x + 3 * Z, y + 2 * Z), (x, tip), (x - 3 * Z, y + 2 * Z)), fill=(33, 125, 111, 105))
        d.polygon(((x - 2 * Z, y), (x + 2 * Z, y), (x, max(y + Z, tip - 3 * Z))), fill=(124, 242, 192, 230))
        d.ellipse((x - 1, y + Z - 1, x + 1, y + Z + 1), fill=(245, 255, 243, 255))
    frame.alpha_composite(layer)


def details(base, ship, state):
    frame = base.copy()
    if state in (0, 1): frame = dim_mint(frame, .42 if state == 0 else .7)
    elif state == 2: frame = dim_mint(frame, 1.15)
    elif state == 6: frame = dim_mint(frame, .65)
    frame = warp_parts(frame, ship, state)
    draw_exhaust(frame, ship, state)
    marks = Image.new("RGBA", frame.size); d = ImageDraw.Draw(marks)
    if state > 0 and state != 6: dot(d, tuple(v * Z for v in ship.core), 1 if state == 1 else 2)
    if state in (3, 4, 5):
        P = lambda *pts: [(x * Z, y * Z) for x, y in pts]
        flip = -1 if state == 4 else 1
        if ship.name == "thunderfin":
            d.line(P((67, 75), (72, 70 + flip), (76, 74), (81, 67)), fill=(*MINT, 230), width=Z)
            d.line(P((125, 75), (120, 70 - flip), (116, 74), (111, 67)), fill=(*VIOLET, 230), width=Z)
        elif ship.name == "trawler":
            y = 115 + (-2 if state == 4 else 2 if state == 5 else 0)
            d.arc((59 * Z, (y - 5) * Z, 133 * Z, (y + 8) * Z), 4, 176, fill=(109, 174, 139, 235), width=Z)
        elif ship.name == "pearl_warden":
            d.arc((82 * Z, 64 * Z, 110 * Z, 93 * Z), 203, 337, fill=(*BONE, 240), width=Z)
        elif ship.name == "riptide":
            d.line(P((96, 28), (96, 39)), fill=(*BONE, 210), width=Z)
    if state == 6:
        cx, cy = (v * Z for v in ship.core)
        jag = [(cx + a * Z, cy + b * Z) for a, b in ((-12, -9), (-6, -3), (-10, 4), (1, 8), (5, 17))]
        d.line(jag, fill=(3, 16, 17, 255), width=5 * Z)
        d.line([(x - 2 * Z, y - Z) for x, y in jag], fill=(*BONE, 255), width=Z)
        d.line(((cx + 5 * Z, cy + 17 * Z), (cx + 12 * Z, cy + 10 * Z), (cx + 15 * Z, cy + 3 * Z)), fill=(*MINT, 255), width=Z)
        for ox, oy in ((-16, -15), (19, 14), (6, 22), (-22, 9)):
            d.rectangle((cx + ox * Z, cy + oy * Z, cx + ox * Z + Z - 1, cy + oy * Z + Z - 1), fill=(*MINT, 255))
            d.rectangle((cx + (ox + 1) * Z, cy + (oy - 1) * Z, cx + (ox + 1) * Z + Z - 1, cy + (oy - 1) * Z + Z - 1), fill=(*BONE, 255))
    frame.alpha_composite(marks)
    return frame


def death_frames(ship, damaged):
    cx, cy = (v * Z for v in ship.core)
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
    sample = np.asarray(damaged); rng = np.random.default_rng(sum(ord(c) for c in ship.name))
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


def build(name: str):
    ship = SHIPS[name]
    key = f"tide_elite_{name}"
    base = paint_master(ship)
    frames = [details(base, ship, i) for i in range(7)]
    strip = E.paste_strip(frames)
    strip = Image.fromarray(remap.remap_rgba(np.asarray(strip), "tide"), "RGBA")
    strip.save(OUT / f"{key}.png", optimize=True)
    death = Image.new("RGBA", (SIZE * 3, SIZE))
    for i, f in enumerate(death_frames(ship, frames[6])): death.alpha_composite(f, (i * SIZE, 0))
    death = Image.fromarray(remap.remap_rgba(np.asarray(death), "tide"), "RGBA")
    death.save(OUT / f"{key}_death.png", optimize=True)
    d = json.load(open(HERE.parents[3] / f"Resources/Elites/Defs/{key}.json"))
    if d.get("cellPixels", 192) == 192:                 # (the Thunderfin def is already HD: leave it)
        d["cellPixels"] = SIZE
        for k in ("muzzles", "nozzles"):
            for p in d[k]:
                p["x"] = round(p["x"] * Z); p["y"] = round(p["y"] * Z)
    json.dump(d, open(OUT / f"{key}.json", "w"), indent=2); open(OUT / f"{key}.json", "a").write("\n")
    old = HERE.parent / f"{key}.png"
    if d["cellPixels"] == 384 and name == "thunderfin": old = HERE.parent / "thunderfin_hd~" / f"{key}.png"
    E.before_after(old, strip, d["cellWorldSize"], OUT / f"{key}_before_after.png", name, DETAIL_CROP[name])
    print("ok", key, strip.size, death.size)


def main():
    OUT.mkdir(exist_ok=True)
    names = sys.argv[1:] or ["riptide", "bathyscaphe", "trawler", "pearl_warden"]
    for n in names: build(n)


if __name__ == "__main__":
    main()
