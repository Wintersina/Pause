"""Rebuild four Frost elite review strips from the generated painted masters.

All resampling is nearest-neighbour; translucent pixels are stepped glow only.
The generated masters in this directory are the authored hulls, not placeholders.
"""

from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
SRC = Path(__file__).resolve().parent
CELL = 192

INK = "#05060c"
METAL = ["#0c1020", "#1e2638", "#33405c", "#4f6188", "#7d93bd"]
ICE = ["#0a1734", "#1c3c68", "#2a62ae", "#4a96e6", "#9fd2f2", "#e2fbff"]
NEON = ["#1569c8", "#1898f7", "#46ddfd", "#aaf2fb", "#f8fefc"]
BRASS = ["#29251e", "#594732", "#987451", "#c4a16e", "#e2c59a"]

SHIPS = {
    "frost_elite_floe_harrower": dict(master="floe_harrower_master.png", size=(118, 106), engines=(83, 109), nozzle_y=143),
    "frost_elite_cryo_siren": dict(master="cryo_siren_master.png", size=(62, 146), engines=(90, 102), nozzle_y=155),
    "frost_elite_glacier_tender": dict(master="glacier_tender_alt.png", size=(98, 140), engines=(77, 115), nozzle_y=158),
    "frost_elite_whiteout_sentinel": dict(master="whiteout_sentinel_alt.png", size=(120, 126), engines=(84, 108), nozzle_y=151),
}


def rgb(h):
    return tuple(bytes.fromhex(h[1:]))


def choose_colors(raw):
    """Map painted pixels into short material-specific Frost ramps."""
    v = raw.astype(np.int16)
    r, g, b = v[:, :, 0], v[:, :, 1], v[:, :, 2]
    # Source image colors are classified by material, then quantized within a ramp.
    neon = (g > r * 1.38) & (b > r * 1.34) & (g > 65)
    brass = (r > g * 1.13) & (g > b * 1.12) & (r > 50)
    light_ice = ((r + g + b) > 365) & (b >= r * 1.03) & ~neon
    palette = np.asarray([rgb(x) for x in ([INK] + METAL + ICE + NEON + BRASS)], np.int16)
    answer = np.zeros_like(v, np.uint8)
    bands = [
        (neon, np.arange(12, 17)),
        (brass & ~neon, np.arange(17, 22)),
        (light_ice & ~brass, np.arange(6, 12)),
        (~neon & ~brass & ~light_ice, np.arange(0, 12)),
    ]
    for mask, choices in bands:
        if not mask.any():
            continue
        samples = v[mask]
        delta = samples[:, None, :] - palette[choices][None, :, :]
        # Green/blue discrimination keeps steel distinct from cyan coolant.
        score = (delta.astype(np.int32) ** 2 * np.array([2, 3, 3])).sum(axis=2)
        answer[mask] = palette[choices[np.argmin(score, axis=1)]].astype(np.uint8)
    return answer, neon


def painted_hull(spec):
    src = Image.open(SRC / spec["master"]).convert("RGBA")
    alpha = src.getchannel("A")
    crop = alpha.point(lambda a: 255 if a >= 128 else 0).getbbox()
    src = src.crop(crop).resize(spec["size"], Image.Resampling.NEAREST)
    raw = np.asarray(src)
    colors, neon = choose_colors(raw[:, :, :3])
    solid = raw[:, :, 3] >= 128
    out = np.concatenate([colors, (solid * 255)[:, :, None].astype(np.uint8)], axis=2)
    hull = Image.fromarray(out, "RGBA")
    # One pixel exterior ink and a restrained cyan lower-right edge.
    ext = Image.new("RGBA", spec["size"])
    a = hull.getchannel("A")
    rim = a.filter(ImageFilter.MaxFilter(3))
    d = ImageDraw.Draw(ext)
    edge = np.asarray(rim) > np.asarray(a)
    ea = np.asarray(ext).copy()
    ea[edge] = (*rgb(INK), 255)
    ext = Image.fromarray(ea, "RGBA")
    ext.alpha_composite(hull)
    x = (CELL - spec["size"][0]) // 2
    y = (CELL - spec["size"][1]) // 2
    result = Image.new("RGBA", (CELL, CELL))
    result.alpha_composite(ext, (x, y))
    mask = Image.new("L", (CELL, CELL))
    mask.paste(Image.fromarray((neon & solid).astype(np.uint8) * 255, "L"), (x, y))
    return result, mask


def recolor_emission(ship, mask, state):
    a = np.asarray(ship).copy()
    em = np.asarray(mask) > 0
    if state == 0:
        a[em, :3] = rgb("#1c3c68")
    elif state == 1:
        a[em, :3] = rgb("#1569c8")
    elif state == 2:
        a[em, :3] = rgb("#aaf2fb")
    return Image.fromarray(a, "RGBA")


def glow_behind(ship, mask, strength):
    if not strength:
        return ship
    core = mask.point(lambda x: 255 if x else 0)
    ring2 = core.filter(ImageFilter.MaxFilter(5))
    ring1 = core.filter(ImageFilter.MaxFilter(3))
    layer = Image.new("RGBA", (CELL, CELL))
    aa = np.asarray(layer).copy()
    body = np.asarray(ship.getchannel("A")) > 0
    outer = (np.asarray(ring2) > 0) & ~body
    inner = (np.asarray(ring1) > 0) & ~body
    aa[outer] = (*rgb("#46c8fd"), min(50, strength))
    aa[inner] = (*rgb("#46c8fd"), min(95, strength * 2))
    layer = Image.fromarray(aa, "RGBA")
    layer.alpha_composite(ship)
    return layer


def engines(ship, spec, state):
    d = ImageDraw.Draw(ship)
    y = spec["nozzle_y"]
    for x in spec["engines"]:
        if state == 0:
            d.ellipse((x-3, y-2, x+3, y+2), fill=METAL[1], outline=INK)
        elif state == 1:
            d.ellipse((x-3, y-2, x+3, y+2), fill=NEON[0], outline=METAL[0])
            d.point((x, y), fill=NEON[1])
        else:
            d.ellipse((x-4, y-2, x+4, y+3), fill=NEON[1], outline=METAL[0])
            d.ellipse((x-2, y-1, x+2, y+2), fill=NEON[3])
            d.point((x, y), fill=NEON[4])
            if state == 2:
                bottom = min(175, y + 17)
                d.polygon([(x-4, y+3), (x+4, y+3), (x+2, bottom-5), (x, bottom), (x-2, bottom-5)], fill=NEON[1])
                d.polygon([(x-2, y+3), (x+2, y+3), (x, bottom-3)], fill=NEON[3])
                d.point((x, bottom-4), fill=NEON[4])
            else:
                bottom = min(172, y + 9)
                d.polygon([(x-3, y+3), (x+3, y+3), (x, bottom)], fill=NEON[1])
                d.line([(x, y+3), (x, bottom-3)], fill=NEON[3], width=1)
    return ship


def damage(ship, name, spec):
    d = ImageDraw.Draw(ship)
    if "floe" in name:
        cracks = [[(57, 70), (52, 77), (55, 82), (48, 89)], [(109, 64), (113, 72), (108, 77), (113, 84)], [(96, 85), (93, 90), (97, 94)]]
    elif "siren" in name:
        cracks = [[(82, 71), (88, 77), (85, 83), (92, 89)], [(108, 70), (105, 77), (109, 82), (104, 88)]]
    elif "tender" in name:
        cracks = [[(73, 89), (78, 94), (75, 99), (81, 105)], [(119, 109), (115, 115), (121, 120)]]
    else:
        # A torn-away front plate reveals steel; neighboring two remain broken.
        d.polygon([(67, 77), (88, 86), (98, 95), (84, 101), (64, 88)], fill=METAL[1], outline=ICE[2])
        d.line([(66, 78), (75, 75), (81, 78)], fill=ICE[5], width=1)
        cracks = [[(110, 62), (105, 68), (112, 75), (107, 80)], [(95, 104), (91, 109), (96, 113)]]
    for points in cracks:
        d.line(points, fill=ICE[0], width=2, joint="curve")
        shifted = [(x+1, y-1) for x, y in points]
        d.line(shifted, fill=ICE[4], width=1, joint="curve")
    x = spec["engines"][1]
    y = spec["nozzle_y"]
    d.ellipse((x-4, y-2, x+4, y+4), fill=METAL[0], outline=ICE[2])
    for end in [((x+5, y-6), (x+7, y-10)), ((x+4, y+2), (x+9, y+4)), ((x-1, y+5), (x+2, y+9))]:
        d.line(end, fill=NEON[2], width=1)
        d.point(end[1], fill=NEON[4])
    d.rectangle((x+7, y-5, x+8, y-4), fill="#e0309e")
    return ship


def build_strip(name, spec):
    hull, mask = painted_hull(spec)
    cells = []
    for state in range(7):
        if state in (4, 5):
            continue
        ship = recolor_emission(hull, mask, state)
        ship = glow_behind(ship, mask, 0 if state == 0 else (20 if state == 1 else 42))
        ship = engines(ship, spec, state)
        if state == 6:
            ship = damage(ship, name, spec)
        cells.append((state, ship))
    by_state = dict(cells)
    # Rotate the finished hover hull, including its nozzles, about the same cell anchor.
    by_state[4] = by_state[3].rotate(8, Image.Resampling.NEAREST, expand=False, center=(96, 96))
    by_state[5] = by_state[3].rotate(-8, Image.Resampling.NEAREST, expand=False, center=(96, 96))
    strip = Image.new("RGBA", (CELL * 7, CELL))
    for i in range(7):
        strip.alpha_composite(by_state[i], (i * CELL, 0))
    strip.save(ROOT / f"{name}.png")
    Image.open(SRC / spec["master"]).save(ROOT / f"{name}_concept.png")
    return by_state


def build_previews(all_cells):
    rime = Image.open(ROOT / "rimebreaker.png").convert("RGBA")
    contact = Image.new("RGBA", (1344 * 2, 192 * 2 * 5))
    for row, strip in enumerate([rime] + [Image.open(ROOT / f"{n}.png").convert("RGBA") for n in SHIPS]):
        contact.alpha_composite(strip.resize((2688, 384), Image.Resampling.NEAREST), (0, row * 384))
    contact.save(ROOT / "contact_sheet.png")
    frames = []
    for state in (3, 4, 3, 5):
        frame = Image.new("RGBA", (192 * 2, 192 * 2 * 4))
        for row, name in enumerate(SHIPS):
            frame.alpha_composite(all_cells[name][state].resize((384, 384), Image.Resampling.NEAREST), (0, row * 384))
        frames.append(frame)
    frames[0].save(ROOT / "preview.gif", save_all=True, append_images=frames[1:], duration=[270, 170, 270, 170], loop=0, disposal=2, transparency=0)


def main():
    all_cells = {name: build_strip(name, spec) for name, spec in SHIPS.items()}
    build_previews(all_cells)


if __name__ == "__main__":
    main()
