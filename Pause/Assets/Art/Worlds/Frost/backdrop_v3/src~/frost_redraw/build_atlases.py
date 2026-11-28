#!/usr/bin/env python3
"""Frost overlay atlases from the approved PixelLab sheets (pixel art, hard 0 / 255 alpha).

    python3 build_atlases.py [--out <Resources/Worlds/Frost/Backdrop3>] [atlas ...]

Sources: sources/atlas_<name>.png (the approved picks, see PICKS). The old sheets and JSON are in old/.
Every atlas keeps its size, its cell grid, its frame names and counts, and its .meta: the JSON is rewritten with the
same cell rects (the director and FrostAmbientCatalog address 256 px cells: loop feet at (128, 236), the aurora's
centre (128, 128)), except wx_ambient.json, which holds each cell's tight bounds and is re-measured.

Per piece: cut from its cell of the pick (grid 64 px; docks rows 0-1 in sixths), stray bits of a neighbour that
cross into the cell are dropped, scaled up NEAREST by an integer (pixel art stays crisp: x4 is the ground tiles' own
pixel size; small lights x2, smoke and steam x2, flames x3, aurora x3, the weather sheets x4 / x8), placed in its
256 cell (centred, or standing on the loop's foot), graded toward the OLD sequence's measured tone (mean HSV value
of its opaque texels; hue kept) and alpha (the old sequence's peak alpha: the art keeps its hard edges, the old
translucency comes from that level), and at the end every visible texel gets the per-texel grain of the tiles
(RGB -1 / 0 / +1, seeded) so no 256 cell is a nearest-neighbour block upscale for BackdropResolutionTest.

HD sources (sources_hd/atlas_<name>.png, preferred when present): the same picks redrawn at twice the detail (512 x 512,
4 x 4 cells of 128 px, the same piece in the same cell), cut from 128 px cells and scaled x2 instead of x4 (a 2 px
source pixel, as the HD ground tiles), every other step the same; the lit lamp pixels stay 4 x 4 (two HD source pixels)
on the 2 px grid. landmarks and sites take them whole; docks only rows 2-3 (the rig and the icebreaker: x2, each
piece found by its own bounds, the HD sheet is shifted a few px off the cell grid), rows 0-1 stay on sources/ (the HD
animated door / hatch drawings do not match frame to frame).

Frames BUILT BY SCRIPT (no matching sequence in the pick), see SCRIPTED: printed by the build.
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
OUT = HERE.parents[4] / "Backgrounds/Resources/Worlds/Frost/Backdrop3"
OLD = HERE / "old"
PICKS = {   # atlas -> PixelLab pick (pl/out2/bd_frost_<atlas>/<pick>.png), copied to sources/atlas_<atlas>.png
    "aurora": "r1_0", "beacons": "r1_1", "docks": "r1_0", "fire_lights": "r1_0", "landmarks": "r1_1", "sites": "r1_1",
    "smoke": "r1_0", "steam": "r1_0", "weather": "r1_0", "wx_ambient": "r1_0", "wx_hazard": "r1_0",
}
SCRIPTED = []


def src(name):
    return np.asarray(Image.open(HERE / "sources" / f"atlas_{name}.png").convert("RGBA")).copy()


def src_hd(name):
    """The HD pick (128 px cells) or None."""
    p = HERE / "sources_hd" / f"atlas_{name}.png"
    return np.asarray(Image.open(p).convert("RGBA")).copy() if p.exists() else None


def pick(name):
    """(sheet, cell px, upscale): the HD pick at x2 when there is one, else the 64 px pick at x4 (the same 256 cell)."""
    a = src_hd(name)
    return (a, 128, 2) if a is not None else (src(name), 64, 4)


def old_json(name):
    return json.loads((OLD / f"{name}.json").read_text())


# ---------------------------------------------------------------- pieces
def cut(a, x0, y0, w, h, keep_all=False):
    """A cell of the pick; components that touch the cell's border and are small next to the main one are a
    neighbour's stray bits and are dropped."""
    c = a[y0:y0 + h, x0:x0 + w].copy()
    m = c[..., 3] > 0
    if keep_all or not m.any():
        return c
    lab, n = ndimage.label(ndimage.binary_dilation(m, iterations=1), structure=np.ones((3, 3)))
    lab = lab * m
    sizes = np.bincount(lab.ravel(), minlength=n + 1)
    main = sizes[1:].argmax() + 1
    for i in range(1, n + 1):
        if i == main:
            continue
        yy, xx = np.nonzero(lab == i)
        if len(yy) == 0:
            continue
        border = yy.min() == 0 or xx.min() == 0 or yy.max() == h - 1 or xx.max() == w - 1
        if border and sizes[i] < sizes[main] * .25:
            c[lab == i] = 0
    return c


def grid(a, r, c, n=64):
    return cut(a, c * n, r * n, n, n)


def tight(p):
    m = p[..., 3] > 0
    if not m.any():
        return np.zeros((1, 1, 4), np.uint8)          # an empty drawing (the puff's last, gone frame)
    ys, xs = np.nonzero(m)
    return p[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def up(p, k):
    return np.repeat(np.repeat(p, k, 0), k, 1)


def place(p, size=(256, 256), mode="centre", foot=236, centre=(128, 128)):
    """Paste tight piece p into an empty cell: centred on `centre`, or standing with its bottom row at `foot`."""
    H, W = size[1], size[0]
    cell = np.zeros((H, W, 4), np.uint8)
    h, w = p.shape[:2]
    if mode == "foot":
        y0 = foot - h + 1
        x0 = int(round(centre[0] - w / 2))
    else:
        y0 = int(round(centre[1] - h / 2))
        x0 = int(round(centre[0] - w / 2))
    if y0 < 0 or x0 < 0 or y0 + h > H or x0 + w > W:
        raise ValueError(f"piece {w}x{h} does not fit at {x0},{y0} in {W}x{H}")
    region = cell[y0:y0 + h, x0:x0 + w]
    on = p[..., 3] > 0
    region[on] = p[on]
    return cell


# ---------------------------------------------------------------- tone
def value(rgb):
    return rgb.max(-1)


def tone(cells, v_target, a_max, alpha_by_value=False):
    """Grade a sequence's cells together: mean value of the opaque texels -> v_target (0..255; hue kept), alpha
    255 -> a_max (alpha_by_value: the alpha follows the texel's value, dark outlines fainter: the aurora)."""
    allv = np.concatenate([value(c[..., :3].astype(np.float64))[c[..., 3] > 0] for c in cells]) / 255
    mean = float(allv.mean())
    t = v_target / 255
    out = []
    for c in cells:
        f = c[..., :3].astype(np.float64) / 255
        if t <= mean:
            f = f * (t / mean)
        else:
            f = 1 - (1 - f) * ((1 - t) / (1 - mean))
        o = c.copy()
        on = c[..., 3] > 0
        o[..., :3] = np.clip(np.rint(f * 255), 0, 255).astype(np.uint8)
        if alpha_by_value:
            v = value(f)
            vmax = max(float(v[on].max()), 1e-3) if on.any() else 1
            al = np.clip(np.rint(a_max * (v / vmax)), 1, 255)
        else:
            al = np.full(c.shape[:2], a_max, np.float64)
        o[..., 3] = np.where(on, (c[..., 3] / 255 * al), 0).astype(np.uint8)
        o[~on] = 0
        out.append(o)
    return out


def no_red(cell):
    """No saturated red / pink / magenta in Frost (as the tiles): such texels turn amber, value kept."""
    rgb = cell[..., :3].astype(np.float64)
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = mx - mn
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    dd = np.where(d > 0, d, 1)
    hue = np.where(mx == r, ((g - b) / dd) % 6, np.where(mx == g, (b - r) / dd + 2, (r - g) / dd + 4)) * 60
    sat = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    bad = (cell[..., 3] > 0) & (d > 0) & (sat > .35) & ((hue >= 300) | (hue < 20))
    if bad.any():
        cell = cell.copy()
        cell[..., 0] = np.where(bad, mx, r)
        cell[..., 1] = np.where(bad, mn + d * 32 / 60, g)
        cell[..., 2] = np.where(bad, mn, b)
    return cell


def grain(sheet, seed):
    rng = np.random.default_rng(seed)
    g = rng.integers(-1, 2, size=sheet.shape[:2]).astype(np.int16)
    s = sheet.astype(np.int16)
    on = s[..., 3] > 0
    s[..., :3] += (g * on)[..., None]
    s = np.clip(s, 0, 255).astype(np.uint8)
    s[~on] = 0
    return s


# ---------------------------------------------------------------- sheets
def sheet_from_cells(name, cells, size):
    """cells: {frame name: (cell RGBA)}; the old JSON's rects (Unity y from the bottom) place them."""
    js = old_json(name)
    W, H = size
    sheet = np.zeros((H, W, 4), np.uint8)
    for s in js["sprites"]:
        c = cells[s["n"]]
        assert c.shape[:2] == (s["h"], s["w"]), (s["n"], c.shape, s)
        y0 = H - s["y"] - s["h"]
        sheet[y0:y0 + s["h"], s["x"]:s["x"] + s["w"]] = c
    return sheet, js


def save(name, sheet, js, seed):
    sheet = grain(sheet, seed)
    Image.fromarray(sheet, "RGBA").save(OUT / f"{name}.png", optimize=True)
    if js is not None:                                 # same cells and names: the shipped JSON, byte for byte
        (OUT / f"{name}.json").write_bytes((OLD / f"{name}.json").read_bytes())
    return sheet


# old sequence tone (measured on old/ by check: mean value of opaque texels 0..255, peak alpha)
OLD_TONE = {
    "rig": (44, 255), "platform": (56, 255), "refinery": (51, 255), "relay": (57, 255), "causeway": (58, 255),
    "icebreaker": (56, 255), "cliff": (62, 255), "derrick": (57, 255), "convoy": (60, 255),
    "hangar": (56, 255), "rigbay": (48, 255), "padring": (59, 255), "crawlerbay": (49, 255), "hatch": (62, 255), "lights": (71, 255),
    "smoke_a": (92, 145), "smoke_b": (194, 130), "steam_vent": (196, 145), "geyser": (194, 165),
    "flare": (180, 220), "searchlight": (234, 255),
    "beacon_magenta": (139, 255), "beacon_cyan": (139, 255), "strobe_white": (160, 240), "window_lights": (73, 255),
    "aurora": (121, 40),
    "cloud_bank": (237, 72), "cloud_wisp": (238, 64), "mist": (239, 48), "blizzard": (240, 48), "snow": (237, 88),
    "dock_rig": (141, 255), "dock_silo": (137, 255), "dock_cave": (166, 255), "dock_icebreaker": (153, 255),
}


def family(n):
    return n.rsplit("_", 1)[0] if n[-2:].isdigit() or n.endswith(("_closed", "_open", "_idle", "_active", "_off", "_on")) else n


# ---------------------------------------------------------------- landmarks / sites
LANDMARKS = {   # frame -> pick cell (row, col); what the drawing is
    "rig_00": (1, 3),        # stilted watch / drill house over the ice
    "rig_01": (1, 1),        # snow-buried scaffold block
    "platform_00": (0, 0),   # domed outpost with mast
    "platform_01": (0, 3),   # dome with satellite
    "platform_02": (3, 2),   # ice spires with lamp pods
    "refinery_00": (1, 2),   # tank farm
    "refinery_01": (3, 0),   # stack plant (its stacks smoke: smoke loops)
    "relay_00": (0, 1),      # dish on an ice crag
    "relay_01": (2, 1),      # antenna crag
    "causeway_00": (3, 1),   # rail train on a diagonal causeway (the span)
    "icebreaker_00": (0, 2),  # ice-locked wreck
    "icebreaker_01": (2, 0),  # ship in pack ice
    "cliff_00": (1, 0),      # ice crystal crag
    "cliff_01": (2, 2),      # iceberg with dug-in windows
    "derrick_00": (2, 3),    # lattice derrick
    "convoy_00": (3, 3),     # crane sled
}
SITES = {
    "hangar_closed": (1, 3), "hangar_open": (2, 2),
    "rigbay_closed": (2, 1), "rigbay_open": (3, 3),
    "padring_idle": (0, 2), "padring_active": (0, 1),
    "crawlerbay_closed": (1, 0), "crawlerbay_open": (1, 2),
    "hatch_closed": (2, 0), "hatch_open": (2, 3),
}


def ground_atlas(name, table, extra=None):
    a, n_px, k = pick(name)
    groups = {}
    for n, (r, c) in table.items():
        groups.setdefault(family(n), []).append((n, up(grid(a, r, c, n_px), k)))
    if extra:
        for n, cell in extra.items():
            groups.setdefault(family(n), []).append((n, cell))
    cells = {}
    for fam, items in groups.items():
        v, al = OLD_TONE[fam]
        toned = tone([c for _, c in items], v, al)
        for (n, _), c in zip(items, toned):
            cells[n] = no_red(c)
    return cells


def lamp_heads(beacons, row, frames, k=2):
    """The lamp heads (bulb + glow, no post) of the beacons pick's lamp row."""
    out = []
    for c in frames:
        p = tight(grid(beacons, row, c))
        h = p.shape[0]
        out.append(tight(p[: int(h * .55)]))
    return [up(p, k) for p in out]


def site_lamps():
    """lights_off / lights_on: a row of five lamp heads (the beacons pick's cyan lamp, unlit / lit), the strip
    FrostTuning.LampsAt draws under a site's door. BUILT BY SCRIPT from the beacons pick."""
    b = src("beacons")
    on = lamp_heads(b, 0, [3], k=1)[0]
    off = on.copy()                                    # the same head unlit: its lit (bright) texels dimmed to the glass shade
    v = off[..., :3].astype(np.float64).max(-1)
    lit = (off[..., 3] > 0) & (v > 140)
    off[lit, :3] = (off[lit, :3] * .3).astype(np.uint8)
    cells = {}
    for n, head in (("lights_off", off), ("lights_on", on)):
        h, w = head.shape[:2]
        gap = 4
        row = np.zeros((h, 5 * w + 4 * gap, 4), np.uint8)
        for i in range(5):
            row[:, i * (w + gap):i * (w + gap) + w] = head
        cells[n] = place(up(row, 2) if row.shape[1] * 2 <= 230 else row, mode="centre", centre=(128, 150))
    SCRIPTED.append("sites lights_off / lights_on: a row of five cyan lamp heads from the beacons pick (the lit head, and the same head dimmed), centred 150 px down the cell as the director expects")
    return cells


AMBIENT = HERE.parents[5] / "Scripts/Worlds/Backdrop/FrostAmbient.cs"
LIT = {"beacon_cyan": (110, 242, 238), "beacon_magenta": (255, 196, 96), "strobe_white": (205, 244, 248),
       "window_lights": (236, 178, 84), "flare": (255, 196, 96), "searchlight": (205, 244, 248)}
LANDMARK_CONTRAST, LANDMARK_SAT = 1.4, 1.3
DOCK_LUM = .27        # painted = drawn (docks are not lifted): near the landmarks' .28 as drawn


def bindings():
    """FrostAmbientCatalog.Bindings: piece -> [(loop, x, y)] (cell pixels from the top-left)."""
    import re
    src = AMBIENT.read_text()
    out = {}
    for piece, body in re.findall(r'B\("(\w+)",(.*?)\)\),?\n', src):
        out[piece] = [(l, int(x), int(y)) for l, x, y in re.findall(r'E\("(\w+)", (\d+), (\d+)', body)]
    return out


def cool(cells, old_sheet, old_js, only=None, lum_target=None):
    """Lift contrast and saturation (about the family's mean), then take the old atlas's cool blue: the family's
    mean RGB is matched to the old drawings' (per channel)."""
    o = np.asarray(Image.open(old_sheet).convert("RGBA")).astype(np.float64)
    H = o.shape[0]
    fam_old = {}
    for sp in json.loads(Path(old_js).read_text())["sprites"]:
        c = o[H - sp["y"] - sp["h"]:H - sp["y"], sp["x"]:sp["x"] + sp["w"]]
        fam_old.setdefault(family(sp["n"]), []).append(c[..., :3][c[..., 3] > 0])
    groups = {}
    for n in cells:
        if only is None or n in only:
            groups.setdefault(family(n), []).append(n)
    for fam, names in groups.items():
        allp = np.concatenate([cells[n][..., :3][cells[n][..., 3] > 0] for n in names]).astype(np.float64)
        mv = allp.max(-1).mean()
        tmp = {}
        for n in names:
            f = cells[n][..., :3].astype(np.float64)
            v = f.max(-1)
            v2 = np.clip(mv + (v - mv) * LANDMARK_CONTRAST, 0, 255)
            f = f * (v2 / np.maximum(v, 1))[..., None]
            l = f.mean(-1, keepdims=True)
            tmp[n] = np.clip(l + (f - l) * LANDMARK_SAT, 0, 255)
        cur = np.concatenate([tmp[n][cells[n][..., 3] > 0] for n in names]).mean(0)
        ratio = np.concatenate(fam_old[fam]).mean(0) / np.maximum(cur, 1)
        for n in names:
            on = cells[n][..., 3] > 0
            cells[n][..., :3] = np.where(on[..., None], np.clip(np.rint(tmp[n] * ratio), 0, 255), 0).astype(np.uint8)
    if lum_target is not None:                         # unlifted art (docks): scale to a painted luminance
        names = [n for g in groups.values() for n in g]
        px = np.concatenate([cells[n][cells[n][..., 3] > 0] for n in names]).astype(np.float64)
        lum = float((px[:, 3] * (px[:, :3] @ [.2126, .7152, .0722])).sum() / px[:, 3].sum() / 255)
        for n in names:
            on = cells[n][..., 3] > 0
            cells[n][..., :3] = np.where(on[..., None], np.clip(np.rint(cells[n][..., :3] * (lum_target / lum)), 0, 255), 0).astype(np.uint8)
    return cells


def strip_lamps(frames, block, colours=("beacon_cyan", "window_lights"), lamp=None):
    """A launch strip's lamps: two lit source pixels at the top of the drawing, a quarter in from each side (the
    topmost drawn block in that column of the idle cell), the same spots in all six cells (so no cell is brighter
    than idle), plus one amber at the drawing's lowest centre block. block: the source pixel (the grid the spots
    sit on); lamp: the lit square's side (default one block; the HD x2 pieces light 2 x 2 source pixels)."""
    lamp = lamp or block
    idle = frames[0]
    on = idle[..., 3] > 0
    xs = np.nonzero(on.any(0))[0]
    x0, x1 = xs.min(), xs.max()
    spots = []
    for fx, col in ((.25, colours[0]), (.75, colours[0])):
        gx = int(x0 + (x1 - x0) * fx) // block * block
        for gy in range(0, idle.shape[0] - block, block):
            if on[gy:gy + lamp, gx:gx + lamp].all():
                spots.append((gx, gy + block, col))    # one block in from the outline
                break
    gx = (x0 + x1) // 2 // block * block
    for gy in range(idle.shape[0] - lamp, 0, -block):
        if on[gy:gy + lamp, gx:gx + lamp].all():
            spots.append((gx, gy - lamp, colours[1]))
            break
    for c in frames:
        for x, y, col in spots:
            if (c[y:y + lamp, x:x + lamp, 3] > 0).all():
                c[y:y + lamp, x:x + lamp, :3] = LIT[col]
    return len(spots)


def light_up(cells, pick, table, alias=None, n_px=64, k=4, lamp=4):
    """A few lit lamp pixels (a lamp x lamp square on the drawing's k px grid: one source pixel of the 64 px pick, two
    of the HD one): at each of the piece's beacon / strobe emitter points (the topmost drawn square within 20 px), a
    short row of three at its window point, and the pick's own lamp-pod texels (green in the drawing) turned cyan
    (each one source pixel). Returns {frame: count}."""
    bind = bindings()
    count = {}
    for n, (r, c) in table.items():
        cell = cells[n]
        on = cell[..., 3] > 0
        lit = []
        src_cell = pick[r * n_px:(r + 1) * n_px, c * n_px:(c + 1) * n_px]
        f = src_cell[..., :3].astype(np.float64) / 255
        mx, mn = f.max(-1), f.min(-1)
        green = (src_cell[..., 3] > 0) & (f[..., 1] == mx) & (mx - mn > .3) & (f[..., 1] - f[..., 2] > .1)
        for y, x in zip(*np.nonzero(green)):
            lit.append((x * k, y * k, LIT["beacon_cyan"], k))
        for loop, x, y in bind.get((alias or {}).get(n, n), []):
            if loop not in LIT:
                continue
            gx, gy = x // k * k, y // k * k
            if loop == "window_lights":
                for dx in (-lamp, 0, lamp):
                    lit.append((gx + dx, gy, LIT[loop], lamp))
                continue
            best = None
            for yy in range(max(0, gy - 20), min(256 - lamp, gy + 21), k):
                for xx in range(max(0, gx - 20), min(256 - lamp, gx + 21), k):
                    if on[yy:yy + lamp, xx:xx + lamp].all() and (best is None or (yy, abs(xx - gx)) < (best[1], abs(best[0] - gx))):
                        best = (xx, yy)
            if best:
                lit.append((best[0], best[1], LIT[loop], lamp))
        cnt = 0
        for x, y, col, s in lit:
            if on[y:y + s, x:x + s].all():
                cell[y:y + s, x:x + s, :3] = col
                cnt += 1
        count[n] = cnt
    return count


def build_landmarks():
    cells = ground_atlas("landmarks", LANDMARKS)
    cells = cool(cells, OLD / "landmarks.png", OLD / "landmarks.json")
    a, n_px, k = pick("landmarks")
    lit = light_up(cells, a, LANDMARKS, n_px=n_px, k=k)
    print("landmarks lit lamp pixels:", lit)
    sheet, js = sheet_from_cells("landmarks", cells, (1024, 1024))
    return save("landmarks", sheet, js, 101)


def build_sites():
    cells = ground_atlas("sites", SITES, extra=site_lamps())
    cells = cool(cells, OLD / "sites.png", OLD / "sites.json", only=set(SITES))
    table = dict(SITES)
    a, n_px, k = pick("sites")
    lit = light_up(cells, a, table, n_px=n_px, k=k, alias={n: n.rsplit("_", 1)[0] + "_closed" if not n.endswith("idle") and not n.endswith("active")
                                                       else "padring_idle" for n in SITES})
    print("sites lit lamp pixels:", lit)
    sheet, js = sheet_from_cells("sites", cells, (1024, 1024))
    return save("sites", sheet, js, 102)


# ---------------------------------------------------------------- weather
WEATHER = {
    "cloud_bank": [(0, 0), (0, 1), (0, 2), (0, 3)], "cloud_wisp": [(1, 0), (1, 1), (1, 2), (1, 3)],
    "mist": [(2, 0), (2, 1), (2, 2)], "blizzard": [(2, 3), (3, 0), (3, 1)], "snow": [(3, 2), (3, 3)],
}


def build_weather():
    a = src("weather")
    cells = {}
    for seq, rc in WEATHER.items():
        raw = [up(cut(a, c * 64, r * 64, 64, 64, keep_all=True), 4) for r, c in rc]
        v, al = OLD_TONE[seq]
        for i, c in enumerate(tone(raw, v, al)):
            cells[f"{seq}_{i:02d}"] = no_red(c)
    # the pick is near white; the old weather is ice blue (mean RGB 142, 179, 238): take its tint, so the ceiling
    # keeps the old brightness as drawn (atlas cap .80) and the cloud_bank's core value (vs the planetfall deck)
    o = np.asarray(Image.open(OLD / "weather.png").convert("RGBA")).astype(np.float64)
    om = o[..., :3][o[..., 3] > 8].mean(0)
    allc = np.concatenate([c[..., :3][c[..., 3] > 0] for c in cells.values()]).astype(np.float64)
    ratio = om / allc.mean(0)
    for n, c in cells.items():
        on = c[..., 3] > 0
        c[..., :3] = np.where(on[..., None], np.clip(np.rint(c[..., :3] * ratio), 0, 255), 0).astype(np.uint8)
    sheet, js = sheet_from_cells("weather", cells, (1024, 1024))
    return save("weather", sheet, js, 103)


# ---------------------------------------------------------------- aurora
def loop_order(frames):
    """Order frames into a smooth loop: greedy nearest neighbour by changed texels, from frame 0."""
    n = len(frames)
    def d(i, j):
        p, q = frames[i], frames[j]
        return int((np.abs(p.astype(int) - q.astype(int)).sum(-1) > 40).sum())
    order, left = [0], set(range(1, n))
    while left:
        j = min(left, key=lambda k: d(order[-1], k))
        order.append(j)
        left.remove(j)
    return order


def build_aurora():
    a = src("aurora")
    pieces = [place(up(tight(grid(a, r, c)), 3), centre=(128, 128)) for r in range(4) for c in range(4)]
    order = loop_order(pieces)
    v, al = OLD_TONE["aurora"]
    toned = tone([pieces[i] for i in order], v, al, alpha_by_value=True)
    cells = {f"aurora_{i:02d}": no_red(c) for i, c in enumerate(toned)}
    print("aurora: pick cells in loop order", order)
    sheet, js = sheet_from_cells("aurora", cells, (1024, 1024))
    return save("aurora", sheet, js, 104)


# ---------------------------------------------------------------- smoke / steam (puff life cycles)
def puff_cycle(name, seqs, k=2, rise=56):
    """The pick is ONE 16-drawing puff life (small -> full -> bursting -> gone). Each loop takes every other drawing
    (a and b interleaved, so both are a whole life cycle), standing on the foot (128, 236) and rising as it grows."""
    a = src(name)
    raw = [tight(cut(a, c * 64, r * 64, 64, 64, keep_all=True)) for r in range(4) for c in range(4)]
    cells = {}
    for si, seq in enumerate(seqs):
        idx = list(range(si, 16, 2))
        frames = []
        for j, i in enumerate(idx):
            p = up(raw[i], k)
            lift_px = int(round(rise * j / (len(idx) - 1)))
            frames.append(place(p, mode="foot", foot=236 - lift_px, centre=(128, 128)))
        v, al = OLD_TONE[seq]
        for j, c in enumerate(tone(frames, v, al)):
            cells[f"{seq}_{j:02d}"] = no_red(c)
    return cells


def build_smoke():
    cells = puff_cycle("smoke", ["smoke_a", "smoke_b"])
    sheet, js = sheet_from_cells("smoke", cells, (1024, 1024))
    return save("smoke", sheet, js, 105)


def build_steam():
    cells = puff_cycle("steam", ["steam_vent", "geyser"])
    sheet, js = sheet_from_cells("steam", cells, (1024, 1024))
    return save("steam", sheet, js, 106)


# ---------------------------------------------------------------- fire_lights
def warm_mask(p):
    rgb = p[..., :3].astype(np.float64) / 255
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = mx - mn
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    dd = np.where(d > 0, d, 1)
    hue = np.where(mx == r, ((g - b) / dd) % 6, np.where(mx == g, (b - r) / dd + 2, (r - g) / dd + 4)) * 60
    sat = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    return (p[..., 3] > 0) & (hue >= 10) & (hue <= 65) & (sat > .35) & (mx > .45)


def flame_of(p):
    """The flame of a torch / brazier drawing: its warm texels and their outline, above the container."""
    w = warm_mask(p)
    ys = np.nonzero(w.any(1))[0]
    keep = ndimage.binary_dilation(w, iterations=1) & (p[..., 3] > 0)
    keep[ys.max() + 1:] = False                       # nothing below the lowest flame row (the bowl)
    o = p.copy()
    o[~keep] = 0
    return tight(o)


def searchlight_frames():
    """No beam in the pick: a pixel-art cone on the tiles' 4 px grid, in the beacons pick's lit cyan, sweeping
    -30..30 degrees and back over 8 frames (alpha falls off along the beam in hard steps). BUILT BY SCRIPT."""
    b = src("beacons")
    lit = grid(b, 0, 3)
    on = lit[..., 3] > 0
    col = lit[..., :3][on].astype(int)
    col = col[col.sum(1).argmax()]                    # the lamp's brightest texel
    frames = []
    for ang in (-24, -12, 0, 12, 24, 12, 0, -12):
        g = np.zeros((64, 64, 4), np.uint8)
        t = np.radians(ang)
        ox, oy = 32, 58
        yy, xx = np.mgrid[0:64, 0:64]
        dx, dy = xx + .5 - ox, oy - (yy + .5)
        along = dx * np.sin(t) + dy * np.cos(t)
        across = dx * np.cos(t) - dy * np.sin(t)
        L = 40.0
        inside = (along > 0) & (along < L) & (np.abs(across) < 1.2 + along * np.tan(np.radians(9)))
        level = np.clip(1 - along / L, 0, 1)
        alpha = np.ceil(level * 4) / 4 * 255
        g[inside, :3] = col
        g[inside, 3] = alpha[inside].astype(np.uint8)
        frames.append(up(g, 4))
    SCRIPTED.append("fire_lights searchlight_00..07: a pixel-art beam cone (4 px grid, the beacons pick's lit cyan), swept -24..24 degrees")
    return frames


def build_fire_lights():
    a = src("fire_lights")
    flames = [flame_of(grid(a, r, c)) for r in (2, 3) for c in range(4)]
    # the 8 drawings are 2 torches and 6 fires: the loop uses the fires only (pick cells 1, 3, 7, 5, 4, 6, 4, 5 of rows 2-3),
    # ordered so each step changes the flame and none jumps between a torch and a fire
    flames = [flames[i] for i in (1, 3, 7, 5, 4, 6, 4, 5)]
    k = 3
    fl = [place(up(p, k), mode="foot", foot=236, centre=(128, 128)) for p in flames]
    SCRIPTED.append("fire_lights flare_00..07: the pick's 6 fire drawings (rows 2-3) cut above their bowls, sequenced as a flicker loop (two reused), x3")
    cells = {}
    v, al = OLD_TONE["flare"]
    for i, c in enumerate(tone(fl, v, al)):
        cells[f"flare_{i:02d}"] = no_red(c)
    v, al = OLD_TONE["searchlight"]
    sl = searchlight_frames()
    for i, c in enumerate(tone(sl, v, al, alpha_by_value=False)):
        c[..., 3] = np.where(sl[i][..., 3] > 0, np.minimum(sl[i][..., 3], al), 0)
        cells[f"searchlight_{i:02d}"] = c
    sheet, js = sheet_from_cells("fire_lights", cells, (1024, 1024))
    return save("fire_lights", sheet, js, 107)


# ---------------------------------------------------------------- beacons
def build_beacons():
    b = src("beacons")
    f = src("fire_lights")
    cells = {}
    for seq, row in (("beacon_cyan", 0), ("beacon_magenta", 1)):
        heads = lamp_heads(b, row, [0, 1, 2, 3])
        frames = [place(h, mode="foot", foot=236) for h in heads]
        v, al = OLD_TONE[seq]
        for i, c in enumerate(tone(frames, v, al)):
            cells[f"{seq}_{i:02d}"] = no_red(c)
    # strobe: the cyan lamp washed white, off -> flash -> fading
    heads = lamp_heads(b, 0, [0, 3, 2, 1])
    frames = []
    for h in heads:
        g = h.copy()
        lum = h[..., :3].astype(np.float64).mean(-1, keepdims=True)
        g[..., :3] = np.clip(lum * .25 + h[..., :3] * .75 * 0 + lum * .75, 0, 255).astype(np.uint8)
        frames.append(place(g, mode="foot", foot=236))
    v, al = OLD_TONE["strobe_white"]
    for i, c in enumerate(tone(frames, v, al)):
        cells[f"strobe_white_{i:02d}"] = c
    SCRIPTED.append("beacons strobe_white_00..03: the cyan lamp head washed to white, frames off / flash / fading / fading")
    # windows: the fire_lights pick's two arched windows (two flame drawings), and each dimmed: a flicker
    w0, w1 = tight(grid(f, 0, 0)), tight(grid(f, 1, 0))
    if w0.shape != w1.shape:
        h = min(w0.shape[0], w1.shape[0]); w = min(w0.shape[1], w1.shape[1])
        w0, w1 = w0[:h, :w], w1[:h, :w]
    def dim(p, k):
        o = p.copy()
        warm = warm_mask(p)
        o[..., :3] = np.where(warm[..., None], np.clip(p[..., :3] * k, 0, 255), p[..., :3]).astype(np.uint8)
        return o
    frames = [place(up(x, 2), mode="foot", foot=236) for x in (w0, w1, dim(w0, .7), dim(w1, .85))]
    v, al = OLD_TONE["window_lights"]
    for i, c in enumerate(tone(frames, v, al)):
        cells[f"window_lights_{i:02d}"] = no_red(c)
    SCRIPTED.append("beacons window_lights_00..03: the fire_lights pick's two arched fire windows, each also dimmed (flicker), x2")
    sheet, js = sheet_from_cells("beacons", cells, (1024, 1024))
    return save("beacons", sheet, js, 108)


# ---------------------------------------------------------------- docks
def fill_holes(p):
    """Enclosed transparent holes (an open hatch drawn see-through) become the drawing's darkest colour: the shaft."""
    on = p[..., 3] > 0
    holes = ndimage.binary_fill_holes(on) & ~on
    if holes.any():
        o = p.copy()
        v = p[..., :3].astype(int).sum(-1)
        dark = p[on][v[on].argmin()]
        o[holes] = dark
        o[holes, 3] = 255
        return o
    return p


def iris(cell, centre, radius):
    """The doors opening: a dark disc (the bay) of `radius` px at `centre`, drawn only over the drawing."""
    if radius <= 0:
        return cell
    o = cell.copy()
    yy, xx = np.mgrid[0:cell.shape[0], 0:cell.shape[1]]
    disc = ((xx - centre[0]) ** 2 / 1.0 + (yy - centre[1]) ** 2 / .8) <= radius ** 2
    disc &= cell[..., 3] > 0
    on = cell[..., 3] > 0
    v = cell[..., :3].astype(int).sum(-1)
    dark = cell[on][v[on].argmin()]
    o[disc, :3] = dark[:3]
    return o


def scripted_strip(base, centre, R):
    """idle, blink, opening, open, hold, closing: the bay iris 0, 0, R/2, R, R, R/2."""
    return [iris(base, centre, r) for r in (0, 0, R // 2, R, R, R // 2)]


def hd_piece(a, r, c, n=128, pad=24):
    """A piece of an HD sheet drawn a few px off its cell: the components (in a window `pad` px wider than the
    cell) whose centre lies in the cell, tight. Neighbours' bits that cross in are dropped (their centre is outside)."""
    y0, x0 = max(0, r * n - pad), max(0, c * n - pad)
    win = a[y0:(r + 1) * n + pad, x0:(c + 1) * n + pad].copy()
    m = win[..., 3] > 0
    lab, k = ndimage.label(ndimage.binary_dilation(m, iterations=1), structure=np.ones((3, 3)))
    lab = lab * m
    keep = np.zeros_like(m)
    for i in range(1, k + 1):
        yy, xx = np.nonzero(lab == i)
        if len(yy) and r * n <= y0 + yy.mean() < (r + 1) * n and c * n <= x0 + xx.mean() < (c + 1) * n:
            keep |= lab == i
    win[~keep] = 0
    return tight(win)


def build_docks():
    a = src("docks")
    a_hd = src_hd("docks")
    sixth = 256 / 6
    def top(r, c):
        x0 = int(round(c * sixth)); x1 = int(round((c + 1) * sixth))
        return cut(a, x0, r * 64, x1 - x0, 64)
    def low(r, c):
        return cut(a, c * 64, r * 64, 64, 64)
    cells, pads = {}, {}
    # cave: the pick's own 6-drawing hangar door (closed, open ... ) -- the launch strip as drawn
    cave = [place(up(tight(top(1, c)), 4)) for c in range(6)]
    cells["cave"] = cave
    # silo: the pick's three hatch drawings (glass shut, open, machine up): idle, idle, open, up, up, open
    hat = [fill_holes(tight(top(0, c))) for c in range(3)]
    h = min(p.shape[0] for p in hat); w = min(p.shape[1] for p in hat)
    hat = [p[:h, :w] for p in hat]
    silo = [place(up(hat[i], 4)) for i in (0, 0, 2, 1, 1, 2)]
    cells["silo"] = silo
    SCRIPTED.append("docks silo_00..05: the pick's 3 hatch drawings in launch order (shut, shut, machine up, open, open, machine up); see-through holes filled as the dark shaft")
    # rows 2-3: the HD pick's pieces at x2 (2 px source pixels) when there is one, else the 64 px pick's at x3
    if a_hd is not None:
        low_t, k_low, rig_r, ice_r = (lambda r, c: hd_piece(a_hd, r, c)), 2, 38, 33
    else:
        low_t, k_low, rig_r, ice_r = (lambda r, c: tight(low(r, c))), 3, 30, 26
    # rig: the platform drawing, its bay iris opening by script
    rig = place(up(low_t(2, 3), k_low))
    cells["rig"] = scripted_strip(rig, (128, 120), rig_r)
    pads["rig"] = (128, 120)
    SCRIPTED.append("docks rig_00..05: the pick's rig platform (row 2) with a dark bay opening / closing on its deck by script")
    # icebreaker: the tracked hull turned upright for the 192 x 256 cell, its deck hatch opening by script
    ice = np.rot90(low_t(3, 1), -1)
    ice = place(up(ice, k_low), size=(192, 256), centre=(96, 128))
    cells["icebreaker"] = scripted_strip(ice, (96, 128), ice_r)
    pads["icebreaker"] = (96, 128)
    SCRIPTED.append("docks icebreaker_00..05: the pick's tracked hull (row 3) turned upright, a deck hatch opening / closing by script")
    named = {}
    for tag, fr in cells.items():
        v, al = OLD_TONE["dock_" + tag]
        for i, c in enumerate(tone(fr, v, al)):
            named[f"{tag}_{i:02d}"] = no_red(c)
    # the landmarks' tone pass: contrast, the old docks' cool blue, painted luminance DOCK_LUM (docks draw unlifted)
    named = cool(named, OLD / "docks.png", OLD / "docks.json", lum_target=DOCK_LUM)
    for tag in cells:
        frames = [named[f"{tag}_{i:02d}"] for i in range(6)]
        block = k_low if tag in ("rig", "icebreaker") else 4
        print(f"docks {tag}: {strip_lamps(frames, block, lamp=4 if block == 2 else None)} lamps")
    sheet, js = sheet_from_cells("docks", named, (1578, 1054))
    save("docks", sheet, js, 109)
    return named, pads


# ---------------------------------------------------------------- weather sheets
def build_wx_ambient():
    a = src("wx_ambient")
    sheet = up(a, 4)                                   # 512 x 128 of 64 px cells -> 2048 x 512 of 256
    olda = 128
    on = sheet[..., 3] > 0
    sheet[..., 3] = np.where(on, olda, 0).astype(np.uint8)
    sheet = no_red(sheet)
    sheet = grain(sheet, 110)
    Image.fromarray(sheet, "RGBA").save(OUT / "wx_ambient.png", optimize=True)
    cells = []
    for r in range(2):
        for c in range(8):
            cell = sheet[r * 256:(r + 1) * 256, c * 256:(c + 1) * 256, 3] > 0
            ys, xs = np.nonzero(cell)
            cells.append({"col": c, "row": r, "x": int(c * 256 + xs.min()), "y": int(512 - (r * 256 + ys.max() + 1)),
                          "w": int(xs.max() - xs.min() + 1), "h": int(ys.max() - ys.min() + 1)})
    (OUT / "wx_ambient.json").write_text(json.dumps({"cells": cells}, indent=1))
    return sheet


def build_wx_hazard():
    a = src("wx_hazard")
    sheet = up(a, 8)                                   # 384 x 192 of 64 px cells -> 3072 x 1536 of 512
    # rows 0-1 the loops (old peak alpha 144, value ~212), row 2 the warnings (old peak alpha 72, value ~215)
    bands = []
    for r0, r1, v, al in ((0, 512, 215, 144), (512, 1024, 209, 144), (1024, 1536, 215, 72)):
        bands.append(tone([sheet[r0:r1]], v, al)[0])
    sheet = grain(no_red(np.concatenate(bands, 0)), 111)
    Image.fromarray(sheet, "RGBA").save(OUT / "wx_hazard.png", optimize=True)
    return sheet


# 2026-11: Frost's weather sheets are built SOFT, with clean cells and gutters, by build_soft_weather.py (the builders below are kept for
# the record only: they cut the picks on their 64 px grid, which the weather picks are not drawn on, and scale them by nearest neighbour)
SUPERSEDED = ("weather", "wx_ambient", "wx_hazard")

BUILD = {
    "landmarks": build_landmarks, "sites": build_sites, "weather": build_weather, "aurora": build_aurora,
    "smoke": build_smoke, "steam": build_steam, "fire_lights": build_fire_lights, "beacons": build_beacons,
    "docks": build_docks, "wx_ambient": build_wx_ambient, "wx_hazard": build_wx_hazard,
}


def main():
    global OUT
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=str(OUT))
    ap.add_argument("atlases", nargs="*", default=[n for n in BUILD if n not in SUPERSEDED])
    args = ap.parse_args()
    for n in args.atlases:
        if n in SUPERSEDED:
            sys.exit(f"{n}: superseded by build_soft_weather.py (this build cuts the pick on its grid, x4 / x8 nearest neighbour: "
                     "the frames cut into each other and the sheet is blocky). Run that instead.")
    OUT = Path(args.out)
    OUT.mkdir(parents=True, exist_ok=True)
    for n in args.atlases:
        r = BUILD[n]()
        if n == "docks":
            print("docks pads (cell px):", r[1])
        print("built", n)
    for s in SCRIPTED:
        print("SCRIPTED:", s)


if __name__ == "__main__":
    main()
