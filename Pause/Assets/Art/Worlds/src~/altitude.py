"""Seen-from-altitude helpers for the planet worlds (Frost, Verdant, Ember).

The ship flies at atmosphere level; the ground is far below. Aerial
perspective rules used everywhere here:
  * farther = smaller, slower (runtime parallax), hazier and less contrasty
  * haze pulls a form toward the world's air colour (`hazed`), lights are
    hazed less than solid forms so they still glint
  * between the ground and the ship there are only clouds and haze bands
    (`cloud_cel`, `haze_band`), which are drawn as flat cels (base + one
    underside shadow + thin background ink), never soft puffs
"""
import math
import random

from bgkit import (TAU, doc, lin, pts, ink_attr, uid, wrap_y, peak, jitter_ridge)


def haze_filter(fid, color, amount):
    """Flat aerial haze: mixes `amount` of `color` over the source, inside
    its own alpha (so silhouettes keep their shape, contrast drops)."""
    return (f'<filter id="{fid}" x="0" y="0" width="100%" height="100%" color-interpolation-filters="sRGB">'
            f'<feFlood flood-color="{color}" flood-opacity="{amount}"/>'
            f'<feComposite in2="SourceAlpha" operator="in" result="h"/>'
            f'<feMerge><feMergeNode in="SourceGraphic"/><feMergeNode in="h"/></feMerge></filter>')


def hazed(body, color, amount):
    """Returns (defs, body) with the haze filter applied to the whole group."""
    fid = uid("hz")
    return haze_filter(fid, color, amount), f'<g filter="url(#{fid})">{body}</g>'


def cloud_cel(seed, base, shadow, w=256, h=96):
    """Angular flat cloud: stacked chamfered lumps, one flat underside
    shadow, thin background ink. Tinted and faded at runtime."""
    rnd = random.Random(seed)
    lumps = []
    x = 18
    while x < w - 30:
        lw = rnd.uniform(40, 70)
        lh = rnd.uniform(20, 38) * (1.0 - abs((x + lw / 2) / w - 0.5) * 0.9)
        lumps.append((x, lw, lh))
        x += lw * rnd.uniform(0.55, 0.8)
    base_y = h * 0.7
    top = [(10, base_y)]
    for lx, lw, lh in lumps:
        top += [(lx + lw * 0.12, base_y - lh * 0.7), (lx + lw * 0.32, base_y - lh),
                (lx + lw * 0.68, base_y - lh), (lx + lw * 0.88, base_y - lh * 0.7)]
    top.append((w - 10, base_y))
    under = [(w - 10, base_y), (w - 30, base_y + 12), (w * 0.6, base_y + 16), (w * 0.3, base_y + 14), (28, base_y + 10)]
    outline = top + under
    shade = [(10, base_y - 2), (w - 10, base_y - 2)] + under[1:]
    clip = uid("cc")
    defs = f'<clipPath id="{clip}"><polygon points="{pts(outline)}"/></clipPath>'
    body = (f'<polygon points="{pts(outline)}" fill="{base}"/>'
            f'<g clip-path="url(#{clip})"><polygon points="{pts(shade)}" fill="{shadow}"/></g>'
            f'<polygon points="{pts(outline)}" fill="none" {ink_attr(2)}/>')
    return doc(w, h, body, defs)


def haze_band(color, w=512, h=96):
    """A thin horizontal band of air (allowed soft: it is sky, not a form)."""
    defs = lin("hb", [(0, color, 0), (0.35, color, 0.9), (0.65, color, 0.9), (1, color, 0)]) + \
        lin("hx", [(0, "#fff", 0.25), (0.2, "#fff", 1), (0.8, "#fff", 1), (1, "#fff", 0.25)], 0, 0, 1, 0) + \
        f'<mask id="m" maskUnits="userSpaceOnUse" x="0" y="0" width="{w}" height="{h}">' \
        f'<rect width="{w}" height="{h}" fill="url(#hx)"/></mask>'
    return doc(w, h, f'<rect width="{w}" height="{h}" fill="url(#hb)" mask="url(#m)"/>', defs)


def scatter_peaks(rnd, W, H, pal, count, wmin, wmax, cap, ink, avoid=None, rim=None, cracks=None):
    """Small free-standing peaks scattered over a tile (seen from high
    above they read as a distant massif field). `avoid(x, y)` -> bool
    skips spots (e.g. the river). Wrapped for a seamless tile."""
    items = []
    tries = 0
    while len(items) < count and tries < count * 20:
        tries += 1
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        w = rnd.uniform(wmin, wmax)
        if avoid and avoid(x, y, w):
            continue
        items.append((y, x, w, rnd.uniform(0.55, 0.85) * w, rnd.random()))
    out = []
    for y, x, w, h, seed in items:
        for dy in (-H, 0, H):
            out.append((y + dy, x, w, h, seed))
    out.sort(key=lambda t: t[0])
    defs, body = [], []
    for y, x, w, h, seed in out:
        if y - h > H + 10 or y + 10 < 0:
            continue
        d, b = peak(random.Random(seed), x, y, w, h, pal, cap, rim, cracks, None, ink=ink)
        defs.append(d)
        body.append(b)
    return "".join(defs), "".join(body)


def haze_doc(svg, color, amount):
    """Applies `hazed` to a finished doc() string (whole body)."""
    head, rest = svg.split("<defs>", 1)
    defs, rest = rest.split("</defs>", 1)
    body = rest[: rest.rindex("</svg>")]
    hd, hb = hazed(body, color, amount)
    return f"{head}<defs>{defs}{hd}</defs>{hb}</svg>"
