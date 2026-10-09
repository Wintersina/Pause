"""Verify the Tide descent contract and write measured registration to manifest.json."""
from __future__ import annotations

import json
import math
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
SIZES = {
    "tide_planet.png": (1024, 1024),
    "tide_planet_limb.png": (2048, 1024),
    "tide_cloud_deck.png": (2048, 1024),
    "tide_cloud_deck_dark.png": (2048, 1024),
    "tide_entry_fx.png": (3072, 1024),
    "tide_breakthrough.png": (5120, 1024),
    "tide_entry_streaks.png": (1024, 2048),
}
HUE_BANDS = {
    "shield178": (172, 184),
    "repair82": (76, 88),
    "violet259": (253, 265),
    "amber37": (31, 43),
    "pink312-326": (312, 326),
    "player_red": (332, 16),
}


def rgba(name: str) -> np.ndarray:
    return np.asarray(Image.open(ROOT / name).convert("RGBA"))


def bounds(a: np.ndarray, threshold: int = 48) -> tuple[int, int, int, int]:
    y, x = np.where(a[:, :, 3] > threshold)
    return int(x.min()), int(y.min()), int(x.max()), int(y.max())


def margins(a: np.ndarray) -> tuple[int, int, int, int]:
    x0, y0, x1, y1 = bounds(a)
    h, w = a.shape[:2]
    return x0, y0, w - 1 - x1, h - 1 - y1


def hue_shares(a: np.ndarray) -> tuple[dict[str, float], int]:
    hsv = np.asarray(Image.fromarray(a[:, :, :3], "RGB").convert("HSV"))
    hue = hsv[:, :, 0].astype(np.float32) * (360.0 / 255)
    colored = (a[:, :, 3] > 32) & (hsv[:, :, 1] >= 46) & (hsv[:, :, 2] >= 31)
    n = max(1, int(colored.sum()))
    shares = {}
    for key, (lo, hi) in HUE_BANDS.items():
        band = ((hue >= lo) & (hue <= hi)) if lo <= hi else ((hue >= lo) | (hue <= hi))
        shares[key] = round(float((colored & band).sum()) / n * 100, 3)
    return shares, n


def measure_disc(a: np.ndarray) -> dict:
    """360 radial alpha scans with a robust circular fit that rejects tethers."""
    alpha = a[:, :, 3]
    cx, cy = 512.0, 512.0
    points = []
    radial = []
    for degree in range(360):
        theta = math.radians(degree)
        ct, st = math.cos(theta), math.sin(theta)
        hit = None
        # First sustained exit from the opaque ocean/atmosphere is the disc.
        for r in range(325, 478):
            x, y = round(cx + r * ct), round(cy + r * st)
            if not (0 <= x < 1024 and 0 <= y < 1024):
                break
            next_alpha = []
            for rr in range(r, min(r + 7, 480)):
                xx, yy = round(cx + rr * ct), round(cy + rr * st)
                next_alpha.append(alpha[yy, xx] > 100)
            if sum(next_alpha) <= 1:
                hit = r
                break
        if hit is not None:
            radial.append(hit)
            points.append((cx + hit * ct, cy + hit * st))
    if len(points) < 350:
        raise AssertionError(f"Only {len(points)} of 360 disc rays measured")
    p = np.asarray(points)
    r = np.asarray(radial)
    median = float(np.median(r))
    good = np.abs(r - median) <= 25
    p = p[good]
    # x²+y² = 2cx*x+2cy*y+c, solved only on the inlier disc edge.
    mat = np.column_stack((2 * p[:, 0], 2 * p[:, 1], np.ones(len(p))))
    rhs = p[:, 0] ** 2 + p[:, 1] ** 2
    fit = np.linalg.lstsq(mat, rhs, rcond=None)[0]
    fx, fy = float(fit[0]), float(fit[1])
    radius = math.sqrt(max(0, float(fit[2]) + fx * fx + fy * fy))
    return {
        "center_px": [round(fx, 1), round(fy, 1)],
        "disc_radius_px": round(radius, 1),
        "rays_measured": len(radial),
        "rays_used_in_fit": int(good.sum()),
        "radial_p10_p50_p90_px": [round(float(np.percentile(r, q)), 1) for q in (10, 50, 90)],
    }


def flood_hole(a: np.ndarray, sx: int, sy: int) -> dict:
    empty = a[:, :, 3] <= 8
    if not empty[sy, sx]:
        raise AssertionError("ship opening center is occupied")
    seen = np.zeros(empty.shape, dtype=bool)
    todo = deque([(sx, sy)])
    seen[sy, sx] = True
    left = right = sx
    top = bottom = sy
    count = 0
    while todo:
        x, y = todo.popleft()
        count += 1
        left = min(left, x); right = max(right, x)
        top = min(top, y); bottom = max(bottom, y)
        if count > 80000 or x == 0 or x == 511 or y == 0 or y == 1023:
            raise AssertionError("ship opening connects to exterior transparency")
        for nx, ny in ((x-1,y),(x+1,y),(x,y-1),(x,y+1)):
            if empty[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = True
                todo.append((nx, ny))
    return {"center_px": [round((left+right)/2, 1), round((top+bottom)/2, 1)],
            "size_px": [right-left+1, bottom-top+1], "flood_area_px": count}


def file_entry(size, grid, count, use, pivot, **other):
    return {"size": list(size), "cell_grid": list(grid), "frame_count": count,
            "intended_use": use, "pivot_px": list(pivot), **other}


def main() -> None:
    failures = []
    results = {}
    palette_rows = {}
    print("FILE                         SIZE       RGBA  SEAM   MARGIN  COLORS  RED%  178%  82%  259%  37%  PINK%")
    for name, expected in SIZES.items():
        im = Image.open(ROOT / name)
        a = np.asarray(im.convert("RGBA"))
        if im.size != expected or im.mode != "RGBA":
            failures.append(f"{name}: size/mode {im.size}/{im.mode}")
        seam = "—"
        if "cloud_deck" in name:
            ok = np.array_equal(a[:, 0], a[:, -1]) and np.array_equal(a[0], a[-1])
            seam = "XY" if ok else "FAIL"
            if not ok: failures.append(f"{name}: X/Y wrap")
        elif "streaks" in name:
            ok = np.array_equal(a[0], a[-1])
            seam = "Y" if ok else "FAIL"
            if not ok: failures.append(f"{name}: Y wrap")
        margin = "—"
        if name == "tide_planet.png":
            m = margins(a)
            margin = str(min(m))
            if min(m) < 24: failures.append(f"{name}: margin {m}")
        elif name in ("tide_entry_fx.png", "tide_breakthrough.png"):
            cell = 512 if "entry_fx" in name else 1024
            count = 6 if cell == 512 else 5
            ms = [min(margins(a[:, i*cell:(i+1)*cell])) for i in range(count)]
            margin = str(min(ms))
            if min(ms) < 18: failures.append(f"{name}: frame margin {ms}")
        shares, colored = hue_shares(a)
        rgb = a[:, :, :3].astype(np.uint32)
        packed = (rgb[:, :, 0] << 16) | (rgb[:, :, 1] << 8) | rgb[:, :, 2]
        colors = len(np.unique(packed))
        if name in ("tide_planet.png", "tide_planet_limb.png", "tide_cloud_deck.png", "tide_cloud_deck_dark.png") and colors < 2000:
            failures.append(f"{name}: only {colors} distinct RGB colors")
        if shares["player_red"] > 0.05: failures.append(f"{name}: red hue share {shares['player_red']}%")
        if shares["pink312-326"] > 1: failures.append(f"{name}: pink share {shares['pink312-326']}%")
        for key in ("shield178", "repair82", "violet259", "amber37"):
            if shares[key] > 2: failures.append(f"{name}: {key} share {shares[key]}%")
        palette_rows[name] = {"colored_visible_pixels": colored, "forbidden_hue_share_percent": shares,
                              "distinct_rgb_colors": colors}
        print(f"{name:28} {im.width}x{im.height:<5} {im.mode:5} {seam:5} {margin:>6} {colors:>7} "
              f"{shares['player_red']:>5.3f} {shares['shield178']:>5.3f} {shares['repair82']:>5.3f} "
              f"{shares['violet259']:>5.3f} {shares['amber37']:>5.3f} {shares['pink312-326']:>6.3f}")
    disc = measure_disc(rgba("tide_planet.png"))
    print("360-ray disc fit:", disc)
    limb = rgba("tide_planet_limb.png")
    heights = [int(np.flatnonzero(limb[:, x, 3] > 128)[0]) for x in (0, 1024, 2047)]
    print("Limb horizon L/C/R:", heights)
    if abs(heights[1]-355)>2 or max(abs(heights[0]-720),abs(heights[2]-720))>2:
        failures.append(f"limb horizon {heights}")
    fx = rgba("tide_entry_fx.png")
    holes = []
    for i in range(6):
        hole = flood_hole(fx[:,i*512:(i+1)*512], 256, 338)
        holes.append(hole)
        w,h = hole["size_px"]
        print(f"Entry frame {i}: center={hole['center_px']} opening={w}x{h} area={hole['flood_area_px']}")
        if not (138 <= w <= 160 and 156 <= h <= 177 and abs(hole['center_px'][0]-256)<=5 and abs(hole['center_px'][1]-338)<=5):
            failures.append(f"entry frame {i} hole geometry {hole}")
    master = Image.open(ROOT / "masters" / "tide_planet_master_4096.png")
    contact = Image.open(ROOT / "preview.png")
    gif = Image.open(ROOT / "preview.gif")
    if master.size != (4096,4096) or master.mode != "RGBA": failures.append("4096 master size/mode")
    if contact.size != (4096,4096): failures.append("2x contact sheet size")
    if gif.size != (256,512) or getattr(gif, "n_frames", 0) != 6: failures.append("animated GIF size/frames")
    print(f"Extras: master={master.size}/{master.mode}, preview={contact.size}, GIF={gif.size}/{gif.n_frames} frames")
    if failures:
        print("FAIL:", *failures, sep="\n  ")
        raise SystemExit(1)
    files = {
        "tide_planet.png": file_entry((1024,1024),(1,1),1,"Space backdrop approach",(512,512),
            disc_radius_px=disc["disc_radius_px"], atmosphere_outer_radius_px=round(disc["disc_radius_px"]+7,1),
            measurement="360 radial alpha rays, robust circle fit; tether platforms excluded as outliers",
            measurement_detail=disc),
        "tide_planet_limb.png": file_entry((2048,1024),(1,1),1,"Close approach curved horizon",(1024,heights[1]),
            horizon_y_at_center_px=heights[1], horizon_y_at_edges_px=720,
            horizon_y_at_left_right_px=[heights[0],heights[2]]),
        "tide_cloud_deck.png": file_entry((2048,1024),(1,1),1,"Fast scrolling upper storm cloud layer; wrap X/Y",(1024,512),wrap_x=True,wrap_y=True),
        "tide_cloud_deck_dark.png": file_entry((2048,1024),(1,1),1,"Deep storm cloud parallax; wrap X/Y",(1024,512),wrap_x=True,wrap_y=True),
        "tide_entry_fx.png": file_entry((3072,1024),(6,1),6,"Looping ship-centered entry shroud",(256,300),
            cell_size=[512,1024], ship_clear_radius_px=44, hole_center_px=[256,338], hole_size_px=[146,166],
            hole_center_px_per_frame=[h['center_px'] for h in holes], hole_size_px_per_frame=[h['size_px'] for h in holes]),
        "tide_breakthrough.png": file_entry((5120,1024),(5,1),5,"One-shot cloud break spray/foam shockwave",(512,512),cell_size=[1024,1024]),
        "tide_entry_streaks.png": file_entry((1024,2048),(1,1),1,"Vertically repeating dive rain streak overlay",(512,1024),wrap_y=True),
        "preview.png": file_entry((4096,4096),(1,1),1,"2x contact sheet on #0b0b1a",(2048,2048)),
        "preview.gif": file_entry((256,512),(6,1),6,"Entry shroud loop preview",(128,150)),
    }
    manifest = {
        "palette": "Tide abyssal teal-black ocean and slate storm cloud; mint-green bioluminescence, muted blue-violet shadows, restrained desaturated brown rig rust",
        "ceiling_match": "Paint later atmosphere-level backdrop cloud banks to match tide_cloud_deck.png: slate grey-teal cloud bodies, abyssal blue-green gaps, mint-green #7CF2C0 crest lights; deeper banks match tide_cloud_deck_dark.png. No stars.",
        "files": files,
        "verification": palette_rows,
    }
    (ROOT / "manifest.json").write_text(json.dumps(manifest,indent=2)+"\n")
    print("PASS: all sizes, alpha, wraps, borders, openings, margins, rays and hue budgets")


if __name__ == "__main__":
    main()
