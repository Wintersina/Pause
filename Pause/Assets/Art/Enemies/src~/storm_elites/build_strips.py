#!/usr/bin/env python3
"""Storm elites, ground-up redesign: build the 7-cell flight strips + 3-cell death strips from the Codex masters.

Masters (masters/<name>.png, Codex, nose up, flat #00FF40 key) are keyed, scaled into a 384 px cell and given drawn engine flames in the
ship's own flame colours. Layout is unchanged (EliteDef.cells flight layout):

  0 landed (engines off, hull dimmed)   1 grounded idle (pilot lights)   2 lift-off ignition (long jets)   3 hover (short jets)
  4 bank left   5 bank right (outer parts flex, anchored)   6 damaged (scorched, cracked, ragged jets)

  python3 build_strips.py [name ...]      writes Elite/Storm/<key>.png + <key>_death.png and the same into Resources/Elites/Storm
"""
import sys, random, math
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]                     # .../Art
SRC_DIR = ART / "Enemies/Elite/Storm"
RES = ART / "Resources/Elites/Storm"
CELL = 384
U = 4
sys.path.insert(0, str(ART / "Enemies/Elite/src~"))

# master-px nozzle (x, y, bell width); fx flame colours; glow = energy spots (master px, colour)
SHIPS = {
    "anvil_cloud": dict(h=280, w=330, flame=((225, 140, 35), (255, 215, 110), (255, 252, 230)), spark=(255, 232, 150),
                        noz=((470, 1165, 190), (775, 1165, 190)),
                        glow=((628, 75, 1), (232, 215, 0), (1020, 215, 0), (627, 530, 0))),
    "refinery_tender": dict(h=280, w=340, flame=((40, 110, 255), (95, 205, 255), (255, 255, 255)), spark=(150, 225, 255),
                            noz=((430, 975, 150), (680, 948, 92), (930, 975, 150)),
                            glow=((678, 222, 1), (678, 640, 1))),
    "rod_dancer": dict(h=280, w=330, flame=((20, 165, 205), (120, 240, 235), (255, 255, 255)), spark=(160, 255, 245),
                       noz=((440, 1255, 62), (540, 1275, 86), (640, 1255, 62)),
                       glow=((260, 120, 1), (820, 120, 1), (540, 640, 0))),
    "skyhook_harrier": dict(h=280, w=330, flame=((255, 105, 20), (255, 195, 70), (255, 248, 215)), spark=(255, 225, 150),
                            noz=((480, 1232, 105), (665, 1232, 105)),
                            glow=((575, 650, 0), (575, 700, 0), (463, 763, 0), (688, 763, 0))),
    "static_bastion": dict(h=280, w=330, flame=((60, 140, 235), (150, 235, 255), (255, 255, 255)), spark=(170, 240, 255),
                           noz=((350, 1100, 125), (620, 1150, 150), (885, 1100, 125), (455, 1100, 62), (775, 1100, 62)),
                           glow=((620, 450, 1), (240, 190, 0), (338, 160, 0), (900, 160, 0), (1000, 190, 0), (620, 75, 0))),
}


def key_out(path):
    im = np.array(Image.open(path).convert("RGB")).astype(float)
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    gn = g - np.maximum(r, b)
    a = np.clip(1 - (gn - 35) / 80, 0, 1)
    gs = np.where(a < 1, np.minimum(g, np.maximum(r, b)), g)
    return Image.fromarray(np.dstack([r, gs, b, a * 255]).astype(np.uint8), "RGBA")


LIFT = {"anvil_cloud": (.88, 1.0), "refinery_tender": (.80, 1.12), "rod_dancer": (.84, 1.05), "skyhook_harrier": (.88, 1.0), "static_bastion": (.76, 1.18)}


def lift(im, gamma, sat):
    """Readability without an outline: lift the value (gamma < 1 raises the darks) and saturation of the hull."""
    a = np.asarray(im.convert("RGBA")).copy()
    hsv = np.asarray(Image.fromarray(a[..., :3], "RGB").convert("HSV")).astype(float) / 255
    hsv[..., 2] = hsv[..., 2] ** gamma
    hsv[..., 1] = np.clip(hsv[..., 1] * sat, 0, 1)
    rgb = np.asarray(Image.fromarray((hsv * 255).astype(np.uint8), "HSV").convert("RGB"))
    a[..., :3] = rgb
    return Image.fromarray(a, "RGBA")


def load(name):
    cfg = SHIPS[name]
    art = lift(key_out(HERE / "masters" / f"{name}.png"), *LIFT[name])
    bb = art.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox()
    crop = art.crop(bb)
    s = min(cfg["w"] / crop.width, cfg["h"] / crop.height)
    hull = crop.resize((round(crop.width * s), round(crop.height * s)), Image.Resampling.LANCZOS)
    hull.putalpha(hull.getchannel("A").point(lambda v: 255 if v >= 128 else 0))
    return cfg, hull, bb, s


def flame(canvas, x, y, length, width, cols, rnd, ragged=0.0):
    d = ImageDraw.Draw(canvas)
    for layer, (col, lk, wk) in enumerate(zip(cols, (1.0, .72, .42), (1.0, .66, .34))):
        L = length * lk / U
        for row in range(int(L) + 1):
            t = row / max(1, L)
            hw = (width * wk / (2 * U)) * (1 - t ** 1.7) ** .9
            if layer == 0 and row > L * .6 and ragged:
                hw *= 1 - ragged * rnd.random()
            hw = max(hw, .5 if row < L else 0)
            x0 = int(round(x / U - hw)); x1 = int(round(x / U + hw - .01))
            d.line([(x0, int(y / U) + row), (max(x0, x1), int(y / U) + row)], fill=col + (255,))


def burn(noz, length, cols, seed, ragged=0.0):
    rnd = random.Random(seed)
    c = Image.new("RGBA", (CELL // U, CELL // U))
    for i, (x, y, w) in enumerate(noz):
        L = length * (1.0 if w > 70 else .8) * (1 + .12 * ((i + seed) % 2))
        flame(c, x, y, L, w * .78, cols, rnd, ragged)
    return c.resize((CELL, CELL), Image.Resampling.NEAREST)


def dots(pts, col, strength, core=(255, 255, 255)):
    """Energy spots: a stepped diamond glow (soft outer step, bright mid, white core), hard pixel steps."""
    im = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(im)
    for x, y, big in pts:
        r = 10 if big else 7
        for rr, al, c in ((r + 5, 55, col), (r, 130, col), (r - 4, 255, col), (max(2, r - 8), 255, core)):
            al = round(al * strength)
            if rr >= 2 and al > 0 and (c is not core or strength > .7):
                d.polygon(((x, y - rr), (x + rr, y), (x, y + rr), (x - rr, y)), fill=c + (al,))
    return im


def move_parts(base, hull_box, name, state):
    """Outer parts flex (cells 0, 2-5); the anchor stays put. hull_box = (ox, oy, w, h) of the hull in the cell."""
    if state not in (0, 2, 3, 4, 5):
        return base.copy()
    ox, oy, w, h = hull_box
    a = np.asarray(base)
    yy, xx = np.indices((CELL, CELL))
    u = (xx - ox) / w; v = (yy - oy) / h
    cx = ox + w / 2
    outer = np.clip((abs(xx - cx) / w - .08) / .32, 0, 1)
    if name == "rod_dancer":
        region = (v < .72) & (abs(u - .5) > .12)
    elif name == "skyhook_harrier":
        region = (v > .4) & (v < .95) & (abs(u - .5) > .12)
    elif name == "refinery_tender":
        region = (v > .25) & (v < .8) & (abs(u - .5) > .16)
    elif name == "anvil_cloud":
        region = (v < .5) & (abs(u - .5) > .16)
    else:
        region = (v > .15) & (v < .85) & (abs(u - .5) > .2)
    if state == 4:
        dy = np.where(xx < cx, -8.0, 6.0) * outer; dx = -4.0 * outer
    elif state == 5:
        dy = np.where(xx < cx, 6.0, -8.0) * outer; dx = 4.0 * outer
    else:
        s = 1 if state in (2, 3) else -1
        dy = 3.2 * s * outer; dx = np.sign(xx - cx) * 3.0 * s * outer
    sy = np.clip(np.rint(yy - dy * region).astype(int), 0, CELL - 1)
    sx = np.clip(np.rint(xx - dx * region).astype(int), 0, CELL - 1)
    warped = a[sy, sx].copy()
    if state in (4, 5):
        sign = -1 if state == 4 else 1
        shift = np.rint(sign * 3.0 * (oy + h / 2 - yy) / (h / 2)).astype(int)
        warped = warped[yy, np.clip(xx - shift, 0, CELL - 1)]
    out = Image.fromarray(warped, "RGBA")
    # a warp must not open holes inside the hull: alpha stays crisp
    out.putalpha(out.getchannel("A").point(lambda v: 255 if v >= 128 else 0))
    return out


def dim(im, k):
    a = np.asarray(im).astype(np.float32).copy()
    s = a[:, :, 3] > 0
    a[s, :3] *= k
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def scorch(im, spark, seed):
    """Damaged: darker hull, soot, one dark fissure with an energy seam, a few sparks."""
    rnd = random.Random(seed)
    a = np.asarray(dim(im, .8)).copy()
    solid = a[:, :, 3] > 0
    ys, xs = np.nonzero(solid)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    cx, cy = (x0 + x1) // 2, y0 + (y1 - y0) * 4 // 10
    im = Image.fromarray(a, "RGBA")
    d = ImageDraw.Draw(im)
    jag = [(cx - 26, cy - 40), (cx - 8, cy - 18), (cx - 22, cy + 2), (cx + 4, cy + 20), (cx - 6, cy + 46), (cx + 14, cy + 64)]
    d.line(jag, fill=(8, 9, 14, 255), width=9)
    d.line([(x + 1, y) for x, y in jag], fill=spark + (255,), width=3)
    d.line([(x + 1, y) for x, y in jag], fill=(255, 255, 255, 255), width=1)
    for _ in range(5):
        px = cx + rnd.randint(-70, 70); py = cy + rnd.randint(-60, 70)
        d.rectangle((px, py, px + 3, py + 3), fill=spark + (255,))
    mask = Image.fromarray((solid * 255).astype(np.uint8), "L")
    im.putalpha(Image.fromarray(np.minimum(np.asarray(im.getchannel("A")), np.asarray(mask)), "L"))   # sparks stay on the hull
    return im


def build(name):
    cfg, hull, bb, s = load(name)
    w, h = hull.size
    ox = (CELL - w) // 2
    oy = 34 + (280 - h) // 2
    cell = Image.new("RGBA", (CELL, CELL))
    cell.alpha_composite(hull, (ox, oy))
    noz = [(ox + round((x - bb[0]) * s), oy + round((y - bb[1]) * s) - 8, round(bw * s)) for x, y, bw in cfg["noz"]]
    glow = [(ox + round((x - bb[0]) * s), oy + round((y - bb[1]) * s), big) for x, y, big in cfg["glow"]]
    cols, spark = cfg["flame"], cfg["spark"]
    box = (ox, oy, w, h)

    def compose(base, fx=None, marks=None):
        c = Image.new("RGBA", (CELL, CELL))
        if fx is not None:
            c.alpha_composite(fx)
        c.alpha_composite(base)
        if marks is not None:
            c.alpha_composite(marks)
        return c
    lights = [(x, y - 4, 0) for x, y, _ in noz]
    cells = [
        compose(dim(move_parts(cell, box, name, 0), .85)),
        compose(cell, None, dots(glow[:2] + lights, spark, .55)),
        compose(move_parts(cell, box, name, 2), burn(noz, 60, cols, 1), dots(glow, spark, 1)),
        compose(move_parts(cell, box, name, 3), burn(noz, 44, cols, 2), dots(glow, spark, 1)),
        compose(move_parts(cell, box, name, 4), burn(noz, 44, cols, 3), dots(glow, spark, 1)),
        compose(move_parts(cell, box, name, 5), burn(noz, 44, cols, 4), dots(glow, spark, 1)),
    ]
    dmg = scorch(cell, spark, 7)
    cells.append(compose(dmg, burn(noz, 22, cols, 5, .6)))
    strip = Image.new("RGBA", (CELL * 7, CELL))
    for i, c in enumerate(cells):
        strip.alpha_composite(c, (i * CELL, 0))
    key = f"storm_elite_{name}"
    for dest in (SRC_DIR, RES):
        strip.save(dest / f"{key}.png")
    # death strip from the bare, undamaged hull (cell 0 without dimming)
    import build_death_strips_hd as D
    D.PINK = (200, 215, 255)   # the shared recipe's core dot is pink (Space only): Storm uses its white-violet cue
    D.WORLDS["storm"] = ("Storm", (225, 240, 255), (150, 200, 255), [(255, 255, 255), (150, 215, 255), (255, 243, 160)], "plasma")
    death = D.build_one(key, cell, "storm", CELL)
    for dest in (SRC_DIR, RES):
        death.save(dest / f"{key}_death.png")
    # hero image next to the strips (keyed master, full detail)
    k = key_out(HERE / "masters" / f"{name}.png")
    k = k.crop(k.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox())
    k.save(SRC_DIR / f"{key}_concept.png")
    bbox = cells[3].getbbox()
    print(name, "hull", (w, h), "hover bbox", bbox, "nozzles", noz)
    return cells


if __name__ == "__main__":
    for n in (sys.argv[1:] or list(SHIPS)):
        build(n)
