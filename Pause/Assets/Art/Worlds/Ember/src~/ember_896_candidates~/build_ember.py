"""Ember ground tiles 1024x2048 (2x the old 512x1024): two Codex repaints (A top, B bottom, 1254 px) per variant,
min-error cuts through the overlaps (vertical join, vertical wrap, horizontal wrap), pixelated to a 2x2 art grid, graded
(median lift, lava shoulder, calm lane). usage: build_ember.py <src dir> <out dir> [variants...]"""
import sys, os
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter
W, H = 896, 1792
OV, OX = 112, 56
S = 1008
# median value target of the mid, lava shoulder start, cap
MED = {"v1": .36, "v2": .36, "v3": .37, "v4": .31}
LANE = .62
SHOULDER = .35
CAPS = {"v1": .46, "v2": .50, "v3": .47, "v4": .36}   # = the old art's brightest lava, so fire-coloured boss shots keep standing out
V1_FACTOR = {"mid": .88, "far": .74, "sky": .72}   # v1 as-drawn caps (WorldBackdropTest)

def pixelate(rgb, cell=1, colors=160):
    h, w = rgb.shape[:2]
    im = Image.fromarray(rgb)
    bl = np.asarray(im.filter(ImageFilter.GaussianBlur(cell * .9))).astype(np.float32)
    f = np.clip(rgb.astype(np.float32) + (rgb.astype(np.float32) - bl) * .5, 0, 255)
    g = f.reshape(h // cell, cell, w // cell, cell, 3).mean((1, 3))
    q = Image.fromarray(g.astype(np.uint8)).quantize(colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    return np.repeat(np.repeat(np.asarray(q.convert("RGB")), cell, 0), cell, 1)

def load(p):
    im = Image.open(p).convert("RGB").resize((S, S), Image.LANCZOS)
    return pixelate(np.asarray(im))

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

def smooth_err(err):
    k = np.ones(5, np.float32) / 5
    return np.apply_along_axis(lambda v: np.convolve(v, k, "same"), 1, err)

def blend_rows(top, bottom):
    err = smooth_err(np.abs(top.astype(np.float32) - bottom.astype(np.float32)).sum(-1))
    r = min_cut(err)
    mask = (np.arange(top.shape[0])[:, None] >= r[None, :])[..., None]
    return np.where(mask, bottom, top)

def build_canvas(A, B):
    A = A[:, OX:]; B = B[:, OX:]                      # tile cols 0..1088
    C = W + OX
    cv = np.zeros((H, C, 3), np.uint8)
    cv[:896] = A[112:1008]; cv[896:] = B[112:1008]
    cv[784:896] = blend_rows(A[896:1008], B[0:112])
    cv[1680:1792] = blend_rows(B[896:1008], A[0:112])
    left = cv[:, :OX].astype(np.float32); extra = cv[:, W:W + OX].astype(np.float32)
    err = smooth_err(np.abs(left - extra).sum(-1).T)  # (OX cols, H rows)
    r = min_cut(err)
    use_extra = np.arange(OX)[None, :] < r[:, None]
    out = cv[:, :W].copy()
    out[:, :OX] = np.where(use_extra[..., None], cv[:, W:W + OX], cv[:, :OX])
    return out

def rgb2hsv(rgb):
    mx = rgb.max(-1); mn = rgb.min(-1); d = mx - mn
    return mx, d

def box_mean(a, k):
    pad = k // 2
    c = np.cumsum(np.pad(a, ((pad + 1, pad), (0, 0), (0, 0)), mode="wrap"), axis=0); a = (c[k:] - c[:-k]) / k
    c = np.cumsum(np.pad(a, ((0, 0), (pad + 1, pad), (0, 0)), mode="wrap"), axis=1); return (c[:, k:] - c[:, :-k]) / k

def grade(u8, v, role):
    rgb = u8.astype(np.float32) / 255
    val = rgb.max(-1, keepdims=True)
    med = float(np.median(val))
    target = MED[v] - {"mid": 0, "far": .03, "sky": .06}[role]
    gain = target / max(med, 1e-3)
    nv = val * gain
    # lava shoulder: compress everything above SHOULDER, hard cap
    nv = np.where(nv > SHOULDER, SHOULDER + (nv - SHOULDER) * .40, nv)
    nv = np.minimum(nv, CAPS[v])
    rgb = rgb * (nv / np.maximum(val, 1e-6))
    if role == "mid":
        x = np.arange(W)[None, :] / (W - 1) - .5
        calm = np.broadcast_to(np.exp(-(x / .2) ** 2)[..., None], (H, W, 1)).astype(np.float32)
        local = box_mean(rgb, 49)
        rgb = local + (rgb - local) * (1 - .30 * calm)
        cap = 1 - .25 * calm
        mxv = rgb.max(-1, keepdims=True)
        rgb = rgb * np.minimum(1.0, (cap * .75 + .25) / np.maximum(mxv, 1e-6))
        # the play lane (the readability guard: enemy body 2.0:1 -> lane median luminance <= ~.026, value ~.21): darker in the
        # central 54% of the width, smooth ramp to the full-bright side bands
        ax = np.abs(x)
        t = np.clip((ax - .27) / .10, 0, 1); t = t * t * (3 - 2 * t)
        lane = (LANE + (1 - LANE) * t)[..., None]
        rgb = rgb * np.broadcast_to(lane, (H, W, 1)).astype(np.float32)
    else:
        gray = rgb.mean(-1, keepdims=True)
        rgb = gray + (rgb - gray) * (.85 if role == "far" else .75)
    if v == "v1": rgb = rgb * V1_FACTOR[role]
    return np.clip(np.rint(rgb * 255), 0, 255).astype(np.uint8)

def flow(v):
    f = Image.open(FLOW % v).convert("RGBA").resize((W, H), Image.LANCZOS)
    a = np.asarray(f).astype(np.float32)
    pass
    return Image.fromarray(a.astype(np.uint8), "RGBA")

FLOW = "/Users/sina/Developer/Pause-worktrees/ember-896/Pause/Assets/Art/Backgrounds/Resources/Worlds/Ember/Backdrop3/v%s/flow.png"

def main():
    src, out = Path(sys.argv[1]), Path(sys.argv[2])
    vs = sys.argv[3:] or ["v1", "v2", "v3", "v4"]
    for v in vs:
        if not ((src / f"{v}_a.png").exists() and (src / f"{v}_b.png").exists()): print("skip", v); continue
        base = build_canvas(load(src / f"{v}_a.png"), load(src / f"{v}_b.png"))
        (out / v).mkdir(parents=True, exist_ok=True)
        for role in ("mid", "far", "sky"):
            layer = base
            if role == "far": layer = np.roll(base, (H // 3, W // 3), axis=(0, 1))[:, ::-1]
            if role == "sky": layer = np.roll(base, (H * 2 // 3, -W // 4), axis=(0, 1))
            g = grade(np.ascontiguousarray(layer), v, role); g[-1] = g[0]
            Image.fromarray(np.dstack([g, np.full(g.shape[:2], 255, np.uint8)]), "RGBA").save(out / v / f"{role}.png")
        # flow keeps its variant digit
        flow(v[1:]).save(out / v / "flow.png")
        print("built", v)
main()
