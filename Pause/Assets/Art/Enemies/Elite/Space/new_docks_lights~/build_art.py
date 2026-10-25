#!/usr/bin/env python3
"""Reproducible art build for Space elite light overlays and dock launch strips."""
import sys
from pathlib import Path
import math
from PIL import Image, ImageDraw, ImageFilter, ImageChops

HERE = Path(__file__).resolve().parent
ELITES = HERE.parent
CELL = 384
CELLS = 17          # 0 parked .. 2 ignition, 3..10 burn, 11..16 damaged burn loop (+ 17..22 the bank loops when <name>_banks.png exists)
NAMES = ("rift_lancer", "eventide_bastion", "orbit_reaver", "singularity_hauler")
PALETTE = {
    "rift_lancer": ((55, 220, 255), (255, 55, 205)),
    "eventide_bastion": ((166, 90, 255), (65, 225, 255)),
    "orbit_reaver": ((255, 45, 205), (255, 125, 225)),
    "singularity_hauler": ((255, 180, 55), (255, 50, 190)),
}


def hsv_score(rgb):
    r, g, b = [v / 255.0 for v in rgb]
    hi, lo = max(r, g, b), min(r, g, b)
    return (hi, (hi - lo) / max(hi, 1e-6))


def feature_points(cell):
    """Find accent/lamp/emitter candidates from bright coloured hull pixels."""
    pix = cell.load()
    a = cell.getchannel("A")
    box = a.point(lambda v: 255 if v > 80 else 0).getbbox()
    if not box:
        return []
    x0, y0, x1, y1 = box
    candidates = []
    for y in range(y0 + 4, y1 - 3, 3):
        for x in range(x0 + 4, x1 - 3, 3):
            r, g, b, al = pix[x, y]
            if al < 120:
                continue
            value, sat = hsv_score((r, g, b))
            # Coloured, luminous pixels are the ship's accent strips and lamps.
            score = value * (0.35 + sat) + max(r, g, b) / 255.0 * 0.18
            if sat > 0.18 and value > 0.32:
                candidates.append((score, x, y))
    candidates.sort(reverse=True)
    points = []
    min_dist = 17
    for score, x, y in candidates:
        if all((x - px) ** 2 + (y - py) ** 2 >= min_dist ** 2 for px, py in points):
            points.append((x, y))
        if len(points) >= 18:
            break
    # Keep stable semantic groups: upper beacon/spine, body running lamps, emitters.
    points.sort(key=lambda p: (p[1], p[0]))
    return points


def glow_layer(size, points, colors, phase, damaged=False):
    glow = Image.new("RGBA", size)
    core = Image.new("RGBA", size)
    gd, cd = ImageDraw.Draw(glow), ImageDraw.Draw(core)
    for i, (x, y) in enumerate(points):
        is_beacon = i < 3
        is_emitter = y > size[1] * .68
        chase = (phase - i) % max(len(points), 1) < 3
        if damaged:
            on = ((i + phase) % 3 != 0) and (i % 4 != 0 or phase == 0)
            strength = .38 if on else .10
        else:
            on = chase or (is_beacon and phase in (1, 2)) or (is_emitter and phase in (2, 3, 4, 5))
            strength = 1.0 if on else .28
        color = colors[1] if (i % 5 == 0 or is_beacon) else colors[0]
        if not on:
            color = tuple(round(c * .42) for c in color)
        r = 1.5 if is_emitter else 1.2
        gd.ellipse((x - 4, y - 4, x + 4, y + 4), fill=(*color, round(62 * strength)))
        gd.ellipse((x - 2, y - 2, x + 2, y + 2), fill=(*color, round(118 * strength)))
        if on:
            cd.rectangle((x - 1, y - 1, x + 1, y + 1), fill=(*color, round(225 * strength)))
            if is_emitter:
                cd.point((x, y), fill=(255, 255, 255, round(250 * strength)))
    glow = glow.filter(ImageFilter.GaussianBlur(1.25))
    return Image.alpha_composite(glow, core)


def build_lights(name):
    base = Image.open(ELITES / f"space_elite_{name}.png").convert("RGBA")
    comp = Image.open(ELITES / f"space_elite_{name}_damaged.png").convert("RGBA")      # cells 12..16 (the damaged burn loop's other drawings)
    bank_path = ELITES / f"space_elite_{name}_banks.png"                              # cells 17..19 bank left loop, 20..22 bank right loop
    banks = Image.open(bank_path).convert("RGBA") if bank_path.exists() else None
    dcells = 12 + comp.width // CELL
    cells = dcells + (banks.width // CELL if banks else 0)
    whole = Image.new("RGBA", (CELL * cells, CELL))
    whole.alpha_composite(base, (0, 0))
    whole.alpha_composite(comp, (CELL * 12, 0))
    if banks:
        whole.alpha_composite(banks, (CELL * dcells, 0))
    base = whole
    out = Image.new("RGBA", base.size)
    previews = []
    points = feature_points(base.crop((0, 0, CELL, CELL)))
    if len(points) < 6:
        raise RuntimeError(f"could not find enough hull features for {name}")
    bank_points = None
    if banks:
        # the bank cells' lamps: found the same way on the BARE bank-left drawing (no plumes: they would read as lamps), mirrored for bank right
        sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "src~" / "space_elites_engine_burn"))
        import build_banks
        bare_left = build_banks.layout(name)[1]
        bank_points = feature_points(bare_left)
        if len(bank_points) < 6:
            raise RuntimeError(f"could not find enough bank hull features for {name}")
    per_bank = (cells - dcells) // 2
    for frame in range(cells):
        cell = base.crop((frame * CELL, 0, (frame + 1) * CELL, CELL))
        if frame == 0:
            phase, strength, damaged = 0, .18, False
        elif frame == 1:
            phase, strength, damaged = 1, .42, False
        elif frame == 2:
            phase, strength, damaged = 2, .72, False
        elif 3 <= frame <= 10:
            phase, strength, damaged = frame - 3, 1.0, False
        elif frame < dcells:
            phase, strength, damaged = frame - 11, .65, True
        else:
            phase, strength, damaged = (frame - dcells) % per_bank, 1.0, False     # the bank loops: lit like the burn cycle's first drawings
        if frame >= dcells:
            overlay = glow_layer((CELL, CELL), bank_points, PALETTE[name], phase, damaged)
            if frame - dcells >= per_bank:
                overlay = overlay.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        else:
            overlay = glow_layer((CELL, CELL), points, PALETTE[name], phase, damaged)
        overlay.putalpha(overlay.getchannel("A").point(lambda v: round(v * strength)))
        out.alpha_composite(overlay, (frame * CELL, 0))
        previews.append(Image.alpha_composite(cell, overlay))
    # (three files, like the hull: a texture wider than 4096 px is downscaled on import)
    out.crop((0, 0, CELL * 12, CELL)).save(HERE / f"space_elite_{name}_lights.png")
    out.crop((CELL * 12, 0, CELL * dcells, CELL)).save(HERE / f"space_elite_{name}_lights_damaged.png")
    if banks:
        out.crop((CELL * dcells, 0, CELL * cells, CELL)).save(HERE / f"space_elite_{name}_lights_banks.png")
    strip = Image.new("RGBA", (CELL * cells, CELL))
    for i, image in enumerate(previews):
        strip.alpha_composite(image, (i * CELL, 0))
    strip.save(HERE / f"space_elite_{name}_preview_lights.png")
    # Eight burn-cycle cells as an animated review GIF.
    previews[3].save(HERE / f"space_elite_{name}_lights.gif", save_all=True,
                     append_images=previews[4:11], duration=130, loop=0, disposal=2, transparency=0)


DOCKS = {
    # Pad is recorded as a normalized pixel point so resizing remains exact.
    "orbital_shipyard_gantry": ((.500, .634), "gantry"),
    "carrier_hangar": ((.564, .704), "carrier"),
    "ring_gate": ((.499, .418), "ring"),
    "derelict_dock": ((.500, .618), "derelict"),
}


def launch_frame(base, pad, kind, frame):
    result = base.copy()
    w, h = result.size
    x, y = pad
    draw = ImageDraw.Draw(result)
    # The opening is intentionally animated with dark mechanical shutters and emissive edge light.
    if kind == "gantry":
        half = int(min(w, h) * (.17, .13, .08, .03, .0, .06)[frame])
        if half:
            draw.rectangle((x - half, y - int(half * 1.8), x + half, y + int(half * 1.8)), fill=(5, 5, 18, 230))
    elif kind == "carrier":
        half = int(min(w, h) * (.20, .15, .10, .03, .0, .10)[frame])
        if half:
            draw.rectangle((x - half * 2, y - half, x + half * 2, y + half), fill=(7, 7, 19, 235))
    elif kind == "ring":
        radius = int(min(w, h) * (.25, .20, .13, .05, .0, .12)[frame])
        if radius:
            draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=(3, 3, 15, 240))
    else:
        half = int(min(w, h) * (.18, .14, .08, .03, .0, .08)[frame])
        if half:
            draw.rectangle((x - half, y - int(half * 1.35), x + half, y + int(half * 1.35)), fill=(3, 3, 12, 235))
    if frame in (1, 2, 3, 4):
        glow = Image.new("RGBA", result.size)
        gd = ImageDraw.Draw(glow)
        col = (255, 75, 211) if kind in ("ring", "derelict") else (65, 225, 255)
        radius = int(min(w, h) * (.025 + frame * .008))
        gd.ellipse((x-radius, y-radius, x+radius, y+radius), fill=(*col, 95 if frame < 3 else 165))
        glow = glow.filter(ImageFilter.GaussianBlur(max(2, radius // 4)))
        result = Image.alpha_composite(result, glow)
        d = ImageDraw.Draw(result)
        d.ellipse((x-3, y-3, x+3, y+3), fill=(*col, 240))
    return result


def build_dock_strips():
    for name, (pad, kind) in DOCKS.items():
        p = HERE / f"{name}.png"
        if not p.exists():
            continue
        base = Image.open(p).convert("RGBA")
        long_side = max(base.size)
        if long_side > 1024:
            scale = 1024 / long_side
            base = base.resize((round(base.width * scale), round(base.height * scale)), Image.Resampling.LANCZOS)
            base.save(p)
        pad_px = (round(pad[0] * base.width), round(pad[1] * base.height))
        strip = Image.new("RGBA", (base.width * 6, base.height))
        for frame in range(6):
            strip.alpha_composite(launch_frame(base, pad_px, kind, frame), (frame * base.width, 0))
        strip.save(HERE / f"{name}_launch.png")


def build_contact_sheet():
    """Eight-card review sheet: four docks and one lit ship cell per elite."""
    card_w, card_h = 360, 330
    sheet = Image.new("RGBA", (card_w * 4, card_h * 2), (10, 8, 35, 255))
    draw = ImageDraw.Draw(sheet)
    dock_names = tuple(DOCKS)
    for i, name in enumerate(dock_names):
        src = Image.open(HERE / f"{name}.png").convert("RGBA")
        src.thumbnail((card_w - 26, card_h - 42), Image.Resampling.LANCZOS)
        x = i * card_w + (card_w - src.width) // 2
        y = 30 + (card_h - 42 - src.height) // 2
        sheet.alpha_composite(src, (x, y))
        draw.text((i * card_w + 10, 10), name.replace("_", " "), fill=(220, 205, 255, 255))
    for i, name in enumerate(NAMES):
        strip = Image.open(HERE / f"space_elite_{name}_preview_lights.png").convert("RGBA")
        src = strip.crop((3 * CELL, 0, 4 * CELL, CELL))
        src.thumbnail((card_w - 26, card_h - 42), Image.Resampling.LANCZOS)
        x = i * card_w + (card_w - src.width) // 2
        y = card_h + 30 + (card_h - 42 - src.height) // 2
        sheet.alpha_composite(src, (x, y))
        draw.text((i * card_w + 10, card_h + 10), name.replace("_", " ") + " lit", fill=(220, 205, 255, 255))
    sheet.convert("RGB").save(HERE / "contact_sheet.png")


if __name__ == "__main__":
    for name in NAMES:
        build_lights(name)
    build_dock_strips()
    build_contact_sheet()
