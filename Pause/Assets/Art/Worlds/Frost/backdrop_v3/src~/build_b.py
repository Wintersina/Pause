"""Build run B atlases from selected image-generated painted sheets.

Only new run B outputs and the manifest's run_b key are written. All resampling
uses nearest-neighbour; the final colour and alpha ramps have discrete steps.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
CELL = 256
ATLAS = 1024

NAMES = {
    "landmarks": [
        "rig_00", "rig_01", "platform_00", "platform_01",
        "refinery_00", "refinery_01", "relay_00", "relay_01",
        "causeway_00", "icebreaker_00", "icebreaker_01", "cliff_00",
        "cliff_01", "derrick_00", "convoy_00", "platform_02",
    ],
    "weather": [
        "cloud_bank_00", "cloud_bank_01", "cloud_bank_02", "cloud_bank_03",
        "cloud_wisp_00", "cloud_wisp_01", "cloud_wisp_02", "cloud_wisp_03",
        "mist_00", "mist_01", "mist_02", "blizzard_00",
        "blizzard_01", "blizzard_02", "snow_00", "snow_01",
    ],
    "sites": [
        "hangar_closed", "hangar_open", "rigbay_closed", "rigbay_open",
        "padring_idle", "padring_active", "crawlerbay_closed", "crawlerbay_open",
        "hatch_closed", "hatch_open", "lights_off", "lights_on",
    ],
}

SOURCE = {"landmarks": "landmarks_a.png", "weather": "weather_a.png", "sites": "sites_a.png"}


def ground_palette() -> np.ndarray:
    """The exact muted steel/navy ramp already present in run A's mid tile."""
    mid = np.asarray(Image.open(ROOT / "mid.png").convert("RGBA"))
    colors = np.unique(mid[:, :, :3].reshape(-1, 3), axis=0)
    colors = colors[colors.max(axis=1) <= 84]
    return colors.astype(np.float32)


def cleaned_source_cell(im: Image.Image, row: int, col: int, rows: int) -> Image.Image:
    w, h = im.size
    x0, x1 = round(col * w / 4), round((col + 1) * w / 4)
    y0, y1 = round(row * h / rows), round((row + 1) * h / rows)
    return im.crop((x0, y0, x1, y1))


def render_piece(crop: Image.Image, family: str, name: str, palette: np.ndarray) -> Image.Image:
    # The generated grid is a painted source, rather than the final cell. Its
    # contents are resampled once with nearest neighbour to retain pixel edges.
    w, h = crop.size
    scale = min(232 / w, 232 / h)
    size = (max(1, round(w * scale)), max(1, round(h * scale)))
    rgba = np.asarray(crop.resize(size, Image.Resampling.NEAREST).convert("RGBA")).copy()
    rgb = rgba[:, :, :3].astype(np.float32)
    src_alpha = rgba[:, :, 3]

    if family == "weather":
        threshold = 40 if name.startswith("snow") else 48
        alpha = np.where(src_alpha >= threshold, src_alpha, 0).astype(np.float32)
        ceiling = (78 if name.startswith("cloud_bank") else
                   66 if name.startswith("cloud_wisp") else
                   48 if name.startswith("mist") else
                   52 if name.startswith("blizzard") else 98)
        alpha = np.minimum(np.rint(alpha * ceiling / 255), ceiling).astype(np.uint8)
        # The painted sheets occasionally touch a source-grid boundary. Fade
        # those cuts in stepped alpha so no rectangular edge appears in game.
        edge_width = (30 if name.startswith("cloud_bank") else
                      18 if name.startswith(("cloud_wisp", "mist")) else
                      16 if name.startswith("blizzard") else 10)
        yy, xx = np.indices(alpha.shape)
        edge_distance = np.minimum.reduce((xx, yy, size[0] - 1 - xx, size[1] - 1 - yy))
        alpha = np.rint(alpha * np.minimum(edge_distance / edge_width, 1)).astype(np.uint8)
        # Restrict the broad, wispy forms to a stepped translucent ramp. Very
        # pale source pixels remain pale, but cannot become opaque white sheets.
        alpha = np.where(alpha < 8, 0, np.maximum(8, (alpha // 8) * 8)).astype(np.uint8)
        rgb[:, :, 0] = np.minimum(rgb[:, :, 0], 207)
        rgb[:, :, 1] = np.minimum(rgb[:, :, 1], 224)
        rgb[:, :, 2] = np.minimum(rgb[:, :, 2], 246)
        color = Image.fromarray(rgb.astype(np.uint8), "RGB").quantize(
            colors=18, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE
        ).convert("RGB")
        out = np.dstack((np.asarray(color), alpha))
    else:
        alpha = np.where(src_alpha >= 100, 255, 0).astype(np.uint8)
        # Darken painted values to the committed mid layer, then use its
        # literal colour ramp. The occasional beacon remains an accent.
        toned = np.clip(rgb * .35 + 6, 0, 255)
        best = np.full(toned.shape[:2], np.inf, dtype=np.float32)
        chosen = np.zeros(toned.shape, dtype=np.uint8)
        for color in palette:
            dist = ((toned - color) ** 2 * np.array([.9, 1.1, 1.15])).sum(axis=2)
            take = dist < best
            chosen[take] = color.astype(np.uint8)
            best[take] = dist[take]
        # Sparse source beacons: retain a few cyan and magenta pinpoints.
        bright = rgb.max(axis=2)
        cyan = (rgb[:, :, 2] > rgb[:, :, 0] * 1.6) & (rgb[:, :, 1] > rgb[:, :, 0] * 1.4) & (bright > 145)
        magenta = (rgb[:, :, 0] > rgb[:, :, 1] * 1.5) & (rgb[:, :, 2] > rgb[:, :, 1] * 1.3) & (bright > 125)
        for mask, accent in ((cyan, (46, 108, 152)), (magenta, (117, 75, 134))):
            mask &= alpha > 0
            coords = np.argwhere(mask)
            if len(coords):
                scores = bright[mask]
                keep = coords[np.argsort(scores)[-max(1, int((alpha > 0).sum() * .004)):]]
                chosen[keep[:, 0], keep[:, 1]] = accent
        out = np.dstack((chosen, alpha))

    out[out[:, :, 3] == 0, :3] = 0
    piece = Image.fromarray(out, "RGBA")
    cell = Image.new("RGBA", (CELL, CELL))
    cell.alpha_composite(piece, ((CELL - size[0]) // 2, (CELL - size[1]) // 2))
    if family != "sites":
        # Some generated silhouettes sit low in their source grid slot. Center
        # the visible painted bounds without changing the pixel sampling.
        bounds = cell.getchannel("A").getbbox()
        if bounds:
            isolated = cell.crop(bounds)
            centered = Image.new("RGBA", (CELL, CELL))
            centered.alpha_composite(isolated, ((CELL - isolated.width) // 2,
                                                (CELL - isolated.height) // 2))
            cell = centered
    if name == "icebreaker_00":
        # Two clipped fragments from neighbouring generated grid cells sit far
        # outside this ship's ice channel. Remove those isolated edge marks.
        clean = np.asarray(cell).copy()
        clean[:, :35] = 0
        clean[216:] = 0
        cell = Image.fromarray(clean, "RGBA")
    return cell


def make_atlas(family: str, palette: np.ndarray) -> Image.Image:
    names = NAMES[family]
    source = Image.open(SRC / SOURCE[family]).convert("RGBA")
    rows = 3 if family == "sites" else 4
    sheet = Image.new("RGBA", (ATLAS, ATLAS))
    rects = []
    for i, name in enumerate(names):
        row, col = divmod(i, 4)
        crop = cleaned_source_cell(source, row, col, rows)
        cell = render_piece(crop, family, name, palette)
        sheet.alpha_composite(cell, (col * CELL, row * CELL))
        rects.append({"n": name, "x": col * CELL, "y": ATLAS - (row + 1) * CELL, "w": CELL, "h": CELL})
    sheet.save(ROOT / f"{family}.png", optimize=True)
    (ROOT / f"{family}.json").write_text(json.dumps({"sprites": rects}, indent=2) + "\n")
    return sheet


def role(name: str, family: str) -> str:
    return "Landmark" if family == "landmarks" else "Site" if family == "sites" else (
        "Cloud" if name.startswith("cloud") else "Atmosphere")


def hint(name: str, family: str) -> list[int]:
    if family == "weather":
        return [420, 750] if name.startswith("cloud_bank") else [210, 470]
    if family == "sites":
        return [95, 180] if not name.startswith("lights") else [110, 200]
    if name.startswith("cliff"):
        return [120, 220]
    return [75, 155]


def note(name: str) -> str:
    if name.startswith("rig_"):
        return "Offshore ice-leg rig; flare, crane and helipad; slow distant scroll."
    if name.startswith("platform_"):
        return "Ice-locked tank and walkway platform with small beacons."
    if name.startswith("refinery_"):
        return "Coastal pipe and tank compound; no baked smoke."
    if name.startswith("relay_"):
        return "Antenna relay array and cable runs."
    if name.startswith("causeway"):
        return "Rail bridge across broken ice; rotatable segment."
    if name.startswith("icebreaker"):
        return "Overhead icebreaker with fractured ice channel wake."
    if name.startswith("cliff"):
        return "Coastal ice cliff headland with shoreline rubble."
    if name.startswith("derrick"):
        return "Drilling derrick on an ice pad."
    if name.startswith("convoy"):
        return "Tracked crawler convoy in a line."
    if name.startswith("cloud_bank"):
        return "Translucent cloud ceiling mass for the arrival; thin coverage downward."
    if name.startswith("cloud_wisp"):
        return "Light pixel-clustered cloud wisp; keep behind combat."
    if name.startswith("mist"):
        return "Low horizontal drifting mist band."
    if name.startswith("blizzard"):
        return "Sparse diagonal wind-driven snow sheet."
    if name.startswith("snow"):
        return "Distinct small flakes for a particle field."
    if name.startswith("lights"):
        return "Pad marker lamps; paired unlit/lit state."
    return "Centered ground emergence point; paired closed/active state."


def update_manifest() -> None:
    path = ROOT / "manifest.json"
    manifest = json.loads(path.read_text())
    manifest["run_b"] = {
        "build": "python3 src~/build_b.py",
        "preview": "preview_b.png",
        "atlas_grid": "4x4 cells, 256x256; JSON y is measured from image bottom",
        "source_candidates": {family: [f"src~/{family}_a.png", f"src~/{family}_b.png"] for family in NAMES},
        "selected_sources": {family: f"src~/{name}" for family, name in SOURCE.items()},
        "pieces": [
            {"atlas": f"{family}.png", "name": name, "layer_role": role(name, family),
             "on_screen_size_px": hint(name, family), "notes": note(name)}
            for family, names in NAMES.items() for name in names
        ],
    }
    path.write_text(json.dumps(manifest, indent=2) + "\n")


def mid_phone() -> Image.Image:
    mid = Image.open(ROOT / "mid.png").convert("RGBA").resize((1024, 2048), Image.Resampling.NEAREST)
    phone = Image.new("RGBA", (1080, 2400), (5, 10, 19, 255))
    for y in (-500, 1548):
        for x in (-300, 724):
            phone.alpha_composite(mid, (x, y))
    return phone


def sprite(sheet: Image.Image, index: int) -> Image.Image:
    row, col = divmod(index, 4)
    return sheet.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))


def place(phone: Image.Image, sheet: Image.Image, index: int, xy: tuple[int, int], size: int) -> None:
    piece = sprite(sheet, index).resize((size, size), Image.Resampling.NEAREST)
    phone.alpha_composite(piece, xy)


def make_preview(sheets: dict[str, Image.Image]) -> None:
    canvas = Image.new("RGB", (4320, 2500), (4, 10, 20))
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default()
    for i, family in enumerate(("landmarks", "weather", "sites")):
        x = 25 + i * 1065
        bg = Image.new("RGBA", (1024, 1024), (8, 20, 35, 255))
        bg.alpha_composite(sheets[family])
        canvas.paste(bg.convert("RGB"), (x, 55))
        draw.text((x, 30), family.upper() + " / 1024 RGBA", fill=(159, 195, 215), font=font)
        for line in range(1, 4):
            draw.line((x + line * 256, 55, x + line * 256, 1079), fill=(33, 54, 70))
            draw.line((x, 55 + line * 256, x + 1024, 55 + line * 256), fill=(33, 54, 70))
        selections = {
            "landmarks": [0, 9, 11, 14],
            "weather": [0, 5, 11, 14],
            "sites": [0, 1, 8, 9],
        }[family]
        for j, index in enumerate(selections):
            r, c = divmod(j, 2)
            px, py = x + c * 510, 1170 + r * 555
            detail = Image.new("RGBA", (480, 480), (8, 20, 35, 255))
            detail.alpha_composite(sprite(sheets[family], index).resize((480, 480), Image.Resampling.NEAREST))
            canvas.paste(detail.convert("RGB"), (px, py))
            draw.text((px, py - 17), NAMES[family][index], fill=(159, 195, 215), font=font)
    phone = mid_phone()
    # The landmarks are deliberately small compared with the terrain. Clouds
    # overlap across the arrival top and taper into wisps and snow below.
    lm, wx, sites = sheets["landmarks"], sheets["weather"], sheets["sites"]
    for i, xy, size in [(0, (150, 550), 150), (2, (750, 850), 130), (6, (760, 1400), 110),
                        (8, (245, 1710), 180), (9, (700, 1870), 145), (11, (110, 2050), 180),
                        (14, (400, 1270), 140)]:
        place(phone, lm, i, xy, size)
    place(phone, sites, 0, (155, 1120), 180)
    place(phone, sites, 3, (715, 1980), 175)
    for i, xy, size in [(0, (-110, -150), 650), (1, (300, -130), 680), (2, (720, -90), 600),
                        (4, (170, 420), 390), (7, (770, 680), 320), (8, (20, 1040), 370),
                        (11, (520, 1560), 410), (12, (90, 1990), 350), (14, (690, 1290), 260)]:
        place(phone, wx, i, xy, size)
    phone_x, phone_y = 3230, 55
    canvas.paste(phone.convert("RGB"), (phone_x, phone_y))
    draw.rectangle((phone_x, phone_y, phone_x + 1079, phone_y + 2399), outline=(83, 118, 142), width=2)
    draw.text((phone_x, 30), "1080 x 2400 ARRIVAL MOCK / CLOUDS THIN DOWNWARD", fill=(159, 195, 215), font=font)
    canvas.save(ROOT / "preview_b.png", optimize=True)


if __name__ == "__main__":
    palette = ground_palette()
    sheets = {family: make_atlas(family, palette) for family in NAMES}
    update_manifest()
    make_preview(sheets)
