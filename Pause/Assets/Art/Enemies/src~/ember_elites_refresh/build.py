"""Build the six staged Ember elite strips from image-generated candidate paintings.

Every resize and local part shift uses nearest-neighbour pixels. Run from any cwd.
"""
from __future__ import annotations

from dataclasses import dataclass
from collections import deque
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
SRC = ROOT / "src~"
CELL = 192
BG = (11, 11, 26, 255)
WHITE = (255, 241, 208)
ORANGE = (247, 104, 24)
DEEP = (109, 42, 15)
RIM = np.array((193, 181, 161), dtype=np.float32)


@dataclass(frozen=True)
class Ship:
    name: str
    selected: int  # zero-based panel on the three-concept source sheet
    width: int
    height: int
    core: tuple[int, int]
    muzzles: tuple[tuple[str, int, int], ...]
    nozzles: tuple[tuple[str, int, int], ...]
    motion: str
    role: str


SHIPS = (
    Ship("sunstoke", 1, 106, 150, (96, 83), (("heat-lance tip", 96, 24),), (("port boiler", 86, 160), ("starboard boiler", 106, 160)), "split wings flare and boiler vents cycle", "spear interceptor / lance dash"),
    Ship("coalrunner", 1, 132, 120, (96, 76), (("port broadside", 30, 93), ("starboard broadside", 162, 93)), (("port", 69, 150), ("center", 96, 152), ("starboard", 123, 150)), "gun pods recoil and stern plates breathe", "broadside gunship / side volley"),
    Ship("brass_vulture", 1, 126, 148, (96, 93), (("port recovery claw", 89, 25), ("starboard recovery claw", 103, 25)), (("tail", 96, 165),), "hook wings and recovery claw flex", "salvage striker / claw dive"),
    Ship("kilnback", 0, 120, 150, (96, 79), (("port slag hatch", 76, 139), ("starboard slag hatch", 116, 139)), (("port", 79, 165), ("starboard", 113, 165)), "slab shields spread and hatches shift", "armored hauler / slag drop"),
    Ship("ash_wraith", 1, 112, 150, (96, 77), (("beak shard gun", 96, 24),), (("port split tail", 83, 164), ("starboard split tail", 109, 164)), "crescent fins sweep and split tails flex", "shadow skirmisher / blink shard fan"),
    Ship("cauterizer", 0, 132, 150, (96, 91), (("pressure cannon", 96, 24),), (("port", 66, 163), ("starboard", 126, 163)), "twin heat shields spread and six vents pulse", "siege catamaran / pressure cannon"),
)


def hue_clean(im: Image.Image) -> Image.Image:
    """Move chromatic outliers to Ember orange without collapsing local tone."""
    a = np.asarray(im.convert("RGBA")).copy()
    hsv = np.asarray(im.convert("RGB").convert("HSV")).copy()
    hue = hsv[:, :, 0].astype(np.float32) * (360 / 255)
    sat = hsv[:, :, 1].astype(np.float32) / 255
    val = hsv[:, :, 2].astype(np.float32) / 255
    colored = sat > .095
    # Neutral gunmetal stays neutral. Warm surfaces retain their painted tone;
    # errant red/pink/yellow/cyan/green are bent to the material's copper hue.
    new_h = np.where((hue >= 18) & (hue <= 33), hue, 27)
    hsv[:, :, 0] = np.where(colored, np.rint(new_h * 255 / 360), hsv[:, :, 0]).astype(np.uint8)
    # Matte materials may carry restrained copper; high-value pixels are kept
    # white/white-gold unless they are small emissive source pixels.
    max_sat = np.where(val > .72, .36, np.where(val > .60, .48, .60))
    hsv[:, :, 1] = np.rint(np.minimum(sat, max_sat) * 255).astype(np.uint8)
    rgb = np.asarray(Image.fromarray(hsv, "HSV").convert("RGB"))
    a[:, :, :3] = rgb
    alpha = a[:, :, 3]
    a[alpha < 32] = (0, 0, 0, 0)
    a[(alpha >= 32) & (alpha < 96), 3] = 64
    a[(alpha >= 96) & (alpha < 160), 3] = 128
    a[(alpha >= 160) & (alpha < 235), 3] = 192
    a[alpha >= 235, 3] = 255
    return Image.fromarray(a, "RGBA")


def source(ship: Ship) -> Image.Image:
    sheet = Image.open(SRC / f"{ship.name}_candidates.png").convert("RGBA")
    slot = sheet.width / 3
    x0 = round(ship.selected * slot)
    x1 = round((ship.selected + 1) * slot)
    panel = sheet.crop((x0, 0, x1, sheet.height))
    alpha = np.asarray(panel.getchannel("A"))
    # Generated panels can bleed the next concept's wing across a slot edge.
    # Retain only the central ship's connected opaque painting.
    opaque = alpha >= 180
    seen = np.zeros_like(opaque, dtype=bool)
    best = []
    h, w = opaque.shape
    for sy, sx in zip(*np.where(opaque & ~seen)):
        if seen[sy, sx]:
            continue
        queue = deque([(int(sy), int(sx))]); seen[sy, sx] = True
        component = []
        while queue:
            y, x = queue.popleft(); component.append((y, x))
            for dy, dx in ((-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)):
                ny, nx = y + dy, x + dx
                if 0 <= ny < h and 0 <= nx < w and opaque[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True; queue.append((ny, nx))
        if len(component) > len(best):
            best = component
    keep = np.zeros_like(opaque)
    for y, x in best:
        keep[y, x] = True
    # Keep translucent anti-alias pixels immediately around the selected hull.
    halo = keep.copy()
    for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        halo |= np.roll(np.roll(keep, dy, 0), dx, 1)
    cleaned = np.asarray(panel).copy()
    cleaned[~halo] = (0, 0, 0, 0)
    panel = Image.fromarray(cleaned, "RGBA")
    alpha = np.asarray(panel.getchannel("A"))
    ys, xs = np.where(alpha >= 180)
    assert len(xs), ship.name
    # Exclude transparent padding, but retain all solid painted extensions.
    panel = panel.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))
    return hue_clean(panel)


def base(ship: Ship, hero: Image.Image) -> Image.Image:
    sprite = hero.resize((ship.width, ship.height), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (CELL, CELL))
    canvas.alpha_composite(sprite, ((CELL - ship.width) // 2, (CELL - ship.height) // 2))
    a = np.asarray(canvas).copy()
    solid = a[:, :, 3] >= 128
    up = np.zeros_like(solid); up[1:] = solid[:-1]
    left = np.zeros_like(solid); left[:, 1:] = solid[:, :-1]
    edge = solid & (~up | ~left)
    interior = solid & (np.roll(edge, 1, 0) | np.roll(edge, 1, 1))
    rgb = a[:, :, :3].astype(np.float32)
    rgb[edge] *= .49
    rgb[interior] = rgb[interior] * .67 + RIM * .33
    rgb[edge] = rgb[edge] * .38 + RIM * .62
    a[:, :, :3] = np.uint8(np.clip(rgb, 0, 255))
    return Image.fromarray(a, "RGBA")


def move_parts(im: Image.Image, ship: Ship, state: int) -> Image.Image:
    a = np.asarray(im)
    yy, xx = np.indices((CELL, CELL))
    if ship.name == "coalrunner":
        part = (abs(xx - 96) > 38) & (yy > 61) & (yy < 131)
    elif ship.name == "kilnback":
        part = (abs(xx - 96) > 30) & (yy > 38) & (yy < 133)
    elif ship.name == "cauterizer":
        part = (abs(xx - 96) > 24) & (yy > 43) & (yy < 149)
    elif ship.name == "brass_vulture":
        part = ((abs(xx - 96) > 24) & (yy > 58) & (yy < 145)) | ((abs(xx - 96) > 7) & (yy < 45))
    else:
        part = (abs(xx - 96) > 20) & (yy > 51) & (yy < 141)
    phase = ((-1, 0), (0, 1), (0, -1), (-1, 1))[state]
    spread = np.sign(xx - 96) * phase[0]
    sx = np.clip(xx - np.where(part, spread, 0), 0, CELL - 1)
    sy = np.clip(yy - np.where(part, phase[1], 0), 0, CELL - 1)
    return Image.fromarray(a[sy, sx], "RGBA")


def add_marks(frame: Image.Image, ship: Ship, state: int) -> Image.Image:
    marks = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(marks)
    cx, cy = ship.core
    if state < 4:
        # A hard, small cockpit lens and moving furnace slits.
        d.rectangle((cx - 1, cy - 1, cx + 1, cy + 1), fill=(*WHITE, 255))
        d.point((cx - 2, cy), fill=(*ORANGE, 255))
        for i, (_, x, y) in enumerate(ship.nozzles):
            length = (4, 6, 5, 7)[(state + i) % 4]
            d.rectangle((x - 2, y, x + 2, y + 2), fill=(*DEEP, 255))
            d.polygon(((x - 2, y + 1), (x + 2, y + 1), (x, y + length)), fill=(*ORANGE, 255))
            d.point((x, y + 1), fill=(*WHITE, 255))
        if ship.name == "cauterizer":
            for x in (64, 126):
                for k, y in enumerate((65, 84, 103)):
                    d.point((x, y + (state + k) % 2), fill=(*ORANGE, 255))
    elif state in (4, 5):
        d.rectangle((cx - 2, cy - 2, cx + 2, cy + 2), fill=(*WHITE, 255))
        for i, (_, x, y) in enumerate(ship.muzzles):
            d.ellipse((x - 3, y - 3, x + 3, y + 3), fill=(*DEEP, 255))
            d.ellipse((x - 1, y - 1, x + 1, y + 1), fill=(*WHITE, 255))
            if state == 5:
                if ship.name == "coalrunner":
                    sign = -1 if x < 96 else 1
                    d.polygon(((x + sign * 2, y - 3), (x + sign * 9, y), (x + sign * 2, y + 3)), fill=(*ORANGE, 255))
                    d.point((x + sign * 5, y), fill=(*WHITE, 255))
                elif ship.name == "kilnback":
                    d.polygon(((x - 3, y), (x + 3, y), (x, y + 9)), fill=(*ORANGE, 255))
                    d.point((x, y + 2), fill=(*WHITE, 255))
                else:
                    d.polygon(((x - 3, y + 2), (x, y - 7), (x + 3, y + 2)), fill=(*ORANGE, 255))
                    d.point((x, y - 3), fill=(*WHITE, 255))
        for _, x, y in ship.nozzles:
            d.rectangle((x - 2, y, x + 2, y + 3), fill=(*ORANGE, 255))
    else:
        # Damage is confined to the body; the silhouette and anchor survive.
        crack = [(cx - 12, cy - 14), (cx - 5, cy - 7), (cx - 9, cy + 1), (cx + 3, cy + 7), (cx + 8, cy + 16)]
        d.line(crack, fill=(11, 9, 9, 255), width=3)
        d.line([(x - 1, y - 1) for x, y in crack], fill=(170, 147, 119, 255), width=1)
        for dx, dy in ((-17, -10), (14, 11), (-20, 13), (9, 22)):
            d.point((cx + dx, cy + dy), fill=(*ORANGE, 255))
            d.point((cx + dx + 1, cy + dy - 1), fill=(*WHITE, 255))
        for _, x, y in ship.nozzles:
            d.point((x, y + 1), fill=(*ORANGE, 255))
    out = frame.copy()
    out.alpha_composite(marks)
    return out


def measure_bbox(frame: Image.Image) -> tuple[int, int, int, int]:
    a = np.asarray(frame.getchannel("A"))
    yy, xx = np.where(a >= 24)
    return int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1


def make_preview(strips: list[Image.Image]) -> None:
    dark = Image.new("RGBA", (1344, 192 * len(strips)), BG)
    for j, strip in enumerate(strips):
        dark.alpha_composite(strip, (0, j * 192))
    dark.resize((2688, dark.height * 2), Image.Resampling.NEAREST).convert("RGB").save(ROOT / "contact_sheet.png")

    # Flight sprites over four actual Ember mid tiles, with no mock interface.
    tiles = [Image.open(ROOT.parents[2] / "Backgrounds" / "Resources" / "Worlds" / "Ember" / "Backdrop3" / f"v{i}" / "mid.png").convert("RGBA") for i in range(1, 5)]
    preview = Image.new("RGBA", (384 * 3, 384 * 2), BG)
    for j, strip in enumerate(strips):
        tile = tiles[j % 4].resize((384, 384), Image.Resampling.NEAREST)
        x, y = (j % 3) * 384, (j // 3) * 384
        preview.alpha_composite(tile, (x, y))
        frame = strip.crop((3 * CELL, 0, 4 * CELL, CELL)).resize((384, 384), Image.Resampling.NEAREST)
        preview.alpha_composite(frame, (x, y))
    preview.convert("RGB").save(ROOT / "preview.png")


def main() -> None:
    strips = []
    rows = ["# Ember elite refresh", "", "All coordinates are measured in 192×192 cell pixels (x from left, y from top). Nose points up (90° in game art coordinates). The listed points are authored features confirmed on the selected image-generated hull and registered across the seven cells.", "", "Cells: 0–3 idle flight; 4 tell; 5 action; 6 hit/damaged. Source paintings: three concepts per ship in `src~/`; selected panel index below is zero-based. Built-in image generation supplied all painted hull content; this script crops and scales with nearest-neighbour sampling, cleans forbidden hues, adds the pale rim and small state details.", ""]
    for ship in SHIPS:
        hero = source(ship)
        hero.save(ROOT / f"{ship.name}_concept.png")
        hull = base(ship, hero)
        cells = [add_marks(move_parts(hull, ship, k), ship, k) for k in range(4)]
        cells += [add_marks(hull, ship, k) for k in (4, 5, 6)]
        strip = Image.new("RGBA", (CELL * 7, CELL))
        for k, frame in enumerate(cells):
            strip.alpha_composite(frame, (k * CELL, 0))
        strip.save(ROOT / f"ember_elite_{ship.name}.png")
        strips.append(strip)
        box = measure_bbox(cells[3])
        rows += [f"## {ship.name.replace('_', ' ').title()}", "", f"- Role / attack: {ship.role}", f"- Concept selected: panel {ship.selected + 1} of 3; idle motion: {ship.motion}", f"- Cell 3 alpha bbox: {box} (x0, y0, x1 exclusive, y1 exclusive)", "- Nose: up (toward decreasing y; 90°)", "- Weapon muzzles: " + ", ".join(f"{label} ({x}, {y})" for label, x, y in ship.muzzles), "- Engine nozzles: " + ", ".join(f"{label} ({x}, {y})" for label, x, y in ship.nozzles), ""]
    (ROOT / "manifest.md").write_text("\n".join(rows))
    make_preview(strips)


if __name__ == "__main__":
    main()
