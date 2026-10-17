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
    base = bare_hull(a, d, mode)
    base_alpha = np.array(base)[..., 3]
    mouths = mouths_for(base, d, manual)
    cols = cols or old_flame_colours(a, base_alpha) or WORLD_FLAME[w]
    ylow = old_flame_length(a, d, base_alpha)
    # length in 192-cell units of engine_fx: its flame is L*K px long from the mouth
    my = max(m[1] for m in mouths)
    L = length or max(14, (ylow - my) * .9 / T.K)
    cs = states(base, mouths, cols, L)
    return a, base, mouths, cols, cs


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
}
# which hull the flame is painted on: Desert's parked cell 0 IS the hull; elsewhere un-dim it
MODE = {"desert": "cell0"}
BIG = {"frost_elite_cryo_siren", "frost_elite_rimebreaker"}


def run(world, out_dir=None, keys=None, defs=True):
    import glob
    out_dir = out_dir or RES
    allk = sorted(Path(f).stem for f in glob.glob(str(RES / "Defs" / f"{world}_elite_*.json")))
    done = []
    for k in keys or allk:
        a, base, mouths, cols, cs = build(k, RES, out_dir, MODE.get(world, "cell0u"), manual=MANUAL.get(k))
        write(k, a, cs, out_dir, k in BIG)
        if defs and k not in BIG:
            p = RES / "Defs" / f"{k}.json"; s = p.read_text()
            if '"flight": [\n      3\n    ]' in s:
                p.write_text(s.replace('"flight": [\n      3\n    ]', '"flight": [\n      3,\n      7,\n      8,\n      9\n    ]', 1))
        done.append(k)
        print(k, [tuple(round(v) for v in m) for m in mouths])
    return done


if __name__ == "__main__":
    run(sys.argv[1], Path(sys.argv[2]) if len(sys.argv) > 2 else None)
