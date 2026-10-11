"""Audit all six Ember elite strips and print their measured production contract."""
from __future__ import annotations

from pathlib import Path
import json
import numpy as np
from PIL import Image

from build import SHIPS

ROOT = Path(__file__).resolve().parent
N = 192


def measure(frame: Image.Image) -> dict:
    a = np.asarray(frame.convert("RGBA"))
    mask = a[:, :, 3] >= 24
    yy, xx = np.where(mask)
    assert len(xx)
    bbox = [int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1]
    rgb = a[mask, :3]
    f = rgb.astype(np.float32) / 255
    mx = f.max(1); mn = f.min(1); delta = mx - mn
    sat = np.divide(delta, mx, out=np.zeros_like(delta), where=mx > 0)
    hue = np.zeros_like(mx)
    active = delta > 0
    r, g, b = f.T
    ix = active & (mx == r)
    hue[ix] = ((g[ix] - b[ix]) / delta[ix]) % 6
    ix = active & (mx == g)
    hue[ix] = (b[ix] - r[ix]) / delta[ix] + 2
    ix = active & (mx == b)
    hue[ix] = (r[ix] - g[ix]) / delta[ix] + 4
    hue *= 60
    red = (sat > .5) & ((hue >= 345) | (hue <= 15))
    pink = (sat > .2) & (hue >= 285) & (hue < 345)
    amber = (sat > .2) & (hue >= 35) & (hue <= 48)
    border = bool(mask[:16].any() or mask[-16:].any() or mask[:, :16].any() or mask[:, -16:].any())
    return {
        "bbox": bbox,
        "bbox_size": [bbox[2] - bbox[0], bbox[3] - bbox[1]],
        "centroid": [round(float(xx.mean()), 3), round(float(yy.mean()), 3)],
        "area": int(mask.sum()),
        "min_margin": min(bbox[0], bbox[1], N - bbox[2], N - bbox[3]),
        "colors": int(len(np.unique(rgb, axis=0))),
        "red_share_pct": round(float(red.mean() * 100), 5),
        "pink_share_pct": round(float(pink.mean() * 100), 5),
        "amber35_48_share_pct": round(float(amber.mean() * 100), 5),
        "border_cut": border,
        "alpha_levels": int(len(np.unique(a[:, :, 3]))),
    }


def main() -> None:
    report = {}
    silhouettes = {}
    print("ship             idle3 bbox         bbox size  margin  colors  max Δctr  max Δarea  min motion  red%   pink%  amber%  cuts")
    print("-" * 125)
    for ship in SHIPS:
        path = ROOT / f"ember_elite_{ship.name}.png"
        im = Image.open(path)
        assert im.mode == "RGBA" and im.size == (1344, 192), (ship.name, im.mode, im.size)
        raw = [np.asarray(im.crop((k*N, 0, (k+1)*N, N))).copy() for k in range(7)]
        cells = [measure(Image.fromarray(a, "RGBA")) for a in raw]
        idle = cells[:4]
        center = idle[3]
        ctr = [float(np.hypot(*(np.array(c["centroid"]) - np.array(center["centroid"])))) for c in idle]
        drift = [abs(c["area"] / center["area"] - 1) * 100 for c in idle]
        motion = []
        for a, b in zip(raw[:3], raw[1:4]):
            union = (a[:, :, 3] >= 24) | (b[:, :, 3] >= 24)
            change = np.any(a != b, axis=2) & union
            motion.append(float(change.sum() / union.sum() * 100))
        margin = min(c["min_margin"] for c in cells)
        red = max(c["red_share_pct"] for c in cells)
        pink = max(c["pink_share_pct"] for c in cells)
        amber = max(c["amber35_48_share_pct"] for c in cells)
        cuts = sum(c["border_cut"] for c in cells)
        assert margin >= 16, (ship.name, "margin", margin)
        assert max(ctr) <= 3, (ship.name, "centroid", ctr)
        assert max(drift) <= 4, (ship.name, "idle area", drift)
        assert min(motion) >= 3, (ship.name, "motion", motion)
        assert center["colors"] >= 1500, (ship.name, "colors", center["colors"])
        assert cuts == 0, (ship.name, "border cut")
        assert red == 0 and pink <= .2, (ship.name, "red/pink", red, pink)
        assert amber <= 1, (ship.name, "amber band", amber)
        assert all(c["alpha_levels"] >= 3 for c in cells), (ship.name, "true alpha")
        silhouettes[ship.name] = raw[3][:, :, 3] >= 128
        report[ship.name] = {"strip_size": [1344, 192], "mode": im.mode, "cells": cells, "max_idle_centroid_drift_px": round(max(ctr), 3), "max_idle_area_drift_pct": round(max(drift), 3), "idle_pair_motion_pct": [round(m, 3) for m in motion]}
        print(f"{ship.name:16} {str(center['bbox']):19} {center['bbox_size'][0]:3}×{center['bbox_size'][1]:3}   {margin:3}    {center['colors']:5}   {max(ctr):5.2f} px   {max(drift):5.2f}%      {min(motion):5.1f}%    {red:5.3f}  {pink:5.3f}   {amber:5.3f}    {cuts}")
    overlap = {}
    for i, a in enumerate(SHIPS):
        for b in SHIPS[i+1:]:
            aa, bb = silhouettes[a.name], silhouettes[b.name]
            overlap[f"{a.name}/{b.name}"] = round(float((aa & bb).sum() / (aa | bb).sum()), 4)
    report["silhouette_iou"] = overlap
    (ROOT / "src~" / "verification.json").write_text(json.dumps(report, indent=2) + "\n")
    print(f"Silhouette maximum IoU: {max(overlap.values()):.3f} (lower means more distinct)")
    print("PASS: 6 RGBA strips, 42 cells; margins, registration, idle motion, detail, alpha, hue, and borders")


if __name__ == "__main__":
    main()
