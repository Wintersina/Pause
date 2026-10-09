#!/usr/bin/env python3
"""Pre-flight for a world's backdrop ground sets (Backdrop3/v1..v4/{sky,far,mid,flow}.png) and atlases.

  python3 verify_backdrop_tiles.py BACKDROP_DIR [--day-p90 .40 .50] [--night-p90 .32 .42] [--night 4]
                                   [--forbid-hue 345 15] [--atlas-dir DIR]

Per tile (the numbers WorldBackdropTest / the Ember manifest verify):
  512x1024 RGBA, seamless on BOTH axes (edge columns/rows identical), p90 of HSV value over opaque
  pixels inside the band (day sets .40-.50, the night set .32-.42), sky/far/mid fully opaque,
  flow mostly transparent (.5%-25% opaque, no fully opaque texel), no pixel with a saturated hue inside
  the forbidden band (default 345..15 deg = the player's red; add --forbid-hue 80 100 etc for a
  world's other no-go colours: hostile shots are 312-326 deg, atoms are cyan 178 / green 82 / violet 259 / amber 37).
Atlases (landmarks, pipes, fires, weather, sites, smoke, ...): 1024x1024 RGBA with a JSON next to it
  ({"sprites":[{"n","x","y","w","h"}]}, y from the BOTTOM), every rect inside the image and non-overlapping,
  >= 6 px of clear margin round each piece (landmark-class atlases: >= 14).
Also checks a mask.txt (32 x 64 chars of W/B/O/C) exists per variant.
Exit status 1 on any FAIL.
"""
import sys, os, json, argparse, colorsys
import numpy as np
from PIL import Image

fails = 0
def check(ok, what):
    global fails
    print(("PASS  " if ok else "FAIL  ") + what)
    if not ok: fails += 1

def hsv(a):
    rgb = a[..., :3].astype(np.float32) / 255
    mx = rgb.max(axis=2); mn = rgb.min(axis=2)
    d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.zeros_like(mx)
    m = d > 0
    rm = m & (mx == r); gm = m & (mx == g) & ~rm; bm = m & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6
    h[gm] = (b - r)[gm] / d[gm] + 2
    h[bm] = (r - g)[bm] / d[bm] + 4
    return h * 60, s, mx

def tile(path, band, forbid, role):
    im = Image.open(path)
    check(im.mode == "RGBA" and im.size == (512, 1024), f"{path}: RGBA 512x1024 ({im.mode} {im.size})")
    a = np.asarray(im.convert("RGBA"))
    check(np.array_equal(a[:, 0], a[:, -1]) and np.array_equal(a[0], a[-1]), f"{os.path.basename(path)}: seamless both axes (edge rows/columns identical)")
    op = a[..., 3] > 0
    if role == "flow":
        f = op.mean()
        check(.005 < f < .25 and not (a[..., 3] == 255).any(), f"{os.path.basename(path)}: flow strip {f:.3f} opaque (.005-.25), no solid texel")
    else:
        check(op.all(), f"{os.path.basename(path)}: fully opaque")
    h, s, v = hsv(a)
    p90 = float(np.percentile(v[op], 90)) if op.any() else 0
    lo, hi = band
    check(lo <= p90 <= hi, f"{os.path.basename(path)}: value p90 {p90:.3f} in {lo}-{hi}")
    for (h0, h1) in forbid:
        inband = ((h >= h0) | (h <= h1)) if h0 > h1 else ((h >= h0) & (h <= h1))
        bad = inband & (s > .5) & (v > .35) & op
        check(not bad.any(), f"{os.path.basename(path)}: no saturated pixels in hue {h0}-{h1} ({int(bad.sum())})")

def atlas(png, margin):
    j = os.path.splitext(png)[0] + ".json"
    im = Image.open(png); a = np.asarray(im.convert("RGBA"))
    check(im.size == (1024, 1024) and im.mode == "RGBA", f"{os.path.basename(png)}: RGBA 1024x1024 ({im.size})")
    if not os.path.exists(j): check(False, f"{j} missing"); return
    sp = json.load(open(j))["sprites"]
    H = im.size[1]; boxes = []
    for s in sp:
        x, y, w, hh = s["x"], s["y"], s["w"], s["h"]
        ok = 0 <= x and x + w <= im.size[0] and 0 <= y and y + hh <= H
        top = H - y - hh
        if ok:
            cell = a[top:top + hh, x:x + w, 3]
            ys, xs = np.nonzero(cell > 16)
            if len(xs):
                m = min(xs.min(), ys.min(), w - 1 - xs.max(), hh - 1 - ys.max())
                if m < margin: ok = False; print(f"      {s['n']}: margin {m} px")
        boxes.append((x, top, x + w, top + hh, s["n"]))
        if not ok: check(False, f"{os.path.basename(png)}: {s['n']} rect/margin")
    for i in range(len(boxes)):
        for k in range(i + 1, len(boxes)):
            A, B = boxes[i], boxes[k]
            if A[0] < B[2] and B[0] < A[2] and A[1] < B[3] and B[1] < A[3]:
                check(False, f"{os.path.basename(png)}: {A[4]} overlaps {B[4]}")
    check(True, f"{os.path.basename(png)}: {len(sp)} sprites checked")

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("dir")
    ap.add_argument("--day-p90", nargs=2, type=float, default=[.40, .50])
    ap.add_argument("--night-p90", nargs=2, type=float, default=[.32, .42])
    ap.add_argument("--night", type=int, nargs="*", default=[4])
    ap.add_argument("--forbid-hue", nargs=2, type=float, action="append")
    ap.add_argument("--atlas-margin", type=int, default=6)
    a = ap.parse_args()
    forbid = [tuple(x) for x in (a.forbid_hue or [[345, 15]])]
    for v in range(1, 5):
        d = os.path.join(a.dir, f"v{v}")
        if not os.path.isdir(d): print(f"(no v{v} folder)"); continue
        band = tuple(a.night_p90) if v in a.night else tuple(a.day_p90)
        for role in ("sky", "far", "mid", "flow"):
            p = os.path.join(d, role + ".png")
            if os.path.exists(p): tile(p, band, forbid, role)
            else: check(False, f"{p} missing")
        check(os.path.exists(os.path.join(d, "mask.txt")), f"v{v}/mask.txt exists (ground_masks.py output)")
    for f in sorted(os.listdir(a.dir)):
        if f.endswith(".png"): atlas(os.path.join(a.dir, f), a.atlas_margin)
    print("RESULT:", "FAIL" if fails else "PASS", f"({fails} failures)")
    sys.exit(1 if fails else 0)
