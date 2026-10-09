"""Pack selected generated Verdant run B paintings into game atlases."""

from __future__ import annotations

import json
import colorsys
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
CELL = 256
ATLAS = 1024
NAMES = {
    "landmarks": [
        "tower_00", "tower_01", "refinery_00", "refinery_01",
        "refinery_02", "relay_00", "relay_01", "bridge_00",
        "barge_00", "barge_01", "waterfall_00", "waterfall_01",
        "dome_00", "silo_00", "derrick_00", "canopy_intake_00",
    ],
    "pipes": [
        "pipe_straight_00", "pipe_straight_01", "pipe_bend_00", "pipe_bend_01",
        "pipe_tee_00", "pipe_cross_00", "manifold_00", "manifold_01",
        "pipebridge_00", "pipebridge_01", "pumphouse_00", "pipe_leak_00",
        "pipe_leak_01", "pipe_tangle_00", "pipe_tangle_01", "pipe_loop_00",
    ],
    "fires": [
        "burnfront_00", "burnfront_01", "burnfront_02", "burnfront_03",
        "burnpatch_00", "burnpatch_01", "burnpatch_02", "burnpatch_03",
        "coalbed_00", "coalbed_01", "scorched_00", "scorched_01",
        "firebreak_00", "burntforest_00", "burntforest_01", "burnpatch_04",
    ],
    "weather": [
        "cloud_bank_00", "cloud_bank_01", "cloud_bank_02", "cloud_bank_03",
        "cloud_wisp_00", "cloud_wisp_01", "cloud_wisp_02", "cloud_wisp_03",
        "mist_00", "mist_01", "mist_02", "pollen_00",
        "pollen_01", "smokepall_00", "smokepall_01", "spore_00",
    ],
    "sites": [
        "roothangar_closed", "roothangar_open", "riverbay_closed", "riverbay_open",
        "podpad_idle", "podpad_active", "towerbay_closed", "towerbay_open",
        "hatch_closed", "hatch_open", "lights_off", "lights_on",
    ],
}
SOURCE = {
    "landmarks": "landmarks_a.png", "pipes": "pipes_b.png",
    "fires": "fires_a.png", "weather": "weather_b.png", "sites": "sites_a.png",
}


def source_cells(family: str) -> list[Image.Image]:
    image = Image.open(SRC / SOURCE[family]).convert("RGBA")
    if family != "weather":
        return isolated_painted_objects(image, len(NAMES[family]))
    # The second weather candidate has visible zero-alpha horizontal gutters
    # at these measured source rows. Equal quarters cut cloud fragments loose.
    ys = [0, 350, 620, 870, image.height]
    xs = [0, 350, 665, 975, image.width]
    return [image.crop((xs[i % 4], ys[i // 4], xs[i % 4 + 1], ys[i // 4 + 1]))
            for i in range(len(NAMES[family]))]


def isolated_painted_objects(image: Image.Image, count: int) -> list[Image.Image]:
    """Assign each connected painted island to its intended grid slot.

    Image generation keeps alpha but does not place every row at an exact
    mathematical quarter. Isolating the painted islands prevents a flange or
    ember at a row seam from entering a neighbouring sprite.
    """
    rgba = np.asarray(image)
    height, width = rgba.shape[:2]
    mask = rgba[:, :, 3] >= 100
    seen = np.zeros((height, width), dtype=bool)
    labels = np.zeros((height, width), dtype=np.uint16)
    found = []
    serial = 0
    for y in range(height):
        for x in range(width):
            if not mask[y, x] or seen[y, x]:
                continue
            serial += 1
            queue = deque([(y, x)])
            seen[y, x] = True
            n = sx = sy = 0
            left = right = x
            top = bottom = y
            while queue:
                cy, cx = queue.popleft()
                labels[cy, cx] = serial
                n += 1
                sx += cx
                sy += cy
                left, right = min(left, cx), max(right, cx)
                top, bottom = min(top, cy), max(bottom, cy)
                for ny, nx in ((cy - 1, cx), (cy + 1, cx), (cy, cx - 1), (cy, cx + 1),
                               (cy - 1, cx - 1), (cy - 1, cx + 1), (cy + 1, cx - 1), (cy + 1, cx + 1)):
                    if 0 <= ny < height and 0 <= nx < width and mask[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        queue.append((ny, nx))
            found.append((n, sx / n, sy / n, left, top, right + 1, bottom + 1, serial))
    assert len(found) >= count, (count, len(found))
    major = sorted(found, reverse=True)[:count]
    rows = [sorted(major, key=lambda item: item[2])[i:i + 4] for i in range(0, count, 4)]
    ordered = [item for row in rows for item in sorted(row, key=lambda item: item[1])]
    result = []
    for _, _, _, left, top, right, bottom, label in ordered:
        crop = rgba[top:bottom, left:right].copy()
        crop[labels[top:bottom, left:right] != label] = 0
        result.append(Image.fromarray(crop, "RGBA"))
    return result


def trimmed(cell: Image.Image, family: str) -> Image.Image:
    arr = np.asarray(cell)
    threshold = 45 if family == "weather" else 100
    yy, xx = np.where(arr[:, :, 3] >= threshold)
    if not len(xx):
        raise ValueError(f"empty source cell in {family}")
    return cell.crop((int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1))


def tone_ground(rgba: np.ndarray, family: str) -> np.ndarray:
    rgb = rgba[:, :, :3].astype(np.float32)
    alpha = np.where(rgba[:, :, 3] >= 100, 255, 0).astype(np.uint8)
    opaque = alpha > 0
    target = {"landmarks": .48, "pipes": .48, "fires": .49, "sites": .47}[family]
    p90 = np.percentile(rgb[opaque].max(axis=1), 90) / 255
    factor = min(1., target / max(p90, .01))
    toned = np.clip(rgb * factor, 0, 230).astype(np.uint8)
    # A small number of painted fire cores retain their orange/amber heat.
    if family == "fires":
        heat = (rgb[:, :, 0] > 175) & (rgb[:, :, 0] > rgb[:, :, 1] * 1.13) & (rgb[:, :, 1] > rgb[:, :, 2] * 1.25)
        toned[heat] = np.clip(rgb[heat] * .83, 0, 242).astype(np.uint8)
    # Median cut without dithering converts painted tonal fields into firm pixel ramps.
    quantized = Image.fromarray(toned, "RGB").quantize(
        colors=48 if family == "fires" else 40,
        method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE,
    ).convert("RGB")
    result = np.dstack((np.asarray(quantized), alpha))
    if family == "fires":
        cell_p90 = np.percentile(result[opaque, :3].max(axis=1), 90) / 255
        if cell_p90 > .53:
            result[opaque, :3] = np.rint(result[opaque, :3] * (.53 / cell_p90)).astype(np.uint8)
    result[~opaque, :3] = 0
    return avoid_red_hues(result)


def tone_weather(rgba: np.ndarray, name: str) -> np.ndarray:
    rgb = rgba[:, :, :3].copy()
    source_alpha = rgba[:, :, 3]
    if name.startswith("cloud_bank"):
        ceiling = 84
    elif name.startswith("cloud_wisp"):
        ceiling = 68
    elif name.startswith("smokepall"):
        ceiling = 58
    elif name.startswith("spore"):
        ceiling = 88
    else:
        ceiling = 52
    alpha = np.where(source_alpha >= 45, np.minimum(source_alpha, ceiling), 0)
    alpha = ((alpha // 8) * 8).astype(np.uint8)
    alpha[alpha < 8] = 0
    quantized = Image.fromarray(rgb, "RGB").quantize(
        colors=32, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE,
    ).convert("RGB")
    result = np.dstack((np.asarray(quantized), alpha))
    result[alpha == 0, :3] = 0
    return avoid_red_hues(result)


def avoid_red_hues(result: np.ndarray) -> np.ndarray:
    """Keep the player-only red sector out of painted background ramps."""
    flat = result[:, :, :3].reshape(-1, 3)
    visible = result[:, :, 3].ravel() > 0
    colors, inverse = np.unique(flat[visible], axis=0, return_inverse=True)
    shifted = colors.copy()
    for i, color in enumerate(colors):
        hue, sat, value = colorsys.rgb_to_hsv(*(float(c) / 255 for c in color))
        if sat > .5 and (hue < 15 / 360 or hue > 345 / 360):
            if max(color) < 8:
                shifted[i] = [max(color)] * 3
            else:
                target = 320 / 360 if color[2] > color[1] else 20 / 360
                shifted[i] = np.rint(np.array(colorsys.hsv_to_rgb(target, sat, value)) * 255).astype(np.uint8)
    flat[visible] = shifted[inverse]
    return result


def make_cell(crop: Image.Image, family: str, name: str, common_scale: float | None = None) -> Image.Image:
    crop = trimmed(crop, family)
    scale = common_scale or min(228 / crop.width, 228 / crop.height)
    size = (max(1, min(228, round(crop.width * scale))),
            max(1, min(228, round(crop.height * scale))))
    pixels = np.asarray(crop.resize(size, Image.Resampling.NEAREST)).copy()
    final = tone_weather(pixels, name) if family == "weather" else tone_ground(pixels, family)
    cell = Image.new("RGBA", (CELL, CELL))
    cell.alpha_composite(Image.fromarray(final, "RGBA"), ((CELL - size[0]) // 2, (CELL - size[1]) // 2))
    return cell


def build_atlas(family: str) -> Image.Image:
    names = NAMES[family]
    crops = source_cells(family)
    atlas = Image.new("RGBA", (ATLAS, ATLAS))
    rects = []
    for i, (name, crop) in enumerate(zip(names, crops)):
        row, col = divmod(i, 4)
        scale = None
        if family == "sites":
            mate = i - i % 2
            a, b = (trimmed(crops[j], family) for j in (mate, mate + 1))
            scale = min(228 / max(a.width, b.width), 228 / max(a.height, b.height))
        cell = make_cell(crop, family, name, scale)
        atlas.alpha_composite(cell, (col * CELL, row * CELL))
        rects.append({"n": name, "x": col * CELL, "y": ATLAS - (row + 1) * CELL,
                      "w": CELL, "h": CELL})
    atlas.save(ROOT / f"{family}.png", optimize=True)
    (ROOT / f"{family}.json").write_text(json.dumps({"sprites": rects}, indent=2) + "\n")
    return atlas


PIPE_ENDS = [
    [[28, 123], [228, 123]], [[128, 29], [128, 224]],
    [[31, 97], [184, 198]], [[70, 34], [227, 158]],
    [[29, 120], [228, 120], [128, 201]],
    [[29, 127], [226, 127], [128, 36], [128, 213]],
    [[29, 133], [232, 133]], [[126, 27], [127, 217]],
    [[35, 99], [227, 99]], [[128, 55], [128, 163]],
    [[28, 158], [230, 158]], [[29, 99], [226, 99]],
    [[120, 27], [120, 224]], [[29, 132], [228, 132]],
    [[119, 29], [119, 224]], [[42, 114], [222, 99]],
]


def fire_emitters(cell: Image.Image) -> list[list[int]]:
    arr = np.asarray(cell)
    r, g, b, a = (arr[:, :, i].astype(np.int16) for i in range(4))
    warm = (a > 0) & (r > 95) & (r > g * 1.16) & (g > b * 1.14)
    yy, xx = np.where(warm)
    if not len(xx):
        return [[128, 128]]
    strength = r[yy, xx] + g[yy, xx]
    chosen = []
    for index in np.argsort(strength)[::-1]:
        pt = [int(xx[index]), int(yy[index])]
        if all((pt[0] - old[0]) ** 2 + (pt[1] - old[1]) ** 2 >= 48 ** 2 for old in chosen):
            chosen.append(pt)
        if len(chosen) == 3:
            break
    return chosen


def note(name: str, family: str) -> str:
    if family == "landmarks":
        return {
            "tower": "Rusted pipe trellis with vine canopy and a stack emitter base.",
            "refinery": "Tank farm, racks, and stack outlets; smoke animation belongs in a later run.",
            "relay": "Vine-tangled antennas above a small forest terrain island.",
            "bridge": "Rail viaduct across the bio-river; rotate for placement.",
            "barge": "Top-down harvester barge with lime river wake.",
            "waterfall": "Cliff-step bio-river fall with a corroded pipe intake.",
            "dome": "Biological greenhouse domes with restrained status lamps.",
            "silo": "Seed silo and trellis over canopy.",
            "derrick": "Sap derrick rising through canopy.",
            "canopy": "Extra sixteenth cell: jungle water intake and pipe gantry.",
        }[name.split("_")[0]]
    if family == "pipes":
        return "Overgrown flanged copper network; pipe endpoints are nominal top-left cell coordinates for chaining."
    if family == "fires":
        return "Static scorched canopy and ember ground; listed hot points are bases for later flame/smoke animation."
    if family == "weather":
        return "Low-alpha atmospheric overlay; keep above terrain but below combat readability priority."
    return "Centered elite emergence site; closed/open pair shares scale and terrain silhouette."


def manifest_piece(family: str, name: str, index: int, atlas: Image.Image) -> dict:
    row, col = divmod(index, 4)
    cell = atlas.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))
    role = ({"landmarks": "Landmark", "pipes": "Pipe", "fires": "Fire", "sites": "Site"}
            .get(family, "Cloud" if name.startswith("cloud") else "Atmosphere"))
    size = ([390, 620] if name.startswith("cloud_bank") else [190, 420]) if family == "weather" else (
        [145, 260] if family == "fires" else [120, 240] if family == "pipes" else
        [100, 190] if family == "sites" else [100, 230])
    emitters = fire_emitters(cell) if family == "fires" else []
    if family == "pipes" and name.startswith("pipe_leak"):
        emitters = [[128, 102]] if name.endswith("00") else [[120, 128]]
    landmark_stacks = {
        "tower_00": [[122, 20]], "tower_01": [[192, 20]],
        "refinery_00": [[138, 20]], "refinery_01": [[156, 20]],
        "refinery_02": [[107, 24]], "silo_00": [[117, 22]],
        "derrick_00": [[133, 20]],
    }
    if family == "landmarks":
        emitters = landmark_stacks.get(name, [])
    piece = {"atlas": f"{family}.png", "name": name, "layer_role": role,
             "emitter_points_px_from_top_left": emitters,
             "pipe_end_points_px_from_top_left": PIPE_ENDS[index] if family == "pipes" else [],
             "on_screen_size_px": size, "notes": note(name, family)}
    if family == "sites":
        piece["emergence_point_px_from_top_left"] = [128, 128]
    return piece


def update_manifest(atlases: dict[str, Image.Image]) -> None:
    path = ROOT / "manifest.json"
    manifest = json.loads(path.read_text())
    manifest["run_b"] = {
        "build": "python3 src~/build_b.py", "preview": "preview_b.png",
        "atlas_grid": "4x4 256x256 cells in 1024x1024 RGBA; JSON y measured from image bottom",
        "source_candidates": {"landmarks": ["src~/landmarks_a.png"],
                              "pipes": ["src~/pipes_a.png", "src~/pipes_b.png"],
                              "fires": ["src~/fires_a.png"],
                              "weather": ["src~/weather_a.png", "src~/weather_b.png"],
                              "sites": ["src~/sites_a.png"]},
        "selected_sources": {family: f"src~/{source}" for family, source in SOURCE.items()},
        "point_origin": "All emitter, pipe-end, and emergence points use (x,y) pixels from cell top-left.",
        "brightness": "Landmarks, pipes, fires, and sites opaque-pixel HSV value p90 <= 0.55; sparse fire cores and lamps may exceed it.",
        "weather_alpha": "Cloud banks <= 80, wisps <= 64, mist and pollen <= 48, smoke palls <= 56, spores <= 88 of 255.",
        "extra_cells": ["canopy_intake_00", "pipe_loop_00", "burnpatch_04"],
        "pieces": [manifest_piece(family, name, i, atlases[family])
                   for family, names in NAMES.items() for i, name in enumerate(names)],
    }
    path.write_text(json.dumps(manifest, indent=2) + "\n")


def sprite(atlas: Image.Image, index: int) -> Image.Image:
    row, col = divmod(index, 4)
    return atlas.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))


def place(phone: Image.Image, atlas: Image.Image, index: int, x: int, y: int, size: int) -> None:
    item = sprite(atlas, index).resize((size, size), Image.Resampling.NEAREST)
    phone.alpha_composite(item, (x, y))


def phone_mock(atlases: dict[str, Image.Image]) -> Image.Image:
    mid = Image.open(ROOT / "v1/mid.png").convert("RGBA")
    base = mid.resize((1200, 2400), Image.Resampling.NEAREST).crop((60, 0, 1140, 2400))
    phone = base.copy()
    for index, x, y, size in [(0, 110, 820, 320), (2, 570, 1250, 340),
                              (7, 70, 1710, 420), (10, 680, 650, 320)]:
        place(phone, atlases["landmarks"], index, x, y, size)
    for index, x, y, size in [(8, 100, 1170, 330), (11, 490, 1790, 290)]:
        place(phone, atlases["pipes"], index, x, y, size)
    for index, x, y, size in [(0, 480, 1800, 360), (5, 50, 1430, 250), (13, 690, 960, 280)]:
        place(phone, atlases["fires"], index, x, y, size)
    place(phone, atlases["sites"], 0, 580, 410, 280)
    place(phone, atlases["sites"], 3, 210, 2010, 310)
    for index, x, y, size in [(0, -100, -50, 650), (1, 340, -85, 660),
                              (2, 740, 40, 580), (4, 10, 260, 430),
                              (8, 230, 1150, 520), (11, 520, 670, 440),
                              (13, 550, 1660, 460), (15, 180, 2040, 390)]:
        place(phone, atlases["weather"], index, x, y, size)
    return phone.convert("RGB")


def preview(atlases: dict[str, Image.Image]) -> None:
    out = Image.new("RGB", (4340, 2500), (6, 19, 14))
    draw = ImageDraw.Draw(out)
    font = ImageFont.load_default()
    order = ["landmarks", "pipes", "fires", "weather", "sites"]
    for i, family in enumerate(order):
        x = 32 + (i % 3) * 1052
        y = 50 + (i // 3) * 1190
        bg = Image.new("RGBA", (1024, 1024), (13, 32, 25, 255))
        bg.alpha_composite(atlases[family])
        out.paste(bg.convert("RGB"), (x, y))
        draw.text((x, y + 1032), family.upper() + " / 1024 RGBA", fill=(190, 213, 183), font=font)
        for line in range(1, 4):
            draw.line((x + 256 * line, y, x + 256 * line, y + 1024), fill=(47, 71, 58))
            draw.line((x, y + 256 * line, x + 1024, y + 256 * line), fill=(47, 71, 58))
    out.paste(phone_mock(atlases), (3240, 50))
    draw.text((3240, 2460), "1080 x 2400 phone mock / v1 mid + run B", fill=(190, 213, 183), font=font)
    out.save(ROOT / "preview_b.png", optimize=True)


def main() -> None:
    atlases = {family: build_atlas(family) for family in NAMES}
    update_manifest(atlases)
    preview(atlases)
    print("Built five Verdant run B atlases, JSON rects, manifest, and preview.")


if __name__ == "__main__":
    main()
