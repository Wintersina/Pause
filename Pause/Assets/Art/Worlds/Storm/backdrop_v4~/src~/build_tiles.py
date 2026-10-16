"""Storm backdrop v4 ground tiles: 1024x2048 (double the old 512x1024), seamless on both axes.

Each variant's `mid` is built from two image-generated 1024x1024 paintings (vN_a over vN_b), no resampling
(one painted texel -> one tile texel). The A|B join and the wrap seams are hidden by an organic, noisy
hard-edged mask (cloud-shaped, not a crossfade-blur). `far` and `sky` are the same material hazier
(shifted, darker, calmer). The middle of the width is calmer than the edges (gameplay readability).
`flow` is a transparent rain / spark strip drawn procedurally at 2x.

Usage: python3 build_tiles.py <src~ dir with v1_a.png ...> <out dir (gets v1..v4/{sky,far,mid,flow}.png)>
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

W, H = 1024, 2048
VARIANTS = ("v1", "v2")          # v3 stays the original 512x1024 set; v4 (night) was dropped by the user
SOURCES = {v: (f"{v}_a.png", f"{v}_b.png") for v in VARIANTS}
# target HSV value p90 of the opaque pixels (mid), sky/far are .05 lower each
P90 = {"v1": .45, "v2": .46, "v3": .44, "v4": .37}
NIGHT = {"v4"}


def native_crop(a: np.ndarray) -> np.ndarray:
    """The generator returns ~1254 px squares: take a (1024+OV) square so every painted texel stays 1:1 (no resampling)."""
    n = 1024 + 200
    h, w = a.shape[:2]
    if h < n or w < n:
        raise ValueError("source smaller than %d px: %s" % (n, a.shape))
    y0, x0 = (h - n) // 2, (w - n) // 2
    return a[y0:y0 + n, x0:x0 + n]


def smooth(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


def lowfreq_noise(shape, seed, cell=64):
    """Seamless-agnostic value noise, bilinear from a coarse random grid, 0..1."""
    rng = np.random.default_rng(seed)
    h, w = shape
    gh, gw = h // cell + 3, w // cell + 3
    g = rng.random((gh, gw)).astype(np.float32)
    ys = np.arange(h) / cell
    xs = np.arange(w) / cell
    y0 = ys.astype(int)
    x0 = xs.astype(int)
    fy = smooth(ys - y0)[:, None]
    fx = smooth(xs - x0)[None, :]
    a = g[y0][:, x0]
    b = g[y0][:, x0 + 1]
    c = g[y0 + 1][:, x0]
    d = g[y0 + 1][:, x0 + 1]
    top = a * (1 - fx) + b * fx
    bot = c * (1 - fx) + d * fx
    return top * (1 - fy) + bot * fy


OV = 200          # overlap (px) used to hide every join with a minimum-error cut through REAL painted content


def min_cut(err: np.ndarray) -> np.ndarray:
    """err: (O, L). Path r[i] in [0, O) minimising the summed error along L, |r[i]-r[i-1]| <= 1."""
    O, L = err.shape
    cost = np.empty_like(err, dtype=np.float64)
    cost[:, 0] = err[:, 0]
    for i in range(1, L):
        p = cost[:, i - 1]
        best = np.minimum(p, np.minimum(np.concatenate(([np.inf], p[:-1])), np.concatenate((p[1:], [np.inf]))))
        cost[:, i] = err[:, i] + best
    r = np.empty(L, np.int32)
    r[-1] = int(np.argmin(cost[:, -1]))
    for i in range(L - 2, -1, -1):
        j = r[i + 1]
        lo, hi = max(0, j - 1), min(O, j + 2)
        r[i] = lo + int(np.argmin(cost[lo:hi, i]))
    return r


def blend_rows(top: np.ndarray, bottom: np.ndarray) -> np.ndarray:
    """top / bottom: (O, W, 3) the same canvas rows seen from two paintings; cut along a min-error path (top above it)."""
    err = np.abs(top.astype(np.float32) - bottom.astype(np.float32)).sum(-1)
    err = (err[:-1] + err[1:]) * .5 if False else err
    # soften the error a little so the path avoids busy lightning / rim edges (error of a 5 px neighbourhood)
    k = np.ones(5, np.float32) / 5
    err = np.apply_along_axis(lambda v: np.convolve(v, k, "same"), 1, err)
    r = min_cut(err)
    mask = (np.arange(top.shape[0])[:, None] >= r[None, :])[..., None]
    return np.where(mask, bottom, top)


def build_canvas(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    """a, b: (1024+OV, 1024+OV, 3) crops. Returns a seamless 2048x1024 canvas (vertical cycle A over B, then horizontal wrap)."""
    H1 = 1024
    C = W + OV
    canvas = np.zeros((2 * H1, C, 3), np.uint8)
    canvas[:H1] = a[:H1, :C]
    canvas[H1:] = b[:H1, :C]
    # join 1 (canvas rows H1..H1+OV): a's extra rows over b's first rows
    canvas[H1:H1 + OV] = blend_rows(a[H1:H1 + OV, :C], b[:OV, :C])
    # join 2 / wrap (canvas rows 0..OV): b's extra rows (they continue past row 2047) over a's first rows
    canvas[:OV] = blend_rows(b[H1:H1 + OV, :C], a[:OV, :C])
    # horizontal wrap: the extra columns continue the right edge; cut a vertical min-error path through the left strip
    left = canvas[:, :OV].astype(np.float32)
    extra = canvas[:, W:W + OV].astype(np.float32)
    err = np.abs(left - extra).sum(-1).T                      # (OV cols, 2048 rows)
    k = np.ones(5, np.float32) / 5
    err = np.apply_along_axis(lambda v: np.convolve(v, k, "same"), 1, err)
    r = min_cut(err)                                          # r[row] = column where the cut sits
    cols = np.arange(OV)[None, :]
    use_extra = cols < r[:, None]
    strip = np.where(use_extra[..., None], canvas[:, W:W + OV], canvas[:, :OV])
    out = canvas[:, :W].copy()
    out[:, :OV] = strip
    return out


def tileable(canvas: np.ndarray) -> np.ndarray:
    return canvas


def rgb2hsv(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1)
    mn = rgb.min(-1)
    d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    h = np.zeros_like(mx)
    dd = np.maximum(d, 1e-6)
    h = np.where(mx == r, ((g - b) / dd) % 6, h)
    h = np.where((mx == g) & (mx != r), (b - r) / dd + 2, h)
    h = np.where((mx == b) & (mx != r) & (mx != g), (r - g) / dd + 4, h)
    h = np.where(d < 1e-6, 0, h) * 60
    return h, s, mx


def hsv2rgb(h, s, v):
    h = (h % 360) / 60
    i = np.floor(h).astype(int) % 6
    f = h - np.floor(h)
    p = v * (1 - s)
    q = v * (1 - s * f)
    t = v * (1 - s * (1 - f))
    r = np.choose(i, [v, q, p, p, t, v])
    g = np.choose(i, [t, v, v, q, p, p])
    b = np.choose(i, [p, p, t, v, v, q])
    return np.stack((r, g, b), -1)


def box_mean(a, k):
    """Separable running mean (for the calm-band contrast compression only)."""
    pad = k // 2
    c = np.cumsum(np.pad(a, ((pad + 1, pad), (0, 0), (0, 0)), mode="wrap"), axis=0)
    a = (c[k:] - c[:-k]) / k
    c = np.cumsum(np.pad(a, ((0, 0), (pad + 1, pad), (0, 0)), mode="wrap"), axis=1)
    return (c[:, k:] - c[:, :-k]) / k


def grade(rgb_u8: np.ndarray, variant: str, role: str) -> np.ndarray:
    """Tone to the p90 target, calm middle, keep the painted colours (only gently pulled into the storm palette)."""
    rgb = rgb_u8.astype(np.float32) / 255
    h, s, v = rgb2hsv(rgb)
    target = P90[variant] - {"mid": 0, "far": .05, "sky": .10}[role]
    p90 = np.percentile(v, 90)
    gain = target / max(p90, 1e-3)
    v = np.clip(v * gain, 0, 1)
    # forbidden: pink / magenta / red fields (hue 290..350 and < 12 at real saturation) -> pull towards indigo / amber
    pink = (h > 288) & (h < 352) & (s > .22)
    h = np.where(pink, 268.0 if variant != "v4" else 262.0, h)
    s = np.where(pink, np.minimum(s, .55), s)
    red = ((h < 14) | (h >= 352)) & (s > .45)
    h = np.where(red, 30.0, h)
    # violet stays moderate (the violet capacitor pickup owns hue ~259)
    viol = (h > 248) & (h < 290)
    s = np.where(viol, np.minimum(s, .62), s)
    # calm middle: compress contrast and saturation, cap the brights in the central band of the width
    x = (np.arange(W)[None, :] / (W - 1) - .5)
    calm = np.exp(-(x / .2) ** 2)[..., None] if role == "mid" else np.full((1, W, 1), .0, np.float32)
    calm = np.broadcast_to(calm, (H, W, 1)).astype(np.float32)
    rgb = hsv2rgb(h, np.clip(s, 0, 1), v)
    if role == "mid":
        local = box_mean(rgb, 49)
        rgb = local + (rgb - local) * (1 - .38 * calm)
        gray = rgb.mean(-1, keepdims=True)
        rgb = gray + (rgb - gray) * (1 - .20 * calm)
        cap = 1 - .30 * calm
        mxv = rgb.max(-1, keepdims=True)
        scale = np.minimum(1.0, (cap * .8 + .2) / np.maximum(mxv, 1e-6))
        rgb = rgb * scale
    if role in ("far", "sky"):
        gray = rgb.mean(-1, keepdims=True)
        rgb = gray + (rgb - gray) * (.85 if role == "far" else .75)
        # hazier: pull a little towards the deep indigo of the palette
        haze = np.array([.07, .07, .22], np.float32)
        k = .22 if role == "far" else .38
        rgb = rgb * (1 - k) + haze * k
    # last pass: mixing amber into indigo haze can land on magenta; pink is Space-only, so rotate any such texel to indigo
    rgb = np.clip(rgb, 0, 1)
    h2, s2, v2 = rgb2hsv(rgb)
    pink2 = (h2 > 286) & (h2 < 352) & (s2 > .2)
    if pink2.any():
        rgb = np.where(pink2[..., None], hsv2rgb(np.full_like(h2, 262.0), np.minimum(s2, .55), v2), rgb)
    return np.clip(np.rint(rgb * 255), 0, 255).astype(np.uint8)


def rain_strip(variant: str, seed: int) -> np.ndarray:
    """Transparent 1024x2048 strip: slanted wind-rain streaks (pixel-stepped alpha) + a few lightning sparks."""
    rng = np.random.default_rng(seed)
    img = np.zeros((H, W, 4), np.uint8)
    lean = .29
    night = variant in NIGHT
    pal = [((214, 233, 255), .55), ((140, 214, 236), .30), ((176, 190, 255), .15)]
    count = 120 if variant != "v2" else 150
    for _ in range(count):
        x0 = rng.integers(0, W)
        y0 = rng.integers(0, H)
        length = int(rng.integers(70, 240))
        thick = 2 if rng.random() < .75 else 3
        r = rng.random()
        acc = 0
        col = pal[0][0]
        for c, p in pal:
            acc += p
            if r <= acc:
                col = c
                break
        amber = rng.random() < .06
        if amber:
            col = (255, 214, 150)
        peak = (170 if night else 150) + rng.integers(0, 80)
        for i in range(length):
            f = i / length
            alpha = int(peak * (.25 + .75 * f) // 24 * 24 + 23) if f > .15 else int(peak * .3)
            y = (y0 + i) % H
            xc = x0 + lean * i
            for t in range(thick):
                # a bright core and dimmer, slightly jittered sides: neighbouring texels never repeat exactly
                a = alpha if t == 0 else int(alpha * (.62 - .1 * t)) + int(rng.integers(-9, 10))
                a = int(min(255, max(8, a)))
                j = int(rng.integers(-7, 8))
                c3 = (min(255, max(0, col[0] + j)), min(255, max(0, col[1] + (j // 2))), min(255, max(0, col[2] - j // 2)))
                x = int(xc + t) % W
                if a > img[y, x, 3]:
                    img[y, x] = (c3[0], c3[1], c3[2], a)
    # tiny electric sparks inside the wind (cross-shaped 5 px glints)
    for _ in range(26):
        x, y = int(rng.integers(8, W - 8)), int(rng.integers(8, H - 8))
        col = (244, 248, 255) if rng.random() < .75 else (126, 204, 255)
        for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
            img[y + dy, x + dx] = (col[0], col[1], col[2], 235 if (dx, dy) == (0, 0) else 140)
        for dx, dy in ((2, 0), (-2, 0), (0, 2), (0, -2)):
            img[y + dy, x + dx] = (col[0], col[1], col[2], 70)
    img[:, -1] = img[:, 0]
    img[-1] = img[0]
    return img


def shift_for(role: str, variant: str):
    # far / sky read a different part of the same material, so the layers do not line up
    return {"mid": (0, 0), "far": (W // 3, H // 3), "sky": (-W // 4, H * 2 // 3)}[role]


def main() -> None:
    src = Path(sys.argv[1])
    out = Path(sys.argv[2])
    for vi, v in enumerate(VARIANTS):
        a = native_crop(np.asarray(Image.open(src / SOURCES[v][0]).convert("RGB")))
        b = native_crop(np.asarray(Image.open(src / SOURCES[v][1]).convert("RGB")))
        base = build_canvas(a, b)
        (out / v).mkdir(parents=True, exist_ok=True)
        for role in ("mid", "far", "sky"):
            dx, dy = shift_for(role, v)
            layer = np.roll(base, (dy, dx), axis=(0, 1)) if role != "mid" else base
            if role == "far":
                layer = layer[:, ::-1]                     # mirrored: a different silhouette from the mid layer
            g = grade(np.ascontiguousarray(layer), v, role)
            g[:, -1] = g[:, 0]
            g[-1] = g[0]
            rgba = np.dstack([g, np.full(g.shape[:2], 255, np.uint8)])
            Image.fromarray(rgba, "RGBA").save(out / v / f"{role}.png")
        Image.fromarray(rain_strip(v, 900 + vi), "RGBA").save(out / v / "flow.png")
        print("built", v)


if __name__ == "__main__":
    main()
