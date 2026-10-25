#!/usr/bin/env python3
"""Space elites: the bank-left / bank-right cells (Oct 25: every ship-style elite banks into sideways moves; Space's four had none).

Codex drew each ship's BANK-LEFT master (masters/bank/<name>_left.png: the same ship rolled ~20 degrees left side down, nose up, flat
#00FF40 key, EMPTY engine bells: prompts/bank_left.md). Bank right is its mirror (the ships are left-right symmetric). Each master is keyed
and scaled to the hover hull's exact height (build_strips.hull's scale), placed at the same top with its bbox centre on the hover hull's
(the pivot does not swing), and given the burn exactly like the hover cells: the bell mouths are found on the bank drawing
(build_strips.find_openings, seeded from the hover mouths mapped across the bbox) and a plume is drawn from each.

Each bank has its own 3-drawing burn loop (the plumes keep burning while it slides; each engine on its own phase). The six cells are a
second companion strip <name>_banks.png (EliteArt appends it after the hull strip and its _damaged companion): cells 17..19 = bank left,
20..22 = bank right (defs: bankLeft 17 / bankLeftLoop [17,18,19], bankRight 20 / bankRightLoop [20,21,22]). The main strip stays 12 cells and
the _damaged companion 5 (a texture wider than 4096 px is downscaled on import). The lights builder (Elite/Space/new_docks_lights~/build_art.py)
gives them their lamps the same way (<name>_lights_banks.png).

Per-cell nozzle offsets (the plume mount follows the drawn bell on every cell) are measured by
.claude/skills/add-elite-ship/scripts/measure_frame_nozzles.py.

  python3 build_banks.py [--out <dir>] [name ...]   default out: Resources/Elites/Space
  python3 build_banks.py --mouths                     print each ship's mouths per bank cell (json), nothing written
"""
import sys, json
from pathlib import Path
import numpy as np
from PIL import Image, ImageOps

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_strips as bs

HERE = bs.HERE
CELL = bs.CELL
BANK_LEFT, BANK_RIGHT = 17, 20     # first cell of each bank loop (strip + _damaged companion = 17 cells before them)
# the bank loops' drawings: burn-cycle phases (build_strips.LENGTHS / WIDTHS), each engine offset by 3 like the hover cycle
BANK_BURN = (0, 3, 6)
# a banked bell's plume is at least this share of the same engine's hover plume width
MIN_PLUME = .85


# bells the dark-pixel search cannot isolate on a bank drawing: {engine index (hover order, left to right): (cx, cy, rx, ry)} on bank LEFT
BANK_MOUTHS = {}


def bell_mouths(cell, n, bottom, manual):
    """The n engine-bell mouths on a bank drawing: the n biggest dark (eroded) blobs in the hull's bottom band, left to right."""
    arr = np.array(cell).astype(int)
    lum = arr[..., 0] * .3 + arr[..., 1] * .59 + arr[..., 2] * .11
    e = bs.erode((lum < 46) & (arr[..., 3] > 200), 2)
    e[:bottom - 48] = False
    seen = np.zeros_like(e)
    blobs = []
    for y, x in zip(*np.nonzero(e)):
        if seen[y, x]:
            continue
        comp = bs._component(e, (y, x))
        seen |= comp.astype(bool)
        yy, xx = np.nonzero(comp)
        blobs.append((len(yy), xx.min() - 2, xx.max() + 2, yy.min() - 2, yy.max() + 2))
    blobs = sorted(sorted(blobs, reverse=True)[:n], key=lambda b: b[1])
    out = [((x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2, (y1 - y0) / 2) for _, x0, x1, y0, y1 in blobs]
    for i, m in manual.items():
        out[i] = m
    if len(out) != n:
        raise RuntimeError(f"found {len(out)} bells, want {n}")
    return out


def bank_art(name, height):
    art = bs.key_out(HERE / "masters" / "bank" / f"{name}_left.png")
    art = art.crop(art.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox())
    s = height / art.height
    art = art.resize((max(1, round(art.width * s)), height), Image.Resampling.LANCZOS)
    art.putalpha(art.getchannel("A").point(lambda v: 255 if v >= 128 else 0))
    return art


def layout(name):
    """-> hover (hull_cell, ox, oy, w, h, mouths), bank-left bare cell, its mouths (hover engine order), bank-right bare cell + mouths."""
    cfg = bs.SHIPS[name]
    art = bs.hull(name)
    w, h = art.size
    ox, oy = (CELL - w) // 2, bs.TOP
    hull_cell = Image.new("RGBA", (CELL, CELL)); hull_cell.alpha_composite(art, (ox, oy))
    seeds = [(ox + round(nx * w), oy + h - 14 + dy) for nx, dy in zip(cfg["nx"], cfg.get("dy", (0,) * len(cfg["nx"])))]
    mouths = bs.find_openings(hull_cell, seeds, cfg.get("mouths", {}))

    ba = bank_art(name, h)
    bw = ba.width
    bx = round(ox + w / 2 - bw / 2)
    left = Image.new("RGBA", (CELL, CELL)); left.alpha_composite(ba, (bx, oy))
    # seeds: each hover mouth carried across the bbox (x by share of the width), then snapped to the dark bell on the bank drawing
    lm = bell_mouths(left, len(mouths), oy + h, BANK_MOUTHS.get(name, {}))
    # a bell seen edge-on reads narrower, but its plume keeps most of its body (a too-thin plume collapses into a white core column)
    lm = [(cx, cy, max(rx, MIN_PLUME * hm[2]), ry) for (cx, cy, rx, ry), hm in zip(lm, mouths)]
    right = ImageOps.mirror(left)
    # mirrored: physical engine i of the right bank is the mirror of the left bank's engine n-1-i
    n = len(lm)
    rm = [(CELL - 1 - lm[n - 1 - i][0], lm[n - 1 - i][1], lm[n - 1 - i][2], lm[n - 1 - i][3]) for i in range(n)]
    return (hull_cell, ox, oy, w, h, mouths), left, lm, right, rm


def burn(cell, mouths, cfg, phase, seed):
    n = len(mouths)
    gs = [(phase + 3 * i) % bs.BURN for i in range(n)]
    return bs.engine_cell(cell, mouths, cfg["flame"], [bs.LENGTHS[g] for g in gs], [bs.WIDTHS[g] for g in gs], seed)


def build(name, out_dir):
    cfg = bs.SHIPS[name]
    _, left, lm, right, rm = layout(name)
    cells = [burn(left, lm, cfg, ph, 70 + i) for i, ph in enumerate(BANK_BURN)] + \
            [burn(right, rm, cfg, ph, 80 + i) for i, ph in enumerate(BANK_BURN)]
    p = out_dir / f"space_elite_{name}_banks.png"
    st = Image.new("RGBA", (CELL * len(cells), CELL))
    for i, c in enumerate(cells):
        st.alpha_composite(c, (CELL * i, 0))
    st.save(p)
    print(name, "banks ->", p.name, json.dumps(dict(left=[tuple(round(v, 1) for v in m) for m in lm], right=[tuple(round(v, 1) for v in m) for m in rm])))


if __name__ == "__main__":
    args = sys.argv[1:]
    if "--mouths" in args:
        for n in bs.SHIPS:
            hv, _, lm, _, rm = layout(n)
            print(n, json.dumps(dict(hover=hv[5], left=lm, right=rm)))
        sys.exit(0)
    out = bs.RES
    if "--out" in args:
        i = args.index("--out"); out = Path(args[i + 1]); del args[i:i + 2]
    for n in (args or list(bs.SHIPS)):
        if (HERE / "masters" / "bank" / f"{n}_left.png").exists():
            build(n, out)
