"""Audit the Storm rail and render the six-world rail contact on two backdrops."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image

from build_rail import rgb_to_hsv


HERE = Path(__file__).resolve().parent
ART = HERE.parents[3]
RAILS = ART / "Resources/Worlds"
TIDE = RAILS / "Tide/rail_tide_wide_v1.png"
CLOUD = ART / "Worlds/Storm/descent~/storm_cloud_deck_dark.png"
ORDER = [
    ("Space", RAILS / "Space/rail_space_wide_v1.png"),
    ("Frost", RAILS / "Frost/rail_frost_wide_v1.png"),
    ("Verdant", RAILS / "Verdant/rail_forest_wide_v1.png"),
    ("Ember", RAILS / "Ember/rail_ember_wide_v1.png"),
    ("Tide", TIDE),
]


def alpha_metrics(arr: np.ndarray) -> dict[str, float | int]:
    a = arr[:, :, 3]
    gaps = 0
    rows = 0
    for row in a:
        visible = np.flatnonzero(row >= 128)
        if len(visible):
            count = int(np.sum(row[visible[0]:visible[-1] + 1] == 0))
            gaps += count
            rows += count > 0
    return {
        "transparent": float(np.mean(a == 0)),
        "partial": float(np.mean((a >= 1) & (a <= 250))),
        "gap_pixels": gaps,
        "gap_rows": rows,
    }


def color_metrics(arr: np.ndarray) -> dict[str, int | float]:
    h, s, v = rgb_to_hsv(arr[:, :, :3])
    opaque = arr[:, :, 3] >= 251
    valid = opaque & (s >= .12) & (v >= .12)
    share = lambda mask: float(np.mean(valid & mask))
    pink = valid & (h >= 310) & (h < 345) & (s >= .4) & (v >= .3)
    # Include neutral white cores, blue-white tubes, and pale-yellow cores.
    lamp = opaque & (v >= .86) & (s <= .5) & (
        (s < .12) | ((h >= 195) & (h <= 235)) | ((h >= 50) & (h <= 68))
    )
    lamp[:, :215] = False
    lamp[:, 495:] = False
    return {
        "red": share((h >= 345) | (h < 15)),
        "cyan": share((h >= 170) & (h <= 186)),
        "lime": share((h >= 74) & (h <= 90)),
        "orange": share((h >= 30) & (h <= 44)),
        "pink": float(np.mean(pink)),
        "violet": share((h >= 250) & (h <= 300)),
        "lamp_pixels": int(lamp.sum()),
        "colors": int(len(np.unique(arr.reshape(-1, 4), axis=0))),
    }


def make_preview(rail_path: Path, preview_path: Path) -> None:
    width, height = 290, 868  # exactly 0.4x of the Storm rail
    gap = 10
    columns = ORDER + [("Storm", rail_path)]
    canvas = Image.new("RGB", (len(columns) * width + (len(columns) + 1) * gap,
                               2 * height + 3 * gap), "#0b0b1a")
    cloud = Image.open(CLOUD).convert("RGB").resize((819, 410), Image.Resampling.NEAREST)
    cloud_panel = Image.new("RGB", (width, height))
    for y in range(0, height, cloud.height):
        cloud_panel.paste(cloud.crop(((cloud.width - width)//2, 0,
                                      (cloud.width + width)//2, cloud.height)), (0, y))
    for i, (_, path) in enumerate(columns):
        rail = Image.open(path).convert("RGBA").resize((width, height), Image.Resampling.NEAREST)
        x = gap + i * (width + gap)
        for j, backdrop in enumerate((Image.new("RGBA", (width, height), "#0b0b1a"),
                                      cloud_panel.convert("RGBA"))):
            backdrop.alpha_composite(rail)
            canvas.paste(backdrop.convert("RGB"), (x, gap + j * (height + gap)))
    if preview_path.exists():
        raise SystemExit(f"Refusing to overwrite {preview_path}")
    preview_path.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(preview_path, optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rail", type=Path,
                        default=RAILS / "Storm/rail_storm_wide_v1.png")
    parser.add_argument("--preview", type=Path,
                        default=HERE.parent / "preview.png")
    args = parser.parse_args()
    im = Image.open(args.rail)
    assert im.mode == "RGBA", f"mode {im.mode}"
    assert im.size == (725, 2170), f"size {im.size}"
    storm = np.array(im)
    tide = np.array(Image.open(TIDE).convert("RGBA"))
    am, tm = alpha_metrics(storm), alpha_metrics(tide)
    cm = color_metrics(storm)
    diff = np.abs(storm[0].astype(np.int16) - storm[-1].astype(np.int16))
    seam_max, seam_mean = int(diff.max()), float(diff.mean())

    checks = [
        ("size/mode", im.size == (725, 2170) and im.mode == "RGBA"),
        ("tiling seam", seam_max <= 1),
        ("alpha share", abs(am["transparent"] - tm["transparent"]) <= .08),
        ("internal gaps", am["gap_pixels"] > 1000 and am["gap_rows"] > 100),
        ("partial halo", am["partial"] <= tm["partial"] + 1e-9),
        ("red hue", cm["red"] <= .0001),
        ("cyan hue", cm["cyan"] <= .001),
        ("lime hue", cm["lime"] <= .001),
        ("orange hue", cm["orange"] <= .001),
        ("pink size", cm["pink"] < .01),
        ("violet hue", cm["violet"] <= .001),
        ("lamp count", cm["lamp_pixels"] >= 10000),
        ("color detail", cm["colors"] >= 3000),
    ]
    print("Storm rail verification")
    print("check                 value                                      result")
    print("-" * 75)
    details = [
        f"{im.size[0]}x{im.size[1]} {im.mode}",
        f"max {seam_max}, mean {seam_mean:.3f}",
        f"Storm {am['transparent']:.2%}, Tide {tm['transparent']:.2%}",
        f"{am['gap_pixels']} px in {am['gap_rows']} rows",
        f"Storm {am['partial']:.2%}, Tide {tm['partial']:.2%}",
        f"{cm['red']:.3%}", f"{cm['cyan']:.3%}",
        f"{cm['lime']:.3%}", f"{cm['orange']:.3%}",
        f"{cm['pink']:.3%}", f"{cm['violet']:.3%}",
        str(cm["lamp_pixels"]), str(cm["colors"]),
    ]
    for (name, passed), detail in zip(checks, details):
        print(f"{name:<21} {detail:<42} {'PASS' if passed else 'FAIL'}")
    if not all(passed for _, passed in checks):
        raise SystemExit("Verification failed")
    make_preview(args.rail, args.preview)
    print(f"preview: {args.preview}")


if __name__ == "__main__":
    main()
