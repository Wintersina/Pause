#!/usr/bin/env python3
"""Deterministic Frost elite light overlays and launch-site strips."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
CELL = 384
FRAME_COUNT = 10
NAMES = ("rimebreaker", "floe_harrower", "cryo_siren", "glacier_tender", "whiteout_sentinel")
SITES = {
    "ice_locked_offshore_rig": ((.500, .500), "rig"),
    "cryo_silo": ((.500, .520), "silo"),
    "glacier_cave_hangar": ((.500, .437), "cave"),
    "icebreaker_deck": ((.500, .515), "ship"),
}

CYAN = (105, 235, 255)
WHITE = (235, 255, 255)
AMBER = (255, 190, 70)


def hsv_score(rgb):
    r, g, b = [v / 255.0 for v in rgb]
    hi, lo = max(r, g, b), min(r, g, b)
    return hi, (hi - lo) / max(hi, 1e-6)


def feature_points(cell):
    """Find stable accent pixels from the parked cell, inside the hull only."""
    pix = cell.load()
    alpha = cell.getchannel("A")
    box = alpha.point(lambda v: 255 if v > 80 else 0).getbbox()
    if not box:
        return []
    x0, y0, x1, y1 = box
    candidates = []
    for y in range(y0 + 4, y1 - 3, 3):
        for x in range(x0 + 4, x1 - 3, 3):
            r, g, b, a = pix[x, y]
            if a < 120:
                continue
            value, sat = hsv_score((r, g, b))
            if sat > .14 and value > .30:
                score = value * (.35 + sat) + max(r, g, b) / 255.0 * .18
                candidates.append((score, x, y))
    candidates.sort(reverse=True)
    points = []
    for score, x, y in candidates:
        if all((x - px) ** 2 + (y - py) ** 2 >= 17 ** 2 for px, py in points):
            points.append((x, y))
        if len(points) >= 18:
            break
    return sorted(points, key=lambda p: (p[1], p[0]))


def glow_layer(size, points, phase, damaged=False):
    glow = Image.new("RGBA", size)
    core = Image.new("RGBA", size)
    gd, cd = ImageDraw.Draw(glow), ImageDraw.Draw(core)
    for i, (x, y) in enumerate(points):
        beacon = i < 3
        emitter = y > size[1] * .68
        chase = (phase - i) % max(len(points), 1) < 3
        if damaged:
            on = ((i + phase) % 3 != 0) and (i % 4 != 0 or phase == 0)
            strength = .42 if on else .08
        else:
            on = chase or (beacon and phase in (1, 2)) or (emitter and phase in (2, 3, 4, 5))
            strength = 1.0 if on else .20
        color = AMBER if (i % 7 == 0 or beacon) else (WHITE if i % 5 == 0 else CYAN)
        if not on:
            color = tuple(round(c * .40) for c in color)
        gd.ellipse((x - 3, y - 3, x + 3, y + 3), fill=(*color, round(48 * strength)))
        gd.ellipse((x - 1, y - 1, x + 1, y + 1), fill=(*color, round(105 * strength)))
        if on:
            radius = 2 if emitter else 1
            cd.rectangle((x - radius, y - radius, x + radius, y + radius), fill=(*color, round(225 * strength)))
            if emitter:
                cd.point((x, y), fill=(*WHITE, round(250 * strength)))
    return Image.alpha_composite(glow.filter(ImageFilter.GaussianBlur(.9)), core)


def build_lights(name):
    base = Image.open(ROOT / f"frost_elite_{name}.png").convert("RGBA")
    out = Image.new("RGBA", base.size)
    previews = []
    points = feature_points(base.crop((0, 0, CELL, CELL)))
    if len(points) < 6:
        raise RuntimeError(f"not enough accent points for {name}: {len(points)}")
    for frame in range(FRAME_COUNT):
        if frame == 0:
            phase, strength, damaged = 0, .10, False
        elif frame == 1:
            phase, strength, damaged = 1, .22, False
        elif frame == 2:
            phase, strength, damaged = 2, .58, False
        elif frame < 9:
            phase, strength, damaged = frame - 3, 1.0, False
        else:
            phase, strength, damaged = 6, .52, True
        overlay = glow_layer((CELL, CELL), points, phase, damaged)
        overlay.putalpha(overlay.getchannel("A").point(lambda v: round(v * strength)))
        out.alpha_composite(overlay, (frame * CELL, 0))
        cell = base.crop((frame * CELL, 0, (frame + 1) * CELL, CELL))
        previews.append(Image.alpha_composite(cell, overlay))
    out.save(HERE / f"frost_elite_{name}_lights.png")
    strip = Image.new("RGBA", base.size)
    for i, image in enumerate(previews):
        strip.alpha_composite(image, (i * CELL, 0))
    strip.save(HERE / f"frost_elite_{name}_preview_lights.png")
    previews[3].save(HERE / f"frost_elite_{name}_lights.gif", save_all=True,
                     append_images=previews[4:9], duration=130, loop=0, disposal=2, transparency=0)


def normalize_site(path):
    image = Image.open(path).convert("RGBA")
    long_side = max(image.size)
    if long_side > 768:
        scale = 768 / long_side
        image = image.resize((round(image.width * scale), round(image.height * scale)), Image.Resampling.LANCZOS)
    image.save(path)
    return image


def launch_frame(base, pad, kind, frame):
    result = base.copy()
    w, h = result.size
    x, y = pad
    draw = ImageDraw.Draw(result)
    amounts = (.18, .14, .09, .035, 0.0, .08)
    if kind in ("rig", "ship"):
        half = int(min(w, h) * amounts[frame])
        if half:
            draw.rectangle((x - half, y - int(half * 1.25), x + half, y + int(half * 1.25)), fill=(4, 12, 22, 238))
    elif kind == "silo":
        radius = int(min(w, h) * amounts[frame] * 1.25)
        if radius:
            draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=(4, 12, 22, 238))
    else:
        half = int(min(w, h) * amounts[frame])
        if half:
            draw.rectangle((x - half * 2, y - half, x + half * 2, y + half), fill=(4, 12, 22, 238))
    # One small amber warning lamp only; no halo, flare or steam.
    if frame == 1:
        draw.rectangle((x + int(min(w, h) * .13), y - 2, x + int(min(w, h) * .13) + 3, y + 1), fill=(*AMBER, 255))
    return result


def build_site_strips():
    for name, (pad, kind) in SITES.items():
        path = HERE / f"{name}.png"
        if not path.exists():
            continue
        base = normalize_site(path)
        point = (round(pad[0] * base.width), round(pad[1] * base.height))
        strip = Image.new("RGBA", (base.width * 6, base.height))
        for frame in range(6):
            strip.alpha_composite(launch_frame(base, point, kind, frame), (frame * base.width, 0))
        strip.save(HERE / f"{name}_launch.png")


def build_contact_sheet():
    card_w, card_h = 300, 285
    sheet = Image.new("RGBA", (card_w * 5, card_h * 2), (5, 14, 27, 255))
    draw = ImageDraw.Draw(sheet)
    for i, name in enumerate(SITES):
        p = HERE / f"{name}.png"
        if p.exists():
            src = Image.open(p).convert("RGBA")
            src.thumbnail((card_w - 20, card_h - 38), Image.Resampling.LANCZOS)
            sheet.alpha_composite(src, (i * card_w + (card_w - src.width) // 2, 30 + (card_h - 38 - src.height) // 2))
        draw.text((i * card_w + 8, 9), name.replace("_", " "), fill=(*WHITE, 255))
    for i, name in enumerate(NAMES):
        p = HERE / f"frost_elite_{name}_preview_lights.png"
        src = Image.open(p).crop((3 * CELL, 0, 4 * CELL, CELL)).convert("RGBA")
        src.thumbnail((card_w - 20, card_h - 38), Image.Resampling.NEAREST)
        sheet.alpha_composite(src, (i * card_w + (card_w - src.width) // 2, card_h + 30 + (card_h - 38 - src.height) // 2))
        draw.text((i * card_w + 8, card_h + 9), name.replace("_", " "), fill=(*WHITE, 255))
    sheet.save(HERE / "contact_sheet.png")


if __name__ == "__main__":
    for ship in NAMES:
        build_lights(ship)
    build_site_strips()
    build_contact_sheet()
