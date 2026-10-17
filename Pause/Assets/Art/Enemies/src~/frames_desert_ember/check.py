#!/usr/bin/env python3
"""Python mirror of Editor/Tests/DesertEmberFrameTest.cs on strips/<name>.png (or --installed for the Resources copies)."""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
RES = HERE.parents[4] / "Assets/Art/Resources/Enemies"


def chg(a, b):
    pa, qa = a[..., 3] > 40, b[..., 3] > 40
    u = pa | qa
    if not u.any():
        return 0.
    d = np.abs(a[..., :3].astype(int) - b[..., :3].astype(int)).max(-1)
    return float(((pa ^ qa) | (pa & qa & (d > 40))).sum() / u.sum())


def sil(a, b):
    pa, qa = a[..., 3] > 40, b[..., 3] > 40
    u = pa | qa
    return float((pa ^ qa).sum() / u.sum()) if u.any() else 0.


def check(p):
    a = np.asarray(Image.open(p).convert("RGBA")); h = a.shape[0]
    cs = [a[:, i * h:(i + 1) * h] for i in range(a.shape[1] // h)]
    msgs = []
    cx, cy = [], []
    for i, c in enumerate(cs):
        ys, xs = np.where(c[..., 3] > 8)
        m = min(xs.min(), ys.min(), h - 1 - xs.max(), h - 1 - ys.max())
        if m < 6: msgs.append(f"cell{i} margin {m}")
        cx.append((xs.min() + xs.max()) / 2); cy.append(float(np.where(c[..., 3] > 8)[0].mean()))
    if np.ptp(cx) > 6 or np.ptp(cy) > 8: msgs.append(f"pivot {np.ptp(cx):.1f}/{np.ptp(cy):.1f}")
    idle = min(chg(cs[i], cs[j]) for i in range(4) for j in range(i + 1, 4))
    t1, t2, hit, tt = sil(cs[0], cs[4]), sil(cs[0], cs[5]), sil(cs[0], cs[6]), sil(cs[4], cs[5])
    if idle < .05: msgs.append(f"idle {idle:.3f}")
    for k, v, lim in (("tell1", t1, .06), ("tell2", t2, .06), ("hit", hit, .06), ("t1t2", tt, .04)):
        if v < lim: msgs.append(f"{k} {v:.3f}")
    print(p.stem.ljust(20), f"idle {idle:.2f} tell {t1:.2f}/{t2:.2f} hit {hit:.2f} t1t2 {tt:.2f}", "OK" if not msgs else "FAIL " + "; ".join(msgs))


if __name__ == "__main__":
    inst = "--installed" in sys.argv
    names = [x for x in sys.argv[1:] if not x.startswith("--")]
    base = RES if inst else HERE / "strips"
    for p in sorted(base.glob("*.png")):
        if (p.stem.startswith(("desert_", "ember_")) and "_rock_" not in p.stem and "mine" not in p.stem) and (not names or p.stem in names):
            check(p)
