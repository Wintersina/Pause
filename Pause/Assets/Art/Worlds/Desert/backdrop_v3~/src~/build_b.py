#!/usr/bin/env python3
"""Compose the generated Desert run-B sprite sources into fixed Unity atlases."""
from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
SRC = Path(__file__).resolve().parent
CELL = 256
SPECS = {
    "landmarks": (4, [
        "refinery_hull_00", "refinery_hull_01", "ribcage_gantry_00", "ribcage_gantry_01",
        "drill_head_tower", "solar_array_skeleton_00", "solar_array_skeleton_01", "crawler_caravan_00",
        "crawler_caravan_01", "radio_mast", "collapsed_dome_cluster", "bucket_wheel_excavator",
        "sunken_cargo_crawler", "fused_glass_crater_rim", "cliff_arch", "pump_well_station"]),
    "pipes": (4, [
        "straight_00", "straight_01", "bend_00", "bend_01",
        "tee", "cross", "manifold_00", "manifold_01",
        "pipe_bridge_00", "pipe_bridge_01", "pump_house", "leak_00",
        "leak_01", "half_buried_pipe", "tangle_00", "tangle_01"]),
    "fires": (4, [
        "rock_fin_00", "rock_fin_01", "wall_drift_00", "wall_drift_01",
        "fulgurite_00", "fulgurite_01", "dry_well_00", "dry_well_01",
        "machine_ribs_00", "machine_ribs_01", "heat_vent_00", "heat_vent_01",
        "crack_scar", "dune_ridge", "wreckage", "lone_crawler"]),
    "weather": (4, [
        "cloud_bank_00", "cloud_bank_01", "cloud_bank_02", "cloud_bank_03",
        "cloud_wisp_00", "cloud_wisp_01", "cloud_wisp_02", "cloud_wisp_03",
        "mist_00", "mist_01", "gust_00", "gust_01",
        "pall_00", "pall_01", "particle_00", "mist_02"]),
    "sites": (3, [
        "worm_burrow_closed", "worm_burrow_open", "crawler_bay_closed", "crawler_bay_open",
        "glass_pad_closed", "glass_pad_open", "buried_silo_closed", "buried_silo_open",
        "ruin_dock_closed", "ruin_dock_open", "lights_off", "lights_on"]),
}

SOURCE = {"fires": "features", **{k: k for k in SPECS if k != "fires"}}
ROLE = {"landmarks": "Landmark", "pipes": "Pipe", "fires": "Feature", "weather": "Cloud", "sites": "Site"}


def clean_colors(im: Image.Image, atlas: str, name: str) -> Image.Image:
    a = np.asarray(im.convert("RGBA")).copy()
    alpha = a[:, :, 3]
    # Image generation left red/yellow almost-transparent fringing around silhouettes.
    # Its alpha is below 40, so remove it before nearest-neighbour resampling.
    alpha[alpha < (13 if atlas == "weather" else 40)] = 0
    hsv = np.asarray(Image.fromarray(a[:, :, :3], "RGB").convert("HSV")).copy()
    h = hsv[:, :, 0].astype(np.float32) * (360.0 / 255.0)
    s = hsv[:, :, 1].astype(np.float32) / 255.0
    v = hsv[:, :, 2].astype(np.float32) / 255.0
    visible = alpha > 0
    forbidden_red = visible & ((h <= 15) | (h >= 345)) & (s > .5)
    alpha[forbidden_red] = 0

    if atlas == "weather":
        # Only low-alpha clusters survive. Ceiling sprites share the descent
        # deck's dusty brown/indigo values after a value reduction.
        warm = (h >= 15) & (h <= 80)
        h[warm] = 44
        s[warm] = np.minimum(s[warm], .22)
        cool = (h >= 210) & (h <= 300)
        h[cool] = 238
        s[cool] = np.minimum(s[cool], .37)
        odd = (~warm) & (~cool)
        s[odd] = np.minimum(s[odd], .20)
        v *= .68
        cap = 72 if name.startswith("cloud_bank") else 60
        if name.startswith("particle"):
            cap = 75
        alpha[:] = (np.ceil((alpha.astype(np.float32) / 255.0 * cap) / 8) * 8).clip(0, cap).astype(np.uint8)
        alpha[alpha < 16] = 0
    else:
        sand = (h >= 30) & (h <= 70) & (s > .07)
        source_v = v.copy()
        h[sand] = 44
        s[sand] = np.minimum(s[sand], .29)
        copper = (h >= 15) & (h < 32) & (s >= .23)
        h[copper] = np.clip(h[copper], 22, 30)
        s[copper] = np.minimum(s[copper], .59)
        blue = (h >= 210) & (h < 300) & (s > .1)
        h[blue] = 238
        s[blue] = np.minimum(s[blue], .40)
        magenta = (h >= 250) & (h <= 345) & (s > .07)
        h[magenta] = 238
        s[magenta] = np.minimum(s[magenta], .38)
        green = (h >= 90) & (h < 210) & (s > .18)
        h[green] = np.where(h[green] < 160, 150, 167)
        s[green] = np.minimum(s[green], .25)
        gold = (h >= 30) & (h <= 48)
        s[gold] = np.minimum(s[gold], .32)
        # Keep the content darker than gameplay materials while retaining
        # small white-hot lamps and steam tips.
        v *= .63
        v[copper] = np.minimum(v[copper], .60)
        light = (a[:, :, :3].min(axis=2) > 225) & (alpha > 180)
        if name in ("lights_on", "leak_00", "leak_01", "pump_well_station"):
            h[light], s[light], v[light] = 44, .22, .96
        # Solid material pixels use full alpha, with a small soft sand skirt.
        if atlas in ("landmarks", "pipes", "sites"):
            base = sand & (source_v > .45) & (alpha > 0)
            structure = (alpha > 80) & ~base
            near = np.asarray(Image.fromarray((structure * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(27))) > 0
            alpha[base & ~near] = 0
        alpha[alpha >= 40] = 255
        if atlas in ("landmarks", "pipes", "sites"):
            alpha[base & near] = 110
    hsv[:, :, 0] = np.rint(h / 360 * 255).clip(0, 255).astype(np.uint8)
    hsv[:, :, 1] = np.rint(s * 255).clip(0, 255).astype(np.uint8)
    hsv[:, :, 2] = np.rint(v * 255).clip(0, 255).astype(np.uint8)
    rgb = np.asarray(Image.fromarray(hsv, "HSV").convert("RGB"))
    # A final HSV pass catches a few round-trip edge hues in generated grain.
    final_hsv = np.asarray(Image.fromarray(rgb, "RGB").convert("HSV")).copy()
    fh = final_hsv[:, :, 0].astype(np.float32) * (360.0 / 255.0)
    fs = final_hsv[:, :, 1].astype(np.float32) / 255.0
    fv = final_hsv[:, :, 2].astype(np.float32) / 255.0
    purple = (fh >= 250) & (fh <= 345)
    final_hsv[:, :, 0][purple] = round(235 / 360 * 255)
    final_hsv[:, :, 1][purple] = np.minimum(final_hsv[:, :, 1][purple], 90)
    red = ((fh <= 15) | (fh >= 345)) & (fs > .5)
    final_hsv[:, :, 0][red] = round(26 / 360 * 255)
    final_hsv[:, :, 1][red] = np.minimum(final_hsv[:, :, 1][red], 140)
    gold = (fh >= 30) & (fh <= 48) & (fs > .45)
    final_hsv[:, :, 1][gold] = 78
    vivid_cyan = (fh >= 155) & (fh <= 205) & (fs > .4)
    final_hsv[:, :, 0][vivid_cyan] = round(167 / 360 * 255)
    final_hsv[:, :, 1][vivid_cyan] = 58
    vivid_green = (fh >= 60) & (fh <= 155) & (fs > .4)
    final_hsv[:, :, 0][vivid_green] = round(150 / 360 * 255)
    final_hsv[:, :, 1][vivid_green] = 65
    matte_copper = (fh >= 18) & (fh <= 34) & (fs > .28)
    final_hsv[:, :, 1][matte_copper] = np.minimum(final_hsv[:, :, 1][matte_copper], 150)
    final_hsv[:, :, 2][matte_copper] = np.minimum(final_hsv[:, :, 2][matte_copper], 145)
    rgb = np.asarray(Image.fromarray(final_hsv, "HSV").convert("RGB"))
    out = np.dstack((rgb, alpha))
    out[alpha == 0] = 0
    return Image.fromarray(out, "RGBA")


def extract_cell(source: Image.Image, atlas: str, name: str, index: int, rows: int) -> Image.Image:
    w, h = source.size
    col, row = index % 4, index // 4
    box = (round(col * w / 4), round(row * h / rows), round((col + 1) * w / 4), round((row + 1) * h / rows))
    piece = clean_colors(source.crop(box), atlas, name)
    arr = np.asarray(piece).copy()
    # If generated parts reach a source cell edge, fade them in discrete
    # pixel steps; the actual atlas still has a fully clear margin.
    hh, ww = arr.shape[:2]
    yy, xx = np.indices((hh, ww))
    edge = np.minimum.reduce((xx, yy, ww - 1 - xx, hh - 1 - yy))
    if atlas == "weather":
        factor = np.clip(edge / 13.0, 0, 1)
        arr[:, :, 3] = (np.floor(arr[:, :, 3] * factor / 8) * 8).astype(np.uint8)
    else:
        arr[:, :, 3][edge < 2] = 0
    piece = Image.fromarray(arr, "RGBA")
    bbox = piece.getchannel("A").getbbox()
    if bbox is None:
        raise ValueError(f"empty generated cell {atlas}:{name}")
    piece = piece.crop(bbox)
    # Author at half size and upscale exactly x2 with nearest-neighbour.
    target = 112 if atlas == "weather" else 110
    scale = min(target / piece.width, target / piece.height)
    small = piece.resize((max(1, round(piece.width * scale)), max(1, round(piece.height * scale))), Image.Resampling.NEAREST)
    # Padding to even half-resolution dimensions keeps every 2x block aligned
    # to the full 256-pixel cell grid after centering.
    if small.width % 2 or small.height % 2:
        even = Image.new("RGBA", (small.width + small.width % 2, small.height + small.height % 2))
        even.alpha_composite(small)
        small = even
    sprite = small.resize((small.width * 2, small.height * 2), Image.Resampling.NEAREST)
    cell = Image.new("RGBA", (CELL, CELL))
    cell.alpha_composite(sprite, ((CELL - sprite.width) // 2, (CELL - sprite.height) // 2))
    return cell


def points_for(atlas: str, name: str):
    if atlas == "pipes":
        sides = {
            "straight_00": ["left", "right"], "straight_01": ["left", "right"],
            "bend_00": ["left", "bottom"], "bend_01": ["left", "bottom"],
            "tee": ["left", "right", "bottom"], "cross": ["left", "right", "top", "bottom"],
            "manifold_00": ["left", "right"], "manifold_01": ["left", "right"],
            "pipe_bridge_00": ["left", "right"], "pipe_bridge_01": ["left", "right"],
            "pump_house": ["left", "right"], "leak_00": ["left", "right"],
            "leak_01": ["left", "right"], "half_buried_pipe": ["left", "right"],
            "tangle_00": ["left", "right", "bottom"], "tangle_01": ["left", "right", "bottom"],
        }[name]
        xy = {"left": [20, 128], "right": [236, 128], "top": [128, 20], "bottom": [128, 236]}
        return {"pipe_ends": [{"side": side, "xy": xy[side]} for side in sides],
                "emitter": [128, 108] if name.startswith("leak") else ([128, 128] if name == "pump_house" else None)}
    if atlas == "sites":
        return {"emitter": [128, 128] if name.endswith("_open") or name == "lights_on" else None}
    if name.startswith("leak") or name.startswith("heat_vent") or name in ("pump_well_station", "dry_well_00", "dry_well_01"):
        return {"emitter": [128, 128]}
    return {"emitter": None}


def build():
    manifest_path = ROOT / "manifest.json"
    manifest = json.loads(manifest_path.read_text())
    records = []
    for atlas, (rows, names) in SPECS.items():
        source = Image.open(SRC / f"{SOURCE[atlas]}_source_a.png").convert("RGBA")
        canvas = Image.new("RGBA", (1024, 1024))
        rects = []
        for i, name in enumerate(names):
            cell = extract_cell(source, atlas, name, i, rows)
            col, row = i % 4, i // 4
            canvas.alpha_composite(cell, (col * CELL, row * CELL))
            rects.append({"n": name, "x": col * CELL, "y": 768 - row * CELL, "w": CELL, "h": CELL})
            points = points_for(atlas, name)
            if name.startswith("cloud_"):
                role = "Cloud"
            elif atlas == "weather":
                role = "Atmosphere"
            else:
                role = ROLE[atlas]
            records.append({"atlas": f"{atlas}.png", "name": name, "role": role,
                            **points, "size_hint": "large" if atlas == "landmarks" else ("low overlay" if atlas == "weather" else "medium"),
                            "notes": "Generated painted source; nearest-neighbour x2; centered clear margin. " +
                            ("Pale white steam emitter." if name.startswith("leak") else
                             "Dark central emergence point." if name.endswith("_open") else
                             "Sparse dusty haze." if atlas == "weather" else
                             "Matte weathered form with a small contact sand skirt.")})
        canvas.save(ROOT / f"{atlas}.png")
        (ROOT / f"{atlas}.json").write_text(json.dumps({"sprites": rects}, indent=2) + "\n")
    manifest["run_b"] = {"sprites": records, "source": "Built-in image generation candidates in src~; source A selected for all five atlas families.",
                         "note": "Weather has 16 physical cells: three mist and two gust entries because the requested inclusive ranges total 17."}
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    preview = Image.new("RGBA", (3072, 2048), (11, 11, 26, 255))
    for i, atlas in enumerate(SPECS):
        sheet = Image.open(ROOT / f"{atlas}.png")
        preview.alpha_composite(sheet, ((i % 3) * 1024, (i // 3) * 1024))
    # Two phone-width views use actual v1/v3 mid tiles, with placed sprites.
    for j, variant in enumerate(("v1", "v3")):
        phone = Image.open(ROOT / variant / "mid.png").convert("RGBA")
        placements = (
            [("landmarks", 0, 102, 76), ("pipes", 8, 240, 438), ("fires", 5, 18, 696), ("sites", 1, 235, 744)],
            [("landmarks", 5, 24, 95), ("pipes", 9, 244, 440), ("fires", 4, 14, 710), ("sites", 5, 244, 756)],
        )[j]
        for atlas, i, x, y in placements:
            sheet = Image.open(ROOT / f"{atlas}.png")
            sprite = sheet.crop(((i % 4) * 256, (i // 4) * 256, (i % 4 + 1) * 256, (i // 4 + 1) * 256))
            phone.alpha_composite(sprite, (x, y))
        cloud = Image.open(ROOT / "weather.png").crop((0, 0, 256, 256))
        phone.alpha_composite(cloud, (200, 280))
        preview.alpha_composite(phone, (2048 + j * 512, 1024))
    preview.convert("RGB").save(ROOT / "preview_b.png")


if __name__ == "__main__":
    build()
