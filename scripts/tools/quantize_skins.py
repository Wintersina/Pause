#!/usr/bin/env python3
"""Palette-quantize the ship skin sheets (Art/Resources/ShipArt/<Key>/<Key>_<Skin>.bytes).

Usage: quantize_skins.py SRC_DIR OUT_DIR [--colors 256]

The sheets are pixel art with only ~3-4.5k unique RGBA values, so instead of a
generic quantizer we run a count-weighted median cut on the unique colours in
RGBA space (alpha is a real axis, so antialiased outline edges keep their
alpha), refine with a few weighted k-means passes, then map every unique colour
to its nearest palette entry. No dithering: flat colours and 1 px outlines stay
exactly flat. Fully transparent pixels collapse to one transparent entry.
Writes palette PNGs (with tRNS) under the same file names, and prints per-sheet
size, PSNR and max per-channel error.
"""
import sys, os, math
import numpy as np
from PIL import Image


def nearest(cols, pal):
    d = ((cols[:, None, :].astype(np.float64) - pal[None, :, :]) ** 2).sum(2)
    return d.argmin(1)


def split_boxes(cols, w, boxes, n):
    """Weighted median cut starting from the given boxes (each box has one alpha value)."""
    def stats(b):
        c = cols[b].astype(np.float64); ww = w[b].astype(np.float64)
        m = (c * ww[:, None]).sum(0) / ww.sum()
        return c, ww, m
    boxes = list(boxes)
    while len(boxes) < n:
        best, bi = 0.0, -1
        for i, b in enumerate(boxes):
            if len(b) < 2: continue
            c, ww, m = stats(b)
            d2 = ((c - m) ** 2).sum(1)
            score = (d2 * ww).sum() + 200.0 * d2.max()   # the max term gives lone outlier pixels their own entry
            if score > best: best, bi = score, i
        if bi < 0: break
        b = boxes.pop(bi)
        c, ww, m = stats(b)
        axis = int(np.argmax((((c - m) ** 2) * ww[:, None])[:, :3].sum(0)))
        order = b[np.argsort(cols[b][:, axis], kind="stable")]
        cw = np.cumsum(w[order]); k = int(np.searchsorted(cw, cw[-1] / 2)) + 1
        k = min(max(k, 1), len(order) - 1)
        boxes += [order[:k], order[k:]]
    return boxes


def quantize(arr, n):
    """Alpha is preserved EXACTLY (the shield / hitbox bakes and ShipSkinsTest compare it to the stock hull):
    every palette entry carries one alpha value and a pixel can only map to an entry with its own alpha."""
    h, w_, _ = arr.shape
    flat = arr.reshape(-1, 4).copy()
    flat[flat[:, 3] == 0] = 0
    cols, inv, cnt = np.unique(flat, axis=0, return_inverse=True, return_counts=True)
    inv = inv.reshape(-1)
    if len(cols) <= n:
        pal = cols.astype(np.float64); idx_u = np.arange(len(cols))
    else:
        alphas = np.unique(cols[:, 3])
        init = [np.where(cols[:, 3] == a)[0] for a in alphas]
        boxes = split_boxes(cols, cnt, init, n)
        pal = np.array([(cols[b].astype(np.float64) * cnt[b][:, None]).sum(0) / cnt[b].sum() for b in boxes])
        pal_a = np.array([cols[b][0, 3] for b in boxes])
        for _ in range(8):  # weighted k-means within each alpha group
            for a in alphas:
                ci = np.where(cols[:, 3] == a)[0]; pi = np.where(pal_a == a)[0]
                if len(pi) < 2: continue
                asg = nearest(cols[ci][:, :3], pal[pi][:, :3])
                for q, j in enumerate(pi):
                    sel = asg == q
                    if sel.any():
                        pal[j, :3] = (cols[ci][sel][:, :3].astype(np.float64) * cnt[ci][sel][:, None]).sum(0) / cnt[ci][sel].sum()
        pal = np.round(pal); pal[:, 3] = pal_a
        idx_u = np.zeros(len(cols), dtype=np.int64)
        for a in alphas:
            ci = np.where(cols[:, 3] == a)[0]; pi = np.where(pal_a == a)[0]
            idx_u[ci] = pi[nearest(cols[ci][:, :3], pal[pi][:, :3])]
    palu = np.clip(np.round(pal), 0, 255).astype(np.uint8)
    idx = idx_u[inv].reshape(h, w_).astype(np.uint8)
    out = palu[idx]
    assert (out[..., 3] == np.where(arr[..., 3] == 0, 0, arr[..., 3])).all(), "alpha changed"
    return idx, palu, out


def metrics(a, b):
    a = a.astype(np.float64); b = b.astype(np.float64)
    # premultiplied-ish: weight RGB by alpha so invisible RGB does not count
    af = a[..., 3:4] / 255.0
    pa = np.concatenate([a[..., :3] * af, a[..., 3:]], -1)
    pb = np.concatenate([b[..., :3] * (b[..., 3:4] / 255.0), b[..., 3:]], -1)
    mse = ((pa - pb) ** 2).mean()
    psnr = 99.0 if mse == 0 else 10 * math.log10(255 ** 2 / mse)
    vis = (a[..., 3] > 0) | (b[..., 3] > 0)
    mx = np.abs(pa - pb)[vis].max() if vis.any() else 0
    return psnr, mx


def save_png(idx, pal, path):
    im = Image.fromarray(idx, "P")
    im.putpalette(pal.tobytes(), "RGBA")
    im.save(path, format="PNG", optimize=True)


def main():
    src, dst = sys.argv[1], sys.argv[2]
    n = 256
    if "--colors" in sys.argv: n = int(sys.argv[sys.argv.index("--colors") + 1])
    os.makedirs(dst, exist_ok=True)
    tb = ta = 0
    for f in sorted(os.listdir(src)):
        if not f.endswith(".bytes"): continue
        p = os.path.join(src, f)
        arr = np.array(Image.open(p).convert("RGBA"))
        idx, pal, out = quantize(arr, n)
        o = os.path.join(dst, f)
        save_png(idx, pal, o)
        back = np.array(Image.open(o).convert("RGBA"))
        assert (back == out).all()
        psnr, mx = metrics(arr, back)
        b, a = os.path.getsize(p), os.path.getsize(o); tb += b; ta += a
        print(f"{f}\t{b}\t{a}\tpsnr={psnr:.2f}\tmax={mx:.0f}", flush=True)
    print(f"TOTAL\t{tb}\t{ta}")

if __name__ == "__main__": main()
