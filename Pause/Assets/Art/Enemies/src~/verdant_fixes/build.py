"""Assemble painted candidates into the Verdant fixes and three-pose death strips.

Candidates in candidates/ are generated source art. The only resize filter used
here is nearest neighbour. Re-running uses original/ for live-strip inputs.
"""
from __future__ import annotations

import math
import shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2] / "Resources" / "Enemies"
DEATH = ART / "Death"
ORIGINAL = HERE / "original"
CANDIDATES = HERE / "candidates"
VERDANT = [
    "verdant_alien", "verdant_big", "verdant_chaser", "verdant_fighter_1",
    "verdant_fighter_2", "verdant_fighter_3", "verdant_fighter_4",
    "verdant_rock_knot", "verdant_rock_pod", "verdant_rock_spore",
    "verdant_rock_vine",
]
DEATH_KEYS = VERDANT + ["verdant_mine", "ember_mine", "frost_mine", "space_mine", "space_big"]
PALETTES = {
    "verdant": ["05060c", "0c0703", "281b0a", "3e2610", "5a3a17", "7d5426",
                "a07a3c", "0e1c03", "1f3f04", "306203", "4dae03", "a6d32a",
                "3d8f02", "70d804", "b8f018", "e6fc5e", "fbffd8",
                "483014", "80531d", "ba7d26", "edb74b", "ffe1a1"],
    "ember": ["05060c", "0e0808", "1d1212", "2e1d1a", "47302b", "6a4a40",
              "957060", "342014", "5b3921", "8c5929", "b87835", "e0a252",
              "6a2404", "c24a06", "f77a0a", "fcb809", "fcee09", "fdfa92", "fffbe0"],
    "frost": ["05060c", "0a1734", "1c3c68", "2a62ae", "4a96e6", "9fd2f2",
              "e2fbff", "0c1020", "1e2638", "33405c", "4f6188", "7d93bd",
              "1569c8", "1898f7", "46ddfd", "aaf2fb", "f8fefc"],
    "space": ["05060c", "0b0f15", "1a2129", "2f343b", "4b5059", "7a7c82",
              "b0b4b6", "0a1118", "162e3c", "2f4550", "476d7b",
              "0b4f7a", "0b91cc", "0bd0f6", "7af6fc", "f5fdfd",
              "3a0a2a", "8c1462", "e0309e", "ff9ad6"],
}
PALS = {k: np.array([tuple(bytes.fromhex(c)) for c in v], dtype=np.uint8)
        for k, v in PALETTES.items()}
FLASH = {"verdant": (251, 255, 216), "ember": (255, 251, 224),
         "frost": (248, 254, 252), "space": (245, 253, 253)}
NEON = {"verdant": (184, 240, 24), "ember": (252, 184, 9),
        "frost": (70, 221, 253), "space": (11, 208, 246)}
RIM = (230, 252, 94)


def rgba(path: Path) -> Image.Image:
    return Image.open(path).convert("RGBA")


def cells(im: Image.Image, count: int) -> list[Image.Image]:
    s = im.height
    assert im.width == count * s, (im.size, count)
    return [im.crop((i * s, 0, (i + 1) * s, s)) for i in range(count)]


def strip(parts: list[Image.Image]) -> Image.Image:
    s = parts[0].height
    out = Image.new("RGBA", (s * len(parts), s))
    for i, part in enumerate(parts):
        out.alpha_composite(part, (i * s, 0))
    return out


def strong_box(im: Image.Image, threshold: int = 160) -> tuple[int, int, int, int]:
    a = np.asarray(im.getchannel("A"))
    ys, xs = np.where(a >= threshold)
    if not len(xs):
        return (0, 0, im.width, im.height)
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def center_box(im: Image.Image, threshold: int = 160) -> tuple[float, float]:
    x0, y0, x1, y1 = strong_box(im, threshold)
    return (x0 + x1) / 2, (y0 + y1) / 2


def mass_center(im: Image.Image, threshold: int = 176) -> tuple[float, float]:
    ys, xs = np.where(np.asarray(im.getchannel("A")) >= threshold)
    return float(xs.mean()), float(ys.mean())


def place_crop(source: Image.Image, side: int, target_w: int, target_h: int,
               center: tuple[float, float], *, threshold: int = 100) -> Image.Image:
    box = strong_box(source, threshold)
    crop = source.crop(box)
    scale = min(target_w / crop.width, target_h / crop.height,
                (side - 16) / crop.width, (side - 16) / crop.height)
    w, h = max(1, round(crop.width * scale)), max(1, round(crop.height * scale))
    crop = crop.resize((w, h), Image.Resampling.NEAREST)
    x = max(8, min(side - 8 - w, round(center[0] - w / 2)))
    y = max(8, min(side - 8 - h, round(center[1] - h / 2)))
    out = Image.new("RGBA", (side, side))
    out.alpha_composite(crop, (x, y))
    return out


def shift(im: Image.Image, dx: int, dy: int) -> Image.Image:
    out = Image.new("RGBA", im.size)
    out.alpha_composite(im, (dx, dy))
    return out


def align(im: Image.Image, reference: Image.Image) -> Image.Image:
    a, b = mass_center(im), mass_center(reference)
    return shift(im, round(b[0] - a[0]), round(b[1] - a[1]))


def scale_center(im: Image.Image, factor: float) -> Image.Image:
    box = strong_box(im, 24)
    crop = im.crop(box)
    w, h = round(crop.width * factor), round(crop.height * factor)
    out = Image.new("RGBA", im.size)
    out.alpha_composite(crop.resize((w, h), Image.Resampling.NEAREST),
                        (round((im.width - w) / 2), round((im.height - h) / 2)))
    return out


def quantize(im: Image.Image, world: str) -> Image.Image:
    a = np.array(im, dtype=np.uint8)
    # Generated fringe and haze become a small set of hard alpha steps.
    alpha = a[:, :, 3]
    levels = np.array([0, 64, 112, 176, 255], dtype=np.uint8)
    alpha[:] = levels[np.abs(alpha.astype(np.int16)[:, :, None] - levels).argmin(axis=2)]
    rgb = a[:, :, :3]
    if world == "verdant":
        # Remove legacy hot-pink thorn lights and accidental generated red.
        hsv = np.asarray(Image.fromarray(rgb, "RGB").convert("HSV")).copy()
        h, s, v = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]
        foreign = (((h > 120) | (h < 9)) & (s > 100) & (v > 40))
        h[foreign] = 30  # amber/gold instead of other-world neon
        rgb[:] = np.asarray(Image.fromarray(hsv, "HSV").convert("RGB"))
    palette = PALS[world].astype(np.int32)
    flat = rgb.reshape(-1, 3).astype(np.int32)
    dest = np.empty((len(flat), 3), dtype=np.uint8)
    green_mask = None
    if world == "verdant":
        hsv = np.asarray(Image.fromarray(rgb, "RGB").convert("HSV"))
        green_mask = ((hsv[:, :, 0] >= 35) & (hsv[:, :, 0] <= 115)
                      & (hsv[:, :, 1] >= 55) & (hsv[:, :, 2] >= 38)).reshape(-1)
    for start in range(0, len(flat), 16000):
        chunk = flat[start:start + 16000]
        dist = ((chunk[:, None, :] - palette[None, :, :]) ** 2).sum(axis=2)
        if green_mask is not None:
            gm = green_mask[start:start + len(chunk)]
            # Moss/leaf paint keeps a green ramp even when rusty brown is
            # slightly closer in RGB distance.
            dist[gm, :7] = 10**9
            dist[gm, 17:] = 10**9
        dest[start:start + len(chunk)] = palette[dist.argmin(axis=1)]
    rgb[:] = dest.reshape(rgb.shape)
    a[alpha == 0, :3] = 0
    return Image.fromarray(a, "RGBA")


def rim_and_accents(im: Image.Image, *, core: tuple[int, int] | None = None) -> Image.Image:
    a = np.array(im)
    solid = a[:, :, 3] >= 176
    above = np.zeros_like(solid)
    above[1:, :] = solid[:-1, :]
    left = np.zeros_like(solid)
    left[:, 1:] = solid[:, :-1]
    # A thin upper-left sel-out rim, with small natural gaps at jagged edges.
    edge = solid & (~above | ~left)
    yy, xx = np.indices(solid.shape)
    safe = (xx >= 6) & (yy >= 6) & (xx < im.width - 6) & (yy < im.height - 6)
    edge &= safe & (((xx + 2 * yy) % 7) != 0)
    a[edge, :3] = RIM
    if core is None:
        core = tuple(map(round, center_box(im)))
    x, y = core
    if 7 <= x < im.width - 7 and 7 <= y < im.height - 7:
        for dx, dy, rgb in [(0, 0, (251, 255, 216)), (1, 0, (184, 240, 24)),
                            (0, 1, (184, 240, 24)), (-2, -1, (237, 183, 75))]:
            if solid[y + dy, x + dx]:
                a[y + dy, x + dx] = (*rgb, 255)
    return Image.fromarray(a, "RGBA")


def bright_safe(im: Image.Image, world: str) -> Image.Image:
    a = np.array(im)
    yy, xx = np.indices((im.height, im.width))
    border = (xx < 6) | (yy < 6) | (xx >= im.width - 6) | (yy >= im.height - 6)
    bright = (a[:, :, :3].max(axis=2) >= 170) & (a[:, :, 3] >= 112)
    a[border & bright, :3] = PALS[world][3]
    return Image.fromarray(a, "RGBA")


def painted_panel(key: str, index: int) -> Image.Image:
    im = rgba(CANDIDATES / f"{key}.png")
    side = im.height
    panel = im.crop((index * side + 5, 5, (index + 1) * side - 5, side - 5))
    a = np.array(panel)
    a[a[:, :, 3] < 65, 3] = 0
    # Remove occasional solid white triptych divider line.
    white = (a[:, :, :3].min(axis=2) > 235) & (a[:, :, 3] > 150)
    col = white.sum(axis=0)
    a[:, col > panel.height * .55, 3] = 0
    return Image.fromarray(a, "RGBA")


def knot_frames(side: int) -> list[Image.Image]:
    base = place_crop(painted_panel("verdant_rock_knot", 0), side, 160, 164, (96, 96))
    base = quantize(base, "verdant")
    result = []
    for i in range(7):
        frame = base.copy()
        d = ImageDraw.Draw(frame)
        # Fixed hull and anchor; small rotating glints, leaves and thorns.
        if i < 4:
            t = i * math.pi / 2
            x = 96 + round(49 * math.cos(t))
            y = 96 + round(45 * math.sin(t))
            d.line((x - 3, y, x + 2, y - 2), fill=(230, 252, 94, 255), width=2)
            d.polygon([(x + 4, y + 2), (x + 9, y - 1), (x + 6, y + 5)], fill=(166, 211, 42, 255))
        elif i < 6:
            r = 11 + 5 * (i - 4)
            d.ellipse((96-r, 96-r, 96+r, 96+r), outline=(184, 240, 24, 255), width=2)
        else:
            d.line((74, 90, 104, 107, 121, 96), fill=(251, 255, 216, 255), width=3)
        result.append(bright_safe(rim_and_accents(frame, core=(96, 96)), "verdant"))
    return result


def live_fixes() -> dict[str, Image.Image]:
    ORIGINAL.mkdir(parents=True, exist_ok=True)
    fixed = {}
    for key in VERDANT:
        source_path = ART / f"{key}.png"
        backup = ORIGINAL / f"{key}.png"
        if not backup.exists():
            shutil.copyfile(source_path, backup)
        old = cells(rgba(backup), 7)
        side = old[0].height
        if key == "verdant_rock_knot":
            new = knot_frames(side)
        else:
            new = [x.copy() for x in old]
            if key == "verdant_chaser":
                # Keep the collision body fixed. Small glints move along wings.
                for i in range(4):
                    new[i] = old[0].copy()
                    d = ImageDraw.Draw(new[i])
                    y = 57 + i * 4
                    d.line((68, y, 73, y - 3), fill=(166, 211, 42, 255), width=2)
                    d.line((120, y, 125, y - 3), fill=(166, 211, 42, 255), width=2)
                    d.point((96, 83 + i % 2), fill=(251, 255, 216, 255))
            if key == "verdant_alien":
                # A rustle/blink variation at the register of poses 0-2.
                new[3] = old[1].copy()
                d = ImageDraw.Draw(new[3])
                d.line((135, 98, 141, 94), fill=(166, 211, 42, 255), width=2)
            if key == "verdant_fighter_1":
                for i in range(1, 4):
                    if i == 1:
                        new[i] = scale_center(new[i], .94)
                    new[i] = align(new[i], old[0])
            if key in ("verdant_fighter_2", "verdant_fighter_4"):
                new = [scale_center(x, .92 if key.endswith("_2") else .93) for x in new]
            if key == "verdant_chaser":
                # The requested tell and hit frames stay byte-for-byte.
                new = [bright_safe(rim_and_accents(quantize(x, "verdant")), "verdant")
                       for x in new[:4]] + old[4:]
            else:
                new = [bright_safe(rim_and_accents(quantize(x, "verdant")), "verdant")
                       for x in new]
        output = strip(new)
        output.save(source_path)
        fixed[key] = output
    return fixed


def mine_idle(world: str) -> Image.Image:
    return _mine_idle(world, brighten=True)


def _mine_idle(world: str, *, brighten: bool) -> Image.Image:
    atlas = rgba(ART / "Mines" / "rail_mines_neon.png")
    boxes = {"space": (0, 0, 310, 308), "frost": (0, 308, 328, 286),
             "verdant": (0, 594, 320, 298), "ember": (0, 892, 309, 362)}
    x, y, w, h = boxes[world]
    crop = atlas.crop((x, y, x+w, y+h))
    out = place_crop(crop, 192, 160, 160, (96, 96), threshold=130)
    out = quantize(out, world)
    if world == "verdant" and brighten:
        out = bright_safe(rim_and_accents(out), world)
    return out


def death_flash(idle: Image.Image, world: str) -> Image.Image:
    out = idle.copy()
    # Keep the exact idle silhouette; a small centered star adds overload.
    cx, cy = center_box(idle)
    x, y = round(cx), round(cy)
    d = ImageDraw.Draw(out)
    neon, white = NEON[world] + (255,), FLASH[world] + (255,)
    radius = 27 if idle.height == 256 else 20
    for i in range(12):
        a = i * math.pi / 6
        length = radius if i % 3 else radius + 6
        end = (x + round(length * math.cos(a)), y + round(length * math.sin(a)))
        bend = (x + round(length*.55*math.cos(a) - 2*math.sin(a)),
                y + round(length*.55*math.sin(a) + 2*math.cos(a)))
        d.line(((x, y), bend, end), fill=neon, width=2)
        d.point(end, fill=white)
    d.ellipse((x-5, y-5, x+5, y+5), fill=neon)
    d.ellipse((x-2, y-2, x+2, y+2), fill=white)
    return bright_safe(out, world)


def make_deaths(fixed: dict[str, Image.Image]) -> dict[str, Image.Image]:
    death = {}
    for key in DEATH_KEYS:
        world = key.split("_", 1)[0]
        if key.endswith("_mine"):
            idle = mine_idle(world)
        elif key in fixed:
            idle = cells(fixed[key], 7)[3]
        else:
            idle = cells(rgba(ART / f"{key}.png"), 7)[3]
        side = idle.height
        ib = strong_box(idle, 176)
        iw, ih = ib[2] - ib[0], ib[3] - ib[1]
        center = center_box(idle, 176)
        poses = [death_flash(idle, world)]
        for stage, factor in ((1, 1.14), (2, 1.17)):
            source = painted_panel(key, stage)
            panel = place_crop(source, side, round(iw*factor), round(ih*factor), center)
            panel = quantize(panel, world)
            poses.append(bright_safe(panel, world))
        death[key] = strip(poses)
        death[key].save(DEATH / f"{key}.png")
    return death


def preview(fixed: dict[str, Image.Image], death: dict[str, Image.Image]) -> None:
    width = 4 * 2 * 256 + 32
    row = 2 * 256 + 30
    height = row * (2 * len(fixed) + 2 + len(death))
    canvas = Image.new("RGB", (width, height), (11, 11, 26))
    draw = ImageDraw.Draw(canvas)
    at = 0
    def add(label: str, parts: list[Image.Image]) -> None:
        nonlocal at
        draw.text((8, at + 5), label, fill=(230, 240, 218))
        for j, cell in enumerate(parts):
            up = cell.resize((cell.width*2, cell.height*2), Image.Resampling.NEAREST)
            canvas.paste(up, (j*512 + 8, at + 25), up)
        at += row
    for key in VERDANT:
        add(key + " before idle", cells(rgba(ORIGINAL/f"{key}.png"), 7)[:4])
        add(key + " after idle", cells(fixed[key], 7)[:4])
    add("verdant_mine atlas dormant before", [_mine_idle("verdant", brighten=False)] * 4)
    add("verdant_mine staged rim after", [mine_idle("verdant")] * 4)
    for key in DEATH_KEYS:
        world = key.split("_", 1)[0]
        idle = mine_idle(world) if key.endswith("_mine") else (
            cells(fixed[key], 7)[3] if key in fixed else cells(rgba(ART/f"{key}.png"), 7)[3])
        add(key + " idle 3 | flash | rupture | drift", [idle] + cells(death[key], 3))
    canvas.save(HERE / "preview.png")


def main() -> None:
    fixed = live_fixes()
    # The mine's live idle is a row in a protected, shared four-world atlas.
    # Stage its corrected frame here and use it in the new death strip.
    mine_idle("verdant").save(HERE / "verdant_mine_rim.png")
    deaths = make_deaths(fixed)
    preview(fixed, deaths)


if __name__ == "__main__":
    main()
