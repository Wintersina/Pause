#!/usr/bin/env python3
"""Paranoid's damage overlay: the shared damage_overlay.py finds delta-wing landmarks (nose, wing tips, wing slab
lights, nozzle glows). Paranoid is a disc between two engine pods, so the same eight roles are found differently here
(kept in this ship's own folder; no shared file is edited):

  N  = the red diamond light at the top of the disc      L / R = leftmost / rightmost silhouette point
  C  = the teal canopy centre                             SL / SR = the red cap band of the left / right pod
  NL / NR = the amber nozzle glow of the left / right pod

    python3 overlay_paranoid.py Paranoid --skins [--preview DIR]
"""
import os
import sys

import numpy as np

SHIP_SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "src~")
sys.path.insert(0, os.path.abspath(SHIP_SRC))
import damage_overlay as do  # noqa: E402

CELL = do.CELL


def paranoid_landmarks(cl):
    rgb = cl[..., :3].astype(int)
    al = cl[..., 3] > 0
    ys, xs = np.where(al)
    h = ys.max() - ys.min()
    yy = np.arange(CELL)[:, None]
    xx = np.arange(CELL)[None, :]
    cyan = al & (rgb[..., 0] < 120) & (rgb[..., 1] > 150) & (rgb[..., 2] > 170)
    cyan &= (abs(yy - (ys.min() + ys.max()) / 2) < 0.3 * h)
    cy, cx = np.where(cyan)
    if len(cx) < 40:
        return None
    canopy = (cx.mean(), cy.mean())
    cxm = canopy[0]
    red = al & (rgb[..., 0] > 150) & (rgb[..., 0] > rgb[..., 1] * 2) & (rgb[..., 0] > rgb[..., 2] * 2)
    top = red & (abs(xx - cxm) < 30) & (yy < ys.min() + 0.25 * h)
    ty, tx = np.where(top)
    if len(tx) < 6:
        return None
    nose = (tx.mean(), ty.mean())
    caps = red & (abs(xx - cxm) > 55) & (yy < ys.min() + 0.5 * h)
    orange = al & (rgb[..., 0] > 200) & (rgb[..., 1] > 120) & (rgb[..., 1] < 200) & (rgb[..., 2] < 90)
    orange &= (abs(xx - cxm) > 55) & (yy > ys.min() + 0.85 * h)
    sl = do.side_blobs(caps, cxm, 12)
    nz = do.side_blobs(orange, cxm, 6)
    if sl is None or nz is None:
        return None
    # pod outer edge: mean outermost silhouette column over the rows between each pod's cap band and nozzle glow
    # (the single extreme pixel jumps along a banked pod's slanted outline)
    def edge(sl_pt, nz_pt, side):
        y0, y1 = int(min(sl_pt[1], nz_pt[1])), int(max(sl_pt[1], nz_pt[1]))
        cols = []
        for y in range(y0, y1 + 1):
            r = np.nonzero(al[y])[0]
            if len(r):
                cols.append(r.min() if side < 0 else r.max())
        return (float(np.mean(cols)), (y0 + y1) / 2.0)
    left = edge(sl[0], nz[0], -1)
    right = edge(sl[1], nz[1], 1)
    return dict(N=nose, L=left, R=right, C=canopy, SL=sl[0], SR=sl[1], NL=nz[0], NR=nz[1])


do.stock_landmarks = paranoid_landmarks

if __name__ == "__main__":
    if "--marks" in sys.argv:
        import shiplib
        a = do.load(shiplib.hull_path("Paranoid"))
        for c in (0, 6, 7):
            lm = paranoid_landmarks(do.cell(a, 0, c))
            print(c, {k: (round(v[0], 1), round(v[1], 1)) for k, v in lm.items()} if lm else None)
        sys.exit(0)
    do.main()
