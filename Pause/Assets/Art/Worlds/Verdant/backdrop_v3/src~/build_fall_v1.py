"""Rebuild Verdant v1 (canopy sea) as its AUTUMN version.

Sources: fall_v1/fall_a.png and fall_b.png (Codex repaints of v1_terrain_a/b,
same composition) and v1_flow_source.png (recoloured here to pale gold mist
and leaf flecks). Output: ../v1/{sky,far,mid,flow}.png, 512x1024 RGBA,
wrap-blended exactly like build.py. mask.txt is untouched (same layout).
Run: python3 build_fall_v1.py
"""
from __future__ import annotations
import sys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build as B

SRC = B.SRC
W, H = B.W, B.H
# value targets sit below the summer set (.415/.445/.475): the lane stays dark
# behind brown and black bullets. Saturation is pulled down in mid/near.
TARGET = {"sky": .36, "far": .38, "mid": .34, "flow": .42}
SAT = {"sky": .8, "far": 1.0, "mid": .78}
FOG = {"sky": (.20, .19, .22), "far": (.20, .16, .18), "mid": (.08, .15, .17)}


def src(name):
    im = Image.open(SRC / "fall_v1" / name).convert("RGBA").resize((W, H), Image.Resampling.NEAREST)
    return np.asarray(im).copy()[:, :, :3]


def desat(rgb, s, fog, haze):
    f = rgb.astype(np.float32) / 255
    g = f.mean(axis=2, keepdims=True)
    f = g + (f - g) * s
    f = f * (1 - haze) + np.array(fog, np.float32) * haze
    return np.uint8(np.clip(np.rint(f * 255), 0, 255))


PAL = {  # per palette: source files, value targets, saturation, contrast, haze
    "fall": dict(a="fall_a4.png", b="fall_b4.png",
                 target={"sky": .36, "far": .42, "mid": .40}, sat={"sky": .75, "far": .85, "mid": .62},
                 contrast={"sky": .30, "far": .62, "mid": .80}, haze={"sky": .34, "far": .08, "mid": 0.0}),
    "green": dict(a="green_a4.png", b="green_b4.png",
                  target={"sky": .38, "far": .44, "mid": .42}, sat={"sky": .85, "far": .95, "mid": .80},
                  contrast={"sky": .30, "far": .62, "mid": .80}, haze={"sky": .30, "far": .08, "mid": 0.0}),
}


def ground(role, pal="fall"):
    P = PAL[pal]
    a = src(P["a"]); b = src(P["b"])
    w = {"sky": .32, "far": .72, "mid": .16}[role]
    rgb = np.uint8(np.rint(a.astype(np.float32) * (1 - w) + b.astype(np.float32) * w))
    haze = P["haze"][role]
    rgb = desat(rgb, P["sat"][role], FOG[role], haze)
    t = P["target"][role]
    contrast = P["contrast"][role]
    cap = .62
    rgb = B.palette_and_value(rgb, t, contrast, 0, cap, colors=42 if role == "sky" else 56)
    rgba = np.dstack((rgb, np.full((H, W), 255, np.uint8)))
    rgba = B.wrap_blend(rgba)
    rgb = B.palette_and_value(rgba[:, :, :3], t, 1.0, 0, cap, colors=56)
    rgba = np.dstack((rgb, np.full((H, W), 255, np.uint8)))
    if role == "far" and pal == "green":
        # carry the painted flower / sakura pixels (blue, violet, pink, red) over the palette indexing, dimmed and
        # a little desaturated, away from the 70 px wrap band (seams stay identical). Orange / gold left out.
        sb = src(P["b"]).astype(np.float32) / 255
        mx, mn = sb.max(axis=2), sb.min(axis=2)
        sat = (mx - mn) / np.maximum(mx, 1e-4)
        r, g, b = sb[..., 0], sb[..., 1], sb[..., 2]
        warm = (r > g + .18) & (r >= b)
        cool = (b > g + .10) & (b > r - .05)
        pale = (r > g + .06) & (b > g + .03)
        flower = (warm | cool | pale) & (mx > .42) & (sat > .18) & ~((r > g + .10) & (g > b + .06) & (mx < .6))
        flower[:70] = False; flower[-70:] = False; flower[:, :70] = False; flower[:, -70:] = False
        gr = sb.mean(axis=2, keepdims=True)
        soft = np.clip((gr + (sb - gr) * .85) * .82, 0, 1) * 255
        rgba[..., :3] = np.where(flower[..., None], np.uint8(soft), rgba[..., :3])
    rgba[:, -1] = rgba[:, 0]; rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


def flow():
    a = np.asarray(Image.open(SRC / "v1_flow_source.png").convert("RGBA").resize((W, H), Image.Resampling.NEAREST)).copy()
    alpha = a[:, :, 3].astype(np.float32)
    alpha = np.where(alpha < 105, 0, np.where(alpha < 170, 72, np.where(alpha < 225, 112, 152))).astype(np.uint8)
    # luminance onto a pale-gold ramp (mist, leaf flecks); dusky, low value
    lum = (a[:, :, :3].astype(np.float32) @ np.array([.299, .587, .114], np.float32)) / 255
    lum = (lum - lum.min()) / max(lum.max() - lum.min(), 1e-3)
    dark = np.array([.30, .20, .16]); light = np.array([.62, .50, .30])
    rgb = np.uint8(np.rint((dark + (light - dark) * lum[..., None]) * 255))
    rgba = np.dstack((rgb, alpha))
    rgba = B.wrap_blend(rgba, band=80)
    rgba[:, :, :3] = B.palette_and_value(rgba[:, :, :3], TARGET["flow"], 1.0, 0, .62, 36, mask=rgba[:, :, 3] > 0)
    levels = np.array([0, 72, 112, 152], np.uint8)
    rgba[:, :, 3] = levels[np.abs(rgba[:, :, 3, None].astype(np.int16) - levels).argmin(axis=2)]
    rgba[:, -1] = rgba[:, 0]; rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


if __name__ == "__main__":
    res = Path(__file__).resolve().parents[5] / "Art/Backgrounds/Resources/Worlds/Verdant/Backdrop3/v1"
    green = Path(__file__).resolve().parent / "green_v1_corrected_UNWIRED"
    green.mkdir(exist_ok=True)
    for pal, out in (("fall", res), ("green", green)):
        for role in ("sky", "far", "mid"):
            im = ground(role, pal)
            im.save(out / f"{role}.png", optimize=True)
            print(pal, role, im.size, B.stats(im))
    # fall flow overlay (pale gold mist / leaf flecks); the green set keeps the summer flow
    flow().save(res / "flow.png", optimize=True)
