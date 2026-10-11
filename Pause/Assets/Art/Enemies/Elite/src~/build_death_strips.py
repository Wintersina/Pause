"""Build <key>_death.png (3 x 192 px cells: flash, rupture, dispersal) for the elites
that lack one (Ember, Frost, Space, Verdant Resin Warden).

Every strip starts from the ship's REAL last flight cell (cell 3 of its flight strip),
so cell 0 sits on the live hull pixel-for-pixel. Rupture halves and debris are cut
from that same hull; effects are stepped pixel art in the world's own palette with a
hostile-pink core accent. Nearest-neighbour everywhere, no red, 6 px+ margin.

Run from the repo root:  python3 Pause/Assets/Art/Enemies/Elite/src~/build_death_strips.py [key ...]
"""
from __future__ import annotations
import math, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parents[6]
RES = REPO / "Pause/Assets/Art/Resources/Elites"
ART = REPO / "Pause/Assets/Art/Enemies/Elite"
S = 192
PINK = (255, 79, 216)
WHITE = (255, 250, 240)

# world: (folder, hot glow, mid glow, accent list, fragment style)
WORLDS = {
    "space":   ("Space",   (150, 245, 255), (120, 80, 255),  [(255, 79, 216), (90, 220, 255), (200, 120, 255)], "plasma"),
    "frost":   ("Frost",   (200, 245, 255), (110, 190, 255), [(255, 255, 255), (150, 215, 255), (255, 79, 216)], "ice"),
    "ember":   ("Ember",   (255, 220, 120), (255, 140, 40),  [(255, 190, 70), (255, 120, 30), (255, 79, 216)], "slag"),
    "verdant": ("Verdant", (190, 255, 150), (90, 210, 110),  [(220, 255, 170), (255, 200, 90), (255, 79, 216)], "leaf"),
}
SHIPS = {
    "space_elite_eventide_bastion": "space", "space_elite_orbit_reaver": "space",
    "space_elite_rift_lancer": "space", "space_elite_singularity_hauler": "space",
    "frost_elite_cryo_siren": "frost", "frost_elite_floe_harrower": "frost",
    "frost_elite_glacier_tender": "frost", "frost_elite_rimebreaker": "frost",
    "frost_elite_whiteout_sentinel": "frost",
    "ember_elite_ash_wraith": "ember", "ember_elite_brass_vulture": "ember",
    "ember_elite_cauterizer": "ember", "ember_elite_coalrunner": "ember",
    "ember_elite_kilnback": "ember", "ember_elite_sunstoke": "ember",
    "verdant_elite_resin_warden": "verdant",
}
ORDER = ["ember_elite_kilnback", "space_elite_eventide_bastion", "space_elite_singularity_hauler"]


def flight_cell(key: str, world: str) -> Image.Image:
    strip = Image.open(RES / WORLDS[world][0] / f"{key}.png").convert("RGBA")
    return strip.crop((3 * S, 0, 4 * S, S))


def center_of(im: Image.Image):
    a = np.asarray(im)[:, :, 3] > 128
    ys, xs = np.nonzero(a)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    # core = centre of the hull's bounding box, nudged to the mass centre vertically
    return int((x0 + x1) // 2), int((ys.mean() + (y0 + y1) / 2) // 2), (x0, y0, x1, y1)


def disc(d, c, r, fill):
    d.ellipse((c[0] - r, c[1] - r, c[0] + r, c[1] + r), fill=fill)


def spikes(d, c, r1, r2, n, col, width=1, phase=0.0):
    for i in range(n):
        a = phase + i * math.tau / n
        d.line((c[0] + r1 * math.cos(a), c[1] + r1 * math.sin(a), c[0] + r2 * math.cos(a), c[1] + r2 * math.sin(a)), fill=col, width=width)


def whiten(im, amount, tint):
    a = np.asarray(im).astype(np.float32).copy()
    solid = a[:, :, 3] > 0
    tgt = np.array(tint, np.float32)
    a[solid, :3] = a[solid, :3] * (1 - amount) + tgt * amount
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def shard(d, x, y, size, col, style, rng):
    a = rng.random() * math.tau
    if style == "ice":      # angular crystal: elongated diamond
        ux, uy = math.cos(a), math.sin(a)
        d.polygon(((x + ux * size, y + uy * size), (x - uy * size * .4, y + ux * size * .4),
                   (x - ux * size * .8, y - uy * size * .8), (x + uy * size * .4, y - ux * size * .4)), fill=col)
    elif style == "leaf":   # leaf: pointed ellipse-ish
        ux, uy = math.cos(a), math.sin(a)
        d.polygon(((x + ux * size, y + uy * size), (x - uy * size * .5, y + ux * size * .5),
                   (x - ux * size, y - uy * size), (x + uy * size * .5, y - ux * size * .5)), fill=col)
    elif style == "slag":   # molten blob
        d.ellipse((x - size * .5, y - size * .5, x + size * .5, y + size * .5), fill=col)
    else:                   # plasma / hull plate
        d.polygon(((x, y), (x + size, y + size * .3), (x + size * .3, y + size)), fill=col)


def build(key: str):
    world = SHIPS[key]
    _, hot, mid, accents, style = WORLDS[world]
    base = flight_cell(key, world)
    cx, cy, (x0, y0, x1, y1) = center_of(base)
    core = (cx, cy)
    rng = np.random.default_rng(sum(map(ord, key)))
    R = max(x1 - x0, y1 - y0) / 2
    frames = []

    arr = np.asarray(base)
    ys, xs = np.nonzero(arr[:, :, 3] > 128)
    nseed = int(np.clip(len(xs) / 420, 9, 16))
    pick = rng.choice(len(xs), nseed, replace=False)
    seeds = np.stack([xs[pick], ys[pick]], 1).astype(np.float32)
    # a few extra seeds hugging the core so the centre fractures too
    lab = np.full((S, S), -1, np.int32)
    gy, gx = np.mgrid[0:S, 0:S]
    dist = np.stack([(gx - sx) ** 2 + (gy - sy) ** 2 + rng.normal(0, 18, (S, S)) for sx, sy in seeds])
    lab[:] = dist.argmin(0)
    lab[arr[:, :, 3] <= 128] = -1

    def chunks(push, rot, shade, alpha, shrink=1.0):
        layer = Image.new("RGBA", (S, S))
        order = sorted(range(nseed), key=lambda i: -np.hypot(seeds[i][0] - cx, seeds[i][1] - cy))
        for i in order:
            m = lab == i
            if not m.any():
                continue
            part = np.zeros_like(arr); part[m] = arr[m]
            px, py = seeds[i]
            vx, vy = px - cx, py - cy
            n = math.hypot(vx, vy) or 1.0
            mv = push * (R * .35 + n)
            ox, oy = vx / n * mv, vy / n * mv
            img = Image.fromarray(part, "RGBA")
            ang = float(rng.uniform(-rot, rot))
            img = img.rotate(ang, resample=Image.NEAREST, center=(px, py))
            if shrink != 1.0:
                sw = max(1, int(S * shrink)); img = img.resize((sw, sw), Image.NEAREST)
                full = Image.new("RGBA", (S, S)); full.alpha_composite(img, (int(px * (1 - shrink)) , int(py * (1 - shrink)))) if False else None
            bb = img.getchannel("A").getbbox()
            if bb:
                ox = max(8 - bb[0], min(S - 8 - bb[2], ox)); oy = max(8 - bb[1], min(S - 8 - bb[3], oy))
            tmp = Image.new("RGBA", (S, S)); tmp.alpha_composite(img, (int(round(ox)), int(round(oy))))
            tint = whiten(tmp, shade, hot)
            ta = np.asarray(tint).copy(); ta[:, :, 3] = (ta[:, :, 3] * alpha).astype(np.uint8)
            layer.alpha_composite(Image.fromarray(ta, "RGBA"))
        return layer

    # --- cell 0: intact hull on the live pixels, white-hot flash
    f = whiten(base, 0.3, WHITE)
    g = Image.new("RGBA", (S, S)); gd = ImageDraw.Draw(g)
    disc(gd, core, int(R * .30), (*hot, 60))
    disc(gd, core, int(R * .20), (*hot, 110))
    disc(gd, core, int(R * .11), (*PINK, 170))
    disc(gd, core, int(R * .06), (*WHITE, 255))
    spikes(gd, core, int(R * .14), int(R * .62), 8, (*WHITE, 235), 1)
    spikes(gd, core, int(R * .14), int(R * .42), 8, (*hot, 220), 2, math.pi / 8)
    f.alpha_composite(g)
    frames.append(f)

    # --- cell 1: rupture. bloom behind, hull fractured into chunks that start to part, core flare on top
    rup = Image.new("RGBA", (S, S)); d = ImageDraw.Draw(rup)
    disc(d, core, int(R * .62), (*mid, 70))
    disc(d, core, int(R * .46), (*hot, 120))
    disc(d, core, int(R * .30), (*hot, 235))
    disc(d, core, int(R * .17), (*PINK, 255))
    rup.alpha_composite(chunks(0.16, 14, 0.12, 1.0))
    d = ImageDraw.Draw(rup)
    disc(d, core, int(R * .09), (*WHITE, 255))
    spikes(d, core, int(R * .32), int(R * .9), 10, (*hot, 230), 1, .1)
    spikes(d, core, int(R * .32), int(R * .66), 5, (*accents[0], 240), 2, .45)
    for _ in range(12):
        a = rng.random() * math.tau; rr = rng.uniform(R * .45, R * .95)
        shard(d, cx + rr * math.cos(a), cy + rr * math.sin(a), int(rng.integers(2, 5)), (*accents[int(rng.integers(len(accents)))], 240), style, rng)
    frames.append(rup)

    # --- cell 2: dispersal. chunks flung wide and fading, vapour arcs, themed motes
    dis = Image.new("RGBA", (S, S)); d = ImageDraw.Draw(dis)
    for rad, al in ((int(R * .5), 110), (int(R * .75), 70), (int(R * 1.0), 38)):
        d.arc((cx - rad, cy - rad, cx + rad, cy + rad), 25, 155, fill=(*hot, al), width=2)
        d.arc((cx - rad, cy - rad, cx + rad, cy + rad), 205, 335, fill=(*mid, al), width=2)
    dis.alpha_composite(chunks(0.5, 55, 0.0, 0.62))
    d = ImageDraw.Draw(dis)
    for _ in range(26):
        a = rng.random() * math.tau; rr = rng.uniform(R * .3, R * 1.15)
        x = int(cx + rr * math.cos(a)); y = int(cy + rr * math.sin(a))
        col = accents[int(rng.integers(len(accents)))]
        shard(d, x, y, int(rng.integers(1, 4)), (*col, int(rng.integers(130, 235))), style, rng)
    disc(d, core, 4, (*PINK, 150)); disc(d, core, 2, (*WHITE, 230))
    frames.append(dis)

    out = Image.new("RGBA", (S * 3, S))
    for i, fr in enumerate(frames):
        out.alpha_composite(fr, (i * S, 0))
    # keep a 6 px+ clear margin in every cell
    arr = np.asarray(out).copy()
    for i in range(3):
        c = arr[:, i * S:(i + 1) * S]
        c[:7, :, 3] = 0; c[-7:, :, 3] = 0; c[:, :7, 3] = 0; c[:, -7:, 3] = 0
    out = Image.fromarray(arr, "RGBA")
    dest = RES / WORLDS[world][0] / f"{key}_death.png"
    out.save(dest, optimize=True)
    # keep a copy next to the authored art
    out.save(ART / WORLDS[world][0] / f"{key}_death.png", optimize=True)
    return out


if __name__ == "__main__":
    keys = sys.argv[1:] or ORDER + [k for k in SHIPS if k not in ORDER]
    for k in keys:
        build(k); print("built", k)
