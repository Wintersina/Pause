#!/usr/bin/env python3
"""Desert elites, ground-up redesign: 7-cell HD strips (384 px cells, the flight layout the defs keep) from the Codex masters.

Masters (masters/<name>.png: Codex, nose up, flat #00FF40 key, empty dark engine bells) are keyed, scaled into a 384 px cell and given
drawn engine flames. Cells:  0 parked (engines off)  1 parked idle (pilot lights)  2 lift-off ignition (long jets)  3 hover (flight)
4 bank left  5 bank right (the hull leans 5 degrees and narrows a little)  6 damaged (cracked hull, low ragged flames).

  python3 build_strips.py [--out <dir>] [name ...]       default out: Resources/Elites/Desert (the live strips)
"""
import sys, json, random, math
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
RES = HERE.parents[2] / "Resources" / "Elites" / "Desert"
CELL = 384
K = 2          # drawn flame sizes are written for a 192 cell
U = 4          # one drawn flame "pixel" = U cell px
# name -> master, max hull box in the 384 cell (w, h), nozzle x as a share of the hull width, optional per-nozzle dy (px, + = down),
#         bell width (192-cell px), flame colours (outer, mid, core), crack colours (main, second), hover flame length (192 px), key = def key
SHIPS = {
    "dune_stalker": dict(key="desert_elite_dune_stalker", src="dune_stalker", w=300, h=290, nx=(.386, .616), bell=17,
                         flame=((60, 120, 255), (120, 215, 255), (255, 255, 255)), crack=((255, 244, 214), (130, 215, 255)), L=26),
    "caravan_hauler": dict(key="desert_elite_caravan_hauler", src="caravan_hauler", w=300, h=272, nx=(.268, .5, .728), bell=24,
                           flame=((255, 150, 40), (255, 215, 90), (255, 255, 225)), crack=((255, 244, 214), (60, 225, 215)), L=24),
    "glass_cannon": dict(key="desert_elite_glass_cannon", src="glass_cannon", w=300, h=292, nx=(.356, .64), bell=24,
                         flame=((255, 190, 60), (255, 235, 130), (255, 255, 240)), crack=((255, 244, 214), (90, 215, 215)), L=24),
    "lens_matriarch": dict(key="desert_elite_lens_matriarch", src="lens_matriarch_v2", w=300, h=290, nx=(.385, .61), bell=17,
                           flame=((70, 215, 170), (160, 255, 225), (255, 255, 245)), crack=((255, 244, 214), (110, 235, 190)), L=28),
    "mirror_bastion": dict(key="desert_elite_mirror_bastion", src="mirror_bastion", w=330, h=272, nx=(.294, .5, .709), bell=20,
                           flame=((110, 120, 255), (130, 225, 255), (255, 255, 255)), crack=((255, 244, 214), (140, 225, 255)), L=24),
    "scorpion_walker": dict(key="desert_elite_scorpion_walker", src="scorpion_v3", w=300, h=300, nx=(.38, .62), bell=20,
                            flame=((255, 160, 40), (255, 220, 100), (255, 255, 230)), crack=((255, 244, 214), (255, 200, 90)), L=30),
}
OVERHANG = 44 * K // 2 * 2    # the longest flame (ignition) hangs this far below the hull bottom


def key_out(path):
    im = np.array(Image.open(path).convert("RGB")).astype(float)
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    gn = g - np.maximum(r, b)
    a = np.clip(1 - (gn - 35) / 80, 0, 1)
    gs = np.where(a < 1, np.minimum(g, np.maximum(r, b)), g)
    return Image.fromarray(np.dstack([r, gs, b, a * 255]).astype(np.uint8), "RGBA")


def hull(name):
    cfg = SHIPS[name]
    art = key_out(HERE / "masters" / f"{cfg['src']}.png")
    art = art.crop(art.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox())
    s = min(cfg["w"] / art.width, cfg["h"] / art.height)
    # premultiplied area average: no dark halo at the edge, then a crisp 1-bit silhouette
    arr = np.asarray(art).astype(np.float32)
    arr[..., :3] *= arr[..., 3:4] / 255
    size = (max(1, round(art.width * s)), max(1, round(art.height * s)))
    ch = [np.asarray(Image.fromarray(arr[..., i]).resize(size, Image.Resampling.BOX)) for i in range(4)]
    al = ch[3]
    rgb = np.stack([np.where(al > 0, c / np.maximum(al, 1) * 255, 0) for c in ch[:3]], 2)
    out = Image.fromarray(np.dstack([np.clip(rgb, 0, 255), np.where(al >= 128, 255, 0)]).astype(np.uint8), "RGBA")
    return out


def lean(art, deg, narrow):
    """Bank: rotate about the hull centre and narrow it a little (the hull stays registered)."""
    w, h = art.size
    a = art.resize((max(1, round(w * narrow)), h), Image.Resampling.BICUBIC)
    pad = Image.new("RGBA", (w + 60, h + 60))
    pad.alpha_composite(a, ((w + 60 - a.width) // 2, 30))
    pad = pad.rotate(deg, resample=Image.Resampling.BICUBIC, center=(pad.width / 2, pad.height / 2))
    pad.putalpha(pad.getchannel("A").point(lambda v: 255 if v >= 128 else 0))
    return pad, 30


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


def burn_layer(nozzles, bell, length, cols, seed, ragged=0.0, wiggle=()):
    rnd = random.Random(seed)
    canvas = Image.new("RGBA", (CELL // U, CELL // U))
    for i, (x, y) in enumerate(nozzles):
        k = wiggle[i % len(wiggle)] if wiggle else 1.0
        flame(canvas, x, y, length * k * K, bell * K, cols, rnd, ragged)
    return canvas.resize((CELL, CELL), Image.Resampling.NEAREST)


def glow_dots(nozzles, bell, col, strength):
    im = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(im)
    for x, y in nozzles:
        r = max(3, round(bell * K * .2))
        d.rectangle((x - r, y - r - 8, x + r, y + r - 8), fill=col + (round(255 * strength),))
    return im


def cracks(im, bbox, cols):
    d = ImageDraw.Draw(im)
    x0, y0, x1, y1 = bbox
    cx = (x0 + x1) // 2
    cy = y0 + (y1 - y0) // 3
    d.line([(cx - 4, cy - 28), (cx + 12, cy - 10), (cx, cy + 6), (cx + 18, cy + 26)], fill=cols[0] + (240,), width=4)
    d.line([(cx - 40, cy + 12), (cx - 24, cy + 24), (cx - 32, cy + 38)], fill=cols[1] + (200,), width=3)
    d.rectangle((cx + 16, cy + 24, cx + 20, cy + 28), fill=(255, 250, 235, 255))
    return im


def layout(name):
    """-> hull art, (ox, oy), nozzle points in the unbanked hull cell."""
    cfg = SHIPS[name]
    art = hull(name)
    w, h = art.size
    ox = (CELL - w) // 2
    oy = max(8, (CELL - h - OVERHANG + 20) // 2)
    bottom = oy + h
    dys = cfg.get("dy", (0,) * len(cfg["nx"]))
    noz = [(ox + round(nx * w), bottom - 10 + dy) for nx, dy in zip(cfg["nx"], dys)]
    return art, (ox, oy), noz


def build(name, out_dir):
    cfg = SHIPS[name]
    art, (ox, oy), noz = layout(name)
    w, h = art.size
    cols, bell, L = cfg["flame"], cfg["bell"], cfg["L"]

    def cell(art_img, pos, nozzles, fx):
        c = Image.new("RGBA", (CELL, CELL))
        c.alpha_composite(fx)
        c.alpha_composite(art_img, pos)
        return c
    base = cell(art, (ox, oy), noz, Image.new("RGBA", (CELL, CELL)))
    hover = lambda n, seed, ragged=0.0, k=1.0: burn_layer(n, bell, L * k, cols, seed, ragged, (1.0, .9, 1.05))
    cells = [base,
             cell(art, (ox, oy), noz, glow_dots(noz, bell, cols[1], .85)),
             cell(art, (ox, oy), noz, burn_layer(noz, bell, 36, cols, 1, 0.0, (1.0, .92, 1.06))),
             cell(art, (ox, oy), noz, hover(noz, 7))]
    for deg in (5, -5):
        la, pad = lean(art, deg, .965)
        px, py = ox - 30, oy - 30
        bb = la.getchannel("A").getbbox()
        sx = round((ox + w / 2) - (px + (bb[0] + bb[2]) / 2))   # keep the bbox centre where the hover cell has it: the pivot does not swing
        px += sx
        cx, cy = CELL / 2 + sx, oy + h / 2
        r = math.radians(-deg)
        bn = []
        for x, y in noz:
            dx, dy = (x - CELL / 2) * .965, y - cy
            bn.append((round(cx + dx * math.cos(r) - dy * math.sin(r)), round(cy + dx * math.sin(r) + dy * math.cos(r))))
        cells.append(cell(la, (px, py), bn, hover(bn, 11 + deg)))
    dmg = cell(art, (ox, oy), noz, burn_layer(noz, bell, 26, cols, 99, .55, (1.0, .8, 1.1)))
    cracks(dmg, (ox, oy, ox + w, oy + h), cfg["crack"])
    cells.append(dmg)
    strip = Image.new("RGBA", (CELL * len(cells), CELL))
    for i, c in enumerate(cells):
        strip.alpha_composite(c, (i * CELL, 0))
    out_dir.mkdir(parents=True, exist_ok=True)
    strip.save(out_dir / f"{cfg['key']}.png")
    print(name, json.dumps(dict(hull=(w, h), ox=ox, oy=oy, nozzles=noz, bbox_hover=cells[3].getbbox())))


if __name__ == "__main__":
    args = sys.argv[1:]
    out = RES
    if "--out" in args:
        i = args.index("--out"); out = Path(args[i + 1]); del args[i:i + 2]
    for n in (args or [n for n, c in SHIPS.items() if (HERE / "masters" / f"{c['src']}.png").exists()]):
        build(n, out)
