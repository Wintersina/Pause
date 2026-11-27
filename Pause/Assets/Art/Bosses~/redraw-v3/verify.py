#!/usr/bin/env python3
"""Pre-flight: reproduces the boss tests' art numbers on out/<World>/ (BossDamageTest, BossHeartsTest, CalmIdleTest, NoPinkOutsideSpaceTest)."""
import sys, json, math
from pathlib import Path
import numpy as np
from PIL import Image
HERE = Path(__file__).resolve().parent
EXP = {"Space", "Ember", "Tide"}

def box(a, t=24):
    ys, xs = np.nonzero(a[..., 3] > t)
    return None if not len(xs) else (xs.min(), ys.min(), xs.max(), ys.max())

def cheb(mask):
    from scipy.ndimage import distance_transform_cdt
    return distance_transform_cdt(~mask, metric="chessboard")

def longest_straight(m):
    best = 0
    for arr in (m, m.T):
        tr = arr[:, :-1] != arr[:, 1:]          # transitions between columns
        for col in tr.T:
            run = 0
            for v in col:
                run = run + 1 if v else 0; best = max(best, run)
    return best

def pink_share(a):
    rgb = a[..., :3].astype(float) / 255; al = a[..., 3]
    mx = rgb.max(-1); mn = rgb.min(-1); d = mx - mn
    h = np.zeros_like(mx); r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]; nz = d > 1e-6
    i = nz & (mx == r); h[i] = ((g - b)[i] / d[i]) % 6
    i = nz & (mx == g) & (mx != r); h[i] = (b - r)[i] / d[i] + 2
    i = nz & (mx == b) & (mx != r) & (mx != g); h[i] = (r - g)[i] / d[i] + 4
    h *= 60; s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    vis = al > 24
    p = vis & (mx > .25) & (s * mx >= .035) & (h >= 280) & (h < 352)
    return p.sum() / max(1, vis.sum())

def cells(sheet, C, cols):
    return [sheet[r * C:(r + 1) * C, c * C:(c + 1) * C] for r in range(sheet.shape[0] // C) for c in range(cols)]

def diff(a, b): return np.abs(a[..., :3].astype(int) - b[..., :3].astype(int)).max(-1)

def ghost(dmg, pose, idle):
    hit = (dmg[..., 3] > 80) & (idle[..., 3] > 80) & (diff(dmg, idle) <= 6) & (diff(pose, idle) > 62) & (diff(dmg, pose) > 6)
    return int(hit.sum())   # upper bound on the test's 5x5-patch count

def run(w):
    od = HERE / "out" / w
    A = np.asarray(Image.open(od / f"{w}.png").convert("RGBA")); C = A.shape[1] // 5; k = C / 384
    body = cells(A, C, 5)
    i0 = 20 if w in EXP else 0
    idle = body[i0]; ib = box(idle)
    res = {}
    res["border"] = int(sum(((c[:6] > 24).any() or (c[-6:] > 24).any()) for c in [x[..., 3] for x in body]))
    D = cells(np.asarray(Image.open(od / f"{w}_damage.png")), C, 2)
    res["damage_in_box"] = all(box(c)[0] >= ib[0] - 6 and box(c)[1] >= ib[1] - 6 and box(c)[2] <= ib[2] + 6 and box(c)[3] <= ib[3] + 6 for c in D)
    res["damage_AB_differ"] = all((D[2 * s] != D[2 * s + 1]).any() for s in range(4))
    X = cells(np.asarray(Image.open(od / f"{w}_death.png")), C, 6)
    bw = ib[2] - ib[0]
    res["death_width"] = [int(box(c)[2] - box(c)[0] - bw) for c in X]
    res["death_ok"] = all(box(c) is not None and box(c)[2] - box(c)[0] <= bw + round(16 * k) for c in X)
    b0 = box(X[0]); res["death0_reg"] = (abs((b0[0] + b0[2]) / 2 - (ib[0] + ib[2]) / 2), abs((b0[1] + b0[3]) / 2 - (ib[1] + ib[3]) / 2))
    F = np.asarray(Image.open(od / f"{w}_damage_fx.png")); fx = cells(F, C, 6)
    m = round(12 * k); bad_border = 0; straight = 0; haze = 0
    for i, c in enumerate(fx):
        a = c[..., 3]
        bad_border += int((a[:m] > 0).sum() + (a[-m:] > 0).sum() + (a[:, :m] > 0).sum() + (a[:, -m:] > 0).sum())
        if i < 6:
            straight = max(straight, longest_straight(a > 0))
            op = a > 0
            if op.any() and ((a[op] < 48).sum() > op.sum() * .01 or a[op].mean() < 200): haze += 1
    res["fx_border_px"] = bad_border; res["smoke_straight"] = (straight, round(20 * k)); res["smoke_haze"] = haze
    res["fx_cells_differ"] = all((fx[i] != fx[i + 1]).any() for i in (0, 1, 2, 3, 4, 6, 7, 8, 9, 10))
    # damaged attack poses
    names = sorted(p for p in od.glob(f"{w.lower()}_*_stage*.png"))
    poses = {"tell0_0": 5, "tell0_1": 6, "tell1_0": 7, "tell1_1": 8, "tell2_0": 10, "tell2_1": 11, "fire": 9} if w not in EXP else \
            {f"tell{p}_{s}": 26 + 3 * p + s for p in range(3) for s in range(3)}
    worst_ov, out_px, gh = 1, 0, 0
    for p in names:
        dmg = np.asarray(Image.open(p)); key = p.stem.split("_", 1)[1].rsplit("_stage", 1)[0]
        pose = body[poses[key]]
        a, b = dmg[..., 3] > 40, pose[..., 3] > 40
        worst_ov = min(worst_ov, (a & b).sum() / (a | b).sum())
        out_px += int((a & (cheb(b) > round(48 * k))).sum())
        gh = max(gh, ghost(dmg, pose, idle))
    res["attack_files"] = len(names); res["attack_overlap_min"] = round(float(worst_ov), 3); res["attack_outside_px"] = out_px; res["ghost_px_upper"] = gh
    # calm idle (frames 0..3)
    cs, bs = [], []
    for c in body[:4]:
        al = c[..., 3] > 32; ys, xs = np.nonzero(al); cs.append((xs.mean(), ys.mean())); bs.append((np.ptp(xs) + 1, np.ptp(ys) + 1))
    dc = max(math.hypot(a[0] - b[0], a[1] - b[1]) for a in cs for b in cs); db = max(max(abs(a[0] - b[0]), abs(a[1] - b[1])) for a in bs for b in bs)
    res["calm"] = (round(dc, 2), int(db), dc <= .015 * C and db <= .03 * C)
    if w != "Space":
        res["pink"] = {s: round(float(pink_share(np.asarray(Image.open(od / f"{w}{s}.png").convert("RGBA")))) * 100, 3) for s in ("", "_damage", "_damage_fx", "_death")}
    return res

for w in (sys.argv[1:] or ["Space", "Frost", "Verdant", "Ember", "Tide"]):
    print(w, json.dumps(run(w), default=str))
