"""verify_preview_v34.py <tiles dir (v3,v4)> <installed Storm Backdrop3 dir (v1,v2)> <preview out png>
Checks the new 1024x2048 tiles (size, RGBA, wrap seams both axes, p90, flow opacity, hue audit) and writes
preview_variants.png: columns v1..v4 each sky|far|mid|flow at 1/2 scale, plus a 1080x2400 composite per variant."""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

tiles, inst, out = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
BG = (11, 11, 26)

def hsv(a):
    rgb = a[..., :3].astype(np.float32) / 255
    mx, mn = rgb.max(2), rgb.min(2); d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.zeros_like(mx); m = d > 0
    rm = m & (mx == r); gm = m & (mx == g) & ~rm; bm = m & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6; h[gm] = (b - r)[gm] / d[gm] + 2; h[bm] = (r - g)[bm] / d[bm] + 4
    return h * 60, s, mx

fails = 0
print("file      size        mode  seamX seamY p90    opaque  green  purple red   status")
for v, (lo, hi) in (("v3", (.32, .42)), ("v4", (.40, .50))):
    for role in ("sky", "far", "mid", "flow"):
        im = Image.open(tiles / v / f"{role}.png"); a = np.asarray(im)
        bad = []
        if im.size != (1024, 2048): bad.append("size")
        if im.mode != "RGBA": bad.append("mode")
        sx = int(np.abs(a[:, 0].astype(int) - a[:, -1].astype(int)).max())
        sy = int(np.abs(a[0].astype(int) - a[-1].astype(int)).max())
        # true wrap continuity: wrapped neighbour step vs typical interior step
        wx = np.abs(a[:, 0].astype(int) - a[:, -1].astype(int)).mean(); ix = np.abs(a[:, 1:].astype(int) - a[:, :-1].astype(int)).mean()
        wy = np.abs(a[0].astype(int) - a[-1].astype(int)).mean(); iy = np.abs(a[1:].astype(int) - a[:-1].astype(int)).mean()
        if sx or sy: bad.append("seam")
        op = a[..., 3] > 0; frac = float(op.mean())
        h, s, val = hsv(a)
        p90 = float(np.percentile(val[op], 90))
        if role == "flow":
            if not .005 <= frac <= .25: bad.append("opacity")
        else:
            if frac < 1: bad.append("not opaque")
            off = {"mid": 0, "far": .05, "sky": .10}[role]
            if not (lo - off - .03 <= p90 <= hi): bad.append("p90")
        sat = op & (s > .18)
        gr = float((sat & (h > 95) & (h < 195)).sum() / max(op.sum(), 1))
        pu = float((op & (s > .30) & (h > 250) & (h < 300)).sum() / max(op.sum(), 1))
        rd = float((op & (s > .5) & ((h < 15) | (h > 340))).sum() / max(op.sum(), 1))
        if gr > .005 or rd > .002: bad.append("hue")
        if pu > .005: bad.append("purple")
        print(f"{v}/{role:<5} {str(im.size):<11} {im.mode} {sx:>4} {sy:>5} {p90:.3f} {frac:.4f} {gr:.4f} {pu:.4f} {rd:.4f} {'FAIL ' + ','.join(bad) if bad else 'ok'}  wrapstep x {wx:.1f}/{ix:.1f} y {wy:.1f}/{iy:.1f}")
        fails += bool(bad)

def load(v, role):
    p = (inst if v in ("v1", "v2") else tiles) / v / f"{role}.png"
    return Image.open(p).convert("RGBA")

cols = []
for v in ("v1", "v2", "v3", "v4"):
    row = []
    for role in ("sky", "far", "mid", "flow"):
        t = load(v, role); bg = Image.new("RGBA", t.size, BG + (255,)); bg.alpha_composite(t)
        row.append(bg.convert("RGB").resize((256, 512), Image.NEAREST))
    cols.append(row)
W = 4 * 4 * 256 + 3 * 16
sheet = Image.new("RGB", (W, 512 + 16 + 1200), BG)
for i, row in enumerate(cols):
    for j, im in enumerate(row):
        sheet.paste(im, (i * (4 * 256 + 16) + j * 256, 0))
# phone composite 1080x2400 per variant: sky, far, mid layers stacked (the way they are drawn), flow on top
for i, v in enumerate(("v1", "v2", "v3", "v4")):
    canvas = Image.new("RGBA", (1024, 2048), BG + (255,))
    for role, alpha in (("sky", 1.0), ("far", .55), ("mid", .8)):
        t = load(v, role)
        if alpha < 1: t.putalpha(t.getchannel("A").point(lambda x: int(x * alpha)))
        canvas.alpha_composite(t)
    canvas.alpha_composite(load(v, "flow"))
    ph = canvas.convert("RGB").resize((1080 // 2 - 0, 2400 // 2), Image.NEAREST) if False else canvas.convert("RGB").crop((0, 0, 1024, 2048)).resize((480, 960), Image.NEAREST)
    sheet.paste(ph, (i * (4 * 256 + 16) + 270, 528))
sheet.save(out)
print("preview", out, sheet.size, "FAILS", fails)
sys.exit(1 if fails else 0)
