#!/usr/bin/env python3
"""Python ports of the elite art tests' pixel rules (EliteTest, EliteBankTest, EliteEngineFireTest, FrameElitesDamagedTest,
ElitePolishTest, the dock / framing tests, EnemyRosterTest.CellsHoldOnePoseEach, EliteHdTest, EliteDeathStripTest, HeartsInArtTest,
ShipTierTest), run on the INSTALLED files so a redraw can be checked without Unity.

    python3 verify.py [key ...]        prints FAIL lines and a per-key summary
"""
import json, math, sys, hashlib
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ART = HERE.parents[3]
RES = ART / "Resources/Elites"
MOUTHS = ART / "Enemies/src~/elite_engine_fire/engine_mouths.txt"
S = 384
RING = [(221, 243, 255), (207, 224, 255), (214, 226, 246), (210, 244, 230), (246, 226, 176), (255, 226, 184), (228, 246, 184)]
HEAVY = {"hauler", "bastion", "siege", "tug", "fortress", "wrecker", "barge", "carrier", "patrol", "logger", "lumber", "abyss", "netter",
         "pearler", "kiln", "furnace", "caravan", "lenspost", "bulwark", "thunderhead", "airship", "citadel", "deep", "trawl", "oyster"}
fails = []


def F(key, msg):
    fails.append(f"{key}: {msg}"); print("FAIL", key, msg)


def cells_of(p):
    if not p.exists(): return []
    a = np.array(Image.open(p).convert("RGBA"))
    return [a[:, i * S:(i + 1) * S] for i in range(a.shape[1] // S)]


def bbox(c, thr=8):
    ys, xs = np.nonzero(c[..., 3] > thr)
    if not len(xs): return None
    return xs.min(), ys.min(), xs.max(), ys.max()


def lum(p): return .3 * p[0] + .59 * p[1] + .11 * p[2]


def hsv(c):
    rgb = c[..., :3].astype(np.float32) / 255; mx = rgb.max(-1); mn = rgb.min(-1)
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0); d = np.maximum(mx - mn, 1e-6)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) / 6
    return h, s, mx


def runs(c):
    op = c[..., 3] > 128; worst = 0
    for side in (-1, 1):
        nb = np.zeros_like(op)
        if side < 0: nb[:, 1:] = c[:, :-1, 3] != 0
        else: nb[:, :-1] = c[:, 1:, 3] != 0
        if side < 0: nb[:, 0] = False
        else: nb[:, -1] = False
        e = op & ~nb
        for x in range(S):
            r = 0
            for y in range(S):
                r = r + 1 if e[y, x] else 0
                worst = max(worst, r)
    return worst


def block_shares(c):
    """EliteHdTest.BlockShares, both parities (Unity's rows run bottom-up)."""
    a = c.astype(int); ok = a[..., 3] >= 250
    out = []
    for p in (0, 1):
        sub = a[p:p + (S - p) // 2 * 2, p:p + (S - p) // 2 * 2]; o = ok[p:p + (S - p) // 2 * 2, p:p + (S - p) // 2 * 2]
        q = [sub[0::2, 0::2], sub[0::2, 1::2], sub[1::2, 0::2], sub[1::2, 1::2]]
        qo = o[0::2, 0::2] & o[0::2, 1::2] & o[1::2, 0::2] & o[1::2, 1::2]
        flat = np.all(q[0] == q[1], -1) & np.all(q[0] == q[2], -1) & np.all(q[0] == q[3], -1)
        out.append((flat & qo).sum() / max(1, qo.sum()))
    return out


def on_hull(c, x, y, r=4, thr=128):
    return (c[max(0, y - r):y + r + 1, max(0, x - r):x + r + 1, 3] > thr).any()


def check(key):
    d = json.load(open(RES / "Defs" / f"{key}.json")); world = d["world"]; W = world.capitalize(); cc = d["cells"]
    main = cells_of(RES / W / f"{key}.png"); dm = cells_of(RES / W / f"{key}_damaged.png"); bk = cells_of(RES / W / f"{key}_banks.png")
    allc = main + dm + bk
    death = cells_of(RES / W / f"{key}_death.png")
    lights = cells_of(RES / W / f"{key}_lights.png") + cells_of(RES / W / f"{key}_lights_damaged.png") + cells_of(RES / W / f"{key}_lights_banks.png")
    flight = cc["flight"]; f0 = main[flight[0]]
    # margins, columns, vertical runs, outline ring, block doubling
    for i, c in enumerate(allc):
        b = bbox(c, 0)
        if b and (b[0] < 6 or b[1] < 6 or b[2] > S - 7 or b[3] > S - 7): F(key, f"cell {i} inside the 6 px margin {b}")
        w = runs(c)
        if w >= 58: F(key, f"cell {i} straight vertical edge {w}")
    for nm, cs in (("lights_damaged", cells_of(RES / W / f"{key}_lights_damaged.png")), ("lights_banks", cells_of(RES / W / f"{key}_lights_banks.png"))):
        for i, c in enumerate(cs):
            w = runs(c)
            if w >= 58: F(key, f"{nm} cell {i} straight vertical edge {w}")
    edge_n = ring_n = 0
    for c in allc + death:
        op = c[..., 3] > 128
        e = op & ~(np.roll(op, 1, 0) & np.roll(op, -1, 0) & np.roll(op, 1, 1) & np.roll(op, -1, 1))
        px = c[e][:, :3].astype(int); edge_n += len(px)
        for t in RING: ring_n += int((np.abs(px - np.array(t)).sum(1) <= 6).sum())
    if ring_n / max(1, edge_n) >= .02: F(key, f"outline-ring tones {ring_n / edge_n:.3f}")
    for ci in (3, cc["damaged"]):
        al, off = block_shares(main[ci])[0], block_shares(main[ci])[1]
        for a_, o_ in ((al, off), (off, al)):
            if not (a_ < .92 and a_ < o_ * 1.6 + .12): F(key, f"cell {ci} block-doubled {a_:.2f}/{o_:.2f}")
    # muzzles / nozzles on the art
    r = 2 * S // 192
    for m in d["muzzles"]:
        if not on_hull(f0, m["x"], m["y"], r): F(key, f"muzzle {m['name']} off the art")
        lx = (m["x"] - S / 2) * d["cellWorldSize"] / S; ly = (S / 2 - m["y"]) * d["cellWorldSize"] / S
        if math.hypot(lx, ly) >= d["cellWorldSize"] * .42: F(key, f"muzzle {m['name']} too far out")
        if not (.04 * S <= m["x"] <= .96 * S and .04 * S <= m["y"] <= .96 * S): F(key, f"muzzle {m['name']} near the edge")
    total = len(allc)
    for nz in d["nozzles"]:
        if not on_hull(f0, nz["x"], nz["y"], r): F(key, f"nozzle {nz['name']} off the art")
        if len(nz["offsets"]) != 2 * total: F(key, f"nozzle {nz['name']} offsets {len(nz['offsets'])} != {2 * total}")
        p = main[0][nz["y"], nz["x"]]
        if world in ("ember", "tide", "space") or key in ("frost_elite_cryo_siren", "frost_elite_rimebreaker"):
            if key != "tide_elite_bathyscaphe" and not (p[3] == 255 and lum(p) < 70): F(key, f"nozzle {nz['name']} not on a dark bell on cell 0 {p}")
        mount_cells = set(flight) | {cc["liftoff"]} | set(cc.get("bankLeftLoop") or [cc["bankLeft"]]) | set(cc.get("bankRightLoop") or [cc["bankRight"]])
        for ci in mount_cells:
            x = nz["x"] + nz["offsets"][2 * ci]; y = nz["y"] + nz["offsets"][2 * ci + 1]
            if not on_hull(allc[ci], int(x), int(y), 3): F(key, f"nozzle {nz['name']} mount off cell {ci}")
        for bc in set(cc.get("bankLeftLoop") or [cc["bankLeft"]]) | set(cc.get("bankRightLoop") or [cc["bankRight"]]):
            if nz["offsets"][2 * bc] == 0 and nz["offsets"][2 * bc + 1] == 0: F(key, f"nozzle {nz['name']} does not move on bank cell {bc}")
        # flame covers the nozzle
        burn = list(range(2, len(main))) if world == "space" else [2] + flight
        if world == "space": burn = [ci for ci in range(2, 17)]
        for ci in burn:
            if ci == cc["damaged"] and world != "space": continue
            x = nz["x"] + nz["offsets"][2 * ci]; y = nz["y"] + nz["offsets"][2 * ci + 1]
            c = allc[ci]; c0 = main[0]
            win = (0, 0) if world == "space" else (2, 5)
            ok = False
            for yy in range(y - win[0], y + win[0] + 1):
                for xx in range(x - win[1], x + win[1] + 1):
                    q = c[yy, xx]
                    if q[3] == 255 and (q[:3] != c0[yy, xx, :3]).any() and q[:3].max() > 150: ok = True
            if not ok: F(key, f"no flame over nozzle {nz['name']} on cell {ci}")
    # flame tips
    hb = bbox(main[0], 128)[3]
    tips = []
    for nz in d["nozzles"]:
        col = main[3][:, max(0, nz["x"] - 3):nz["x"] + 4, 3] > 128
        ys = np.nonzero(col.any(1))[0]; tips.append(ys.max())
        if ys.max() < hb + 10: F(key, f"flame tip {nz['name']} {ys.max()} < hull bottom {hb} + 10")
    if len(tips) > 1 and len(set(tips)) < 2: F(key, "every engine burns the same length in cell 3")
    # EliteEngineFireTest: flight cells identical outside the engine zone, later cells differ in the flames
    if world != "space":
        zone = np.zeros((S, S), bool); yy, xx = np.indices((S, S))
        for nz in d["nozzles"]: zone |= (np.abs(xx - nz["x"]) <= 48) & (yy >= nz["y"] - 28) & (yy < 378)
        for ci in flight[1:]:
            diff = (main[ci] != main[flight[0]]).any(-1)
            if (diff & ~zone).sum(): F(key, f"flight cell {ci} differs outside the engine zone ({(diff & ~zone).sum()} px)")
            if (diff & zone).sum() < 40: F(key, f"flight cell {ci} flames barely differ")
    # damaged cell (FrameElitesDamagedTest)
    dc = main[cc["damaged"]]; a0 = main[flight[0]][..., 3] >= 96; ad = dc[..., 3] >= 96
    iou = (a0 & ad).sum() / max(1, (a0 | ad).sum())
    if iou < .85: F(key, f"damaged IoU {iou:.2f}")
    ytop = min(n["y"] for n in d["nozzles"])
    rows = np.arange(S)[:, None] < ytop - 2
    hullpx = (a0 | ad) & rows
    ch = ((np.abs(dc.astype(int) - main[flight[0]].astype(int))[..., :3].max(-1) > 35) | (a0 != ad)) & hullpx
    if ch.sum() / max(1, hullpx.sum()) < .04: F(key, f"damaged changes only {ch.sum() / hullpx.sum():.3f} of the hull")
    fl = (np.arange(S)[:, None] >= ytop + 4) & (dc[..., 3] >= 200) & ((dc[..., :3].max(-1).astype(int) - dc[..., :3].min(-1)) > 90)
    if fl.sum() < 120: F(key, f"damaged flame px {fl.sum()} < 120")
    # damaged loop
    loop = cc.get("damagedLoop") or []
    if loop:
        for nz in d["nozzles"]:
            hs = []; lit = 0
            for ci in loop:
                c = allc[ci]; x0, x1 = nz["x"] - S // 14, nz["x"] + S // 14; y0, y1 = nz["y"], nz["y"] + S // 5
                reg = c[y0:y1, x0:x1]; hs.append(hashlib.md5(reg.tobytes()).hexdigest())
                par = main[0][y0:y1, x0:x1].astype(int); rr = reg.astype(int)
                fp = ((np.abs(rr[..., :3] - par[..., :3]).max(-1) > 60) | (np.abs(rr[..., 3] - par[..., 3]) > 100)) & (rr[..., 3] > 200) & (rr[..., :3].max(-1) > 150)
                lit += fp.sum() > 12
            if len(set(hs)) < 4: F(key, f"damaged loop {nz['name']}: {len(set(hs))} distinct")
            if any(hs[i] == hs[(i + 1) % len(hs)] for i in range(len(hs))): F(key, f"damaged loop {nz['name']}: repeated cell")
            if lit < len(loop) - 2: F(key, f"damaged loop {nz['name']}: lit in {lit}/{len(loop)}")
    # lights
    if lights:
        if len(lights) != total: F(key, f"lights cells {len(lights)} != {total}")
        cov = (lights[3][..., 3] > 0).mean()
        lim = .08 if world in ("space", "frost") else .1
        if not 0 < cov < lim: F(key, f"lights coverage {cov:.3f}")
        if world == "frost":
            for i in range(len(main)):
                lit = lights[i][..., 3] > 0
                if lit.sum() == 0: F(key, f"lights cell {i} dark")
                elif (lit & (main[i][..., 3] >= 128)).sum() / lit.sum() < .8: F(key, f"lights cell {i} off the hull")
    # death
    if len(death) != 3: F(key, "death cells != 3")
    else:
        for i, c in enumerate(death):
            b = bbox(c, 127)
            if b and (b[0] < 12 or b[1] < 12 or b[2] > S - 13 or b[3] > S - 13): F(key, f"death cell {i} inside the 12 px margin")
        if world in ("ember", "frost", "space") and key not in ("frost_elite_cryo_siren", "frost_elite_rimebreaker"):
            h0 = main[0][..., 3] > 127; d0 = death[0][..., 3] > 127
            if (h0 & d0).sum() / h0.sum() < .98: F(key, f"death cell 0 covers {(h0 & d0).sum() / h0.sum():.3f} of the hull")
            pk = sum(((c[..., 0] > 229) & (c[..., 1] < 115) & (c[..., 2] > 178) & (c[..., 3] > 127)).sum() for c in death)
            if world == "ember" and pk > 20: F(key, f"death pink {pk}")
            if world != "ember" and pk <= 20: F(key, f"death pink accent missing {pk}")
    # silhouette / size
    best = 0
    for c in allc:
        ys, xs = np.nonzero(c[..., 3] >= 128)
        best = max(best, math.sqrt(((xs + .5 - S / 2) ** 2 + (ys + .5 - S / 2) ** 2).max()) / S)
    if abs(best - d["silhouetteShare"]) > .003: F(key, f"silhouetteShare {d['silhouetteShare']} vs {best:.4f}")
    drawn = 0
    for ci in flight:
        b = bbox(main[ci], 40); drawn = max(drawn, max(b[2] - b[0] + 1, b[3] - b[1] + 1) / S * d["cellWorldSize"])
    floor = (1.7 if d["brain"] in HEAVY else 1.5) * .78 * .97
    if not floor <= drawn < 2.4: F(key, f"drawn {drawn:.3f} outside [{floor:.3f}, 2.4)")
    if world == "frost" and drawn > 1.7: F(key, f"frost drawn {drawn:.3f} > 1.7")
    rt = d["hullRadius"] / (drawn * .5)
    if not .35 <= rt <= .8: F(key, f"hullRadius ratio {rt:.2f}")
    if d["hullRadius"] * d["heartOrbit"] + d["heartSize"] * 1.05 >= .52: F(key, "heart orbit too wide")
    # framing
    tb = [bbox(c) for c in main]
    def ptp(v): return max(v) - min(v)
    cxs = [(b[0] + b[2]) / 2 for b in tb]; tops = [b[1] for b in tb]; wid = [b[2] - b[0] for b in tb]; hts = [b[3] - b[1] for b in tb]
    if world == "tide":
        if ptp(tops) > 6 or ptp(cxs) > 6: F(key, f"tide framing top {ptp(tops)} cx {ptp(cxs)}")
        if ptp([wid[i] for i in (0, 1, 2, 3, len(main) - 1)]) > 10: F(key, "tide framing width")
    if world == "storm" and (ptp(cxs) > 12 or ptp(tops) > 12 or ptp(wid) > 40): F(key, f"storm framing {ptp(cxs)} {ptp(tops)} {ptp(wid)}")
    if world == "desert":
        if ptp(cxs[3:7]) > 14 or ptp(wid[3:7]) > 24: F(key, f"desert framing cx {ptp(cxs[3:7])} w {ptp(wid[3:7])}")
        cys = [(b[1] + b[3]) / 2 for b in tb]
        if ptp(cys[3:6]) > 8 or ptp(hts[3:6]) > 12: F(key, f"desert framing cy {ptp(cys[3:6])} h {ptp(hts[3:6])}")
    if world == "ember":
        if ptp(tops[:7]) > 6 or ptp(cxs[:7]) > 10 or ptp([wid[i] for i in (0, 1, 3, 4)]) > 60: F(key, "ember framing")
        if abs(tops[7] - tops[3]) > 16 or abs(cxs[7] - cxs[3]) > 10: F(key, "ember damaged framing")
        if ((np.abs(main[3].astype(int) - main[4].astype(int)).sum(-1) > 40).sum()) < 400: F(key, "ember 3 vs 4")
        if ((np.abs(main[3].astype(int) - main[7].astype(int)).sum(-1) > 40).sum()) < 4000: F(key, "ember 3 vs 7")
    if key in ("frost_elite_cryo_siren", "frost_elite_rimebreaker"):
        if ptp(tops[:7]) > 10 or ptp(cxs[:7]) > 18 or ptp(wid[:7]) > 40: F(key, "frost A framing")
        hy = d["nozzles"][0]["y"] - 6
        def df(a, b):
            A, B = main[a][:hy].astype(int), main[b][:hy].astype(int)
            return ((np.abs(A - B)[..., :3].sum(-1) > 48) | ((A[..., 3] > 128) != (B[..., 3] > 128))).sum()
        for (a, b), mn in {(0, 3): 1200, (2, 3): 600, (3, 7): 1500, (3, 8): 1500, (7, 8): 1500, (3, 9): 3000, (0, 2): 400}.items():
            if df(a, b) < mn: F(key, f"frost A cells {a}/{b} differ {df(a, b)} < {mn}")
    return dict(share=round(best, 4), drawn=round(drawn, 3), lights_cov=round(float((lights[3][..., 3] > 0).mean()), 4) if lights else None)


def seats(keys):
    bad = 0; n = 0
    for ln in MOUTHS.read_text().splitlines():
        p = ln.split()
        if len(p) != 6: continue
        n += 1
        if p[0] not in keys: continue
        k, ci = p[0], int(p[1]); cx, cy, rx, ry = map(float, p[2:])
        W = k.split("_")[0].capitalize()
        c = cells_of(RES / W / f"{k}.png")[ci]
        yr = int(math.floor(cy - ry * .5))
        row = c[yr - 4, int(cx - .6 * rx):int(cx + .6 * rx) + 1]
        w = max(rx, 6)
        band = c[yr + 4:yr + 10, int(cx - w):int(cx + w) + 1].astype(int)
        mx = band[..., :3].max(-1); mn = band[..., :3].min(-1)
        fire = (band[..., 3] > 200) & (mx > 130) & ((mx - mn > 50) | (mn > 215))
        ys, xs = np.nonzero(fire)
        ok = (row[:, 3] > 128).mean() >= .5 and len(xs) >= 5 and abs(xs.mean() + int(cx - w) - cx) <= max(3.5, .55 * rx)
        if not ok: bad += 1; F(k, f"seat cell {ci} mouth {p[2:]}")
    print("engine_mouths lines", n, "bad", bad)
    if n <= 400: F("engine_mouths", f"only {n} lines")


if __name__ == "__main__":
    sel = sys.argv[1:]
    picks = json.load(open("/private/tmp/claude-501/-Users-sina-Developer/50b8c9d7-da6c-44fe-8c92-736a84a1fed0/scratchpad/picks_all.json"))["approved"]
    keys = sorted(k for k, v in picks.items() if v["group"] == "elite")
    if sel: keys = [k for k in keys if any(s in k for s in sel)]
    for k in keys:
        print(k, check(k))
    seats(set(keys))
    print(len(fails), "failures")
