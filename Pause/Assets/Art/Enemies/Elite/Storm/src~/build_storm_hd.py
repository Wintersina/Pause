"""Storm elites at true high resolution: build_storm_elites.py's recipe on a 384 px cell (Z = 2) with a premultiplied area-averaging
downsample of the same painted masters (the old one dropped ~7 of 8 source texels), then the Storm palette unification.
Drawing code is the old 192-space code through elite_hd.ZDraw, the warps run on elite_hd.zgrid. Writes ../hd~/.
    python3 build_storm_hd.py [skyhook_harrier refinery_tender rod_dancer anvil_cloud static_bastion]
"""
from __future__ import annotations
import colorsys, math, sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ART = HERE.parents[3]
sys.path.insert(0, str(ART / "Enemies" / "src~" / "palette_unify")); sys.path.insert(0, str(HERE.parent.parent / "src~"))
import remap
import elite_hd as E
from elite_hd import Z, CELL, ZDraw

OUT = HERE.parent / "hd~"
WHITE = (242, 246, 255); YELLOW = (255, 243, 160); ARC = (154, 201, 229); INK = (5, 9, 16)

# name: (w, h, core, nozzles, detail crop)
SHIPS = {
    "skyhook_harrier": (112, 140, (96, 82), ((86, 158), (106, 158)), (66, 20, 126, 80)),
    "refinery_tender": (120, 100, (96, 96), ((74, 135), (118, 135)), (62, 60, 134, 132)),
    "rod_dancer": (100, 148, (96, 78), ((87, 158), (105, 158)), (60, 20, 132, 92)),
    "anvil_cloud": (122, 126, (96, 85), ((82, 150), (110, 150)), (60, 30, 132, 102)),
    "static_bastion": (124, 132, (96, 89), ((66, 151), (86, 151), (106, 151), (126, 151)), (56, 40, 128, 112)),
}


def clean_master(name):
    w, h, *_ = SHIPS[name]
    original = Image.open(HERE / f"{name}_master.png").convert("RGBA")
    a = np.asarray(E.fit_to_master(original, 32, (w, h), name)).copy()
    hh, ww = a.shape[:2]
    for y in range(hh):
        for x in range(ww):
            r, g, b, alpha = map(int, a[y, x])
            if alpha < 32:
                a[y, x] = (0, 0, 0, 0); continue
            hu, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            deg = hu * 360
            bad = s > .11 and (deg < 52 or deg > 245 or 75 < deg < 175)
            if bad: hu, s = 215 / 360, min(s, .18)
            elif 175 <= deg <= 215 and s > .25: s = .25
            elif s > .30: s = .30
            rr, gg, bb = colorsys.hsv_to_rgb(hu, s, v)
            a[y, x, :3] = round(rr * 255), round(gg * 255), round(bb * 255)
            if alpha >= 240: a[y, x, 3] = 255
    base = Image.new("RGBA", (CELL, CELL))
    base.alpha_composite(Image.fromarray(a, "RGBA"), ((CELL - ww) // 2, (CELL - hh) // 2))
    pix = np.asarray(base).copy()
    solid = pix[:, :, 3] >= 128
    yy, xx = np.indices((CELL, CELL))
    up = E.shift_mask(solid, 0, Z); left = E.shift_mask(solid, Z, 0)
    edge = solid & (~up | ~left)
    rim = solid & (((xx // Z) + (yy // Z)) % 5 != 0) & (~up | ~left)
    interior = solid & (E.shift_mask(rim, 0, Z) | E.shift_mask(rim, Z, 0))
    rgb = pix[:, :, :3].astype(np.float32)
    rgb[edge] = rgb[edge] * .48 + np.array(INK) * .52
    rgb[interior] = rgb[interior] * .60 + np.array((172, 196, 214)) * .40
    rgb[rim] = rgb[rim] * .38 + np.array((196, 215, 229)) * .62
    pix[:, :, :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    return Image.fromarray(pix, "RGBA")


def move_parts(base, name, state):
    if state not in (0, 2, 3, 4, 5): return base.copy()
    a = np.asarray(base)
    yy, xx = E.zgrid()
    outer = np.clip((abs(xx - 96) - 12) / 48, 0, 1)
    if name == "rod_dancer": region = (yy < 105) & (abs(xx - 96) > 12)
    elif name == "skyhook_harrier": region = (yy > 73) & (yy < 144) & (abs(xx - 96) > 17)
    elif name == "refinery_tender": region = (yy > 71) & (yy < 138) & (abs(xx - 96) > 20)
    elif name == "anvil_cloud": region = (yy > 43) & (yy < 116) & (abs(xx - 96) > 19)
    else: region = (yy > 46) & (yy < 135) & (abs(xx - 96) > 22)
    if state == 4: dy = np.where(xx < 96, -4.0, 3.0) * outer; dx = -2.0 * outer
    elif state == 5: dy = np.where(xx < 96, 3.0, -4.0) * outer; dx = 2.0 * outer
    else:
        dy = (1.6 if state in (2, 3) else -1.5) * outer
        dx = np.sign(xx - 96) * (1.5 if state in (2, 3) else -1.0) * outer
    warped = E.zsample(a, yy - dy * region, xx - dx * region).copy()
    if state in (4, 5):
        sign = -1 if state == 4 else 1
        shift = sign * 1.5 * (96 - yy) / 96
        warped = E.zsample(warped, yy, xx - shift)
    return Image.fromarray(warped, "RGBA")


def light_point(d, x, y, intensity):
    if intensity <= 0: return
    d.ellipse((x - 3, y - 3, x + 3, y + 3), fill=(70, 108, 132, 170))
    d.ellipse((x - 1, y - 1, x + 1, y + 1), fill=(*ARC, 230))
    if intensity >= 2:
        d.point((x, y), fill=(*WHITE, 255)); d.point((x + 1, y), fill=(*YELLOW, 255))


MUZZLES = {"skyhook_harrier": [(96, 28)], "refinery_tender": [(96, 126), (63, 92), (129, 92)], "rod_dancer": [(71, 24), (121, 24), (96, 76)],
           "anvil_cloud": [(96, 37)], "static_bastion": [(96, 90), (51, 53), (141, 53)]}


def flight_frame(base, name, state):
    _, _, core, nozzles, _ = SHIPS[name]
    frame = move_parts(base, name, state)
    if state in (0, 1, 6):
        pix = np.asarray(frame).copy()
        k = .69 if state == 0 else .85 if state == 1 else .75
        cold = (pix[:, :, 2] > pix[:, :, 0] * 1.13) & (pix[:, :, 2] > 115)
        pix[cold, :3] = (pix[cold, :3] * k).astype(np.uint8)
        frame = Image.fromarray(pix, "RGBA")
    marks = Image.new("RGBA", (CELL, CELL)); d = ZDraw(marks)
    cx, cy = core
    if state in (1, 2, 3, 4, 5):
        light_point(d, cx, cy, 1 if state == 1 else 2)
        if state >= 2:
            for mx, my in MUZZLES[name][:2]:
                d.point((mx, my), fill=(*WHITE, 255)); d.point((mx + 1, my + 1), fill=(*YELLOW, 220))
    if 2 <= state <= 5:
        for i, (x, y) in enumerate(nozzles):
            length = (12 if state == 2 else 6) + (i + state) % 2
            d.polygon(((x - 3, y - 1), (x + 3, y - 1), (x, min(y + length, 174))), fill=(81, 133, 167, 120))
            d.line(((x, y), (x, min(y + length - 3, 172))), fill=(*ARC, 245), width=2)
            d.point((x, y), fill=(*WHITE, 255))
    if state in (3, 4, 5):
        s = -1 if state == 4 else 1 if state == 5 else 0
        if name == "rod_dancer":
            d.line(((71 + s, 28), (72 + s, 39), (68 + s, 49)), fill=(*ARC, 220)); d.line(((121 + s, 28), (120 + s, 39), (124 + s, 49)), fill=(*WHITE, 220))
        elif name == "static_bastion":
            for x in (51, 70, 122, 141):
                d.point((x + s, 57), fill=(*WHITE, 255)); d.point((x + s, 59), fill=(*ARC, 210))
        elif name == "refinery_tender":
            d.line(((60, 101 + s), (70, 105 + s), (73, 113 + s)), fill=(*ARC, 190)); d.line(((132, 101 - s), (122, 105 - s), (119, 113 - s)), fill=(*ARC, 190))
        elif name == "skyhook_harrier":
            d.line(((96, 29), (96 + s, 40)), fill=(*WHITE, 220))
        else:
            d.arc((82, 69 + s, 110, 92 + s), 195, 342, fill=(*ARC, 220))
    if state == 6:
        jag = [(cx - 13, cy - 10), (cx - 5, cy - 4), (cx - 10, cy + 4), (cx + 2, cy + 8), (cx + 8, cy + 18)]
        d.line(jag, fill=(*INK, 255), width=5); d.line([(x - 2, y - 1) for x, y in jag], fill=(*WHITE, 255))
        for ox, oy in ((-17, -15), (17, 12), (4, 24), (-23, 8)):
            d.point((cx + ox, cy + oy), fill=(*ARC, 255)); d.point((cx + ox + 1, cy + oy - 1), fill=(*YELLOW, 255))
    frame.alpha_composite(marks)
    return frame


def death_frames(name, damaged):
    cx, cy = SHIPS[name][2]
    flash = damaged.copy(); a = np.asarray(flash).copy()
    solid = a[:, :, 3] > 128
    a[solid, :3] = np.clip(a[solid, :3].astype(float) * .52 + np.array(WHITE) * .48, 0, 255).astype(np.uint8)
    flash = Image.fromarray(a, "RGBA"); d = ZDraw(flash)
    d.ellipse((cx - 8, cy - 8, cx + 8, cy + 8), fill=(*WHITE, 245))
    for angle in range(0, 360, 45):
        t = math.radians(angle)
        d.line(((cx + round(9 * math.cos(t)), cy + round(9 * math.sin(t))), (cx + round(27 * math.cos(t)), cy + round(27 * math.sin(t)))), fill=(*ARC, 230), width=2)
    rupture = Image.new("RGBA", (CELL, CELL))
    rupture.alpha_composite(damaged.crop((0, 0, cx * Z, CELL)), (-5 * Z, -2 * Z)); rupture.alpha_composite(damaged.crop((cx * Z, 0, CELL, CELL)), ((cx + 5) * Z, 2 * Z))
    d = ZDraw(rupture)
    d.polygon(((cx, cy - 20), (cx + 13, cy), (cx, cy + 20), (cx - 13, cy)), fill=(*ARC, 220)); d.ellipse((cx - 6, cy - 6, cx + 6, cy + 6), fill=(*WHITE, 255))
    debris = Image.new("RGBA", (CELL, CELL))
    for box, (dx, dy) in [((36, 35, 85, 84), (-7, -9)), ((107, 35, 156, 84), (9, -8)), ((53, 98, 91, 153), (-10, 8)), ((101, 98, 139, 153), (10, 10))]:
        part = damaged.crop(tuple(v * Z for v in box))
        debris.alpha_composite(part, ((box[0] + dx) * Z, (box[1] + dy) * Z))
    d = ZDraw(debris)
    for angle in range(0, 360, 30):
        t = math.radians(angle); x, y = cx + round(25 * math.cos(t)), cy + round(25 * math.sin(t))
        d.point((x, y), fill=(*WHITE, 255)); d.point((x + 1, y + 1), fill=(*ARC, 230))
    return [flash, rupture, debris]


def build(name):
    key = f"storm_elite_{name}"
    base = clean_master(name)
    cells = [flight_frame(base, name, n) for n in range(7)]
    strip = E.paste_strip(cells)
    death = E.paste_strip(death_frames(name, cells[6]))
    E.emit(OUT, key, strip, death, ART / "Resources/Elites/Defs", HERE.parent / f"{key}.png", SHIPS[name][4], lambda a: remap.remap_rgba(a, "storm"))


if __name__ == "__main__":
    OUT.mkdir(exist_ok=True)
    for n in (sys.argv[1:] or list(SHIPS)): build(n)
