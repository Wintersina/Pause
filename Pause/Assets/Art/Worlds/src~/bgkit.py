"""Shared helpers for the world-background SVG generators.

Every background layer is authored as SVG by the per-world modules next to
this file, then rasterized with resvg (brew install resvg) and packed into the
PNGs/atlases Unity loads from Resources/Worlds/<World>/Backdrop/.

Conventions
  * Tiles are 512 x 1024 px and must loop vertically. Anything that crosses the
    top or bottom edge is drawn three times (y - H, y, y + H) by `wrap_y`, and
    instances are sorted by y so overlap order is the same on both sides of
    the seam.
  * Animated pieces are templates: a Python function of a phase in [0, 1)
    that returns an SVG. Rendering N phases gives a looping flipbook.
  * Style: flat 80s anime cels (see palette.py) -- flat fills, one shadow
    tone, one highlight tone, thick ink outlines; soft blur only for skies
    and lights. Colours stay dim: these sit behind the gameplay sprites.
"""
import json
import math
import os
import random
import shutil
import subprocess

from PIL import Image

TAU = math.pi * 2.0
HERE = os.path.dirname(os.path.abspath(__file__))
ART_WORLDS = os.path.dirname(HERE)                               # Assets/Art/Worlds
RESOURCES = os.path.join(os.path.dirname(ART_WORLDS), "Resources", "Worlds")


# --------------------------------------------------------------------- svg ---

def doc(w, h, body, defs=""):
    return ('<svg xmlns="http://www.w3.org/2000/svg" '
            'xmlns:xlink="http://www.w3.org/1999/xlink" '
            f'width="{w}" height="{h}" viewBox="0 0 {w} {h}">'
            f"<defs>{defs}</defs>{body}</svg>")


def blur(fid, sd, pad=1.0):
    p = int(pad * 100)
    return (f'<filter id="{fid}" x="-{p}%" y="-{p}%" width="{100 + 2 * p}%" '
            f'height="{100 + 2 * p}%" color-interpolation-filters="sRGB">'
            f'<feGaussianBlur stdDeviation="{sd}"/></filter>')


def lin(gid, stops, x1=0, y1=0, x2=0, y2=1, units="objectBoundingBox"):
    s = "".join(f'<stop offset="{o}" stop-color="{c}" stop-opacity="{a}"/>'
                for o, c, a in stops)
    return (f'<linearGradient id="{gid}" x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" '
            f'gradientUnits="{units}">{s}</linearGradient>')


def rad(gid, stops, cx=0.5, cy=0.5, r=0.5, fx=None, fy=None, units="objectBoundingBox"):
    s = "".join(f'<stop offset="{o}" stop-color="{c}" stop-opacity="{a}"/>'
                for o, c, a in stops)
    f = ""
    if fx is not None:
        f = f' fx="{fx}" fy="{fy}"'
    return (f'<radialGradient id="{gid}" cx="{cx}" cy="{cy}" r="{r}"{f} '
            f'gradientUnits="{units}">{s}</radialGradient>')


def pts(points):
    return " ".join(f"{x:.1f},{y:.1f}" for x, y in points)


def poly(points, fill, extra=""):
    return f'<polygon points="{pts(points)}" fill="{fill}" {extra}/>'


def path_smooth(points, closed=True):
    """Catmull-Rom through the points, as cubic beziers."""
    n = len(points)
    if n < 3:
        return "M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in points)
    d = [f"M{points[0][0]:.1f},{points[0][1]:.1f}"]
    rng = range(n) if closed else range(n - 1)
    for i in rng:
        p0 = points[(i - 1) % n] if closed or i > 0 else points[i]
        p1 = points[i]
        p2 = points[(i + 1) % n]
        p3 = points[(i + 2) % n] if closed or i + 2 < n else p2
        c1 = (p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6)
        c2 = (p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6)
        d.append(f"C{c1[0]:.1f},{c1[1]:.1f} {c2[0]:.1f},{c2[1]:.1f} {p2[0]:.1f},{p2[1]:.1f}")
    if closed:
        d.append("Z")
    return " ".join(d)


def wrap_y(items, H):
    """items: list of (y, fn(dy) -> svg). Draws each at dy in (-H, 0, +H),
    sorted by on-tile y so the seam keeps the same overlap order."""
    inst = []
    for y, fn in items:
        for dy in (-H, 0, H):
            yy = y + dy
            inst.append((yy, fn, dy))
    inst.sort(key=lambda t: t[0])
    return "".join(fn(dy) for _, fn, dy in inst)


class PNoise:
    """Periodic 1-D noise on t in [0, 1): a few integer-frequency sines."""

    def __init__(self, rnd, octaves=((1, 1.0), (2, 0.5), (3, 0.3), (5, 0.18), (8, 0.1))):
        self.terms = [(f, a * rnd.uniform(0.6, 1.0), rnd.uniform(0, TAU)) for f, a in octaves]
        self.norm = sum(a for _, a, _ in self.terms)

    def __call__(self, t):
        return sum(a * math.sin(TAU * f * t + p) for f, a, p in self.terms) / self.norm


def jitter_ridge(rnd, x0, y0, x1, y1, steps, amp):
    """Jagged line between two points (used for ridgelines)."""
    out = []
    for i in range(steps + 1):
        t = i / steps
        x = x0 + (x1 - x0) * t
        y = y0 + (y1 - y0) * t
        if 0 < i < steps:
            env = math.sin(math.pi * t)
            x += rnd.uniform(-amp, amp) * 0.4 * env
            y += rnd.uniform(-amp, amp) * env
        out.append((x, y))
    return out


def mix(c1, c2, t):
    a = hex_rgb(c1)
    b = hex_rgb(c2)
    return rgb_hex(tuple(a[i] + (b[i] - a[i]) * t for i in range(3)))


def hex_rgb(c):
    c = c.lstrip("#")
    return tuple(int(c[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def rgb_hex(rgb):
    return "#" + "".join(f"{int(max(0, min(1, v)) * 255 + 0.5):02x}" for v in rgb)


# ------------------------------------------------------------ landforms ---
# Flat cel style: a base tone, one hard shadow plane, one highlight shape
# (snow cap / lit crust) and a thick ink outline. No gradients on forms.

_uid = [0]


def uid(prefix):
    _uid[0] += 1
    return f"{prefix}{_uid[0]}"


def ink_attr(width=None):
    """Background ink (docs/art-style.md 2.4): BG_INK, round joins."""
    from palette import BG_INK, INK_W
    return f'stroke="{BG_INK}" stroke-width="{width or INK_W}" stroke-linejoin="round"'


def grain_defs():
    """Fractal-noise grain (docs/art-style.md 2.8). stitchTiles keeps it
    seamless across the tile's top/bottom edge."""
    return ('<filter id="grain" x="0" y="0" width="100%" height="100%">'
            '<feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="2" seed="7" stitchTiles="stitch"/>'
            '<feColorMatrix type="matrix" values="0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  0 0 0 0.9 -0.35"/>'
            '</filter>')


def grain(w, h):
    from palette import GRAIN
    return f'<rect width="{w}" height="{h}" filter="url(#grain)" opacity="{GRAIN}"/>'


def hard_glow(x, y, r, color, alpha=0.5, steps=3):
    """'Hard bloom': stepped flat halos instead of a soft blur."""
    out = []
    for i in range(steps, 0, -1):
        k = i / steps
        out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r * k:.1f}" fill="{color}" '
                   f'opacity="{alpha * (1 - k + 1 / steps) / 1.6:.2f}"/>')
    return "".join(out)


def peak(rnd, cx, base, w, h, pal, cap=0.0, rim=None, cracks=None, side=None, skirt=0.6, ink=None):
    """A flat 3/4-view mountain whose apex points up the screen.

    pal: dict(lit, dark, cap, cap_dark). Left face lit, right face one hard
    shadow plane split along an angular spine. `cap` adds a snow/crust cap
    down to that fraction of the height. `side` (0 = left edge, 1 = right
    edge) replaces the flat base with a skirt sloping off-screen, so stacked
    ranges overlap as layered silhouettes. Returns (defs, body)."""
    ax = cx + rnd.uniform(-0.12, 0.12) * w
    ay = base - h
    left = jitter_ridge(rnd, cx - w / 2, base, ax, ay, 4, h * 0.07)
    right = jitter_ridge(rnd, ax, ay, cx + w / 2, base, 4, h * 0.07)
    sx = cx + rnd.uniform(0.0, 0.15) * w
    spine = jitter_ridge(rnd, ax, ay, sx, base, 3, w * 0.05)
    tail = []
    if side == 0:
        tail = [(-40, base + h * skirt), (-40, base)]
    elif side == 1:
        tail = [(cx + w / 2 + 40 + w, base + h * skirt), (cx + w / 2 + 40 + w, base)]
    if side == 0:
        outline = left + right[1:] + [(-40, base + h * skirt)] + [(-40, base)]
        outline = left + right[1:] + [(cx - w / 2 - 40, base + h * skirt)]
    elif side == 1:
        outline = left + right[1:] + [(cx + w / 2 + 40, base + h * skirt)]
    else:
        outline = left + right[1:]
    shadow = spine + list(reversed(right))
    if side is not None:
        shadow = spine + [(sx + (w if side == 1 else -w * 0.1), base + h * skirt)] + list(reversed(right))
    clip = uid("c")
    defs = f'<clipPath id="{clip}"><polygon points="{pts(outline)}"/></clipPath>'
    body = [poly(outline, pal["lit"])]
    inner = [poly(shadow, pal["dark"])]
    if cap > 0:
        cy = ay + h * cap
        edge = []
        n = 6
        for i in range(n + 1):
            t = i / n
            x = cx - w / 2 + w * t
            dip = (h * cap * 0.35) if i % 2 else -(h * cap * 0.15)
            edge.append((x, cy + dip - abs(t - 0.5) * h * cap * 0.8))
        capl = [(cx - w, ay - 10), (cx + w, ay - 10)] + list(reversed(edge))
        cs = clip + "s"
        defs += f'<clipPath id="{cs}"><polygon points="{pts(shadow)}"/></clipPath>'
        inner.append(poly(capl, pal["cap"]))
        inner.append(f'<g clip-path="url(#{cs})">{poly(capl, pal["cap_dark"])}</g>')
    if cracks:
        for i in range(3):
            x0 = ax + rnd.uniform(-0.22, 0.22) * w
            y0 = ay + h * rnd.uniform(0.15, 0.45)
            seg = jitter_ridge(rnd, x0, y0, x0 + rnd.uniform(-0.2, 0.2) * w, y0 + h * rnd.uniform(0.25, 0.45), 4, 6)
            d = "M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in seg)
            inner.append(f'<path d="{d}" stroke="{cracks}" stroke-width="5" fill="none" opacity="0.35"/>'
                         f'<path d="{d}" stroke="{cracks}" stroke-width="1.8" fill="none" opacity="0.9"/>')
    body.append(f'<g clip-path="url(#{clip})">{"".join(inner)}</g>')
    body.append(f'<polygon points="{pts(outline)}" fill="none" {ink_attr(ink)}/>')
    if rim:
        d = "M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in left)
        body.append(f'<path d="{d}" stroke="{rim}" stroke-width="1.6" fill="none" opacity="0.75" '
                    f'transform="translate(2.2 1)"/>')
    return defs, "".join(body)


def edge_range(rnd, W, H, pal, count, wmin, wmax, hmin, hmax, inner, cap=0.0, rim=None,
               cracks=None, sides=(0, 1), ink=None, foot=None):
    """Flat peaks stacked along both screen edges, looping vertically. Each
    peak's skirt slopes off the screen edge, so the range reads as layered
    cel silhouettes. `inner` is how far (px) toward the centre peaks reach.
    `foot` (colour) draws a continuous ink-edged foothill band first."""
    items = []
    for side in sides:
        for i in range(count):
            y = (i + rnd.uniform(-0.2, 0.2)) * H / count
            w = rnd.uniform(wmin, wmax)
            h = rnd.uniform(hmin, hmax)
            reach = rnd.uniform(0.55, 1.0) * inner
            cx = (reach - w / 2) if side == 0 else (W - reach + w / 2)
            items.append((y, cx, w, h, side, rnd.random()))
    defs, body = [], []
    if foot:
        for side in sides:
            n = PNoise(rnd)
            ys = [H * k / 32 for k in range(33)]
            fx = [(inner * 0.45 + inner * 0.12 * n(y / H)) for y in ys]
            if side == 0:
                shape = [(-10, -10)] + [(x, y) for x, y in zip(fx, ys)] + [(-10, H + 10)]
            else:
                shape = [(W + 10, -10)] + [(W - x, y) for x, y in zip(fx, ys)] + [(W + 10, H + 10)]
            body.append(f'<polygon points="{pts(shape)}" fill="{foot}" {ink_attr(ink)}/>')
    out = []
    for y, cx, w, h, side, seed in items:
        for dy in (-H, 0, H):
            out.append((y + dy, cx, w, h, side, seed))
    out.sort(key=lambda t: t[0])
    for y, cx, w, h, side, seed in out:
        if y - h > H + 20 or y + h < -20:
            continue
        d, b = peak(random.Random(seed), cx, y, w, h, pal, cap, rim, cracks, side, ink=ink)
        defs.append(d)
        body.append(b)
    return "".join(defs), "".join(body)


# ------------------------------------------------------------- rendering ---

def render(svg_text, svg_path, png_path, w=None, h=None):
    os.makedirs(os.path.dirname(svg_path), exist_ok=True)
    with open(svg_path, "w") as f:
        f.write(svg_text)
    cmd = ["resvg", svg_path, png_path]
    if w:
        cmd[1:1] = ["-w", str(w), "-h", str(h)]
    subprocess.run(cmd, check=True)


class World:
    """Collects one world's layers, renders and packs them."""

    def __init__(self, name):
        self.name = name
        self.src = os.path.join(ART_WORLDS, name, "src~")
        self.svgdir = os.path.join(self.src, "svg")
        self.tmp = os.path.join(self.src, ".build")
        self.out = os.path.join(RESOURCES, name, "Backdrop")
        for d in (self.svgdir, self.tmp, self.out):
            os.makedirs(d, exist_ok=True)
        self.atlases = {}     # atlas name -> list of (sprite name, png path)

    def tile(self, name, svg_text):
        """A full 512x1024 tile written straight to Resources."""
        render(svg_text, os.path.join(self.svgdir, name + ".svg"),
               os.path.join(self.out, name + ".png"))

    def sprite(self, atlas, name, svg_text, keep_svg=True, size=None):
        """size=(w, h) rasterizes the SVG at a different pixel size."""
        svg_path = os.path.join(self.svgdir if keep_svg else self.tmp, name + ".svg")
        png = os.path.join(self.tmp, name + ".png")
        render(svg_text, svg_path, png, *(size or (None, None)))
        self.atlases.setdefault(atlas, []).append((name, png))

    def flipbook(self, atlas, name, frames, fn, size=None):
        """fn(phase) -> svg. Only frame 00 is kept as a source; the rest are
        regenerated from the template in the module."""
        for i in range(frames):
            self.sprite(atlas, f"{name}_{i:02d}", fn(i / frames), keep_svg=(i == 0), size=size)

    def pack(self, size=1024, pad=2):
        for atlas, items in self.atlases.items():
            imgs = [(n, Image.open(p).convert("RGBA")) for n, p in items]
            # Skyline packer, tallest first; keeps flipbook frames adjacent.
            order = sorted(imgs, key=lambda t: (-t[1].height, -t[1].width, t[0]))
            sheet = Image.new("RGBA", (size, size), (0, 0, 0, 0))
            sky = [0] * size          # per-column filled height
            rects = []
            for n, im in order:
                w, h = im.size
                best = None
                for x in range(0, size - w + 1, 2):
                    y = max(sky[x:x + w + pad])
                    if y + h <= size and (best is None or y < best[1]):
                        best = (x, y)
                if best is None:
                    raise SystemExit(f"{self.name}/{atlas}: atlas overflow at {n}")
                x, y = best
                for c in range(x, min(size, x + w + pad)):
                    sky[c] = y + h + pad
                sheet.paste(im, (x, y))
                rects.append({"n": n, "x": x, "y": y, "w": w, "h": h})
            # Trim to the smallest power-of-two height that holds everything,
            # so a half-empty atlas doesn't cost a full 1024 x 1024.
            used = max(r["y"] + r["h"] for r in rects)
            height = 128
            while height < used:
                height *= 2
            sheet = sheet.crop((0, 0, size, height))
            for r in rects:
                r["y"] = height - r["y"] - r["h"]   # Unity rects are bottom-left based.
            sheet.save(os.path.join(self.out, atlas + ".png"), optimize=True)
            with open(os.path.join(self.out, atlas + ".json"), "w") as f:
                json.dump({"sprites": sorted(rects, key=lambda r: r["n"])}, f, indent=0)
        shutil.rmtree(self.tmp, ignore_errors=True)
