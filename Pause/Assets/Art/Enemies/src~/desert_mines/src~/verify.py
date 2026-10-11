#!/usr/bin/env python3
"""Verify the staged Mirage Mine row against the Tide grid and Desert palette."""
from pathlib import Path
import sys
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
ATLAS = ROOT / "rail_mines_neon_desert.png"
TIDE = HERE.parents[3] / "Resources/Enemies/Mines/rail_mines_neon_tide.png"
EDGES = (0, 313, 627, 940, 1254)
PIVOT = (215, 158)


def fail_if(condition: bool, message: str) -> None:
    if condition:
        raise AssertionError(message)


def centroid_bright(pose: Image.Image, radius: int = 16) -> tuple[float, float]:
    a = np.asarray(pose.convert("RGBA"))
    yy, xx = np.indices(a.shape[:2])
    r = (xx-PIVOT[0])**2 + (yy-PIVOT[1])**2 <= radius**2
    hot = r & (a[:, :, 3] > 200) & (a[:, :, :3].max(axis=2) >= 245)
    fail_if(not hot.any(), "Missing bright beam exit at lens pivot")
    return float(xx[hot].mean()), float(yy[hot].mean())


def main() -> None:
    im = Image.open(ATLAS).convert("RGBA")
    ref = Image.open(TIDE).convert("RGBA")
    fail_if(im.size != ref.size or im.size != (1254, 314), "Atlas size differs from Tide")
    poses = [im.crop((x0, 0, x1, 314)) for x0, x1 in zip(EDGES[:-1], EDGES[1:])]
    assert len(poses) == 4
    print("state       cell     bounds (exclusive)       min edge  opaque  RGB tones  p90 V")
    for name, pose in zip(("Dormant", "Waking", "Charging", "Burst"), poses):
        a = np.asarray(pose)
        x0, y0, x1, y1 = pose.getbbox() or (0, 0, 0, 0)
        fail_if(not x1 or not y1, f"{name} is empty")
        margin = min(x0, y0, pose.width-x1, pose.height-y1)
        opaque = a[:, :, 3] > 128
        tones = len(np.unique(a[opaque, :3], axis=0))
        p90 = np.percentile(a[opaque, :3].max(axis=1) / 255, 90)
        print(f"{name:10} {pose.width:3}x314  ({x0:3},{y0:3},{x1:3},{y1:3})   {margin:3}      {opaque.sum():5}     {tones:5}    {p90:.3f}")
        fail_if(margin < 6, f"{name} touches the cell edge")
        fail_if(tones < 300, f"{name} lost painted tonal detail")
        fail_if(x0 > 60, f"{name} clamp does not reach Tide's rail side")

    tide_box = ref.crop((0, 0, 313, 314)).getbbox()
    desert_box = poses[0].getbbox()
    fail_if(abs(tide_box[0]-desert_box[0]) > 2 or abs(tide_box[2]-desert_box[2]) > 2,
            "Dormant clamp/body horizontal bounds differ from Tide")
    fail_if(abs(tide_box[1]-desert_box[1]) > 2 or abs(tide_box[3]-desert_box[3]) > 12,
            "Dormant vertical silhouette differs from Tide")
    clamp_reach = (PIVOT[0] - 54 - desert_box[0]) / 313
    fail_if(not .29 <= clamp_reach <= .36, "Clamp reach is outside the ~0.33 u target")

    # The outer boundary is exactly one dark pixel on the selected painted pose.
    first = np.asarray(poses[0]); mask = first[:, :, 3] > 128
    interior = mask.copy()
    interior[1:, :] &= mask[:-1, :]; interior[:-1, :] &= mask[1:, :]
    interior[:, 1:] &= mask[:, :-1]; interior[:, :-1] &= mask[:, 1:]
    boundary = mask & ~interior
    dark_edge = np.mean(first[:, :, :3].max(axis=2)[boundary] <= 41)
    fail_if(dark_edge < .95, "Less than 95% of outer edge has a 1 px dark outline")
    wake_exit = centroid_bright(poses[1]); charge_exit = centroid_bright(poses[2])
    for pt in (wake_exit, charge_exit):
        fail_if(abs(pt[0]-PIVOT[0]) > 3 or abs(pt[1]-PIVOT[1]) > 3,
                "Beam exit differs from fixed lens pivot")

    rgba = np.asarray(im)
    hsv = np.asarray(im.convert("HSV"))
    on = rgba[:, :, 3] > 128
    H = hsv[:, :, 0].astype(float) * 360 / 255
    S = hsv[:, :, 1].astype(float) / 255
    V = hsv[:, :, 2].astype(float) / 255
    total = int(on.sum())
    masks = {
        "red S>.5": ((H >= 345) | (H <= 15)) & (S > .5),
        "pink": (H >= 312) & (H <= 326),
        "violet": (H >= 250) & (H <= 300),
        "amber S>.45": (H >= 30) & (H <= 48) & (S > .45),
        "bright teal": (H >= 145) & (H <= 190) & (S > .4),
        "copper": (H >= 22) & (H <= 30),
        "sand": (H >= 40) & (H <= 48),
        "glass": (H >= 160) & (H <= 170),
        "verdigris": (H >= 135) & (H <= 155),
    }
    print(f"\nopaque pixels: {total}; Dormant dark-outline boundary: {dark_edge:.1%}")
    print(f"clamp reach: {clamp_reach:.3f} u; lens pivot: {PIVOT}; Waking exit: {wake_exit}; Charging exit: {charge_exit}")
    print("palette         pixels    share    p95 S   max S   max V")
    for name, test in masks.items():
        q = test & on
        count = int(q.sum())
        p95s = float(np.percentile(S[q], 95)) if count else 0
        maxs = float(S[q].max()) if count else 0
        maxv = float(V[q].max()) if count else 0
        print(f"{name:15} {count:6}  {count/total:7.3%}  {p95s:6.3f}  {maxs:6.3f}  {maxv:6.3f}")
        if name in ("red S>.5", "pink", "violet", "bright teal"):
            fail_if(count != 0, f"Forbidden {name} pixels")
        if name == "amber S>.45":
            fail_if(count / total > .003, "Amber pickup collision")
        if name == "copper":
            fail_if(maxs > .62 or maxv > .605, "Copper is too saturated or emissive")
        if name == "sand":
            fail_if(p95s > .334, "Sand exceeds desaturation cap")
        if name == "glass":
            fail_if(maxs > .225 or count/total > .04, "Glass shimmer exceeds trace limit")
        if name == "verdigris":
            fail_if(maxs > .325 or count/total > .03, "Verdigris exceeds trace limit")

    preview = Image.open(ROOT / "preview.png")
    fail_if(preview.size != (2508, 628), "Preview is not 2x")
    fail_if(preview.getpixel((0, 0)) != (11, 11, 26), "Preview background differs from #0b0b1a")
    for i in (1, 2, 3):
        fail_if(not (HERE / f"candidate_{i}.png").is_file(), f"Missing painted candidate {i}")
    print("PASS: four states, Tide grid/pivot/clamp, 1 px outline, candidates, preview, Desert palette")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
