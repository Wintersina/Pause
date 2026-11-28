"""Part-rig renderer for the painted bosses (the many-frame animation atlases).

A boss is ONE painted drawing (its idle cell). A rig cuts it into layers --
limbs, jaws, wings, tentacles, turrets -- each the drawing's own opaque pixels
inside a polygon, with a pivot, a parent and a z (behind / in front of the
static core). A pose gives each part a rotation, an offset and a scale; the
frame is the core plus every part transformed about its pivot, so every frame
is the same painting at the same registration (no regeneration, no drift).

Rotation is RotSprite-style (three Scale2x passes to 8x, nearest rotate, one
sample per 8x8 block), so a rotated limb keeps hard pixel edges and its own
colours. Where a part moves off the core it leaves a hole: `fill` says what the
hole shows -- 'body' (the neighbouring core, in shadow), 'maw' (a dark gullet
lit by the world's material) or 'clear'.

FX are pixel-stepped (hard bands, no soft gradients): charge orbs, rings,
sparks, flares in the world's material colours.
"""
import math
import numpy as np
from PIL import Image
from scipy import ndimage

UP = 8          # RotSprite factor (three Scale2x passes)


# ---- Scale2x / RotSprite -------------------------------------------------------------------------------

def _pack(a):
    return (a[..., 0].astype(np.uint32) << 24) | (a[..., 1].astype(np.uint32) << 16) | \
           (a[..., 2].astype(np.uint32) << 8) | a[..., 3].astype(np.uint32)


def scale2x(a):
    """EPX / Scale2x on an RGBA uint8 array."""
    p = _pack(a)
    P = np.pad(p, 1, mode='edge')
    E = P[1:-1, 1:-1]
    B = P[:-2, 1:-1]; H = P[2:, 1:-1]; D = P[1:-1, :-2]; F = P[1:-1, 2:]
    c = (B != H) & (D != F)
    e0 = np.where(c & (D == B), D, E)
    e1 = np.where(c & (B == F), F, E)
    e2 = np.where(c & (D == H), D, E)
    e3 = np.where(c & (H == F), F, E)
    h, w = p.shape
    out = np.empty((h * 2, w * 2), np.uint32)
    out[0::2, 0::2] = e0; out[0::2, 1::2] = e1; out[1::2, 0::2] = e2; out[1::2, 1::2] = e3
    r = np.empty(out.shape + (4,), np.uint8)
    r[..., 0] = out >> 24; r[..., 1] = (out >> 16) & 255; r[..., 2] = (out >> 8) & 255; r[..., 3] = out & 255
    return r


def upscale(a):
    for _ in range(3):
        a = scale2x(a)
    return a


def affine_layer(up, size, pivot, rot, dx, dy, sx, sy, curl=0., reach=1., base_rot=0.):
    """Transform an UP-scaled layer (full cell, UP x) about `pivot` (cell px) and sample it back to `size`.
    Inverse-maps every output pixel centre (nearest), so pixels never tear."""
    H = W = size
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float64)
    px, py = pivot
    # output pixel centre -> source position
    X = xs + .5 - px - dx
    Y = ys + .5 - py - dy
    # inverse rotate (positive rot = clockwise on screen, y down). With curl > 0 the part's OWN rotation is a
    # twirl: none at the pivot, growing as (r / reach) ** curl toward the tip -- a tentacle / wing / frond bends
    # smoothly from its root instead of hinging (rotation about the pivot keeps r, so the inverse is exact).
    if curl > 0:
        rr = np.hypot(X, Y)
        ang = np.radians(base_rot + rot * np.minimum(1., rr / max(reach, 1.)) ** curl)
    else:
        ang = math.radians(base_rot + rot)
    c, s = np.cos(ang), np.sin(ang)
    Xr = c * X + s * Y
    Yr = -s * X + c * Y
    Xr /= sx; Yr /= sy
    sxp = ((Xr + px) * UP).astype(np.int64)
    syp = ((Yr + py) * UP).astype(np.int64)
    ok = (sxp >= 0) & (syp >= 0) & (sxp < up.shape[1]) & (syp < up.shape[0])
    out = np.zeros((H, W, 4), np.uint8)
    out[ok] = up[syp[ok], sxp[ok]]
    return out


def compose(dst, src):
    """Alpha-over of uint8 RGBA arrays (in place on a float copy)."""
    a = src[..., 3:4].astype(np.float32) / 255.
    db = dst.astype(np.float32)
    out_a = a + db[..., 3:4] / 255. * (1 - a)
    rgb = (src[..., :3] * a + db[..., :3] * (db[..., 3:4] / 255.) * (1 - a)) / np.maximum(out_a, 1e-6)
    res = np.concatenate([rgb, out_a * 255.], -1)
    return np.clip(res + .5, 0, 255).astype(np.uint8)


# ---- polygons -------------------------------------------------------------------------------------------

def poly_mask(size, poly):
    from PIL import ImageDraw
    m = Image.new('L', (size, size), 0)
    ImageDraw.Draw(m).polygon([tuple(p) for p in poly], fill=255)
    return np.array(m) > 0


# ---- the rig ---------------------------------------------------------------------------------------------

class Part:
    def __init__(self, name, poly, pivot, z=1, fill='body', parent=None, alpha_min=8, shade=None, curl=0.):
        self.name, self.poly, self.pivot, self.z, self.fill, self.parent = name, poly, pivot, z, fill, parent
        self.shade = shade
        self.curl = curl
        self.alpha_min = alpha_min


class Rig:
    """src: RGBA image of the idle cell. parts: list of Part, coordinates in BASE px (scaled by size / base)."""

    def __init__(self, src, parts, base=384, maw_dark=(18, 10, 12), body_shadow=.5, fill_reach=7):
        self.src = np.array(src.convert('RGBA'))
        self.size = self.src.shape[0]
        k = self.size / base
        self.k = k
        self.parts = []
        claimed = np.zeros(self.src.shape[:2], bool)
        opaque = self.src[..., 3] >= 8
        for p in parts:
            poly = [(x * k, y * k) for x, y in p.poly]
            m = poly_mask(self.size, poly) & opaque & ~claimed   # first part listed owns a shared pixel
            claimed |= m
            p.mask = m
            p.piv = (p.pivot[0] * k, p.pivot[1] * k)
            ys, xs = np.nonzero(m)
            p.reach = float(np.hypot(xs - p.piv[0], ys - p.piv[1]).max()) if len(xs) else 1.
            self.parts.append(p)
        self.byname = {p.name: p for p in self.parts}
        # a box cut leaves crumbs: a small island of one part's pixels that belongs to the limb next door goes to
        # the part (or the core) it touches most, so nothing floats loose when the limbs move apart
        for p in self.parts:
            lab, n = ndimage.label(p.mask, structure=np.ones((3, 3)))
            if n < 2:
                continue
            sizes = ndimage.sum(np.ones_like(lab), lab, range(1, n + 1))
            big = sizes.max()
            for i, sz in enumerate(sizes):
                if sz >= max(40 * k * k, .04 * big):
                    continue
                comp = lab == i + 1
                ring_ = ndimage.binary_dilation(comp, iterations=2) & ~comp & opaque
                best, bestn = None, (ring_ & ~claimed).sum()      # the core
                for q in self.parts:
                    if q is p:
                        continue
                    c = (ring_ & q.mask).sum()
                    if c > bestn:
                        best, bestn = q, c
                p.mask &= ~comp
                if best is None:
                    claimed &= ~comp
                else:
                    best.mask |= comp
        core = self.src.copy()
        core[claimed] = 0
        # hole fill: near the remaining core, the core's nearest colour in shadow; 'maw' holes a dark gullet
        core_op = core[..., 3] >= 128
        dist, (iy, ix) = ndimage.distance_transform_edt(~core_op, return_indices=True)
        self.fill_body = np.zeros_like(core)
        self.maw_region = np.zeros(self.src.shape[:2], bool)
        for p in self.parts:
            if p.fill in ('body', 'clear'):
                # 'clear' still closes the seam along the core (a few px), so a swung limb never shows a slit
                h = p.mask & (dist <= (fill_reach if p.fill == 'body' else 3) * k)
                col = self.src[iy[h], ix[h]].astype(np.float32)
                col[:, :3] *= body_shadow if p.shade is None else p.shade
                col[:, 3] = 255
                self.fill_body[h] = col.astype(np.uint8)
            elif p.fill == 'maw':
                self.maw_region |= p.mask
        self.core = core
        self.maw_dark = maw_dark
        # each part's own layer, upscaled once (cropped work: the full cell, but only its pixels are opaque)
        for p in self.parts:
            layer = np.zeros_like(self.src)
            layer[p.mask] = self.src[p.mask]
            p.up = upscale(layer)

    def world_xform(self, name, pose):
        """Chain of transforms root -> part (parent first)."""
        chain = []
        p = self.byname[name]
        while p is not None:
            chain.append(p)
            p = self.byname.get(p.parent) if p.parent else None
        return list(reversed(chain))

    def render(self, pose, fx=None, maw=None, core_dy=0, core_dx=0):
        """pose: {part: (rot, dx, dy, sx, sy)} (missing = rest); fx: callable(img_array, k) drawn on top;
        maw: (glow 0..1, colour rgb, centre (x, y) base px, radius base px) lights the gullet."""
        out = np.zeros_like(self.src)
        base = self.core.copy()
        layers_back, layers_front = [], []
        cavity = np.zeros(self.src.shape[:2], bool)
        shift = (0., 0.)
        for p in self.parts:
            img, d, dpar = self._part_image(p, pose)
            (layers_back if p.z < 0 else layers_front).append((p.z, img))
            if p.fill == 'maw':
                # the gullet spans the jaw's sweep from its parent's offset (the upper teeth) to its own
                # (only the jaw's upper part sweeps a gullet: the teeth row, not the icicles / chin below it)
                ys_ = np.nonzero(p.mask.any(1))[0]
                upper = p.mask.copy()
                upper[ys_[0] + int((ys_[-1] - ys_[0]) * .45):] = False
                n = int(max(abs(d[0] - dpar[0]), abs(d[1] - dpar[1]))) + 1
                for s in np.linspace(0, 1, n + 1):
                    ox = dpar[0] + (d[0] - dpar[0]) * s; oy = dpar[1] + (d[1] - dpar[1]) * s
                    cavity |= np.roll(upper, (int(round(oy)), int(round(ox))), (0, 1))
                shift = ((dpar[0] + d[0]) * .5, (dpar[1] + d[1]) * .5)
        out = compose(out, self.fill_body)
        for z, img in sorted(layers_back, key=lambda t: t[0]):
            out = compose(out, img)
        out = compose(out, base)
        if cavity.any():
            m = maw
            if m is not None:
                m = (m[0], m[1], (m[2][0] + shift[0] / self.k, m[2][1] + shift[1] / self.k), m[3])
            out = compose(out, self._maw_layer(m, cavity))
        for z, img in sorted(layers_front, key=lambda t: t[0]):
            out = compose(out, img)
        if fx is not None:
            out = fx(out, self.k)
        return out

    def _part_image(self, p, pose):
        chain = self.world_xform(p.name, pose)
        # compose transforms: apply child first then parents; approximate by summing rotations about
        # each ancestor's pivot -- done by mapping the pivot through the parents.
        rot = 0.; dx = 0.; dy = 0.; sx = 1.; sy = 1.
        # accumulate: a point x -> parent(...(own(x)))
        # we transform the part's pivot through ancestors to find its final pivot position, and add rotations
        piv = np.array(p.piv, float)
        own = pose.get(p.name, (0, 0, 0, 1, 1))
        r_o, dx_o, dy_o, sx_o, sy_o = (list(own) + [1, 1])[:5]
        final_piv = piv + np.array([dx_o, dy_o]) * self.k
        total_rot = r_o
        for anc in reversed(chain[:-1]):
            a = pose.get(anc.name, (0, 0, 0, 1, 1))
            ar, adx, ady = a[0], a[1], a[2]
            ap = np.array(anc.piv, float)
            v = final_piv - ap
            rr = math.radians(ar)
            v = np.array([math.cos(rr) * v[0] - math.sin(rr) * v[1], math.sin(rr) * v[0] + math.cos(rr) * v[1]])
            final_piv = ap + v + np.array([adx, ady]) * self.k
            total_rot += ar
        d = final_piv - piv
        dpar = d - np.array([dx_o, dy_o]) * self.k
        if p.curl > 0:
            img = affine_layer(p.up, self.size, tuple(piv), r_o, d[0], d[1], sx_o, sy_o, curl=p.curl, reach=p.reach,
                               base_rot=total_rot - r_o)
        else:
            img = affine_layer(p.up, self.size, tuple(piv), total_rot, d[0], d[1], sx_o, sy_o)
        return img, d, dpar

    def _maw_layer(self, maw, cavity):
        out = np.zeros_like(self.src)
        # each row filled between its outermost cavity pixels (a solid gullet, not the teeth's comb)
        reg = np.zeros_like(cavity)
        rows = np.nonzero(cavity.any(1))[0]
        for y in rows:
            xs = np.nonzero(cavity[y])[0]
            inset = max(1, int(2 * self.k))
            if xs[-1] - xs[0] > 2 * inset:
                reg[y, xs[0] + inset:xs[-1] - inset + 1] = True
        dark = np.array(self.maw_dark, np.float32)
        out[reg, :3] = dark.astype(np.uint8)
        out[reg, 3] = 255
        if maw is not None and maw[0] > 0:
            g, col, (cx, cy), rad = maw
            ys, xs = np.nonzero(reg)
            d = np.hypot(xs - cx * self.k, (ys - cy * self.k) * 1.6) / (rad * self.k)
            # stepped bands
            lvl = np.clip(np.floor((1 - d) * 4 + g * 2.2) / 4, 0, 1) * min(1, g * 1.4)
            c = np.array(col, np.float32)
            hot = np.minimum(255, c * .45 + 150)
            band = np.where(lvl[:, None] > .74, hot, c)
            rgb = dark * (1 - lvl[:, None]) + band * lvl[:, None]
            out[ys, xs, :3] = np.clip(rgb, 0, 255).astype(np.uint8)
        return out


# ---- pixel-stepped FX ---------------------------------------------------------------------------------

def _blend_add(img, ys, xs, col, a):
    """Screen-ish add of `col` at alpha `a` onto img pixels (keeps alpha >= a*255)."""
    c = np.array(col, np.float32)
    p = img[ys, xs].astype(np.float32)
    rgb = p[:, :3] + (c - p[:, :3] * (c / 255.)) * a        # screen
    alpha = np.maximum(p[:, 3], 255 * a)
    img[ys, xs, :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    img[ys, xs, 3] = np.clip(alpha, 0, 255).astype(np.uint8)


def orb(img, k, cx, cy, r, pal, strength=1.):
    """Stepped glowing orb: pal = (outer, mid, core) rgb; r in base px."""
    if r <= 0 or strength <= 0:
        return img
    S = img.shape[0]
    R = r * k
    x0, x1 = int(max(0, cx * k - R * 1.8)), int(min(S, cx * k + R * 1.8 + 1))
    y0, y1 = int(max(0, cy * k - R * 1.8)), int(min(S, cy * k + R * 1.8 + 1))
    ys, xs = np.mgrid[y0:y1, x0:x1]
    d = np.hypot(xs + .5 - cx * k, ys + .5 - cy * k) / R
    for lim, col, a in ((1.75, pal[0], .35), (1.25, pal[0], .7), (.85, pal[1], .95), (.45, pal[2], 1.)):
        m = d <= lim
        if m.any():
            _blend_add(img, ys[m], xs[m], col, min(1, a * strength))
    return img


def ring(img, k, cx, cy, r, w, col, strength=1.):
    S = img.shape[0]
    R = r * k; W = max(1., w * k)
    x0, x1 = int(max(0, cx * k - R - W - 1)), int(min(S, cx * k + R + W + 2))
    y0, y1 = int(max(0, cy * k - R - W - 1)), int(min(S, cy * k + R + W + 2))
    ys, xs = np.mgrid[y0:y1, x0:x1]
    d = np.abs(np.hypot(xs + .5 - cx * k, ys + .5 - cy * k) - R)
    m = d <= W * .5
    if m.any():
        _blend_add(img, ys[m], xs[m], col, min(1, strength))
    return img


def spark(img, k, cx, cy, n, length, col, seed, strength=1., spread=360., heading=0., width=1):
    """n short straight streaks out of (cx, cy) -- a release flash / embers."""
    rng = np.random.RandomState(seed)
    S = img.shape[0]
    for i in range(n):
        ang = math.radians(heading + (rng.rand() - .5) * spread)
        L = length * (.5 + rng.rand() * .7) * k
        start = length * .25 * k * rng.rand()
        for t in np.arange(start, L, .7):
            x = int(cx * k + math.cos(ang) * t); y = int(cy * k + math.sin(ang) * t)
            for wx in range(-(width // 2), width - width // 2):
                xx = x + (wx if abs(math.sin(ang)) > .5 else 0); yy = y + (wx if abs(math.sin(ang)) <= .5 else 0)
                if 0 <= xx < S and 0 <= yy < S:
                    _blend_add(img, np.array([yy]), np.array([xx]), col, strength * (1 - t / max(L, 1)) * .9 + .1)
    return img


def bolt(img, k, x0, y0, x1, y1, col, seed, strength=1., jag=6, width=1):
    """A jagged electric arc between two points."""
    rng = np.random.RandomState(seed)
    S = img.shape[0]
    n = 7
    pts = [(x0, y0)]
    for i in range(1, n):
        t = i / n
        pts.append((x0 + (x1 - x0) * t + (rng.rand() - .5) * jag * 2, y0 + (y1 - y0) * t + (rng.rand() - .5) * jag * 2))
    pts.append((x1, y1))
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        L = max(1, int(math.hypot(bx - ax, by - ay) * k * 1.5))
        for j in range(L + 1):
            t = j / L
            x = int((ax + (bx - ax) * t) * k); y = int((ay + (by - ay) * t) * k)
            for wx in range(width):
                if 0 <= x + wx < S and 0 <= y < S:
                    _blend_add(img, np.array([y]), np.array([x + wx]), col, strength)
    return img


def shard(img, k, cx, cy, length, ang, col, edge, strength=1.):
    """A small pointed crystal (ice / glass) -- a filled diamond with a light edge."""
    S = img.shape[0]
    a = math.radians(ang)
    ux, uy = math.cos(a), math.sin(a)
    vx, vy = -uy, ux
    L = length * k; Wd = max(1.2, length * .28 * k)
    x0, x1 = int(cx * k - L - 2), int(cx * k + L + 2)
    y0, y1 = int(cy * k - L - 2), int(cy * k + L + 2)
    ys, xs = np.mgrid[max(0, y0):min(S, y1), max(0, x0):min(S, x1)]
    px = xs + .5 - cx * k; py = ys + .5 - cy * k
    u = px * ux + py * uy; v = px * vx + py * vy
    inside = (np.abs(u) / L + np.abs(v) / Wd) <= 1
    rim = inside & ((np.abs(u) / L + np.abs(v) / Wd) > .62)
    if inside.any():
        _blend_add(img, ys[inside], xs[inside], col, strength)
    if rim.any():
        _blend_add(img, ys[rim], xs[rim], edge, strength * .9)
    return img


def no_pink(img):
    """Pull any pink (hue 285..352, s > .25, v > .3) the FX might have mixed back to violet / orange (safety net)."""
    a = img.astype(np.float32) / 255.
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx = a[..., :3].max(-1); mn = a[..., :3].min(-1)
    d = mx - mn + 1e-6
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    pink = (h >= 285) & (h <= 352) & (s > .25) & (mx > .3) & (img[..., 3] > 0)
    return pink


def clamp_pink(img, to=270.):
    """Every pink pixel (WorldCue.IsPink) turned back to violet at hue `to`, same saturation / value. Only Space
    may wear pink; the FX screen-blend can push a violet storm glow over 285."""
    m = no_pink(img)
    if not m.any():
        return img
    a = img[m].astype(np.float32) / 255.
    mxv = a[:, :3].max(1); mnv = a[:, :3].min(1)
    s = (mxv - mnv) / np.maximum(mxv, 1e-6)
    h = to / 60.
    i = int(h) % 6; f = h - int(h)
    p = mxv * (1 - s); q = mxv * (1 - s * f); t = mxv * (1 - s * (1 - f))
    rgb = [(mxv, t, p), (q, mxv, p), (p, mxv, t), (p, q, mxv), (t, p, mxv), (mxv, p, q)][i]
    out = img.copy()
    out[m, 0] = np.clip(rgb[0] * 255 + .5, 0, 255); out[m, 1] = np.clip(rgb[1] * 255 + .5, 0, 255)
    out[m, 2] = np.clip(rgb[2] * 255 + .5, 0, 255)
    return out
