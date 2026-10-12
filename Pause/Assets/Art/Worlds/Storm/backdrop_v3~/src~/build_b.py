"""Assemble Storm piece atlases from the selected painted imagegen sheets.

All scaling is nearest-neighbour.  Pixel cleanup is alpha clipping, local hue
correction, and cell-edge attenuation; no spatial blur or global RGB palette.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from collections import deque

ROOT = Path(__file__).resolve().parent.parent
SRC = Path(__file__).resolve().parent
BG = (11, 11, 26, 255)
CELL = 256

NAMES = {
    "landmarks": [
        "anvil_tower_00", "anvil_tower_01", "anvil_tower_02", "anvil_tower_03",
        "spire_00", "spire_01", "skyhook_00", "skyhook_01",
        "refinery_float_00", "refinery_float_01", "refinery_float_02", "siphon_tower_00",
        "airship_dock_00", "gantry_00", "turbine_raft_00", "relay_dish_00",
    ],
    "sites": [
        "skyhook_dock_closed", "skyhook_dock_open", "refinery_pad_closed", "refinery_pad_open",
        "balloon_hangar_closed", "balloon_hangar_open", "gantry_hatch_closed", "gantry_hatch_open",
        "cloud_bay_closed", "cloud_bay_open", "lights_off", "lights_on",
    ],
    "weather": [
        *[f"cloud_bank_{i:02d}" for i in range(4)],
        *[f"cloud_wisp_{i:02d}" for i in range(3)], "mist_00", "mist_01",
        "gust_00", "gust_01", "pall_00",
        *[f"rain_{i:02d}" for i in range(4)],
    ],
    "lightning": [
        *[f"bolt_{i:02d}" for i in range(6)], "strike_00", "strike_01",
        "flash_00", "flash_01", "arc_00",
        *[f"debris_{i:02d}" for i in range(5)],
    ],
}

SOURCES = {
    "landmarks": "landmarks_candidate_b.png",
    "sites": "sites_candidate_a.png",
    "weather": "weather_candidate_b.png",
    "lightning": "lightning_candidate_b.png",
}


def source_filename(atlas: str, index: int) -> str:
    return "lightning_candidate_a.png" if atlas == "lightning" and index >= 11 else SOURCES[atlas]


def source_cell(atlas: str, index: int) -> Image.Image:
    im = Image.open(SRC / source_filename(atlas, index)).convert("RGBA")
    col, row = index % 4, index // 4
    if atlas == "landmarks":
        ys = [0, 320, 630, 925, im.height]
    elif atlas == "sites":
        ys = [0, 290, 575, 845, im.height]
    else:
        ys = [round(i * im.height / 4) for i in range(5)]
    xs = [round(i * im.width / 4) for i in range(5)]
    return im.crop((xs[col], ys[row], xs[col + 1], ys[row + 1]))


def hue_clean(im: Image.Image, atlas: str) -> Image.Image:
    a = np.array(im.convert("RGBA"))
    rgb = Image.fromarray(a[:, :, :3], "RGB")
    hsv = np.array(rgb.convert("HSV"))
    h = hsv[:, :, 0].astype(np.float32) * 360 / 255
    s = hsv[:, :, 1].astype(np.float32) / 255
    v = hsv[:, :, 2].astype(np.float32) / 255
    bad = (((h >= 95) & (h <= 195) & (s > .18)) |
           ((h >= 250) & (h <= 300) & (s > .30)) |
           (((h < 70) | (h > 340)) & (s > .18)))
    h[bad] = 222
    s[bad] = np.minimum(s[bad], .45 if atlas == "lightning" else .30)
    # Storm has a quiet material palette and pale electric accents.
    s = np.minimum(s, .55 if atlas == "lightning" else .38)
    if atlas == "landmarks":
        opaque = a[:, :, 3] >= 128
        p90 = float(np.percentile(v[opaque], 90)) if opaque.any() else 1
        knee = max(p90, .01)
        lo = v <= knee
        v[lo] *= .53 / knee
        v[~lo] = .53 + (v[~lo] - knee) * (.45 / max(1 - knee, .01))
    hsv[:, :, 0] = np.clip(h * 255 / 360, 0, 255).astype(np.uint8)
    hsv[:, :, 1] = np.clip(s * 255, 0, 255).astype(np.uint8)
    hsv[:, :, 2] = np.clip(v * 255, 0, 255).astype(np.uint8)
    a[:, :, :3] = np.array(Image.fromarray(hsv, "HSV").convert("RGB"))
    return Image.fromarray(a, "RGBA")


def alpha_cleanup(im: Image.Image, atlas: str, name: str) -> Image.Image:
    a = np.array(im, dtype=np.uint8)
    alpha = a[:, :, 3].astype(np.float32)
    alpha[alpha < (18 if atlas == "weather" else 9)] = 0
    if atlas == "weather":
        # Painted translucency is preserved but held beneath gameplay.
        limit = 150 if name.startswith("cloud_bank") else 105
        alpha *= limit / max(float(alpha.max()), 1)
    elif atlas == "lightning" and name.startswith("debris"):
        alpha[alpha < 28] = 0
    a[:, :, 3] = np.clip(alpha, 0, 255).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


def fill_holes(mask: np.ndarray) -> np.ndarray:
    """Keep enclosed flat plating after identifying textured metal edges."""
    h, w = mask.shape
    outside = np.zeros((h, w), dtype=bool)
    todo = deque()
    for x in range(w):
        if not mask[0, x]: todo.append((0, x))
        if not mask[h - 1, x]: todo.append((h - 1, x))
    for y in range(h):
        if not mask[y, 0]: todo.append((y, 0))
        if not mask[y, w - 1]: todo.append((y, w - 1))
    while todo:
        y, x = todo.popleft()
        if y < 0 or x < 0 or y >= h or x >= w or outside[y, x] or mask[y, x]:
            continue
        outside[y, x] = True
        todo.extend(((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)))
    return ~outside


def remove_smooth_matte(im: Image.Image) -> Image.Image:
    """Distinguish detailed painted steel from imagegen's smooth grey surround."""
    arr = np.array(im)
    grey = im.convert("L")
    hi = np.array(grey.filter(ImageFilter.MaxFilter(5)), dtype=np.int16)
    lo = np.array(grey.filter(ImageFilter.MinFilter(5)), dtype=np.int16)
    detail = (hi - lo) > 6
    detail |= (np.array(grey) > 82) & ((hi - lo) > 3)
    dilated = np.array(Image.fromarray((detail * 255).astype(np.uint8), "L").filter(ImageFilter.MaxFilter(5))) > 0
    # The generated sheets put a smooth backdrop inside the same alpha bounds.
    # Filling every hole would restore that rectangle, so retain only textured
    # material and a short neighbourhood around it.
    mask = dilated
    arr[:, :, 3] = np.where(mask, arr[:, :, 3], 0)
    return Image.fromarray(arr, "RGBA")


def lightning_core_matte(im: Image.Image) -> Image.Image:
    """Take branching painted channels while discarding their broad blue canvas."""
    arr = np.array(im)
    grey = im.convert("L")
    luma = np.array(grey, dtype=np.int16)
    core = (luma > 220) & (arr[:, :, 3] > 80)
    # The real painted branches form a few long connected components; reject
    # isolated luminous dust that would otherwise produce a full-cell halo.
    seen = np.zeros(core.shape, dtype=bool)
    kept = np.zeros(core.shape, dtype=bool)
    for y, x in zip(*np.nonzero(core)):
        if seen[y, x]:
            continue
        q = deque([(int(y), int(x))])
        seen[y, x] = True
        points = []
        while q:
            cy, cx = q.popleft()
            points.append((cy, cx))
            for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)):
                ny, nx = cy + dy, cx + dx
                if 0 <= ny < core.shape[0] and 0 <= nx < core.shape[1] and core[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    q.append((ny, nx))
        if len(points) >= 25:
            for cy, cx in points:
                kept[cy, cx] = True
    core = kept
    arr[:, :, 3] = np.where(core, np.maximum(arr[:, :, 3], 210), 0)
    return Image.fromarray(arr, "RGBA")


def add_channel_glow(im: Image.Image) -> Image.Image:
    core = np.array(im.getchannel("A")) > 0
    base = Image.fromarray((core * 255).astype(np.uint8), "L")
    layer = np.zeros((im.height, im.width, 4), dtype=np.uint8)
    layer[:, :, :3] = (147, 183, 231)
    for radius, opacity in [(13, 18), (9, 35), (5, 65), (3, 110)]:
        dilated = np.array(base.filter(ImageFilter.MaxFilter(radius))) > 0
        layer[:, :, 3] = np.maximum(layer[:, :, 3], (dilated * opacity).astype(np.uint8))
    out = Image.fromarray(layer, "RGBA")
    out.alpha_composite(im)
    return out


def radial_flash_matte(im: Image.Image) -> Image.Image:
    a = np.array(im)
    yy, xx = np.mgrid[:im.height, :im.width]
    rx = (xx - im.width / 2) / (im.width * .48)
    ry = (yy - im.height / 2) / (im.height * .48)
    r = np.sqrt(rx * rx + ry * ry)
    fade = np.clip((.85 - r) / .45, 0, 1) ** 2
    fade = np.floor(fade * 16) / 16
    a[:, :, 3] = (a[:, :, 3].astype(float) * fade).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


def remove_landmark_islands(im: Image.Image) -> Image.Image:
    """Clear detached sheet-crop fragments around the painted main silhouette."""
    a = np.array(im)
    mask = a[:, :, 3] > 8
    seen = np.zeros(mask.shape, dtype=bool)
    groups = []
    for y, x in zip(*np.nonzero(mask)):
        if seen[y, x]:
            continue
        q = deque([(int(y), int(x))])
        seen[y, x] = True
        points = []
        while q:
            cy, cx = q.popleft()
            points.append((cy, cx))
            for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)):
                ny, nx = cy + dy, cx + dx
                if 0 <= ny < 256 and 0 <= nx < 256 and mask[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    q.append((ny, nx))
        groups.append(points)
    if not groups:
        return im
    biggest = max(map(len, groups))
    for group in groups:
        if len(group) < max(24, biggest * .025):
            for y, x in group:
                a[y, x, 3] = 0
    return Image.fromarray(a, "RGBA")


def fit_piece(im: Image.Image, atlas: str, name: str) -> Image.Image:
    a = np.array(im.getchannel("A"))
    ys, xs = np.nonzero(a > (15 if atlas == "weather" else 8))
    if not len(xs):
        return Image.new("RGBA", (CELL, CELL))
    im = im.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))
    maxw, maxh = (226, 226)
    if name.startswith("spire_"):
        maxw, maxh = 205, 192
    if name.startswith(("bolt_", "arc_")):
        maxw, maxh = 214, 214
    if name.startswith("strike_"):
        maxw, maxh = 214, 200
    if name.startswith("lights_"):
        maxw, maxh = 170, 95
    if name.startswith("rain_"):
        maxw, maxh = 220, 214
    ratio = min(maxw / im.width, maxh / im.height)
    size = (max(1, round(im.width * ratio)), max(1, round(im.height * ratio)))
    im = im.resize(size, Image.Resampling.NEAREST)
    out = Image.new("RGBA", (CELL, CELL))
    x = (CELL - size[0]) // 2
    y = (CELL - size[1]) // 2
    if name.startswith("spire_"):
        y = 40
    if name.startswith("strike_"):
        y = 20
    out.paste(im, (x, y))
    if atlas == "weather":
        # A stepped edge fade prevents cropped source clouds ending flat.
        p = np.array(out)
        yy, xx = np.mgrid[:CELL, :CELL]
        edge = np.minimum.reduce([xx, yy, CELL - 1 - xx, CELL - 1 - yy])
        rx = (xx - 127.5) / 113
        ry = (yy - 127.5) / 113
        radial = np.clip((1 - np.sqrt(rx * rx + ry * ry)) / .45, 0, 1)
        fade = np.clip((edge - 13) / 52, 0, 1) * radial
        fade = np.floor(fade * 16) / 16
        p[:, :, 3] = (p[:, :, 3].astype(float) * fade).astype(np.uint8)
        out = Image.fromarray(p, "RGBA")
    return out


def add_exact_features(im: Image.Image, name: str) -> Image.Image:
    if name.startswith("spire_"):
        d = ImageDraw.Draw(im)
        if name == "spire_01":
            # A subordinate painted-pylon extension makes this the double-rod
            # variant while the generated central rod remains tallest.
            d.line([(151, 176), (151, 91)], fill=(5, 7, 12, 255), width=7)
            d.line([(151, 175), (151, 92)], fill=(34, 42, 57, 255), width=5)
            d.line([(149, 171), (149, 98)], fill=(89, 103, 122, 255), width=1)
            d.line([(153, 173), (153, 96)], fill=(16, 24, 36, 255), width=1)
            d.line([(128, 170), (150, 151)], fill=(6, 8, 14, 255), width=3)
            d.line([(128, 169), (150, 150)], fill=(54, 65, 82, 255), width=1)
            for yy in (112, 137, 162):
                d.point((150, yy), fill=(134, 153, 180, 255))
            d.rectangle((148, 88, 154, 94), fill=(124, 164, 218, 80))
            d.point((151, 91), fill=(244, 248, 255, 255))
        # Fixed Unity cell top coordinate; pixel-stepped cold tip.
        d.rectangle((125, 37, 131, 43), fill=(102, 147, 206, 65))
        d.rectangle((126, 38, 130, 42), fill=(155, 189, 231, 145))
        d.point((128, 40), fill=(244, 248, 255, 255))
    elif name.startswith("strike_"):
        d = ImageDraw.Draw(im)
        # Only the channel core touches the top edge, at x=128.
        d.line([(128, 0), (128, 13), (126, 16), (128, 20)], fill=(244, 248, 255, 255), width=1)
        d.line([(128, 216), (125, 220), (128, 226)], fill=(244, 248, 255, 255), width=1)
        for dx, dy, al in [(-4, 0, 80), (4, 0, 80), (-2, -2, 155), (2, -2, 155), (0, 2, 90)]:
            d.point((128 + dx, 226 + dy), fill=(165, 197, 238, al))
        d.point((128, 226), fill=(244, 248, 255, 255))
    return im


def role(name: str) -> str:
    return name.rsplit("_", 1)[0]


def piece_notes(name: str) -> str:
    if name.startswith("anvil_tower"):
        return "Flat anvil cap, illuminated thunderhead body, feathered vapour foot."
    if name.startswith("spire"):
        return "Lightning-rod mast on small suspended platform; cold rod-tip glow."
    if name.startswith("skyhook"):
        return "Cable anchor with suspended ballast balloons."
    if name.startswith("refinery_float"):
        return "Floating gas refinery with ballast skirt, stacks, storm-white lamps."
    if name.startswith("cloud_bank"):
        return "Ceiling thunderhead; feathered semi-transparent edge."
    if name.startswith("rain"):
        return "Painted heavy rain sheet; slanted luminous streaks and foot mist."
    if name.startswith("bolt"):
        return "Painted branching channel with thin white core and 3-6 px stepped halo."
    if name.startswith("strike"):
        return "Painted branching strike with fixed top entry and bottom-centre impact."
    if name.startswith("flash"):
        return "Painted white-blue radial bloom, faded to transparent at the cell edge."
    if name.startswith("debris"):
        return "Tumbling dark gunmetal debris with pale upper-left rim light."
    if name.startswith("lights"):
        return "Four-lamp horizontal signal centred at the site emergence point."
    if name.endswith("_open"):
        return "Elite emergence site with lit central opening."
    if name.endswith("_closed"):
        return "Elite emergence site with sealed central hatch."
    return "Painted high-altitude storm structure with cold upper-left rim light."


def build() -> None:
    metadata = []
    previews = []
    for atlas, names in NAMES.items():
        sheet = Image.new("RGBA", (1024, 1024))
        rects = []
        for i, name in enumerate(names):
            piece = source_cell(atlas, i)
            piece = hue_clean(piece, atlas)
            if atlas == "sites" or (atlas == "lightning" and name.startswith("debris")):
                piece = remove_smooth_matte(piece)
            if atlas == "lightning" and name.startswith(("bolt", "strike", "arc")):
                piece = lightning_core_matte(piece)
            if atlas == "lightning" and name.startswith("flash"):
                piece = radial_flash_matte(piece)
            if name == "lights_off":
                p = np.array(piece)
                p[:, :, :3] = (p[:, :, :3].astype(float) * .72).astype(np.uint8)
                piece = Image.fromarray(p, "RGBA")
            piece = alpha_cleanup(piece, atlas, name)
            piece = fit_piece(piece, atlas, name)
            if atlas == "landmarks":
                piece = remove_landmark_islands(piece)
            if atlas == "lightning" and name.startswith(("bolt", "strike", "arc")):
                piece = add_channel_glow(piece)
            piece = add_exact_features(piece, name)
            col, row = i % 4, i // 4
            sheet.paste(piece, (col * CELL, row * CELL))
            rects.append({"n": name, "x": col * CELL, "y": 1024 - (row + 1) * CELL, "w": CELL, "h": CELL})
            alpha = np.array(piece.getchannel("A"))
            item = {
                "atlas": atlas, "name": name, "role": role(name),
                "size_hint": "large" if name.startswith(("anvil", "cloud_bank")) else "small" if name.startswith(("lights", "debris")) else "medium",
                "max_alpha": int(alpha.max()),
                "source_candidate": source_filename(atlas, i),
                "notes": piece_notes(name),
            }
            if name.startswith("spire_"):
                item["rod_tip_from_top"] = [128, 40]
            if name.startswith("strike_"):
                item["strike_end_from_top"] = [128, 226]
            metadata.append(item)
        sheet.save(ROOT / f"{atlas}.png")
        (ROOT / f"{atlas}.json").write_text(json.dumps({"sprites": rects}, indent=2) + "\n")
        previews.append(sheet)
    (ROOT / "manifest_b.json").write_text(json.dumps({"pieces": metadata, "source_choices": SOURCES,
                                                 "source_override": {"lightning debris_00..04": "lightning_candidate_a.png"}}, indent=2) + "\n")

    # Five art-only contact sheets: four on #0b0b1a and a second lightning
    # view over the requested existing cloud tile, converted to mid-grey.
    canvas = Image.new("RGBA", (4096, 6144), BG)
    positions = [(0, 0), (2048, 0), (0, 2048), (2048, 2048)]
    for sheet, pos in zip(previews, positions):
        layer = sheet.resize((2048, 2048), Image.Resampling.NEAREST)
        canvas.alpha_composite(layer, pos)
    ref = Image.open(ROOT.parent.parent.parent / "Backgrounds/Resources/Worlds/Ember/Backdrop3/v2/mid.png").convert("L")
    ref = ref.resize((2048, 2048), Image.Resampling.NEAREST)
    g = np.array(ref).astype(np.float32)
    g = np.clip(64 + g * .32, 62, 139).astype(np.uint8)
    grey = Image.fromarray(np.stack([g, g, g, np.full_like(g, 255)], axis=-1), "RGBA")
    canvas.alpha_composite(grey, (0, 4096))
    lightning_2x = previews[3].resize((2048, 2048), Image.Resampling.NEAREST)
    canvas.alpha_composite(lightning_2x, (0, 4096))
    canvas.save(ROOT / "preview_b.png")


if __name__ == "__main__":
    build()
