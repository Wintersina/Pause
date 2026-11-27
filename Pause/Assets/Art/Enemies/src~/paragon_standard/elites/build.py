#!/usr/bin/env python3
"""Paragon-standard elite redraw: one approved PixelLab drawing -> the elite's whole strip family, installed.

    python3 build.py [key ...] [--no-install] [--sheet DIR]

Per elite (spec.py holds the hand-read engines / muzzles; hull.py keys the drawing):
  hull      the approved drawing flipped nose-up (the elite convention: noseDeg 90, engines at the bottom), hard alpha 128, cut edges
            closed, drawn flames cut off, NEAREST-scaled so its longest edge = the old frame 0's, centred on the old frame 0's bbox;
            each engine bell gets a dark mouth (EliteTest's bell rule: the nozzle point is dark hull on cell 0)
  cells     the OLD strip's layout, read from the def's cells map and the old files' cell counts:
              parked      hull dimmed (engines off)        parkedIdle  pilot lights glowing in the mouths
              liftoff     short flames                     flight      engine_fx flames (the shared elite fire recipe,
                                                                       tide_elites/build_strips.py), a different length per cell / engine
              banks       shear lean + 3 % narrowing + a light-to-dark shading shift across the hull, flames on the leaned mouths
              damaged     dimmed, scorched, cracked (seams glow in the world cue), sparks coloured from the hull's own glow pixels,
                          ragged flames; the _damaged companion loops it with the flames / sparks moving
  _lights   additive overlay on the drawing's glowing parts: bright saturated pixels in the world cue hue (+-30 deg), warped like
            the cell, dim on the pad, pulsing in flight, flickering when damaged
  _death    the world's existing elite death method (Elite/src~/build_death_strips_hd.py palettes per world; Verdant's split death)
  def       muzzles / nozzles (+ per-cell nozzle offsets) re-measured on the new drawing, silhouetteShare re-baked
  mouths    engine_mouths.txt rows for the drawn flames (EliteEngineFireTest.Seats)
The old strips / defs / mouths are kept in old/.
"""
import colorsys, importlib.util, json, math, random, re, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import hull as Hm                                                     # noqa: E402
from spec import SPEC                                                 # noqa: E402

ART = HERE.parents[3]                                                 # .../Assets/Art
RES = ART / "Resources/Elites"
CODEX = ART / "Enemies/Elite"
SRC = ART / "Enemies/src~"
OLD = HERE / "old"
OUT = HERE / "strips"
MOUTHS = SRC / "elite_engine_fire/engine_mouths.txt"
PICKS = Path("/private/tmp/claude-501/-Users-sina-Developer/50b8c9d7-da6c-44fe-8c92-736a84a1fed0/scratchpad/picks_all.json")
CELL = 384
CUE = Hm.CUE


def load_mod(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m); return m


T = load_mod("tidebuild", SRC / "tide_elites/build_strips.py")       # engine_fx / pilot_lights: the shared elite fire
sys.path.insert(0, str(CODEX / "src~"))
import build_death_strips_hd as D                                     # noqa: E402


def rgb(h, s, v):
    return tuple(int(round(c * 255)) for c in colorsys.hsv_to_rgb((h % 360) / 360, s, v))


def flame_cols(world):
    h = CUE[world]
    return rgb(h, .85, .8), rgb(h, .5, 1.0), (255, 255, 255)


# ---------------------------------------------------------------------------------------------------------------- image helpers
def blank():
    return Image.new("RGBA", (CELL, CELL))


def hard(im):
    a = np.array(im); a[..., 3] = np.where(a[..., 3] > 128, 255, 0); return Image.fromarray(a, "RGBA")


def dim(im, k):
    a = np.asarray(im).astype(np.float32).copy(); s = a[..., 3] > 0
    a[s, :3] *= k
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def shift(im, dx, dy):
    c = blank(); c.alpha_composite(im, (dx, dy)) if dx >= 0 and dy >= 0 else c.paste(im, (dx, dy), im); return c


def break_runs(im, limit=48):
    """EnemyRosterTest: no straight vertical silhouette edge of 58 px or more (a cut-off pose). A flat-sided drawing scaled up gets a
    1 px notch every `limit` rows of a straight side."""
    a = np.array(im); op = a[..., 3] > 128
    H, W = op.shape
    for side in (-1, 1):
        nb = np.zeros_like(op)
        if side < 0: nb[:, 1:] = op[:, :-1]
        else: nb[:, :-1] = op[:, 1:]
        edge = op & ~nb
        for x in range(W):
            run = 0
            for y in range(H):
                if edge[y, x]:
                    run += 1
                    if run >= limit:
                        a[y, x, 3] = 0; op[y, x] = False; run = 0
                else:
                    run = 0
    return Image.fromarray(a, "RGBA")


def lum(px):
    return .3 * px[0] + .59 * px[1] + .11 * px[2]


def paint_mouths(hull, mouths):
    """Each bell's exit: a dark ellipse on the hull (the flames start inside it; the nozzle point sits on it)."""
    a = np.array(hull); yy, xx = np.indices((CELL, CELL)); out = []
    for cx, cy, rx, ry in mouths:
        # the mouth centre must be on the hull: climb until it is
        while a[int(round(cy)), int(round(cx)), 3] < 255 and cy > 8: cy -= 1
        m = (((xx - cx) / (rx * .78)) ** 2 + ((yy - cy) / max(2.0, ry * .9)) ** 2 <= 1) & (a[..., 3] == 255)
        a[m, :3] = (16, 14, 22)
        out.append((cx, cy, rx, ry))
    return Image.fromarray(a, "RGBA"), out


# ---------------------------------------------------------------------------------------------------------------- bank / shading
def bank_x(sign, top, h):
    mid = top + h * .55
    return lambda x, y: CELL / 2 + (x - CELL / 2) * .97 + sign * 4 * (mid - y) / (h * .5)


def bank(im, sign, top, h, box):
    """Lean into the turn (the tide recipe's shear + 3 % narrowing) and shift the shading: the side the elite rolls toward darkens."""
    b, _ = T.bank(im, sign, top, h)
    a = np.asarray(b).astype(np.float32).copy()
    x0, x1 = box
    xn = np.clip((np.arange(CELL) - (x0 + x1) / 2) / max(1, (x1 - x0) / 2), -1, 1)
    k = 1 + .16 * xn if sign < 0 else 1 - .16 * xn   # bank left: the left side (xn < 0) darkens, the right lightens; bank right mirrored
    s = a[..., 3] > 0
    a[..., :3] = np.where(s[..., None], np.clip(a[..., :3] * k[None, :, None], 0, 255), a[..., :3])
    return Image.fromarray(a.astype(np.uint8), "RGBA")


# ---------------------------------------------------------------------------------------------------------------- damage
def glow_palette(hull, mask):
    px = np.asarray(hull)[mask][:, :3].astype(int)
    if len(px) < 8: return [(255, 255, 255)]
    order = np.argsort(px.sum(1))
    return [tuple(int(v) for v in px[order[int(q * (len(px) - 1))]]) for q in (.55, .8, .97)]


def damage(hull, mask, world, seed):
    """Dimmed hull, soot blotches, two cracks with a glowing seam, sparks taken from the hull's own glow colours (clipped to the hull)."""
    rnd = random.Random(seed)
    a = np.asarray(dim(hull, .78)).astype(np.float32).copy()
    op = a[..., 3] > 0
    ys, xs = np.nonzero(op); x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    yy, xx = np.indices((CELL, CELL))
    for _ in range(7):                                           # soot: darker irregular blotches
        cx, cy = rnd.uniform(x0 + 20, x1 - 20), rnd.uniform(y0 + 20, y1 - 30)
        r = rnd.uniform(14, 30)
        n = np.sin(xx * .31 + cy) * np.cos(yy * .27 + cx) * 4
        m = ((xx - cx) ** 2 + (yy - cy) ** 2 <= (r + n) ** 2) & op
        a[m, :3] *= .62
    im = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")
    d = ImageDraw.Draw(im)
    seam = rgb(CUE[world], .55, 1.0)
    cx = (x0 + x1) // 2; cy = y0 + (y1 - y0) * 2 // 5
    for k, (sx, sy, n) in enumerate(((cx - 26, cy - 34, 6), (cx + 30, cy + 26, 4))):
        pts = [(sx, sy)]
        for _ in range(n):
            pts.append((pts[-1][0] + rnd.randint(-14, 14), pts[-1][1] + rnd.randint(10, 18)))
        d.line(pts, fill=(10, 9, 14, 255), width=7 - k * 2)
        d.line([(x + 1, y) for x, y in pts], fill=seam + (255,), width=3 - k)
        d.line([(x + 1, y) for x, y in pts], fill=(255, 255, 255, 255), width=1)
    out = np.asarray(im).copy()
    out[..., 3] = np.where(op, out[..., 3], 0)                    # nothing off the hull
    out[~op] = 0
    return Image.fromarray(out, "RGBA")


def sparks(im, hull_mask, pal, seed, n=7):
    rnd = random.Random(seed)
    im = im.copy(); px = im.load()
    ys, xs = np.nonzero(hull_mask)
    for _ in range(n):
        i = rnd.randrange(len(xs)); x, y = int(xs[i]), int(ys[i])
        col = pal[rnd.randrange(len(pal))] + (255,)
        L = rnd.randint(2, 4)
        for t in range(-L, L + 1):
            for (u, v) in ((x + t, y), (x, y + t)):
                if 0 <= u < CELL and 0 <= v < CELL and hull_mask[v, u]:
                    px[u, v] = (255, 255, 255, 255) if abs(t) <= 1 else col
    return im


# ---------------------------------------------------------------------------------------------------------------- lights
def lights_cell(mask, world, strength, seed=None, flicker=False, comps=None, alpha_max=210):
    """Additive overlay: the glow mask in the cue colour; flicker switches whole glow parts off."""
    col = rgb(CUE[world], .45, 1.0)
    m = mask.copy()
    if flicker and comps is not None:
        lab, n = comps
        rnd = random.Random(seed)
        for i in range(1, n + 1):
            if rnd.random() < .4: m[lab == i] = False
        if not m.any(): m = mask.copy()
    m[::48] = False                                               # (no straight 58 px edge in the overlay either: EnemyRosterTest)
    a = np.zeros((CELL, CELL, 4), np.uint8)
    a[m, :3] = col; a[m, 3] = max(1, int(round(alpha_max * strength)))
    return Image.fromarray(a, "RGBA")


def warp_mask(mask, f):
    im = Image.fromarray(np.dstack([np.full(mask.shape + (3,), 255, np.uint8), (mask * 255).astype(np.uint8)]), "RGBA")
    return np.asarray(f(im))[..., 3] > 128


def clip_margin(im, m=6):
    a = np.array(im); a[:m] = 0; a[-m:] = 0; a[:, :m] = 0; a[:, -m:] = 0; return Image.fromarray(a, "RGBA")


# ---------------------------------------------------------------------------------------------------------------- the build
def cells_count(path):
    if not path.exists(): return 0
    im = Image.open(path); return im.width // im.height


def build(key, src_path):
    world = key.split("_")[0]; W = world.capitalize()
    d = json.load(open(OLD / "defs" / f"{key}.json"))
    c = d["cells"]
    n_main = cells_count(OLD / f"{key}.png"); n_dmg = cells_count(OLD / f"{key}_damaged.png"); n_banks = cells_count(OLD / f"{key}_banks.png")
    has_lights = (OLD / f"{key}_lights.png").exists()
    r = Hm.place(key, src_path)
    H0 = break_runs(r["hull"])
    H, mouths = paint_mouths(H0, r["mouths"])
    cols = flame_cols(world)
    op = np.asarray(H)[..., 3] > 128
    ys, xs = np.nonzero(op); top, bot = ys.min(), ys.max(); hh = bot - top + 1
    box = (xs.min(), xs.max())
    my = max(m[1] for m in mouths)
    L = float(SPEC[key].get("L", np.clip((CELL - 8 - my) / (2 * 1.75), 10, 26)))     # engine_fx length unit (flame px ~ 2 L): stays inside the 6 px margin
    lift_shift = -2 if key in ("frost_elite_cryo_siren", "frost_elite_rimebreaker") else 0

    def fire(base, ms, lens, seed, ragged=0.0):
        return clip_margin(T.engine_fx(base, ms, lens, cols, seed, ragged))

    n = len(mouths)
    flight = list(c["flight"])
    states = []
    facs = [1.0, .8, 1.12, .9, 1.05, .76, 1.16, .86]
    pick = SPEC[key].get("nozzles")
    Hf, fm, live = H, mouths, list(range(n))
    static_mask = np.zeros((CELL, CELL), bool)
    if pick:
        # engines that are not def nozzles (high on the hull): their flames are drawn once into the flight hull, so the flight cells
        # differ only round the nozzles (EliteEngineFireTest); their flicker is carried by the lights overlay instead
        st = [m for i, m in enumerate(mouths) if i not in pick]
        Hf = fire(H, st, [(L * .62, .9)] * len(st), 5)
        static_mask = (np.asarray(Hf)[..., 3] > 128) & (np.asarray(H)[..., 3] == 0)
        fm = [m for i, m in enumerate(mouths) if i in pick]; live = pick
    for k in range(len(flight)):
        lens = [(L * (facs[k % 8] + [0, .3, -.15, .12, -.22][(i + k) % 5]), .92 + .08 * ((i + k) % 2)) for i in live]
        states.append(fire(Hf, fm, lens, 10 + 7 * k, ragged=.15 * (k % 2)))
    total = n_main + n_dmg + n_banks
    cells = [None] * total
    xform = {}                                                    # cell -> function(img) giving that cell's hull pose (for lights)
    seats = {}                                                    # cell -> mouths the flame was drawn on
    ident = lambda im: im
    cells[c["parked"]] = dim(H, .82); xform[c["parked"]] = ident
    pi = H.copy(); pi.alpha_composite(T.pilot_lights(mouths, cols[1], .85)); cells[c["parkedIdle"]] = pi; xform[c["parkedIdle"]] = ident
    Hl = shift(H, 0, lift_shift) if lift_shift else H
    ml = [(x, y + lift_shift, rx, ry) for x, y, rx, ry in mouths]
    cells[c["liftoff"]] = fire(Hl, ml, [(L * (.42 + .06 * (i % 2)), .9) for i in range(n)], 77)
    xform[c["liftoff"]] = (lambda im: shift(im, 0, lift_shift)) if lift_shift else ident
    seats[c["liftoff"]] = ml
    for k, ci in enumerate(flight):
        cells[ci] = states[k]; xform[ci] = ident; seats[ci] = mouths
    bank_mouths = {}
    for sign, name, loop in ((-1, "bankLeft", c.get("bankLeftLoop")), (1, "bankRight", c.get("bankRightLoop"))):
        idxs = loop or [c[name]]
        f = bank_x(sign, top, hh)
        bm = [(f(x, y), y, rx * .97, ry) for x, y, rx, ry in mouths]
        B = bank(H, sign, top, hh, box)
        for j, ci in enumerate(idxs):
            lens = [(L * (1.06 + .08 * ((i + j) % 2) - .1 * (j == 1)), .96 + .06 * (i % 2)) for i in range(n)]
            cells[ci] = fire(B, bm, lens, 40 + ci)
            xform[ci] = (lambda s: (lambda im: bank(im, s, top, hh, box)))(sign)
            seats[ci] = bm; bank_mouths[ci] = bm
    gm = Hm.glow_mask(H, world, key)
    gm = tighten(H, gm, world, key)
    pal = glow_palette(H, gm)
    dmg = damage(H, gm, world, 7)
    hull_m = np.asarray(dmg)[..., 3] > 128
    dloop = list(c.get("damagedLoop") or [c["damaged"]])
    if c["damaged"] not in dloop: dloop = [c["damaged"]] + dloop
    patt = [.7, .42, .82, .5, .76, .36]
    for j, ci in enumerate(dloop):
        lens = [(L * patt[(j + 2 * i) % 6], .8 + .06 * ((i + j) % 3)) for i in range(n)]
        im = fire(dmg, mouths, lens, 99 + 13 * j, ragged=.5)
        cells[ci] = sparks(im, hull_m, pal, 300 + j, 6 + j % 3); xform[ci] = ident
    # anything left unset (a layout cell the map does not name) gets the hover cell
    for i in range(total):
        if cells[i] is None:
            cells[i] = states[0]; xform[i] = ident
    # lights
    lights = None
    if has_lights:
        from scipy import ndimage
        comps = ndimage.label(gm)
        lights = []
        for i in range(total):
            m = warp_mask(gm, xform[i])
            m &= np.asarray(cells[i])[..., 3] > 128
            if i == c["parked"]: lc = lights_cell(m, world, .16)
            elif i == c["parkedIdle"]: lc = lights_cell(m, world, .32)
            elif i == c["liftoff"]: lc = lights_cell(m, world, .66)
            elif i in dloop: lc = lights_cell(m, world, .62, seed=500 + i, flicker=True, comps=(ndimage.label(m)))
            elif i in flight:
                lc = lights_cell(m, world, [1.0, .78, .92, .7, .96, .74, .88, .8][flight.index(i) % 8])
                if static_mask.any():                                  # the static (non-nozzle) flames flicker in the overlay
                    fl = lights_cell(static_mask, world, [.9, .35, .7, .2][flight.index(i) % 4], alpha_max=170)
                    lc.alpha_composite(fl)
            else: lc = lights_cell(m, world, .9)
            lights.append(lc)
    return dict(key=key, world=world, W=W, d=d, cells=cells, lights=lights, n_main=n_main, n_dmg=n_dmg, n_banks=n_banks,
                H=H, mouths=mouths, seats=seats, bank_mouths=bank_mouths, top=top, hh=hh, r=r, gm=gm, L=L, lift_shift=lift_shift,
                flight=flight, dloop=dloop)


def tighten(H, gm, world, key):
    """The cue band can catch a whole hull painted in that hue (Desert brass, Frost ice): raise the bar until it is the glowing parts
    only (<= 2.4 % of the cell); a drawing whose glow sits outside the band gets the band widened."""
    if "glow" in SPEC[key]: return gm
    a = np.asarray(H); h, s, v = Hm.hsv_arrays(a)
    gap = Hm.hue_gap(h, CUE[world]); op = a[..., 3] > 128
    band = 30
    if gm.sum() < 150: band = 60
    smin, vmin = .45, .6
    m = op & (gap <= band) & (s > smin) & (v > vmin)
    while m.sum() > .024 * CELL * CELL and vmin < .97:
        vmin += .02; smin = min(.7, smin + .01)
        m = op & (gap <= band) & (s > smin) & (v > vmin)
    return m


# ---------------------------------------------------------------------------------------------------------------- death
DEATH_PAL = {
    "ember": ((255, 214, 140), ("Ember", (255, 220, 120), (255, 140, 40), [(255, 255, 255), (255, 150, 50), (255, 196, 90)], "slag"), 3),
    "tide": ((150, 245, 225), ("Tide", (170, 245, 235), (90, 200, 235), [(255, 255, 255), (130, 225, 235), (255, 196, 90)], "plasma"), 3),
    "space": ((255, 79, 216), ("Space", (150, 245, 255), (120, 80, 255), [(255, 79, 216), (90, 220, 255), (200, 120, 255)], "plasma"), 0),
    "storm": ((200, 215, 255), ("Storm", (225, 240, 255), (150, 200, 255), [(255, 255, 255), (150, 215, 255), (255, 243, 160)], "plasma"), "bare"),
}
DESERT_PAL = {
    "dune_stalker": ((190, 225, 255), (70, 120, 235), [(255, 255, 255), (150, 215, 255), (235, 200, 120)], "plasma"),
    "caravan_hauler": ((255, 225, 140), (255, 160, 50), [(255, 255, 255), (255, 215, 90), (90, 225, 215)], "slag"),
    "glass_cannon": ((255, 235, 160), (255, 190, 70), [(255, 255, 255), (255, 225, 120), (120, 215, 220)], "ice"),
    "lens_matriarch": ((190, 255, 225), (80, 215, 170), [(255, 255, 255), (150, 235, 255), (230, 200, 120)], "ice"),
    "mirror_bastion": ((200, 225, 255), (120, 130, 255), [(255, 255, 255), (150, 225, 255), (235, 200, 110)], "ice"),
}
FROST_DEFAULT = D.WORLDS["frost"]


def death(b):
    key, world = b["key"], b["world"]
    if world == "verdant":
        V = load_mod("verdant_hd", CODEX / "Verdant/new/src~/build_verdant_hd.py")
        ys, xs = np.nonzero(b["gm"]) if b["gm"].any() else np.nonzero(np.asarray(b["H"])[..., 3] > 128)
        spec = {"core": (int(xs.mean() / 2), int(ys.mean() / 2))}
        frames = V.death_frames(b["H"], spec)
        out = Image.new("RGBA", (CELL * 3, CELL))
        for i, f in enumerate(frames): out.alpha_composite(f, (i * CELL, 0))
        return out
    if world == "desert":
        D.PINK = (255, 244, 214)
        D.WORLDS["d_x"] = ("Desert",) + DESERT_PAL[key.replace("desert_elite_", "")]
        return D.build_one(key, b["cells"][0], "d_x", CELL)
    if world == "frost":
        if key in ("frost_elite_cryo_siren", "frost_elite_rimebreaker"):
            D.PINK = (190, 235, 255)
            D.WORLDS["frost"] = ("Frost", (200, 245, 255), (110, 190, 255), [(255, 255, 255), (150, 215, 255), (210, 240, 255)], "ice")
        else:                                                     # the authored Frost deaths keep the shared recipe's pink core accent
            D.PINK = (255, 79, 216); D.WORLDS["frost"] = FROST_DEFAULT
        return D.build_one(key, b["cells"][3], "frost", CELL)
    pink, pal, base = DEATH_PAL[world]
    D.PINK = pink; D.WORLDS[world] = pal
    src = b["H"] if base == "bare" else b["cells"][base]
    return D.build_one(key, src, world, CELL)


# ---------------------------------------------------------------------------------------------------------------- points / def
def snap_opaque(cell, x, y, r=4):
    a = np.asarray(cell)[..., 3] > 128
    x, y = int(round(x)), int(round(y))
    if a[max(0, y - 1):y + 2, max(0, x - 1):x + 2].any(): return x, y
    ys, xs = np.nonzero(a)
    i = int(np.argmin((xs - x) ** 2 + (ys - y) ** 2))
    return int(xs[i]), int(ys[i])


def points(b):
    d = b["d"]; c = d["cells"]
    flight0 = b["cells"][c["flight"][0]]
    r = b["r"]
    mz = []
    for m in d["muzzles"]:
        sx, sy = SPEC[b["key"]]["muzzles"][m["name"]]
        x, y = r["to_cell"](sx, sy)
        x, y = snap_opaque(flight0, x, y)
        mz.append(dict(name=m["name"], x=x, y=y, dir=m.get("dir", -1.0)))
    names = [n["name"] for n in d["nozzles"]]
    letters = "abcdefgh" if names[0] in "abcdefgh" else None
    if len(names) != len(b["mouths"]):
        if letters: names = list(letters[:len(b["mouths"])])
        elif len(b["mouths"]) == 2: names = ["l", "r"]
        elif len(b["mouths"]) == 3: names = ["l", "c", "r"]
        else: names = list("abcdefgh"[:len(b["mouths"])])
    total = len(b["cells"])
    nz = []
    pick = SPEC[b["key"]].get("nozzles")
    if pick:
        names = [n["name"] for n in d["nozzles"]]
        if len(names) != len(pick): names = list("abcdefgh"[:len(pick)])
        names = {j: names[k] for k, j in enumerate(pick)}
    for i, (cx, cy, rx, ry) in enumerate(b["mouths"]):
        if pick and i not in pick: continue
        x, y = int(round(cx)), int(round(cy))
        offs = [0] * (2 * total)
        for ci in range(total):
            if ci in b["bank_mouths"]:
                bx = b["bank_mouths"][ci][i][0]
                offs[2 * ci] = int(round(bx - cx)) or (1 if bx >= cx else -1)
            elif ci == c["liftoff"] and b["lift_shift"]:
                offs[2 * ci + 1] = b["lift_shift"]
        nz.append(dict(name=names[i], x=x, y=y, dir=-1.0, offsets=offs))
    return mz, nz


def share(cells):
    best = 0.0
    for im in cells:
        a = np.asarray(im)[..., 3] >= 128
        ys, xs = np.nonzero(a)
        if not len(xs): continue
        dx = xs + .5 - CELL / 2; dy = ys + .5 - CELL / 2
        best = max(best, math.sqrt(float((dx * dx + dy * dy).max())) / CELL)
    return round(best, 4)


def fmt_points(items, indent, offsets):
    out = []
    for p in items:
        lines = [f'{indent}  {{', f'{indent}    "name": "{p["name"]}",', f'{indent}    "x": {p["x"]},', f'{indent}    "y": {p["y"]},',
                 f'{indent}    "dir": {float(p["dir"])}' + ("," if offsets else "")]
        if offsets: lines.append(f'{indent}    "offsets": [' + ", ".join(str(v) for v in p["offsets"]) + "]")
        lines.append(f"{indent}  }}")
        out.append("\n".join(lines))
    return "[\n" + ",\n".join(out) + f"\n{indent}]"


def replace_block(text, field, block):
    i = text.index(f'"{field}"'); j = text.index("[", i)
    depth, k = 0, j
    while True:
        ch = text[k]
        if ch == "[": depth += 1
        elif ch == "]":
            depth -= 1
            if depth == 0: break
        k += 1
    return text[:j] + block + text[k + 1:]


def write_def(b, mz, nz, sil, hr):
    p = RES / "Defs" / f"{b['key']}.json"
    t = (OLD / "defs" / f"{b['key']}.json").read_text()
    ind = re.search(r'\n(\s*)"muzzles"', t).group(1)
    t = replace_block(t, "muzzles", fmt_points(mz, ind, False))
    t = replace_block(t, "nozzles", fmt_points(nz, ind, True))
    t = re.sub(r'"silhouetteShare":\s*[-0-9.eE]+', f'"silhouetteShare": {sil}', t, 1)
    if hr is not None: t = re.sub(r'"hullRadius":\s*[-0-9.eE]+', f'"hullRadius": {hr}', t, 1)
    if SPEC[b["key"]].get("edge", 1.0) != 1.0:      # a drawing scaled off the old edge (block-doubling at ~2x): same world size
        cws = round(b["d"]["cellWorldSize"] / SPEC[b["key"]]["edge"], 3)
        t = re.sub(r'"cellWorldSize":\s*[-0-9.eE]+', f'"cellWorldSize": {cws}', t, 1)
    json.loads(t)
    p.write_text(t)
    return t


# ---------------------------------------------------------------------------------------------------------------- install
def strip(cells):
    s = Image.new("RGBA", (CELL * len(cells), CELL))
    for i, c in enumerate(cells): s.alpha_composite(c, (i * CELL, 0))
    return s


def save_everywhere(img, name, W, key):
    """Resources/Elites/<W>/<name> (in place: the .meta and its GUID stay) and every byte-identical source copy the tests compare."""
    OUT.mkdir(exist_ok=True)
    img.save(OUT / name, optimize=True)
    data = (OUT / name).read_bytes()
    targets = [RES / W / name, CODEX / W / name]
    nd = CODEX / W / "new_docks_lights~"
    if name == f"{key}_lights.png": targets.append(nd / name)
    if name == f"{key}_damaged.png": targets.append(nd / "damaged" / name)
    if name == f"{key}_lights_damaged.png": targets.append(nd / "damaged" / f"{key}_damaged_lights.png")
    done = []
    for t in targets:
        if t.exists():
            t.write_bytes(data); done.append(str(t.relative_to(ART)))
    return done


def install(b, death_strip):
    key, W = b["key"], b["W"]
    n0, n1 = b["n_main"], b["n_main"] + b["n_dmg"]
    written = []
    written += save_everywhere(strip(b["cells"][:n0]), f"{key}.png", W, key)
    if b["n_dmg"]: written += save_everywhere(strip(b["cells"][n0:n1]), f"{key}_damaged.png", W, key)
    if b["n_banks"]: written += save_everywhere(strip(b["cells"][n1:]), f"{key}_banks.png", W, key)
    written += save_everywhere(death_strip, f"{key}_death.png", W, key)
    if b["lights"]:
        written += save_everywhere(strip(b["lights"][:n0]), f"{key}_lights.png", W, key)
        if b["n_dmg"]: written += save_everywhere(strip(b["lights"][n0:n1]), f"{key}_lights_damaged.png", W, key)
        if b["n_banks"]: written += save_everywhere(strip(b["lights"][n1:]), f"{key}_lights_banks.png", W, key)
    return written


def seat_ok(c, cx, cy, rx, ry):
    """EliteEngineFireTest.Seats on one row: hull right above the flame root, the fire just under it centred on the mouth."""
    yr = int(math.floor(cy - ry * .5))
    row = c[yr - 4, int(cx - .6 * rx):int(cx + .6 * rx) + 1]
    w = max(rx, 6)
    band = c[yr + 4:yr + 10, int(cx - w):int(cx + w) + 1].astype(int)
    mx = band[..., :3].max(-1); mn = band[..., :3].min(-1)
    fire = (band[..., 3] > 200) & (mx > 130) & ((mx - mn > 50) | (mn > 215))
    ys, xs = np.nonzero(fire)
    return len(row) and (row[:, 3] > 128).mean() >= .5 and len(xs) >= 5 and abs(xs.mean() + int(cx - w) - cx) <= max(3.5, .55 * rx)


def mouth_rows(b):
    """engine_mouths.txt rows (main-strip cells only, as EliteEngineFireTest reads them): every cell a flame was drawn on, each mouth
    rounded to the whole pixel that seats (a narrow bell on a leaned cell can round off its stub)."""
    rows = []
    for ci, ms in sorted(b["seats"].items()):
        if ci >= b["n_main"]: continue
        c = np.asarray(b["cells"][ci])
        for (x, y, rx, ry) in ms:
            X, Y, RX, RY = round(x), round(y), round(rx), round(ry)
            for dx in (0, -1, 1, -2, 2, -3, 3):
                if seat_ok(c, X + dx, Y, RX, RY): X += dx; break
            else:
                print("  (no seat)", b["key"], ci, X, Y)
            rows.append(f"{b['key']} {ci} {X} {Y} {RX} {RY}")
    return rows


def write_mouths(rows_by_key):
    keep = [ln for ln in (OLD / "engine_mouths.txt").read_text().splitlines() if ln.split() and ln.split()[0] not in rows_by_key]
    new = [r for k in sorted(rows_by_key) for r in rows_by_key[k]]
    MOUTHS.write_text("\n".join(sorted(keep + new)) + "\n")
    return len(keep) + len(new)


def main(argv):
    picks = json.load(open(PICKS))["approved"]
    keys = sorted(k for k, v in picks.items() if v["group"] == "elite")
    sel = [a for a in argv if not a.startswith("--")]
    if sel: keys = [k for k in keys if any(s in k for s in sel)]
    do_install = "--no-install" not in argv
    report = {}
    mouths_rows = {}
    if MOUTHS.exists():
        for ln in MOUTHS.read_text().splitlines():
            p = ln.split()
            if p and p[0] in SPEC and p[0] not in keys: mouths_rows.setdefault(p[0], []).append(ln)
    for key in keys:
        b = build(key, picks[key]["src"])
        dth = death(b)
        mz, nz = points(b)
        sil = share(b["cells"])
        d = b["d"]
        flights = [b["cells"][i] for i in b["flight"]]
        bb = [np.nonzero(np.asarray(f)[..., 3] > 40) for f in flights]
        drawn_px = max(max(ys.max() - ys.min() + 1, xs.max() - xs.min() + 1) for ys, xs in bb)
        drawn = drawn_px / CELL * d["cellWorldSize"] / SPEC[key].get("edge", 1.0)
        hr = d["hullRadius"]; ratio = hr / (drawn * .5)
        new_hr = None
        if not .37 <= ratio <= .78:
            new_hr = round(float(np.clip(ratio, .37, .78)) * drawn * .5, 3)
        rep = dict(L=round(b["L"], 1), mouths=[tuple(round(v) for v in m) for m in b["mouths"]], muzzles=[(m["name"], m["x"], m["y"]) for m in mz],
                   nozzles=[(n["name"], n["x"], n["y"]) for n in nz], silhouetteShare=sil, old_share=d.get("silhouetteShare"),
                   drawn_u=round(drawn, 3), hr_ratio=round(ratio, 3), new_hullRadius=new_hr, glow_px=int(b["gm"].sum()))
        if do_install:
            write_def(b, mz, nz, sil, new_hr)
            rep["files"] = install(b, dth)
            if b["world"] != "space": mouths_rows[key] = mouth_rows(b)
        report[key] = rep
        build.cache = getattr(build, "cache", {}); build.cache[key] = (b, dth, mz, nz)
        print(key, json.dumps({k: v for k, v in rep.items() if k != "files"}), flush=True)
    if do_install and mouths_rows:
        print("engine_mouths rows:", write_mouths(mouths_rows))
    (HERE / "report.json").write_text(json.dumps(report, indent=1, default=str))
    return report


if __name__ == "__main__":
    main(sys.argv[1:])
