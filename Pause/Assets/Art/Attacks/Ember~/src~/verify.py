"""Pixel and animation audit for ember_attack_mineflame.png."""
from __future__ import annotations

import colorsys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
PATH = ROOT / "ember_attack_mineflame.png"
RESERVED = {(3, 3), (4, 3), (5, 3)}


def cell(im: np.ndarray, col: int, row: int) -> np.ndarray:
    top = 0 if row == 0 else (row + 2) * 128
    height = 384 if row == 0 else 128
    return im[top : top + height, col * 128 : (col + 1) * 128]


def hue_counts(pixels: np.ndarray) -> tuple[float, list[float], int]:
    if not len(pixels):
        return 0, [0] * 4, 0
    pink = 0
    pickups = [0, 0, 0, 0]
    red = 0
    for r, g, b, _ in pixels:
        hue, sat, val = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        hue *= 360
        pink += (300 <= hue <= 340 and sat >= .18) or (val >= .88 and sat <= .25)
        if sat > .65:
            for i, target in enumerate((178, 82, 259, 37)):
                pickups[i] += min(abs(hue - target), 360 - abs(hue - target)) <= 20
        red += (hue >= 345 or hue <= 15) and sat > .5
    return 100 * pink / len(pixels), [100 * n / len(pixels) for n in pickups], red


def difference(a: np.ndarray, b: np.ndarray) -> float:
    opaque = (a[:, :, 3] > 24) | (b[:, :, 3] > 24)
    if not opaque.any():
        return 0
    changed = np.any(a != b, axis=2) & opaque
    return 100 * changed.sum() / opaque.sum()


def run() -> None:
    image = Image.open(PATH)
    size_ok = image.size == (768, 768)
    rgba_ok = image.mode == "RGBA"
    im = np.array(image.convert("RGBA"))
    failures = []
    if not size_ok or not rgba_ok:
        failures.append("size/RGBA")
    print(f"file: {PATH.name} | size {image.size} {'OK' if size_ok else 'FAIL'} | RGBA {'OK' if rgba_ok else 'FAIL'}")
    print("cell          ink  border pink%  near178/82/259/37%       red  width")
    print("-----------------------------------------------------------------------")
    for row in range(4):
        for col in range(6):
            c = cell(im, col, row)
            alpha = c[:, :, 3]
            nonempty = int((alpha > 24).sum())
            reserved = (col, row) in RESERVED
            border = np.zeros(alpha.shape, dtype=bool)
            border[:6] = border[-6:] = border[:, :6] = border[:, -6:] = True
            border_ok = not np.any(alpha[border] > 24)
            pixels = c[alpha > 24]
            pink, pickups, red = hue_counts(pixels)
            xs = np.where(alpha > 24)[1]
            width = int(xs.max() - xs.min() + 1) if len(xs) else 0
            label = f"r{row}c{col}"
            if reserved:
                print(f"{label:5} reserved  {'OK' if nonempty == 0 else 'FAIL'}")
                if nonempty:
                    failures.append(f"reserved {label}")
                continue
            valid = nonempty > 0 and border_ok and pink >= 30 and red == 0 and (row != 0 or width <= 100)
            if not valid:
                failures.append(label)
            p = "/".join(f"{v:4.1f}" for v in pickups)
            print(f"{label:5} {nonempty:7}  {'OK' if border_ok else 'FAIL':>4}  {pink:5.1f}  {p:>21}  {red:4}  {width:3}")

    # Exact 2x nearest reconstruction from the native 384x384 canvas.
    native = image.resize((384, 384), Image.Resampling.NEAREST)
    round_trip = np.array(native.resize((768, 768), Image.Resampling.NEAREST))
    round_ok = np.array_equal(im, round_trip)
    print(f"x2 nearest round trip: {'OK' if round_ok else 'FAIL'}")
    if not round_ok:
        failures.append("x2")

    body = [cell(im, i, 0) for i in range(6)]
    steps = [difference(body[i], body[(i + 1) % 6]) for i in range(6)]
    print("body loop 12 fps differences (% incl. closure): " + ", ".join(f"{v:.1f}" for v in steps))
    seam_ok = steps[-1] >= 3 and steps[-1] <= max(steps[:-1]) * 1.5
    motion_ok = min(steps) >= 3
    print(f"body motion >=3%: {'OK' if motion_ok else 'FAIL'} | loop seam: {'OK' if seam_ok else 'FAIL'}")
    if not motion_ok or not seam_ok:
        failures.append("body loop")
    for name, a, b in (("aim", cell(im, 4, 1), cell(im, 5, 1)), ("haze", cell(im, 1, 3), cell(im, 2, 3))):
        d = difference(a, b)
        print(f"{name} 2-frame loop step/seam: {d:.1f}% {'OK' if d >= 3 else 'FAIL'}")
        if d < 3:
            failures.append(f"{name} loop")

    # The authored margin makes top/bottom edge rows exactly equal (transparent).
    # Stretching is the intended runtime use; tiling introduces a narrow clear join.
    vertical = [np.array_equal(c[:6], c[-6:]) for c in body]
    print(f"body vertical edge equality: {sum(vertical)}/6 {'OK' if all(vertical) else 'FAIL'}")
    if not all(vertical):
        failures.append("vertical edges")
    haze_max = max(int(cell(im, i, 3)[:, :, 3].max()) for i in (1, 2))
    print(f"haze maximum alpha: {haze_max}/255 {'OK' if haze_max < 64 else 'FAIL'}")
    if haze_max >= 64:
        failures.append("haze alpha")
    print(f"reserved cells empty: {'OK' if all(np.max(cell(im, c, r)[:, :, 3]) == 0 for c, r in RESERVED) else 'FAIL'}")
    print(f"result: {'PASS' if not failures else 'FAIL (' + ', '.join(failures) + ')'}")
    if failures:
        raise SystemExit(1)


if __name__ == "__main__":
    run()
