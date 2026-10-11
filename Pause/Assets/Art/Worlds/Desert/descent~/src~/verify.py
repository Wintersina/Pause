"""Verify Desert planetfall images and print measured geometry/palette table."""
from __future__ import annotations

import json
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
SPECS = {
    "desert_planet.png": (1024, 1024),
    "desert_planet_limb.png": (2048, 1024),
    "desert_cloud_deck.png": (2048, 1024),
    "desert_cloud_deck_dark.png": (2048, 1024),
    "desert_entry_fx.png": (3072, 1024),
    "desert_breakthrough.png": (5120, 1024),
    "desert_entry_streaks.png": (1024, 2048),
}


def hsv_metrics(im):
    rgba = np.asarray(im.convert("RGBA"))
    hsv = np.asarray(im.convert("RGB").convert("HSV"), dtype=np.float32)
    h = hsv[:, :, 0] * 360 / 255
    s = hsv[:, :, 1] / 255
    v = hsv[:, :, 2] / 255
    opaque = rgba[:, :, 3] > 16
    total = max(1, int(opaque.sum()))
    share = lambda mask: float((mask & opaque).sum()) / total * 100
    red = (((h >= 345) | (h <= 15)) & (s > .5))
    pink = ((h >= 312) & (h <= 326) & (s > .2))
    violet = ((h >= 250) & (h <= 300) & (s > .12))
    amber = ((h >= 30) & (h <= 48) & (s > .45))
    teal = ((h >= 145) & (h <= 195) & (s > .45))
    lime = ((h >= 70) & (h <= 135) & (s > .40))
    sand = opaque & (h >= 40) & (h <= 48) & (s >= .07)
    body = sand & (v >= .25) & (v <= .55)
    shadow = opaque & (h >= 232) & (h <= 248) & (s >= .28) & (s <= .45) & (v >= .10) & (v <= .30)
    copper = opaque & (h >= 22) & (h <= 30) & (s > .32)
    verdigris = opaque & (h >= 135) & (h <= 155) & (s > .05)
    glass = opaque & (h >= 160) & (h <= 170) & (s > .05)
    return {
        "visible": total,
        "sand_s_p95": round(float(np.percentile(s[sand], 95)), 3) if sand.any() else 0,
        "sand_body_v_median": round(float(np.median(v[body])), 3) if body.any() else 0,
        "shadow_pct": round(share(shadow), 3),
        "copper_pct": round(share(copper), 3),
        "verdigris_pct": round(share(verdigris), 3),
        "glass_pct": round(share(glass), 3),
        "red_pct": round(share(red), 4),
        "pink_pct": round(share(pink), 4),
        "violet_pct": round(share(violet), 4),
        "amber_pct": round(share(amber), 4),
        "teal_pct": round(share(teal), 4),
        "lime_pct": round(share(lime), 4),
        "p90_v": round(float(np.percentile(v[opaque], 90)), 3),
    }


def alpha_margin(im):
    a = np.asarray(im.getchannel("A")) > 16
    yy, xx = np.where(a)
    return [int(xx.min()), int(im.width - 1 - xx.max()), int(yy.min()), int(im.height - 1 - yy.max())]


def disc_360(im):
    a = np.asarray(im.getchannel("A")) >= 96
    ox, oy = 558, 529
    points = []
    rays = []
    for angle in np.arange(360) * np.pi / 180:
        dx, dy = np.cos(angle), np.sin(angle)
        last = None
        for r in range(300, 460):
            x, y = int(round(ox + r * dx)), int(round(oy + r * dy))
            if not (0 <= x < 1024 and 0 <= y < 1024) or not a[y, x]:
                last = r - 1
                break
        if last is None:
            last = 459
        rays.append(last)
        points.append((ox + last * dx, oy + last * dy))
    xy = np.asarray(points)
    rr = np.asarray(rays)
    keep = (rr >= np.percentile(rr, 5)) & (rr <= np.percentile(rr, 90))
    q = xy[keep]
    # Least-squares circle: x²+y² = 2cx*x + 2cy*y + c.
    mat = np.column_stack((2 * q[:, 0], 2 * q[:, 1], np.ones(len(q))))
    cx, cy, c = np.linalg.lstsq(mat, np.sum(q * q, axis=1), rcond=None)[0]
    rad = np.sqrt(max(0, c + cx * cx + cy * cy))
    return {"center_px": [round(float(cx), 1), round(float(cy), 1)], "radius_px": round(float(rad), 1), "rays": 360, "rays_fit": int(keep.sum()), "radii_p10_p50_p90": [round(float(x), 1) for x in np.percentile(rr, [10, 50, 90])]}


def opening_component(cell):
    a = np.asarray(cell.getchannel("A")) <= 16
    seed = (338, 256)
    assert a[seed], "ship center is not clear"
    todo = deque([seed]); seen = np.zeros(a.shape, bool); seen[seed] = True
    minx = maxx = seed[1]; miny = maxy = seed[0]; n = 0
    while todo:
        y, x = todo.popleft(); n += 1
        minx, maxx = min(minx, x), max(maxx, x)
        miny, maxy = min(miny, y), max(maxy, y)
        for yy, xx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
            if 0 <= yy < 1024 and 0 <= xx < 512 and a[yy, xx] and not seen[yy, xx]:
                seen[yy, xx] = True; todo.append((yy, xx))
    return {"center_px": [round((minx + maxx) / 2, 1), round((miny + maxy) / 2, 1)], "size_px": [maxx - minx + 1, maxy - miny + 1], "area_px": n, "sealed": not (minx == 0 or miny == 0 or maxx == 511 or maxy == 1023)}


def main():
    manifest = json.loads((ROOT / "manifest.json").read_text())
    errors = []
    stats = {}
    print("file                           size       sand S95 body V50  p90 V shadow% Cu% amber% red% pink% violet% seam")
    for name, size in SPECS.items():
        path = ROOT / name
        im = Image.open(path)
        if im.mode != "RGBA" or im.size != size:
            errors.append(f"{name}: expected RGBA {size}, got {im.mode} {im.size}")
        if manifest["files"][name]["size"] != list(size):
            errors.append(f"{name}: manifest size mismatch")
        m = hsv_metrics(im)
        seam = 0
        arr = np.asarray(im)
        if name in {"desert_cloud_deck.png", "desert_cloud_deck_dark.png"}:
            seam = max(int(np.max(np.abs(arr[:, 0].astype(int) - arr[:, -1].astype(int)))), int(np.max(np.abs(arr[0].astype(int) - arr[-1].astype(int)))))
        if name == "desert_entry_streaks.png":
            seam = int(np.max(np.abs(arr[0].astype(int) - arr[-1].astype(int))))
        stats[name] = dict(m, seam_max_channel=seam)
        print(f"{name:30} {size[0]}x{size[1]:<5} {m['sand_s_p95']:>7.3f} {m['sand_body_v_median']:>8.3f} {m['p90_v']:>6.3f} {m['shadow_pct']:>7.2f} {m['copper_pct']:>5.2f} {m['amber_pct']:>7.3f} {m['red_pct']:>5.3f} {m['pink_pct']:>5.3f} {m['violet_pct']:>7.3f} {seam:>4}")
        if m["red_pct"] or m["pink_pct"] or m["violet_pct"] or m["teal_pct"] or m["lime_pct"]:
            errors.append(f"{name}: forbidden hue detected")
        if m["amber_pct"] > .3:
            errors.append(f"{name}: saturated amber exceeds 0.3%")
        if seam > 1:
            errors.append(f"{name}: tile seam {seam}")
        if name in {"desert_cloud_deck.png", "desert_cloud_deck_dark.png"}:
            if m["sand_s_p95"] > .33 or m["shadow_pct"] > 25 or m["copper_pct"] > 12 or m["verdigris_pct"] > 3 or m["glass_pct"] > 4:
                errors.append(f"{name}: tile palette coverage")
            if not .30 <= m["sand_body_v_median"] <= .50:
                errors.append(f"{name}: sand body value outside .30-.50")
            low, high = (.40, .50) if name == "desert_cloud_deck.png" else (.32, .42)
            if not low <= m["p90_v"] <= high:
                errors.append(f"{name}: p90 value outside {low}-.{int(high*100):02d}")
    planet = Image.open(ROOT / "desert_planet.png").convert("RGBA")
    disc = disc_360(planet)
    margins = alpha_margin(planet)
    print("planet 360-ray disc:", disc, "alpha margins LRTB:", margins)
    if min(margins) < 24:
        errors.append("planet alpha margin under 24 px")
    if abs(disc["center_px"][0] - manifest["files"]["desert_planet.png"]["disc_center_px"][0]) > 15 or abs(disc["radius_px"] - manifest["files"]["desert_planet.png"]["disc_radius_px"]) > 15:
        errors.append("planet manifest disc data differs from measurement")
    limb = np.asarray(Image.open(ROOT / "desert_planet_limb.png").getchannel("A"))
    horizon = [int(np.where(limb[:, x] > 16)[0][0]) for x in (0, 1024, 2047)]
    print("limb horizon L/C/R:", horizon)
    if max(abs(horizon[i] - [720, 355, 720][i]) for i in range(3)) > 1:
        errors.append("limb horizon registration")
    entry = Image.open(ROOT / "desert_entry_fx.png").convert("RGBA")
    openings = []
    for i in range(6):
        cell = entry.crop((i * 512, 0, (i + 1) * 512, 1024))
        arr = np.asarray(cell.getchannel("A"))
        border = np.concatenate((arr[:8].ravel(), arr[-8:].ravel(), arr[:, :8].ravel(), arr[:, -8:].ravel()))
        if border.max() > 16:
            errors.append(f"entry frame {i}: border occupied")
        o = opening_component(cell)
        openings.append(o)
        cx, cy = o["center_px"]; w, h = o["size_px"]
        if not o["sealed"] or abs(cx - 256) > 12 or abs(cy - 338) > 12 or not 132 <= w <= 180 or not 148 <= h <= 196:
            errors.append(f"entry frame {i}: opening geometry")
    print("entry openings:", openings)
    burst = np.asarray(Image.open(ROOT / "desert_breakthrough.png").getchannel("A"))
    for i in range(5):
        cell = burst[:, i * 1024:(i + 1) * 1024]
        border = np.concatenate((cell[:12].ravel(), cell[-12:].ravel(), cell[:, :12].ravel(), cell[:, -12:].ravel()))
        if border.max() > 16:
            errors.append(f"breakthrough frame {i}: border occupied")
    print("other hard-NO shares (max across art):", {k: max(x[k] for x in stats.values()) for k in ("red_pct", "pink_pct", "violet_pct", "teal_pct", "lime_pct")})
    print("RESULT:", "PASS" if not errors else "FAIL")
    for e in errors:
        print(" -", e)
    raise SystemExit(bool(errors))


if __name__ == "__main__":
    main()
