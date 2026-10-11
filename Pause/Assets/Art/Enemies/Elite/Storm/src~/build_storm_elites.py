"""Assemble Storm elite sprites from image-generated painted masters.

All resampling and part movement is nearest-neighbour. The painted source
retains its many local material tones; this script only removes prohibited
chromatic pixels and draws small state-specific pixel details.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import colorsys
import math

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
CELL = 192
BG = (11, 11, 26, 255)
WHITE = (242, 246, 255)
YELLOW = (255, 243, 160)
ARC = (154, 201, 229)
INK = (5, 9, 16)


@dataclass(frozen=True)
class Ship:
    name: str
    width: int
    height: int
    core: tuple[int, int]
    nozzles: tuple[tuple[int, int], ...]
    muzzles: tuple[tuple[str, int, int], ...]
    moving: str
    site: str
    attack: str


SHIPS = (
    Ship("skyhook_harrier", 112, 140, (96, 82), ((86, 158), (106, 158)), (("hook lance", 96, 28),), "swept wings flex; winch prong pulses", "skyhook dock", "hooked dash lance"),
    Ship("refinery_tender", 120, 100, (96, 96), ((74, 135), (118, 135)), (("canister drop", 96, 126), ("port rack", 63, 92), ("starboard rack", 129, 92)), "pipe racks and cradles bob", "refinery pad", "sinking canisters"),
    Ship("rod_dancer", 100, 148, (96, 78), ((87, 158), (105, 158)), (("port rod", 71, 24), ("starboard rod", 121, 24), ("arc fan", 96, 76)), "twin rods splay; coils alternate", "balloon hangar", "blink and arc fan"),
    Ship("anvil_cloud", 122, 126, (96, 85), ((82, 150), (110, 150)), (("shell cannon", 96, 37),), "anvil shoulders and recoil collar pulse", "gantry hatch", "lane-telegraphed lightning shell"),
    Ship("static_bastion", 124, 132, (96, 89), ((66, 151), (86, 151), (106, 151), (126, 151)), (("ward core", 96, 90), ("port capacitor", 51, 53), ("starboard capacitor", 141, 53)), "four spires pulse; shield plates breathe", "cloud bay", "curtain of arcs / ward"),
)


def clean_master(ship: Ship) -> Image.Image:
    original = Image.open(SRC / f"{ship.name}_master.png").convert("RGBA")
    box = original.getchannel("A").point(lambda a: 255 if a >= 32 else 0).getbbox()
    assert box
    sprite = original.crop(box).resize((ship.width, ship.height), Image.Resampling.NEAREST)
    a = np.asarray(sprite).copy()
    for y in range(ship.height):
        for x in range(ship.width):
            r, g, b, alpha = map(int, a[y, x])
            if alpha < 32:
                a[y, x] = (0, 0, 0, 0)
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            deg = h * 360
            # The generator paints tiny stray warm and violet glints. Bend
            # only these hue families into local desaturated steel/ice.
            bad = s > .11 and (deg < 52 or deg > 245 or 75 < deg < 175)
            if bad:
                h, s = 215 / 360, min(s, .18)
            elif 175 <= deg <= 215 and s > .25:
                s = .25
            elif s > .30:
                s = .30
            rr, gg, bb = colorsys.hsv_to_rgb(h, s, v)
            a[y, x, :3] = round(rr * 255), round(gg * 255), round(bb * 255)
            if alpha >= 240:
                a[y, x, 3] = 255
    base = Image.new("RGBA", (CELL, CELL))
    base.alpha_composite(Image.fromarray(a, "RGBA"), ((CELL - ship.width) // 2, (CELL - ship.height) // 2))
    pix = np.asarray(base).copy()
    solid = pix[:, :, 3] >= 128
    # A single selective dark edge and two-pixel upper-left cold rim. Their
    # sparse masks preserve the source's thousands of distinct tonal pixels.
    yy, xx = np.indices((CELL, CELL))
    up = np.zeros_like(solid); up[1:] = solid[:-1]
    left = np.zeros_like(solid); left[:, 1:] = solid[:, :-1]
    edge = solid & (~up | ~left)
    rim = solid & ((xx + yy) % 5 != 0) & ((~up) | (~left))
    interior = solid & (np.roll(rim, 1, axis=0) | np.roll(rim, 1, axis=1))
    rgb = pix[:, :, :3].astype(np.float32)
    rgb[edge] = rgb[edge] * .48 + np.array(INK) * .52
    rgb[interior] = rgb[interior] * .60 + np.array((172, 196, 214)) * .40
    rgb[rim] = rgb[rim] * .38 + np.array((196, 215, 229)) * .62
    pix[:, :, :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    return Image.fromarray(pix, "RGBA")


def move_parts(base: Image.Image, ship: Ship, state: int) -> Image.Image:
    if state not in (0, 2, 3, 4, 5):
        return base.copy()
    a = np.asarray(base)
    yy, xx = np.indices((CELL, CELL))
    outer = np.clip((abs(xx - 96) - 12) / 48, 0, 1)
    if ship.name == "rod_dancer":
        region = (yy < 105) & (abs(xx - 96) > 12)
    elif ship.name == "skyhook_harrier":
        region = (yy > 73) & (yy < 144) & (abs(xx - 96) > 17)
    elif ship.name == "refinery_tender":
        region = (yy > 71) & (yy < 138) & (abs(xx - 96) > 20)
    elif ship.name == "anvil_cloud":
        region = (yy > 43) & (yy < 116) & (abs(xx - 96) > 19)
    else:
        region = (yy > 46) & (yy < 135) & (abs(xx - 96) > 22)
    if state == 4:
        dy = np.where(xx < 96, -4.0, 3.0) * outer
        dx = -2.0 * outer
    elif state == 5:
        dy = np.where(xx < 96, 3.0, -4.0) * outer
        dx = 2.0 * outer
    else:
        dy = (1.6 if state in (2, 3) else -1.5) * outer
        dx = np.sign(xx - 96) * (1.5 if state in (2, 3) else -1.0) * outer
    sy = np.clip(np.rint(yy - dy * region).astype(int), 0, CELL - 1)
    sx = np.clip(np.rint(xx - dx * region).astype(int), 0, CELL - 1)
    warped = a[sy, sx].copy()
    if state in (4, 5):
        # Gentle bank lean without shifting the ship's anchor.
        sign = -1 if state == 4 else 1
        shift = np.rint(sign * 1.5 * (96 - yy) / 96).astype(int)
        warped = warped[yy, np.clip(xx - shift, 0, CELL - 1)]
    return Image.fromarray(warped, "RGBA")


def light_point(d: ImageDraw.ImageDraw, x: int, y: int, intensity: int) -> None:
    if intensity <= 0:
        return
    d.ellipse((x - 3, y - 3, x + 3, y + 3), fill=(70, 108, 132, 170))
    d.ellipse((x - 1, y - 1, x + 1, y + 1), fill=(*ARC, 230))
    if intensity >= 2:
        d.point((x, y), fill=(*WHITE, 255))
        d.point((x + 1, y), fill=(*YELLOW, 255))


def flight_frame(base: Image.Image, ship: Ship, state: int) -> Image.Image:
    frame = move_parts(base, ship, state)
    if state in (0, 1, 6):
        pix = np.asarray(frame).copy()
        hsv_dimming = .69 if state == 0 else .85 if state == 1 else .75
        # Only pale power pixels dim; surface detail remains legible.
        cold = (pix[:, :, 2] > pix[:, :, 0] * 1.13) & (pix[:, :, 2] > 115)
        pix[cold, :3] = (pix[cold, :3] * hsv_dimming).astype(np.uint8)
        frame = Image.fromarray(pix, "RGBA")
    marks = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(marks)
    cx, cy = ship.core
    if state in (1, 2, 3, 4, 5):
        light_point(d, cx, cy, 1 if state == 1 else 2)
        if state >= 2:
            # Exterior short arcs and feature tips are restrained, not fields.
            for _, mx, my in ship.muzzles[:2]:
                d.point((mx, my), fill=(*WHITE, 255))
                d.point((mx + 1, my + 1), fill=(*YELLOW, 220))
    if state >= 2 and state <= 5:
        for i, (x, y) in enumerate(ship.nozzles):
            length = (12 if state == 2 else 6) + (i + state) % 2
            d.polygon(((x - 3, y - 1), (x + 3, y - 1), (x, min(y + length, 174))), fill=(81, 133, 167, 120))
            d.line(((x, y), (x, min(y + length - 3, 172))), fill=(*ARC, 245), width=2)
            d.point((x, y), fill=(*WHITE, 255))
    if state in (3, 4, 5):
        s = -1 if state == 4 else 1 if state == 5 else 0
        if ship.name == "rod_dancer":
            d.line(((71 + s, 28), (72 + s, 39), (68 + s, 49)), fill=(*ARC, 220), width=1)
            d.line(((121 + s, 28), (120 + s, 39), (124 + s, 49)), fill=(*WHITE, 220), width=1)
        elif ship.name == "static_bastion":
            for x in (51, 70, 122, 141):
                d.point((x + s, 57), fill=(*WHITE, 255))
                d.point((x + s, 59), fill=(*ARC, 210))
        elif ship.name == "refinery_tender":
            d.line(((60, 101+s), (70, 105+s), (73, 113+s)), fill=(*ARC, 190), width=1)
            d.line(((132, 101-s), (122, 105-s), (119, 113-s)), fill=(*ARC, 190), width=1)
        elif ship.name == "skyhook_harrier":
            d.line(((96, 29), (96+s, 40)), fill=(*WHITE, 220), width=1)
        else:
            d.arc((82, 69+s, 110, 92+s), 195, 342, fill=(*ARC, 220), width=1)
    if state == 6:
        jag = [(cx - 13, cy - 10), (cx - 5, cy - 4), (cx - 10, cy + 4), (cx + 2, cy + 8), (cx + 8, cy + 18)]
        d.line(jag, fill=(*INK, 255), width=5)
        d.line([(x - 2, y - 1) for x, y in jag], fill=(*WHITE, 255), width=1)
        for ox, oy in ((-17, -15), (17, 12), (4, 24), (-23, 8)):
            d.point((cx + ox, cy + oy), fill=(*ARC, 255))
            d.point((cx + ox + 1, cy + oy - 1), fill=(*YELLOW, 255))
    frame.alpha_composite(marks)
    return frame


def death_frames(ship: Ship, damaged: Image.Image) -> list[Image.Image]:
    cx, cy = ship.core
    flash = damaged.copy()
    a = np.asarray(flash).copy()
    solid = a[:, :, 3] > 128
    a[solid, :3] = np.clip(a[solid, :3].astype(float) * .52 + np.array(WHITE) * .48, 0, 255).astype(np.uint8)
    flash = Image.fromarray(a, "RGBA")
    d = ImageDraw.Draw(flash)
    d.ellipse((cx-8, cy-8, cx+8, cy+8), fill=(*WHITE, 245))
    for angle in range(0, 360, 45):
        t = math.radians(angle)
        d.line(((cx+round(9*math.cos(t)), cy+round(9*math.sin(t))), (cx+round(27*math.cos(t)), cy+round(27*math.sin(t)))), fill=(*ARC, 230), width=2)
    rupture = Image.new("RGBA", (CELL, CELL))
    rupture.alpha_composite(damaged.crop((0, 0, cx, CELL)), (-5, -2))
    rupture.alpha_composite(damaged.crop((cx, 0, CELL, CELL)), (cx+5, 2))
    d = ImageDraw.Draw(rupture)
    d.polygon(((cx, cy-20), (cx+13, cy), (cx, cy+20), (cx-13, cy)), fill=(*ARC, 220))
    d.ellipse((cx-6, cy-6, cx+6, cy+6), fill=(*WHITE, 255))
    debris = Image.new("RGBA", (CELL, CELL))
    # Actual master fragments travel away from the painted hull's anchor.
    chunks = [((36, 35, 85, 84), (-7, -9)), ((107, 35, 156, 84), (9, -8)), ((53, 98, 91, 153), (-10, 8)), ((101, 98, 139, 153), (10, 10))]
    for box, (dx, dy) in chunks:
        part = damaged.crop(box)
        debris.alpha_composite(part, (box[0]+dx, box[1]+dy))
    d = ImageDraw.Draw(debris)
    for angle in range(0, 360, 30):
        t = math.radians(angle)
        x, y = cx+round(25*math.cos(t)), cy+round(25*math.sin(t))
        d.point((x, y), fill=(*WHITE, 255))
        d.point((x+1, y+1), fill=(*ARC, 230))
    return [flash, rupture, debris]


def composite_on_bg(im: Image.Image) -> Image.Image:
    bg = Image.new("RGBA", im.size, BG)
    bg.alpha_composite(im)
    return bg.convert("RGB")


def save_concept(ship: Ship) -> None:
    """Retain the hero painting's full detail while removing stray hues."""
    im = Image.open(SRC / f"{ship.name}_master.png").convert("RGBA")
    alpha = im.getchannel("A")
    hsv = np.asarray(im.convert("RGB").convert("HSV")).copy()
    hue = hsv[:, :, 0].astype(float) * 360 / 255
    sat = hsv[:, :, 1].astype(float) / 255
    bad = (sat > .11) & ((hue < 52) | (hue > 245) | ((hue > 75) & (hue < 175)))
    hsv[:, :, 0][bad] = round(215 / 360 * 255)
    hsv[:, :, 1][bad] = np.minimum(hsv[:, :, 1][bad], round(.18 * 255))
    blue = (~bad) & (hue >= 175) & (hue <= 215)
    hsv[:, :, 1][blue] = np.minimum(hsv[:, :, 1][blue], round(.25 * 255))
    rest = (~bad) & (~blue)
    hsv[:, :, 1][rest] = np.minimum(hsv[:, :, 1][rest], round(.30 * 255))
    rgb = Image.fromarray(hsv, "HSV").convert("RGB")
    rgb.putalpha(alpha)
    rgb.save(ROOT / f"storm_elite_{ship.name}_concept.png")


def main() -> None:
    rows = []
    preview_frames = [[] for _ in range(4)]
    for ship in SHIPS:
        save_concept(ship)
        base = clean_master(ship)
        cells = [flight_frame(base, ship, n) for n in range(7)]
        strip = Image.new("RGBA", (CELL*7, CELL))
        for n, cell in enumerate(cells):
            strip.alpha_composite(cell, (n*CELL, 0))
        strip.save(ROOT / f"storm_elite_{ship.name}.png")
        death = Image.new("RGBA", (CELL*3, CELL))
        for n, cell in enumerate(death_frames(ship, cells[6])):
            death.alpha_composite(cell, (n*CELL, 0))
        death.save(ROOT / f"storm_elite_{ship.name}_death.png")
        rows.append(strip)
        for i, cell_no in enumerate((3, 4, 3, 5)):
            preview_frames[i].append(cells[cell_no])
    compare = Image.open("Pause/Assets/Art/Resources/Elites/Space/space_elite_eventide_bastion.png").convert("RGBA")
    rows.append(compare)
    sheet = Image.new("RGBA", (CELL*7*2, CELL*len(rows)*2), BG)
    for i, row in enumerate(rows):
        sheet.alpha_composite(row.resize((row.width*2, row.height*2), Image.Resampling.NEAREST), (0, i*CELL*2))
    composite_on_bg(sheet).save(ROOT / "contact_sheet.png")
    gif = []
    for group in preview_frames:
        canvas = Image.new("RGBA", (CELL*2*5, CELL*2), BG)
        for i, frame in enumerate(group):
            canvas.alpha_composite(frame.resize((CELL*2, CELL*2), Image.Resampling.NEAREST), (i*CELL*2, 0))
        gif.append(composite_on_bg(canvas))
    gif[0].save(ROOT / "preview.gif", save_all=True, append_images=gif[1:], duration=[190, 190, 190, 190], loop=0, disposal=2)


if __name__ == "__main__":
    main()
