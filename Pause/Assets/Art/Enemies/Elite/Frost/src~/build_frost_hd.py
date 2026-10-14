"""Frost elites at true high resolution: build_new_elites.py's recipe on a 384 px cell (Z = 2). The same painted masters are
area-averaged (premultiplied Lanczos) to twice the old hull size instead of NEAREST-decimated, then classified into the same material
ramps (INK / METAL / ICE / NEON / BRASS), so the hull keeps the Frost palette and gains the painted detail. Hull ink edge, glow,
engines and cracks are the old 192-space code through elite_hd.ZDraw; banks are bicubic rotations of the finished hover hull.
The death strips (build_death_strips_hd.py) follow. Writes ../hd~/ (Rimebreaker has no master and is not rebuilt; Cryo Siren's live art is the Codex v2 silver redraw drawn directly at 192, the blue cryo_siren_master.png in src~ is a different, unused design).
    python3 build_frost_hd.py [floe_harrower glacier_tender whiteout_sentinel]
"""
from __future__ import annotations
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

HERE = Path(__file__).resolve().parent
ART = HERE.parents[3]
sys.path.insert(0, str(HERE.parent.parent / "src~"))
import elite_hd as E
from elite_hd import Z, CELL, ZDraw

OUT = HERE.parent / "hd~"
INK = "#05060c"
METAL = ["#0c1020", "#1e2638", "#33405c", "#4f6188", "#7d93bd"]
ICE = ["#0a1734", "#1c3c68", "#2a62ae", "#4a96e6", "#9fd2f2", "#e2fbff"]
NEON = ["#1569c8", "#1898f7", "#46ddfd", "#aaf2fb", "#f8fefc"]
BRASS = ["#29251e", "#594732", "#987451", "#c4a16e", "#e2c59a"]
SHIPS = {
    "frost_elite_floe_harrower": dict(master="floe_harrower_master.png", size=(118, 106), engines=(83, 109), nozzle_y=143, crop=(50, 60, 142, 140)),
    "frost_elite_glacier_tender": dict(master="glacier_tender_alt.png", size=(98, 140), engines=(77, 115), nozzle_y=158, crop=(60, 50, 132, 122)),
    "frost_elite_whiteout_sentinel": dict(master="whiteout_sentinel_alt.png", size=(120, 126), engines=(84, 108), nozzle_y=151, crop=(60, 50, 132, 122)),
}


def rgb(h): return tuple(bytes.fromhex(h[1:]))


def choose_colors(raw):
    v = raw.astype(np.int16)
    r, g, b = v[:, :, 0], v[:, :, 1], v[:, :, 2]
    neon = (g > r * 1.38) & (b > r * 1.34) & (g > 65)
    brass = (r > g * 1.13) & (g > b * 1.12) & (r > 50)
    light_ice = ((r + g + b) > 365) & (b >= r * 1.03) & ~neon
    palette = np.asarray([rgb(x) for x in ([INK] + METAL + ICE + NEON + BRASS)], np.int16)
    answer = np.zeros_like(v, np.uint8)
    for mask, choices in [(neon, np.arange(12, 17)), (brass & ~neon, np.arange(17, 22)), (light_ice & ~brass, np.arange(6, 12)), (~neon & ~brass & ~light_ice, np.arange(0, 12))]:
        if not mask.any(): continue
        delta = v[mask][:, None, :] - palette[choices][None, :, :]
        score = (delta.astype(np.int32) ** 2 * np.array([2, 3, 3])).sum(axis=2)
        answer[mask] = palette[choices[np.argmin(score, axis=1)]].astype(np.uint8)
    return answer, neon


def painted_hull(spec):
    w, h = spec["size"]
    src = E.fit_to_master(Image.open(HERE / spec["master"]).convert("RGBA"), 128, (w, h), spec["master"])
    raw = np.asarray(src)
    colors, neon = choose_colors(raw[:, :, :3])
    solid = raw[:, :, 3] >= 128
    hull = Image.fromarray(np.concatenate([colors, (solid * 255)[:, :, None].astype(np.uint8)], axis=2), "RGBA")
    ext = Image.new("RGBA", hull.size)
    a = hull.getchannel("A")
    rim = a.filter(ImageFilter.MaxFilter(2 * Z + 1))        # a 1 px (x Z) exterior ink
    edge = np.asarray(rim) > np.asarray(a)
    ea = np.asarray(ext).copy(); ea[edge] = (*rgb(INK), 255)
    ext = Image.fromarray(ea, "RGBA"); ext.alpha_composite(hull)
    x, y = (CELL - hull.width) // 2, (CELL - hull.height) // 2
    result = Image.new("RGBA", (CELL, CELL)); result.alpha_composite(ext, (x, y))
    mask = Image.new("L", (CELL, CELL)); mask.paste(Image.fromarray((neon & solid).astype(np.uint8) * 255, "L"), (x, y))
    return result, mask


def recolor_emission(ship, mask, state):
    a = np.asarray(ship).copy(); em = np.asarray(mask) > 0
    if state == 0: a[em, :3] = rgb("#1c3c68")
    elif state == 1: a[em, :3] = rgb("#1569c8")
    elif state == 2: a[em, :3] = rgb("#aaf2fb")
    return Image.fromarray(a, "RGBA")


def glow_behind(ship, mask, strength):
    if not strength: return ship
    core = mask.point(lambda x: 255 if x else 0)
    ring2 = core.filter(ImageFilter.MaxFilter(4 * Z + 1)); ring1 = core.filter(ImageFilter.MaxFilter(2 * Z + 1))
    aa = np.zeros((CELL, CELL, 4), np.uint8)
    body = np.asarray(ship.getchannel("A")) > 0
    aa[(np.asarray(ring2) > 0) & ~body] = (*rgb("#46c8fd"), min(50, strength))
    aa[(np.asarray(ring1) > 0) & ~body] = (*rgb("#46c8fd"), min(95, strength * 2))
    layer = Image.fromarray(aa, "RGBA"); layer.alpha_composite(ship)
    return layer


def engines(ship, spec, state):
    ship = ship.copy(); d = ZDraw(ship)
    y = spec["nozzle_y"]
    for x in spec["engines"]:
        if state == 0: d.ellipse((x - 3, y - 2, x + 3, y + 2), fill=METAL[1], outline=INK)
        elif state == 1:
            d.ellipse((x - 3, y - 2, x + 3, y + 2), fill=NEON[0], outline=METAL[0]); d.point((x, y), fill=NEON[1])
        else:
            d.ellipse((x - 4, y - 2, x + 4, y + 3), fill=NEON[1], outline=METAL[0]); d.ellipse((x - 2, y - 1, x + 2, y + 2), fill=NEON[3]); d.point((x, y), fill=NEON[4])
            if state == 2:
                bottom = min(175, y + 17)
                d.polygon([(x - 4, y + 3), (x + 4, y + 3), (x + 2, bottom - 5), (x, bottom), (x - 2, bottom - 5)], fill=NEON[1])
                d.polygon([(x - 2, y + 3), (x + 2, y + 3), (x, bottom - 3)], fill=NEON[3]); d.point((x, bottom - 4), fill=NEON[4])
            else:
                bottom = min(172, y + 9)
                d.polygon([(x - 3, y + 3), (x + 3, y + 3), (x, bottom)], fill=NEON[1]); d.line([(x, y + 3), (x, bottom - 3)], fill=NEON[3], width=1)
    return ship


def damage(ship, name, spec):
    ship = ship.copy(); d = ZDraw(ship)
    if "floe" in name: cracks = [[(57, 70), (52, 77), (55, 82), (48, 89)], [(109, 64), (113, 72), (108, 77), (113, 84)], [(96, 85), (93, 90), (97, 94)]]
    elif "siren" in name: cracks = [[(82, 71), (88, 77), (85, 83), (92, 89)], [(108, 70), (105, 77), (109, 82), (104, 88)]]
    elif "tender" in name: cracks = [[(73, 89), (78, 94), (75, 99), (81, 105)], [(119, 109), (115, 115), (121, 120)]]
    else:
        d.polygon([(67, 77), (88, 86), (98, 95), (84, 101), (64, 88)], fill=METAL[1], outline=ICE[2])
        d.line([(66, 78), (75, 75), (81, 78)], fill=ICE[5], width=1)
        cracks = [[(110, 62), (105, 68), (112, 75), (107, 80)], [(95, 104), (91, 109), (96, 113)]]
    for points in cracks:
        d.line(points, fill=ICE[0], width=2, joint="curve")
        d.line([(x + 1, y - 1) for x, y in points], fill=ICE[4], width=1, joint="curve")
    x = spec["engines"][1]; y = spec["nozzle_y"]
    d.ellipse((x - 4, y - 2, x + 4, y + 4), fill=METAL[0], outline=ICE[2])
    for end in [((x + 5, y - 6), (x + 7, y - 10)), ((x + 4, y + 2), (x + 9, y + 4)), ((x - 1, y + 5), (x + 2, y + 9))]:
        d.line(end, fill=NEON[2], width=1); d.point(end[1], fill=NEON[4])
    d.rectangle((x + 7, y - 5, x + 8, y - 4), fill="#e0309e")
    return ship


def build(name):
    spec = SHIPS[name]
    hull, mask = painted_hull(spec)
    by_state = {}
    for state in (0, 1, 2, 3, 6):
        ship = recolor_emission(hull, mask, state)
        ship = glow_behind(ship, mask, 0 if state == 0 else (20 if state == 1 else 42))
        ship = engines(ship, spec, state)
        if state == 6: ship = damage(ship, name, spec)
        by_state[state] = ship
    by_state[4] = E.rotate(by_state[3], 8, (96, 96))
    by_state[5] = E.rotate(by_state[3], -8, (96, 96))
    strip = E.paste_strip([by_state[i] for i in range(7)])
    # the death strip: build_death_strips_hd.py reads this strip's hover cell
    OUT.mkdir(exist_ok=True)
    strip.save(OUT / f"{name}.png", optimize=True)
    import build_death_strips_hd as D
    death = D.build_one(name, by_state[3], "frost", CELL)
    E.emit(OUT, name, strip, death, ART / "Resources/Elites/Defs", HERE.parent / f"{name}.png", spec["crop"])


if __name__ == "__main__":
    sys.path.insert(0, str(HERE.parent.parent / "src~"))
    OUT.mkdir(exist_ok=True)
    for n in (sys.argv[1:] or list(SHIPS)): build("frost_elite_" + n if not n.startswith("frost_") else n)
