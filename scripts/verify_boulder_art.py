#!/usr/bin/env python3
"""Verify (and measure) one world's cover-boulder art.

    python3 scripts/verify_boulder_art.py <DIR> --world Frost [--manifest] [--sheet out.png]

<DIR> holds the delivered files (the contract of docs/cover-boulders.md section 6):

    boulder.png   1536 x 512   three variants, one 512 x 512 cell each (transparent background)
    break.png     2560 x 1536  the destruction strip: row = variant, 5 columns of 512 x 512 cells
                               col 0 = the intact variant with its first cracks lit, col 4 = the last drifting dust

Prints a PASS / FAIL table and exits 1 on any failure.  --manifest writes <DIR>/manifest.json: per variant the
alpha bounding box, a few hit circles (cell px, origin = cell centre, y up) fitted inside the silhouette, and the
measured stats the game reads (docs/cover-boulders.md 6).  --sheet writes a contact sheet on dark #0b0b1a at 1x
(never any mock UI) for the user to vet.
"""
import argparse, json, os, sys
import numpy as np
from PIL import Image

CELL = 512   # set by --cell (768 for the high-res sheets)
VARIANTS = 3
FRAMES = 5
FAIL = 0


def check(what, ok, extra=""):
    global FAIL
    print(("PASS  " if ok else "FAIL  ") + what + (("  [" + extra + "]") if extra else ""))
    if not ok:
        FAIL += 1


def load(path, size):
    im = Image.open(path).convert("RGBA")
    ok = im.size == size
    check(os.path.basename(path) + " is %dx%d" % size, ok, "got %dx%d" % im.size)
    return np.array(im) if ok else None


def bbox(a):
    ys, xs = np.nonzero(a[..., 3] > 8)
    if len(xs) == 0:
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def luma(rgb):
    return rgb[..., 0] * .299 + rgb[..., 1] * .587 + rgb[..., 2] * .114


def pinkness(a):
    """Share of opaque pixels that are pink (hue 285..352, s > .25, v > .3: WorldCue.IsPink)."""
    op = a[..., 3] > 8
    rgb = a[..., :3][op] / 255.0
    if len(rgb) == 0:
        return 0.0
    mx = rgb.max(1); mn = rgb.min(1); d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    h = np.zeros(len(rgb))
    r, g, b = rgb[:, 0], rgb[:, 1], rgb[:, 2]
    m = d > 1e-6
    idx = m & (mx == r); h[idx] = (60 * ((g[idx] - b[idx]) / d[idx])) % 360
    idx = m & (mx == g); h[idx] = 60 * ((b[idx] - r[idx]) / d[idx]) + 120
    idx = m & (mx == b); h[idx] = 60 * ((r[idx] - g[idx]) / d[idx]) + 240
    pink = (s > .25) & (mx > .3) & (h >= 285) & (h <= 352)
    return float(pink.mean())


def edt(mask):
    """Distance (px) of every True pixel to the nearest False pixel (brute force on a coarse grid)."""
    h, w = mask.shape
    ys, xs = np.nonzero(~mask)
    out = np.zeros((h, w), np.float32)
    if len(xs) == 0:
        return out + max(h, w)
    py, px = np.nonzero(mask)
    best = np.full(len(py), 1e9, np.float32)
    for i in range(0, len(xs), 512):
        bx = xs[i:i + 512][None, :]; by = ys[i:i + 512][None, :]
        d = (px[:, None] - bx) ** 2 + (py[:, None] - by) ** 2
        best = np.minimum(best, d.min(1))
    out[py, px] = np.sqrt(best)
    return out


def fit_circles(cellarr, maxn=4, shrink=.94):
    """Greedy inscribed circles of the silhouette (cell px, origin = cell centre, y up)."""
    step = 4
    m = (cellarr[..., 3] > 128)[::step, ::step]
    d = edt(m)
    covered = np.zeros_like(m)
    yy, xx = np.mgrid[0:m.shape[0], 0:m.shape[1]]
    total = max(1, int(m.sum()))
    circles = []
    for _ in range(maxn):
        free = np.where(m & ~covered, d, 0)
        k = int(free.argmax())
        cy, cx = divmod(k, m.shape[1])
        r = float(d[cy, cx])
        if r < 5:
            break
        covered |= (xx - cx) ** 2 + (yy - cy) ** 2 <= r * r
        circles.append((cx * step + step / 2 - CELL / 2, -(cy * step + step / 2 - CELL / 2), r * step * shrink))
        if covered.sum() / total > .9:
            break
    return [[round(c[0], 1), round(c[1], 1), round(c[2], 1)] for c in circles]


def block_score(a, k):
    """Share of opaque texels equal to the first texel of their k x k block, best phase (>= .97 = a block upscale)."""
    op = a[..., 3] > 8
    best = 0.0
    for oy in range(k):
        for ox in range(k):
            h = (a.shape[0] - oy) // k * k; w = (a.shape[1] - ox) // k * k
            sub = a[oy:oy + h, ox:ox + w]
            first = np.repeat(np.repeat(sub[::k, ::k], k, 0), k, 1)
            eq = (sub == first).all(-1) & op[oy:oy + h, ox:ox + w]
            n = op[oy:oy + h, ox:ox + w].sum()
            if n > 300:
                best = max(best, float(eq.sum() / n))
    return best


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir")
    ap.add_argument("--world", required=True)
    ap.add_argument("--manifest", action="store_true")
    ap.add_argument("--sheet")
    ap.add_argument("--cell", type=int, default=512, help="cell size in px (768 for the high-res sheets)")
    ap.add_argument("--no-break", action="store_true", help="the intact sheet only (Space before its strip exists)")
    ap.add_argument("--intact", nargs=3, metavar="RES", help="Resources paths of three in-place intact sprites (Space)")
    args = ap.parse_args()
    global CELL
    CELL = args.cell
    S = CELL / 512.0
    world = args.world
    space = world.lower() == "space"

    b = load(os.path.join(args.dir, "boulder.png"), (CELL * VARIANTS, CELL))
    k = None if args.no_break else load(os.path.join(args.dir, "break.png"), (CELL * FRAMES, CELL * VARIANTS))
    if b is None or (k is None and not args.no_break):
        print("cannot go on without both files at the right size")
        sys.exit(1)

    for name, arr in (("boulder.png", b), ("break.png", k)):
        if arr is None:
            continue
        worst = max(block_score(arr, kk) for kk in (2, 3, 4))
        check(name + " is real resolution, not a block upscale (block score %.3f < .97)" % worst, worst < .97)
    variants = []
    lights = []
    for v in range(VARIANTS):
        cell = b[:, v * CELL:(v + 1) * CELL]
        bb = bbox(cell)
        check("v%d: not empty" % v, bb is not None)
        if bb is None:
            continue
        x0, y0, x1, y1 = bb
        w, h = x1 - x0, y1 - y0
        check("v%d: art %d x %d px fills 60-95%% of the cell width, 45-95%% of its height" % (v, w, h),
              .60 * CELL <= w <= .95 * CELL and .45 * CELL <= h <= .95 * CELL)
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        check("v%d: centred within 6%% (%.0f, %.0f)" % (v, cx, cy), abs(cx - CELL / 2) < .06 * CELL and abs(cy - CELL / 2) < .06 * CELL)
        check("v%d: 8 px clear margin" % v, x0 >= 8 * S and y0 >= 8 * S and x1 <= CELL - 8 * S and y1 <= CELL - 8 * S)
        al = cell[..., 3]
        mid = ((al > 8) & (al < 247)).sum() / max(1, (al > 8).sum())
        check("v%d: edges (%.1f%% soft alpha; the base fades out, max %d%%)" % (v, mid * 100, 9 if space else 45), mid <= (.09 if space else .45))
        # light from the upper left: the upper-left half of the body is brighter than the lower right one
        op = al > 128
        l = luma(cell[..., :3].astype(np.float32))
        yy, xx = np.mgrid[0:CELL, 0:CELL]
        ul = op & ((xx + yy) < (cx + cy)); lr = op & ((xx + yy) >= (cx + cy))
        lights.append(float(l[ul].mean() - l[lr].mean()))
        if not space:
            check("v%d: lit from the upper left (%.0f vs %.0f)" % (v, l[ul].mean(), l[lr].mean()), l[ul].mean() > l[lr].mean() + 3)
        # NO pale outline / halo (revision 2: the boulder blends into its backdrop): the 3 px rim of the SOLID body is not
        # brighter than the body just inside it, and the body is not brighter than the backdrop's landmarks (median luma <= 110)
        if not space:
            solid = al > 240
            er = solid.copy()
            for _ in range(int(round(3 * S))):
                n = er.copy()
                n[1:, :] &= er[:-1, :]; n[:-1, :] &= er[1:, :]; n[:, 1:] &= er[:, :-1]; n[:, :-1] &= er[:, 1:]
                er = n
            ring = solid & ~er
            er2 = er.copy()
            for _ in range(int(round(12 * S))):
                n = er2.copy()
                n[1:, :] &= er2[:-1, :]; n[:-1, :] &= er2[1:, :]; n[:, 1:] &= er2[:, :-1]; n[:, :-1] &= er2[:, 1:]
                er2 = n
            inner = er & ~er2
            if ring.sum() > 50 and inner.sum() > 50:
                dl = float(l[ring].mean() - l[inner].mean())
                check("v%d: no pale outline or halo (rim luma %+.0f vs the body just inside, max +14)" % (v, dl), dl <= 14)
            med = float(np.median(l[op]))
            check("v%d: body at backdrop brightness (median luma %.0f <= 112)" % (v, med), med <= 112)
        if not space:
            pk = pinkness(cell)
            check("v%d: no pink outside Space (%.2f%%)" % (v, pk * 100), pk <= .002)
        variants.append({"x0": x0, "y0": y0, "x1": x1, "y1": y1,
                         "circles": [{"x": c[0], "y": c[1], "r": c[2]} for c in fit_circles(cell)]})

    if space:   # the user's reference sheet is lit from the right (cyan rim): only the three must agree
        check("the three variants share one light direction (%s)" % " ".join("%.1f" % x for x in lights),
              all(x > 1 for x in lights) or all(x < -1 for x in lights))
    for v in range(VARIANTS if k is not None else 0):
        frames = [k[v * CELL:(v + 1) * CELL, f * CELL:(f + 1) * CELL] for f in range(FRAMES)]
        a0 = (b[:, v * CELL:(v + 1) * CELL][..., 3] > 128)
        f0 = frames[0][..., 3] > 128
        iou = (a0 & f0).sum() / max(1, (a0 | f0).sum())
        check("v%d: break frame 0 is the intact silhouette (IoU %.2f >= .90)" % (v, iou), iou >= .90)
        areas = [(f[..., 3] > 128).sum() / max(1, a0.sum()) for f in frames]
        check("v%d: break frames fall apart (area %s)" % (v, " ".join("%.2f" % a for a in areas)),
              areas[0] > .85 and areas[-1] < .45 and areas[-1] > .01)
        diffs = [int((np.abs(frames[i][..., :3].astype(int) - frames[i + 1][..., :3].astype(int)).sum() > 0)) for i in range(FRAMES - 1)]
        check("v%d: every break frame differs from the last" % v, all(diffs))
        if not space:
            pk = max(pinkness(f) for f in frames)
            check("v%d: no pink in the break frames (%.2f%%)" % (v, pk * 100), pk <= .002)
        for i, f in enumerate(frames):
            bb = bbox(f)
            if bb is not None and (bb[0] < 2 * S or bb[1] < 2 * S or bb[2] > CELL - 2 * S or bb[3] > CELL - 2 * S):
                check("v%d f%d: debris stays inside its cell" % (v, i), False)
                break

    if args.manifest:
        out = {"world": world, "cell": CELL, "variants": variants, "breakFrames": 0 if k is None else FRAMES,
               "intact": list(args.intact) if args.intact else []}
        with open(os.path.join(args.dir, "manifest.json"), "w") as fh:
            json.dump(out, fh, indent=1)
        print("wrote manifest.json (%d circles)" % sum(len(v["circles"]) for v in variants))

    if args.sheet:
        W = CELL * FRAMES
        sheet = Image.new("RGBA", (W, CELL * (VARIANTS + 1)), (11, 11, 26, 255))
        sheet.alpha_composite(Image.fromarray(b), (0, 0))
        if k is not None:
            kk = Image.fromarray(k)
            for v in range(VARIANTS):
                sheet.alpha_composite(kk.crop((0, v * CELL, W, (v + 1) * CELL)), (0, CELL * (v + 1)))
        sheet.convert("RGB").save(args.sheet)
        print("wrote", args.sheet)

    print("RESULT:", "PASS" if FAIL == 0 else "FAIL (%d)" % FAIL)
    sys.exit(1 if FAIL else 0)


if __name__ == "__main__":
    main()
