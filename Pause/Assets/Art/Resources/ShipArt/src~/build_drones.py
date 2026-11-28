"""Player-ship drone sheets: every ship's gun drones (one design per drone slot tier) + its healing drone.

Design: every ship has 3 gun-drone designs, one per drone slot its upgrades unlock (t1 = first slot =
the main floatie / UltimateGun barrel, t2 = second, t3 = third), plus a separate healing drone (heal),
each with a colour variation per ship skin.

Sources (one PNG per tier, transparent, 1 px pixel art, any size up to CELL - 2*PAD):
    ShipArt/<Key>/src~/drones/<tier>.png          tier in t1, t2, t3, heal

Outputs (all in the ship's own folder; Resources-loaded by Scripts/Ship/DroneArt.cs):
    ShipArt/<Key>/<Key>_dronelayout.json          layout (own name: Resources paths drop the extension, so it must not
                                                  share "<Key>_drones" with the stock sheet): cell, frames, fps, pivot, tiers (row + sprite rect), skins
    ShipArt/<Key>/<Key>_drones.bytes              stock sheet (PNG bytes): one row per present tier, one column per frame
    ShipArt/<Key>/<Key>_drones_<Skin>.bytes       the same sheet recoloured for each non-stock skin of <Key>_data.json

A missing tier is simply absent from the json: the game falls back to the old roster tile
(UltimateGun.CompanionSprite -> ShipArt/Guns/ship_gun_roster.png; HealDrone tints it green) for that tier
only. Dropping a new <tier>.png into src~/drones/ and re-running this script is all it takes to add art:
the loader finds tiers and ships by file, nothing is listed in code.

Usage:
    python3 build_drones.py                         build every ship that has src~/drones/*.png
    python3 build_drones.py NeonComet VoltViper     only these
    python3 build_drones.py --import-picks REVIEWS CANDIDATES
        copy the approved picks (REVIEWS/drone_<Key>_<tier>.json: status "approved",
        pick "s/drone_<Key>_<tier>__nr1.png#<i>") from CANDIDATES/drone_<Key>_<tier>/r1_<i>.png
        into <Key>/src~/drones/<tier>.png (+ picks.json provenance), then build.
    python3 build_drones.py --contact OUT.png       also write a contact sheet (ships x skins x tiers + frames)

Skins: the same approach as the hull skins' scripted recolour (build_restyle.recolour): the ship's main
armour hue family (hue near the stock `hue` of <Key>_data.json, or `armourHue` / `hueTol` of an optional
<Key>/src~/drones/config.json when the drones' armour is another hue, e.g. Paragon; saturated) is gradient-mapped onto the
skin's shadow / base / highlight; lights, cores, glass, metal and emblems (other hues, greys) are kept.
A Special skin keeps the stock armour and paints its livery pattern (racing / chevrons / diagonal /
split) in the accent colours over the armour.

Idle animation: FRAMES frames of a light pulse (the non-armour bright pixels: cores, engines, lamps,
blended toward white by PULSE[f]); the game plays them at FPS (the bob is code).
"""
import glob
import json
import os
import shutil
import sys
import uuid

import numpy as np
from PIL import Image
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
SHIPART = os.path.normpath(os.path.join(HERE, ".."))
sys.path.insert(0, HERE)
import shiplib  # noqa: E402

TIERS = ["t1", "t2", "t3", "heal"]
CELL = 104            # sheet cell (px); drawings are <= 96 px, centred, so cells never bleed into each other
FILL = 0.93           # drawing's longest side / sprite rect side: the old roster tiles fill ~93% of their cell,
                      # and the game sizes a drone by its sprite bounds, so the on-screen size stays the same
PIVOT = (0.5, 0.12)   # the roster tiles' pivot (UltimateGun.GunSpriteFor)
FRAMES = 4
FPS = 6.0
PULSE = [0.0, 0.3, 0.55, 0.3]
HUE_TOL = 17          # armour = hue within this of the armour hue (PIL 0-255 hue); per ship: src~/drones/config.json
MIN_SPECK = 6         # opaque islands smaller than this (px) are stray pixels and removed


# ------------------------------------------------------------------ colour helpers

def hexrgb(h):
    h = (h or "").lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32) if len(h) == 6 else None


def hsv_of(rgb):
    """uint8 HxWx3 -> float h, s, v in 0..255 (PIL's HSV)."""
    hsv = np.array(Image.fromarray(rgb.astype(np.uint8)).convert("HSV")).astype(np.float32)
    return hsv[..., 0], hsv[..., 1], hsv[..., 2]


def hue_of(hexcol):
    c = hexrgb(hexcol).astype(np.uint8).reshape(1, 1, 3)
    return hsv_of(c)[0][0, 0]


def luma(rgb):
    return rgb[..., 0] * .299 + rgb[..., 1] * .587 + rgb[..., 2] * .114


def core_mask(a, stock_hue):
    """Glowing cores / lamps that share the armour's hue family (Paranoid's green heal core): compact blobs of very
    bright saturated pixels (thin rims do not survive the opening) whose hue is still off the armour's own (>= 6/255;
    the armour's bright highlight plates sit within ~2), grown one pixel into their same-hue ring. Kept on every skin."""
    h, s, v = hsv_of(a[..., :3])
    on = a[..., 3] > 0
    glow = on & (v > 225) & (s > 140)
    lab, n = ndimage.label(ndimage.binary_opening(glow, structure=np.ones((3, 3))))
    out = np.zeros_like(on)
    for i in range(1, n + 1):
        blob = lab == i
        bh = float(np.median(h[blob]))
        d = abs(bh - stock_hue)
        if min(d, 255 - d) < 6:
            continue
        dh = np.abs(h - bh)
        ring = on & (np.minimum(dh, 255 - dh) < 6) & (v > 120)
        out |= ndimage.binary_dilation(blob, structure=np.ones((3, 3)), iterations=1, mask=ring) | blob
    return out


def armour_mask(a, stock_hue):
    h, s, v = hsv_of(a[..., :3])
    d = np.abs(h - stock_hue)
    d = np.minimum(d, 255 - d)
    return (a[..., 3] > 0) & (d < HUE_TOL) & (s > 70) & (v > 35) & ~core_mask(a, stock_hue)


def light_mask(a, armour):
    h, s, v = hsv_of(a[..., :3])
    return (a[..., 3] > 0) & ~armour & (((v > 190) & (s > 80)) | (v > 235))


def gradient(y, anchors, cols):
    """Piecewise-linear map of luma y through (anchor, colour) pairs (anchors ascending)."""
    out = np.zeros(y.shape + (3,), np.float32)
    xs = np.asarray(anchors, np.float32)
    for ch in range(3):
        out[..., ch] = np.interp(y, xs, np.asarray([c[ch] for c in cols], np.float32))
    return out


def gradient_map(a, mask, shadow, base, high):
    """Recolour the `mask` pixels onto shadow / base / highlight by their own luma rank (keeps the shading)."""
    out = a.copy()
    if mask.sum() < 4:
        return out
    y = luma(a[..., :3].astype(np.float32))
    ym = y[mask]
    p10, p50, p92 = np.percentile(ym, 10), np.percentile(ym, 50), np.percentile(ym, 92)
    p50 = max(p50, p10 + 1)
    p92 = max(p92, p50 + 1)
    lift = high + (255 - high) * .45
    anchors = [0, p10, p50, p92, 255]
    cols = [shadow * .25, shadow, base, high, lift]
    new = gradient(y, anchors, cols)
    out[..., :3] = np.where(mask[..., None], np.clip(new, 0, 255), a[..., :3]).astype(np.uint8)
    return out


def pattern_mask(kind, alpha):
    """Livery region of a Special skin over the drawing's bbox (racing / chevrons / diagonal / split)."""
    ys, xs = np.nonzero(alpha)
    if len(xs) == 0:
        return np.zeros_like(alpha, bool)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    w, h = x1 - x0 + 1, y1 - y0 + 1
    cx = (x0 + x1) / 2.0
    yy, xx = np.mgrid[0:alpha.shape[0], 0:alpha.shape[1]].astype(np.float32)
    dx = np.abs(xx + .5 - (cx + .5))
    if kind == "racing":
        m = (dx >= w * .05) & (dx <= w * .14)
    elif kind == "chevrons":
        period = max(6.0, h * .3)
        m = np.mod((yy - y0) + dx * .9, period) < period * .38
    elif kind == "diagonal":
        period = max(6.0, w * .3)
        m = np.mod(xx + yy, period) < period * .42
    elif kind == "split":
        m = xx + .5 < cx + .5
    else:
        m = np.zeros_like(alpha, bool)
    return m & alpha


def recolour(a, stock_hue, skin):
    armour = armour_mask(a, stock_hue)
    kind = skin.get("kind")
    if kind == "Palette":
        return gradient_map(a, armour, hexrgb(skin["shadow"]), hexrgb(skin["hue"]), hexrgb(skin["highlight"]))
    if kind == "Special":
        acc = hexrgb(skin.get("accent"))
        if acc is None:
            return a.copy()
        sh = hexrgb(skin.get("accentShadow")) if skin.get("accentShadow") else acc * .62
        hi = hexrgb(skin.get("accentHighlight")) if skin.get("accentHighlight") else acc + (255 - acc) * .35
        livery = armour & pattern_mask(skin.get("pattern"), a[..., 3] > 0)
        return gradient_map(a, livery, sh, acc, hi)
    return a.copy()


def pulse(a, lights, k):
    out = a.copy()
    if k <= 0:
        return out
    rgb = a[..., :3].astype(np.float32)
    lit = rgb + (255 - rgb) * k
    out[..., :3] = np.where(lights[..., None], lit, rgb).astype(np.uint8)
    return out


def bleed(a, steps=3):
    """Copy edge colours into the transparent margin (alpha stays 0): no dark fringe under bilinear filtering."""
    out = a.copy()
    filled = out[..., 3] > 0
    for _ in range(steps):
        idx = ndimage.distance_transform_edt(~filled, return_distances=False, return_indices=True)
        grow = ndimage.binary_dilation(filled) & ~filled
        out[..., :3][grow] = out[..., :3][idx[0][grow], idx[1][grow]]
        filled = filled | grow
    return out


# ------------------------------------------------------------------ cleaning

def clean(path):
    """Trimmed RGBA array: binary alpha, transparent pixels zeroed, stray specks removed."""
    a = np.array(Image.open(path).convert("RGBA"))
    al = a[..., 3] >= 128
    lab, n = ndimage.label(al, structure=np.ones((3, 3)))
    if n > 1:
        sizes = ndimage.sum(al, lab, range(1, n + 1))
        for i, sz in enumerate(sizes, 1):
            if sz < MIN_SPECK:
                al[lab == i] = False
    a[..., 3] = np.where(al, 255, 0)
    a[~al] = 0
    ys, xs = np.nonzero(al)
    if len(xs) == 0:
        raise SystemExit("empty drawing: " + path)
    a = a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    limit = CELL - 8
    if max(a.shape[:2]) > limit:
        raise SystemExit("%s is %dx%d, larger than %d px: redraw smaller or raise CELL" % (path, a.shape[1], a.shape[0], limit))
    return a


# ------------------------------------------------------------------ build

def write_meta(path, importer="TextScriptImporter"):
    meta = path + ".meta"
    if os.path.exists(meta):
        return
    with open(meta, "w") as f:
        f.write("fileFormatVersion: 2\nguid: %s\n%s:\n  externalObjects: {}\n  userData: \n"
                "  assetBundleName: \n  assetBundleVariant: \n" % (uuid.uuid4().hex, importer))


def drone_dir(key):
    return os.path.join(SHIPART, key, "src~", "drones")


def build_ship(key):
    src = drone_dir(key)
    tiers = [t for t in TIERS if os.path.exists(os.path.join(src, t + ".png"))]
    if not tiers:
        return None
    global HUE_TOL
    data = json.load(open(os.path.join(SHIPART, key, key + "_data.json")))
    # optional per-ship override when the drones' armour is not the data file's stock hue (Paragon: its painted
    # hull and drones are indigo / violet, its data hue is the old blue):  {"armourHue": 180, "hueTol": 26}
    cfgp = os.path.join(src, "config.json")
    cfg = json.load(open(cfgp)) if os.path.exists(cfgp) else {}
    ah = cfg.get("armourHue")
    stock_hue = hue_of(ah) if isinstance(ah, str) else float(ah) if ah is not None else hue_of(data["hue"][0])
    HUE_TOL = cfg.get("hueTol", 17)
    skins = [s for s in data.get("skins", []) if s.get("kind") != "Stock"]

    rows = len(tiers)
    sheets = {None: np.zeros((rows * CELL, FRAMES * CELL, 4), np.uint8)}
    for s in skins:
        sheets[s["name"]] = np.zeros_like(sheets[None])
    layout = []
    for r, t in enumerate(tiers):
        d = clean(os.path.join(src, t + ".png"))
        h, w = d.shape[:2]
        ox, oy = (CELL - w) // 2, (CELL - h) // 2                 # top-left of the drawing in its cell (image coords)
        cell = np.zeros((CELL, CELL, 4), np.uint8)
        cell[oy:oy + h, ox:ox + w] = d
        armour = armour_mask(cell, stock_hue)
        lights = light_mask(cell, armour)
        variants = {None: cell}
        for s in skins:
            variants[s["name"]] = recolour(cell, stock_hue, s)
        for name, base in variants.items():
            for f in range(FRAMES):
                sheets[name][r * CELL:(r + 1) * CELL, f * CELL:(f + 1) * CELL] = bleed(pulse(base, lights, PULSE[f]))
        # sprite rect: a square around the drawing (FILL of it), centred on the drawing, inside the cell
        side = min(CELL, int(np.ceil(max(w, h) / FILL)))
        cx, cy = ox + w / 2.0, oy + h / 2.0
        x = int(round(cx - side / 2.0))
        y_img = int(round(cy - side / 2.0))
        x = max(0, min(CELL - side, x))
        y_img = max(0, min(CELL - side, y_img))
        # Unity rects count from the bottom of the texture
        y_tex = (rows - 1 - r) * CELL + (CELL - (y_img + side))
        layout.append({"tier": t, "row": r, "x": x, "y": int(y_tex), "w": side, "h": side,
                       "drawW": int(w), "drawH": int(h), "lights": int(lights.sum()), "armour": int(armour.sum())})

    out = {"cell": CELL, "frames": FRAMES, "fps": FPS, "pivotX": PIVOT[0], "pivotY": PIVOT[1],
           "tiers": [{k: v for k, v in l.items() if k in ("tier", "row", "x", "y", "w", "h")} for l in layout],
           "skins": [s["name"] for s in skins]}
    folder = os.path.join(SHIPART, key)
    jp = os.path.join(folder, key + "_dronelayout.json")
    with open(jp, "w") as f:
        json.dump(out, f, indent=2)
        f.write("\n")
    write_meta(jp)
    for name, sheet in sheets.items():
        p = os.path.join(folder, key + "_drones" + ("" if name is None else "_" + name) + ".bytes")
        Image.fromarray(sheet, "RGBA").save(p, format="PNG", optimize=True)
        write_meta(p)
    # a skin that no longer exists leaves no stale sheet behind
    keep = {key + "_drones" + ("" if n is None else "_" + n) + ".bytes" for n in sheets}
    for p in glob.glob(os.path.join(folder, key + "_drones*.bytes")):
        if os.path.basename(p) not in keep:
            os.remove(p)
            if os.path.exists(p + ".meta"):
                os.remove(p + ".meta")
    return layout, sheets, skins


def import_picks(reviews, candidates):
    picked = {}
    for f in sorted(glob.glob(os.path.join(reviews, "drone_*.json"))):
        d = json.load(open(f))
        d = d.get("data", d)
        name = os.path.basename(f)[:-5]                      # drone_<Key>_<tier>
        _, key, tier = name.split("_", 2)
        if d.get("status") != "approved" or tier not in TIERS:
            continue
        i = int(d["pick"].split("#")[1])
        srcp = os.path.join(candidates, name, "r1_%d.png" % i)
        dst = drone_dir(key)
        os.makedirs(dst, exist_ok=True)
        shutil.copyfile(srcp, os.path.join(dst, tier + ".png"))
        picked.setdefault(key, {})[tier] = {"pick": d["pick"], "candidate": "r1_%d.png" % i}
    for key, tiers in picked.items():
        pj = os.path.join(drone_dir(key), "picks.json")
        old = json.load(open(pj)) if os.path.exists(pj) else {}
        old.update(tiers)
        with open(pj, "w") as f:
            json.dump(old, f, indent=2, sort_keys=True)
            f.write("\n")
    return picked


def contact(results, out):
    """Per ship: one row per skin (stock first), the four tiers side by side, then the stock pulse frames."""
    S = 2
    pad = 6
    blocks = []
    for key, (layout, sheets, skins) in results.items():
        names = [None] + [s["name"] for s in skins]
        tiers = {l["tier"]: l["row"] for l in layout}
        wcells = len(TIERS) + 1 + FRAMES
        img = Image.new("RGBA", (wcells * (CELL * S + pad), len(names) * (CELL * S + pad) + 20), (14, 20, 36, 255))
        for ri, n in enumerate(names):
            sh = sheets[n]
            for ci, t in enumerate(TIERS):
                if t not in tiers:
                    continue
                r = tiers[t]
                c = Image.fromarray(sh[r * CELL:(r + 1) * CELL, 0:CELL]).resize((CELL * S, CELL * S), Image.NEAREST)
                img.alpha_composite(c, (ci * (CELL * S + pad), 20 + ri * (CELL * S + pad)))
            if n is None:
                r = tiers.get("heal", tiers[layout[0]["tier"]])
                for f in range(FRAMES):
                    c = Image.fromarray(sh[r * CELL:(r + 1) * CELL, f * CELL:(f + 1) * CELL]).resize((CELL * S, CELL * S), Image.NEAREST)
                    img.alpha_composite(c, ((len(TIERS) + 1 + f) * (CELL * S + pad), 20))
        from PIL import ImageDraw
        dr = ImageDraw.Draw(img)
        dr.text((4, 4), "%s   rows: %s   cols: %s | pulse frames" % (key, ", ".join(n or "Stock" for n in names), " ".join(TIERS)), fill=(230, 230, 230, 255))
        blocks.append(img)
    W = max(b.width for b in blocks)
    H = sum(b.height + 10 for b in blocks)
    sheet = Image.new("RGBA", (W, H), (40, 40, 48, 255))
    y = 0
    for b in blocks:
        sheet.alpha_composite(b, (0, y))
        y += b.height + 10
    sheet.convert("RGB").save(out)


def main(argv):
    out_contact = None
    if "--contact" in argv:
        i = argv.index("--contact")
        out_contact = argv[i + 1]
        argv = argv[:i] + argv[i + 2:]
    if argv[:1] == ["--import-picks"]:
        picked = import_picks(argv[1], argv[2])
        print("imported", sum(len(v) for v in picked.values()), "picks for", len(picked), "ships")
        argv = argv[3:]
    keys = [shiplib.resolve(a) for a in argv] if argv else [k for k in shiplib.all_keys() if os.path.isdir(drone_dir(k))]
    results = {}
    for key in keys:
        r = build_ship(key)
        if r is None:
            print("%-12s no drone art (fallback: roster tile)" % key)
            continue
        results[key] = r
        print("%-12s %s" % (key, "  ".join("%s %dx%d rect %d armour %d lights %d" % (l["tier"], l["drawW"], l["drawH"], l["w"], l["armour"], l["lights"]) for l in r[0])))
    if out_contact and results:
        contact(results, out_contact)
        print("contact sheet:", out_contact)


if __name__ == "__main__":
    main(sys.argv[1:])
