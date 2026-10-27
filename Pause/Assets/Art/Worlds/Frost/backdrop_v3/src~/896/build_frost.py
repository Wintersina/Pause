"""Frost ground tiles 896x1792 (1.75x the old 512x1024): two Codex repaints (A top, B bottom, 1254 px) per variant,
min-error cuts through the overlaps (vertical join, vertical wrap, horizontal wrap), pixelated to a 2x2 art grid, graded
(median lift, lava shoulder, calm lane). usage: build_ember.py <src dir> <out dir> [variants...]"""
import sys, os
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter
pass #, '/private/tmp/claude-501/-Users-sina-Developer-Pause/bdb30dea-5c57-4096-8366-274437827548/scratchpad/salt-flats')
W, H = 896, 1792
OV, OX = 112, 56
S = 1008
# median value target of the mid, lava shoulder start, cap
MED = {"v1": .52, "v2": .52, "v3": .52, "v4": .52}
LANE = .62
SHOULDER, CAP = .80, .93

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
    a = pixelate(np.asarray(im)).astype(np.int16)
    pk = (a[..., 0] - a[..., 1] > 6) & (a[..., 2] - a[..., 1] > 6)   # no pink / magenta in Frost
    a[..., 0] = np.where(pk, a[..., 1], a[..., 0]); a[..., 2] = np.where(pk, np.minimum(a[..., 2], a[..., 1] + 40), a[..., 2])
    return a.astype(np.uint8)

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
    cv[:896] = A[OV:S]; cv[896:] = B[OV:S]
    cv[784:896] = blend_rows(A[896:S], B[0:OV])
    cv[1680:1792] = blend_rows(B[896:S], A[0:OV])
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
    # bright, clear grade: gamma on the value channel to a median target (hue kept), then a saturated-blue floor so nothing is black
    rgb = u8.astype(np.float32) / 255
    val = rgb.max(-1, keepdims=True)
    med = float(np.median(val))
    target = MED[v] + {"mid": 0, "far": .02, "sky": .06}[role]
    g = np.log(target) / np.log(max(med, 1e-3))
    nv = val ** g
    nv = np.where(nv > SHOULDER, SHOULDER + (nv - SHOULDER) * .5, nv)
    nv = np.minimum(nv, CAP)
    rgb = rgb * (nv / np.maximum(val, 1e-3))
    rgb = np.maximum(rgb, np.array([.09, .19, .40], np.float32) * FLOOR[role])
    if role != "mid":
        gray = rgb.mean(-1, keepdims=True)
        rgb = gray + (rgb - gray) * (.92 if role == "far" else .85)
    return np.clip(np.rint(rgb * 255), 0, 255).astype(np.uint8)

FLOOR = {"mid": 1.0, "far": 1.0, "sky": 1.1}

FLOWSRC = "/private/tmp/claude-501/-Users-sina-Developer-Pause/bdb30dea-5c57-4096-8366-274437827548/scratchpad/frost-896/cand/v%s/flow.png"
def flow_unused(v):
    return Image.open(FLOWSRC % v).convert("RGBA")

def main():
    src, out = Path(sys.argv[1]), Path(sys.argv[2])
    vs = sys.argv[3:] or ["v1", "v2", "v3", "v4"]
    for v in vs:
        MS = Path("/private/tmp/claude-501/-Users-sina-Developer-Pause/bdb30dea-5c57-4096-8366-274437827548/scratchpad/frost-896/cand/mid_masksrc") / v / "mid.png"
        if (src / f"{v}_a.png").exists() and (src / f"{v}_b.png").exists():
            base = build_canvas(load(src / f"{v}_a.png"), load(src / f"{v}_b.png"))
        elif MS.exists():
            base = np.asarray(Image.open(MS).convert("RGB"))   # leftover ungraded seamless Codex mid
        else: print("skip", v); continue
        (out / v).mkdir(parents=True, exist_ok=True)
        for role in ("mid", "far", "sky"):
            layer = base
            if role == "far": layer = np.roll(base, (H // 3, W // 3), axis=(0, 1))[:, ::-1]
            if role == "sky": layer = np.roll(base, (H * 2 // 3, -W // 4), axis=(0, 1))
            g = grade(np.ascontiguousarray(layer), v, role)
            g=np.dstack([g, np.full(g.shape[:2], 255, np.uint8)]); g[-1]=g[0]; Image.fromarray(g, "RGBA").save(out / v / f"{role}.png")
        # flow keeps its variant digit
        flow(v[1:]).save(out / v / "flow.png")
        print("built", v)
if __name__ == "__main__": main()
# NOTE: the shipped flow.png is the old 512x1024 overlay resized to 896x1792 with Lanczos (old_512~/vN/flow.png), see WorldBackdropImport / PR body.
