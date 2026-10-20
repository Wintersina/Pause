#!/usr/bin/env python3
"""Build Ember launch strips with shaded, site-specific door mechanisms.

Each frame starts from the normalized base, so pixels outside the launch
opening remain identical.  Door plates are drawn in soot-black iron and brass
with seams, rivets, edge heat, and a small warning lamp only in frame 1.
"""
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
SITES = {
    "crucible_silo": {"pad": (.500, .520), "kind": "crucible"},
    "lava_moat_bunker": {"pad": (.500, .500), "kind": "lava"},
    "brass_gantry_cradle": {"pad": (.500, .530), "kind": "gantry"},
    "kiln_mouth": {"pad": (.500, .539), "kind": "kiln"},
}

IRON = (48, 44, 40, 255)
IRON_HI = (112, 91, 67, 255)
IRON_LO = (20, 22, 22, 255)
SEAM = (9, 12, 12, 255)
BRASS = (159, 104, 35, 255)
BRASS_HI = (235, 166, 62, 255)
BRASS_LO = (76, 48, 23, 255)
PIT = (8, 10, 10, 255)
PIT_HI = (48, 26, 17, 255)
MOLTEN = (255, 106, 20, 255)
AMBER = (255, 191, 53, 255)
WARNING = (255, 226, 112, 255)


def box(v):
    return tuple(round(n) for n in v)


def rect(d, b, fill, outline=None, width=1):
    d.rectangle(box(b), fill=fill, outline=outline, width=width)


def rivets(d, pts):
    for x, y in pts:
        d.point((round(x), round(y)), fill=BRASS_HI)
        d.point((round(x + 1), round(y)), fill=BRASS_LO)


def bay(d, b, step=18, circular=False):
    """A shaded depth surface, never a featureless black cutout."""
    x0, y0, x1, y1 = box(b)
    if circular:
        d.ellipse((x0, y0, x1, y1), fill=PIT, outline=SEAM, width=4)
        for x in range(x0 + 12, x1 - 8, step):
            d.line((x, y0 + 10, x, y1 - 10), fill=PIT_HI, width=2)
        d.arc((x0 + 8, y0 + 8, x1 - 8, y1 - 8), 15, 165, fill=(91, 38, 18, 255), width=2)
    else:
        rect(d, b, PIT, SEAM, 4)
        for x in range(x0 + 9, x1 - 7, step):
            d.line((x, y0 + 8, x, y1 - 8), fill=PIT_HI, width=2)
            d.line((x + 3, y0 + 8, x + 3, y1 - 8), fill=(22, 22, 20, 255), width=2)
        d.line((x0 + 9, y1 - 17, x1 - 9, y1 - 17), fill=IRON_HI, width=2)
        d.line((x0 + 11, y1 - 12, x1 - 11, y1 - 12), fill=SEAM, width=2)


def panel(d, b, direction="v", brass=False):
    x0, y0, x1, y1 = box(b)
    if x1 - x0 < 12 or y1 - y0 < 12:
        return
    mid, hi, lo = (BRASS, BRASS_HI, BRASS_LO) if brass else (IRON, IRON_HI, IRON_LO)
    rect(d, (x0 - 2, y0 - 2, x1 + 2, y1 + 2), lo, SEAM, 2)
    rect(d, (x0, y0, x1, y1), mid)
    if direction == "v":
        d.line((x0 + 4, y0 + 5, x1 - 4, y0 + 5), fill=hi, width=2)
        d.line((x0 + 5, y1 - 5, x1 - 5, y1 - 5), fill=lo, width=2)
        for yy in range(y0 + 22, y1 - 8, 24):
            d.line((x0 + 5, yy, x1 - 5, yy), fill=lo, width=2)
            d.line((x0 + 8, yy + 2, x1 - 8, yy + 2), fill=hi, width=1)
    else:
        d.line((x0 + 5, y0 + 4, x0 + 5, y1 - 4), fill=hi, width=2)
        d.line((x1 - 5, y0 + 5, x1 - 5, y1 - 5), fill=lo, width=2)
        for xx in range(x0 + 22, x1 - 8, 24):
            d.line((xx, y0 + 5, xx, y1 - 5), fill=lo, width=2)
            d.line((xx + 2, y0 + 8, xx + 2, y1 - 8), fill=hi, width=1)
    rivets(d, ((x0 + 6, y0 + 6), (x1 - 6, y0 + 6), (x0 + 6, y1 - 6), (x1 - 6, y1 - 6)))


def edge_heat(d, pts):
    d.line(tuple(round(v) for p in pts for v in p), fill=MOLTEN, width=2, joint="curve")
    # A narrow amber inner edge keeps the heat readable without a halo.
    d.line(tuple(round(v) for p in pts for v in p), fill=AMBER, width=1, joint="curve")


def lamp(d, cx, cy, r=3):
    d.ellipse((cx-r, cy-r, cx+r, cy+r), fill=BRASS_LO, outline=SEAM, width=1)
    d.rectangle((cx-1, cy-1, cx+1, cy+1), fill=WARNING)


def crucible(d, cx, cy, frame):
    gap = (0, 16, 44, 78, 116, 38)[frame]
    bay(d, (cx-78, cy-100, cx+78, cy+74), 19)
    if gap < 116:
        panel(d, (cx-132, cy-98, cx-gap, cy+58), "h")
        panel(d, (cx+gap, cy-98, cx+132, cy+58), "h")
        panel(d, (cx-gap, cy-98, cx+gap, cy-gap*.35), "v", brass=True)
    edge_heat(d, ((cx-95, cy-88), (cx-95, cy+39)))
    edge_heat(d, ((cx+95, cy-88), (cx+95, cy+39)))
    if frame == 1:
        lamp(d, cx+116, cy-112)


def lava(d, cx, cy, frame):
    radius = (104, 88, 68, 42, 0, 66)[frame]
    if radius == 0:
        bay(d, (cx-52, cy-52, cx+52, cy+52), 14, circular=True)
    else:
        d.ellipse((cx-55, cy-55, cx+55, cy+55), fill=PIT, outline=SEAM, width=3)
        for i in range(6):
            a0, a1 = i*60+3, i*60+55
            d.pieslice((cx-radius, cy-radius, cx+radius, cy+radius), a0, a1, fill=IRON, outline=SEAM, width=3)
            d.arc((cx-radius+7, cy-radius+7, cx+radius-7, cy+radius-7), a0, a1, fill=IRON_HI, width=2)
        rivets(d, ((cx, cy-radius+12), (cx+radius-12, cy), (cx, cy+radius-12), (cx-radius+12, cy)))
        d.ellipse((cx-radius+4, cy-radius+4, cx+radius-4, cy+radius-4), outline=AMBER, width=1)
    if frame == 1:
        lamp(d, cx+122, cy-92)


def gantry(d, cx, cy, frame):
    gap = (0, 16, 36, 67, 93, 34)[frame]
    bay(d, (cx-48, cy-112, cx+48, cy+112), 16)
    if gap < 93:
        panel(d, (cx-78, cy-105, cx-gap, cy+105), "h", brass=True)
        panel(d, (cx+gap, cy-105, cx+78, cy+105), "h", brass=True)
        d.line((cx-gap, cy-105, cx-gap, cy+105), fill=AMBER, width=1)
        d.line((cx+gap, cy-105, cx+gap, cy+105), fill=AMBER, width=1)
    if frame == 1:
        lamp(d, cx+93, cy-119)


def kiln(d, cx, cy, frame):
    gap = (0, 18, 45, 84, 126, 48)[frame]
    bay(d, (cx-145, cy-51, cx+145, cy+106), 22)
    if gap < 126:
        # Heavy shutters withdraw sideways from the furnace mouth.
        panel(d, (cx-150, cy-52, cx-gap, cy+96), "v", brass=True)
        panel(d, (cx+gap, cy-52, cx+150, cy+96), "v", brass=True)
        d.line((cx-gap, cy-45, cx-gap, cy+88), fill=AMBER, width=2)
        d.line((cx+gap, cy-45, cx+gap, cy+88), fill=AMBER, width=2)
    edge_heat(d, ((cx-151, cy-49), (cx-151, cy+91)))
    edge_heat(d, ((cx+151, cy-49), (cx+151, cy+91)))
    if frame == 1:
        lamp(d, cx+167, cy-65)


def render_frame(base, cfg, index):
    out = base.copy()
    layer = Image.new("RGBA", base.size)
    draw = ImageDraw.Draw(layer)
    cx = round(cfg["pad"][0] * base.width)
    cy = round(cfg["pad"][1] * base.height)
    {"crucible": crucible, "lava": lava, "gantry": gantry, "kiln": kiln}[cfg["kind"]](draw, cx, cy, index)
    return Image.alpha_composite(out, layer)


def main():
    sheet = Image.new("RGBA", (960, 4 * 230), (10, 13, 13, 255))
    sd = ImageDraw.Draw(sheet)
    for row, (name, cfg) in enumerate(SITES.items()):
        base = Image.open(HERE / f"{name}.png").convert("RGBA")
        frames = [render_frame(base, cfg, i) for i in range(6)]
        strip = Image.new("RGBA", (base.width * 6, base.height))
        for i, image in enumerate(frames):
            strip.alpha_composite(image, (i * base.width, 0))
        strip.save(HERE / f"{name}_launch_v2.png")
        # Six-frame row, making the contact sheet useful for reviewing order.
        thumb_w, thumb_h = 150, 180
        y = row * 230 + 34
        sd.text((8, row * 230 + 8), name.replace("_", " "), fill=WARNING)
        for i, image in enumerate(frames):
            image.thumbnail((thumb_w, thumb_h), Image.Resampling.NEAREST)
            x = i * 160 + (thumb_w - image.width) // 2
            sheet.alpha_composite(image, (x, y))
            sd.text((i * 160 + 4, row * 230 + 214), str(i), fill=IRON_HI)
    sheet.save(HERE / "launch_v2_contact_sheet.png")


if __name__ == "__main__":
    main()
