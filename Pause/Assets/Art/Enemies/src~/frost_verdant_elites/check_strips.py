#!/usr/bin/env python3
"""Python mirror of the strip-geometry checks of FrostVerdantEliteFramesTest (margins, nose line, pivot, width, distinct drawings, nozzle
point dark + covered by the flame, flame past the hull). A quick gate before Unity runs.  python3 check_strips.py"""
import json
from pathlib import Path
import numpy as np
from PIL import Image
HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]
S = 384
bad = 0


def chk(what, ok):
    global bad
    print(("PASS " if ok else "FAIL ") + what)
    bad += 0 if ok else 1


for key, world in (("frost_elite_cryo_siren", "Frost"), ("frost_elite_rimebreaker", "Frost"), ("verdant_elite_resin_warden", "Verdant")):
    d = json.load(open(ART / "Resources/Elites/Defs" / f"{key}.json"))
    im = np.array(Image.open(ART / "Resources/Elites" / world / f"{key}.png").convert("RGBA")).astype(int)
    cells = [im[:, i * S:(i + 1) * S] for i in range(im.shape[1] // S)]
    chk(key + " 10 cells", len(cells) == 10)
    tops, cxs, wid = [], [], []
    for i, c in enumerate(cells):
        ys, xs = np.nonzero(c[..., 3] > 8)
        m = min(xs.min(), ys.min(), S - 1 - xs.max(), S - 1 - ys.max())
        chk(f"{key} cell {i} margin {m} >= 6", m >= 6)
        tops.append(ys.min()); cxs.append((xs.min() + xs.max()) / 2); wid.append(xs.max() - xs.min())
    chk(f"{key} nose ptp {np.ptp(tops)} <= 10", np.ptp(tops) <= 10)
    chk(f"{key} pivot ptp {np.ptp(cxs):.1f} <= 18", np.ptp(cxs) <= 18)
    chk(f"{key} width ptp {np.ptp(wid[:7])} <= 40", np.ptp(wid[:7]) <= 40)
    nz = d["nozzles"]
    mouthY = int(nz[0]["y"]) - 6
    for a, b, need in ((0, 3, 1200), (2, 3, 600), (3, 4, 600), (3, 7, 1500), (3, 8, 1500), (7, 8, 1500), (3, 9, 3000), (0, 2, 400)):
        diff = (np.abs(cells[a][:mouthY, :, :3] - cells[b][:mouthY, :, :3]).sum(-1) > 48) | ((cells[a][:mouthY, :, 3] > 128) != (cells[b][:mouthY, :, 3] > 128))
        chk(f"{key} cells {a}/{b} differ {diff.sum()} >= {need}", diff.sum() >= need)
    for c in (4, 5, 6):
        diff = (np.abs(cells[3][mouthY:, :, :3] - cells[c][mouthY:, :, :3]).sum(-1) > 48) | ((cells[3][mouthY:, :, 3] > 128) != (cells[c][mouthY:, :, 3] > 128))
        chk(f"{key} burn cell {c} differs under hull {diff.sum()} >= 150", diff.sum() >= 150)
    hb = np.nonzero(cells[0][..., 3] > 128)[0].max()
    tips = []
    for n in nz:
        nx, ny = int(n["x"]), int(n["y"])
        h0 = cells[0][ny, nx]
        chk(f"{key} {n['name']} nozzle dark {h0[:3]} a={h0[3]}", h0[3] == 255 and h0[0] * .3 + h0[1] * .59 + h0[2] * .11 < 70)
        for ci in range(2, 10):
            cov = False
            for dy in range(-3, 4):
                for dx in range(-9, 10):
                    p = cells[ci][ny + dy, nx + dx]; q = cells[0][ny + dy, nx + dx]
                    if p[3] == 255 and (p[:3] != q[:3]).any() and p[:3].max() > 150:
                        cov = True
            chk(f"{key} cell {ci} {n['name']} flame covers nozzle", cov)
        tip = ny
        for y in range(ny, S):
            for dx in range(-3, 4):
                q = cells[3][y, nx + dx]; r = cells[0][y, nx + dx]
                if q[3] > 0 and (q != r).any():
                    tip = max(tip, y)
        chk(f"{key} {n['name']} flame tip {tip} >= hull bottom {hb}+10", tip >= hb + 10)
        tips.append(tip)
    chk(f"{key} engines differ in phase {tips}", len(set(tips)) >= 2)
print("failures", bad)
