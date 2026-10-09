"""Pack generated Ember run B paintings into Unity sprite atlases."""

from __future__ import annotations

import colorsys
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
        "forgetower_00", "forgetower_01", "refinery_00", "refinery_01",
        "refinery_02", "coolingtower_00", "coolingtower_01", "smelter_00",
        "kiln_00", "derrick_00", "derrick_01", "slagbarge_00",
        "slagbarge_01", "lavafall_00", "viaduct_00", "forgewell_00",
    ],
    "pipes": [
        "pipe_straight_00", "pipe_straight_01", "pipe_bend_00", "pipe_bend_01",
        "pipe_tee_00", "pipe_cross_00", "manifold_00", "manifold_01",
        "pipebridge_00", "pipebridge_01", "pumphouse_00", "pipe_leak_00",
        "pipe_leak_01", "pipe_lava_00", "pipe_tangle_00", "pipe_tangle_01",
    ],
    "fires": [
        "lavafountain_00", "lavafountain_01", "lavafountain_02", "eruptionscar_00",
        "eruptionscar_01", "eruptionscar_02", "coalbed_00", "coalbed_01",
        "lavapool_00", "lavapool_01", "flarestack_00", "flarestack_01",
        "scorched_00", "scorched_01", "firebreak_00", "lavachannel_00",
    ],
    "weather": [
        "cloud_bank_00", "cloud_bank_01", "cloud_bank_02", "cloud_bank_03",
        "cloud_wisp_00", "cloud_wisp_01", "cloud_wisp_02", "cloud_wisp_03",
        "mist_00", "mist_01", "mist_02", "ashgust_00",
        "ashgust_01", "ashgust_02", "smokepall_00", "smokepall_01", "embers_00",
    ],
    "sites": [
        "foundryhangar_closed", "foundryhangar_open", "magmabay_closed", "magmabay_open",
        "slagpad_idle", "slagpad_active", "furnacebay_closed", "furnacebay_open",
        "hatch_closed", "hatch_open", "lights_off", "lights_on",
    ],
}

# The first landmark sheet omits the second barge; a separately painted barge
# supplies that named sprite. The flare bases likewise use a corrected painting.
SOURCE_INDEX = {"landmarks": list(range(12)) + [11, 12, 13, 14],
                "pipes": list(range(16)), "fires": list(range(16)),
                "weather": list(range(16)) + [15], "sites": list(range(12))}
SOURCE_FILE = {family: f"{family}_a.png" for family in NAMES}


def source_crops(family: str) -> list[Image.Image]:
    image = Image.open(SRC / SOURCE_FILE[family]).convert("RGBA")
    rows = 3 if family == "sites" else 4
    xedges = [round(i * image.width / 4) for i in range(5)]
    yedges = [round(i * image.height / rows) for i in range(rows + 1)]
    cells = [image.crop((xedges[col], yedges[row], xedges[col + 1], yedges[row + 1]))
             for row in range(rows) for col in range(4)]
    result = [cells[i] for i in SOURCE_INDEX[family]]
    if family == "landmarks":
        # The painting placed five objects across its third visual row, so
        # equal grid cuts would mix a derrick with a barge at their seam.
        top, bottom = yedges[2], yedges[3]
        result[9] = image.crop((380, top, 700, bottom))
        result[10] = image.crop((690, top, 965, bottom))
        result[11] = image.crop((955, top, 1245, bottom))
        result[12] = Image.open(SRC / "slagbarge_b.png").convert("RGBA")
    if family == "fires":
        flare_sheet = Image.open(SRC / "flarestacks_b.png").convert("RGBA")
        middle = flare_sheet.width // 2
        result[10] = flare_sheet.crop((0, 0, middle, flare_sheet.height))
        result[11] = flare_sheet.crop((middle, 0, flare_sheet.width, flare_sheet.height))
    if family == "weather":
        # The source's last slot paints smoke at left and an ember field at right.
        last = cells[15]
        result[15] = last.crop((0, 0, round(last.width * .60), last.height))
        result[16] = last.crop((round(last.width * .58), 0, last.width, last.height))
    return result


def hue_guard(rgba: np.ndarray) -> np.ndarray:
    flat = rgba[:, :, :3].reshape(-1, 3)
    visible = rgba[:, :, 3].ravel() > 0
    colors, inverse = np.unique(flat[visible], axis=0, return_inverse=True)
    shifted = colors.copy()
    for i, color in enumerate(colors):
        h, s, v = colorsys.rgb_to_hsv(*(float(c) / 255 for c in color))
        degrees = h * 360
        # Player red is ~354 degrees. Saturated material stays amber (>=23 deg),
        # and the small warning lamps stay magenta. No green/ice material.
        if s > .22 and (degrees < 23 or degrees >= 342):
            h = 320 / 360 if color[2] > color[1] * 1.25 else 26 / 360
        elif s > .22 and 72 < degrees < 278:
            h = 38 / 360
        shifted[i] = np.rint(np.array(colorsys.hsv_to_rgb(h, s, v)) * 255)
    flat[visible] = shifted[inverse]
    return rgba


def pixel_clean(image: Image.Image, family: str, name: str) -> Image.Image:
    rgba = np.array(image.convert("RGBA"), copy=True)
    a = rgba[:, :, 3]
    if family == "weather":
        cap = (80 if name.startswith("cloud_bank") else 64 if name.startswith("cloud_wisp")
               else 56 if name.startswith("smokepall") else 72 if name.startswith("embers") else 48)
        a = np.where(a >= 28, np.minimum(a, cap), 0)
        a = ((a // 8) * 8).astype(np.uint8)
        rgba[:, :, 3] = a
    else:
        rgba[:, :, 3] = np.where(a >= 95, 255, 0).astype(np.uint8)
    opaque = rgba[:, :, 3] > 0
    if not opaque.any():
        raise ValueError(f"Empty sprite {name}")
    if family != "weather":
        rgb = rgba[:, :, :3].astype(np.float32)
        p90 = np.percentile(rgb[opaque].max(axis=1), 90) / 255
        target = .48 if family in ("landmarks", "sites") else .50 if family == "pipes" else .51
        rgb *= min(1.0, target / max(.01, p90))
        rgba[:, :, :3] = np.clip(rgb, 0, 240).astype(np.uint8)
    quant = Image.fromarray(rgba[:, :, :3], "RGB").quantize(
        colors=48 if family == "fires" else 40, method=Image.Quantize.MEDIANCUT,
        dither=Image.Dither.NONE).convert("RGB")
    rgba[:, :, :3] = np.asarray(quant)
    hue_guard(rgba)
    rgba[~opaque, :3] = 0
    if family != "weather":
        p90 = np.percentile(rgba[opaque, :3].max(axis=1), 90) / 255
        if p90 > .54:
            rgba[opaque, :3] = np.rint(rgba[opaque, :3] * (.54 / p90)).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA")


def fitted_sprite(source: Image.Image, family: str, name: str,
                  bounds: tuple[int, int] = (256, 256), common_scale: float | None = None) -> Image.Image:
    alpha = np.asarray(source.getchannel("A"))
    threshold = 28 if family == "weather" else 95
    yy, xx = np.where(alpha >= threshold)
    if not len(xx):
        raise ValueError(f"Empty source {name}")
    cut = source.crop((int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1))
    bw, bh = bounds
    scale = common_scale or min((bw - 28) / cut.width, (bh - 28) / cut.height)
    # Two-pixel nearest-neighbour clusters produce a stable Unity-sized pixel grid.
    native = (max(1, int((cut.width * scale) // 2)), max(1, int((cut.height * scale) // 2)))
    small = cut.resize(native, Image.Resampling.NEAREST)
    painted = pixel_clean(small, family, name).resize((native[0] * 2, native[1] * 2), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", bounds)
    canvas.alpha_composite(painted, ((bw - painted.width) // 2, (bh - painted.height) // 2))
    return canvas


def build_atlas(family: str) -> tuple[Image.Image, list[dict]]:
    names, crops = NAMES[family], source_crops(family)
    atlas = Image.new("RGBA", (ATLAS, ATLAS))
    rects = []
    for i, (name, crop) in enumerate(zip(names, crops)):
        row, col = divmod(min(i, 15), 4)
        if family == "weather" and i >= 15:
            # Seventeen requested weather names share the final 256-pixel slot.
            bw = 128
            bx = col * CELL + (0 if i == 15 else 128)
        else:
            bw = CELL
            bx = col * CELL
        by = row * CELL
        common = None
        if family == "sites":
            mate = i - i % 2
            pair = crops[mate:mate + 2]
            extents = []
            for item in pair:
                a = np.asarray(item.getchannel("A")) >= 95
                yy, xx = np.where(a)
                extents.append((int(xx.max() - xx.min() + 1), int(yy.max() - yy.min() + 1)))
            common = min(228 / max(e[0] for e in extents), 228 / max(e[1] for e in extents))
        item = fitted_sprite(crop, family, name, (bw, CELL), common)
        atlas.alpha_composite(item, (bx, by))
        rects.append({"n": name, "x": bx, "y": ATLAS - by - CELL, "w": bw, "h": CELL})
    atlas.save(ROOT / f"{family}.png", optimize=True)
    (ROOT / f"{family}.json").write_text(json.dumps({"sprites": rects}, indent=2) + "\n")
    return atlas, rects


PIPE_ENDS = [
    [[20, 128], [236, 128]], [[128, 20], [128, 236]],
    [[24, 88], [176, 226]], [[26, 90], [190, 228]],
    [[20, 128], [236, 128], [128, 20]],
    [[20, 128], [236, 128], [128, 20], [128, 236]],
    [[20, 130], [236, 130]], [[20, 130], [236, 130]],
    [[20, 110], [236, 110]], [[20, 110], [236, 110]],
    [[20, 147], [214, 144]], [[128, 233]], [[20, 128], [236, 128]],
    [[20, 128], [236, 128]], [[32, 210], [188, 34]], [[20, 128], [236, 128]],
]


def cell_image(atlas: Image.Image, rect: dict) -> Image.Image:
    y = ATLAS - rect["y"] - rect["h"]
    return atlas.crop((rect["x"], y, rect["x"] + rect["w"], y + rect["h"]))


def hot_points(cell: Image.Image, count: int = 2) -> list[list[int]]:
    a = np.asarray(cell)
    r, g, b = (a[:, :, i].astype(int) for i in range(3))
    hot = (a[:, :, 3] > 0) & (r > 80) & (r > b * 1.25) & (g > b * 1.25)
    yy, xx = np.where(hot)
    if not len(xx):
        return [[cell.width // 2, cell.height // 2]]
    strength = r[yy, xx] + g[yy, xx]
    out = []
    for i in np.argsort(strength)[::-1]:
        point = [int(xx[i]), int(yy[i])]
        if all((point[0] - p[0]) ** 2 + (point[1] - p[1]) ** 2 > 48 ** 2 for p in out):
            out.append(point)
        if len(out) == count:
            break
    return out


def piece(family: str, name: str, index: int, atlas: Image.Image, rect: dict) -> dict:
    role = {"landmarks": "Landmark", "pipes": "Pipe", "fires": "Fire", "sites": "Site"}.get(
        family, "Cloud" if name.startswith("cloud") else "Atmosphere")
    emitter = []
    if family == "fires":
        emitter = ([[128, 32]] if name.startswith("flarestack") else
                   hot_points(cell_image(atlas, rect), 3 if name.startswith("lavafountain") else 2))
    elif family == "pipes" and name.startswith("pipe_leak"):
        emitter = hot_points(cell_image(atlas, rect), 1)
    elif family == "landmarks" and name.startswith(("forgetower", "refinery", "coolingtower", "smelter", "derrick", "forgewell")):
        emitter = [[128, 36]]
    notes = {
        "landmarks": "Painted brass/black-basalt industrial terrain island; stack smoke animates separately.",
        "pipes": "Painted copper/brass flanged pipework; endpoint coordinates are nominal chain anchors.",
        "fires": "Static lava and fire ground; hot points anchor later animated flame or smoke.",
        "weather": "Low-alpha drifting ash or smoke overlay above terrain, behind enemies and bullets.",
        "sites": "Centered elite emergence aperture; paired state shares scale and basalt footing.",
    }[family]
    if name == "slagbarge_01":
        notes += " Distinct separately painted canal barge."
    if name.startswith("flarestack"):
        notes += " Open glowing mouth is the flame emitter; no baked flame plume."
    if name in ("forgewell_00", "lavachannel_00"):
        notes += " Additional painted sixteenth-cell subject."
    if name in ("smokepall_01", "embers_00"):
        notes += " Shares the final 256-pixel weather cell as a separate half-cell rect."
    size = ([390, 620] if name.startswith("cloud_bank") else [190, 420]) if family == "weather" else (
        [150, 270] if family == "fires" else [140, 260] if family == "pipes" else
        [110, 200] if family == "sites" else [125, 255])
    item = {"atlas": f"{family}.png", "name": name, "layer_role": role,
            "emitter_points_px_from_top_left": emitter,
            "pipe_end_points_px_from_top_left": PIPE_ENDS[index] if family == "pipes" else [],
            "on_screen_size_px": size, "notes": notes}
    if family == "sites":
        item["emergence_point_px_from_top_left"] = [128, 128]
    return item


def update_manifest(built: dict[str, tuple[Image.Image, list[dict]]]) -> None:
    path = ROOT / "manifest.json"
    manifest = json.loads(path.read_text())
    manifest["run_b"] = {
        "build": "python3 src~/build_b.py", "preview": "preview_b.png",
        "atlas_grid": "4x4 grid of 256x256 cells in 1024x1024 RGBA; weather final cell contains two nonoverlapping half-cell sprites; sites use three rows",
        "json_coordinates": "Unity sprite rect y from image bottom; points from individual sprite rect top-left",
        "source_candidates": {family: [f"src~/{SOURCE_FILE[family]}"] +
                              (["src~/slagbarge_b.png"] if family == "landmarks" else
                               ["src~/flarestacks_b.png"] if family == "fires" else [])
                              for family in NAMES},
        "selected_sources": {family: [f"src~/{SOURCE_FILE[family]}"] +
                             (["src~/slagbarge_b.png"] if family == "landmarks" else
                              ["src~/flarestacks_b.png"] if family == "fires" else [])
                             for family in NAMES},
        "brightness": "Landmarks, pipes, fires and sites opaque HSV value p90 <= 0.55; sparse cores may be brighter.",
        "weather_alpha": "Banks <=80, wisps <=64, mist/gust <=48, smoke <=56, embers <=72 of 255.",
        "count_resolution": "Landmarks and fires each list 15 names, so forgewell_00 and lavachannel_00 fill the sixteenth cells. Weather lists 17 names; two sprites share its last 256px cell.",
        "pieces": [piece(family, name, i, built[family][0], built[family][1][i])
                   for family, names in NAMES.items() for i, name in enumerate(names)],
    }
    path.write_text(json.dumps(manifest, indent=2) + "\n")


def place(canvas: Image.Image, atlas: Image.Image, rect: dict,
          x: int, y: int, size: int) -> None:
    sprite = cell_image(atlas, rect)
    scale = size / max(sprite.width, sprite.height)
    image = sprite.resize((round(sprite.width * scale), round(sprite.height * scale)), Image.Resampling.NEAREST)
    canvas.alpha_composite(image, (x, y))


def phone_mock(built: dict[str, tuple[Image.Image, list[dict]]], variant: str) -> Image.Image:
    mid = Image.open(ROOT / variant / "mid.png").convert("RGBA")
    base = mid.resize((1200, 2400), Image.Resampling.NEAREST).crop((60, 0, 1140, 2400))
    phone = base.copy()
    for family, entries in {
        "landmarks": [(0, 100, 820, 370), (2, 550, 1250, 390), (14, 70, 1750, 430), (9, 640, 670, 320)],
        "pipes": [(8, 80, 1180, 340), (5, 520, 1740, 290), (14, 400, 2100, 250)],
        "fires": [(0, 460, 1770, 340), (4, 40, 1430, 290), (12, 690, 920, 260)],
        "sites": [(0, 580, 420, 280), (3, 190, 2070, 300)],
        "weather": [(0, -110, -60, 680), (1, 340, -60, 680), (2, 760, 30, 560),
                    (4, 20, 300, 410), (8, 220, 1100, 540), (11, 530, 700, 480),
                    (14, 610, 1610, 500), (16, 240, 2010, 350)],
    }.items():
        atlas, rects = built[family]
        for index, x, y, size in entries:
            place(phone, atlas, rects[index], x, y, size)
    return phone.convert("RGB")


def preview(built: dict[str, tuple[Image.Image, list[dict]]]) -> None:
    out = Image.new("RGB", (5430, 2510), (9, 9, 8))
    draw = ImageDraw.Draw(out)
    font = ImageFont.load_default()
    for i, family in enumerate(NAMES):
        x = 24 + (i % 3) * 1050
        y = 40 + (i // 3) * 1190
        bg = Image.new("RGBA", (ATLAS, ATLAS), (15, 12, 10, 255))
        bg.alpha_composite(built[family][0])
        out.paste(bg.convert("RGB"), (x, y))
        draw.text((x, y + 1033), family.upper() + " / 1024 RGBA", fill=(191, 169, 137), font=font)
        for line in range(1, 4):
            draw.line((x + CELL * line, y, x + CELL * line, y + ATLAS), fill=(63, 44, 28))
            draw.line((x, y + CELL * line, x + ATLAS, y + CELL * line), fill=(63, 44, 28))
    for variant, x in [("v1", 3200), ("v2", 4310)]:
        out.paste(phone_mock(built, variant), (x, 40))
        draw.text((x, 2450), f"1080 x 2400 / {variant} mid + run B", fill=(191, 169, 137), font=font)
    out.save(ROOT / "preview_b.png", optimize=True)


def main() -> None:
    built = {family: build_atlas(family) for family in NAMES}
    update_manifest(built)
    preview(built)
    print("Built five Ember run B painted atlases, JSONs, manifest, and two-phone preview.")


if __name__ == "__main__":
    main()
