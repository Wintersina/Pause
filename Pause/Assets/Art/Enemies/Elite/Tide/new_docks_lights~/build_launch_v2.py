#!/usr/bin/env python3
"""Build deterministic six-frame Tide launch-door strips."""
from pathlib import Path
import math
from PIL import Image, ImageChops, ImageDraw

HERE = Path(__file__).resolve().parent
W = H = 768
SITES = {
    "floating_carrier_pad": {"center": (384, 376), "kind": "slide", "lamp": (526, 256)},
    "lighthouse_silo": {"center": (384, 369), "kind": "iris", "lamp": (502, 257)},
    "submarine_pen": {"center": (384, 430), "kind": "slide", "lamp": (534, 336)},
    "wreck_hull_bay": {"center": (384, 430), "kind": "slide", "lamp": (514, 309)},
}
INK = (8, 24, 29, 255); PIT = (5, 20, 25, 255); PIT_HI = (18, 62, 67, 255)
PIT_LO = (3, 12, 17, 255); TEAL_HI = (94, 177, 168, 255); RUST = (116, 60, 38, 255)
BRASS = (169, 111, 46, 255); BRASS_HI = (237, 179, 77, 255); BRASS_LO = (67, 42, 29, 255)
WARNING = (255, 232, 142, 255)
MIST_FOAM = (232, 246, 244)
MIST_TEAL = (143, 179, 176)
MIST_SHADOW = (78, 110, 112)


def transparent():
    return Image.new("RGBA", (W, H), (0, 0, 0, 0))


def lamp(layer, x, y):
    d = ImageDraw.Draw(layer)
    d.ellipse((x - 5, y - 5, x + 5, y + 5), fill=BRASS_LO, outline=INK, width=2)
    d.rectangle((x - 2, y - 2, x + 2, y + 2), fill=WARNING)


def shaded_bay(layer, rect, circular=False):
    d = ImageDraw.Draw(layer); x0, y0, x1, y1 = rect
    for yy in range(y0, y1 + 1):
        depth = (yy - y0) / max(1, y1 - y0)
        col = tuple(round(PIT_LO[i] * (1 - depth) + PIT_HI[i] * depth) for i in range(3)) + (255,)
        d.line((x0, yy, x1, yy), fill=col, width=1)
    if circular:
        cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
        d.ellipse(rect, outline=INK, width=5)
        for dx in range(-76, 77, 22):
            d.line((cx + dx, y0 + 16, cx + dx, y1 - 16), fill=PIT_HI, width=2)
        d.arc((x0 + 12, y0 + 12, x1 - 12, y1 - 12), 18, 162, fill=TEAL_HI, width=2)
        d.arc((x0 + 20, y0 + 20, x1 - 20, y1 - 20), 198, 342, fill=BRASS, width=2)
        d.line((x0 + 22, y1 - 22, x1 - 22, y1 - 22), fill=BRASS, width=3)
        d.line((x0 + 28, y1 - 16, x1 - 28, y1 - 16), fill=TEAL_HI, width=2)
    else:
        d.rectangle(rect, outline=INK, width=5)
        for xx in range(x0 + 18, x1 - 8, 26):
            d.line((xx, y0 + 12, xx, y1 - 28), fill=PIT_HI, width=3)
            d.line((xx + 5, y0 + 12, xx + 5, y1 - 28), fill=PIT_LO, width=2)
        d.line((x0 + 14, y1 - 22, x1 - 14, y1 - 22), fill=BRASS, width=3)
        d.line((x0 + 18, y1 - 15, x1 - 18, y1 - 15), fill=TEAL_HI, width=2)
        if y1 - y0 > 110:
            rx = x1 - 24
            d.line((rx, y0 + 20, rx, y1 - 32), fill=BRASS_HI, width=2)
            d.line((rx - 12, y0 + 20, rx - 12, y1 - 32), fill=BRASS, width=2)
            for yy in range(y0 + 30, y1 - 32, 22): d.line((rx - 12, yy, rx, yy), fill=BRASS_HI, width=2)


def paste_source(dst, base, rect, dx, dy, clip):
    x0, y0, x1, y1 = rect
    crop = base.crop((x0, y0, x1 + 1, y1 + 1))
    visible = Image.new("L", crop.size, 0)
    ImageDraw.Draw(visible).rectangle((clip[0] - x0 - dx, clip[1] - y0 - dy,
                                       clip[2] - x0 - dx, clip[3] - y0 - dy), fill=255)
    dst.paste(crop, (x0 + dx, y0 + dy), visible)


def draw_mist_clump(layer, cx, cy, width, height, phase, alpha):
    """Draw a rounded, stepped cloud with three readable pixel-art tones."""
    d = ImageDraw.Draw(layer)
    shadow = (*MIST_SHADOW, round(alpha * 0.72))
    mid = (*MIST_TEAL, round(alpha * 0.82))
    foam = (*MIST_FOAM, alpha)
    # Uneven lobes make each cloud read as a billow rather than a flat haze.
    lobes = [
        (-0.38, 0.08, 0.44, 0.58),
        (-0.10, -0.18, 0.52, 0.72),
        (0.20, -0.02, 0.50, 0.64),
        (0.43, 0.16, 0.34, 0.48),
    ]
    for index, (ox, oy, sx, sy) in enumerate(lobes):
        wobble = math.sin(phase * 1.7 + index * 2.2) * width * 0.035
        box = (cx + (ox - sx / 2) * width + wobble,
               cy + (oy - sy / 2) * height,
               cx + (ox + sx / 2) * width + wobble,
               cy + (oy + sy / 2) * height)
        d.ellipse(tuple(round(v) for v in box), fill=shadow if index in (0, 3) else mid)
    # Lower body, central body, and pale upper caps are separate tones.
    d.ellipse((round(cx - width * 0.48), round(cy - height * 0.06),
               round(cx + width * 0.48), round(cy + height * 0.47)), fill=mid)
    d.ellipse((round(cx - width * 0.29), round(cy - height * 0.42),
               round(cx + width * 0.29), round(cy + height * 0.12)), fill=foam)
    d.rectangle((round(cx - width * 0.32), round(cy + height * 0.25),
                 round(cx + width * 0.32), round(cy + height * 0.42)), fill=shadow)
    # Broken 2–4 px perimeter pixels provide a soft dither without a glow.
    for index in range(10):
        side = -1 if index % 2 == 0 else 1
        px = round(cx + side * (width * (0.47 + (index % 3) * 0.035)))
        py = round(cy + (index - 5) * height * 0.07)
        edge_alpha = max(18, round(alpha * (0.18 + (index % 3) * 0.07)))
        d.rectangle((min(px, px + side * 3), py, max(px, px + side * 3), py + 2),
                    fill=(*MIST_TEAL, edge_alpha))
    for index in range(6):
        px = round(cx + (index - 3) * width * 0.13)
        py = round(cy - height * (0.47 + (index % 2) * 0.035))
        d.rectangle((px, py, px + 2, py + 2), fill=(*MIST_FOAM, max(16, alpha // 3)))


def mist_overlay(name, frame, opening, circular=False):
    """Return readable billowing bay mist and its changed-pixel mask."""
    layer = transparent()
    if frame < 2 or frame > 5:
        return layer, layer.getchannel("A")
    x0, y0, x1, y1 = opening
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    amount = {2: 0.55, 3: 0.82, 4: 0.92, 5: 0.62}[frame]
    if circular:
        layouts = {
            2: [(-0.24, 0.73, 0.42, 0.30)],
            3: [(-0.30, 0.70, 0.46, 0.34), (0.18, 0.57, 0.40, 0.32)],
            4: [(-0.30, 0.72, 0.52, 0.36), (0.20, 0.60, 0.46, 0.38), (0.02, 0.38, 0.34, 0.28)],
            5: [(-0.12, 0.71, 0.44, 0.31), (0.35, 0.52, 0.32, 0.25)],
        }
    else:
        layouts = {
            2: [(-0.18, 0.78, 0.42, 0.30)],
            3: [(-0.36, 0.78, 0.46, 0.33), (0.12, 0.63, 0.48, 0.38), (0.40, 0.77, 0.34, 0.29)],
            4: [(-0.38, 0.78, 0.52, 0.37), (-0.02, 0.62, 0.55, 0.42), (0.38, 0.74, 0.48, 0.34), (0.04, 0.37, 0.30, 0.27)],
            5: [(-0.24, 0.77, 0.44, 0.30), (0.27, 0.65, 0.38, 0.29)],
        }
    for index, (ox, oy, ow, oh) in enumerate(layouts[frame]):
        px = round(x0 + (x1 - x0) * (0.5 + ox))
        py = round(y0 + (y1 - y0) * oy)
        w = round((x1 - x0) * ow)
        h = round((y1 - y0) * oh)
        draw_mist_clump(layer, px, py, w, h, frame * 3 + index, round(150 * amount))
    # Limit the cloud body to the bay while allowing a deliberate 10–30 px
    # venting spill above the rim and just inside the door edges.
    clip = Image.new("L", (W, H), 0)
    cd = ImageDraw.Draw(clip)
    if circular:
        cd.ellipse((x0 - 18, y0 - 28, x1 + 18, y1 + 4), fill=255)
    else:
        cd.rectangle((x0 - 18, y0 - 28, x1 + 18, y1 + 4), fill=255)
    layer.putalpha(ImageChops.multiply(layer.getchannel("A"), clip))
    return layer, layer.getchannel("A")


def make_closed_submarine(base):
    """Make a closed gate base; the original submarine_pen.png remains untouched."""
    out = base.copy(); d = ImageDraw.Draw(out)
    x0, y0, x1, y1 = 244, 224, 524, 378
    d.rectangle((x0, y0, x1, y1), fill=(38, 47, 47, 255), outline=INK, width=7)
    d.line((384, y0 + 4, 384, y1 - 4), fill=BRASS_HI, width=4)
    for xa, xb in ((x0 + 5, 381), (387, x1 - 5)):
        d.rectangle((xa, y0 + 5, xb, y1 - 5), fill=(48, 68, 68, 255), outline=(18, 29, 31, 255), width=3)
        d.line((xa + 8, y0 + 10, xb - 8, y0 + 10), fill=(111, 123, 111, 255), width=2)
        d.line((xa + 8, y1 - 12, xb - 8, y1 - 12), fill=RUST, width=3)
        for xx in range(xa + 15, xb - 4, 30): d.line((xx, y0 + 16, xx, y1 - 18), fill=(28, 43, 44, 255), width=2)
        for yy in range(y0 + 22, y1 - 8, 34): d.line((xa + 8, yy, xb - 8, yy), fill=(25, 39, 40, 255), width=2)
        for xx, yy in ((xa + 10, y0 + 12), (xb - 10, y0 + 12), (xa + 10, y1 - 12), (xb - 10, y1 - 12)):
            d.ellipse((xx - 3, yy - 3, xx + 3, yy + 3), fill=BRASS_HI, outline=BRASS_LO)
        for xx in range(xa + 8, xb - 5, 16): d.line((xx, y0 + 2, xx + 8, y0 + 10), fill=WARNING, width=3)
    return out


def slide_spec(name):
    if name == "floating_carrier_pad":
        # Travel fractions for cells 0..5: 0, 0, ~.45, ~.8, 1.0, ~.5.
        return (278, 284, 382, 492), (386, 284, 490, 492), (272, 278, 496, 498), (0, 0, 54, 100, 120, 60)
    if name == "wreck_hull_bay":
        return (278, 286, 382, 488), (386, 286, 490, 488), (272, 280, 496, 494), (0, 0, 52, 93, 116, 58)
    return (250, 230, 382, 374), (386, 230, 518, 374), (244, 224, 524, 380), (0, 0, 68, 120, 150, 75)


def render_slide(base, name, frame):
    left, right, opening, shifts = slide_spec(name)
    if frame == 0: return base.copy()
    if frame == 1:
        out = base.copy(); layer = transparent(); lamp(layer, *SITES[name]["lamp"]); return Image.alpha_composite(out, layer)
    layer = transparent(); shaded_bay(layer, opening)
    out = Image.alpha_composite(base, layer)
    shift = shifts[frame]
    # A soft recessed edge shadow remains beneath each moved panel.
    md = ImageDraw.Draw(out)
    md.line((opening[0] + 4, opening[1] + 4, opening[0] + 4, opening[3] - 4), fill=(2, 10, 13, 210), width=4)
    md.line((opening[2] - 4, opening[1] + 4, opening[2] - 4, opening[3] - 4), fill=(2, 10, 13, 210), width=4)
    paste_source(out, base, left, -shift, 0, opening); paste_source(out, base, right, shift, 0, opening)
    mist, _ = mist_overlay(name, frame, opening)
    out = Image.alpha_composite(out, mist)
    return out


def iris_masks(center):
    cx, cy = center; masks = []; claimed = Image.new("L", (W, H), 0)
    for start in (0, 90, 180, 270):
        mask = Image.new("L", (W, H), 0); pts = [(cx, cy)]
        # Leave a one-degree seam between wedges so no source pixel belongs to
        # two moving panels at the cardinal joins.
        for angle in range(start + 1, start + 90, 3):
            a = math.radians(angle); pts.append((round(cx + 94 * math.cos(a)), round(cy + 94 * math.sin(a))))
        ImageDraw.Draw(mask).polygon(pts, fill=255)
        mask = ImageChops.subtract(mask, claimed)
        claimed = ImageChops.lighter(claimed, mask); masks.append(mask)
    return masks


def paste_masked(dst, base, mask, dx, dy, clip):
    moved = Image.new("L", (W, H), 0); moved.paste(mask, (dx, dy))
    visible = Image.new("L", (W, H), 0); ImageDraw.Draw(visible).rectangle(clip, fill=255)
    moved = ImageChops.multiply(moved, visible)
    shifted = Image.new("RGBA", (W, H), (0, 0, 0, 0)); shifted.paste(base, (dx, dy))
    dst.paste(shifted, (0, 0), moved)


def render_silo(base, frame):
    if frame == 0: return base.copy()
    if frame == 1:
        out = base.copy(); layer = transparent(); lamp(layer, *SITES["lighthouse_silo"]["lamp"]); return Image.alpha_composite(out, layer)
    cx, cy = SITES["lighthouse_silo"]["center"]; opening = (290, 275, 478, 463)
    layer = transparent(); shaded_bay(layer, opening, circular=True); out = Image.alpha_composite(base, layer)
    shift = {2: 18, 3: 38, 4: 0, 5: 24}[frame]
    if shift:
        for mask, (dx, dy) in zip(iris_masks((cx, cy)), ((shift, 0), (0, shift), (-shift, 0), (0, -shift))): paste_masked(out, base, mask, dx, dy, opening)
    mist, _ = mist_overlay("lighthouse_silo", frame, opening, circular=True)
    out = Image.alpha_composite(out, mist)
    return out


def assert_no_pink(image, label):
    for r, g, b, a in image.get_flattened_data():
        if a > 32 and r > 180 and b > 150 and g < 100 and g * 2 < min(r, b): raise AssertionError(f"pink/magenta pixel in {label}")


def assert_shifted(base, frame, rect, dx, dy, clip, mist_mask=None):
    x0, y0, x1, y1 = rect
    for sy in range(y0, y1 + 1):
        for sx in range(x0, x1 + 1):
            tx, ty = sx + dx, sy + dy
            if base.getpixel((sx, sy))[3] and clip[0] <= tx <= clip[2] and clip[1] <= ty <= clip[3]:
                if mist_mask is not None and mist_mask.getpixel((tx, ty)):
                    continue
                assert frame.getpixel((tx, ty)) == base.getpixel((sx, sy)), (sx, sy, tx, ty)


def pixel_difference(base, frame):
    return sum(a != b for a, b in zip(base.get_flattened_data(), frame.get_flattened_data()))


def validate(name, base, frames):
    assert base.size == (W, H) and len(frames) == 6
    if name == "lighthouse_silo":
        shifts = {2: 18, 3: 38, 5: 24}
        for index, shift in shifts.items():
            mist_mask = mist_overlay(name, index, (290, 275, 478, 463), circular=True)[1]
            for mask, (dx, dy) in zip(iris_masks(SITES[name]["center"]), ((shift, 0), (0, shift), (-shift, 0), (0, -shift))):
                for y in range(H):
                    for x in range(W):
                        if mask.getpixel((x, y)) and base.getpixel((x, y))[3] and 290 <= x + dx <= 478 and 275 <= y + dy <= 463:
                            if mist_mask.getpixel((x + dx, y + dy)):
                                continue
                            assert frames[index].getpixel((x + dx, y + dy)) == base.getpixel((x, y))
    else:
        left, right, opening, shifts = slide_spec(name)
        for index in (2, 3, 5):
            mist_mask = mist_overlay(name, index, opening)[1]
            assert_shifted(base, frames[index], left, -shifts[index], 0, opening, mist_mask); assert_shifted(base, frames[index], right, shifts[index], 0, opening, mist_mask)
    differences = [pixel_difference(frames[0], frame) for frame in frames]
    assert differences[2] <= differences[3] <= differences[4], (name, differences)
    assert differences[4] == max(differences), (name, differences)
    for frame in frames: assert frame.size == (W, H); assert_no_pink(frame, name)


def main():
    closed_sub = make_closed_submarine(Image.open(HERE / "submarine_pen.png").convert("RGBA")); closed_sub.save(HERE / "submarine_pen_closed.png")
    rows = []
    for name, info in SITES.items():
        base = closed_sub if name == "submarine_pen" else Image.open(HERE / f"{name}.png").convert("RGBA")
        frames = [render_silo(base, f) if info["kind"] == "iris" else render_slide(base, name, f) for f in range(6)]
        validate(name, base, frames)
        strip = Image.new("RGBA", (W * 6, H))
        for i, frame in enumerate(frames): strip.paste(frame, (i * W, 0))
        assert strip.size == (4608, 768) and strip.width // W == 6 and strip.height == H
        strip.save(HERE / f"{name}_launch_v2.png"); rows.append((name, frames))
    sheet = Image.new("RGBA", (960, 920), (7, 24, 29, 255)); sd = ImageDraw.Draw(sheet)
    for row, (name, frames) in enumerate(rows):
        y = row * 230 + 34; sd.text((8, row * 230 + 8), name.replace("_", " "), fill=WARNING)
        for i, frame in enumerate(frames):
            thumb = frame.copy(); thumb.thumbnail((150, 180), Image.Resampling.NEAREST); x = i * 160 + (150 - thumb.width) // 2
            sheet.alpha_composite(thumb, (x, y)); sd.text((i * 160 + 4, row * 230 + 214), str(i), fill=BRASS_HI)
    assert sheet.size == (960, 920)
    sheet.convert("RGB").save(HERE / "launch_v2_contact_sheet.png"); assert_no_pink(sheet.convert("RGBA"), "launch_v2_contact_sheet.png")
    # Native-resolution inspection sheet: only the bay crops from cells 2–5.
    zoom = Image.new("RGBA", (4 * 280, 4 * 290), (7, 24, 29, 255)); zd = ImageDraw.Draw(zoom)
    for row, (name, frames) in enumerate(rows):
        if name == "lighthouse_silo":
            opening = (290, 275, 478, 463)
            center = ((opening[0] + opening[2]) // 2, (opening[1] + opening[3]) // 2)
        else:
            _, _, opening, _ = slide_spec(name)
            center = ((opening[0] + opening[2]) // 2, (opening[1] + opening[3]) // 2)
        zd.text((8, row * 290 + 6), name.replace("_", " "), fill=WARNING)
        for col, frame in enumerate(frames[2:6]):
            crop = frame.crop((center[0] - 128, center[1] - 128, center[0] + 128, center[1] + 128))
            assert crop.size == (256, 256)
            zoom.alpha_composite(crop, (col * 280 + 12, row * 290 + 26))
            zd.text((col * 280 + 16, row * 290 + 266), str(col + 2), fill=BRASS_HI)
    assert zoom.size == (1120, 1160)
    zoom.save(HERE / "launch_v2_mist_zoom.png"); assert_no_pink(zoom, "launch_v2_mist_zoom.png")


if __name__ == "__main__": main()
