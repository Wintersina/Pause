"""Verify all Storm backdrop_v3 tiles. Exits nonzero if any contract fails."""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent
ROLES = ("sky", "far", "mid", "flow")
VARIANTS = ("v1", "v2", "v3", "v4")


def hsv(a: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    rgb = a[:, :, :3].astype(np.float32) / 255.0
    hi = rgb.max(2)
    lo = rgb.min(2)
    delta = hi - lo
    sat = np.divide(delta, hi, out=np.zeros_like(delta), where=hi > 0)
    hue = np.zeros_like(hi)
    active = delta > 1e-5
    r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    mr = active & (hi == r)
    mg = active & (hi == g) & ~mr
    mb = active & ~mr & ~mg
    hue[mr] = np.mod((g[mr] - b[mr]) / delta[mr], 6)
    hue[mg] = (b[mg] - r[mg]) / delta[mg] + 2
    hue[mb] = (r[mb] - g[mb]) / delta[mb] + 4
    return hue * 60, sat, hi


def main() -> int:
    manifest = json.loads((ROOT / "manifest.json").read_text())
    failed = []
    p90s = {}
    print("file       size      mode seam  p90   opaque   green/teal  purple  red    status")
    for variant in VARIANTS:
        for role in ROLES:
            key = f"{variant}/{role}.png"
            path = ROOT / key
            reasons = []
            if not path.exists():
                failed.append(f"{key}: missing")
                continue
            im = Image.open(path)
            a = np.asarray(im)
            if im.size != (512, 1024):
                reasons.append("size")
            if im.mode != "RGBA":
                reasons.append("RGBA")
            edge = max(int(np.abs(a[:, 0].astype(np.int16) - a[:, -1].astype(np.int16)).max()),
                       int(np.abs(a[0].astype(np.int16) - a[-1].astype(np.int16)).max()))
            if edge:
                reasons.append("seam")
            opaque = a[:, :, 3] > 0
            fraction = float(opaque.mean())
            if role == "flow":
                if not .005 <= fraction <= .25:
                    reasons.append("flow coverage")
            elif fraction != 1:
                reasons.append("opaque base")
            hue, sat, value = hsv(a)
            p90 = float(np.percentile(value[opaque], 90)) if np.any(opaque) else 0.0
            p90s[(variant, role)] = p90
            count = max(int(opaque.sum()), 1)
            bad_green = float(np.count_nonzero(opaque & (hue >= 95) & (hue <= 195) & (sat > .18)) / count)
            bad_purple = float(np.count_nonzero(opaque & (hue >= 250) & (hue <= 300) & (sat > .30)) / count)
            bad_red = float(np.count_nonzero(opaque & ((hue < 15) | (hue > 340)) & (sat > .5)) / count)
            if bad_green >= .005:
                reasons.append("green/teal")
            if bad_purple >= .005:
                reasons.append("purple")
            if bad_red >= .002:
                reasons.append("red")
            recorded = manifest["files"].get(key, {})
            if recorded.get("size") != [512, 1024] or recorded.get("role") != role or recorded.get("variant") != variant:
                reasons.append("manifest")
            if abs(recorded.get("p90_value_opaque", -10) - p90) > .005:
                reasons.append("manifest p90")
            if recorded.get("wrap_edge_max_channel_difference") != edge:
                reasons.append("manifest seam")
            if reasons:
                failed.append(f"{key}: {', '.join(reasons)}")
            print(f"{key:<10} {im.width:>3}x{im.height:<4} {im.mode:<4} {edge:>4} {p90:5.3f} {fraction:7.3%} "
                  f"{bad_green:10.3%} {bad_purple:7.3%} {bad_red:6.3%} {'FAIL' if reasons else 'PASS'}")
    for variant in VARIANTS:
        if (variant, "mid") not in p90s:
            continue
        mid, far, sky = (p90s[(variant, x)] for x in ("mid", "far", "sky"))
        lo, hi = (.32, .42) if variant == "v4" else (.40, .50)
        if not lo <= mid <= hi:
            failed.append(f"{variant}: mid p90 outside {lo:.2f}-{hi:.2f}")
        if not .035 <= mid - far <= .065 or not .035 <= far - sky <= .065:
            failed.append(f"{variant}: depth p90 spacing")
    if not (ROOT / "preview_variants.png").exists():
        failed.append("preview_variants.png: missing")
    if failed:
        print("\nFAILED:")
        for issue in failed:
            print(" -", issue)
        return 1
    print("\nPASS: 16 RGBA tiles; exact wrap edges; depth p90; flow coverage; hue limits; manifest; preview.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
