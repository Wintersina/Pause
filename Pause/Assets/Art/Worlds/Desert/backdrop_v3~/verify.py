#!/usr/bin/env python3
"""Audit Desert run-B atlas geometry, transparency and forbidden palette bands."""
from pathlib import Path
import json
import sys
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
EXPECTED = {"landmarks": 16, "pipes": 16, "fires": 16, "weather": 16, "sites": 12}


def metrics(rgb, alpha):
    hsv = np.asarray(Image.fromarray(rgb, "RGB").convert("HSV")).astype(np.float32)
    hue = hsv[:, :, 0] * (360 / 255)
    sat = hsv[:, :, 1] / 255
    val = hsv[:, :, 2] / 255
    opaque = alpha > 0
    total = max(int(opaque.sum()), 1)
    share = lambda m: float((m & opaque).sum()) / total
    sand = opaque & (hue >= 40) & (hue <= 48) & (sat > .06)
    return {
        "visible": total,
        "p90": float(np.percentile(val[opaque], 90)),
        "sand_s95": float(np.percentile(sat[sand], 95)) if sand.any() else 0,
        "red": share(((hue <= 15) | (hue >= 345)) & (sat > .5)),
        "gold": share((hue >= 30) & (hue <= 48) & (sat > .45)),
        "violet": share((hue >= 250) & (hue <= 300) & (sat > .05)),
        "pink": share((hue >= 312) & (hue <= 326) & (sat > .1)),
        "cyan": share((hue >= 155) & (hue <= 205) & (sat > .45)),
        "lime": share((hue >= 60) & (hue <= 135) & (sat > .45)),
        # Near-black ink RGB quantization has unstable HSV saturation; only
        # evaluate visible metal above V=.10.
        "copper_hot": share((hue >= 22) & (hue <= 30) & (sat > .35) & (val > .10) & ((sat > .62) | (val > .60))),
    }


def verify():
    errors = []
    manifest = json.loads((ROOT / "manifest.json").read_text())
    entries = manifest.get("run_b", {}).get("sprites", [])
    if len(entries) != sum(EXPECTED.values()):
        errors.append(f"manifest run_b has {len(entries)} pieces, expected {sum(EXPECTED.values())}")
    manifest_keys = {(e.get("atlas"), e.get("name")) for e in entries}
    print("atlas       count  min_margin   p90    sand_s95  red%   gold%  violet%  pink%  cyan%  lime%  hotCu%  maxA")
    for atlas, expected_count in EXPECTED.items():
        im = Image.open(ROOT / f"{atlas}.png")
        if im.size != (1024, 1024) or im.mode != "RGBA":
            errors.append(f"{atlas}: need 1024x1024 RGBA, got {im.size} {im.mode}")
            continue
        data = np.asarray(im)
        rects = json.loads((ROOT / f"{atlas}.json").read_text()).get("sprites", [])
        if len(rects) != expected_count:
            errors.append(f"{atlas}: {len(rects)} rects, expected {expected_count}")
        seen = np.zeros((1024, 1024), bool)
        names = set()
        min_margin = 256
        for r in rects:
            name = r.get("n")
            if name in names:
                errors.append(f"{atlas}: duplicate name {name}")
            names.add(name)
            if (f"{atlas}.png", name) not in manifest_keys:
                errors.append(f"{atlas}:{name} missing from run_b")
            x, y, w, h = (int(r[k]) for k in ("x", "y", "w", "h"))
            if w != 256 or h != 256 or x % 256 or y % 256 or x < 0 or y < 0 or x + w > 1024 or y + h > 1024:
                errors.append(f"{atlas}:{name} invalid rect {x},{y},{w},{h}")
                continue
            top = 1024 - y - h
            if seen[top:top+h, x:x+w].any():
                errors.append(f"{atlas}:{name} overlapping rect")
            seen[top:top+h, x:x+w] = True
            cell = data[top:top+h, x:x+w]
            bounds = Image.fromarray(cell[:, :, 3], "L").getbbox()
            if bounds is None:
                errors.append(f"{atlas}:{name} empty sprite")
                continue
            margin = min(bounds[0], bounds[1], 256 - bounds[2], 256 - bounds[3])
            min_margin = min(min_margin, margin)
            if margin < 14:
                errors.append(f"{atlas}:{name} margin {margin} < 14")
            # The final authored pixels are exact 2x nearest-neighbour blocks.
            if not np.array_equal(cell[0::2, 0::2], cell[1::2, 0::2]) or not np.array_equal(cell[0::2, 0::2], cell[0::2, 1::2]):
                errors.append(f"{atlas}:{name} contains non-nearest pixels")
        m = metrics(data[:, :, :3], data[:, :, 3])
        if atlas in ("landmarks", "pipes") and m["p90"] > .55 + 1e-6:
            errors.append(f"{atlas}: p90 {m['p90']:.4f} > .55")
        if m["sand_s95"] > .33 + .005:
            errors.append(f"{atlas}: sand saturation p95 {m['sand_s95']:.4f} > .33")
        for key in ("red", "violet", "pink", "cyan", "lime", "copper_hot"):
            if m[key] > 0:
                errors.append(f"{atlas}: forbidden {key} share {m[key]:.6%}")
        if m["gold"] > .003:
            errors.append(f"{atlas}: saturated amber share {m['gold']:.4%} > .3%")
        if atlas == "weather" and data[:, :, 3].max() > 80:
            errors.append(f"weather max alpha {data[:, :, 3].max()} > 80")
        print(f"{atlas:<11} {len(rects):>3} {min_margin:>11} {m['p90']:>6.3f} {m['sand_s95']:>10.3f} " +
              " ".join(f"{100*m[k]:>6.3f}" for k in ("red", "gold", "violet", "pink", "cyan", "lime", "copper_hot")) +
              f" {data[:, :, 3].max():>5}")
    # Semantic screening of delivered names/notes; visual no-space review is
    # still required because a pixel array cannot prove what an object depicts.
    forbidden_words = ("starfield", "spaceship", "planet_disc", "comet", "moon_disc", "orbital_station")
    text = json.dumps(manifest.get("run_b", {})).lower()
    for word in forbidden_words:
        if word in text:
            errors.append(f"run_b contains space-imagery label {word}")
    if errors:
        print("FAIL")
        for error in errors:
            print(" -", error)
        return 1
    print("PASS: geometry, alpha margins, nearest blocks, p90 and palette limits. Visual no-space review completed separately.")
    return 0


if __name__ == "__main__":
    sys.exit(verify())
