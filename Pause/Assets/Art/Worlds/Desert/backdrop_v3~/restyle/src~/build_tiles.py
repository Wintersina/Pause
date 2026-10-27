"""Desert painted-ground tiles, hi-res PIXEL ART: 1024x2048 sky / far / mid, seamless on both axes.

Usage: python3 build_tiles.py <variant: v1 day dunes | v3 salt flat | v4 night dunes> <src dir with mid_a.png, mid_b.png> <out dir>
(writes <out dir>/<variant>/{sky,far,mid}.png)

`mid` is built from two Codex 1254 px paintings (A over B), one painted texel -> one tile texel before the pixel pass. The A|B join and
the wrap seams are cut along a minimum-error path through real painted content (no crossfade). `far` and `sky` are the same painting
shifted / mirrored, hazier and darker. Each layer is graded (value p90 target, calm central lane, forbidden hues mapped to the
world's accent) and then run through the PIXEL PASS (`pixelate`): unsharp (cracks and crystal edges survive), block-average to a 4 px
cell grid (a 256 x 512 pixel image, ~1.5 x the 1/64 u sprite pixel), snap to a 96-colour palette, upscale nearest. Every step is per
cell and the cell grid divides the period, so the tile stays seamless. Import point-filtered.
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

W, H = 1024, 2048
VARIANTS = (sys.argv[1],) if len(sys.argv) > 1 else ("v3",)     # v1 day dunes, v3 salt flat, v4 night dunes
SOURCES = {v: ("mid_a.png", "mid_b.png") for v in ("v1", "v3", "v4")}
# target HSV value p90 of the opaque pixels (mid); far is .05 lower, sky .10
P90 = {"v1": .49, "v3": .465, "v4": .40}
NIGHT = {"v4"}
ACCENT_HUE = {"v1": 36.0, "v3": 36.0, "v4": 215.0}   # pink / purple / red texels are rotated here (ochre by day, blue at night)
HAZE = {"v1": (.20, .17, .12), "v3": (.20, .17, .12), "v4": (.04, .07, .13)}
CELL, COLORS = 4, 96


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


def pixelate(rgb: np.ndarray, cell: int = CELL, colors: int = COLORS, sharpen: float = 1.0) -> np.ndarray:
    """The pixel pass: (H, W, 3) uint8 -> the same size drawn as hard-edged cell x cell pixels in a limited palette."""
    from PIL import ImageFilter
    h, w = rgb.shape[:2]
    f = rgb.astype(np.float32)
    blur = np.asarray(Image.fromarray(rgb).filter(ImageFilter.GaussianBlur(cell * .9))).astype(np.float32)
    f = np.clip(f + (f - blur) * sharpen, 0, 255)
    g = f.reshape(h // cell, cell, w // cell, cell, 3).mean((1, 3))
    q = Image.fromarray(g.astype(np.uint8)).quantize(colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    small = np.asarray(q.convert("RGB"))
    return np.repeat(np.repeat(small, cell, 0), cell, 1)


def grade(rgb_u8: np.ndarray, variant: str, role: str) -> np.ndarray:
    """Tone to the p90 target, calm middle, keep the painted colours (only gently pulled into the storm palette)."""
    rgb = rgb_u8.astype(np.float32) / 255
    h, s, v = rgb2hsv(rgb)
    target = P90[variant] - {"mid": 0, "far": .05, "sky": .10}[role]
    p90 = np.percentile(v, 90)
    gain = target / max(p90, 1e-3)
    v = np.clip(v * gain, 0, 1)
    # forbidden: pink / magenta / purple / strong red -> pull to ochre (Desert palette: ivory, gold, ochre, teal)
    bad = (((h > 270) & (h < 352)) | (h < 10) | (h >= 352)) & (s > .22)
    h = np.where(bad, ACCENT_HUE[variant], h)
    s = np.where(bad, np.minimum(s, .6), s)
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
        haze = np.array(HAZE[variant], np.float32)
        k = .08 if role == "far" else .16
        rgb = rgb * (1 - k) + haze * k
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
    src = Path(sys.argv[2])
    out = Path(sys.argv[3])
    for vi, v in enumerate(VARIANTS):
        if not (src / SOURCES[v][1]).exists():
            print("skip", v); continue
        a = native_crop(np.asarray(Image.open(src / SOURCES[v][0]).convert("RGB")))
        b = native_crop(np.asarray(Image.open(src / SOURCES[v][1]).convert("RGB")))
        base = build_canvas(a, b)
        (out / v).mkdir(parents=True, exist_ok=True)
        for role in ("mid", "far", "sky"):
            dx, dy = shift_for(role, v)
            layer = np.roll(base, (dy, dx), axis=(0, 1)) if role != "mid" else base
            if role == "far":
                layer = layer[:, ::-1]                     # mirrored: a different silhouette from the mid layer
            g = pixelate(grade(np.ascontiguousarray(layer), v, role))
            rgba = np.dstack([g, np.full(g.shape[:2], 255, np.uint8)])
            Image.fromarray(rgba, "RGBA").save(out / v / f"{role}.png")
        print("built", v)


if __name__ == "__main__":
    main()
