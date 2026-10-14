"""HD death strips (3 cells: flash, rupture, dispersal) for the HD elite rebuilds: build_death_strips.py's recipe at cell size S
(384 = 2 x 192). Every length of the old code that was a pixel count is multiplied by k = S / 192 (line widths, shard / disc sizes,
margins, the fracture noise), the lengths that were a share of the hull radius R are unchanged, rotations are bicubic instead of
nearest. The hull cell comes from the caller (the new HD hover cell), the strip is returned (build_frost_hd.py writes it).
"""
from __future__ import annotations
import math
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
import elite_hd as E

PINK = (255, 79, 216)
WHITE = (255, 250, 240)
WORLDS = {
    "frost":   ("Frost",   (200, 245, 255), (110, 190, 255), [(255, 255, 255), (150, 215, 255), (255, 79, 216)], "ice"),
}


def center_of(im: Image.Image):
    a = np.asarray(im)[:, :, 3] > 128
    ys, xs = np.nonzero(a)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    return int((x0 + x1) // 2), int((ys.mean() + (y0 + y1) / 2) // 2), (x0, y0, x1, y1)


def disc(d, c, r, fill):
    d.ellipse((c[0] - r, c[1] - r, c[0] + r, c[1] + r), fill=fill)


def spikes(d, c, r1, r2, n, col, width=1, phase=0.0, k=1.0):
    for i in range(n):
        a = phase + i * math.tau / n
        d.line((c[0] + r1 * math.cos(a), c[1] + r1 * math.sin(a), c[0] + r2 * math.cos(a), c[1] + r2 * math.sin(a)), fill=col, width=max(1, round(width * k)))


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


def build_one(key: str, base: Image.Image, world: str, S: int) -> Image.Image:
    k = S / 192.0
    _, hot, mid, accents, style = WORLDS[world]
    cx, cy, (x0, y0, x1, y1) = center_of(base)
    core = (cx, cy)
    rng = np.random.default_rng(sum(map(ord, key)))
    R = max(x1 - x0, y1 - y0) / 2
    frames = []

    arr = np.asarray(base)
    ys, xs = np.nonzero(arr[:, :, 3] > 128)
    nseed = int(np.clip(len(xs) / (420 * k * k), 9, 16))
    pick = rng.choice(len(xs), nseed, replace=False)
    seeds = np.stack([xs[pick], ys[pick]], 1).astype(np.float32)
    # a few extra seeds hugging the core so the centre fractures too
    lab = np.full((S, S), -1, np.int32)
    gy, gx = np.mgrid[0:S, 0:S]
    dist = np.stack([(gx - sx) ** 2 + (gy - sy) ** 2 + rng.normal(0, 18 * k * k, (S, S)) for sx, sy in seeds])
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
            img = E.rotate(img, ang, (px / k, py / k))
            if shrink != 1.0:
                sw = max(1, int(S * shrink)); img = img.resize((sw, sw), Image.NEAREST)
                full = Image.new("RGBA", (S, S)); full.alpha_composite(img, (int(px * (1 - shrink)) , int(py * (1 - shrink)))) if False else None
            bb = img.getchannel("A").getbbox()
            if bb:
                ox = max(8 * k - bb[0], min(S - 8 * k - bb[2], ox)); oy = max(8 * k - bb[1], min(S - 8 * k - bb[3], oy))
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
    spikes(gd, core, int(R * .14), int(R * .62), 8, (*WHITE, 235), 1, 0.0, k)
    spikes(gd, core, int(R * .14), int(R * .42), 8, (*hot, 220), 2, math.pi / 8, k)
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
    spikes(d, core, int(R * .32), int(R * .9), 10, (*hot, 230), 1, .1, k)
    spikes(d, core, int(R * .32), int(R * .66), 5, (*accents[0], 240), 2, .45, k)
    for _ in range(12):
        a = rng.random() * math.tau; rr = rng.uniform(R * .45, R * .95)
        shard(d, cx + rr * math.cos(a), cy + rr * math.sin(a), int(rng.integers(2, 5)) * k, (*accents[int(rng.integers(len(accents)))], 240), style, rng)
    frames.append(rup)

    # --- cell 2: dispersal. chunks flung wide and fading, vapour arcs, themed motes
    dis = Image.new("RGBA", (S, S)); d = ImageDraw.Draw(dis)
    for rad, al in ((int(R * .5), 110), (int(R * .75), 70), (int(R * 1.0), 38)):
        d.arc((cx - rad, cy - rad, cx + rad, cy + rad), 25, 155, fill=(*hot, al), width=round(2 * k))
        d.arc((cx - rad, cy - rad, cx + rad, cy + rad), 205, 335, fill=(*mid, al), width=round(2 * k))
    dis.alpha_composite(chunks(0.5, 55, 0.0, 0.62))
    d = ImageDraw.Draw(dis)
    for _ in range(26):
        a = rng.random() * math.tau; rr = rng.uniform(R * .3, R * 1.15)
        x = int(cx + rr * math.cos(a)); y = int(cy + rr * math.sin(a))
        col = accents[int(rng.integers(len(accents)))]
        shard(d, x, y, int(rng.integers(1, 4)) * k, (*col, int(rng.integers(130, 235))), style, rng)
    disc(d, core, int(4 * k), (*PINK, 150)); disc(d, core, int(2 * k), (*WHITE, 230))
    frames.append(dis)

    out = Image.new("RGBA", (S * 3, S))
    for i, fr in enumerate(frames):
        out.alpha_composite(fr, (i * S, 0))
    # keep a 6 px+ clear margin in every cell
    arr = np.asarray(out).copy()
    for i in range(3):
        c = arr[:, i * S:(i + 1) * S]
        m = int(7 * k); c[:m, :, 3] = 0; c[-m:, :, 3] = 0; c[:, :m, 3] = 0; c[:, -m:, 3] = 0
    out = Image.fromarray(arr, "RGBA")
    return out
