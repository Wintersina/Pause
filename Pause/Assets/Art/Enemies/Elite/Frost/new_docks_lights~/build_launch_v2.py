#!/usr/bin/env python3
"""Build the Frost launch-site strips with drawn, segmented doors.

The base is copied for every frame and only the launch-door footprint is
redrawn.  This keeps the surrounding ice, rigging, and deck pixels stable.
"""
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
SITES = {
    "ice_locked_offshore_rig": {"pad": (.500, .500), "kind": "rig"},
    "cryo_silo": {"pad": (.500, .520), "kind": "silo"},
    "glacier_cave_hangar": {"pad": (.500, .437), "kind": "cave"},
    "icebreaker_deck": {"pad": (.500, .515), "kind": "ship"},
}
STEEL = (83, 105, 126, 255)
STEEL_HI = (160, 190, 210, 255)
STEEL_LO = (36, 52, 70, 255)
INK = (10, 22, 36, 255)
INTERIOR = (8, 18, 30, 255)
INTERIOR_HI = (25, 47, 66, 255)
FROST = (177, 235, 255, 255)
AMBER = (255, 190, 70, 255)


def rect(d, box, fill, outline=None, width=1):
    d.rectangle(tuple(round(v) for v in box), fill=fill, outline=outline, width=width)


def rivets(d, points, fill=(205, 232, 240, 255)):
    for x, y in points:
        d.point((x, y), fill=fill)
        d.point((x + 1, y), fill=STEEL_LO)


def shaded_bay(d, box, step=18):
    x0, y0, x1, y1 = box
    rect(d, box, INTERIOR, INK, 4)
    for x in range(x0 + 8, x1 - 5, step):
        d.line((x, y0 + 8, x, y1 - 8), fill=INTERIOR_HI, width=3)
        d.line((x + 4, y0 + 8, x + 4, y1 - 8), fill=(13, 28, 44, 255), width=2)
    d.line((x0 + 8, y1 - 17, x1 - 8, y1 - 17), fill=STEEL_LO, width=4)
    d.line((x0 + 12, y1 - 22, x1 - 12, y1 - 22), fill=STEEL_HI, width=1)


def panel(d, box, direction="v"):
    x0, y0, x1, y1 = (round(v) for v in box)
    if x1 - x0 < 12 or y1 - y0 < 12:
        return
    rect(d, box, STEEL_LO, INK, 3)
    rect(d, (x0 + 5, y0 + 5, x1 - 5, y1 - 5), STEEL, None)
    if direction == "v":
        d.line((x0 + 8, y0 + 7, x1 - 8, y0 + 7), fill=STEEL_HI, width=2)
        d.line((x0 + 8, y1 - 8, x1 - 8, y1 - 8), fill=STEEL_LO, width=2)
        for yy in range(y0 + 28, y1 - 10, 28):
            d.line((x0 + 8, yy, x1 - 8, yy), fill=STEEL_LO, width=2)
            d.line((x0 + 10, yy + 3, x1 - 10, yy + 3), fill=(121, 151, 170, 255), width=1)
    else:
        d.line((x0 + 7, y0 + 8, x0 + 7, y1 - 8), fill=STEEL_HI, width=2)
        d.line((x1 - 8, y0 + 8, x1 - 8, y1 - 8), fill=STEEL_LO, width=2)
        for xx in range(x0 + 28, x1 - 10, 28):
            d.line((xx, y0 + 8, xx, y1 - 8), fill=STEEL_LO, width=2)
            d.line((xx + 3, y0 + 10, xx + 3, y1 - 10), fill=(121, 151, 170, 255), width=1)
    rivets(d, [(x0 + 8, y0 + 8), (x1 - 8, y0 + 8), (x0 + 8, y1 - 8), (x1 - 8, y1 - 8)])


def rig(d, cx, cy, frame):
    # Square elevator hatch with four sliding armored leaves.
    open_amount = (0, 18, 45, 76, 112, 34)[frame]
    size = 190
    if frame == 4:
        shaded_bay(d, (cx - 96, cy - 112, cx + 96, cy + 112), 22)
    else:
        shaded_bay(d, (cx - 96 + open_amount, cy - 112 + open_amount, cx + 96 - open_amount, cy + 112 - open_amount), 22)
        p = max(42, 96 - open_amount)
        panel(d, (cx - 96, cy - 112, cx - p, cy + 112), "h")
        panel(d, (cx + p, cy - 112, cx + 96, cy + 112), "h")
        panel(d, (cx - p, cy - 112, cx + p, cy - p), "v")
        panel(d, (cx - p, cy + p, cx + p, cy + 112), "v")
    if frame == 1:
        d.rectangle((cx + 115, cy - 128, cx + 120, cy - 123), fill=AMBER)


def silo(d, cx, cy, frame):
    # Circular iris: six overlapping wedge-like plates with visible seams.
    r = (112, 92, 63, 31, 0, 70)[frame]
    if r == 0:
        shaded_bay(d, (cx - 28, cy - 28, cx + 28, cy + 28), 12)
    else:
        d.ellipse((cx - 35, cy - 35, cx + 35, cy + 35), fill=INTERIOR, outline=INK, width=3)
        for i in range(6):
            a0, a1 = i * 60 + 4, i * 60 + 54
            d.pieslice((cx - r, cy - r, cx + r, cy + r), a0, a1, fill=STEEL, outline=INK, width=3)
            d.arc((cx - r + 6, cy - r + 6, cx + r - 6, cy + r - 6), a0, a1, fill=STEEL_HI, width=2)
        d.ellipse((cx - 35, cy - 35, cx + 35, cy + 35), outline=STEEL_HI, width=2)
        rivets(d, [(cx, cy - r + 12), (cx + r - 12, cy), (cx, cy + r - 12), (cx - r + 12, cy)])
    if frame == 1:
        d.rectangle((cx + 116, cy - 5, cx + 121, cy), fill=AMBER)


def cave(d, cx, cy, frame):
    # Roof slabs slide into the glacier face from the sides and top.
    gap = (0, 20, 52, 88, 120, 43)[frame]
    x0, x1, y0, y1 = cx - 152, cx + 152, cy - 112, cy + 112
    if gap >= 120:
        shaded_bay(d, (cx - 116, cy - 92, cx + 116, cy + 108), 24)
    else:
        shaded_bay(d, (cx - 116, cy - 92, cx + 116, cy + 108), 24)
        panel(d, (x0, y0, cx - gap, y1), "h")
        panel(d, (cx + gap, y0, x1, y1), "h")
        panel(d, (cx - gap, y0, cx + gap, cy - gap * .35), "v")
    if frame == 1:
        d.rectangle((cx + 137, cy - 98, cx + 142, cy - 93), fill=AMBER)


def ship(d, cx, cy, frame):
    # Deck hatch made from longitudinal cold-steel roof segments.
    gap = (0, 16, 38, 70, 102, 32)[frame]
    x0, x1, y0, y1 = cx - 86, cx + 86, cy - 104, cy + 106
    if gap >= 102:
        shaded_bay(d, (cx - 42, cy - 92, cx + 42, cy + 96), 17)
    else:
        shaded_bay(d, (cx - 42, cy - 92, cx + 42, cy + 96), 17)
        panel(d, (x0, y0, cx - gap, y1), "h")
        panel(d, (cx + gap, y0, x1, y1), "h")
        panel(d, (cx - gap, y0, cx + gap, cy - gap), "v")
        panel(d, (cx - gap, cy + gap, cx + gap, y1), "v")
    if frame == 1:
        d.rectangle((cx + 93, cy - 100, cx + 98, cy - 95), fill=AMBER)


def frame(base, pad, kind, index):
    out = base.copy()
    layer = Image.new("RGBA", base.size)
    d = ImageDraw.Draw(layer)
    cx, cy = round(pad[0] * base.width), round(pad[1] * base.width)
    {"rig": rig, "silo": silo, "cave": cave, "ship": ship}[kind](d, cx, cy, index)
    return Image.alpha_composite(out, layer)


def main():
    contact = Image.new("RGBA", (720, 2 * 270), (5, 14, 27, 255))
    for i, (name, cfg) in enumerate(SITES.items()):
        path = HERE / f"{name}.png"
        base = Image.open(path).convert("RGBA")
        strips = []
        for i_frame in range(6):
            strips.append(frame(base, cfg["pad"], cfg["kind"], i_frame))
        strip = Image.new("RGBA", (base.width * 6, base.height))
        for i_frame, im in enumerate(strips):
            strip.alpha_composite(im, (i_frame * base.width, 0))
        strip.save(HERE / f"{name}_launch_v2.png")
        thumb = strips[0].copy(); thumb.thumbnail((330, 240), Image.Resampling.LANCZOS)
        x = (i % 2) * 360 + (360 - thumb.width) // 2
        y = (i // 2) * 270 + (240 - thumb.height) // 2 + 22
        contact.alpha_composite(thumb, (x, y))
        ImageDraw.Draw(contact).text(((i % 2) * 360 + 8, (i // 2) * 270 + 7), name.replace("_", " "), fill=(*FROST[:3], 255))
    contact.save(HERE / "launch_v2_contact_sheet.png")


if __name__ == "__main__":
    main()
