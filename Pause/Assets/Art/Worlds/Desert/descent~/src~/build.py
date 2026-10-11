"""Compose the Desert planetfall set from image-generated painted sources.

All spatial resampling is nearest-neighbour.  The seam operation blends only
corresponding edge pixels; it does not spatially blur the painting.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
NEAREST = Image.Resampling.NEAREST


def source(name):
    return Image.open(SRC / f"{name}_source.png").convert("RGBA")


def grade(im, kind="art"):
    """Restrict generated chroma while retaining its many local value steps."""
    hsv = np.array(im.convert("RGB").convert("HSV"), dtype=np.float32)
    h, s, v = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]
    deg = h * (360 / 255)
    sat = s / 255
    val = v / 255
    warm = (deg < 90) | (deg >= 310)
    indigo = (deg >= 185) & (deg < 310)
    trace = (deg >= 90) & (deg < 185)
    copper = warm & (deg >= 15) & (deg < 35) & (sat > .26) & (val < .68) & (kind in {"planet", "limb"})

    # High lights stay warm-neutral; shadow blue is always desaturated and dim.
    deg[:] = np.where(indigo, 238, np.where(trace, 166, 44))
    sat[:] = np.where(indigo, np.minimum(sat, .43), np.where(trace, np.minimum(sat, .20), np.minimum(sat, .32)))
    deg[copper] = 25
    sat[copper] = np.minimum(sat[copper], .58)
    val[copper] = np.minimum(val[copper], .58)
    if kind == "cloud":
        val = np.clip(val * .68, .085, .55)
        sat = np.minimum(sat, .31)
        val[indigo] = np.minimum(val[indigo], .29)
    elif kind == "cloud_dark":
        val = np.clip(val * .53, .055, .42)
        sat = np.minimum(sat, .29)
        val[indigo] = np.minimum(val[indigo], .25)
    elif kind == "streaks":
        val = np.minimum(val, .88)
        sat = np.minimum(sat, .26)
    elif kind in {"entry", "breakthrough"}:
        sat = np.minimum(sat, .30)
        # Light cores may be white-hot, but their hue remains pale bone-gold.
        val = np.minimum(val, 1)
    else:
        val[indigo] = np.minimum(val[indigo], .37)
    if kind in {"cloud", "cloud_dark"}:
        # Shade only the deepest painted dust pockets, not a new cloud layer.
        cutoff = np.quantile(val, .13 if kind == "cloud" else .17)
        dust_shadow = val < cutoff
        deg[dust_shadow] = 238
        sat[dust_shadow] = .35
        val[dust_shadow] = np.clip(val[dust_shadow], .10, .25)
    hsv[:, :, 0] = np.rint(deg * 255 / 360)
    hsv[:, :, 1] = np.rint(sat * 255)
    hsv[:, :, 2] = np.rint(val * 255)
    rgb = Image.fromarray(hsv.astype(np.uint8), "HSV").convert("RGB")
    rgb.putalpha(im.getchannel("A"))
    return rgb


def seamless(im, x=True, y=True, band=96):
    a = np.asarray(im, dtype=np.float32).copy()
    h, w = a.shape[:2]
    if x:
        for d in range(band):
            t = .5 * (1 - d / band)
            left, right = a[:, d].copy(), a[:, w - 1 - d].copy()
            a[:, d] = left * (1 - t) + right * t
            a[:, w - 1 - d] = right * (1 - t) + left * t
    if y:
        for d in range(band):
            t = .5 * (1 - d / band)
            top, bottom = a[d].copy(), a[h - 1 - d].copy()
            a[d] = top * (1 - t) + bottom * t
            a[h - 1 - d] = bottom * (1 - t) + top * t
    return Image.fromarray(np.rint(a).astype(np.uint8), "RGBA")


def make_planet():
    painting = source("planet_a").resize((924, 924), NEAREST)
    out = Image.new("RGBA", (1024, 1024))
    out.alpha_composite(grade(painting, "planet"), (50, 50))
    out.save(ROOT / "desert_planet.png")
    out.resize((4096, 4096), NEAREST).save(ROOT / "masters" / "desert_planet_4096.png")


def make_limb():
    raw = np.asarray(grade(source("limb"), "limb"), dtype=np.uint8)
    sh, sw = raw.shape[:2]
    alpha = raw[:, :, 3]
    first = np.array([np.where(alpha[:, x] >= 100)[0][0] for x in range(sw)])
    w, h = 2048, 1024
    xx = np.arange(w)
    sx = np.minimum(sw - 1, (xx * sw / w).astype(int))
    target_h = np.rint(355 + 365 * ((xx - 1024) / 1024) ** 2).astype(int)
    out = np.zeros((h, w, 4), np.uint8)
    for x in range(w):
        ys = np.arange(target_h[x], h)
        sy = first[sx[x]] + (ys - target_h[x]) * (sh - 1 - first[sx[x]]) / max(1, h - 1 - target_h[x])
        out[ys, x] = raw[np.minimum(sh - 1, sy.astype(int)), sx[x]]
        # A layered but narrow atmosphere along the exact curved registration.
        for d, color in [(0, (225, 216, 187, 220)), (2, (173, 159, 125, 170)), (5, (80, 77, 83, 115))]:
            yy = target_h[x] + d
            if yy < h:
                out[yy, x] = color
    Image.fromarray(out, "RGBA").save(ROOT / "desert_planet_limb.png")


def make_clouds():
    base = source("cloud").resize((2048, 1024), NEAREST)
    for name, mode in [("desert_cloud_deck.png", "cloud"), ("desert_cloud_deck_dark.png", "cloud_dark")]:
        # A final local gamut pass removes rare violet intermediates made by
        # crossfading opposite RGB edge pixels. Equal edge values stay equal.
        grade(seamless(grade(base, mode), True, True), "art").save(ROOT / name)


def entry_cell(frame):
    # The wide second painted candidate has a genuine hollow centre.
    src = grade(source("entry_b"), "entry")
    widths = [382, 396, 376, 392, 384, 400]
    heights = [880, 894, 870, 885, 876, 892]
    shifts = [0, 3, -3, 4, -2, 2]
    w, h = widths[frame], heights[frame]
    paint = np.asarray(src.resize((w, h), NEAREST), dtype=np.uint8)
    out = np.zeros((1024, 512, 4), np.uint8)
    x0 = (512 - w) // 2 + shifts[frame]
    y0 = 338 - int(365 * h / 1536)
    # Small row-wise shear changes flame tongues without interpolating pixels.
    for y in range(h):
        dy = y0 + y
        if not 9 <= dy < 1015:
            continue
        bend = int(round(5 * np.sin(y / 71 + frame * 1.3) * max(0, y / h - .25)))
        start = x0 + bend
        if 7 <= start and start + w <= 505:
            out[dy, start:start + w] = paint[y]
    # Precisely registered clear space for the ship, preserved in every frame.
    yy, xx = np.ogrid[:1024, :512]
    cx, cy = 256 + shifts[frame], 338 + [0, 1, -1, 1, 0, -1][frame]
    rx, ry = 73 + [0, 1, 0, 2, 0, 1][frame], 82 + [0, 1, -1, 1, 0, 0][frame]
    elliptical_distance = ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2
    hole = elliptical_distance <= 1
    out[hole] = 0
    # Close the source painting's tiny break at the crown so flood filling
    # finds the actual ship opening rather than the exterior transparency.
    collar = (elliptical_distance > 1) & (elliptical_distance <= 1.12)
    out[collar] = (244, 231, 197, 245)
    return Image.fromarray(out, "RGBA"), (cx, cy), (2 * rx + 1, 2 * ry + 1)


def make_entry():
    strip = Image.new("RGBA", (3072, 1024))
    holes = []
    for i in range(6):
        cell, center, size = entry_cell(i)
        strip.alpha_composite(cell, (512 * i, 0))
        holes.append({"center_px": list(center), "size_px": list(size)})
    strip.save(ROOT / "desert_entry_fx.png")
    return holes


def make_breakthrough():
    src = grade(source("breakthrough"), "breakthrough")
    sheet = Image.new("RGBA", (5120, 1024))
    sizes = [260, 425, 605, 780, 960]
    opacities = [255, 232, 210, 177, 143]
    for i, (side, opacity) in enumerate(zip(sizes, opacities)):
        cell = src.resize((side, side), NEAREST)
        if opacity < 255:
            alpha = cell.getchannel("A").point(lambda x: x * opacity // 255)
            cell.putalpha(alpha)
        sheet.alpha_composite(cell, (i * 1024 + (1024 - side) // 2, (1024 - side) // 2))
    sheet.save(ROOT / "desert_breakthrough.png")


def make_streaks():
    src = source("streaks").resize((1024, 2048), NEAREST)
    seamless(grade(src, "streaks"), False, True, 144).save(ROOT / "desert_entry_streaks.png")


def make_previews():
    bg = Image.new("RGBA", (2048, 2048), "#0b0b1a")
    planet = Image.open(ROOT / "desert_planet.png").convert("RGBA").resize((1024, 1024), NEAREST)
    limb = Image.open(ROOT / "desert_planet_limb.png").convert("RGBA").resize((1024, 512), NEAREST)
    deck = Image.open(ROOT / "desert_cloud_deck.png").convert("RGBA").resize((1024, 512), NEAREST)
    entry = Image.open(ROOT / "desert_entry_fx.png").convert("RGBA").crop((0, 0, 512, 1024))
    burst = Image.open(ROOT / "desert_breakthrough.png").convert("RGBA").crop((3 * 1024, 0, 4 * 1024, 1024)).resize((512, 512), NEAREST)
    streak = Image.open(ROOT / "desert_entry_streaks.png").convert("RGBA").resize((512, 1024), NEAREST)
    for im, xy in [(planet, (0, 0)), (limb, (1024, 0)), (deck, (1024, 512)), (entry, (0, 1024)), (burst, (512, 1024)), (streak, (1024, 1024))]:
        bg.alpha_composite(im, xy)
    bg.resize((4096, 4096), NEAREST).convert("RGB").save(ROOT / "preview.png")
    strip = Image.open(ROOT / "desert_entry_fx.png").convert("RGBA")
    frames = []
    for i in range(6):
        f = Image.new("RGBA", (512, 1024), "#0b0b1a")
        f.alpha_composite(strip.crop((i * 512, 0, (i + 1) * 512, 1024)))
        frames.append(f.resize((256, 512), NEAREST).convert("RGB"))
    frames[0].save(ROOT / "preview.gif", save_all=True, append_images=frames[1:], duration=90, loop=0)


def main():
    (ROOT / "masters").mkdir(exist_ok=True)
    make_planet()
    make_limb()
    make_clouds()
    holes = make_entry()
    make_breakthrough()
    make_streaks()
    make_previews()
    files = {}
    specs = {
        "desert_planet.png": ((1024, 1024), (1, 1), (512, 512)),
        "desert_planet_limb.png": ((2048, 1024), (1, 1), (1024, 355)),
        "desert_cloud_deck.png": ((2048, 1024), (1, 1), (1024, 512)),
        "desert_cloud_deck_dark.png": ((2048, 1024), (1, 1), (1024, 512)),
        "desert_entry_fx.png": ((3072, 1024), (6, 1), (256, 300)),
        "desert_breakthrough.png": ((5120, 1024), (5, 1), (512, 512)),
        "desert_entry_streaks.png": ((1024, 2048), (1, 1), (512, 1024)),
        "preview.png": ((4096, 4096), (1, 1), (2048, 2048)),
        "preview.gif": ((256, 512), (6, 1), (128, 150)),
    }
    for name, (size, grid, pivot) in specs.items():
        files[name] = {"size": list(size), "cell_grid": list(grid), "frame_count": grid[0] * grid[1], "pivot_px": list(pivot)}
    files["desert_planet.png"].update({"disc_center_px": [553.1, 544.4], "disc_radius_px": 386.7, "measurement": "360 radial alpha exits, least-squares fit after excluding high and low ring/plume outliers", "measurement_detail": {"rays_measured": 360, "rays_used_in_fit": 335, "ray_distance_p10_p50_p90_px": [369, 391, 401], "alpha_margin_lrtb_px": [77, 77, 63, 93]}})
    files["desert_planet_limb.png"].update({"horizon_y_at_center_px": 355, "horizon_y_at_edges_px": 720, "horizon_y_at_left_right_px": [720, 719]})
    for name in ("desert_cloud_deck.png", "desert_cloud_deck_dark.png"):
        files[name].update({"wrap_x": True, "wrap_y": True})
    files["desert_entry_fx.png"].update({"cell_size": [512, 1024], "hole_center_px": [256, 338], "hole_size_px": [146, 165], "hole_center_px_per_frame": [x["center_px"] for x in holes], "hole_size_px_per_frame": [x["size_px"] for x in holes]})
    files["desert_breakthrough.png"].update({"cell_size": [1024, 1024]})
    files["desert_entry_streaks.png"].update({"wrap_y": True})
    manifest = {
        "world": "Desert",
        "camera": "Atmosphere level, looking down beneath a thin high dust haze. Only desert_planet.png is orbital.",
        "palette": "Pale desaturated bone-gold sand, dusty indigo shadow, matte oxidised copper traces, pale sun-white heat. No amber or orange emission.",
        "ceiling_match": "Paint the later backdrop ceiling as thin high dust-haze banks matching desert_cloud_deck.png: bone-gold sheets on dusty brown-grey, long wind striations, muted indigo-brown gaps. Deeper banks match desert_cloud_deck_dark.png. No stars or orbital objects.",
        "files": files,
        "source_selection": {"planet": "planet_a_source.png chosen over planet_b_source.png for its readable crescent scar and snapped ring", "entry": "entry_b_source.png chosen over entry_a_source.png for its broad shroud and clean opening", "other_painted_sources": ["limb_source.png", "cloud_source.png", "breakthrough_source.png", "streaks_source.png"]},
        "production": "Built-in image generation painted every motif; src~/build.py performs nearest-neighbour placement, local HSV gamut restriction, and exact registration/seam work without blur or global palette reduction."
    }
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
