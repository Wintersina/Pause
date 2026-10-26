"""Rebuild an Ember ground set (sky/far/mid) from Codex redraws.

Sources: ember_v1/v{N}_new_a.png (play layer redraw: dark, cool, low
saturation) and ember_v1/v{N}_new_b.png (far layer redraw: the colour and
ecology). Output: ../../../Backgrounds/Resources/Worlds/Ember/Backdrop3/v{N}/
{sky,far,mid}.png, 512x1024 RGBA, nearest resize, wrap-blended like build.py.
flow.png and everything else stay untouched.
Run: python3 build_ember_v1.py 2 [3 1 4]
"""
from __future__ import annotations
import sys, colorsys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent))
from build import W, H, wrap_blend

HERE = Path(__file__).resolve().parent
SRC = HERE / "ember_v1"
RES = HERE.parents[3] / "Backgrounds/Resources/Worlds/Ember/Backdrop3"

# tuned per variant: target p90 value, contrast, saturation, haze, per role
CFG = {
  "day": dict(target={"sky": .36, "far": .44, "mid": .36}, sat={"sky": .95, "far": 1.0, "mid": .88},
              contrast={"sky": .55, "far": .70, "mid": .85}, haze={"sky": .18, "far": .05, "mid": 0.0}),
  "night": dict(target={"sky": .30, "far": .38, "mid": .32}, sat={"sky": .95, "far": 1.0, "mid": .88},
              contrast={"sky": .55, "far": .70, "mid": .85}, haze={"sky": .18, "far": .05, "mid": 0.0}),
}
# per-variant mid target override: the mid lane median luminance must stay <= the old set (enemy body contrast)
MID_TARGET = {3: .26, 1: .27}
FAR_TARGET = {3: .432}  # keeps far p90 under the .45 cap once the gold traces are added
# sparse gold traces (user-approved): the hottest painted lava cores stay gold in far/sky only; never in mid (atoms are gold/orange)
GOLD = {"far": (172, 128, 48), "sky": (122, 92, 38)}
# v1 mid (the only visible ground layer: sky/far sit under the opaque mid tile) is the Codex redraw that has the right-hand lava river
MID_SRC = {1: "v1_new_a_river.png"}
FOG = (.09, .08, .08)
CAP = .45
NCOL = {"sky": 44, "far": 84, "mid": 58}


def src(name):
    im = Image.open(SRC / name).convert("RGB")
    wd = min(im.width, im.height // 2)
    x = (im.width - wd) // 2; y = (im.height - 2 * wd) // 2
    im = im.crop((x, y, x + wd, y + 2 * wd))
    return np.asarray(im.resize((W, H), Image.Resampling.NEAREST)).copy()


def pink_guard(rgb):
    """No pink / magenta: such pixels go to a cool blue-grey of the same value."""
    f = rgb.astype(np.float32) / 255
    mx = f.max(2); mn = f.min(2); d = mx - mn
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    # magenta-ish: blue strong, green weakest, red >= .5 blue-ish
    pink = (g < r) & (g < b) & (d > .12) & (b > .45 * r) & (b >= .3 * mx)
    out = rgb.copy()
    v = mx[pink]
    out[pink] = np.uint8(np.clip(np.stack([v * .62, v * .70, v * .82], 1) * 255, 0, 255))
    return out


def grade(rgb, target, contrast, sat, haze, colors, cap=CAP, mask=None):
    f = rgb.astype(np.float32) / 255
    g = f.mean(axis=2, keepdims=True)
    f = g + (f - g) * sat
    f = f * (1 - haze) + np.array(FOG, np.float32) * haze
    old_v = f.max(axis=2)
    p90 = float(np.percentile(old_v, 90))
    new_v = np.clip(target + (old_v - p90) * contrast, .05, cap)
    f *= (new_v / np.maximum(old_v, .001))[..., None]
    idx = Image.fromarray(np.uint8(np.rint(np.clip(f, 0, 1) * 255)), "RGB").quantize(
        colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
    q = np.asarray(idx).astype(np.float32) / 255
    q *= np.clip(target / max(float(np.percentile(q.max(2), 90)), .001), .5, 1.5)
    q = np.clip(q, 0, cap)
    return pink_guard(np.uint8(np.rint(q * 255)))


def ground(v, role):
    cfg = CFG["night" if v == 4 else "day"]
    a = src(f"v{v}_new_a.png"); b = src(f"v{v}_new_b.png")
    if role == "far":
        painted = np.roll(b, (143, 61), axis=(0, 1))
    elif role == "mid":
        painted = src(MID_SRC[v]) if v in MID_SRC else a
    else:
        painted = np.uint8(np.rint(a.astype(np.float32) * .30 + np.roll(b, (241, -49), axis=(0, 1)).astype(np.float32) * .70))
    t = cfg["target"][role]
    if role == "mid": t = MID_TARGET.get(v, t)
    if role == "far": t = FAR_TARGET.get(v, t)
    rgb = grade(painted, t, cfg["contrast"][role], cfg["sat"][role], cfg["haze"][role], NCOL[role])
    rgba = np.dstack((rgb, np.full((H, W), 255, np.uint8)))
    rgba = wrap_blend(rgba, 78)
    rgba[:, :, :3] = grade(rgba[:, :, :3], t, 1.0, 1.0, 0, NCOL[role])
    if role in GOLD:
        hot = (painted[..., 0] > 235) & (painted[..., 1] > 190) & (painted[..., 2] < 150)
        hot[:8] = hot[-8:] = False; hot[:, :8] = hot[:, -8:] = False
        rgba[:, :, :3][hot] = GOLD[role]
    rgba[:, -1] = rgba[:, 0]; rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


def lin(c):
    return np.where(c <= .03928, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def stats(im):
    a = np.asarray(im).astype(np.float32) / 255
    rgb = a[..., :3]
    lum = (.2126 * lin(rgb[..., 0]) + .7152 * lin(rgb[..., 1]) + .0722 * lin(rgb[..., 2])).mean()
    return dict(p90=round(float(np.percentile(rgb.max(2), 90)), 3), lum=round(float(lum), 3),
                chroma=round(float((rgb.max(2) - rgb.min(2)).mean()), 3))


if __name__ == "__main__":
    for v in [int(x) for x in sys.argv[1:]]:
        for role in ("sky", "far", "mid"):
            im = ground(v, role)
            old = Image.open(RES / f"v{v}" / f"{role}.png")
            assert im.size == old.size == (W, H)
            im.save(RES / f"v{v}" / f"{role}.png", optimize=True)
            print(v, role, im.size, stats(im), "seam", int(np.abs(np.asarray(im)[0].astype(int) - np.asarray(im)[-1]).max()),
                  int(np.abs(np.asarray(im)[:, 0].astype(int) - np.asarray(im)[:, -1]).max()))
