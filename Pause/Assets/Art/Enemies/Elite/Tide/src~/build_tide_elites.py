"""Build Tide elite strips from five image-generated painted masters.

The masters in this directory are the authored painted key frames. All scaling,
bank deformation and sprite fragment motion use nearest-neighbour sampling.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import colorsys
import math
import shutil

import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
SIZE = 192
MINT = (124, 242, 192)
DEEP = (24, 76, 70)
BONE = (229, 239, 216)
VIOLET = (104, 108, 184)


@dataclass(frozen=True)
class Ship:
    name: str
    width: int
    height: int
    center_y: int
    nozzles: tuple[tuple[int, int], ...]
    muzzles: tuple[tuple[str, int, int], ...]
    core: tuple[int, int]
    moving: str
    site: str
    attack: str


SHIPS = (
    Ship("riptide", 103, 148, 96, ((87, 160), (105, 160)), (("lance tip", 96, 22),), (96, 72), "swept fins flex", "trench hatch", "surf dash / lance charge"),
    Ship("bathyscaphe", 122, 109, 96, ((64, 142), (128, 142)), (("port claw", 35, 119), ("starboard claw", 157, 119), ("charge rack", 96, 136)), (96, 91), "cable claws reach", "rig bay", "depth charge lob"),
    Ship("trawler", 124, 111, 96, ((80, 145), (112, 145)), (("port net boom", 34, 94), ("starboard net boom", 158, 94), ("winch", 96, 109)), (96, 67), "net booms bow", "reef dock", "lane snare net"),
    Ship("thunderfin", 112, 127, 96, ((89, 151), (103, 151)), (("lightning rod", 96, 30), ("port coil", 66, 77), ("starboard coil", 126, 77)), (96, 71), "ray fins ripple and coils pulse", "vent stack", "telegraphed lane strike"),
    Ship("pearl_warden", 120, 113, 96, ((71, 144), (121, 144)), (("pearl aperture", 96, 77), ("port shell", 49, 84), ("starboard shell", 143, 84)), (96, 77), "shell halves breathe", "wreck bay", "pearl curtain / shell shield"),
)


def paint_master(ship: Ship) -> Image.Image:
    original = Image.open(SRC / f"{ship.name}_master.png").convert("RGBA")
    alpha = original.getchannel("A")
    bbox = alpha.point(lambda a: 255 if a >= 32 else 0).getbbox()
    assert bbox
    cropped = original.crop(bbox).resize((ship.width, ship.height), Image.Resampling.NEAREST)
    pixels = np.asarray(cropped).copy()
    # Remove fringe texels and bend any unintended crimson/brown-red hue to
    # the Tide bronze side of the hue wheel. Keep the painted tonal variance.
    h, w = pixels.shape[:2]
    for y in range(h):
        for x in range(w):
            r, g, b, a = map(int, pixels[y, x])
            if a < 24:
                pixels[y, x] = (0, 0, 0, 0)
                continue
            hue, sat, val = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            degree = hue * 360
            if sat > 0.15 and (degree >= 345 or degree < 16):
                degree = 21
            if 15 <= degree <= 42 and sat > 0.45:
                sat = 0.45
            nr, ng, nb = colorsys.hsv_to_rgb(degree / 360, sat, val)
            pixels[y, x, :3] = (round(nr * 255), round(ng * 255), round(nb * 255))
            if a >= 240:
                pixels[y, x, 3] = 255
    sprite = Image.new("RGBA", (SIZE, SIZE))
    top = ship.center_y - ship.height // 2
    sprite.alpha_composite(Image.fromarray(pixels), ((SIZE - ship.width) // 2, top))
    # Keep the original dark outer pixel; tint the two interior pixels on
    # upper and left-facing silhouette edges so the ship lifts off dark water.
    arr = np.asarray(sprite).copy()
    solid = arr[:, :, 3] >= 180
    rim = np.zeros_like(solid)
    for y in range(19, 154):
        xs = np.flatnonzero(solid[y])
        if len(xs):
            left = int(xs[0])
            for x in (left + 1, left + 2):
                if x < SIZE and solid[y, x]:
                    rim[y, x] = True
    for x in range(30, 163):
        ys = np.flatnonzero(solid[:, x])
        if len(ys):
            top_y = int(ys[0])
            for y in (top_y + 1, top_y + 2):
                if y < SIZE and solid[y, x]:
                    rim[y, x] = True
    # Sparse rim glints preserve the 4-6-tone painted surface rather than
    # turning every contour into a flat white sticker line.
    yy, xx = np.indices(solid.shape)
    rim &= ((xx * 3 + yy * 5) % 7 != 0)
    lit = arr[:, :, :3].astype(np.float32)
    lit[rim] = lit[rim] * .48 + np.array((174, 230, 203)) * .52
    arr[:, :, :3] = np.clip(lit, 0, 255).astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


def warp_parts(im: Image.Image, ship: Ship, state: int) -> Image.Image:
    """A tiny local nearest-neighbour bend keeps the central chassis anchored."""
    if state not in (0, 2, 3, 4, 5):
        return im
    arr = np.asarray(im)
    yy, xx = np.indices((SIZE, SIZE))
    side = np.sign(xx - 96)
    outer = np.clip((np.abs(xx - 96) - 17) / 43, 0, 1)
    if ship.name == "riptide":
        region = (yy > 65) & (yy < 137)
    elif ship.name == "bathyscaphe":
        region = (yy > 76) & (yy < 148)
    elif ship.name == "trawler":
        region = (yy > 56) & (yy < 135)
    elif ship.name == "thunderfin":
        region = (yy > 56) & (yy < 123)
    else:
        region = (yy > 45) & (yy < 135)
    amount = {0: -3.0, 2: 1.0, 3: 1.5, 4: 8.0, 5: 8.0}.get(state, 0)
    if state == 4:
        dy = -amount * outer * (side < 0) + amount * outer * (side > 0) * 0.45
        dx = -1.4 * outer
    elif state == 5:
        dy = amount * outer * (side < 0) * 0.45 - amount * outer * (side > 0)
        dx = 1.4 * outer
    else:
        dy = amount * outer * (1 if state != 0 else -1)
        dx = amount * outer * side * 0.45
    sy = np.clip(np.rint(yy - dy * region).astype(int), 0, SIZE - 1)
    sx = np.clip(np.rint(xx - dx * region).astype(int), 0, SIZE - 1)
    warped = arr[sy, sx].copy()
    # Banks also lean in travel direction, with at most two pixels at the bow.
    if state in (4, 5):
        sign = -1 if state == 4 else 1
        shift = np.rint(sign * 2 * (96 - yy) / 96).astype(int)
        sx = np.clip(xx - shift, 0, SIZE - 1)
        warped = warped[yy, sx]
    return Image.fromarray(warped, "RGBA")


def dim_mint(im: Image.Image, factor: float) -> Image.Image:
    if factor == 1:
        return im
    arr = np.asarray(im).copy()
    for y, x in zip(*np.where(arr[:, :, 3] > 0)):
        r, g, b = map(int, arr[y, x, :3])
        hue, sat, val = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        if 125 <= hue * 360 <= 190 and sat > 0.15 and val > 0.35:
            val = min(1, val * factor)
            rr, gg, bb = colorsys.hsv_to_rgb(hue, sat, val)
            arr[y, x, :3] = (round(rr * 255), round(gg * 255), round(bb * 255))
    return Image.fromarray(arr, "RGBA")


def dot(draw: ImageDraw.ImageDraw, p: tuple[int, int], strength: int) -> None:
    x, y = p
    if strength <= 0:
        return
    draw.ellipse((x - 4, y - 4, x + 4, y + 4), fill=(*DEEP, 85 if strength == 1 else 120))
    draw.ellipse((x - 2, y - 2, x + 2, y + 2), fill=(*MINT, 200))
    draw.point((x, y), fill=(243, 255, 239, 255))


def draw_exhaust(frame: Image.Image, ship: Ship, state: int) -> None:
    if state < 2:
        return
    layer = Image.new("RGBA", frame.size)
    d = ImageDraw.Draw(layer)
    length = 11 if state == 2 else 6
    for i, (x, y) in enumerate(ship.nozzles):
        flick = (state + i) % 2
        tip = min(175, y + length + flick)
        d.polygon(((x - 4, y - 2), (x + 4, y - 2), (x + 3, y + 2), (x, tip), (x - 3, y + 2)), fill=(33, 125, 111, 105))
        d.polygon(((x - 2, y), (x + 2, y), (x, max(y + 1, tip - 3))), fill=(124, 242, 192, 230))
        d.point((x, y + 1), fill=(245, 255, 243, 255))
    frame.alpha_composite(layer)


def details(frame: Image.Image, ship: Ship, state: int) -> Image.Image:
    frame = frame.copy()
    if state in (0, 1):
        frame = dim_mint(frame, 0.42 if state == 0 else 0.7)
    elif state == 2:
        frame = dim_mint(frame, 1.15)
    elif state == 6:
        frame = dim_mint(frame, 0.65)
    frame = warp_parts(frame, ship, state)
    draw_exhaust(frame, ship, state)
    marks = Image.new("RGBA", frame.size)
    d = ImageDraw.Draw(marks)
    if state > 0 and state != 6:
        dot(d, ship.core, 1 if state == 1 else 2)
    if state in (3, 4, 5):
        if ship.name == "thunderfin":
            flip = -1 if state == 4 else 1
            d.line(((67, 75), (72, 70 + flip), (76, 74), (81, 67)), fill=(*MINT, 230), width=1)
            d.line(((125, 75), (120, 70 - flip), (116, 74), (111, 67)), fill=(*VIOLET, 230), width=1)
        elif ship.name == "trawler":
            y = 115 + (-2 if state == 4 else 2 if state == 5 else 0)
            d.arc((59, y - 5, 133, y + 8), 4, 176, fill=(109, 174, 139, 235), width=1)
        elif ship.name == "pearl_warden":
            d.arc((82, 64, 110, 93), 203, 337, fill=(*BONE, 240), width=1)
        elif ship.name == "riptide":
            d.line(((96, 28), (96, 39)), fill=(*BONE, 210), width=1)
    if state == 6:
        cx, cy = ship.core
        # A ruptured dark fissure plus two pale cracked edges reads as damage
        # even at the small in-game size.
        jag = [(cx - 12, cy - 9), (cx - 6, cy - 3), (cx - 10, cy + 4), (cx + 1, cy + 8), (cx + 5, cy + 17)]
        d.line(jag, fill=(3, 16, 17, 255), width=5)
        d.line([(x - 2, y - 1) for x, y in jag], fill=(*BONE, 255), width=1)
        d.line(((cx + 5, cy + 17), (cx + 12, cy + 10), (cx + 15, cy + 3)), fill=(*MINT, 255), width=1)
        for ox, oy in ((-16, -15), (19, 14), (6, 22), (-22, 9)):
            d.point((cx + ox, cy + oy), fill=(*MINT, 255))
            d.point((cx + ox + 1, cy + oy - 1), fill=(*BONE, 255))
    frame.alpha_composite(marks)
    return frame


def death_frames(ship: Ship, damaged: Image.Image) -> list[Image.Image]:
    out = []
    # Flash: same silhouette, burst from its painted core.
    flash = damaged.copy()
    a = np.asarray(flash).copy()
    solid = a[:, :, 3] > 100
    light = a[:, :, :3].astype(np.float32)
    tint = np.array((194, 246, 220), np.float32)
    light[solid] = light[solid] * .52 + tint * .48
    a[:, :, :3] = np.clip(light, 0, 255).astype(np.uint8)
    flash = Image.fromarray(a, "RGBA")
    d = ImageDraw.Draw(flash)
    cx, cy = ship.core
    d.ellipse((cx - 14, cy - 14, cx + 14, cy + 14), fill=(*MINT, 145))
    d.ellipse((cx - 5, cy - 5, cx + 5, cy + 5), fill=(245, 255, 238, 255))
    for angle in range(0, 360, 45):
        rad = math.radians(angle)
        d.line(((cx + int(7 * math.cos(rad)), cy + int(7 * math.sin(rad))),
                (cx + int(23 * math.cos(rad)), cy + int(23 * math.sin(rad)))), fill=(*MINT, 240), width=2)
    out.append(flash)
    # Rupture: actual painted hull halves pull apart, then a hard mint core.
    rupture = Image.new("RGBA", (SIZE, SIZE))
    left = damaged.crop((0, 0, 96, SIZE))
    right = damaged.crop((96, 0, SIZE, SIZE))
    rupture.alpha_composite(left, (-7, -3))
    rupture.alpha_composite(right, (103, 3))
    d = ImageDraw.Draw(rupture)
    d.polygon(((cx, cy - 22), (cx + 15, cy), (cx, cy + 24), (cx - 15, cy)), fill=(*DEEP, 235))
    d.ellipse((cx - 9, cy - 9, cx + 9, cy + 9), fill=(*MINT, 255))
    d.ellipse((cx - 3, cy - 3, cx + 3, cy + 3), fill=(245, 255, 237, 255))
    for angle in range(15, 360, 45):
        rad = math.radians(angle)
        r1, r2 = 18, 39
        d.line(((cx + int(r1 * math.cos(rad)), cy + int(r1 * math.sin(rad))),
                (cx + int(r2 * math.cos(rad)), cy + int(r2 * math.sin(rad)))), fill=(*BONE, 225), width=1)
    out.append(rupture)
    # Debris: fragments sampled from the painted hull, stepped vapor rings.
    debris = Image.new("RGBA", (SIZE, SIZE))
    d = ImageDraw.Draw(debris)
    for radius, alpha in ((23, 120), (31, 80), (40, 42)):
        d.arc((cx - radius, cy - radius, cx + radius, cy + radius), 24, 158, fill=(*MINT, alpha), width=2)
        d.arc((cx - radius, cy - radius, cx + radius, cy + radius), 204, 334, fill=(*MINT, alpha), width=2)
    sample = np.asarray(damaged)
    seed = sum(ord(c) for c in ship.name)
    rng = np.random.default_rng(seed)
    opaque = np.argwhere(sample[:, :, 3] > 230)
    for _ in range(26):
        sy, sx = opaque[rng.integers(len(opaque))]
        col = tuple(int(v) for v in sample[sy, sx, :3]) + (240,)
        angle = rng.random() * math.tau
        radius = rng.uniform(16, 63)
        x = int(cx + radius * math.cos(angle))
        y = int(cy + radius * math.sin(angle))
        if 17 <= x < 175 and 17 <= y < 175:
            d.polygon(((x, y), (x + 3, y + 1), (x + 1, y + 4)), fill=col)
    d.ellipse((cx - 3, cy - 3, cx + 3, cy + 3), fill=(*MINT, 190))
    out.append(debris)
    return out


def build() -> None:
    previews = []
    metrics = []
    for ship in SHIPS:
        key = f"tide_elite_{ship.name}"
        base = paint_master(ship)
        frames = [details(base, ship, i) for i in range(7)]
        strip = Image.new("RGBA", (SIZE * 7, SIZE))
        for index, frame in enumerate(frames):
            strip.alpha_composite(frame, (index * SIZE, 0))
        strip.save(ROOT / f"{key}.png", optimize=True)
        shutil.copyfile(SRC / f"{ship.name}_master.png", ROOT / f"{ship.name}_concept.png")
        death = Image.new("RGBA", (SIZE * 3, SIZE))
        for index, frame in enumerate(death_frames(ship, frames[6])):
            death.alpha_composite(frame, (index * SIZE, 0))
        death.save(ROOT / f"{key}_death.png", optimize=True)
        previews.append((ship, frames, strip))
        metrics.append((ship, frames))

    # Every row is one unscaled strip magnified exactly 2x with nearest.
    label_w = 192
    sheet = Image.new("RGB", (label_w + 7 * SIZE * 2, len(previews + [None]) * SIZE * 2), (6, 22, 27))
    d = ImageDraw.Draw(sheet)
    for row, (ship, _, strip) in enumerate(previews):
        sheet.paste(strip.resize((strip.width * 2, strip.height * 2), Image.Resampling.NEAREST), (label_w, row * SIZE * 2), strip.resize((strip.width * 2, strip.height * 2), Image.Resampling.NEAREST))
        d.text((12, row * SIZE * 2 + 20), ship.name.upper(), fill=(211, 242, 223))
    reference = Image.open(Path(__file__).parents[4] / "Resources/Elites/Space/space_elite_orbit_reaver.png").convert("RGBA")
    if reference.width >= SIZE * 7:
        reference = reference.crop((0, 0, SIZE * 7, SIZE))
        ref2 = reference.resize((SIZE * 14, SIZE * 2), Image.Resampling.NEAREST)
        sheet.paste(ref2, (label_w, 5 * SIZE * 2), ref2)
        d.text((12, 5 * SIZE * 2 + 20), "SPACE REFERENCE", fill=(211, 242, 223))
    sheet.save(ROOT / "contact_sheet.png", optimize=True)

    gif_frames = []
    for index in (3, 4, 3, 5):
        canvas = Image.new("RGB", (SIZE * 2, SIZE * 2 * len(previews)), (6, 22, 27))
        for row, (_, frames, _) in enumerate(previews):
            fr = frames[index].resize((SIZE * 2, SIZE * 2), Image.Resampling.NEAREST)
            canvas.paste(fr, (0, row * SIZE * 2), fr)
        gif_frames.append(canvas)
    gif_frames[0].save(ROOT / "preview.gif", save_all=True, append_images=gif_frames[1:], duration=[150] * 4, loop=0, disposal=2)

    lines = ["# Tide elite flight art", "", "Painted source masters: `src~/*_master.png` (built-in image generation). `src~/build_tide_elites.py` creates all strips by nearest-neighbour sampling and stepped pixel effects.", "", "| Cell | Pose |", "| --- | --- |", "| 0 | Landed, engines off |", "| 1 | Grounded idle, restrained mint glow |", "| 2 | Lift-off ignition |", "| 3 | Straight hover |", "| 4 | Bank left |", "| 5 | Bank right |", "| 6 | Second-heart damage, hull crack and sparks |", "", "Each optional `<key>_death.png` has three 192 px cells: flash, rupture, debris.", "", "## Registration and hardpoints", "", "Coordinates below are approximate hover-cell pixels from the left/top. Muzzles name the intended origin; hostile shot and heart colours are supplied in game.", "", "| Ship | Ground site | Signature | Nozzles (x,y) | Muzzles (x,y) | Moving parts |", "| --- | --- | --- | --- | --- | --- |"]
    for ship, frames in metrics:
        nozzle = ", ".join(f"({x},{y})" for x, y in ship.nozzles)
        muzzle = ", ".join(f"{label} ({x},{y})" for label, x, y in ship.muzzles)
        lines.append(f"| {ship.name.replace('_', ' ').title()} | {ship.site} | {ship.attack} | {nozzle} | {muzzle} | {ship.moving} |")
    lines += ["", "## Cell measurements", "", "Alpha bbox at threshold 24 and width × height. Body registration is measured on cells 3, 4 and 5.", "", "| Ship | Hover bbox (x0,y0,x1,y1) | Hover size | Bank left size | Bank right size | Hover→left centroid Δ | Hover→right centroid Δ | Min margin, cells 0–6 | Opaque colours, hover | Red-band share, hover |", "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for ship, frames in metrics:
        stats = []
        for frame in frames:
            arr = np.asarray(frame)
            yy, xx = np.where(arr[:, :, 3] >= 24)
            bbox = (int(xx.min()), int(yy.min()), int(xx.max() + 1), int(yy.max() + 1))
            solid = arr[:, :, 3] >= 128
            sy, sx = np.where(solid)
            centroid = (float(sx.mean()), float(sy.mean()))
            stats.append((bbox, centroid))
        box = stats[3][0]
        sizes = [f"{stats[i][0][2]-stats[i][0][0]}×{stats[i][0][3]-stats[i][0][1]}" for i in (3, 4, 5)]
        drift = [math.dist(stats[3][1], stats[i][1]) for i in (4, 5)]
        margin = min(min(b[0], b[1], SIZE - b[2], SIZE - b[3]) for b, _ in stats)
        arr = np.asarray(frames[3])
        solid = arr[:, :, 3] >= 128
        colors = len(np.unique(arr[solid, :3].reshape(-1, 3), axis=0))
        red = 0
        for r, g, b in arr[solid, :3]:
            h, s, _ = colorsys.rgb_to_hsv(int(r)/255, int(g)/255, int(b)/255)
            if s > .45 and (h * 360 >= 345 or h * 360 < 15):
                red += 1
        share = red / int(solid.sum())
        lines.append(f"| {ship.name} | {box} | {sizes[0]} | {sizes[1]} | {sizes[2]} | {drift[0]:.2f} px | {drift[1]:.2f} px | {margin} px | {colors} | {share:.3%} |")
    lines += ["", "The generated concepts were scaled to flight cells with nearest-neighbour sampling. Tiny procedural cracks, exhaust, fin bends and debris make the flight and death keys; the painted masters carry the material detail. Weapon telegraphs, nets, pearls, and hostile-pink shots are gameplay FX rather than baked into the hull.", ""]
    (ROOT / "manifest_new_elites.md").write_text("\n".join(lines))


if __name__ == "__main__":
    build()
