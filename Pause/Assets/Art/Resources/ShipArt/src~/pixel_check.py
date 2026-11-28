#!/usr/bin/env python3
"""Mirrors the EditMode tests' pixel / byte / number rules for one ship's art (CI is the real judge).

    python3 pixel_check.py <Key> [<Key> ...]

ShipArtTest (size vs oldRects, clean binary alpha, clear top/bottom PNG rows), ShipSkinsTest (5 skins, alpha ==
stock, every cell drawn, a changed pixel), ShipDamageTest (row alpha identity, damage counts, emitters on paint
in every pose), SkinDamageGameplayTest (col-0 damage counts, with margin), ShipNozzlesTest (inside / lower half
/ scale / near paint per column / col-0 offset 0 / 18 offsets / Paranoid outboard), ExhaustStyleTest (frames ==
ticks, boost brighter + longer, coverage), ShipHitboxTest + PickupHitchTest (bakes == fresh bake, hull polygon on
paint, 2 px inset, area >= .8 body, shield clears hull), RailsShipSizeTest (hull width / max(w,h) < .963),
ShipRotorsTest (hubs inside the rect).
"""
import io
import math
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import shiplib  # noqa: E402
import bake_silhouettes as bake  # noqa: E402
from pixel_restyle import OLD_RECTS, IDLE_DY  # noqa: E402

CELL = 256
fails = []


def check(name, ok, info=""):
    print("  %-4s %s %s" % ("ok" if ok else "FAIL", name, info))
    if not ok:
        fails.append(name)


def posed(u, v, col):
    dy = IDLE_DY[col] if col < 6 else 0
    b = -1 if col == 6 else (1 if col == 7 else 0)
    if b:
        d = u - 64
        near = (d < 0) == (b < 0)
        k = .8 if near else .95
        u = 64 + d * k + 1.5 * b
        v += (1.2 if near else -.6) * abs(d) / 60
    return u, v + dy


def load(p):
    return np.array(Image.open(p).convert("RGBA"))


def run(key):
    print(key)
    sid = shiplib.ship_id(key)
    data = shiplib.load_data(key)
    spinner = data["exhaust"]["kind"] in ("SpinBlades", "SpinVortex")
    sheet = load(shiplib.hull_path(key))
    A = sheet[..., 3]
    r = data["rect"]
    rx, ry, w, h, ppu = r["x"], r["y"], r["w"], r["h"], r["ppu"]
    check("sheet 2304x768", sheet.shape[:2] == (768, 2304))
    # size (ShipArtTest.Size)
    ow, oh = OLD_RECTS[sid]
    m = max(w, h)
    mo = max(ow, oh)
    world = (w / m, h / m)
    oldw = (ow / mo, oh / mo)
    within = lambda a, b: all(abs(x - y) <= y * .1 for x, y in zip(a, b))
    check("world size within 10% of old", within(world, oldw), "%s vs %s" % (np.round(world, 3), np.round(oldw, 3)))
    check("bounds within 10% (ppu)", within((w / ppu, h / ppu), (ow / 100, oh / 100)), "%.3f %.3f vs %.2f %.2f" % (w / ppu, h / ppu, ow / 100, oh / 100))
    check("alpha binary", set(np.unique(A)) <= {0, 255})
    check("png top/bottom rows clear", A[0].max() == 0 and A[-1].max() == 0)
    check("rect inside cell", 0 <= rx and rx + w <= CELL and 0 <= ry and ry + h <= CELL)
    # every cell's art inside the rect
    top = CELL - ry - h
    inside = True
    for st in range(3):
        for c in range(9):
            cell = A[st * CELL:(st + 1) * CELL, c * CELL:(c + 1) * CELL]
            outside = cell.copy()
            outside[top:top + h, rx:rx + w] = 0
            inside &= outside.max() == 0
    check("all art inside the rect", inside)
    # rows share alpha
    check("rows 1-2 alpha == row 0", all((A[s * CELL:(s + 1) * CELL] == A[:CELL]).all() for s in (1, 2)))
    # damage counts (ShipDamageTest)
    def counts(sh):
        a0 = sh[:CELL, :, 3] >= 250
        a0[:, 8 * CELL:] = False
        rows = [sh[s * CELL:(s + 1) * CELL, :, :3].astype(int) for s in range(3)]
        diff = lambda p, q: (np.abs(p - q).sum(-1) > 24) & a0
        return diff(rows[1], rows[0]).sum(), diff(rows[2], rows[0]).sum(), diff(rows[2], rows[1]).sum(), a0.sum()
    c10, c20, c21, op = counts(sheet)
    check("damage row1 > 4800, row2-row1 > 4800, row2 > row1", c10 > 4800 and c21 > 4800 and c20 > c10, "%d %d %d" % (c10, c20, c21))
    check("wreck >= 20%% of hull and > 1.2x damaged", c20 / op >= .2 and c20 > 1.2 * c10, "%.3f, %.2fx" % (c20 / op, c20 / max(1, c10)))
    # skins
    skins = data["skins"]
    check("5 skins", len(skins) == 5)
    for sk in skins[1:]:
        p = shiplib.skin_path(key, sk["name"])
        s = load(p)
        ok = s.shape == sheet.shape and (s[..., 3] == A).all()
        drawn = all((s[st * CELL:(st + 1) * CELL:2, c * CELL + 88:c * CELL + 168:2, 3] > 200).any() for st in range(3) for c in range(9))
        changed = ((s[..., :3] != sheet[..., :3]).any(-1) & (A > 250)).any()
        k10, k20, k21, _ = counts(s)
        check("skin %s: alpha == stock, cells drawn, recoloured, damage" % sk["name"], ok and drawn and changed and k10 > 4800 and k21 > 4800 and k20 > k10,
              "%d %d %d" % (k10, k20, k21))
        # SkinDamageGameplayTest (col 0, |d| > 48, a >= 128), margin x1.5
        for st, need in ((1, 400), (2, 4000)):
            cur = s[st * CELL:(st + 1) * CELL, :CELL].astype(int)
            base = s[:CELL, :CELL].astype(int)
            n = ((cur[..., 3] >= 128) & (np.abs(cur[..., :3] - base[..., :3]).sum(-1) > 48)).sum()
            if n <= need * 1.5:
                check("skin %s col0 state %d damage > %d (x1.5)" % (sk["name"], st, need), False, str(n))
    for st, need in ((1, 400), (2, 4000)):
        cur = sheet[st * CELL:(st + 1) * CELL, :CELL].astype(int)
        base = sheet[:CELL, :CELL].astype(int)
        n = ((cur[..., 3] >= 128) & (np.abs(cur[..., :3] - base[..., :3]).sum(-1) > 48)).sum()
        check("stock col0 state %d damage > %d (x1.5)" % (st, need), n > need * 1.5, str(n))
    # emitters
    em = data["emitters"]
    states = [e["state"] for e in em]
    kinds = {e["kind"] for e in em}
    s1 = [e for e in em if e["state"] == 1]
    s2 = [e for e in em if e["state"] == 2]
    sm = lambda L: sum(e["kind"] in ("Smoke", "Smolder") for e in L)
    check("emitters sorted, counts, kinds", states == sorted(states) and 0 not in states and len(s1) >= 2 and len(s2) > len(s1)
          and kinds == {"Sparks", "Arc", "Smoke", "Flame", "Leak", "Smolder"} and sm(s1) >= 2 and sm(em) >= sm(s1) + 3)
    onp = True
    for e in em:
        for col in range(9):
            u, v = posed(e["u"], e["v"], 0 if spinner and col in (6, 7) else col)
            for s in range(e["state"], 3):
                x, y = col * CELL + int(u * 2), s * CELL + int(v * 2)
                if A[y, x] < 204:
                    onp = False
                    print("     off paint", e, col, s)
        lx, ly = e["u"] * 2 - rx, CELL - e["v"] * 2 - ry
        onp &= 0 <= lx <= w and 0 <= ly <= h
    check("emitters on paint in every pose", onp)
    # nozzles
    nz = data["nozzles"]
    ok = len(nz) > 0
    for n in nz:
        ok &= 0 < n["x"] < w and 0 < n["y"] < h and n["y"] < h / 2 and .2 < n["scale"] <= 1.0
        off = n.get("offsets") or []
        ok &= len(off) == 18 and off[0] == 0 and off[1] == 0
        for col in range(9):
            R = 1.5 if col == 0 else 2.0
            x, y = n["x"] + off[2 * col], n["y"] + off[2 * col + 1]
            cx, cy = rx + x, (CELL - ry) - y          # top-down in the cell
            good = False
            k = int(math.ceil(R))
            for yy in range(int(math.floor(cy)) - k, int(math.floor(cy)) + k + 1):
                for xx in range(int(math.floor(cx)) - k, int(math.floor(cx)) + k + 1):
                    if not (rx <= xx < rx + w and CELL - ry - h <= yy < CELL - ry):
                        continue
                    if (xx + .5 - cx) ** 2 + (yy + .5 - cy) ** 2 > (R + .75) ** 2:
                        continue
                    good |= A[yy, col * CELL + xx] >= 128
            if not good:
                print("     nozzle off paint col", col, n["x"], n["y"])
            ok &= good
    check("nozzles inside, lower half, on paint in every column", ok, str([(n["x"], n["y"], n["scale"]) for n in nz]))
    # flames (unchanged sheet, still checked)
    ex = data["exhaust"]
    fl = load(shiplib.flame_path(key))
    strips = {s["layer"]: s for s in ex["strips"]}
    if not spinner:
        ok = strips["Plume"]["frames"] == len(ex["ticks"]) == strips["PlumeBoost"]["frames"]
        def bright_len(s):
            br = ln = 0.0
            for f in range(s["frames"]):
                top = fl.shape[0] - s["y"] - s["h"]
                fr = fl[top:top + s["h"], s["x"] + f * s["stride"]:s["x"] + f * s["stride"] + s["w"]].astype(float) / 255
                br += (fr[::2, ::2, 3] * fr[::2, ::2, :3].sum(-1)).sum()
                rows = np.nonzero((fr[..., 3] > .3).any(1))[0]
                ln += (rows.max() + 1) if len(rows) else 0      # h - first row from the bottom with paint
            return br, ln
        bn, ln = bright_len(strips["Plume"])
        bb, lb = bright_len(strips["PlumeBoost"])
        check("flames: frames == ticks, boost > 1.08x brighter and longer", ok and bb > 1.08 * bn and lb > ln, "%.2fx" % (bb / bn))
    # bakes
    sb, hb, geo = bake.compute(key)
    d = shiplib.ship_dir(key)
    check("shield + hitbox bakes current", open(os.path.join(d, key + "_shield.bytes"), "rb").read() == sb and
          open(os.path.join(d, key + "_hitbox.bytes"), "rb").read() == hb)
    S, sw, shh, sppu = bake.silhouette(key)
    hull = [(float(x) * ppu + w / 2, float(y) * ppu + h / 2) for x, y in geo["hull"]]
    xs = [p[0] for p in hull]
    check("hull hitbox width / max(w,h) < .963 (rails)", (max(xs) - min(xs)) / max(w, h) < .963, "%.3f" % ((max(xs) - min(xs)) / max(w, h)))
    area = abs(sum(hull[i - 1][0] * hull[i][1] - hull[i][0] * hull[i - 1][1] for i in range(len(hull)))) / 2
    Sg = np.zeros((shh + 8, sw + 8), bool)
    Sg[4:-4, 4:-4] = S
    body = bake.body_of(Sg).sum()
    check("hull polygon area >= .80 body, 3..32 points", area >= .8 * body and 3 <= len(hull) <= 32, "%.2f, %d pts" % (area / body, len(hull)))
    # ShieldFitTest: the contour the game cuts from the bake
    shield_fit(key, S, sw, shh, sppu)
    # rotors
    rp = os.path.join(d, key + "_rotors.json")
    if os.path.exists(rp):
        import json
        rot = json.load(open(rp))["rotors"]
        check("rotor hubs inside the rect", all(rx <= q["x"] <= rx + w and ry <= CELL - q["y"] <= ry + h and .2 < q["scale"] < 4 for q in rot))


def shield_fit(key, S, w, h, ppu):
    """ShieldFitTest.ContourShield: covers every hull pixel, hugs it (padding, gaps), smaller than the old bubble,
    has >= 6 plates."""
    F = np.float32
    pivot = (F(F(w) * F(.5)), F(F(h) * F(.5)))
    poly = bake.contour_polygon(S, w, h, pivot, F(ppu))
    P = np.array([(float(x), float(y)) for x, y in poly])
    ppu = float(ppu)
    piv = (float(pivot[0]), float(pivot[1]))
    U = max(w, h) / 30.0
    unit = U / ppu
    ys, xs = np.nonzero(S)
    # 4 inset corners of every opaque pixel inside the polygon
    unc = np.zeros(len(xs), bool)
    for cx, cy in ((.02, .02), (.98, .02), (.02, .98), (.98, .98)):
        qx = ((xs + cx - piv[0]) / ppu).astype(np.float32)
        qy = ((ys + cy - piv[1]) / ppu).astype(np.float32)
        unc |= ~bake.point_in_polygon_grid(poly, qx, qy)
    check("shield covers every hull pixel", not unc.any(), "%d outside" % unc.sum())
    vb = ((xs.min() - piv[0]) / ppu, (ys.min() - piv[1]) / ppu, (xs.max() + 1 - piv[0]) / ppu, (ys.max() + 1 - piv[1]) / ppu)
    ob = (P[:, 0].min(), P[:, 1].min(), P[:, 0].max(), P[:, 1].max())
    md = max(w, h) / ppu
    worst = max(vb[0] - ob[0], ob[2] - vb[2], vb[1] - ob[1], ob[3] - vb[3])
    check("shield outline within 18% padding", 0 < worst <= .18 * md, "%.1f%%" % (worst / md * 100))
    n = len(P)
    E = np.roll(P, -1, 0) - P
    L = np.hypot(E[:, 0], E[:, 1])
    samples = []
    for i in range(n):
        k = max(1, int(math.ceil(L[i] / (.6 * unit))))
        for j in range(k):
            samples.append(P[i] + E[i] * (j / k))
    samples = np.array(samples)
    hull = np.c_[(xs + .5 - piv[0]) / ppu, (ys + .5 - piv[1]) / ppu]
    from scipy.spatial import cKDTree
    dist, _ = cKDTree(hull).query(samples)
    gaps = dist / unit
    check("shield follows the hull (mean <= 5.7U, max <= 9.2U)", gaps.mean() <= 5.7 and gaps.max() <= 9.2,
          "mean %.1fU max %.1fU" % (gaps.mean(), gaps.max()))
    area = abs(np.sum(P[:, 0] * np.roll(P[:, 1], -1) - np.roll(P[:, 0], -1) * P[:, 1])) / 2
    R = max(vb[2] - vb[0], vb[3] - vb[1]) * .5 * 1.35
    check("shield area under the old bubble", area < math.pi * R * R, "%.2f of bubble" % (area / (math.pi * R * R)))
    plen, gap = 3.4 * unit, .6 * unit
    plates = 0
    for l in L:
        c = int(math.floor((l + gap) / (plen + gap)))
        if c == 0 and l > 1.1 * unit:
            c = 1
        plates += c
    check("shield has >= 6 plates", plates >= 6, str(plates))


def main():
    for a in sys.argv[1:]:
        run(shiplib.resolve(a))
    print("FAILED: %s" % fails if fails else "all checks passed")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
