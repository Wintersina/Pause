"""App (launcher / store) icon generator.

    python3 make_icon.py                    # every candidate -> ../<Ship>/
    python3 make_icon.py NeonComet          # one candidate
    python3 make_icon.py --preview <dir>    # + comparison sheets in <dir>

Each candidate is one roster hull (in one of its skins), flying up out of
the frame on its own engine exhaust through a big neon "pause" sign -- the
two bars are frozen-time light streaks -- on a deep-space pixel backdrop.

The ship and its exhaust are the game's own art, never redrawn:
  hull     Resources/ShipArt/Hulls/<Key>.png (stock) or Skins/<Key>_<Skin>.bytes,
           idle frame (state 0, column 0), scaled by an integer, nearest neighbour
  exhaust  Resources/ShipArt/Exhaust/exhaust_atlas.png, the ship's boost strip,
           mounted on its nozzles (Scripts/Ship/ShipNozzles.cs)
Sprite rects, nozzles and atlas strips are read from the generated tables in
Scripts/Ship/ShipHullArt.cs, ShipNozzles.cs and ShipExhaustStyle.cs.

Everything else (backdrop, bloom, stars, speed lines, pause bars, glow) is
drawn on a coarse pixel grid and scaled up nearest neighbour, so it reads as
neon pixel art next to the hull.

The design is composed on a 3072 px "viewport" (what a full-bleed square icon
shows). Android adaptive layers are 108 dp of which the launcher shows the
middle 72 dp, so they are composed with a 1/4 bleed on every side (4608 px)
and the ship + its glow must stay inside the 66 dp safe circle (asserted).

Outputs per candidate in ../<Ship>/:
  <Ship>_1024.png            master, opaque (also the default/standalone icon)
  <Ship>_ios_1024.png        iOS / App Store, RGB, no alpha channel
  <Ship>_adaptive_fg_432.png Android adaptive foreground (ship, exhaust, glow)
  <Ship>_adaptive_bg_432.png Android adaptive background (space + pause bars)
  <Ship>_legacy_192.png      Android legacy square (rounded square, alpha)
  <Ship>_round_192.png       Android legacy round (circle, alpha)
Editor/Tools/AppIconSetter.cs wires one candidate into Player Settings.
"""
import math
import os
import random
import re
import sys

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_ROOT = os.path.abspath(os.path.join(HERE, ".."))
ASSETS = os.path.abspath(os.path.join(HERE, "../../.."))
SHIPART = os.path.join(ASSETS, "Art/Resources/ShipArt")
SCRIPTS = os.path.join(ASSETS, "Scripts/Ship")

V = 3072          # design viewport (px)
G = 24            # backdrop pixel grid (px at V): 8 px at 1024
CELL = 256        # hull sheet cell
PZOOM = 1.5       # exhaust plume px per design unit (exhaust.py)
HEAD = 3.0        # plume flame head y (design units)
SAFE = 33.0 / 54.0  # adaptive: safe-circle radius / layer half-size (66dp of 108dp)


def hexc(h, a=255):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


# ------------------------------------------------------------- candidates --
# bars: the pause bars' neon; glow: the hull's halo; bloom: the backdrop glow
# behind the ship (a contrasting tone, so the hull never sits on its own hue);
# sky: backdrop top / bottom; streaks: speed-line colours; k: hull scale (max,
# reduced until it fits the adaptive safe circle); cy: hull centre (viewport);
# pause (optional): bar width, gap, height, centre y (viewport fractions);
# hull (optional): the roster hull, when the candidate is named otherwise.
CANDIDATES = {
    "NeonComet": dict(skin="Kaneda", k=9, cy=0.46, bars="#6EF2EE", glow="#FF5B45",
                      bloom="#3B2FB0", sky=("#05041A", "#160A3A"), streaks=("#6EF2EE", "#FF3FA4"),
                      title="Neon Comet / Kaneda"),
    "SolarFang": dict(skin=None, k=9, cy=0.46, bars="#6EF2EE", glow="#FFB43C",
                      bloom="#0F6A7A", sky=("#030A18", "#0B1E3A"), streaks=("#6EF2EE", "#FFB43C"),
                      title="Solar Fang"),
    "Lightning": dict(skin=None, k=9, cy=0.46, bars="#FF3FA4", glow="#FFF6A8",
                      bloom="#5A22B0", sky=("#0A0420", "#22093F"), streaks=("#FF3FA4", "#6EF2EE"),
                      title="Lightning"),
    "GoldWarden": dict(skin="Regent", k=9, cy=0.47, bars="#FF3FA4", glow="#FFD36A",
                       bloom="#4A1E9A", sky=("#07031C", "#1A0838"), streaks=("#FF3FA4", "#6EF2EE"),
                       title="Gold Warden / Regent"),
    # the flagship in its top skin: gold + navy hull, magenta halo to lift the
    # navy half off the violet night, cyan bars (the canopy's colour) set wide
    # so the pause sign frames the broad hull instead of hiding behind it
    "GoldWarden": dict(skin="Regent", k=10, cy=0.53, pause=(0.09, 0.44, 0.68, 0.48),
                       bars="#6EF2EE", glow="#FF3FA4", bloom="#5A1E9A", sky=("#08031C", "#1E0838"),
                       streaks=("#6EF2EE", "#FFD36A"), title="Gold Warden / Regent"),
    "Ninja": dict(skin=None, k=9, cy=0.46, bars="#FFB43C", glow="#6EF2EE",
                  bloom="#8A1A78", sky=("#08041A", "#1E0832"), streaks=("#FFB43C", "#FF3FA4"),
                  title="Ninja"),
}
RECOMMENDED = "GoldWarden"


# ------------------------------------------------------- generated tables --
def read(path):
    with open(path) as fh:
        return fh.read()


def hull_rects():
    src = read(os.path.join(SCRIPTS, "ShipHullArt.cs"))
    out = {}
    for m in re.finditer(r"/\*\s*(\d+)\s+(\w+)\s*\*/\s*new HullRect\((\d+), (\d+), (\d+), (\d+),", src):
        out[m.group(2)] = dict(id=int(m.group(1)), x=int(m.group(3)), y=int(m.group(4)),
                               w=int(m.group(5)), h=int(m.group(6)))
    return out


def nozzles():
    src = read(os.path.join(SCRIPTS, "ShipNozzles.cs"))
    out = {}
    for m in re.finditer(r"/\*\s*\d+\s+(\w+)\s+\d+x\d+\s*\*/\s*new\[\] \{(.*?)\},", src):
        lst = []
        for n in re.finditer(r"N\(([\d.]+)f, ([\d.]+)f(?:, (Twin|[\d.]+f))?\)", m.group(2)):
            s = n.group(3)
            scale = 1.0 if s is None else (0.72 if s == "Twin" else float(s.rstrip("f")))
            lst.append((float(n.group(1)), float(n.group(2)), scale))
        out[m.group(1)] = lst
    return out


def strips():
    src = read(os.path.join(SCRIPTS, "ShipExhaustStyle.cs"))
    out = {}
    for m in re.finditer(r"new Strip\((\d+), ExhaustLayer\.(\w+), (\d+), (\d+), (\d+), (\d+), (\d+), (\d+)\)", src):
        sid, layer = int(m.group(1)), m.group(2)
        out[(sid, layer)] = tuple(int(m.group(i)) for i in range(3, 9))
    return out


RECTS, NOZZLES, STRIPS = hull_rects(), nozzles(), strips()
ATLAS = Image.open(os.path.join(SHIPART, "Exhaust/exhaust_atlas.png")).convert("RGBA")


def hull_cell(key, skin):
    if skin:
        sheet = Image.open(os.path.join(SHIPART, "Skins", f"{key}_{skin}.bytes"))
    else:
        sheet = Image.open(os.path.join(SHIPART, "Hulls", f"{key}.png"))
    return sheet.convert("RGBA").crop((0, 0, CELL, CELL))


def strip_frames(key, layer):
    x, y, w, h, n, stride = STRIPS[(RECTS[key]["id"], layer)]
    top = ATLAS.height - y - h
    return [ATLAS.crop((x + i * stride, top, x + i * stride + w, top + h)) for i in range(n)]


def longest(frames):
    """The frame whose flame reaches furthest (the most dramatic burst)."""
    return max(frames, key=lambda f: (f.getchannel("A").getbbox() or (0, 0, 0, 0))[3])


# ------------------------------------------------------------- drawing --
def nn(im, k):
    return im.resize((im.width * k, im.height * k), Image.NEAREST)


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))


class Canvas:
    """A layer of the design: viewport V plus a bleed on every side."""

    def __init__(self, bleed):
        self.o = int(V * bleed)          # viewport origin inside the canvas
        self.size = V + 2 * self.o
        self.grid = self.size // G

    def vx(self, fx):                    # viewport fraction -> canvas px
        return self.o + fx * V


def backdrop(cv, c, seed):
    """Sky gradient, stepped bloom behind the ship, speed lines and stars,
    on the pixel grid."""
    n = cv.grid
    top, bot = hexc(c["sky"][0]), hexc(c["sky"][1])
    bloom = np.array(hexc(c["bloom"])[:3], float)
    ys, xs = np.mgrid[0:n, 0:n].astype(float) + 0.5
    # viewport fractions of each grid cell
    fy = (ys * G - cv.o) / V
    fx = (xs * G - cv.o) / V
    t = np.clip(fy, 0, 1)[..., None]
    rgb = np.array(top[:3], float) * (1 - t) + np.array(bot[:3], float) * t
    d = np.hypot(fx - 0.5, (fy - c["cy"]) * 0.9)
    b = np.clip(1 - d / 0.62, 0, 1) ** 1.6
    b = np.floor(b * 6) / 6                       # posterised bloom rings
    rgb = rgb * (1 - b[..., None] * 0.85) + bloom * b[..., None] * 0.85 + bloom * 0.0
    # darker corners (vignette), stepped
    v = np.clip((np.hypot(fx - 0.5, fy - 0.5) - 0.45) / 0.45, 0, 1)
    v = np.floor(v * 4) / 4
    rgb = rgb * (1 - 0.55 * v[..., None])
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB").convert("RGBA")

    rnd = random.Random(seed)
    px = img.load()

    def plot(x, y, col, a):
        if 0 <= x < n and 0 <= y < n:
            r, g, bb, _ = px[x, y]
            px[x, y] = (int(r + (col[0] - r) * a), int(g + (col[1] - g) * a), int(bb + (col[2] - bb) * a), 255)

    # speed lines: vertical streaks falling past the ship, kept off its body
    cols = [hexc(s) for s in c["streaks"]]
    for i in range(30):
        x = rnd.randrange(n)
        fxx = (x * G - cv.o) / V
        if abs(fxx - 0.5) < 0.17:
            continue
        L = rnd.randint(5, 22)
        y0 = rnd.randrange(-L, n)
        col = cols[i % len(cols)] if rnd.random() < 0.55 else (244, 234, 212, 255)
        a0 = rnd.uniform(0.25, 0.6)
        for j in range(L):
            plot(x, y0 + j, col, a0 * (1 - j / L) ** 0.7)  # bright head, fading tail (ship goes up)
    # stars: specks and a few 4-point twinkles
    for _ in range(90):
        x, y = rnd.randrange(n), rnd.randrange(n)
        plot(x, y, (244, 234, 212), rnd.uniform(0.3, 0.9))
    for _ in range(9):
        x, y = rnd.randrange(n), rnd.randrange(n)
        if math.hypot((x * G - cv.o) / V - 0.5, (y * G - cv.o) / V - c["cy"]) < 0.3:
            continue
        col = cols[rnd.randrange(len(cols))]
        plot(x, y, (255, 255, 255), 1.0)
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            plot(x + dx, y + dy, col, 0.85)
        for dx, dy in ((2, 0), (-2, 0), (0, 2), (0, -2)):
            plot(x + dx, y + dy, col, 0.35)
    return nn(img, G)


def pause_bars(cv, c):
    """The pause sign: two neon bars (frozen-time light streaks), pixel grid."""
    n = cv.grid
    col = hexc(c["bars"])
    core = lerp(col, (255, 255, 255, 255), 0.65)
    shade = lerp(col, (0, 0, 0, 255), 0.45)
    lay = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    # bar width, gap, height, centre y (viewport fractions)
    bw, gap, bh, cyb = c.get("pause", (0.125, 0.11, 0.70, 0.5))
    g = lambda f: int(round(cv.o / G + f * V / G))
    rects = []
    for side in (-1, 1):
        cx = 0.5 + side * (gap / 2 + bw / 2)
        rects.append((g(cx - bw / 2), g(cyb - bh / 2), g(cx + bw / 2), g(cyb + bh / 2)))
    # stepped glow
    mask = Image.new("L", (n, n), 0)
    md = ImageDraw.Draw(mask)
    for r in rects:
        md.rectangle((r[0], r[1], r[2] - 1, r[3] - 1), fill=255)
    for grow, a in ((5, 40), (3, 70), (1, 120)):
        m = mask.filter(ImageFilter.MaxFilter(2 * grow + 1))
        halo = Image.new("RGBA", (n, n), col[:3] + (0,))
        halo.putalpha(m.point(lambda v, a=a: a if v else 0))
        lay = Image.alpha_composite(lay, halo)
    d = ImageDraw.Draw(lay)
    t = c.get("tube", 3)                                                  # tube width (cells)
    inner = col[:3] + (60,)
    for x0, y0, x1, y1 in rects:
        x1 -= 1
        y1 -= 1
        # a neon sign tube: bright outline, light core line, dim glass inside
        d.rectangle((x0, y0, x1, y1), fill=col)
        d.rectangle((x0 + t, y0 + t, x1 - t, y1 - t), fill=(0, 0, 0, 0))
        d.rectangle((x0 + t, y0 + t, x1 - t, y1 - t), fill=inner)
        d.rectangle((x0 + 1, y0 + 1, x1 - 1, y1 - 1), outline=core, width=1)
        for cx, cy in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):           # chamfered corners
            d.point((cx, cy), fill=col[:3] + (120,))
    return nn(lay, G)


def stepped_glow(alpha_full, cv, colour, steps=((1, 200), (2, 120), (4, 60), (6, 28))):
    """A blocky neon halo around a sprite: its silhouette on the pixel grid,
    grown in steps of falling alpha."""
    n = cv.grid
    a = alpha_full.resize((n, n), Image.BOX).point(lambda v: 255 if v > 40 else 0)
    out = Image.new("L", (n, n), 0)
    for grow, alpha in reversed(steps):
        m = a.filter(ImageFilter.MaxFilter(2 * grow + 1)).point(lambda v, al=alpha: al if v else 0)
        out = ImageChops.lighter(out, m)
    lay = Image.new("RGBA", (n, n), hexc(colour)[:3] + (0,))
    lay.putalpha(out)
    return nn(lay, G)


def ship_layers(cv, key, c, k):
    """(exhaust, glow, hull) RGBA layers of the canvas for hull scale k."""
    key = c.get("hull", key)          # a candidate may be named apart from its hull
    cell = hull_cell(key, c["skin"])
    r = RECTS[key]
    rx, ry_top = r["x"], CELL - r["y"] - r["h"]
    cx, cy = cv.vx(0.5), cv.vx(c["cy"])
    # hull rect centre -> (cx, cy)
    # snapped so the master's 3:1 box reduction keeps the hull's pixels whole
    q = V // 1024
    ox = int(round((cx - (rx + r["w"] / 2) * k) / q)) * q
    oy = int(round((cy - (ry_top + r["h"] / 2) * k) / q)) * q
    hull = Image.new("RGBA", (cv.size, cv.size), (0, 0, 0, 0))
    hull.alpha_composite(nn(cell, k), (ox, oy))

    exhaust = Image.new("RGBA", (cv.size, cv.size), (0, 0, 0, 0))
    unit = 2.0 * k / PZOOM            # exhaust px -> canvas px at the hull's scale
    if key in ("Ninja", "UFO"):
        ring = longest(strip_frames(key, "RingBoost"))
        wake = longest(strip_frames(key, "WakeBoost"))
        kr = max(1, int(round(r["w"] * k * 1.12 / ring.width)))
        kw = max(1, int(round(unit * 1.2)))
        wk = nn(wake, kw)
        hx, hy = ox + (rx + r["w"] / 2) * k, oy + (ry_top + r["h"] / 2) * k
        exhaust.alpha_composite(wk, (int(hx - wk.width / 2), int(hy + r["h"] * k * 0.18)))
        rg = nn(ring, kr)
        ringlay = Image.new("RGBA", (cv.size, cv.size), (0, 0, 0, 0))
        ringlay.alpha_composite(rg, (int(hx - rg.width / 2), int(hy - rg.height / 2)))
        exhaust = Image.alpha_composite(exhaust, ringlay)
    else:
        plume = longest(strip_frames(key, "PlumeBoost"))
        for nx, ny, s in NOZZLES[key]:
            kp = max(1, int(round(unit * s)))
            p = nn(plume, kp)
            px = ox + (rx + nx) * k - p.width / 2
            py = oy + (CELL - r["y"] - ny) * k - HEAD * PZOOM * kp
            exhaust.alpha_composite(p, (int(round(px)), int(round(py))))
    glow = stepped_glow(hull.getchannel("A"), cv, c["glow"])
    return exhaust, glow, hull


def foreground(cv, key, c, k):
    exhaust, glow, hull = ship_layers(cv, key, c, k)
    fg = Image.alpha_composite(glow, exhaust)
    # hot exhaust: add it once more, lightened, for a neon core
    fg = Image.alpha_composite(fg, exhaust)
    fg = Image.alpha_composite(fg, hull)
    return fg, hull, glow


def fits_safe(key, c, k):
    cv = Canvas(0.25)
    _, glow, hull = ship_layers(cv, key, c, k)
    a = np.array(ImageChops.lighter(hull.getchannel("A"), glow.getchannel("A").point(lambda v: 255 if v > 50 else 0)))
    ys, xs = np.nonzero(a > 20)
    half = cv.size / 2
    dist = np.hypot(xs + 0.5 - half, ys + 0.5 - half).max()
    return dist <= SAFE * half, dist / half


def scale_for(key, c):
    for k in range(c["k"], 0, -1):
        ok, frac = fits_safe(key, c, k)
        if ok:
            return k, frac
    raise SystemExit(f"{key}: ship does not fit the adaptive safe zone at any scale")


# ------------------------------------------------------------- outputs --
def rounded_mask(size, radius_frac, inset=0):
    m = Image.new("L", (size * 4, size * 4), 0)
    ImageDraw.Draw(m).rounded_rectangle((inset * 4, inset * 4, size * 4 - 1 - inset * 4, size * 4 - 1 - inset * 4),
                                        radius=int(size * 4 * radius_frac), fill=255)
    return m.resize((size, size), Image.LANCZOS)


def circle_mask(size, inset=0):
    m = Image.new("L", (size * 4, size * 4), 0)
    ImageDraw.Draw(m).ellipse((inset * 4, inset * 4, size * 4 - 1 - inset * 4, size * 4 - 1 - inset * 4), fill=255)
    return m.resize((size, size), Image.LANCZOS)


def squircle_mask(size, n=5.0):
    s = size * 4
    ys, xs = np.mgrid[0:s, 0:s].astype(float)
    u, v = (xs + 0.5) / s * 2 - 1, (ys + 0.5) / s * 2 - 1
    m = ((np.abs(u) ** n + np.abs(v) ** n) <= 1).astype(np.uint8) * 255
    return Image.fromarray(m, "L").resize((size, size), Image.LANCZOS)


def masked(img, mask):
    out = img.convert("RGBA")
    out.putalpha(ImageChops.multiply(out.getchannel("A"), mask))
    return out


def build(key):
    c = CANDIDATES[key]
    k, frac = scale_for(key, c)
    seed = sum(map(ord, key))

    # master: full-bleed square
    cv = Canvas(0.0)
    bg = Image.alpha_composite(backdrop(cv, c, seed), pause_bars(cv, c))
    fg, _, _ = foreground(cv, key, c, k)
    master = Image.alpha_composite(bg, fg).convert("RGB").reduce(V // 1024)

    # adaptive layers: 108 dp with the 72 dp viewport in the middle
    ca = Canvas(0.25)
    abg = Image.alpha_composite(backdrop(ca, c, seed), pause_bars(ca, c)).convert("RGB")
    afg, _, _ = foreground(ca, key, c, k)
    abg = abg.resize((432, 432), Image.LANCZOS)
    afg = afg.resize((432, 432), Image.LANCZOS)

    out = os.path.join(OUT_ROOT, key)
    os.makedirs(out, exist_ok=True)
    files = {
        f"{key}_1024.png": master,
        f"{key}_ios_1024.png": master.convert("RGB"),
        f"{key}_adaptive_fg_432.png": afg,
        f"{key}_adaptive_bg_432.png": abg,
        f"{key}_legacy_192.png": masked(master.resize((192, 192), Image.LANCZOS), rounded_mask(192, 0.18, 8)),
        f"{key}_round_192.png": masked(master.resize((192, 192), Image.LANCZOS), circle_mask(192, 8)),
    }
    for name, im in files.items():
        im.save(os.path.join(out, name), optimize=True)
    print(f"{key}: hull x{k}, ship+glow reach {frac:.3f} of the adaptive half-size (safe {SAFE:.3f}) -> {out}")
    return master, afg, abg


# ------------------------------------------------------------- previews --
def font(size):
    try:
        return ImageFont.load_default(size=size)
    except TypeError:
        return ImageFont.load_default()


def adaptive_composite(afg, abg, size, mask):
    """What a launcher shows: the middle 72 dp of the two layers, masked."""
    full = Image.alpha_composite(abg.convert("RGBA"), afg)
    crop = full.crop((72, 72, 360, 360)).resize((size, size), Image.LANCZOS)
    return masked(crop, mask)


PLACEHOLDERS = [("#2F80ED", "o"), ("#27AE60", "="), ("#F2F2F2", "#"), ("#EB5757", ">"),
                ("#F2994A", "*"), ("#9B51E0", "@"), ("#1C1C1E", "+"), ("#56CCF2", "~"),
                ("#FFD43B", "!"), ("#34495E", "%"), ("#E91E63", "&")]


def placeholder(size, colour, glyph, mask):
    im = Image.new("RGBA", (size, size), hexc(colour))
    d = ImageDraw.Draw(im)
    fg = (40, 40, 40, 255) if colour in ("#F2F2F2", "#FFD43B", "#56CCF2") else (255, 255, 255, 255)
    s = size // 3
    if glyph in "o@":
        d.ellipse((s, s, size - s, size - s), outline=fg, width=max(2, size // 14))
    elif glyph in "=%":
        for i in range(3):
            y = s + i * s // 2
            d.rectangle((s, y, size - s, y + max(2, size // 16)), fill=fg)
    elif glyph in "#&":
        d.rectangle((s, s, size - s, size - s), outline=fg, width=max(2, size // 14))
    elif glyph == ">":
        d.polygon([(s, s), (size - s, size // 2), (s, size - s)], fill=fg)
    else:
        d.ellipse((size // 2 - s // 2, size // 2 - s // 2, size // 2 + s // 2, size // 2 + s // 2), fill=fg)
    return masked(im, mask)


def homescreen(icons, dark, size=128):
    cols, rows, pad = 4, 4, 44
    w = cols * (size + pad) + pad
    h = rows * (size + pad + 30) + pad + 60
    top, bot = ((22, 24, 40), (8, 8, 16)) if dark else ((236, 232, 245), (200, 214, 232))
    img = Image.new("RGBA", (w, h))
    d = ImageDraw.Draw(img)
    for y in range(h):
        d.line([(0, y), (w, y)], fill=lerp(top + (255,), bot + (255,), y / h))
    d.text((pad, 18), "9:41", fill=(255, 255, 255) if dark else (20, 20, 20), font=font(26))
    mask = squircle_mask(size)
    rnd = random.Random(7 if dark else 11)
    slots = list(range(cols * rows))
    ph = PLACEHOLDERS[:]
    rnd.shuffle(ph)
    icon_slots = {5: 0, 10: 1, 3: 2, 12: 3}
    for slot in slots:
        x = pad + (slot % cols) * (size + pad)
        y = 60 + pad + (slot // cols) * (size + pad + 30)
        if slot in icon_slots and icon_slots[slot] < len(icons):
            name, ic = icons[icon_slots[slot]]
            img.alpha_composite(masked(ic.resize((size, size), Image.LANCZOS), mask), (x, y))
            label = "Pause"
        else:
            colour, glyph = ph[slot % len(ph)]
            img.alpha_composite(placeholder(size, colour, glyph, mask), (x, y))
            label = "App"
        f = font(20)
        tw = d.textlength(label, font=f)
        d.text((x + (size - tw) / 2, y + size + 6), label, fill=(240, 240, 240) if dark else (30, 30, 30), font=f)
    return img


def previews(results, out):
    os.makedirs(out, exist_ok=True)
    keys = list(results)
    f = font(34)
    # sizes sheet: 1024 | 180 | 96 | 48 | adaptive in circle / squircle / rounded square
    W = 1024 + 40 + 180 + 30 + 96 + 30 + 48 + 60 + 3 * (220 + 30) + 40
    rowh = 1024 + 80
    sheet = Image.new("RGBA", (W, rowh * len(keys)), (30, 30, 36, 255))
    d = ImageDraw.Draw(sheet)
    for i, key in enumerate(keys):
        master, afg, abg = results[key]
        y0 = i * rowh + 60
        d.text((20, y0 - 48), f"{CANDIDATES[key]['title']}" + ("   (RECOMMENDED)" if key == RECOMMENDED else ""),
               fill=(255, 255, 255), font=f)
        sheet.alpha_composite(master.convert("RGBA"), (20, y0))
        x = 20 + 1024 + 40
        for s in (180, 96, 48):
            sheet.alpha_composite(masked(master.resize((s, s), Image.LANCZOS), squircle_mask(s)), (x, y0))
            d.text((x, y0 + s + 8), f"{s}px", fill=(200, 200, 200), font=font(20))
            x += s + 30
        x += 30
        for mname, m in (("circle", circle_mask(220)), ("squircle", squircle_mask(220)),
                         ("rounded sq", rounded_mask(220, 0.2))):
            sheet.alpha_composite(adaptive_composite(afg, abg, 220, m), (x, y0))
            d.text((x, y0 + 228), f"adaptive / {mname}", fill=(200, 200, 200), font=font(20))
            x += 250
        # adaptive 48 in circle, and the safe circle overlay on the raw layers
        x = 20 + 1024 + 40
        y1 = y0 + 300
        lay = Image.alpha_composite(abg.convert("RGBA"), afg)
        ov = lay.copy()
        od = ImageDraw.Draw(ov)
        od.rectangle((72, 72, 359, 359), outline=(255, 255, 255, 200), width=2)
        od.ellipse((216 - 132, 216 - 132, 216 + 132, 216 + 132), outline=(0, 255, 120, 255), width=2)
        sheet.alpha_composite(ov, (x, y1))
        d.text((x, y1 + 440), "108dp layers: white = 72dp view", fill=(200, 200, 200), font=font(20))
        d.text((x, y1 + 466), "green = 66dp safe circle", fill=(200, 200, 200), font=font(20))
        sheet.alpha_composite(afg, (x + 460, y1))
        d.text((x + 460, y1 + 440), "foreground layer", fill=(200, 200, 200), font=font(20))
        for j, s in enumerate((96, 48)):
            sheet.alpha_composite(adaptive_composite(afg, abg, s, circle_mask(s)), (x + 920 + j * 120, y1))
    sheet.convert("RGB").save(os.path.join(out, "compare_sizes.png"))

    icons = [(k, results[k][0]) for k in keys]
    dark = homescreen(icons, True)
    light = homescreen(icons, False)
    small_dark = homescreen(icons, True, 64)
    small_light = homescreen(icons, False, 64)
    hs = Image.new("RGBA", (dark.width * 2 + 60 + small_dark.width * 2 + 60, max(dark.height, small_dark.height * 2) + 20),
                   (30, 30, 36, 255))
    hs.alpha_composite(dark, (0, 0))
    hs.alpha_composite(light, (dark.width + 30, 0))
    hs.alpha_composite(small_dark, (dark.width * 2 + 90, 0))
    hs.alpha_composite(small_light, (dark.width * 2 + 90 + small_dark.width + 20, 0))
    hs.convert("RGB").save(os.path.join(out, "compare_homescreen.png"))

    # overview: one row per candidate at 256 / 96 / 48 + both home screens, small
    ov = Image.new("RGBA", (256 * len(keys) + 20 * (len(keys) + 1), 256 + 220), (30, 30, 36, 255))
    od = ImageDraw.Draw(ov)
    for i, key in enumerate(keys):
        x = 20 + i * 276
        m = results[key][0]
        ov.alpha_composite(masked(m.resize((256, 256), Image.LANCZOS), squircle_mask(256)), (x, 20))
        ov.alpha_composite(masked(m.resize((96, 96), Image.LANCZOS), squircle_mask(96)), (x, 300))
        ov.alpha_composite(masked(m.resize((48, 48), Image.LANCZOS), squircle_mask(48)), (x + 120, 300))
        ov.alpha_composite(adaptive_composite(*results[key][1:], 48, circle_mask(48)), (x + 190, 300))
        od.text((x, 420), key, fill=(255, 255, 255), font=font(22))
    ov.convert("RGB").save(os.path.join(out, "compare_overview.png"))
    print("previews ->", out)


def main(argv):
    preview = None
    if "--preview" in argv:
        i = argv.index("--preview")
        preview = argv[i + 1]
        argv = argv[:i] + argv[i + 2:]
    keys = argv or list(CANDIDATES)
    results = {k: build(k) for k in keys}
    if preview:
        previews(results, preview)


if __name__ == "__main__":
    main(sys.argv[1:])
