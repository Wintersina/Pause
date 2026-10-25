#!/usr/bin/env python3
"""Per-cell nozzle offsets for an elite: where each engine nozzle (the def's `nozzles`, measured on the hover cell cells.flight[0]) sits
on every other cell of the strip, so the engine glow / plume mount follows the drawn bell when the hull banks, lifts off or burns.

Each nozzle is tracked by template matching: a patch of the HULL around the nozzle (the bell and the plating above it; the rows below the
mouth hold the animated flame, so they are left out) is searched for in each cell within +-SEARCH px; the best match (alpha-weighted
squared difference) gives that cell's (dx, dy) in cell px (x right, y down). Cells where the match is poor (the bare parked hull still
matches; a death/blank cell would not) fall back to (0, 0).

  python3 measure_frame_nozzles.py <Defs/key.json> [--write] [--sheet out.png]
    --write   stores ElitePoint.offsets (dx, dy per cell, the hull strip then its _damaged companion) in the def
    --sheet   a review image: each cell with every nozzle's tracked point
"""
import sys, json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

SEARCH = 14       # banks lean a few degrees: the bells move a few px (Space's redrawn banks: build_banks.py writes theirs)
UP, DOWN, HALF = 22, 3, 14       # patch rows above / below the nozzle point, half width


def cells_of(strip_path):
    im = Image.open(strip_path).convert("RGBA")
    h = im.size[1]
    cells = [im.crop((i * h, 0, i * h + h, h)) for i in range(im.size[0] // h)]
    for suffix in ("_damaged", "_banks"):                 # the companions EliteArt appends, in its order (EliteArt.Companions)
        comp = strip_path.with_name(strip_path.stem + suffix + ".png")
        if comp.exists():
            c = Image.open(comp).convert("RGBA")
            cells += [c.crop((i * h, 0, i * h + h, h)) for i in range(c.size[0] // h)]
    return cells


def track(ref, cell, x, y):
    """-> (dx, dy, score) of the patch round (x, y) of ref inside cell."""
    r = np.asarray(ref).astype(np.float32); c = np.asarray(cell).astype(np.float32)
    S = r.shape[0]
    x, y = int(round(x)), int(round(y))
    y0, y1, x0, x1 = max(0, y - UP), min(S, y + DOWN), max(0, x - HALF), min(S, x + HALF)
    p = r[y0:y1, x0:x1]
    w = p[..., 3:4] / 255.0
    if w.sum() < 20:
        return 0, 0, 0.0
    best = None
    for dy in range(-SEARCH, SEARCH + 1):
        if y0 + dy < 0 or y1 + dy > S:
            continue
        for dx in range(-SEARCH, SEARCH + 1):
            if x0 + dx < 0 or x1 + dx > S:
                continue
            q = c[y0 + dy:y1 + dy, x0 + dx:x1 + dx]
            d = (((p[..., :3] - q[..., :3]) ** 2).sum(2, keepdims=True) * w).sum() + ((p[..., 3:4] - q[..., 3:4]) ** 2).sum() * .5
            d += (dx * dx + dy * dy) * 40.0        # (a gentle pull to the nearest of two look-alike bells)
            if best is None or d < best[0]:
                best = (d, dx, dy)
    # score: how alike the best match is (1 = identical), relative to the patch's own energy
    energy = ((p[..., :3] ** 2).sum(2, keepdims=True) * w).sum() + 1.0
    return best[1], best[2], max(0.0, 1.0 - best[0] / energy)


def seated_mouths():
    """Art/Enemies/src~/elite_engine_fire/engine_mouths.txt (written by engine_fire.py): the bell mouth every drawn flame was seated on,
    per elite and cell -- exact where it covers a cell, so it wins over tracking."""
    import collections
    path = Path(__file__).resolve().parents[4] / "Pause/Assets/Art/Enemies/src~/elite_engine_fire/engine_mouths.txt"
    out = collections.defaultdict(list)
    if path.exists():
        for ln in open(path):
            p = ln.split()
            if len(p) == 6:
                out[(p[0], int(p[1]))].append(tuple(map(float, p[2:4])))
    return out


def tracked_cells(c):
    """The cells drawn off a different pose than the hover cell: banks (and their loops), parked, pilot lights, lift-off. The flight loop
    and the damaged burn loop are the hover hull itself (same registration, every pipeline builds them on it): offset 0."""
    out = set()
    for k in ("bankLeft", "bankRight", "parked", "parkedIdle", "liftoff"):
        if c.get(k, -1) >= 0: out.add(c[k])
    for k in ("bankLeftLoop", "bankRightLoop"):
        out.update(c.get(k) or [])
    return out


def space_bank_offsets(d, n_cells):
    """Space's redrawn banks: the exact bell mouths the plumes were drawn from (src~/space_elites_engine_burn/build_banks.py)."""
    root = Path(__file__).resolve().parents[4] / "Pause/Assets/Art/Enemies/src~/space_elites_engine_burn"
    sys.path.insert(0, str(root))
    import build_banks
    name = d["key"].replace("space_elite_", "")
    hv, _, lm, _, rm = build_banks.layout(name)
    hover = hv[5]
    c = d["cells"]
    out = []
    for i, nz in enumerate(d["nozzles"]):
        offs = [0.0] * (2 * n_cells)
        for loop, mouths in ((c.get("bankLeftLoop") or [c["bankLeft"]], lm), (c.get("bankRightLoop") or [c["bankRight"]], rm)):
            for cell in loop:
                offs[2 * cell] = float(round(mouths[i][0] - hover[i][0]))
                offs[2 * cell + 1] = float(round(mouths[i][1] - hover[i][1]))
        out.append(offs)
    return out


def mouth_shift(mouths, d, cell, nz):
    """The engine's shift on `cell` from engine_mouths.txt: its hover mouth (the one nearest the nozzle point, within 15 px) and the same
    engine's mouth on that cell. None: not covered."""
    hov = mouths.get((d["key"], d["cells"]["flight"][0]))
    on = mouths.get((d["key"], cell))
    if not hov or not on or len(on) != len(hov):
        return None
    i = min(range(len(hov)), key=lambda j: (hov[j][0] - nz["x"]) ** 2 + (hov[j][1] - nz["y"]) ** 2)
    if (hov[i][0] - nz["x"]) ** 2 + (hov[i][1] - nz["y"]) ** 2 > 15 ** 2:
        return None
    return round(on[i][0] - hov[i][0]), round(on[i][1] - hov[i][1])


def measure(def_path):
    d = json.load(open(def_path))
    world = d["world"].capitalize()
    strip = Path(def_path).resolve().parents[1] / world / f"{d['key']}.png"
    cells = cells_of(strip)
    ref = cells[d["cells"]["flight"][0]]
    track_set = tracked_cells(d["cells"])
    mouths = seated_mouths()
    space_banks = space_bank_offsets(d, len(cells)) if d["world"] == "space" and d["cells"].get("bankLeft", -1) >= 0 else None
    bank_set = {d["cells"].get("bankLeft", -1), d["cells"].get("bankRight", -1)} | set(d["cells"].get("bankLeftLoop") or []) | set(d["cells"].get("bankRightLoop") or [])
    out = []
    for k, nz in enumerate(d["nozzles"]):
        offs = []
        for ci, cell in enumerate(cells):
            dx = dy = 0
            if space_banks is not None and ci in bank_set:
                dx, dy = space_banks[k][2 * ci], space_banks[k][2 * ci + 1]
            elif ci in track_set:
                seated = mouth_shift(mouths, d, ci, nz)
                if seated is not None:
                    dx, dy = seated
                else:
                    dx, dy, score = track(ref, cell, nz["x"], nz["y"])
                    if score < .55:
                        dx, dy = 0, 0
            offs += [float(dx), float(dy)]
        out.append(offs)
    return d, cells, out


def write_offsets(text, offs):
    """The def's text with each nozzle's "offsets" set (an existing one replaced), everything else left exactly as written
    (the defs are hand-formatted: a json round trip would reflow them)."""
    import re
    i = text.index('"nozzles"')
    i = text.index("[", i)
    depth, j, objs, start = 0, i, [], None
    while True:
        ch = text[j]
        if ch in "[{":
            depth += 1
            if ch == "{" and depth == 2: start = j
        elif ch in "]}":
            depth -= 1
            if ch == "}" and depth == 1: objs.append((start, j))
            if depth == 0: break
        j += 1
    assert len(objs) == len(offs), (len(objs), len(offs))
    out, last = [], 0
    for (s0, s1), o in zip(objs, offs):
        body = re.sub(r',\s*"offsets"\s*:\s*\[[^\]]*\]', "", text[s0:s1])
        vals = ", ".join(str(int(v)) for v in o)
        tail = body[len(body.rstrip()):]                       # the whitespace before the closing brace, kept as written
        if "\n" in body:
            ind = tail.split("\n")[-1] + "  "
            out.append(text[last:s0]); out.append(body.rstrip() + ',\n' + ind + '"offsets": [' + vals + "]" + tail)
        else:
            out.append(text[last:s0]); out.append(body.rstrip() + ', "offsets": [' + vals + "]" + tail)
        last = s1
    out.append(text[last:])
    t = "".join(out)
    json.loads(t)
    return t


def sheet(d, cells, offs, path, only=None):
    idx = only if only is not None else list(range(len(cells)))
    S = cells[0].size[0]
    im = Image.new("RGBA", (S * len(idx), S), (30, 24, 60, 255))
    dr = ImageDraw.Draw(im)
    for j, i in enumerate(idx):
        im.alpha_composite(cells[i], (j * S, 0))
        for k, nz in enumerate(d["nozzles"]):
            x = nz["x"] + offs[k][2 * i] + j * S; y = nz["y"] + offs[k][2 * i + 1]
            dr.ellipse((x - 6, y - 6, x + 6, y + 6), outline=(0, 255, 255, 255), width=2)
        dr.text((j * S + 4, 4), str(i), fill=(255, 255, 0, 255))
    im.save(path)


if __name__ == "__main__":
    a = sys.argv[1:]
    d, cells, offs = measure(a[0])
    for k, nz in enumerate(d["nozzles"]):
        print(nz.get("name"), [(int(offs[k][2 * i]), int(offs[k][2 * i + 1])) for i in range(len(cells))])
    if "--sheet" in a:
        sheet(d, cells, offs, a[a.index("--sheet") + 1])
    if "--write" in a:
        text = write_offsets(open(a[0]).read(), offs)     # (read in full before the file is reopened for writing)
        open(a[0], "w").write(text)
