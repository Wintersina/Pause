#!/usr/bin/env python3
"""Elite engine fire, all worlds: one fixed hull + flames that leave each bell mouth (the Tide recipe, engine_fx in ../tide_elites/build_strips.py).

For every elite with a single flight cell (and the two 10-cell Frost strips whose hull swayed) this
  1. takes the bare hull (parked cell 0, un-dimmed; or flight cell 3 with the old pasted-on flame removed),
  2. finds the dark mouth of each bell,
  3. paints N flame states (different length / ragged edge per drawing and per engine) that start INSIDE the mouth,
     with the bell lip redrawn over the root, white core, ragged taper, faint halo,
  4. writes them as flight cells: single-cell strips get cells 7, 8, 9 appended (def flight = [3, 7, 8, 9]), the
     10-cell Frost strips get cells 3-6 rewritten.
Parked / lift-off / bank / damaged cells are not touched.

  python3 engine_fire.py <world|key> [--out DIR] [--src DIR]
"""
import sys, json, random, importlib.util
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
RES = HERE.parents[2] / "Resources" / "Elites"
spec = importlib.util.spec_from_file_location("tidebuild", HERE.parent / "tide_elites" / "build_strips.py")
T = importlib.util.module_from_spec(spec); spec.loader.exec_module(T)
CELL = 384

# per world defaults: flame colours (outer, mid, core); elites may override in OVERRIDE
WORLD_FLAME = {
    "frost": ((60, 130, 255), (150, 225, 255), (255, 255, 255)),
    "verdant": ((30, 170, 120), (140, 245, 200), (255, 255, 255)),
}
# length factors per flight cell (state), per engine phase offsets
STATES = [(1.00, 0), (.78, 1), (1.14, 2), (.90, 3)]


def load_def(key):
    return json.load(open(RES / "Defs" / f"{key}.json"))


def cell(a, i):
    return Image.fromarray(a[:, i * CELL:(i + 1) * CELL].copy(), "RGBA")


def old_flame_colours(a, base_alpha):
    """outer / mid colours of the elite's current flame (pixels of cell 3 that the bare hull does not have)."""
    c3 = np.array(cell(a, 3)); fl = (c3[..., 3] > 200) & (base_alpha < 8)
    px = c3[fl][:, :3].astype(int)
    if len(px) < 50: return None
    sat = px.max(1) - px.min(1); lum = px.sum(1)
    col = px[(sat > 60) & (lum < 650)]
    if len(col) < 30: return None
    order = np.argsort(col.sum(1))
    outer = tuple(int(v) for v in np.median(col[order[:max(1, len(col) // 4)]], 0))
    mid = tuple(int(v) for v in np.median(col[order[len(col) // 2:]], 0))
    return outer, mid, (255, 255, 255)


def old_flame_length(a, d, base_alpha):
    c3 = np.array(cell(a, 3)); fl = (c3[..., 3] > 200) & (base_alpha < 8)
    ys = np.where(fl.any(1))[0]
    return int(ys.max()) if len(ys) else 0


def bare_hull(a, d, mode):
    """mode 'cell0': parked cell 0 as is (identical hull to cell 3) | 'cell0u': cell 0 un-dimmed by the median ratio to cell 3."""
    c0 = np.array(cell(a, 0)).astype(np.float32); c3 = np.array(cell(a, 3)).astype(np.float32)
    if mode == "cell0u":
        both = (c0[..., 3] > 200) & (c3[..., 3] > 200)
        l0 = c0[..., :3].sum(-1)[both]; l3 = c3[..., :3].sum(-1)[both]
        ratio = np.median(l3 / np.maximum(l0, 1)); ratio = float(np.clip(ratio, 1.0, 1.6))
        c0[..., :3] = np.clip(c0[..., :3] * ratio, 0, 255)
    return Image.fromarray(c0.astype(np.uint8), "RGBA")


def strip_flame(a, mouths, dark=(14, 22, 38)):
    """Mode 'strip': flight cell 3 with its old flame taken out. In each mouth zone, flame-coloured pixels (saturated blue / white) go:
    inside the mouth ellipse they become the bell's dark floor, outside it transparent."""
    c = np.array(cell(a, 3)).astype(int)
    out = c.copy(); yy, xx = np.indices((CELL, CELL))
    sat = c[..., :3].max(-1) - c[..., :3].min(-1); mn = c[..., :3].min(-1)
    flameish = (c[..., 3] > 0) & ((sat > 110) | (mn > 215))
    for cx, cy, rx, ry in mouths:
        zone = (np.abs(xx - cx) <= rx + 5) & (yy >= cy - ry - 2) & flameish
        inside = ((xx - cx) / max(rx, 1)) ** 2 + ((yy - cy) / max(ry, 1)) ** 2 <= 1.0
        out[zone & inside] = (*dark, 255)
        out[zone & ~inside & (yy > cy + ry * .5)] = (0, 0, 0, 0)
        # the flame is gone below the mouth line anywhere in the zone
        below = (np.abs(xx - cx) <= rx + 5) & (yy > cy + ry) & (c[..., 3] > 0) & flameish
        out[below] = (0, 0, 0, 0)
    return Image.fromarray(out.astype(np.uint8), "RGBA")


def mouths_for(base, d, manual=None):
    seeds = [(int(n["x"]), int(n["y"])) for n in d["nozzles"]]
    return T.find_openings(base, seeds, manual or {})


def states(base, mouths, cols, length, seed0=10):
    out = []
    n = len(mouths)
    for k, (f, ph) in enumerate(STATES):
        lens = []
        for i in range(n):
            wob = [0, .12, -.1, .06, -.14][(i + ph) % 5]
            lens.append((length * (f + wob), .92 + .08 * ((i + ph) % 2)))
        out.append(T.engine_fx(base, mouths, lens, cols, seed0 + 7 * k + 3 * ph, ragged=.15 * (k % 2)))
    return out


def build(key, src_dir, out_dir, mode, length=None, cols=None, manual=None, keep_cell3=False):
    d = load_def(key); w = key.split("_")[0]
    a = np.array(Image.open(src_dir / w.capitalize() / f"{key}.png").convert("RGBA"))
    if key not in BIG and a.shape[1] > 7 * CELL: a = a[:, :7 * CELL]      # re-runs: only the original 7 cells
    if mode == "strip":
        mouths = [manual[i] for i in range(len(d["nozzles"]))]
        base = strip_flame(a, mouths)
    else:
        base = bare_hull(a, d, mode)
        mouths = mouths_for(base, d, manual)
    base_alpha = np.array(base)[..., 3]
    cols = cols or (WORLD_FLAME.get(w) if w in FORCE_PALETTE else None) or old_flame_colours(a, base_alpha) or WORLD_FLAME[w]
    ylow = old_flame_length(a, d, base_alpha) if mode != "strip" else int(np.where((np.array(cell(a, 3))[..., 3] > 200).any(1))[0].max())
    # length in 192-cell units of engine_fx: its flame is L*K px long from the mouth
    my = max(m[1] for m in mouths)
    L = length or max(14, (ylow - my) * .9 / T.K)
    cs = states(base, mouths, cols, L)
    for c in cs:                                                       # keep the 6 px framing margin: no flame pixel in the bottom rows
        c.paste((0, 0, 0, 0), (0, CELL - 6, CELL, CELL))
    build.last = dict(L=L)
    return a, base, mouths, cols, cs


def match_shift(R, C, m, rng=14):
    """(dx, dy) that carries the reference hull R onto cell C around mouth m: matched on the bell body above the flame."""
    cx, cy, rx, ry = m
    x0, x1 = int(cx - rx - 10), int(cx + rx + 10); y0, y1 = int(cy - 26), int(cy - max(5, ry * .6))
    ref = R[y0:y1, x0:x1].astype(int)
    best = (1e18, 0, 0)
    for dy in range(-rng, rng + 1):
        for dx in range(-rng, rng + 1):
            cur = C[y0 + dy:y1 + dy, x0 + dx:x1 + dx].astype(int)
            if cur.shape != ref.shape: continue
            e = np.abs(cur - ref).sum() + 400 * (np.abs(cur[..., 3] - ref[..., 3]) > 100).sum()
            if e < best[0]: best = (e, dx, dy)
    return best[1], best[2]


def old_flame_on(C, m, dx, dy):
    cx, cy, rx, ry = m
    x0, x1 = int(cx + dx - rx - 2), int(cx + dx + rx + 3); y = int(cy + dy + ry + 4)
    z = C[y:y + 40, x0:x1]
    return int((z[..., 3] > 200).sum())


def bank_cells(a, d, base, mouths, cols, L):
    """Bank (steering) and damaged cells: the old pasted-on flames come out, the bell zones are re-cut from the fixed hull, the new flames go in.
    Damaged: only the engine that still burned in the old cell sputters (short, ragged), the rest stay dark."""
    c = d["cells"]; R = np.array(base)
    out = {}
    for name, idx in (("bankLeft", c.get("bankLeft")), ("bankRight", c.get("bankRight")), ("damaged", c.get("damaged"))):
        if idx is None or idx < 0: continue
        C = np.array(cell(a, idx)); X = C.copy(); yy, xx = np.indices((CELL, CELL))
        sh = []; burning = []
        for m in mouths:
            dx, dy = match_shift(R, C, m)
            sh.append((dx, dy)); burning.append(old_flame_on(C, m, dx, dy) > 150)
        for m, (dx, dy) in zip(mouths, sh):
            cx, cy, rx, ry = m
            zone = (np.abs(xx - (cx + dx)) <= rx + 8) & (yy >= cy + dy - ry - 1) & (yy < CELL)
            src = np.zeros_like(R)
            sy, sx = np.clip(yy - dy, 0, CELL - 1), np.clip(xx - dx, 0, CELL - 1)
            src = R[sy, sx]
            X[zone] = src[zone]
        mm = [(cx + dx, cy + dy, rx, ry) for (cx, cy, rx, ry), (dx, dy) in zip(mouths, sh)]
        base_c = Image.fromarray(X, "RGBA")
        if name == "damaged":
            keep = [i for i, b in enumerate(burning) if b] or [0]
            lens = [((L * .45, .8) if i in keep else (0, 0)) for i in range(len(mm))]
            sel = [(m, l) for m, l in zip(mm, lens) if l[0] > 0]
            im = T.engine_fx(base_c, [m for m, _ in sel], [l for _, l in sel], cols, 99, ragged=.6) if sel else base_c
        else:
            lens = [(L * (1.08 + .06 * (i % 2)), .96 + .06 * (i % 2)) for i in range(len(mm))]
            im = T.engine_fx(base_c, mm, lens, cols, 40 + idx)
        im = im.copy(); im.paste((0, 0, 0, 0), (0, CELL - 6, CELL, CELL))
        out[idx] = im
    return out


def write(key, a, cs, out_dir, big):
    N = a.shape[1] // CELL
    if big:
        strip = a.copy()
        for i, c in enumerate(cs): strip[:, (3 + i) * CELL:(4 + i) * CELL] = np.array(c)
    else:
        strip = np.concatenate([a] + [np.array(c) for c in cs[1:]], 1)   # cell 3 replaced below
        strip[:, 3 * CELL:4 * CELL] = np.array(cs[0])
    w = key.split("_")[0]
    p = out_dir / w.capitalize(); p.mkdir(parents=True, exist_ok=True)
    Image.fromarray(strip).save(p / f"{key}.png")


# hand-set bell mouths (cx, cy, rx, ry) where the dark-mouth detector grabs the whole bell
MANUAL = {
    "desert_elite_glass_cannon": {0: (152, 288, 21, 9), 1: (234, 288, 21, 9)},
    "frost_elite_cryo_siren": {0: (183, 295, 9, 3), 1: (203, 296, 9, 3)},
    "frost_elite_rimebreaker": {0: (166, 229, 13, 6), 1: (220, 229, 13, 6)},
    "frost_elite_floe_harrower": {0: (167, 287, 8, 4), 1: (219, 287, 8, 4)},
    "frost_elite_glacier_tender": {0: (154, 316, 9, 4), 1: (232, 316, 9, 4)},
    "frost_elite_whiteout_sentinel": {0: (165, 304, 12, 8), 1: (219, 304, 12, 8)},
}
# which hull the flame is painted on: Desert's parked cell 0 IS the hull; elsewhere un-dim it
MODE = {"desert": "cell0", "frost_elite_cryo_siren": "strip", "frost_elite_rimebreaker": "strip"}
FORCE_PALETTE = {"frost", "verdant"}   # their own cold / green fire, not whatever tiny glow the old strip had
LENGTH = {"frost_elite_floe_harrower": 24, "frost_elite_glacier_tender": 22, "frost_elite_whiteout_sentinel": 24}   # key -> flame length in engine_fx units (px = L * 2)
BIG = {"frost_elite_cryo_siren", "frost_elite_rimebreaker"}


def run(world, out_dir=None, keys=None, defs=True):
    import glob
    out_dir = out_dir or RES
    allk = sorted(Path(f).stem for f in glob.glob(str(RES / "Defs" / f"{world}_elite_*.json")))
    done = []
    for k in keys or allk:
        a, base, mouths, cols, cs = build(k, RES, out_dir, MODE.get(k, MODE.get(world, "cell0u")), length=LENGTH.get(k), manual=MANUAL.get(k))
        write(k, a, cs, out_dir, k in BIG)
        banks = bank_cells(a, load_def(k), base, mouths, cols, build.last["L"])
        p0 = out_dir / k.split("_")[0].capitalize() / f"{k}.png"
        strip = np.array(Image.open(p0).convert("RGBA"))
        for idx, im in banks.items(): strip[:, idx * CELL:(idx + 1) * CELL] = np.array(im)
        Image.fromarray(strip).save(p0)
        if defs and k not in BIG:
            p = RES / "Defs" / f"{k}.json"; s = p.read_text()
            if '"flight": [\n      3\n    ]' in s:
                p.write_text(s.replace('"flight": [\n      3\n    ]', '"flight": [\n      3,\n      7,\n      8,\n      9\n    ]', 1))
        done.append(k)
        print(k, [tuple(round(v) for v in m) for m in mouths])
    return done


if __name__ == "__main__":
    run(sys.argv[1], Path(sys.argv[2]) if len(sys.argv) > 2 else None)
