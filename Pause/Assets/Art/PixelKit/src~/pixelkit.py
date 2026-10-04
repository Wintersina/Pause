"""PixelKit: a small procedural kit for Pause's neon pixel art (docs/art-style.md v2).

Agents can't hand-place pixels, so the look is built in a fixed order, always at
NATIVE game-pixel resolution (1 game pixel = 1/64 world unit), then upscaled:

    1. shapes      boolean masks (no anti-aliasing): disc, ellipse, rect, poly, capsule, ring, line
    2. heights     a fake height field per shape: sphere, dome (pillow), bevel, flat
    3. shade       normals from the height field -> Lambert -> + texture -> quantised to a
                   4-6 tone material RAMP (optionally Bayer-dithered only on band borders)
    4. detail      panel lines, scratches, rivets, cracks, facets (all drawn in ramp colours)
    5. rim         1 px rim light on the side away from the key light
    6. outline     1 px dark SELECTIVE outline (ink on the shadow side, darkened local
                   colour on the lit side)
    7. neon        hard emissive pixels (cores, lights) painted in a neon ramp
    8. glow        a separate halo layer built from the emissive pixels: blurred, then
                   quantised to 3-4 alpha steps so it reads as a pixel halo, composited
                   ADDITIVELY behind/over the sprite
    9. particles   sparks, snowflakes, leaves, embers, energy arcs
   10. export      frames -> strip/sheet -> integer nearest-neighbour upscale; GIF preview

Only numpy + Pillow. Images are numpy uint8 arrays shaped (h, w, 4) (RGBA), masks are
bool arrays shaped (h, w), height fields are float arrays in 0..1.

Quick use:

    import pixelkit as pk
    W = H = 64
    body = pk.disc(W, H, 38, 33, 17)
    img = pk.shade(body, pk.height_sphere(body, 38, 33, 17), pk.PAL["space"]["metal"],
                   tex=pk.value_noise(W, H, 4, seed=1), tex_amt=.12)
    img = pk.outline(img)
    pk.save(pk.upscale(img, 4), "mine.png")
"""
from __future__ import annotations

import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

# --------------------------------------------------------------------------- constants

GAME_PPU = 64          # game pixels per world unit (art-style.md v2 section 2.1)
EXPORT_SCALE = 4       # integer nearest-neighbour upscale on export -> texture PPU 256
TICK_FPS = 24          # animation timing is authored in ticks of 1/24 s
LIGHT = (-0.55, -0.65, 0.52)   # key light: top-left, towards the viewer (x right, y down)


def hexrgb(h: str) -> tuple[int, int, int]:
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def ramp(*hexes: str) -> np.ndarray:
    """A material or neon ramp, dark -> light, as an (n, 3) uint8 array."""
    return np.array([hexrgb(h) for h in hexes], dtype=np.uint8)


INK = "#05060c"        # the outline / deepest shadow colour, shared by every world
PLAYER_RED = "#d8232c"  # reserved for the player; never on an enemy

# Per-world palettes. Sampled from the original rail-mine atlas (git 18b5e5f^) with
# Pillow median-cut, then cleaned up into even ramps. Ember's magma was shifted out of
# red into orange/amber (enemy-never-red), Verdant's thorns from crimson to magenta.
PAL = {
    "space": {
        "metal": ramp("#0b0f15", "#1a2129", "#2f343b", "#4b5059", "#7a7c82", "#b0b4b6"),
        "trim":  ramp("#0a1118", "#162e3c", "#2f4550", "#476d7b"),
        "neon":  ramp("#0b4f7a", "#0b91cc", "#0bd0f6", "#7af6fc", "#f5fdfd"),
        "glow":  "#15d8fc",
        "bg":    ramp("#03050a", "#070c16", "#0c1424", "#132036", "#1c2e48"),
        "bg_neon": "#22668a",
    },
    "frost": {
        "ice":   ramp("#0a1734", "#1c3c68", "#2a62ae", "#4a96e6", "#9fd2f2", "#e2fbff"),
        "metal": ramp("#0c1020", "#1e2638", "#33405c", "#4f6188", "#7d93bd"),
        "neon":  ramp("#1569c8", "#1898f7", "#46ddfd", "#aaf2fb", "#f8fefc"),
        "glow":  "#46c8fd",
        "bg":    ramp("#03060e", "#07101f", "#0d1a31", "#142645", "#1d3558"),
        "bg_neon": "#2e6c98",
    },
    "verdant": {
        "bark":  ramp("#0c0703", "#281b0a", "#3e2610", "#5a3a17", "#7d5426", "#a07a3c"),
        "vine":  ramp("#0e1c03", "#1f3f04", "#306203", "#4dae03", "#a6d32a"),
        "neon":  ramp("#3d8f02", "#70d804", "#b8f018", "#e6fc5e", "#fbffd8"),
        "thorn": ramp("#3a0a2a", "#8c1462", "#e0309e", "#ff9ad6"),
        "glow":  "#8ef014",
        "bg":    ramp("#030603", "#060f07", "#0b1a0d", "#122615", "#1b351c"),
        "bg_neon": "#3f7a22",
    },
    "ember": {
        "basalt": ramp("#0e0808", "#1d1212", "#2e1d1a", "#47302b", "#6a4a40", "#957060"),
        "magma":  ramp("#6a2404", "#c24a06", "#f77a0a", "#fcb809", "#fcee09", "#fdfa92"),
        "neon":   ramp("#c24a06", "#fc7a08", "#fcb809", "#fdf06a", "#fffbe0"),
        "glow":   "#fc8a10",
        "bg":     ramp("#060303", "#100707", "#1a0c0a", "#26130f", "#341c16"),
        "bg_neon": "#8f4a1c",
    },
}

# --------------------------------------------------------------------------- shapes

def _draw(w, h, fn) -> np.ndarray:
    im = Image.new("L", (w, h), 0)
    fn(ImageDraw.Draw(im))
    return np.array(im) > 127


def ellipse(w, h, x0, y0, x1, y1) -> np.ndarray:
    return _draw(w, h, lambda d: d.ellipse((x0, y0, x1, y1), fill=255))


def disc(w, h, cx, cy, r) -> np.ndarray:
    yy, xx = np.mgrid[0:h, 0:w]
    return (xx - cx) ** 2 + (yy - cy) ** 2 <= r * r + r * 0.6


def ring(w, h, cx, cy, r_out, r_in) -> np.ndarray:
    return disc(w, h, cx, cy, r_out) & ~disc(w, h, cx, cy, r_in)


def rect(w, h, x0, y0, x1, y1) -> np.ndarray:
    m = np.zeros((h, w), bool)
    m[max(0, y0):max(0, y1 + 1), max(0, x0):max(0, x1 + 1)] = True
    return m


def round_rect(w, h, x0, y0, x1, y1, r=2) -> np.ndarray:
    return _draw(w, h, lambda d: d.rounded_rectangle((x0, y0, x1, y1), radius=r, fill=255))


def poly(w, h, pts) -> np.ndarray:
    return _draw(w, h, lambda d: d.polygon([tuple(p) for p in pts], fill=255))


def line(w, h, pts, width=1) -> np.ndarray:
    return _draw(w, h, lambda d: d.line([tuple(p) for p in pts], fill=255, width=width))


def capsule(w, h, p0, p1, r) -> np.ndarray:
    """Pill between two points: everything within r of the segment."""
    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    (ax, ay), (bx, by) = p0, p1
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy or 1e-6
    t = np.clip(((xx - ax) * dx + (yy - ay) * dy) / L2, 0, 1)
    d2 = (xx - ax - t * dx) ** 2 + (yy - ay - t * dy) ** 2
    return d2 <= r * r + r * 0.5


def star(w, h, cx, cy, r_out, r_in, points=4, rot=0.0) -> np.ndarray:
    pts = []
    for i in range(points * 2):
        a = rot + math.pi * i / points
        r = r_out if i % 2 == 0 else r_in
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return poly(w, h, pts)


def polar_pts(cx, cy, r, deg):
    a = math.radians(deg)
    return cx + r * math.cos(a), cy + r * math.sin(a)


# --------------------------------------------------------------------------- mask ops

def shift(m: np.ndarray, dx: int, dy: int) -> np.ndarray:
    out = np.zeros_like(m)
    h, w = m.shape[:2]
    xs0, xs1 = max(0, -dx), min(w, w - dx)
    ys0, ys1 = max(0, -dy), min(h, h - dy)
    out[ys0 + dy:ys1 + dy, xs0 + dx:xs1 + dx] = m[ys0:ys1, xs0:xs1]
    return out


def dilate(m, n=1, diag=False):
    for _ in range(n):
        o = m | shift(m, 1, 0) | shift(m, -1, 0) | shift(m, 0, 1) | shift(m, 0, -1)
        if diag:
            o |= shift(m, 1, 1) | shift(m, -1, -1) | shift(m, 1, -1) | shift(m, -1, 1)
        m = o
    return m


def erode(m, n=1):
    for _ in range(n):
        m = m & shift(m, 1, 0) & shift(m, -1, 0) & shift(m, 0, 1) & shift(m, 0, -1)
    return m


def edge(m, dx=0, dy=0):
    """Pixels of m on its border; with dx/dy only the border facing that direction."""
    if dx or dy:
        return m & ~shift(m, -dx, -dy)
    return m & ~erode(m)


def distance(m, cap=32) -> np.ndarray:
    """Approximate distance (in pixels) from each inside pixel to the mask border."""
    d = np.zeros(m.shape, float)
    cur = m.copy()
    for i in range(cap):
        if not cur.any():
            break
        d[cur] += 1
        cur = erode(cur)
    return d


# --------------------------------------------------------------------------- heights

def height_sphere(m, cx, cy, r) -> np.ndarray:
    yy, xx = np.mgrid[0:m.shape[0], 0:m.shape[1]]
    z = np.sqrt(np.clip(1 - ((xx - cx) ** 2 + (yy - cy) ** 2) / (r * r), 0, 1))
    return np.where(m, z, 0.0)


def height_dome(m, width=None) -> np.ndarray:
    """Pillow-shaped height from the distance to the border (any silhouette)."""
    d = distance(m)
    width = width or max(1.0, d.max())
    t = np.clip(d / width, 0, 1)
    return np.where(m, np.sqrt(1 - (1 - t) ** 2), 0.0)


def height_bevel(m, width=2) -> np.ndarray:
    """Flat top with a hard bevel of `width` px: plates, brackets, panels."""
    return np.where(m, np.clip(distance(m) / width, 0, 1), 0.0)


def normals(hf, strength=6.0) -> np.ndarray:
    gy, gx = np.gradient(hf)
    n = np.dstack([-gx * strength, -gy * strength, np.ones_like(hf)])
    return n / np.linalg.norm(n, axis=2, keepdims=True)


# --------------------------------------------------------------------------- textures

def value_noise(w, h, scale=4, seed=0, octaves=2) -> np.ndarray:
    """Smooth-ish noise in -1..1, cell size `scale` px."""
    rng = np.random.default_rng(seed)
    out = np.zeros((h, w))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        s = max(1, int(scale / (2 ** o)))
        g = rng.uniform(-1, 1, (h // s + 2, w // s + 2))
        im = Image.fromarray(((g + 1) * 127.5).astype(np.uint8)).resize(
            ((w // s + 2) * s, (h // s + 2) * s), Image.BILINEAR)
        out += amp * (np.array(im)[:h, :w] / 127.5 - 1)
        total += amp
        amp *= 0.5
    return out / total


def grain(w, h, seed=0) -> np.ndarray:
    return np.random.default_rng(seed).uniform(-1, 1, (h, w))


def scratches(w, h, n=8, seed=0, length=(2, 4), within=None) -> np.ndarray:
    """Short 1px diagonal/horizontal scuffs (metal)."""
    rng = random.Random(seed)
    m = np.zeros((h, w), bool)
    ys, xs = np.nonzero(within) if within is not None else (None, None)
    for _ in range(n):
        if xs is not None and len(xs):
            i = rng.randrange(len(xs))
            x, y = xs[i], ys[i]
        else:
            x, y = rng.randrange(w), rng.randrange(h)
        dx, dy = rng.choice([(1, 0), (1, 1), (1, -1), (0, 1)])
        for k in range(rng.randint(*length)):
            xx, yy = x + dx * k, y + dy * k
            if 0 <= xx < w and 0 <= yy < h:
                m[yy, xx] = True
    return m if within is None else m & within


def cracks(w, h, starts, steps=10, seed=0, branch=0.25, within=None) -> np.ndarray:
    """Branching random-walk cracks (magma veins, ice fractures, bark grain)."""
    rng = random.Random(seed)
    m = np.zeros((h, w), bool)
    stack = [(x, y, rng.uniform(0, 2 * math.pi), steps) for x, y in starts]
    while stack:
        x, y, a, s = stack.pop()
        for _ in range(s):
            a += rng.uniform(-0.6, 0.6)
            x += math.cos(a)
            y += math.sin(a)
            xi, yi = int(round(x)), int(round(y))
            if not (0 <= xi < w and 0 <= yi < h):
                break
            if within is not None and not within[yi, xi]:
                break
            m[yi, xi] = True
            if rng.random() < branch * 0.15:
                stack.append((x, y, a + rng.choice([-1, 1]) * 1.1, s // 2))
    return m


def facets(w, h, n=10, seed=0, within=None) -> tuple[np.ndarray, np.ndarray]:
    """Voronoi facets: returns (cell_id map, per-pixel tilt in -1..1) for crystal/ice/rock."""
    rng = np.random.default_rng(seed)
    pts = rng.uniform(0, 1, (n, 2)) * [w, h]
    if within is not None and within.any():
        ys, xs = np.nonzero(within)
        idx = rng.choice(len(xs), n)
        pts = np.stack([xs[idx], ys[idx]], 1).astype(float)
    yy, xx = np.mgrid[0:h, 0:w]
    d = (xx[..., None] - pts[:, 0]) ** 2 + (yy[..., None] - pts[:, 1]) ** 2
    ids = d.argmin(-1)
    tilt = rng.uniform(-1, 1, n)[ids]
    return ids, tilt


def facet_lines(ids, within=None) -> np.ndarray:
    m = (ids != np.roll(ids, 1, 0)) | (ids != np.roll(ids, 1, 1))
    return m if within is None else m & erode(within)


# --------------------------------------------------------------------------- shading

_BAYER4 = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0


def bayer(w, h) -> np.ndarray:
    return np.tile(_BAYER4, (h // 4 + 1, w // 4 + 1))[:h, :w] - 0.47


def blank(w, h) -> np.ndarray:
    return np.zeros((h, w, 4), np.uint8)


def shade(mask, hf, rmp, light=LIGHT, ambient=0.18, tex=None, tex_amt=0.12,
          dither=0.0, bias=0.0, strength=6.0, spec=0.0) -> np.ndarray:
    """Lambert-light a height field and quantise it onto a ramp.

    strength converts the 0..1 height field to pixels: use about the shape's radius
    (sphere) or bevel/dome width, so the slopes are true; too low = flat plateau.
    spec: 0..0.1, pixels this close to full light snap to the top ramp tone (hot spot).

    dither: 0 = hard bands (default; preferred), 0.3-0.6 = ordered dither on band borders only.
    bias shifts the whole object up/down the ramp (use for 'in shadow' parts).
    """
    h, w = mask.shape
    n = normals(hf, strength)
    L = np.array(light, float)
    L /= np.linalg.norm(L)
    lam = np.clip((n * L).sum(-1), 0, 1)
    v = ambient + (1 - ambient) * lam + bias
    if tex is not None:
        v = v + tex * tex_amt
    k = len(rmp) - 1
    idx = v * k
    if dither:
        idx = idx + bayer(w, h) * dither
    idx = np.clip(np.round(idx), 0, k).astype(int)
    if spec:
        idx[lam >= 1 - spec] = k
    img = blank(w, h)
    img[..., :3] = rmp[idx]
    img[..., 3] = np.where(mask, 255, 0)
    img[~mask] = 0
    return img


def fill(img, mask, color, alpha=255):
    """Paint a flat colour (hex or rgb) into mask, in place. Returns img."""
    c = hexrgb(color) if isinstance(color, str) else color
    img[mask, :3] = c
    img[mask, 3] = alpha
    return img


def tone(img, mask, rmp, steps):
    """Move masked pixels `steps` ramp tones up (+) or down (-) by nearest-ramp lookup."""
    if not mask.any():
        return img
    px = img[mask, :3].astype(int)
    d = ((px[:, None, :] - rmp[None].astype(int)) ** 2).sum(-1)
    idx = np.clip(d.argmin(1) + steps, 0, len(rmp) - 1)
    img[mask, :3] = rmp[idx]
    return img


def over(dst, src, x=0, y=0) -> np.ndarray:
    """Alpha-composite src over dst at (x, y). Returns a new array."""
    out = dst.astype(float).copy()
    h, w = src.shape[:2]
    H, W = dst.shape[:2]
    x0, y0, x1, y1 = max(0, x), max(0, y), min(W, x + w), min(H, y + h)
    if x0 >= x1 or y0 >= y1:
        return dst
    s = src[y0 - y:y1 - y, x0 - x:x1 - x].astype(float)
    d = out[y0:y1, x0:x1]
    sa = s[..., 3:4] / 255
    da = d[..., 3:4] / 255
    oa = sa + da * (1 - sa)
    rgb = (s[..., :3] * sa + d[..., :3] * da * (1 - sa)) / np.maximum(oa, 1e-6)
    out[y0:y1, x0:x1, :3] = rgb
    out[y0:y1, x0:x1, 3:4] = oa * 255
    return np.clip(out + 0.5, 0, 255).astype(np.uint8)


def add(dst, src, x=0, y=0) -> np.ndarray:
    """Additive composite (glow, bloom). Alpha of the result is max of both."""
    out = dst.astype(float).copy()
    h, w = src.shape[:2]
    H, W = dst.shape[:2]
    x0, y0, x1, y1 = max(0, x), max(0, y), min(W, x + w), min(H, y + h)
    s = src[y0 - y:y1 - y, x0 - x:x1 - x].astype(float)
    d = out[y0:y1, x0:x1]
    sa = s[..., 3:4] / 255
    da = d[..., 3:4] / 255
    # over a transparent destination an additive layer is just itself
    rgb = d[..., :3] * da + s[..., :3] * sa
    a = np.maximum(da, sa)
    out[y0:y1, x0:x1, :3] = np.where(a > 0, rgb / np.maximum(a, 1e-6), 0)
    out[y0:y1, x0:x1, 3:4] = a * 255
    return np.clip(out + 0.5, 0, 255).astype(np.uint8)


# --------------------------------------------------------------------------- outline & rim

def outline(img, ink=INK, selective=True, light=LIGHT, lit_mix=0.45) -> np.ndarray:
    """1px outer outline. Selective: on the lit side the line is the neighbour's colour
    darkened (sel-out), on the shadow side it is pure ink."""
    a = img[..., 3] > 0
    ring_ = dilate(a) & ~a
    out = img.copy()
    inkc = np.array(hexrgb(ink), float)
    out[ring_, :3] = inkc.astype(np.uint8)
    out[ring_, 3] = 255
    if selective:
        lx, ly = light[0], light[1]   # direction towards the light
        # an outline pixel is on the lit side when its sprite neighbour lies away from the light
        lit = np.zeros_like(a)
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if dx * lx + dy * ly < 0:   # neighbour at (x+dx) is further from the light
                lit |= ring_ & shift(a, -dx, -dy)
        nb = np.zeros(img.shape[:2] + (3,), float)
        cnt = np.zeros(img.shape[:2], float)
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            s = shift(a, -dx, -dy)
            col = np.roll(np.roll(img[..., :3].astype(float), -dy, 0), -dx, 1)
            nb[s] += col[s]
            cnt[s] += 1
        nb /= np.maximum(cnt, 1)[..., None]
        c = inkc * (1 - lit_mix) + nb * lit_mix * 0.55
        out[lit, :3] = np.clip(c[lit], 0, 255).astype(np.uint8)
    return out


def part(mask, hf, rmp, rim_color=None, rim_dir=(1, 1), outline_=True, **shade_kw) -> np.ndarray:
    """One shaded, rim-lit, outlined part. Build sprites from parts back to front with
    over(): every part keeps its own 1px ink line, which is what separates overlapping
    pieces (pods over a hull, a clamp over a sphere) in pixel art."""
    img = shade(mask, hf, rmp, **shade_kw)
    if rim_color is not None:
        img = rim(img, mask, rim_color, *rim_dir)
    return outline(img) if outline_ else img


def inner_line(img, mask, color=INK) -> np.ndarray:
    """Panel seams / interior separation lines in a flat colour."""
    return fill(img.copy(), mask & (img[..., 3] > 0), color)


def rim(img, mask, color, dx=1, dy=1, width=1) -> np.ndarray:
    """Rim light: the border pixels of `mask` facing (dx, dy) (away from the key light)."""
    out = img.copy()
    m = mask.copy()
    for _ in range(width):
        e = edge(m, dx, dy)
        fill(out, e, color)
        m = m & ~e
    return out


def highlight(img, pts, color):
    out = img.copy()
    c = hexrgb(color) if isinstance(color, str) else color
    for x, y in pts:
        if 0 <= y < out.shape[0] and 0 <= x < out.shape[1] and out[y, x, 3]:
            out[y, x, :3] = c
    return out


# --------------------------------------------------------------------------- glow

def glow(emissive, color, radius=3, strength=1.0, steps=4, core=None) -> np.ndarray:
    """Pixel halo from an emissive mask (bool) or RGBA image.

    Blurs, then quantises alpha into `steps` hard levels so the halo reads as
    stepped pixel rings, not a smooth airbrush. Composite with add() (behind or over).
    `core`: optional brighter colour for the inner step.
    """
    m = emissive if emissive.dtype == bool else emissive[..., 3] > 0
    h, w = m.shape
    im = Image.fromarray((m * 255).astype(np.uint8))
    b = np.array(im.filter(ImageFilter.GaussianBlur(radius))).astype(float) / 255
    b = np.clip(b * 2.2 * strength, 0, 1)
    q = np.ceil(b * steps) / steps
    q[b < 0.04] = 0
    out = blank(w, h)
    c = np.array(hexrgb(color) if isinstance(color, str) else color)
    out[..., :3] = c
    if core is not None:
        cc = np.array(hexrgb(core))
        out[q >= 0.99, :3] = cc
    out[..., 3] = (q * 0.62 * 255).astype(np.uint8)   # glow never fully opaque
    return out


def halo(w, h, cx, cy, r, color, steps=4, peak=0.55) -> np.ndarray:
    """Round stepped halo (burst flash, aura) independent of any sprite mask."""
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / max(r, 1)
    t = np.clip(1 - d, 0, 1)
    q = np.ceil(t * steps) / steps
    out = blank(w, h)
    out[..., :3] = hexrgb(color)
    out[..., 3] = (q * peak * 255).astype(np.uint8)
    return out


def hit_flash(img, color="#f5fdfd", ink=INK) -> np.ndarray:
    """Hit-flash frame: every visible non-ink pixel becomes `color` (neon step 4),
    the outline stays ink. Hold for 2 ticks."""
    out = img.copy()
    inkc = np.array(hexrgb(ink))
    solid = (out[..., 3] > 0) & (np.abs(out[..., :3].astype(int) - inkc).sum(-1) > 24)
    out[solid, :3] = hexrgb(color) if isinstance(color, str) else color
    return out


def emissive_of(img, lum=200) -> np.ndarray:
    """Pixels bright enough to bloom (luma >= lum)."""
    rgb = img[..., :3].astype(float)
    y = 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]
    return (img[..., 3] > 0) & (y >= lum)


# --------------------------------------------------------------------------- particles

def _put(img, x, y, color, a=255):
    x, y = int(round(x)), int(round(y))
    if 0 <= y < img.shape[0] and 0 <= x < img.shape[1]:
        img[y, x, :3] = hexrgb(color) if isinstance(color, str) else color
        img[y, x, 3] = a


def twinkle(img, x, y, size, color, core="#ffffff"):
    """4-point plus-star (sparkle). size 1..4."""
    for k in range(1, size + 1):
        for dx, dy in ((k, 0), (-k, 0), (0, k), (0, -k)):
            _put(img, x + dx, y + dy, color)
    _put(img, x, y, core)
    return img


def spark(img, x, y, ang, length, color, tip="#ffffff"):
    """Streak spark: a 1px line with a bright tip."""
    for k in range(length):
        _put(img, x + math.cos(ang) * k, y + math.sin(ang) * k, color)
    _put(img, x + math.cos(ang) * length, y + math.sin(ang) * length, tip)
    return img


def snowflake(img, x, y, r, color, core="#ffffff"):
    """6-armed pixel snowflake, r 2..4."""
    for a in range(0, 360, 60):
        for k in range(1, r + 1):
            px, py = polar_pts(x, y, k, a + 90)
            _put(img, px, py, color)
        if r >= 3:
            bx, by = polar_pts(x, y, r - 1, a + 90)
            for s in (-1, 1):
                _put(img, *polar_pts(bx, by, 1, a + 90 + 60 * s), color)
    _put(img, x, y, core)
    return img


def leaf(img, x, y, ang, size, rmp):
    """Small pointed leaf with a 2-tone body and a dark midrib."""
    for k in range(size):
        cx, cy = x + math.cos(ang) * k, y + math.sin(ang) * k
        wdt = math.sin(math.pi * (k + .5) / size) * size * 0.35
        for s in np.linspace(-wdt, wdt, max(1, int(wdt * 2) + 1)):
            px, py = cx + math.cos(ang + math.pi / 2) * s, cy + math.sin(ang + math.pi / 2) * s
            _put(img, px, py, rmp[-1] if s < 0 else rmp[-2])
        _put(img, cx, cy, rmp[1])
    return img


def ember(img, x, y, color, core):
    _put(img, x, y, core)
    _put(img, x + 1, y, color)
    _put(img, x, y + 1, color)
    return img


def arc(img, p0, p1, color, jag=2.0, seed=0, core=None, bow=0.0):
    """Jagged 1px energy arc between two points (optionally bowed outwards)."""
    rng = random.Random(seed)
    (x0, y0), (x1, y1) = p0, p1
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    nx, ny = -(y1 - y0), (x1 - x0)
    ln = math.hypot(nx, ny) or 1
    nx, ny = nx / ln, ny / ln
    off = 0.0
    for i in range(n + 1):
        t = i / n
        off = max(-jag, min(jag, off + rng.uniform(-1, 1)))
        b = math.sin(math.pi * t) * bow
        x = x0 + (x1 - x0) * t + nx * (off + b)
        y = y0 + (y1 - y0) * t + ny * (off + b)
        _put(img, x, y, core if (core is not None and i % 3 == 0) else color)
    return img


def orbit_arc(img, cx, cy, r, a0, a1, color, wobble=1.0, seed=0, ry=None):
    """Curved energy arc on a circle/ellipse from angle a0 to a1 (degrees)."""
    rng = random.Random(seed)
    ry = ry or r
    steps = int(abs(a1 - a0) / 360 * 2 * math.pi * r * 1.5) + 2
    for i in range(steps):
        a = math.radians(a0 + (a1 - a0) * i / (steps - 1))
        rr = r + rng.uniform(-wobble, wobble)
        _put(img, cx + rr * math.cos(a), cy + rr * ry / r * math.sin(a), color)
    return img


def scatter(img, kind, n, box, seed=0, **kw):
    """Scatter particles of `kind` ('twinkle'|'snow'|'leaf'|'ember'|'dot') in box (x0,y0,x1,y1)."""
    rng = random.Random(seed)
    x0, y0, x1, y1 = box
    for _ in range(n):
        x, y = rng.uniform(x0, x1), rng.uniform(y0, y1)
        if kind == "twinkle":
            twinkle(img, x, y, rng.choice(kw.get("sizes", [1, 1, 2])), kw["color"], kw.get("core", "#ffffff"))
        elif kind == "snow":
            snowflake(img, x, y, rng.choice(kw.get("sizes", [2, 3])), kw["color"], kw.get("core", "#ffffff"))
        elif kind == "leaf":
            leaf(img, x, y, rng.uniform(0, 2 * math.pi), rng.choice(kw.get("sizes", [4, 5])), kw["ramp"])
        elif kind == "ember":
            ember(img, x, y, kw["color"], kw["core"])
        else:
            _put(img, x, y, kw["color"])
    return img


# --------------------------------------------------------------------------- export

BG_MAX_VALUE_P95 = 0.35   # 95th-percentile HSV value of a background
BG_ACCENT_VALUE = 0.60    # no background pixel brighter than this
BG_ACCENT_SAT = 0.80      # bright background pixels (value > .3) at most this saturated
BG_ACCENT_PCT = 3.0       # pixels with value > .40, percent of the background


def bg_check(img) -> dict:
    """Measure a background against the v2 readability limits. Returns metrics + 'ok'."""
    rgb = img[..., :3].astype(float) / 255
    mx, mn = rgb.max(-1), rgb.min(-1)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    bright = mx > .3
    m = {"p95_value": round(float(np.percentile(mx, 95)), 3),
         "max_value": round(float(mx.max()), 3),
         "max_sat_bright": round(float(sat[bright].max()) if bright.any() else 0.0, 3),
         "accent_pct": round(float((mx > .40).mean() * 100), 2)}
    m["ok"] = (m["p95_value"] <= BG_MAX_VALUE_P95 and m["max_value"] <= BG_ACCENT_VALUE
               and m["max_sat_bright"] <= BG_ACCENT_SAT and m["accent_pct"] <= BG_ACCENT_PCT)
    return m


def to_image(img) -> Image.Image:
    return Image.fromarray(img, "RGBA")


def upscale(img, k=EXPORT_SCALE) -> np.ndarray:
    """Integer nearest-neighbour upscale (the ONLY resampling allowed on sprites)."""
    return np.repeat(np.repeat(img, k, 0), k, 1)


def save(img, path):
    to_image(img).save(path, optimize=True)


def strip(frames, gap=0) -> np.ndarray:
    h = max(f.shape[0] for f in frames)
    w = sum(f.shape[1] for f in frames) + gap * (len(frames) - 1)
    out = blank(w, h)
    x = 0
    for f in frames:
        out[:f.shape[0], x:x + f.shape[1]] = f
        x += f.shape[1] + gap
    return out


def sheet(frames, cols) -> np.ndarray:
    fh, fw = frames[0].shape[:2]
    rows = (len(frames) + cols - 1) // cols
    out = blank(fw * cols, fh * rows)
    for i, f in enumerate(frames):
        out[(i // cols) * fh:(i // cols + 1) * fh, (i % cols) * fw:(i % cols + 1) * fw] = f
    return out


def on_bg(img, color="#05060c") -> np.ndarray:
    bg = blank(img.shape[1], img.shape[0])
    fill(bg, np.ones(img.shape[:2], bool), color)
    return over(bg, img)


def save_gif(frames, path, ticks, scale=EXPORT_SCALE, bg="#05060c"):
    """GIF preview. ticks: hold per frame in 1/24 s ticks (list or int)."""
    if isinstance(ticks, int):
        ticks = [ticks] * len(frames)
    ims = [to_image(upscale(on_bg(f, bg), scale)).convert("RGB") for f in frames]
    ims[0].save(path, save_all=True, append_images=ims[1:], loop=0,
                duration=[int(round(t * 1000 / TICK_FPS)) for t in ticks], disposal=2)


def palette_swatch(pal: dict, cell=10) -> np.ndarray:
    """Render a PAL entry (dict of ramps / hexes) as rows of swatches."""
    rows = [(k, v if isinstance(v, np.ndarray) else ramp(v)) for k, v in pal.items()]
    w = max(len(r) for _, r in rows) * cell
    out = blank(w, len(rows) * cell)
    for j, (_, r) in enumerate(rows):
        for i, c in enumerate(r):
            out[j * cell:(j + 1) * cell, i * cell:(i + 1) * cell, :3] = c
            out[j * cell:(j + 1) * cell, i * cell:(i + 1) * cell, 3] = 255
    return out
