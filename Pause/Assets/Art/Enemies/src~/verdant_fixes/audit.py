"""Audit live Verdant fixes and the new three-cell death PNGs.

Run: python3 Pause/Assets/Art/Enemies/src~/verdant_fixes/audit.py
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2] / "Resources" / "Enemies"
sys.path.insert(0, str(HERE))
from build import DEATH_KEYS, VERDANT, center_box, cells, mass_center, mine_idle, rgba, strong_box  # noqa: E402

FAIL: list[str] = []


def check(ok: bool, message: str) -> None:
    if not ok:
        FAIL.append(message)


def extent(im: Image.Image) -> tuple[int, int]:
    x0, y0, x1, y1 = strong_box(im, 176)
    return x1 - x0, y1 - y0


def bright_margin(im: Image.Image) -> int:
    a = np.asarray(im)
    bright = (a[:, :, :3].max(axis=2) >= 170) & (a[:, :, 3] >= 112)
    ys, xs = np.where(bright)
    if not len(xs):
        return im.width
    return int(min(xs.min(), ys.min(), im.width - 1 - xs.max(), im.height - 1 - ys.max()))


def rim_share(im: Image.Image) -> float:
    a = np.asarray(im)
    body = a[:, :, 3] >= 176
    rim = np.all(a[:, :, :3] == (230, 252, 94), axis=2) & body
    return float(rim.sum()) / max(1, int(body.sum()))


def material_core(im: Image.Image) -> int:
    a = np.asarray(im)
    yy, xx = np.indices((im.height, im.width))
    cx, cy = center_box(im)
    central = (xx-cx)**2 + (yy-cy)**2 < (im.height*.19)**2
    value = a[:, :, :3].max(axis=2)
    return int((central & (a[:, :, 3] >= 176) & (value < 155)).sum())


def foreign_verdant_pixels(im: Image.Image) -> int:
    a = np.asarray(im)
    hsv = np.asarray(Image.fromarray(a[:, :, :3], "RGB").convert("HSV"))
    h, s, v = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]
    return int(((h >= 120) & (h <= 240) & (s > 80) & (v > 45)
                & (a[:, :, 3] > 100)).sum())


def main() -> int:
    print("LIVE STRIPS")
    for key in VERDANT:
        p = ART / f"{key}.png"
        im = Image.open(p)
        side = 256 if key == "verdant_big" else 192
        check(im.mode == "RGBA", f"{key}: mode {im.mode}")
        check(im.size == (7*side, side), f"{key}: size {im.size}")
        parts = cells(im.convert("RGBA"), 7)
        shares = [rim_share(c) for c in parts[:4]]
        check(min(shares) >= .012, f"{key}: rim share {min(shares):.3f} < .012")
        # Chaser tell/hit cells are expressly preserved from the source PNG.
        check(all(foreign_verdant_pixels(c) == 0 for c in parts[:4]),
              f"{key}: foreign hue in edited idle cells")
        if key in ("verdant_chaser", "verdant_fighter_1", "verdant_rock_knot"):
            base_c = mass_center(parts[0])
            drifts = [max(abs(mass_center(c)[j]-base_c[j]) for j in (0, 1)) for c in parts[:4]]
            check(max(drifts) <= 3.0, f"{key}: centroid drift {max(drifts):.2f} > 3")
            if key in ("verdant_chaser", "verdant_rock_knot"):
                bw, bh = extent(parts[0])
                changes = [max(abs(extent(c)[0]/bw-1), abs(extent(c)[1]/bh-1)) for c in parts[:4]]
                check(max(changes) <= .04, f"{key}: scale change {max(changes):.3f} > .04")
        if key == "verdant_alien":
            c3 = center_box(parts[3]); refs = [center_box(c) for c in parts[:3]]
            drift = min(max(abs(c3[j]-ref[j]) for j in (0,1)) for ref in refs)
            check(drift <= 3, f"{key}: cell 3 anchor {drift:.1f} > 3")
            s3 = extent(parts[3]); sr = [extent(c) for c in parts[:3]]
            scale = min(max(abs(s3[0]/w-1), abs(s3[1]/h-1)) for w,h in sr)
            check(scale <= .04, f"{key}: cell 3 scale {scale:.3f} > .04")
        if key == "verdant_chaser":
            original = cells(rgba(HERE/"original"/f"{key}.png"), 7)
            check(all(parts[i].tobytes() == original[i].tobytes() for i in (4,5,6)),
                  f"{key}: tell or hit changed")
        if key in ("verdant_fighter_2", "verdant_fighter_4"):
            margins = [bright_margin(c) for c in parts]
            check(min(margins) >= 6, f"{key}: bright margin {min(margins)} < 6")
        print(f"  {key:22s} rim {min(shares):.3f}..{max(shares):.3f}")

    print("DEATH STRIPS")
    for key in DEATH_KEYS:
        p = ART / "Death" / f"{key}.png"
        check(p.exists(), f"{key}: missing death PNG")
        if not p.exists():
            continue
        im = Image.open(p)
        side = 256 if key.endswith("_big") else 192
        check(im.mode == "RGBA", f"{key} death: mode {im.mode}")
        check(im.size == (3*side, side), f"{key} death: size {im.size}")
        parts = cells(im.convert("RGBA"), 3)
        if key.startswith("verdant_"):
            check(all(foreign_verdant_pixels(c) == 0 for c in parts),
                  f"{key}: foreign hue in death cells")
        if key.endswith("_mine"):
            idle = mine_idle(key.split("_", 1)[0])
        else:
            idle = cells(rgba(ART/f"{key}.png"), 7)[3]
        a = np.asarray(idle.getchannel("A"))
        b = np.asarray(parts[0].getchannel("A"))
        # The white-hot flash must retain the intact idle hull.
        check(np.all((a < 176) | (b >= 176)), f"{key}: flash removes idle hull")
        ca, cb = center_box(idle), center_box(parts[0])
        drift = max(abs(ca[0]-cb[0]), abs(ca[1]-cb[1]))
        check(drift <= 3, f"{key}: death flash anchor drift {drift:.1f} > 3")
        margins = [bright_margin(c) for c in parts]
        check(min(margins) >= 6, f"{key}: death bright margin {min(margins)} < 6")
        check(all(np.asarray(c.getchannel("A")).max() > 0 for c in parts),
              f"{key}: empty death cell")
        # In the final pose, dark opaque core material should have dispersed.
        core_ratio = material_core(parts[2]) / max(1, material_core(idle))
        check(core_ratio < .72, f"{key}: final pose retains core ({core_ratio:.2f})")
        print(f"  {key:22s} margin {min(margins):2d}  flash drift {drift:.1f}  final core {core_ratio:.2f}")
    check((HERE/"preview.png").exists(), "preview.png missing")
    staged_mine = HERE/"verdant_mine_rim.png"
    check(staged_mine.exists(), "staged mine rim missing")
    if staged_mine.exists():
        check(rim_share(rgba(staged_mine)) >= .012, "staged mine rim too dim")
    if FAIL:
        print("FAILURES:")
        for message in FAIL:
            print("  ", message)
        return 1
    print(f"PASS: {len(VERDANT)} live strips, {len(DEATH_KEYS)} death strips, preview")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
