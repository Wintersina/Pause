"""Storm landmarks + hazard sheet build: slices Codex's painted sheets into the existing atlas cells (JSON untouched). Layout/fit/loop
(rotate, roll, wrap-cut) only; every pixel of art comes from Codex.

    python3 build_lm_haz.py <src~ dir> <Backdrop3 dir>
"""
import sys, json
from pathlib import Path
import numpy as np
from PIL import Image

SRC = Path(sys.argv[1]); DST = Path(sys.argv[2])

def load(name):
    im = Image.open(SRC / (name + ".png")).convert("RGBA")
    return np.asarray(im).astype(np.float32) / 255

def to_img(a):
    return Image.fromarray(np.rint(np.clip(a, 0, 1) * 255).astype(np.uint8), "RGBA")

def cells(a, cols, rows):
    h, w = a.shape[:2]; ch, cw = h // rows, w // cols
    return [a[r * ch:(r + 1) * ch, c * cw:(c + 1) * cw] for r in range(rows) for c in range(cols)]

def bbox(a, thr=.06):
    ys, xs = np.where(a[..., 3] > thr)
    return ys.min(), ys.max() + 1, xs.min(), xs.max() + 1

def scaled(a, s):
    im = to_img(a)
    return im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)

def fit(c, w, h, margin=.05):
    y0, y1, x0, x1 = bbox(c); t = c[y0:y1, x0:x1]
    s = min(w * (1 - margin) / t.shape[1], h * (1 - margin) / t.shape[0])
    return scaled(t, s)

def put_center(sheet, im, cx, cy):
    sheet.alpha_composite(im, (int(round(cx - im.width / 2)), int(round(cy - im.height / 2))))

def despeckle(c, k=6, keep=.04):
    """Drop stray fragments (bits of the neighbouring cell, loose specks): connected pieces under `keep` of the biggest one's mass are removed."""
    a = c[..., 3] > .06; h, w = a.shape; gh, gw = h // k, w // k
    g = a[:gh * k, :gw * k].reshape(gh, k, gw, k).any((1, 3))
    lab = np.zeros((gh, gw), np.int32); sizes = [0]; n = 0
    for y in range(gh):
        for x in range(gw):
            if g[y, x] and lab[y, x] == 0:
                n += 1; stack = [(y, x)]; lab[y, x] = n; cnt = 0
                while stack:
                    cy, cx = stack.pop(); cnt += 1
                    for dy in (-1, 0, 1):
                        for dx in (-1, 0, 1):
                            ny, nx = cy + dy, cx + dx
                            if 0 <= ny < gh and 0 <= nx < gw and g[ny, nx] and lab[ny, nx] == 0:
                                lab[ny, nx] = n; stack.append((ny, nx))
                sizes.append(cnt)
    if n < 2: return c
    big = max(sizes[1:]); good = np.zeros(n + 1, bool)
    for i in range(1, n + 1): good[i] = sizes[i] >= keep * big
    m = good[lab]
    m = np.kron(m, np.ones((k, k), bool)); full = np.zeros((h, w), bool); full[:m.shape[0], :m.shape[1]] = m
    # grow the kept region by one block so soft edges survive
    from numpy import roll
    grown = full.copy()
    for s in (k, -k):
        grown |= np.roll(full, s, 0) | np.roll(full, s, 1)
    out = c.copy(); out[..., 3] *= grown
    return out

META = {}

def cell_xy(n, sx, sy):
    """a point of the source painting (px) -> the landmark cell (px from its top-left)."""
    m = META[n]; return ((sx - m["x0"]) * m["scale"] + m["ox"], (sy - m["y0"]) * m["scale"] + m["oy"])

def fill_landmarks():
    j = json.load(open(DST / "landmarks.json")); sheet = Image.open(DST / "landmarks.png").convert("RGBA"); H = sheet.height
    by = {s["n"]: s for s in j["sprites"]}
    maps = {"lm_anvils": ["anvil_tower_00", "anvil_tower_01", "anvil_tower_02", "anvil_tower_03"],
            "lm_masts": ["spire_00", "spire_01", "skyhook_00", "skyhook_01"],
            "lm_refineries": ["refinery_float_00", "refinery_float_01", "refinery_float_02", "siphon_tower_00"],
            "lm_docks": ["airship_dock_00", "gantry_00", "turbine_raft_00", "relay_dish_00"]}
    for sname, names in maps.items():
        if not (SRC / (sname + ".png")).exists():
            print("missing", sname); continue
        for n, c in zip(names, [despeckle(x) for x in cells(load(sname), 2, 2)]):
            if n == "turbine_raft_00" and (SRC / "tr_raft.png").exists(): c = despeckle(load("tr_raft"))      # bare hubs: the fans are the rotors atlas
            if n == "relay_dish_00" and (SRC / "dish_mast.png").exists(): c = despeckle(load("dish_mast"))   # bare mount: the dish is its own turning sprite
            s = by[n]; box = (s["x"], H - s["y"] - s["h"], s["x"] + s["w"], H - s["y"])
            sheet.paste((0, 0, 0, 0), box)
            if n.startswith("spire_"):
                # StormTuning.RodTipY: the rod's tip (topmost solid point) sits at (128, 40) from the cell top, nothing above y=18
                y0, y1, x0, x1 = bbox(c); core = np.where(c[..., 3] > .5); ty = core[0].min(); tx = int(np.median(core[1][core[0] < ty + 6]))
                sc = (s["h"] - 8 - 40) / max(1, y1 - ty)
                t = c[ty:y1, x0:x1]; im = scaled(t, sc); left = 128 - round((tx - x0) * sc)
                im = im.crop((max(0, -left), 0, min(im.width, s["w"] - left), im.height))
                cell = Image.new("RGBA", (s["w"], s["h"])); cell.alpha_composite(im, (max(0, left), 40)); sheet.alpha_composite(cell, box[:2]); continue
            y0, y1, x0, x1 = bbox(c); sc = min(s["w"] * .95 / (x1 - x0), s["h"] * .95 / (y1 - y0)); im = fit(c, s["w"], s["h"])
            ox = (s["w"] - im.width) / 2; oy = (s["h"] - im.height) / 2     # where the cropped, scaled art sits in the 256 cell
            META[n] = {"scale": sc, "x0": int(x0), "y0": int(y0), "ox": ox, "oy": oy}
            put_center(sheet, im, s["x"] + s["w"] / 2, H - s["y"] - s["h"] / 2)
    sheet.save(DST / "landmarks.png")

def min_cut(err):
    O, L = err.shape; cost = np.empty_like(err, dtype=np.float64); cost[:, 0] = err[:, 0]
    for i in range(1, L):
        p = cost[:, i - 1]
        cost[:, i] = err[:, i] + np.minimum(p, np.minimum(np.concatenate(([np.inf], p[:-1])), np.concatenate((p[1:], [np.inf]))))
    r = np.empty(L, np.int32); r[-1] = int(np.argmin(cost[:, -1]))
    for i in range(L - 2, -1, -1):
        jj = r[i + 1]; lo, hi = max(0, jj - 1), min(O, jj + 2); r[i] = lo + int(np.argmin(cost[lo:hi, i]))
    return r

def wrap_tile(a, size=1024, ov=200):
    """Make a horizontally seamless size x size RGBA tile from a (size+ov) wide crop, cutting along a min-error path."""
    h, w = a.shape[:2]; y0 = (h - size) // 2; x0 = (w - size - ov) // 2
    c = a[y0:y0 + size, x0:x0 + size + ov]
    left = c[:, :ov]; extra = c[:, size:size + ov]
    err = np.abs(left - extra).sum(-1).T; k = np.ones(5, np.float32) / 5
    err = np.apply_along_axis(lambda v: np.convolve(v, k, "same"), 1, err)
    r = min_cut(err); cols = np.arange(ov)[None, :]
    strip = np.where((cols < r[:, None])[..., None], extra, left)
    out = c[:, :size].copy(); out[:, :ov] = strip
    return out

def fill_hazard():
    swirl_p, front_p = SRC / "hz_swirl.png", SRC / "hz_front.png"
    sheet = Image.open(DST / "wx_hazard.png").convert("RGBA")
    def put(r, c, im): 
        box = (c * 512, r * 512, c * 512 + 512, r * 512 + 512); sheet.paste((0, 0, 0, 0), box); sheet.alpha_composite(im, box[:2])
    if swirl_p.exists():
        a = load("hz_swirl"); y0, y1, x0, x1 = bbox(a); t = to_img(a[y0:y1, x0:x1]); s = 500 / max(t.width, t.height)
        t = t.resize((round(t.width * s), round(t.height * s)), Image.LANCZOS)
        cell = Image.new("RGBA", (512, 512)); cell.alpha_composite(t, ((512 - t.width) // 2, (512 - t.height) // 2))
        for col in range(6): put(0, col, cell)            # the game turns the body itself; the loop frames are the same painting
        for col in (0, 1):                                  # warning cells: the gathering vortex, fainter, two turns
            w = cell.rotate(30 * col, resample=Image.BICUBIC); arr = np.asarray(w).astype(np.float32) / 255; arr[..., 3] *= .8
            put(2, col, to_img(arr))
    if front_p.exists():
        a = load("hz_front"); tile = wrap_tile(a)
        tile = np.asarray(to_img(tile).resize((512, 512), Image.LANCZOS)).astype(np.float32) / 255
        for col in range(6):
            put(1, col, to_img(np.roll(tile, round(col * 512 / 6), axis=1)))        # a drifting loop: the tile wraps seamlessly
        for col in (2, 3):
            arr = np.roll(tile, round((col - 2) * 256), axis=1).copy(); arr[..., 3] *= .55; put(2, col, to_img(arr))
    sheet.save(DST / "wx_hazard.png")

HUBS_627 = [(367, 122), (203, 236), (506, 306)]        # hub caps measured on the 627 px preview of tr_raft.png (x2 = source px)
HUB_R_627 = [80, 60, 60]                               # blade tip radius per fan on the same preview

def fill_rotors_dish():
    """rotors.png: three fan rotors (119 px cells) + the relay dish (238 px cell, its pivot = the cell centre). Writes tuning.json."""
    out = {}
    if (SRC / "tr_rotors.png").exists():
        sheet = Image.new("RGBA", (357 + 238, 238))
        for k, c in enumerate(cells(load("tr_rotors"), 2, 2)[:3]):
            al = c[..., 3]; ys, xs = np.mgrid[0:al.shape[0], 0:al.shape[1]]; w = al * (al > .06)
            cy = (ys * w).sum() / w.sum(); cx = (xs * w).sum() / w.sum()        # three-fold symmetric: the mass centroid is the hub
            rad = float(np.sqrt((((ys - cy) ** 2 + (xs - cx) ** 2) * (al > .3))).max())
            s = 56.0 / rad
            im = to_img(c).resize((round(c.shape[1] * s), round(c.shape[0] * s)), Image.LANCZOS)
            cell = Image.new("RGBA", (119, 119)); cell.alpha_composite(im, (round(59.5 - cx * s), round(59.5 - cy * s)))
            sheet.alpha_composite(cell, (k * 119, 238 - 119))
        out["rotor_cell_radius"] = 56.0
    if (SRC / "dish_dish.png").exists():
        a = despeckle(load("dish_dish")); y0, y1, x0, x1 = bbox(a); t = a[y0:y1, x0:x1]
        s = 96.0 / max(t.shape[0], t.shape[1]); im = to_img(t).resize((round(t.shape[1] * s), round(t.shape[0] * s)), Image.LANCZOS)
        cell = Image.new("RGBA", (238, 238)); cell.alpha_composite(im, (119 - im.width // 2, 119 - im.height))     # bottom centre = the pivot
        sheet.alpha_composite(cell, (357, 0))
    sheet.save(DST / "rotors.png")
    j = {"sprites": [{"n": "rotor_0%d" % k, "x": k * 119, "y": 0, "w": 119, "h": 119} for k in range(3)] + [{"n": "dish_00", "x": 357, "y": 0, "w": 238, "h": 238}]}
    json.dump(j, open(DST / "rotors.json", "w"))
    if "turbine_raft_00" in META:
        hubs = [cell_xy("turbine_raft_00", 2 * x, 2 * y) for x, y in HUBS_627]
        out["FanHubs"] = [[round(h[0], 1), round(h[1], 1)] for h in hubs]
        sc = META["turbine_raft_00"]["scale"]
        out["FanScales"] = [round(2 * r * sc / 56.0, 3) for r in HUB_R_627]
    if "relay_dish_00" in META and (SRC / "dish_mast.png").exists():
        a = despeckle(load("dish_mast")); core = np.where(a[..., 3] > .5); ty = core[0].min(); tx = int(np.median(core[1][core[0] < ty + 8]))
        out["DishMount"] = [round(v, 1) for v in cell_xy("relay_dish_00", tx, ty + 14)]
    json.dump(out, open(SRC / "tuning.json", "w"), indent=1); print(out)

def fill_thunder():
    """thunder.png: each tower's lightning layer = the bright, bluish lightning pixels of the new tower art (the strike flash re-draws them on top)."""
    lj = json.load(open(DST / "landmarks.json")); lm = np.asarray(Image.open(DST / "landmarks.png").convert("RGBA")).astype(np.float32) / 255; H = lm.shape[0]
    by = {s["n"]: s for s in lj["sprites"]}; sheet = np.zeros((256, 1024, 4), np.float32)
    for k in range(4):
        s = by["anvil_tower_0%d" % k]; y = H - s["y"] - s["h"]; c = lm[y:y + 256, s["x"]:s["x"] + 256].copy()
        v = c[..., :3].max(-1); b = c[..., 2] - c[..., 0]
        m = np.clip((v - .62) / .25, 0, 1) * np.clip((b + .02) / .1, 0, 1)
        c[..., 3] *= m; sheet[:, k * 256:(k + 1) * 256] = c
    to_img(sheet).save(DST / "thunder.png")

if __name__ == "__main__":
    fill_landmarks(); fill_hazard(); fill_rotors_dish(); fill_thunder()
