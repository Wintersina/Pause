#!/usr/bin/env python3
"""Python port of the Unity bake ShieldSilhouetteBaker.Bake (ShieldSilhouetteBaker.cs + ShipHitboxBaker.cs +
ShieldContour.cs + ShipHitbox.Encode), so a ship's two baked files can be rewritten without opening Unity:

    <Key>/<Key>_shield.bytes   "SIL1" stock rest silhouette (alpha >= 40), bottom row first, LSB-first bits
    <Key>/<Key>_hitbox.bytes   "HBX1" margin, hull polygon (24..32 pts) + shield zone polygon (<= 32 pts)

    python3 bake_silhouettes.py <Key> [<Key> ...]        write the files (only when the bytes differ)
    python3 bake_silhouettes.py <Key> --check            compare with the files on disk, write nothing
    python3 bake_silhouettes.py --all --check            every roster ship (port self-test)

Float maths follows the C# code operation by operation in IEEE single precision (numpy float32 / an f32()
rounding of each double result, which is exact for + - * / and sqrt of float32 operands), so the bytes
match the Unity bake. Validate with `--all --check` on ships whose art did not change.
"""
import io
import json
import os
import struct
import sys
from collections import deque

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import shiplib  # noqa: E402

F32 = np.float32
CELL = 256
ALPHA_CUTOFF = 40
SIL_MAGIC = 0x314C4953
HBX_MAGIC = 0x31584248
SHIELD_MARGIN = F32(0.15)
REFERENCE_HULL = F32(0.58)
INSET_PX = F32(2.0)
THIN_PX = F32(6.0)
TARGET_HULL_POINTS = 24
MAX_HULL_POINTS = 32
MIN_COVER = F32(0.82)
MAX_SHIELD_POINTS = 32
PAD = 4
# ShieldContour
REF_PIXELS = F32(30.0)
DILATE_U = F32(4.2)
SIMPLIFY_U = F32(1.3)


def f32(x):
    """Round a double to the nearest float32 (as a Python float)."""
    return struct.unpack("<f", struct.pack("<f", x))[0]


# ---------------------------------------------------------------- inputs

def silhouette(key):
    """ShieldSilhouetteBaker.Silhouette: stock rest drawing, alpha >= 40, rows bottom-up. -> (mask[h][w] bool, w, h, ppu)"""
    d = shiplib.load_data(key)
    r = d["rect"]
    im = np.array(Image.open(shiplib.hull_path(key)).convert("RGBA"))
    H = im.shape[0]
    x0, y0, w, h = r["x"], r["y"], r["w"], r["h"]
    # Unity rect of state 0 column 0: (x0, 2*256 + y0) from the bottom of the texture
    ybot = (H // CELL - 1) * CELL + y0
    rows = []
    for yb in range(h):
        py = H - 1 - (ybot + yb)
        rows.append(im[py, x0:x0 + w, 3] >= ALPHA_CUTOFF)
    return np.array(rows), w, h, F32(r["ppu"])


# ---------------------------------------------------------------- grid helpers (flat lists, index y*W+x)

def squared_distance(src, W, H):
    """ShieldContour.SquaredDistance (Felzenszwalb-Huttenlocher, exact ints). src: bool array (H, W)."""
    from scipy import ndimage
    if not src.any():
        return np.full((H, W), 1 << 28, np.int64)
    edt = ndimage.distance_transform_edt(~src, return_distances=True, return_indices=True)
    _, idx = edt
    yy, xx = np.mgrid[0:H, 0:W]
    dy = yy - idx[0]
    dx = xx - idx[1]
    return (dx.astype(np.int64) ** 2 + dy.astype(np.int64) ** 2)


def erode(m, r):
    H, W = m.shape
    d = squared_distance(~m, W, H)
    r2 = F32(r) * F32(r)
    return m & (d.astype(np.float32) > r2)


def dilate_full(m, r):
    H, W = m.shape
    d = squared_distance(m, W, H)
    r2 = F32(r) * F32(r)
    return d.astype(np.float32) <= r2


def largest_component(m):
    """Same tie rule as C#: first component (scan order) of the biggest size wins."""
    H, W = m.shape
    label = np.zeros((H, W), np.int32)
    best, best_size, nxt = 0, 0, 0
    flat = m.ravel()
    lab = label.ravel()
    for s in np.flatnonzero(flat):
        if lab[s]:
            continue
        nxt += 1
        size = 0
        stack = [s]
        lab[s] = nxt
        while stack:
            i = stack.pop()
            size += 1
            x, y = i % W, i // W
            for j, ok in ((i - 1, x > 0), (i + 1, x < W - 1), (i - W, y > 0), (i + W, y < H - 1)):
                if ok and flat[j] and lab[j] == 0:
                    lab[j] = nxt
                    stack.append(j)
        if size > best_size:
            best_size, best = size, nxt
    return (label == best) & (best != 0)


def trace(m):
    """ShieldContour.Trace: crack-following outer boundary, solid on the left (CCW). m: (H, W) bool."""
    H, W = m.shape
    VW = W + 1
    nextA = {}
    nextB = {}
    start = -1
    order = []

    def add(fr, to):
        nonlocal start
        if fr not in nextA:
            nextA[fr] = to
        else:
            nextB[fr] = to
        if start < 0:
            start = fr

    for y in range(1, H - 1):
        row = m[y]
        if not row[1:W - 1].any():
            continue
        for x in np.flatnonzero(row[1:W - 1]) + 1:
            x = int(x)
            if not m[y - 1, x]:
                add(y * VW + x, y * VW + x + 1)
            if not m[y, x + 1]:
                add(y * VW + x + 1, (y + 1) * VW + x + 1)
            if not m[y + 1, x]:
                add((y + 1) * VW + x + 1, (y + 1) * VW + x)
            if not m[y, x - 1]:
                add((y + 1) * VW + x, y * VW + x)
    loop = []
    if start < 0:
        return loop
    v, pdx, pdy = start, 1, 0
    guard = VW * (H + 1) * 2
    while True:
        loop.append((v % VW, v // VW))
        a = nextA.get(v, -1)
        b = nextB.get(v, -1)
        if b < 0:
            n = a
            nextA[v] = -1
        else:
            ax, ay = a % VW - v % VW, a // VW - v // VW
            if pdx * ay - pdy * ax > 0:
                n = a
                nextA[v] = b
                nextB[v] = -1
            else:
                n = b
                nextB[v] = -1
        if n < 0:
            break
        pdx, pdy = n % VW - v % VW, n // VW - v // VW
        v = n
        guard -= 1
        if v == start or guard <= 0:
            break
    return loop


def fill_holes(m):
    from scipy import ndimage
    H, W = m.shape
    outside = np.zeros((H, W), bool)
    # 4-connected flood of non-solid cells from the border
    lab, n = ndimage.label(~m, structure=[[0, 1, 0], [1, 1, 1], [0, 1, 0]])
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    for b in border:
        outside |= lab == b
    return m | ~outside


def count_components(m):
    from scipy import ndimage
    _, n = ndimage.label(m, structure=[[0, 1, 0], [1, 1, 1], [0, 1, 0]])
    return n


# ---------------------------------------------------------------- float32 geometry

def cross(o, a, b):
    """float32 Cross on float32 inputs (exact for half-grid points; emulated anyway)."""
    return f32(f32(f32(a[0] - o[0]) * f32(b[1] - o[1])) - f32(f32(a[1] - o[1]) * f32(b[0] - o[0])))


def dist_to_segment(p, a, b):
    abx, aby = f32(b[0] - a[0]), f32(b[1] - a[1])
    sq = f32(f32(abx * abx) + f32(aby * aby))
    if sq > f32(1e-12):
        dot = f32(f32(f32(p[0] - a[0]) * abx) + f32(f32(p[1] - a[1]) * aby))
        t = min(1.0, max(0.0, f32(dot / sq)))
    else:
        t = 0.0
    qx = f32(f32(a[0] + f32(abx * t)) - p[0])
    qy = f32(f32(a[1] + f32(aby * t)) - p[1])
    return f32(f32(f32(qx * qx) + f32(qy * qy)) ** 0.5)


def dist_to_segment_vec(px, py, a, b):
    """vectorised float32 DistanceToSegment(p_i, a, b)"""
    a0, a1, b0, b1 = F32(a[0]), F32(a[1]), F32(b[0]), F32(b[1])
    abx, aby = b0 - a0, b1 - a1
    sq = abx * abx + aby * aby
    if sq > F32(1e-12):
        dot = (px - a0) * abx + (py - a1) * aby
        t = np.clip(dot / sq, F32(0), F32(1)).astype(np.float32)
    else:
        t = np.zeros_like(px)
    qx = (a0 + abx * t) - px
    qy = (a1 + aby * t) - py
    return np.sqrt(qx * qx + qy * qy).astype(np.float32)


def signed_area(p):
    a = F32(0)
    n = len(p)
    j = n - 1
    for i in range(n):
        a = F32(a + (F32(F32(p[j][0]) * F32(p[i][1])) - F32(F32(p[i][0]) * F32(p[j][1]))))
        j = i
    return F32(a * F32(.5))


def segments_cross(p1, p2, q1, q2):
    d1, d2 = cross(q1, q2, p1), cross(q1, q2, p2)
    d3, d4 = cross(p1, p2, q1), cross(p1, p2, q2)
    if ((d1 > 0 and d2 < 0) or (d1 < 0 and d2 > 0)) and ((d3 > 0 and d4 < 0) or (d3 < 0 and d4 > 0)):
        return True
    if abs(d1) < 1e-4 and on_segment(q1, q2, p1):
        return True
    if abs(d2) < 1e-4 and on_segment(q1, q2, p2):
        return True
    if abs(d3) < 1e-4 and on_segment(p1, p2, q1):
        return True
    if abs(d4) < 1e-4 and on_segment(p1, p2, q2):
        return True
    return False


def on_segment(a, b, p):
    e = f32(1e-4)
    return (p[0] >= f32(min(a[0], b[0]) - e) and p[0] <= f32(max(a[0], b[0]) + e) and
            p[1] >= f32(min(a[1], b[1]) - e) and p[1] <= f32(max(a[1], b[1]) + e))


def in_fence(a, b, fence):
    H, W = fence.shape
    e = f32(1e-3)
    if a[0] > b[0]:
        a, b = b, a
    cx0 = int(np.floor(f32(a[0] - e)))
    cx1 = int(np.floor(f32(b[0] + e)))
    dx = f32(b[0] - a[0])
    for cx in range(cx0, cx1 + 1):
        xa = max(a[0], float(cx))
        xb = min(b[0], float(cx + 1))
        if dx < 1e-6:
            ya, yb = a[1], b[1]
        else:
            dy = f32(b[1] - a[1])
            ca = min(max(xa, a[0]), b[0])
            cb = min(max(xb, a[0]), b[0])
            ya = f32(a[1] + f32(f32(dy * f32(ca - a[0])) / dx))
            yb = f32(a[1] + f32(f32(dy * f32(cb - a[0])) / dx))
        cy0 = int(np.floor(f32(min(ya, yb) - e)))
        cy1 = int(np.floor(f32(max(ya, yb) + e)))
        if cx < 0 or cx >= W:
            return False
        for cy in range(cy0, cy1 + 1):
            if cy < 0 or cy >= H or not fence[cy, cx]:
                return False
    return True


# ---------------------------------------------------------------- hull polygon (ShipHitboxBaker)

def body_of(Sg):
    opened = dilate_full(erode(Sg, THIN_PX), THIN_PX)
    return opened & Sg


def cut_holes(core, fence):
    H, W = core.shape
    for _ in range(16):
        outside = np.zeros((H, W), bool)
        q = deque()
        for x in range(W):
            for i in (x, (H - 1) * W + x):
                y_, x_ = divmod(i, W)
                if not outside[y_, x_] and not core[y_, x_]:
                    outside[y_, x_] = True
                    q.append(i)
        for y in range(H):
            for i in (y * W, y * W + W - 1):
                y_, x_ = divmod(i, W)
                if not outside[y_, x_] and not core[y_, x_]:
                    outside[y_, x_] = True
                    q.append(i)
        while q:
            i = q.popleft()
            y, x = divmod(i, W)
            for j, ok in ((i - 1, x > 0), (i + 1, x < W - 1), (i - W, y > 0), (i + W, y < H - 1)):
                if ok:
                    jy, jx = divmod(j, W)
                    if not outside[jy, jx] and not core[jy, jx]:
                        outside[jy, jx] = True
                        q.append(j)
        holes = np.flatnonzero((~core & ~outside).ravel())
        if len(holes) == 0:
            return
        frm = np.full(W * H, -2, np.int64)
        cf = core.ravel()
        of = outside.ravel()
        q = deque()
        for i in holes:
            frm[i] = -1
            q.append(int(i))
        ex = -1
        while q and ex < 0:
            i = q.popleft()
            x, y = i % W, i // W
            for j in (i - 1 if x > 0 else -1, i + 1 if x < W - 1 else -1, i - W if y > 0 else -1, i + W if y < H - 1 else -1):
                if j < 0 or frm[j] != -2:
                    continue
                frm[j] = i
                if of[j]:
                    ex = j
                    break
                q.append(j)
        if ex < 0:
            return
        path = []
        i = int(frm[ex])
        while i >= 0 and cf[i]:
            path.append(i)
            i = int(frm[i])
        for i in path:
            x, y = i % W, i // W
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    xx, yy = x + dx, y + dy
                    if 0 <= xx < W and 0 <= yy < H:
                        core[yy, xx] = False
                        fence[yy, xx] = False


def can_remove(pts, k):
    n = len(pts)
    ia, ib = (k + n - 1) % n, (k + 1) % n
    a, b = pts[ia], pts[ib]
    for i in range(n):
        j = (i + 1) % n
        if i in (ia, k, ib) or j in (ia, k, ib):
            continue
        if segments_cross(a, b, pts[i], pts[j]):
            return False
    arr = np.array(pts, np.float32)
    d = dist_to_segment_vec(arr[:, 0], arr[:, 1], a, b)
    d[[ia, ib, k]] = 1.0
    return not (d < F32(.05)).any()


def edges_clear(pts, k, c):
    n = len(pts)
    ia, ib = (k + n - 1) % n, (k + 1) % n
    a, b = pts[ia], pts[ib]
    if cross(a, c, b) == 0.0:
        return False
    for i in range(n):
        j = (i + 1) % n
        if i == ia or i == k or j == k:
            continue
        if i != ib and segments_cross(c, b, pts[i], pts[j]):
            return False
        if j != ia and segments_cross(a, c, pts[i], pts[j]):
            return False
    arr = np.array(pts, np.float32)
    d1 = dist_to_segment_vec(arr[:, 0], arr[:, 1], a, c)
    d2 = dist_to_segment_vec(arr[:, 0], arr[:, 1], c, b)
    d1[[k, ia]] = 1.0
    d2[[k, ib]] = 1.0
    return not ((d1 < F32(.05)).any() or (d2 < F32(.05)).any())


def relax_vertex(pts, k, fence, reach):
    n = len(pts)
    a, v, b = pts[(k + n - 1) % n], pts[k], pts[(k + 1) % n]
    best = v
    best_gain = f32(1e-4)
    base = cross(a, b, v)
    steps = []
    t = -reach
    while t <= reach:
        steps.append(t)
        t += .5
    for dy in steps:
        for dx in steps:
            c = (f32(v[0] + dx), f32(v[1] + dy))
            gain = f32(f32(base - cross(a, b, c)) * .5)
            if gain <= best_gain:
                continue
            if not in_fence(a, c, fence) or not in_fence(c, b, fence):
                continue
            if not edges_clear(pts, k, c):
                continue
            best, best_gain = c, gain
    if best == v:
        return False
    pts[k] = best
    return True


def reduce_pts(pts, fence, mx):
    chord = [-1] * len(pts)
    while len(pts) > 3:
        n = len(pts)
        cost = [f32(cross(pts[(i + n - 1) % n], pts[i], pts[(i + 1) % n]) * .5) for i in range(n)]
        order = sorted(range(n), key=lambda i: (cost[i], i))
        pick = -1
        for i in order:
            if n <= mx and cost[i] > 0:
                break
            if chord[i] < 0:
                chord[i] = 1 if in_fence(pts[(i + n - 1) % n], pts[(i + 1) % n], fence) else 0
            if chord[i] == 0:
                continue
            if can_remove(pts, i):
                pick = i
                break
        if pick < 0:
            break
        del pts[pick]
        del chord[pick]
        n -= 1
        chord[(pick + n - 1) % n] = -1
        chord[pick % n] = -1
        if n <= 3 * mx:
            left, right = (pick + n - 1) % n, pick % n
            for _ in range(3):
                moved = relax_vertex(pts, left, fence, 3.0)
                moved = relax_vertex(pts, right, fence, 3.0) or moved
                if not moved:
                    break
            for d in range(-2, 3):
                chord[((pick + d) % n + n) % n] = -1


def relax(pts, fence):
    for _ in range(12):
        moved = False
        for k in range(len(pts)):
            moved = relax_vertex(pts, k, fence, 4.0) or moved
        if not moved:
            break


def hull_polygon(S, w, h, pivot, ppu):
    Sg = np.zeros((h + 2 * PAD, w + 2 * PAD), bool)
    Sg[PAD:PAD + h, PAD:PAD + w] = S
    body = body_of(Sg)
    body_area = float(body.sum())
    fence = erode(Sg, INSET_PX)
    core = largest_component(erode(body, F32(INSET_PX + F32(1.5))))
    cut_holes(core, fence)
    core = largest_component(core)
    crack = trace(core)
    pts = []
    for i in range(len(crack)):
        a, b = crack[i], crack[(i + 1) % len(crack)]
        dx, dy = b[0] - a[0], b[1] - a[1]
        c = ((a[0] + b[0]) * .5 + (-dy) * .5, (a[1] + b[1]) * .5 + dx * .5)
        if not pts or pts[-1] != c:
            pts.append(c)
    while len(pts) > 1 and pts[0] == pts[-1]:
        pts.pop()
    i = len(pts) - 1
    while i >= 0 and len(pts) > 3:
        n = len(pts)
        p, q, r = pts[(i + n - 1) % n], pts[i], pts[(i + 1) % n]
        dot = (q[0] - p[0]) * (r[0] - q[0]) + (q[1] - p[1]) * (r[1] - q[1])
        if abs(cross(p, q, r)) < 1e-6 and dot > 0:
            del pts[i]
        i -= 1
    seed = list(pts)
    for budget in range(TARGET_HULL_POINTS, MAX_HULL_POINTS + 1):
        pts = list(seed)
        reduce_pts(pts, fence, budget)
        relax(pts, fence)
        if abs(signed_area(pts)) >= F32(MIN_COVER * F32(body_area)):
            break
    poly = [(F32(F32(F32(x) - F32(PAD)) - pivot[0]) / ppu, F32(F32(F32(y) - F32(PAD)) - pivot[1]) / ppu) for x, y in pts]
    return [(F32(a), F32(b)) for a, b in poly]


# ---------------------------------------------------------------- shield contour (ShieldContour.BuildMask)

def dp_contour(p, a, b, eps, keep):
    """ShieldContour.DouglasPeucker (cross / len)."""
    stack = [(a, b)]
    while stack:
        a, b = stack.pop()
        if b <= a + 1:
            continue
        A, B = p[a], p[b]
        ABx, ABy = F32(B[0] - A[0]), F32(B[1] - A[1])
        ln = np.sqrt(F32(ABx * ABx + ABy * ABy))
        seg = p[a + 1:b]
        if ln > F32(1e-5):
            d = np.abs(ABx * (seg[:, 1] - A[1]) - ABy * (seg[:, 0] - A[0])) / ln
        else:
            qx, qy = seg[:, 0] - A[0], seg[:, 1] - A[1]
            d = np.sqrt(qx * qx + qy * qy)
        idx = int(np.argmax(d))
        best = d[idx]
        if best > eps:
            index = a + 1 + idx
            keep[index] = True
            # recursion order does not matter for `keep`
            stack.append((a, index))
            stack.append((index, b))


def contour_polygon(S, w, h, pivot, ppu):
    U = F32(F32(max(w, h)) / REF_PIXELS)
    R = F32(DILATE_U * U)
    attempts = 5
    step = F32(F32(.75) * U)
    pad = int(np.ceil(F32(F32(R + F32(step * F32(attempts))) + F32(3))))
    W, H = w + 2 * pad, h + 2 * pad
    hull = np.zeros((H, W), bool)
    hull[pad:pad + h, pad:pad + w] = S
    dist = squared_distance(hull, W, H)
    grown = None
    for attempt in range(attempts + 1):
        r2 = F32(R * R)
        grown = np.zeros((H, W), bool)
        grown[1:H - 1, 1:W - 1] = dist[1:H - 1, 1:W - 1].astype(np.float32) <= r2
        grown = fill_holes(grown)
        if count_components(grown) <= 1:
            break
        if attempt == attempts:
            raise RuntimeError("convex hull fallback not ported")
        R = F32(R + step)
    loop = trace(grown)
    ox = oy = pad
    nose = 0
    for i in range(1, len(loop)):
        if loop[i][1] > loop[nose][1] or (loop[i][1] == loop[nose][1] and
                                          abs(F32(F32(loop[i][0] - ox) - pivot[0])) < abs(F32(F32(loop[nose][0] - ox) - pivot[0]))):
            nose = i
    eps = F32(SIMPLIFY_U * U)
    n = len(loop)
    p = np.array([loop[(nose + i) % n] for i in range(n)], np.float32)
    dx, dy = p[1:, 0] - p[0, 0], p[1:, 1] - p[0, 1]
    d = dx * dx + dy * dy
    far = 1 + int(np.argmax(d))
    keep = [False] * (n + 1)
    keep[0] = keep[far] = keep[n] = True
    ext = np.vstack([p, p[:1]])
    dp_contour(ext, 0, far, eps, keep)
    dp_contour(ext, far, n, eps, keep)
    simp = [p[i] for i in range(n) if keep[i]]
    poly = [(F32(F32(F32(s[0]) - F32(ox)) - pivot[0]) / ppu, F32(F32(F32(s[1]) - F32(oy)) - pivot[1]) / ppu) for s in simp]
    poly = [(F32(a), F32(b)) for a, b in poly]
    if signed_area(poly) < 0:
        poly = [poly[0]] + poly[1:][::-1]
    return poly


def point_in_polygon_grid(poly, qx, qy):
    """vectorised float32 PointInPolygon for arrays of query points"""
    inside = np.zeros(qx.shape, bool)
    n = len(poly)
    j = n - 1
    for i in range(n):
        pix, piy = F32(poly[i][0]), F32(poly[i][1])
        pjx, pjy = F32(poly[j][0]), F32(poly[j][1])
        cond = (piy > qy) != (pjy > qy)
        with np.errstate(divide="ignore", invalid="ignore"):
            xs = (pjx - pix) * (qy - piy) / (pjy - piy) + pix
        inside ^= cond & (qx < xs)
        j = i
    return inside


def dp_baker(p, a, b, eps, keep):
    """ShipHitboxBaker.DouglasPeucker (DistanceToSegment)."""
    stack = [(a, b)]
    while stack:
        a, b = stack.pop()
        if b <= a + 1:
            continue
        seg = p[a + 1:b]
        d = dist_to_segment_vec(seg[:, 0], seg[:, 1], p[a], p[b])
        idx = int(np.argmax(d))
        if d[idx] > eps:
            index = a + 1 + idx
            keep[index] = True
            stack.append((a, index))
            stack.append((index, b))


def grow(poly, margin_px, pivot, ppu, max_points):
    p = [(F32(F32(x * ppu) + pivot[0]), F32(F32(y * ppu) + pivot[1])) for x, y in poly]
    minx = min(q[0] for q in p)
    maxx = max(q[0] for q in p)
    miny = min(q[1] for q in p)
    maxy = max(q[1] for q in p)
    eps = F32(1.5)
    for attempt in range(12):
        g = F32(F32(margin_px + eps) + F32(1.5))
        pad = int(np.ceil(g)) + 3
        ox, oy = int(np.floor(minx)) - pad, int(np.floor(miny)) - pad
        W, H = int(np.ceil(maxx)) + pad - ox, int(np.ceil(maxy)) + pad - oy
        yy, xx = np.mgrid[0:H, 0:W]
        qx = (F32(ox) + xx.astype(np.float32)) + F32(.5)
        qy = (F32(oy) + yy.astype(np.float32)) + F32(.5)
        qx = (xx + ox).astype(np.float32) + F32(.5)
        qy = (yy + oy).astype(np.float32) + F32(.5)
        inside = point_in_polygon_grid(p, qx.astype(np.float32), qy.astype(np.float32))
        grown = dilate_full(inside, g)
        grown[0, :] = grown[-1, :] = False
        grown[:, 0] = grown[:, -1] = False
        loop = trace(grown)
        n = len(loop)
        pp = np.array(loop + [loop[0]], np.float32)
        dx, dy = pp[1:n, 0] - pp[0, 0], pp[1:n, 1] - pp[0, 1]
        far = 1 + int(np.argmax(dx * dx + dy * dy))
        keep = [False] * (n + 1)
        keep[0] = keep[far] = keep[n] = True
        dp_baker(pp, 0, far, eps, keep)
        dp_baker(pp, far, n, eps, keep)
        simple = [pp[i] for i in range(n) if keep[i]]
        if len(simple) > max_points and attempt < 11:
            eps = F32(eps * F32(1.4))
            continue
        return [(F32(F32(F32(s[0]) + F32(ox)) - pivot[0]) / ppu, F32(F32(F32(s[1]) + F32(oy)) - pivot[1]) / ppu) for s in simple]
    return poly


# ---------------------------------------------------------------- encode

def encode_shield(sid, w, h, S):
    bits = np.packbits(S.ravel(), bitorder="little").tobytes()
    return struct.pack("<iiiii", SIL_MAGIC, 1, sid, w, h) + bits


def encode_hitbox(sid, hull, shield):
    out = io.BytesIO()
    out.write(struct.pack("<ifii", HBX_MAGIC, float(SHIELD_MARGIN), 1, sid))
    for path in (hull, shield):
        out.write(struct.pack("<i", len(path)))
        for x, y in path:
            out.write(struct.pack("<ff", float(F32(x)), float(F32(y))))
    return out.getvalue()


def compute(key):
    S, w, h, ppu = silhouette(key)
    sid = shiplib.ship_id(key)
    pivot = (F32(F32(w) * F32(.5)), F32(F32(h) * F32(.5)))
    shield_bytes = encode_shield(sid, w, h, S)
    size = max(F32(F32(w) / ppu), F32(F32(h) / ppu))
    scale = F32(REFERENCE_HULL / size)
    hull = hull_polygon(S, w, h, pivot, ppu)
    contour = contour_polygon(S, w, h, pivot, ppu)
    margin_px = F32(F32(SHIELD_MARGIN / scale) * ppu)
    shield = grow(contour, margin_px, pivot, ppu, MAX_SHIELD_POINTS)
    return shield_bytes, encode_hitbox(sid, hull, shield), dict(hull=hull, shield=shield, contour=contour)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    keys = shiplib.all_keys()[1:] if "--all" in sys.argv else [shiplib.resolve(a) for a in args]
    check = "--check" in sys.argv
    bad = 0
    for key in keys:
        if not os.path.exists(shiplib.hull_path(key)):
            continue
        sb, hb, _ = compute(key)
        d = shiplib.ship_dir(key)
        for name, data in ((key + "_shield.bytes", sb), (key + "_hitbox.bytes", hb)):
            p = os.path.join(d, name)
            old = open(p, "rb").read() if os.path.exists(p) else None
            same = old == data
            if check:
                print("%-28s %s" % (name, "same" if same else "DIFFERS (%s vs %s bytes)" % (len(data), None if old is None else len(old))))
                bad += not same
            elif not same:
                open(p, "wb").write(data)
                print("wrote", name, len(data), "bytes")
            else:
                print("unchanged", name)
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
