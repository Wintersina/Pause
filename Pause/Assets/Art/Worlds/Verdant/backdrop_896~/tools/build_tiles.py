"""Verdant backdrop tiles 896x1792 from two Codex 1254px repaints per variant (vN_a over vN_b), min-error cut joins,
graded by quantile matching to the OLD layer of the same role (so brightness/saturation limits stay as shipped).
usage: build_tiles.py <src dir> <old Backdrop3 dir> <out dir>"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image
W, H, OV = 896, 1792, 200
H1 = H // 2

def native_crop(a):
    n = W + OV
    h, w = a.shape[:2]
    y0, x0 = (h - n) // 2, (w - n) // 2
    return a[y0:y0 + n, x0:x0 + n]

def min_cut(err):
    O, L = err.shape
    cost = np.empty_like(err, dtype=np.float64); cost[:, 0] = err[:, 0]
    for i in range(1, L):
        p = cost[:, i - 1]
        best = np.minimum(p, np.minimum(np.concatenate(([np.inf], p[:-1])), np.concatenate((p[1:], [np.inf]))))
        cost[:, i] = err[:, i] + best
    r = np.empty(L, np.int32); r[-1] = int(np.argmin(cost[:, -1]))
    for i in range(L - 2, -1, -1):
        j = r[i + 1]; lo, hi = max(0, j - 1), min(O, j + 2)
        r[i] = lo + int(np.argmin(cost[lo:hi, i]))
    return r

def smoothed(err):
    k = np.ones(5, np.float32) / 5
    return np.apply_along_axis(lambda v: np.convolve(v, k, "same"), 1, err)

def blend_rows(top, bottom):
    err = smoothed(np.abs(top.astype(np.float32) - bottom.astype(np.float32)).sum(-1))
    r = min_cut(err)
    mask = (np.arange(top.shape[0])[:, None] >= r[None, :])[..., None]
    return np.where(mask, bottom, top)

def build_canvas(a, b):
    C = W + OV
    canvas = np.zeros((2 * H1, C, 3), np.uint8)
    canvas[:H1] = a[:H1, :C]; canvas[H1:] = b[:H1, :C]
    canvas[H1:H1 + OV] = blend_rows(a[H1:H1 + OV, :C], b[:OV, :C])
    canvas[:OV] = blend_rows(b[H1:H1 + OV, :C], a[:OV, :C])
    left = canvas[:, :OV].astype(np.float32); extra = canvas[:, W:W + OV].astype(np.float32)
    err = smoothed(np.abs(left - extra).sum(-1).T)
    r = min_cut(err)
    use_extra = np.arange(OV)[None, :] < r[:, None]
    out = canvas[:, :W].copy()
    out[:, :OV] = np.where(use_extra[..., None], canvas[:, W:W + OV], canvas[:, :OV])
    return out

def rgb2hsv(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1); mn = rgb.min(-1); d = mx - mn; dd = np.maximum(d, 1e-6)
    h = np.zeros_like(mx)
    h = np.where(mx == r, ((g - b) / dd) % 6, h)
    h = np.where((mx == g) & (mx != r), (b - r) / dd + 2, h)
    h = np.where((mx == b) & (mx != r) & (mx != g), (r - g) / dd + 4, h)
    h = np.where(d < 1e-6, 0, h) * 60
    return h, np.where(mx > 0, d / np.maximum(mx, 1e-6), 0), mx

def hsv2rgb(h, s, v):
    h = (h % 360) / 60; i = np.floor(h).astype(int) % 6; f = h - np.floor(h)
    p = v * (1 - s); q = v * (1 - s * f); t = v * (1 - s * (1 - f))
    return np.stack((np.choose(i, [v, q, p, p, t, v]), np.choose(i, [t, v, v, q, p, p]), np.choose(i, [p, p, t, v, v, q])), -1)

def stats(rgb):
    h, s, v = rgb2hsv(rgb)
    return np.quantile(v, np.linspace(0, 1, 257)), float(s.mean())

def grade(new_u8, old_u8):
    new = new_u8.astype(np.float32) / 255; old = old_u8.astype(np.float32) / 255
    oq, os_ = stats(old)
    h, s, v = rgb2hsv(new)
    nq = np.quantile(v, np.linspace(0, 1, 257))
    nq = np.maximum.accumulate(nq + np.arange(257) * 1e-7)
    v2 = np.interp(v, nq, oq)
    # saturation: scale so the mean matches the old layer
    sm = float(s.mean())
    s2 = np.clip(s * (os_ / max(sm, 1e-3)), 0, 1)
    # pink / magenta / purple -> leaf green (Verdant has no pink outside Space)
    bad = (h > 255) & (h < 352) & (s2 > .15)
    h = np.where(bad, 105.0, h)
    return np.clip(np.rint(hsv2rgb(h, s2, v2) * 255), 0, 255).astype(np.uint8)

def old_layer(d, v, role):
    return np.asarray(Image.open(d / f"v{v}" / f"{role}.png").convert("RGB"))

def main():
    src, old, out = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    vs = sys.argv[4:] or ["1", "2", "3", "4"]
    for v in vs:
        pa, pb = src / f"v{v}_a.png", src / f"v{v}_b.png"
        if not (pa.exists() and pb.exists()): print("skip v" + v); continue
        a = native_crop(np.asarray(Image.open(pa).convert("RGB"))); b = native_crop(np.asarray(Image.open(pb).convert("RGB")))
        base = build_canvas(a, b)
        (out / f"v{v}").mkdir(parents=True, exist_ok=True)
        for role, (dx, dy) in (("mid", (0, 0)), ("far", (W // 3, H // 3)), ("sky", (-W // 4, H * 2 // 3))):
            layer = np.roll(base, (dy, dx), axis=(0, 1)) if role != "mid" else base
            if role == "far": layer = layer[:, ::-1]
            g = grade(np.ascontiguousarray(layer), old_layer(old, v, role))
            g[:, -1] = g[:, 0]; g[-1] = g[0]
            Image.fromarray(np.dstack([g, np.full(g.shape[:2], 255, np.uint8)]), "RGBA").save(out / f"v{v}" / f"{role}.png")
        # flow: old strip scaled 1.75x nearest (aligned with the repainted ground)
        fo = Image.open(old / f"v{v}" / "flow.png").convert("RGBA")
        fa = np.asarray(fo)
        ys = np.minimum((np.arange(H) / 1.75).astype(int), fa.shape[0] - 1); xs = np.minimum((np.arange(W) / 1.75).astype(int), fa.shape[1] - 1)
        Image.fromarray(fa[ys][:, xs], "RGBA").save(out / f"v{v}" / "flow.png")
        print("built v" + v)

if __name__ == "__main__": main()
