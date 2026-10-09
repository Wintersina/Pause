"""Cut the selected painted Tide candidates into Unity-ready Run B atlases.

All shape and texture come from image-generated candidate sheets saved beside this
script. This pass only crops, scales with nearest-neighbour, quantises the palette,
and records placement metadata.
"""
from __future__ import annotations

import colorsys
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
SRC = Path(__file__).resolve().parent
NAMES = {
    "landmarks": [
        "oilrig_00", "oilrig_01", "oilrig_02", "refineryrig_00",
        "refineryrig_01", "crane_gantry_00", "tanker_00", "trawlerbarge_00",
        "trawlerbarge_01", "lighthouse_00", "sunken_city_00", "sunken_city_01",
        "pump_platform_00", "wind_turbine_00", "mooring_buoys_00", "bridge_section_00",
    ],
    "pipes": [
        "pipe_straight_00", "pipe_straight_01", "pipe_bend_00", "pipe_bend_01",
        "pipe_tee_00", "pipe_cross_00", "manifold_00", "manifold_01",
        "pipebridge_00", "pipebridge_01", "pumphouse_00", "pipe_leak_00",
        "pipe_leak_01", "barnacled_intake_00", "pipe_tangle_00", "pipe_tangle_01",
    ],
    "fires": [
        "whirlpool_00", "whirlpool_01", "oil_slick_00", "oil_slick_01",
        "reef_head_00", "reef_head_01", "wreck_hull_00", "wreck_hull_01",
        "wreck_hull_02", "bubbling_vent_00", "bubbling_vent_01", "flare_stack_00",
        "flare_stack_01", "plankton_bloom_00", "plankton_bloom_01", "salvage_crawler_00",
    ],
    "weather": [
        "cloud_bank_00", "cloud_bank_01", "cloud_bank_02", "cloud_bank_03",
        "cloud_wisp_00", "cloud_wisp_01", "cloud_wisp_02", "cloud_wisp_03",
        "mist_00", "mist_01", "mist_02", "gust_00",
        "gust_01", "gust_02", "pall_00", "rain_00",
    ],
    "sites": [
        "trench_hatch_closed", "trench_hatch_open", "rig_bay_closed", "rig_bay_open",
        "reef_dock_closed", "reef_dock_open", "vent_stack_closed", "vent_stack_open",
        "wreck_bay_closed", "wreck_bay_open", "lights_off", "lights_on",
    ],
}

# Each atlas has two painted candidates. Choose coherent A layouts while taking
# smaller foam bases and the stronger paired site states from B.
USE_B = {
    "landmarks": {10, 11},
    "pipes": {2, 3, 8, 9, 13},
    "fires": {2, 3, 6, 7, 8, 15},
    "weather": {1, 3, 5, 7, 10, 12, 14},
    "sites": set(range(12)),
}

RAMPS = {
    "ink": [(5, 16, 14), (9, 26, 23)],
    "teal": [(5, 17, 14), (9, 29, 25), (15, 43, 37), (24, 61, 53), (36, 80, 69), (50, 102, 88)],
    "rust": [(21, 18, 16), (36, 29, 24), (53, 41, 32), (73, 55, 42), (95, 71, 54), (120, 90, 69)],
    "mint": [(12, 41, 30), (20, 67, 47), (35, 105, 72), (57, 147, 101), (88, 194, 138), (124, 229, 173)],
    "violet": [(18, 26, 46), (28, 41, 72), (42, 61, 107), (62, 88, 150), (87, 123, 192)],
    "slate": [(17, 29, 26), (27, 41, 37), (43, 60, 57), (65, 85, 80), (91, 114, 107), (128, 155, 146)],
    "foam": [(30, 63, 56), (48, 91, 80), (73, 123, 107), (105, 159, 139), (143, 194, 171)],
}

EMITTERS = {
    "oilrig_00": [("smoke", .43, .11)], "oilrig_01": [("smoke", .74, .14)],
    "oilrig_02": [("smoke", .39, .11)],
    "refineryrig_00": [("smoke", .32, .09)],
    "refineryrig_01": [("smoke", .31, .13), ("smoke", .43, .16)],
    "lighthouse_00": [("beacon", .40, .13)],
    "pump_platform_00": [("beacon", .43, .12)],
    "pipe_leak_00": [("bubbles", .51, .37)],
    "pipe_leak_01": [("bubbles", .56, .37)],
    "pumphouse_00": [("steam", .36, .18)],
    "bubbling_vent_00": [("bubbles", .51, .51)],
    "bubbling_vent_01": [("bubbles", .51, .51)],
    "flare_stack_00": [("cool_flare", .37, .09), ("smoke", .37, .09)],
    "flare_stack_01": [("cool_flare", .47, .09), ("smoke", .47, .09)],
    "salvage_crawler_00": [("bubbles", .53, .51)],
}
ENDS = {
    "pipe_straight_00": [(20, 109), (236, 109)],
    "pipe_straight_01": [(128, 20), (128, 236)],
    "pipe_bend_00": [(28, 52), (166, 220)],
    "pipe_bend_01": [(75, 24), (215, 128)],
    "pipe_tee_00": [(20, 113), (236, 113), (130, 50)],
    "pipe_cross_00": [(20, 113), (236, 113), (130, 27), (130, 220)],
    "manifold_00": [(20, 108), (236, 108)],
    "manifold_01": [(20, 128), (236, 128)],
    "pipebridge_00": [(20, 100), (236, 100)],
    "pipebridge_01": [(40, 38), (216, 166)],
    "pumphouse_00": [(21, 151), (220, 154)],
    "pipe_leak_00": [(20, 124), (236, 124)],
    "pipe_leak_01": [(66, 29), (143, 95)],
    "barnacled_intake_00": [(80, 165), (215, 55)],
    "pipe_tangle_00": [(22, 150), (220, 142), (125, 25)],
    "pipe_tangle_01": [(22, 168), (228, 151), (80, 29)],
}


def ramp_color(rgb: np.ndarray, atlas: str) -> np.ndarray:
    flat = rgb.reshape(-1, 3).astype(np.float32) / 255
    hi = flat.max(axis=1)
    lo = flat.min(axis=1)
    delta = hi - lo
    sat = np.divide(delta, hi, out=np.zeros_like(hi), where=hi > 0)
    hue = np.zeros_like(hi)
    nz = delta > 1e-5
    red, green, blue = flat.T
    m = nz & (hi == red)
    hue[m] = ((green[m] - blue[m]) / delta[m]) % 6
    m = nz & (hi == green)
    hue[m] = (blue[m] - red[m]) / delta[m] + 2
    m = nz & (hi == blue)
    hue[m] = (red[m] - green[m]) / delta[m] + 4
    hue *= 60

    families = np.full(len(flat), "teal", dtype="<U6")
    families[(hue >= 5) & (hue < 75) & (sat >= .13)] = "rust"
    families[(hue >= 198) & (hue < 295) & (sat >= .16)] = "violet"
    mint = (hue >= 125) & (hue < 198) & (sat >= .31) & (hi >= .30)
    families[mint] = "mint"
    families[sat < .17] = "slate"
    families[(sat < .28) & (hi > .42) & (hue >= 120) & (hue <= 205)] = "foam"
    families[hi < .105] = "ink"
    if atlas == "weather":
        families[:] = "slate"
        families[mint & (hi > .46)] = "mint"

    out = np.zeros((len(flat), 3), np.uint8)
    target_v = hi * (.82 if atlas == "weather" else .76)
    for family, ramp in RAMPS.items():
        mask = families == family
        if not mask.any():
            continue
        colors = np.array(ramp, dtype=np.uint8)
        vals = colors.max(axis=1) / 255
        index = np.abs(target_v[mask, None] - vals[None, :]).argmin(axis=1)
        out[mask] = colors[index]
    return out.reshape(rgb.shape)


def selected_cell(atlas: str, index: int) -> tuple[Image.Image, str]:
    variant = "b" if index in USE_B[atlas] else "a"
    im = Image.open(SRC / f"candidate_b_{atlas}_{variant}.png").convert("RGBA")
    col, row = index % 4, index // 4
    w, h = im.size
    if atlas == "sites":
        # The generator used three filled rows in a square canvas, leaving the
        # fourth quarter blank; these are the actual painted row gutters.
        row_edges = (0, 365, 705, 1030)
        box = (round(col*w/4), row_edges[row], round((col+1)*w/4), row_edges[row+1])
    else:
        box = (round(col*w/4), round(row*h/4), round((col+1)*w/4), round((row+1)*h/4))
    cell = im.crop(box)
    if atlas in {"landmarks", "pipes", "sites"}:
        # Candidate sheets sometimes carry detached pixels from an adjacent row
        # into a nominal cell. Keep the dominant projected object span per axis.
        data = np.array(cell)
        for axis in (0, 1):
            projection = np.count_nonzero(data[:, :, 3] >= 65, axis=1-axis)
            active = projection >= 4
            groups = []
            start = None
            for k in range(len(active)+1):
                on = k < len(active) and active[k]
                if on and start is None:
                    start = k
                if start is not None and not on:
                    groups.append((start, k, int(projection[start:k].sum())))
                    start = None
            if groups:
                main = max(groups, key=lambda g: g[2])
                for start, stop, weight in groups:
                    gap = max(main[0]-stop, start-main[1], 0)
                    if (start, stop) != main[:2] and (gap >= 5 and weight < main[2]*.25 or
                                                           gap >= 3 and weight < main[2]*.10):
                        if axis == 0:
                            data[start:stop, :, :] = 0
                        else:
                            data[:, start:stop, :] = 0
        cell = Image.fromarray(data, "RGBA")
    alpha = np.array(cell)[:, :, 3]
    threshold = 25 if atlas == "weather" else 65
    ys, xs = np.nonzero(alpha >= threshold)
    if not len(xs):
        raise ValueError(f"Empty generated cell: {atlas} {index}")
    inset = 1
    bbox = (max(0, int(xs.min())-inset), max(0, int(ys.min())-inset),
            min(cell.width, int(xs.max())+inset+1), min(cell.height, int(ys.max())+inset+1))
    return cell.crop(bbox), variant


def clean_piece(piece: Image.Image, atlas: str) -> Image.Image:
    max_extent = 226 if atlas == "weather" else 224
    scale = min(max_extent/piece.width, max_extent/piece.height)
    size = (max(1, round(piece.width*scale)), max(1, round(piece.height*scale)))
    piece = piece.resize(size, Image.Resampling.NEAREST)
    arr = np.array(piece)
    arr[:, :, :3] = ramp_color(arr[:, :, :3], atlas)
    if atlas == "weather":
        a = arr[:, :, 3].astype(np.float32)
        # Storm ceiling is noticeable at level start but remains see-through.
        a = np.where(a < 24, 0, np.minimum(105, a*.41))
        arr[:, :, 3] = np.round(a/15)*15
    else:
        a = arr[:, :, 3]
        arr[:, :, 3] = np.where(a < 65, 0, np.where(a < 160, 128, 255))
    arr[arr[:, :, 3] == 0, :3] = 0
    return Image.fromarray(arr, "RGBA")


def build() -> None:
    records = []
    for atlas, names in NAMES.items():
        sheet = Image.new("RGBA", (1024, 1024))
        sprites = []
        for i, name in enumerate(names):
            raw, variant = selected_cell(atlas, i)
            piece = clean_piece(raw, atlas)
            if name == "wind_turbine_00":
                # Pale blades otherwise push the landmark's p90 over 0.55.
                blade = np.array(piece)
                blade[:, :, :3] = (blade[:, :, :3].astype(np.float32)*.88).astype(np.uint8)
                off_hue = np.all(blade[:, :, :3] == (37, 52, 50), axis=2)
                blade[off_hue, 2] = 48
                piece = Image.fromarray(blade, "RGBA")
            col, row = i % 4, i // 4
            dx, dy = (256-piece.width)//2, (256-piece.height)//2
            sheet.alpha_composite(piece, (col*256+dx, row*256+dy))
            sprites.append({"n": name, "x": col*256, "y": (3-row)*256, "w": 256, "h": 256})
            emit = [{"kind": kind, "x": round(x*256), "y": round(y*256)}
                    for kind, x, y in EMITTERS.get(name, [])]
            if atlas == "sites" and name.endswith("_open"):
                emit.append({"kind": "elite_emergence", "x": 128, "y": 128})
            records.append({
                "atlas": atlas, "name": name,
                "role": {"landmarks":"Landmark", "pipes":"Pipe", "fires":"Feature",
                         "weather":"Cloud" if i < 8 else "Atmosphere", "sites":"Site"}[atlas],
                "emit": emit,
                "ends": [{"x": x, "y": y} for x, y in ENDS.get(name, [])],
                "size_hint": "large" if atlas == "landmarks" or (atlas == "weather" and i < 4) else "medium",
                "notes": ("Storm ceiling; low-alpha overlay" if atlas == "weather" and i < 4 else
                          "Transparent overlay; keep bullets visible" if atlas == "weather" else
                          "Small foam fringe at contact points" if atlas in {"landmarks", "fires"} else
                          "Elite emerges at cell centre" if atlas == "sites" else
                          "Connector coordinates are nominal placement guides"),
                "painted_candidate": variant,
            })
        sheet.save(ROOT/f"{atlas}.png", optimize=True)
        (ROOT/f"{atlas}.json").write_text(json.dumps({"sprites": sprites}, indent=2)+"\n")

    manifest_path = ROOT/"manifest.json"
    manifest = json.loads(manifest_path.read_text())
    manifest["run_b"] = {
        "source": "Built-in image generation; two inspected painted candidates per atlas, selected and nearest-neighbour pixel-cleaned",
        "point_space": "Cell-local pixels, top-left origin; atlas JSON rect y is measured from bottom",
        "pieces": records,
    }
    manifest_path.write_text(json.dumps(manifest, indent=2)+"\n")

    # Reviewable contact sheet and two portrait composites at 1:1 atlas scale.
    panel = Image.new("RGB", (2220, 1390), (10, 25, 28))
    draw = ImageDraw.Draw(panel)
    for i, atlas in enumerate(NAMES):
        sh = Image.open(ROOT/f"{atlas}.png")
        sh.thumbnail((540, 540), Image.Resampling.NEAREST)
        bg = Image.new("RGBA", sh.size, (9, 25, 26, 255)); bg.alpha_composite(sh)
        x = (i%4)*550+6; y = (i//4)*635+34
        panel.paste(bg.convert("RGB"), (x, y))
        draw.text((x, y-21), atlas.upper(), fill=(192, 230, 212))
    for j, variant in enumerate((1, 2)):
        bg = Image.open(ROOT/f"v{variant}"/"mid.png").convert("RGBA").resize((310, 620), Image.Resampling.NEAREST)
        x0, y0 = 1130+j*420, 670
        phone = Image.new("RGBA", (350, 680), (3, 8, 10, 255)); phone.alpha_composite(bg, (20, 30))
        placements = [
            ("landmarks", 0, 23, 110, 145), ("landmarks", 6, 158, 360, 104),
            ("pipes", 8, 28, 307, 130), ("fires", 9, 215, 208, 92),
            ("sites", 1, 83, 480, 115), ("weather", 0, 12, 8, 270),
        ]
        for atlas, idx, xx, yy, width in placements:
            sh = Image.open(ROOT/f"{atlas}.png")
            col, row = idx%4, idx//4
            sprite = sh.crop((col*256, row*256, col*256+256, row*256+256))
            sprite = sprite.resize((width, width), Image.Resampling.NEAREST)
            phone.alpha_composite(sprite, (xx, yy))
        panel.paste(phone.convert("RGB"), (x0, y0))
        draw.text((x0, y0-21), f"PHONE COMPOSITE — v{variant}/mid", fill=(192, 230, 212))
    panel.save(ROOT/"preview_b.png")
    print(f"Wrote 5 atlases, 5 JSON files, {len(records)} manifest pieces, preview_b.png")


if __name__ == "__main__":
    build()
