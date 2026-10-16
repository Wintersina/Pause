import numpy as np
from collections import deque
from PIL import Image

C = 384
rng = np.random.RandomState(7)
NOISE = rng.rand(1024, 1024)

def load(path):
    return np.array(Image.open(path).convert('RGBA'))

def save(arr, path):
    Image.fromarray(arr.astype(np.uint8), 'RGBA').save(path)

def components(mask):
    """8-connected labels of a boolean mask -> (labels, count)"""
    h, w = mask.shape
    lab = np.zeros((h, w), np.int32)
    n = 0
    ys, xs = np.nonzero(mask)
    for y0, x0 in zip(ys, xs):
        if lab[y0, x0]:
            continue
        n += 1
        lab[y0, x0] = n
        dq = deque([(y0, x0)])
        while dq:
            y, x = dq.popleft()
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < h and 0 <= xx < w and mask[yy, xx] and not lab[yy, xx]:
                        lab[yy, xx] = n
                        dq.append((yy, xx))
    return lab, n

def _declip_bottom(cell, depth, minrun, tol=1, noise_off=(0, 0)):
    """cell: HxWx4 uint8; dissolve flat bottom edges (rows below which nothing is left)."""
    a = cell[:, :, 3]
    h, w = a.shape
    yb = np.full(w, -1)
    for x in range(w):
        ys = np.nonzero(a[:, x] > 0)[0]
        if len(ys):
            yb[x] = ys.max()
    x = 0
    changed = False
    while x < w:
        if yb[x] < 0:
            x += 1
            continue
        x2 = x
        while x2 + 1 < w and yb[x2 + 1] >= 0 and abs(yb[x2 + 1] - yb[x]) <= tol:
            x2 += 1
        if x2 - x + 1 >= minrun:
            base = int(np.median(yb[x:x2 + 1]))
            for xx in range(x, x2 + 1):
                for y in range(max(0, base - depth), base + 2):
                    d = base - y + .5          # distance above the clipped edge
                    p = min(1.0, max(0.0, d / depth)) ** 1.1
                    n = NOISE[(y + noise_off[0]) % 1024, (xx + noise_off[1]) % 1024]
                    if a[y, xx] and n >= p:
                        cell[y, xx, 3] = 0
                        changed = True
        x = x2 + 1
    return changed

def declip_plumes(cell, depth=22, minrun=12, tol=1, rounds=2, lo=150, hi=260):
    """declip each of the two plumes of a smoke cell on its own (left / right of the widest gap)"""
    s = gap_split(cell[:, :, 3], lo, hi)
    out = cell.copy()
    L = cell.copy(); L[:, s:] = 0
    R = cell.copy(); R[:, :s] = 0
    L = declip(L, depth, minrun, tol, rounds)
    R = declip(R, depth, minrun, tol, rounds)
    out[:] = 0
    out[:, :s] = L[:, :s]
    out[:, s:] = R[:, s:]
    return out

def declip(cell, depth=22, minrun=12, tol=1, rounds=2):
    """dissolve flat-clipped bottom / left / right edges of the drawing in `cell` (H,W,4); never the top"""
    out = cell.copy()
    for k in range(rounds):
        for rot in (0, 1, 3):
            r = np.ascontiguousarray(np.rot90(out, rot))
            _declip_bottom(r, depth, minrun, tol, (rot * 131, rot * 57))
            out = np.ascontiguousarray(np.rot90(r, -rot))
    return out

def longest_straight(a):
    """longest straight axis-aligned alpha edge run in an alpha plane"""
    m = a > 0
    best = 0
    for arr in (m[:, :-1] != m[:, 1:], (m[:-1, :] != m[1:, :]).T):
        for x in range(arr.shape[1]):
            run = 0
            for v in arr[:, x]:
                run = run + 1 if v else 0
                if run > best:
                    best = run
    return best

def cell_of(fx, row, col, cell=C):
    return fx[row * cell:(row + 1) * cell, col * cell:(col + 1) * cell]

def border_fade(cell, margin=14, fade=24):
    """dissolve everything that comes closer than `margin` px to the cell border, ramping in over `fade` px"""
    out = cell.copy()
    h, w = out.shape[:2]
    for y in range(h):
        for x in range(w):
            if out[y, x, 3] == 0:
                continue
            d = min(x, y, w - 1 - x, h - 1 - y)
            if d >= margin + fade:
                continue
            p = max(0.0, (d - margin) / fade)
            if NOISE[y, x] >= p:
                out[y, x, 3] = 0
    return out

def gap_split(a, lo, hi):
    """x of the middle of the widest empty column band in [lo,hi)"""
    cols = (a > 0).any(axis=0)
    best, bs, x = 0, (lo + hi) // 2, lo
    while x < hi:
        if not cols[x]:
            x2 = x
            while x2 + 1 < hi and not cols[x2 + 1]:
                x2 += 1
            if x2 - x + 1 > best:
                best, bs = x2 - x + 1, (x + x2) // 2
            x = x2 + 1
        else:
            x += 1
    return bs

def bbox(a):
    ys, xs = np.nonzero(a > 0)
    return xs.min(), ys.min(), xs.max(), ys.max()

def shifted(img, dx, dy):
    out = np.zeros_like(img)
    h, w = img.shape[:2]
    ys0, ys1 = max(0, dy), min(h, h + dy)
    xs0, xs1 = max(0, dx), min(w, w + dx)
    out[ys0:ys1, xs0:xs1] = img[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx]
    return out
