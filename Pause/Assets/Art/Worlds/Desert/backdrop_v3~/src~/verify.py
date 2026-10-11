"""Verify the four Desert tile sets and produce the manifest / art preview."""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image

from build import rgb_to_hsv

ROOT = Path(__file__).resolve().parent.parent
ROLES = ("sky", "far", "mid", "flow")
NOTES = {
    1: "Open barchan dune sea and long wind ripples; no baked worm forms.",
    2: "Dry copper canyon, half-buried refinery hulls and ribcage gantry.",
    3: "Cracked salt-and-glass pan with dead solar array skeletons.",
    4: "Night dune sea, cold indigo sand and narrow bone crests; no sky objects.",
}
ROLE_NOTES = {
    "sky": "Deepest hazy ground plane; source-matched terrain material.",
    "far": "Middle-depth ground with source-matched forms.",
    "mid": "Most detailed ground plane, behind gameplay.",
    "flow": "Sparse translucent painted-edge wind, dust or crack glints.",
}


def metrics(path: Path, variant: int, role: str) -> dict:
    image = Image.open(path)
    arr = np.asarray(image)
    if image.mode != "RGBA" or image.size != (512, 1024):
        raise AssertionError(f"{path}: expected RGBA 512x1024, got {image.mode} {image.size}")
    h, s, v = rgb_to_hsv(arr[:, :, :3])
    shown = arr[:, :, 3] > 0
    n = max(int(shown.sum()), 1)
    fraction = lambda m: float(np.count_nonzero(m & shown) / n)
    forbid_gold = (h >= 30) & (h <= 48) & (s > .45)
    forbid_red = ((h >= 345) | (h <= 15)) & (s > .5)
    forbid_violet = (h >= 250) & (h <= 300)
    pink = (h >= 312) & (h <= 326)
    forbidden = forbid_gold | forbid_red | forbid_violet | pink
    copper = (h >= 22) & (h <= 30) & (s > .33)
    verdigris = (h >= 135) & (h <= 155) & (s > .15)
    glass = (h >= 160) & (h <= 170) & (s > .10)
    indigo = (h >= 232) & (h <= 248) & (s >= .28)
    sand = (h >= 40) & (h <= 48) & shown & ~forbid_gold
    xdiff = int(np.abs(arr[:, 0].astype(np.int16) - arr[:, -1].astype(np.int16)).max())
    ydiff = int(np.abs(arr[0].astype(np.int16) - arr[-1].astype(np.int16)).max())
    result = {
        "size": [512, 1024], "mode": "RGBA", "variant": f"v{variant}", "role": role,
        "notes": f"{NOTES[variant]} {ROLE_NOTES[role]}",
        "p90_value_visible": round(float(np.percentile(v[shown], 90)), 4),
        "wrap_edge_difference_max_rgba": max(xdiff, ydiff),
        "wrap_edge_difference_x": xdiff,
        "wrap_edge_difference_y": ydiff,
        "visible_fraction": round(float(shown.mean()), 5),
        "max_alpha": int(arr[:, :, 3].max()),
        "hard_no_share": round(fraction(forbidden), 6),
        "saturated_gold_share": round(fraction(forbid_gold), 6),
        "red_share": round(fraction(forbid_red), 6),
        "violet_share": round(fraction(forbid_violet), 6),
        "pink_share": round(fraction(pink), 6),
        "copper_share": round(fraction(copper), 6),
        "verdigris_share": round(fraction(verdigris), 6),
        "glass_share": round(fraction(glass), 6),
        "indigo_shadow_share": round(fraction(indigo), 6),
        "sand_saturation_p95": round(float(np.percentile(s[sand], 95)), 4) if sand.any() else None,
    }
    problems = []
    if result["wrap_edge_difference_max_rgba"]: problems.append("wrap")
    if result["saturated_gold_share"] > .003: problems.append("saturated gold")
    if result["red_share"]: problems.append("red")
    if result["violet_share"]: problems.append("violet")
    if result["pink_share"] > .003: problems.append("pink")
    if result["copper_share"] > .12: problems.append("copper area")
    if result["verdigris_share"] > .03: problems.append("verdigris area")
    if result["glass_share"] > .04: problems.append("glass area")
    if result["indigo_shadow_share"] > .25: problems.append("indigo area")
    if result["sand_saturation_p95"] is not None and result["sand_saturation_p95"] > .33:
        problems.append("sand saturation")
    if copper.any() and ((s[copper] > .62).any() or (v[copper] > .60).any()):
        problems.append("copper saturation/value")
    if role != "flow":
        if not (arr[:, :, 3] == 255).all(): problems.append("ground alpha")
        lower, upper = (.32, .42) if variant == 4 else (.40, .50)
        if not lower <= result["p90_value_visible"] <= upper: problems.append("p90")
    else:
        if not .005 <= result["visible_fraction"] <= .25: problems.append("flow coverage")
        if result["max_alpha"] == 255: problems.append("opaque flow texel")
    if problems: raise AssertionError(f"{path}: {', '.join(problems)}")
    return result


def repeated(image: Image.Image, size=(1080, 2400), xoff=0, yoff=0) -> Image.Image:
    # The game scales a 512px tile to the view width. Point filtering keeps
    # the source pixel edges hard in this phone-sized art-only preview.
    tile = image.resize((1080, 2160), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", size)
    y = yoff - 2160
    while y < size[1]:
        canvas.alpha_composite(tile, (xoff, y))
        y += 2160
    return canvas


def preview():
    dark = (11, 11, 26, 255)
    group_w = 4 * 512 + 1080
    sheet = Image.new("RGBA", (group_w * 4, 2400), dark)
    for variant in range(1, 5):
        x0 = (variant - 1) * group_w
        layers = {role: Image.open(ROOT / f"v{variant}" / f"{role}.png").convert("RGBA")
                  for role in ROLES}
        for i, role in enumerate(ROLES):
            if role == "flow":
                sheet.alpha_composite(layers[role], (x0 + i * 512, 0))
            else:
                sheet.paste(layers[role], (x0 + i * 512, 0))
        # Opaque tile layers really do stack in this order in BackdropSet;
        # the closest mid covers the deeper sky/far at a static instant.
        phone = Image.new("RGBA", (1080, 2400), dark)
        for role, off in (("sky", 0), ("far", 310), ("mid", 660), ("flow", 970)):
            phone.alpha_composite(repeated(layers[role], yoff=off))
        sheet.alpha_composite(phone, (x0 + 2048, 0))
    sheet.convert("RGB").save(ROOT / "preview_variants.png", optimize=True)


def main():
    files = {}
    print("file       p90    wrap  visible  max α  gold    red   violet  copper  indigo  hard NO")
    for variant in range(1, 5):
        for role in ROLES:
            name = f"v{variant}/{role}.png"
            m = metrics(ROOT / name, variant, role)
            files[name] = m
            print(f"{name:12} {m['p90_value_visible']:.3f}  {m['wrap_edge_difference_max_rgba']:4}"
                  f"  {m['visible_fraction']:.3f}    {m['max_alpha']:3}"
                  f"  {m['saturated_gold_share']:.4f}  {m['red_share']:.4f}"
                  f"  {m['violet_share']:.4f}   {m['copper_share']:.3f}   "
                  f"{m['indigo_shadow_share']:.3f}   {m['hard_no_share']:.4f}")
        a, b, c = (files[f"v{variant}/{role}.png"]["p90_value_visible"] for role in ("sky", "far", "mid"))
        if not (.035 <= b - a <= .065 and .035 <= c - b <= .065):
            raise AssertionError(f"v{variant}: depth brightness {a}, {b}, {c}")
    (ROOT / "manifest.json").write_text(json.dumps({"files": files}, indent=2) + "\n")
    preview()
    print("PASS: 16 RGBA tiles, exact wrap, brightness/depth, sparse flow, palette rules.")


if __name__ == "__main__":
    main()
