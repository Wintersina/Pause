"""Measure Storm limb geometry, perspective cues, and palette.

Run: python3 Pause/Assets/Art/Worlds/Storm/descent~/limbfix/src~/verify.py
"""

from pathlib import Path
import sys
import numpy as np
from PIL import Image


IMAGE = Path(__file__).resolve().parent.parent / "storm_planet_limb.png"
COLS = (205, 614, 1024, 1434, 1843)


def report(name, measured, target, ok):
    print(f"| {name:<24} | {measured:<49} | {target:<23} | {'PASS' if ok else 'FAIL'} |")
    return ok


def box(a, width):
    return np.convolve(a, np.ones(width) / width, mode="same")


def dominant_vertical_scale(values):
    """Scale with the strongest band-pass contrast after linear trend removal."""
    xx = np.arange(len(values))
    detrended = values - np.polyval(np.polyfit(xx, values, 1), xx)
    scores = []
    for scale in (2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64):
        if 2 * scale + 1 > len(detrended):
            continue
        band = box(detrended, scale) - box(detrended, 2 * scale + 1)
        if len(detrended) > 5 * scale:
            band = band[2 * scale : -2 * scale]
        scores.append((float(np.sqrt(np.mean(band * band))), scale))
    return max(scores)[1]


def main():
    image = Image.open(IMAGE)
    a = np.asarray(image.convert("RGBA"))
    h, w = a.shape[:2]
    passed = []
    print("| Check                    | Measured                                          | Target                  | Result |")
    print("|--------------------------|---------------------------------------------------|-------------------------|--------|")
    passed.append(report("dimensions", f"{w} x {h}", "2048 x 1024", (w, h) == (2048, 1024)))
    passed.append(report("mode", image.mode, "RGBA", image.mode == "RGBA"))
    if (w, h) != (2048, 1024):
        return 1

    alpha = a[:, :, 3]
    first = np.argmax(alpha > 0, axis=0)
    mask = np.arange(h)[:, None] >= first[None, :]
    wrong = int(np.count_nonzero((alpha > 0) != mask))
    partial = int(np.count_nonzero((alpha != 0) & (alpha != 255)))
    passed.append(report("alpha mask", f"{wrong} stray; {partial} partial-alpha px", "0 above; opaque below", wrong == 0 and partial == 0))
    anchors = f"centre {first[1024]}; left {first[0]}; right {first[-1]}"
    passed.append(report("horizon anchors", anchors, "355; 720; 720 (±1)", abs(int(first[1024])-355)<=1 and abs(int(first[0])-720)<=1 and abs(int(first[-1])-720)<=1))

    # Algebraic least-squares circle through the measured first opaque pixel of every column.
    xx = np.arange(w, dtype=np.float64)
    yy = first.astype(np.float64)
    cx, cy, term = np.linalg.lstsq(np.column_stack((2 * xx, 2 * yy, np.ones(w))), xx*xx + yy*yy, rcond=None)[0]
    radius = np.sqrt(cx*cx + cy*cy + term)
    predicted = cy - np.sqrt(radius*radius - (xx-cx)**2)
    circle_error = float(np.max(np.abs(predicted - yy)))
    passed.append(report("circle fit max error", f"{circle_error:.2f} px; radius {radius:.1f} px", "≤ 6 px", circle_error <= 6))

    rgb = a[:, :, :3].astype(np.float64)
    gray = rgb[:, :, 0]*.2126 + rgb[:, :, 1]*.7152 + rgb[:, :, 2]*.0722
    angles = []
    errors = []
    correlations = []
    for x in COLS:
        # Track a 72 px horizontal separation in the fine band region below the rim.
        rows = np.arange(int(first[x] + 15), int(first[x] + 110))
        left = gray[rows[:, None], np.arange(x-41, x-30)[None, :]].mean(axis=1)
        choices = []
        for shift in range(-50, 51):
            right = gray[(rows+shift)[:, None], np.arange(x+31, x+42)[None, :]].mean(axis=1)
            choices.append((float(np.corrcoef(left, right)[0, 1]), shift))
        corr, shift = max(choices)
        measured_angle = np.degrees(np.arctan(shift / 72))
        # Constant radial distance curves have (1-s) times the horizon slope.
        radial_mid = 62.5 / (h - first[x])
        slope = (x-cx) / np.sqrt(radius*radius-(x-cx)**2)
        target_angle = np.degrees(np.arctan((1-radial_mid)*slope))
        angles.append(measured_angle)
        errors.append(abs(measured_angle-target_angle))
        correlations.append(corr)
    orientation_text = "/".join(f"{v:+.1f}°" for v in angles)
    passed.append(report("band angles at 5 cols", orientation_text, "arc tangent ±10°", max(errors) <= 10 and min(correlations) >= .45))
    passed.append(report("band worst error", f"{max(errors):.1f}°; min correlation {min(correlations):.2f}", "≤10°; corr ≥.45", max(errors) <= 10 and min(correlations) >= .45))

    near_scales = []
    bottom_scales = []
    for x in COLS:
        line = gray[:, x-8:x+9].mean(axis=1)
        y0 = predicted[x]
        near = line[round(y0+.06*(h-y0)):round(y0+.28*(h-y0))]
        bottom = line[round(y0+.53*(h-y0)):round(y0+.91*(h-y0))]
        near_scales.append(dominant_vertical_scale(near))
        bottom_scales.append(dominant_vertical_scale(bottom))
    ratio = float(np.mean(near_scales) / np.mean(bottom_scales))
    scale_text = f"near {np.mean(near_scales):.1f}px; bottom {np.mean(bottom_scales):.1f}px; {ratio:.2f}x"
    passed.append(report("vertical feature size", scale_text, "near ≤ 0.55 × bottom", ratio <= .55))

    hsv = np.asarray(Image.fromarray(a[:, :, :3], "RGB").convert("HSV"))
    hue = hsv[:, :, 0].astype(float)*360/256
    sat = hsv[:, :, 1].astype(float)/255
    visible = alpha > 0
    chromatic = visible & (sat > .06)
    blue = chromatic & (hue >= 205) & (hue <= 220)
    bronze = chromatic & (hue >= 28) & (hue <= 40)
    lightning = chromatic & (hue >= 52) & (hue <= 58)
    allowed = blue | bronze | lightning
    coverage = np.count_nonzero(allowed) / max(1, np.count_nonzero(chromatic))
    hue_text = f"blue {np.count_nonzero(blue):,}; bronze {np.count_nonzero(bronze):,}; yellow {np.count_nonzero(lightning):,}; {coverage:.2%}"
    passed.append(report("hue band coverage", hue_text, "≥99% chromatic pixels", coverage >= .99))
    bronze_max = float(sat[bronze].max()) if np.any(bronze) else 0
    passed.append(report("bronze saturation", f"max {bronze_max:.3f}", "≤0.300", bronze_max <= .3))
    yellow_fraction = np.count_nonzero(lightning) / max(1, np.count_nonzero(visible))
    passed.append(report("yellow-white density", f"{yellow_fraction:.3%} of visible pixels", "≤3%", yellow_fraction <= .03))
    red = visible & (sat > .01) & ((hue >= 345) | (hue <= 15))
    passed.append(report("red band", f"{np.count_nonzero(red)} px", "0 px", not np.any(red)))
    print(f"\n{sum(passed)}/{len(passed)} checks passed")
    return 0 if all(passed) else 1


if __name__ == "__main__":
    sys.exit(main())
