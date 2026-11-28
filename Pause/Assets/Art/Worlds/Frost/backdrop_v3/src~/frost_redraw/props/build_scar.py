#!/usr/bin/env python3
"""Frost's starship CRASH SCAR decals: Backdrop3/scar.png + scar.json (user, 2026-11-28: "blend the crash setting into the
backdrop for it to make sense please with the prop").

Procedural (no generation): one ground-level decal per starship drawing (try_a: variants 0, 1; try_b: 2, 3), dry and wet,
drawn under the wreck on the pinned props layer (FrostPropsTuning.Scar*): a long gouged FURROW trailing behind the hull along
its axis (~2 hull-lengths past the tail, fading out), ploughed-up SNOW banked at its sides, radial fracture CRACKS round the
impact, scattered hull DEBRIS along the furrow, SOOT by the engines, and (the wet decal, when the layout has water beside the
wreck) broken ice PLATES with dark water at the crater's edge. Everything is feathered to zero alpha well inside the decal
(no rectangle, no halo), in the wreck crust's and the ground's own palette and value range (sampled from the tiles' ice /
water and the strips' crust); the ground itself is never recoloured.

Geometry is in the starship's 256 px cell pixels (x right, y down from its top-left): the decal covers DECAL_BOX (720 px
square) at 768 texels; FrostPropsTuning.ScarCentre / ScarSpan and the keep-out boxes (ScarBoxes) are printed for the C#.

    python3 build_scar.py            # writes Art/Backgrounds/Resources/Worlds/Frost/Backdrop3/scar.{png,json}
    python3 build_scar.py --check
"""
import json, math, sys
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter, distance_transform_edt

HERE = Path(__file__).resolve().parent
OUT = Path(sys.argv[sys.argv.index("--out") + 1]) if "--out" in sys.argv else HERE.parents[5] / "Backgrounds" / "Resources" / "Worlds" / "Frost" / "Backdrop3"
N = 768
X0, Y0, SPAN = -112.0, -352.0, 720.0          # the decal's cell-px box
K = N / SPAN                                   # texels per cell px
# per drawing (cell px): hull centre (impact), tail, nose, engines; the furrow runs from the centre through the tail
DRAW = {
    "a": dict(centre=(138, 113), tail=(226, 26), nose=(50, 200), engines=[(214, 30), (232, 50), (196, 18)], hl=250),
    "b": dict(centre=(125, 140), tail=(200, 62), nose=(38, 212), engines=[(196, 68), (206, 86), (176, 56)], hl=235),
}
PAST = 2.0                                     # furrow length past the tail, in hull lengths
# palette (painted RGB 0..1): the ground's ice (tiles' O / C mean ~ (32, 46, 54)) to the crust's snow (~ (84, 89, 92))
ICE_DK = np.array([24, 33, 40]) / 255
ICE = np.array([40, 52, 60]) / 255
SNOW = np.array([78, 88, 94]) / 255
CRACK = np.array([10, 17, 24]) / 255
SOOT = np.array([14, 13, 14]) / 255
WATER = np.array([3, 12, 24]) / 255
METAL = [np.array(c) / 255 for c in ((50, 52, 56), (30, 31, 34), (74, 76, 80), (58, 44, 34))]


def noise(rng, sigma, shape=(N, N)):
    n = gaussian_filter(rng.standard_normal(shape), sigma)
    return n / (n.std() + 1e-9)


def to_tex(p):
    return ((p[0] - X0) * K, (p[1] - Y0) * K)


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def over(dst, rgb, a):
    a = np.clip(a, 0, 1)[..., None]
    out_a = a + dst[..., 3:] * (1 - a)
    dst[..., :3] = (rgb * a + dst[..., :3] * dst[..., 3:] * (1 - a)) / np.maximum(out_a, 1e-6)
    dst[..., 3:] = out_a


def decal(t, wet, seed):
    d = DRAW[t]
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:N, 0:N].astype(float)
    img = np.zeros((N, N, 4))
    cx, cy = to_tex(d["centre"]); tx, ty = to_tex(d["tail"]); nx, ny = to_tex(d["nose"])
    ux, uy = tx - cx, ty - cy; L0 = math.hypot(ux, uy); ux, uy = ux / L0, uy / L0
    hl = d["hl"] * K
    along = (xx - cx) * ux + (yy - cy) * uy                    # from the impact towards (and past) the tail
    across = -(xx - cx) * uy + (yy - cy) * ux
    end = L0 + PAST * hl
    s = np.clip(along / end, 0, 1)
    wob = noise(rng, 40) * 6 * K * s                           # the furrow wanders a little
    ac = across + wob
    half = (42 - 24 * s) * K                                   # its half-width, narrowing to the far end
    fade = smooth(1.0, .55, along / end) * smooth(-.05 * hl, .1 * hl, along)
    inside = smooth(1.0, .7, np.abs(ac) / half)
    # the floor: dark gouged ice, striated along the furrow
    # streaks: noise smeared along the axis (sample a 1-D noise by `across`)
    line = gaussian_filter(rng.standard_normal(4 * N), 1.5); line /= line.std()
    idx = np.clip((ac / K * 1.5 + 2 * N).astype(int), 0, 4 * N - 1)
    streak = line[idx] * .6 + noise(rng, 1.2) * .3
    floor = ICE_DK + (ICE - ICE_DK) * np.clip(.5 + .35 * streak, 0, 1)[..., None]
    over(img, floor, inside * fade * .85)
    # banks: ploughed snow just outside the edges, lumpy, brighter near the wreck
    bank_d = (np.abs(ac) - half) / K
    lump = np.clip(.6 + .5 * noise(rng, 4), 0, 1.2)
    bank = np.exp(-((bank_d - 8) / 7) ** 2) * lump * smooth(1.0, .4, along / end) * smooth(-.25 * hl, .15 * hl, along)
    shade = np.where(ac > 0, 1.0, .82)[..., None]                # the lit side and the shadowed side
    over(img, ICE + (SNOW - ICE) * np.clip(.5 + .35 * noise(rng, 1), 0, 1)[..., None] * shade, bank * .8)
    # a darker lip between the floor and the banks
    lip = np.exp(-((bank_d - 1) / 2.5) ** 2) * fade
    over(img, CRACK, lip * .45)
    # the crater apron: churned snow round the impact, under the wreck's crust, feathered out
    r = np.hypot(xx - cx, yy - cy) / K
    apron = smooth(165, 115, r + 16 * noise(rng, 10)) * np.clip(.55 + .45 * noise(rng, 1.5), 0, 1)
    over(img, ICE + (SNOW - ICE) * np.clip(.3 + .3 * noise(rng, 1), 0, 1)[..., None], apron * .4)
    # radial fracture cracks from the impact (random walks), thin and dark, fading outwards
    cr = np.zeros((N, N))
    for i in range(26):
        a = rng.uniform(0, 2 * math.pi); length = rng.uniform(110, 185) * K      # tips <= ~250 px from the impact (ScarBelow)
        px, py = cx + math.cos(a) * 60 * K, cy + math.sin(a) * 60 * K
        w0 = rng.uniform(.8, 1.6)
        step = 2.0
        for j in range(int(length / step)):
            a += rng.normal(0, .12); px += math.cos(a) * step; py += math.sin(a) * step
            if not (1 <= px < N - 1 and 1 <= py < N - 1): break
            k = 1 - j * step / length
            cr[int(py), int(px)] = max(cr[int(py), int(px)], k * w0)
            if rng.random() < .03 and k > .3:            # a branch
                b = a + rng.choice([-1, 1]) * rng.uniform(.5, 1.0); bx, by = px, py
                for _ in range(int(rng.uniform(10, 40))):
                    b += rng.normal(0, .15); bx += math.cos(b) * step; by += math.sin(b) * step
                    if 1 <= bx < N - 1 and 1 <= by < N - 1: cr[int(by), int(bx)] = max(cr[int(by), int(bx)], k * .7)
    cr = np.maximum(gaussian_filter(cr, .6) * 2.2, 0)
    over(img, CRACK, np.clip(cr, 0, 1) * .8)
    # soot by the engines: soft, uneven
    soot = np.zeros((N, N))
    for e in d["engines"]:
        ex, ey = to_tex(e)
        soot += np.exp(-(((xx - ex - ux * 22 * K) ** 2 + (yy - ey - uy * 22 * K) ** 2) / (2 * (18 * K) ** 2)))
    soot *= np.clip(.6 + .6 * noise(rng, 2), 0, 1.3)
    over(img, SOOT, np.clip(soot, 0, 1) * .45)
    # debris: a few hull fragments and specks along the furrow, thinning out
    for i in range(70):
        f = rng.uniform(.15, 1.0) ** 1.6
        al = L0 + f * (end * .85 - L0) + rng.normal(0, 12)
        ad = rng.normal(0, 1) * (36 * K) * (1.4 - .6 * f)
        px, py = cx + ux * al - uy * ad, cy + uy * al + ux * ad
        size = max(1, int(round(rng.choice([1, 1, 2, 2, 3, 4]) * (1.3 - f))))
        c = METAL[rng.integers(len(METAL))]
        x0, y0 = int(px), int(py)
        if not (2 <= x0 < N - 6 and 2 <= y0 < N - 6): continue
        m = np.zeros((N, N)); m[y0:y0 + size, x0:x0 + size + (size > 2)] = 1
        over(img, CRACK, gaussian_filter(m, 1.0) * .5)      # its shadow / the pit it dug
        over(img, c, m)
        if size >= 3: img[y0, x0, :3] = METAL[2]
    if wet:
        # broken ice plates with dark water showing at the crater's edge, on the nose side
        ang = np.arctan2(yy - cy, xx - cx); nang = math.atan2(ny - cy, nx - cx)
        side = np.cos(ang - nang)
        ring = smooth(115, 140, r + 8 * noise(rng, 5)) * smooth(200, 160, r + 10 * noise(rng, 6)) * smooth(0, .6, side)
        # Voronoi plates: water in the gaps between cells
        pts = rng.uniform(0, N, (900, 2))
        from scipy.spatial import cKDTree
        dd, _ = cKDTree(pts).query(np.stack([xx.ravel(), yy.ravel()], -1), k=2)
        gap = (dd[:, 1] - dd[:, 0]).reshape(N, N)
        water = smooth(4, 1, gap + 1.5 * noise(rng, 1)) * ring
        open_w = smooth(.7, .95, ring) * smooth(.75, .95, side + .15 * noise(rng, 6)) * (noise(rng, 6) > .3)
        plate = ICE + (SNOW - ICE) * np.clip(.35 + .3 * noise(rng, 1), 0, 1)[..., None]
        over(img, plate, ring * .55)
        over(img, WATER, np.clip(np.maximum(water, open_w * .85), 0, 1) * .9)
    # never a hard edge: everything to zero well inside the square
    img[..., :3] *= np.clip(1 + .07 * noise(rng, .6), .8, 1.2)[..., None]      # pixel grain, like the painted ground
    edge = np.minimum.reduce([xx, yy, N - 1 - xx, N - 1 - yy])
    img[..., 3] *= smooth(4, 60, edge)
    return np.clip(img, 0, 1)


SCARS = [("a", False), ("a", True), ("b", False), ("b", True)]   # scar_0 .. scar_3: drawing a dry / wet, b dry / wet


def boxes(t):
    """keep-out boxes along the furrow (cell px from the cell centre, y up; half-size), for FrostPropsTuning.ScarBoxes"""
    d = DRAW[t]; cx, cy = d["centre"]; tx, ty = d["tail"]
    L0 = math.hypot(tx - cx, ty - cy); ux, uy = (tx - cx) / L0, (ty - cy) / L0
    out = []
    for f in (.12, .42, .72, 1.0):
        al = L0 + f * PAST * d["hl"] * .85
        out.append((round(cx + ux * al - 128), round(128 - (cy + uy * al)), round(62 - 22 * f)))
    return out


def build():
    sheet = Image.new("RGBA", (2 * N, 2 * N), (0, 0, 0, 0)); sprites = []
    for i, (t, wet) in enumerate(SCARS):
        a = decal(t, wet, 7 + i // 2)
        im = Image.fromarray((a * 255 + .5).astype(np.uint8), "RGBA")
        x, y = (i % 2) * N, (i // 2) * N
        sheet.paste(im, (x, y))
        sprites.append({"n": f"scar_{i}", "x": x, "y": 2 * N - y - N, "w": N, "h": N})
    return sheet, {"sprites": sprites}


def main():
    sheet, js = build()
    if "--check" in sys.argv:
        old = Image.open(OUT / "scar.png").convert("RGBA")
        same = old.size == sheet.size and old.tobytes() == sheet.tobytes() and json.loads((OUT / "scar.json").read_text()) == js
        print("scar atlas up to date" if same else "scar atlas DIFFERS"); sys.exit(0 if same else 1)
    sheet.save(OUT / "scar.png", optimize=True)
    (OUT / "scar.json").write_text(json.dumps(js, indent=2) + "\n")
    print("wrote", OUT / "scar.png", sheet.size)
    print("decal centre (cell px from the cell centre, y up):", X0 + SPAN / 2 - 128, 128 - (Y0 + SPAN / 2), "span", SPAN)
    for t in "ab": print("boxes", t, boxes(t))


if __name__ == "__main__":
    main()
