"""usage: verify_ember.py V OLDDIR  -- sizes, alpha, seams, pink, caps, lane proxy, fire-coloured mass in mid"""
import sys, os
from pathlib import Path
import numpy as np
from PIL import Image
v, old = sys.argv[1], Path(sys.argv[2])
new = Path(__file__).resolve().parents[5] / "Backgrounds/Resources/Worlds/Ember/Backdrop3" / f"v{v}"
def lin(c): return np.where(c <= .03928, c / 12.92, ((c + .055) / 1.055) ** 2.4)
def lum(a): return .2126 * lin(a[..., 0]) + .7152 * lin(a[..., 1]) + .0722 * lin(a[..., 2])
for r in ["sky", "far", "mid", "flow"]:
    o = Image.open(old / f"{r}.png"); n = Image.open(new / f"{r}.png")
    A = np.asarray(n.convert("RGBA")).astype(np.float32) / 255; O = np.asarray(o.convert("RGBA")).astype(np.float32) / 255
    rgb = A[..., :3]; mx = rgb.max(2); mn = rgb.min(2); d = mx - mn
    hsv = np.asarray(n.convert("RGB").convert("HSV")).astype(np.float32); hue = hsv[..., 0] * 360 / 255; sat = d / np.maximum(mx, 1e-3)
    pink = ((hue > 285) & (hue < 345) & (sat > .3) & (mx > .25)).sum()
    fire = ((hue < 65) & (sat > .6) & (mx > .35)).mean()
    ofire = None
    if r != "flow":
        oh = np.asarray(o.convert("RGB").convert("HSV")).astype(np.float32); ohue = oh[..., 0] * 360 / 255
        om = O[..., :3].max(2); od = om - O[..., :3].min(2); osat = od / np.maximum(om, 1e-3)
        ofire = ((ohue < 65) & (osat > .6) & (om > .35)).mean()
    lane = lambda X: float(np.median(lum(X[:, int(512 * .2):int(512 * .8), :3])))
    sz = lambda p: os.path.getsize(p)
    arr = np.asarray(n)
    seam = (int(np.abs(arr[0].astype(int) - arr[-1]).max()), int(np.abs(arr[:, 0].astype(int) - arr[:, -1]).max()))
    print(f"v{v} {r:4s} size {n.size}=={o.size} mode {n.mode}/{o.mode} alpha_same {bool((A[..., 3] == O[..., 3]).all()) if r=='flow' else A[...,3].min()==1} "
          f"seam {seam} pink {pink} | p90 {np.percentile(mx,90):.3f} (old {np.percentile(O[...,:3].max(2),90):.3f}) lum {lum(rgb).mean():.3f} (old {lum(O[...,:3]).mean():.3f}) "
          f"chroma {d.mean():.3f} (old {(O[...,:3].max(2)-O[...,:3].min(2)).mean():.3f}) laneMedLum {lane(A):.4f} (old {lane(O):.4f}) "
          f"fireMass {fire:.3f} (old {ofire if ofire is None else round(float(ofire),3)}) bytes {sz(new/(r+'.png'))} (old {sz(old/(r+'.png'))})")
