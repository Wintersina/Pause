"""Verify Tide rail export and render a two-backdrop comparison preview."""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from build_rail import rgb_to_hsv


HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]
RAILS = ART / "Resources/Worlds"
TIDE = RAILS / "Tide/rail_tide_wide_v1.png"
EMBER = RAILS / "Ember/rail_ember_wide_v1.png"
TILE = ART / "Backgrounds/Resources/Worlds/Tide/Backdrop3/v2/mid.png"
PREVIEW = HERE / "preview.png"


def bands(arr):
    hue, sat, val = rgb_to_hsv(arr[:, :, :3])
    valid = (arr[:, :, 3] >= 251) & (sat >= 0.12) & (val >= 0.12)
    def share(mask):
        return float(np.mean(mask & valid))
    return {
        "red": share((hue >= 345) | (hue < 15)),
        "cyan": share((hue >= 170) & (hue <= 186)),
        "lime": share((hue >= 74) & (hue <= 90)),
        "orange": share((hue >= 30) & (hue <= 44)),
        "mint_pixels": int(np.sum(valid & (hue >= 145) & (hue <= 165) & (sat >= .3) & (val >= .7))),
        "pink": share((hue >= 300) & (hue < 345) & (sat >= .4) & (val >= .3)),
        "violet_pixels": int(np.sum(valid & (hue >= 215) & (hue <= 240) & (sat >= .35) & (val >= .45))),
    }


def alpha_metrics(arr):
    alpha = arr[:, :, 3]
    gap_count = 0
    rows_with_gaps = 0
    for row in alpha:
        visible = np.flatnonzero(row >= 128)
        if len(visible):
            gaps = int(np.sum(row[visible[0]:visible[-1] + 1] == 0))
            gap_count += gaps
            rows_with_gaps += gaps > 0
    return {
        "transparent": float(np.mean(alpha == 0)),
        "partial": float(np.mean((alpha >= 1) & (alpha <= 250))),
        "gap_pixels": gap_count,
        "gap_rows": rows_with_gaps,
    }


def make_preview():
    names = ["Space", "Frost", "Verdant", "Ember", "Tide"]
    files = [
        RAILS / "Space/rail_space_wide_v1.png",
        RAILS / "Frost/rail_frost_wide_v1.png",
        RAILS / "Verdant/rail_forest_wide_v1.png",
        EMBER,
        TIDE,
    ]
    width, height = 290, 868
    gutter, label_height = 12, 24
    panel_width = width + gutter
    canvas = Image.new("RGB", (panel_width * 5 + gutter, (height + label_height) * 2 + gutter), "#0b0b1a")
    tile = Image.open(TILE).convert("RGB").resize((width, height), Image.Resampling.NEAREST)
    draw = ImageDraw.Draw(canvas)
    for col, (name, file) in enumerate(zip(names, files)):
        x = gutter + col * panel_width
        rail = Image.open(file).convert("RGBA").resize((width, height), Image.Resampling.NEAREST)
        for row in range(2):
            y = label_height + row * (height + label_height)
            base = Image.new("RGBA", (width, height), "#0b0b1a") if row == 0 else tile.convert("RGBA")
            base.alpha_composite(rail)
            canvas.paste(base.convert("RGB"), (x, y))
            draw.text((x, y - 16), name + (" / dark" if row == 0 else " / Tide mid"), fill="#dddddd")
    canvas.save(PREVIEW, optimize=True)


def main():
    image = Image.open(TIDE)
    assert image.mode == "RGBA", f"mode {image.mode}"
    assert image.size == (725, 2170), f"size {image.size}"
    tide = np.asarray(image)
    ember = np.asarray(Image.open(EMBER).convert("RGBA"))
    am = alpha_metrics(tide)
    em = alpha_metrics(ember)
    band = bands(tide)
    hue, sat, val = rgb_to_hsv(tide[:, :, :3])
    oversaturated_brown = int(np.sum((tide[:, :, 3] == 255) & (hue >= 15) &
                                     (hue <= 28) & (sat > .46) & (val > .12)))
    colors = len(np.unique(tide.reshape(-1, 4), axis=0))
    row_diff = np.abs(tide[0].astype(np.int16) - tide[-1].astype(np.int16))
    seam_max = int(row_diff.max())
    seam_mean = float(row_diff.mean())
    assert seam_max <= 1, f"seam max diff {seam_max}"
    assert abs(am["transparent"] - em["transparent"]) <= .08, "transparency share"
    assert am["gap_pixels"] > 1000 and am["gap_rows"] > 100, "missing alpha gaps"
    assert am["partial"] <= em["partial"] + 1e-9, "partial-alpha halo share"
    assert band["red"] <= .02, "too much red"
    assert band["cyan"] <= .01, "too much cyan"
    assert band["lime"] <= .01, "too much lime"
    assert band["orange"] <= .01, "too much orange"
    assert band["mint_pixels"] >= 1000, "mint lamps absent"
    assert band["pink"] < .01, "pink warning lights too large"
    assert band["violet_pixels"] >= 20, "blue-violet accents absent"
    assert oversaturated_brown == 0, "rust-brown saturation exceeds limit"
    assert colors >= 3000, "insufficient color detail"
    make_preview()
    print("PASS 725x2170 RGBA")
    print(f"seam max/mean channel difference: {seam_max}/{seam_mean:.3f}")
    print(f"transparent: Tide {am['transparent']:.2%}, Ember {em['transparent']:.2%}")
    print(f"partial alpha: Tide {am['partial']:.2%}, Ember {em['partial']:.2%}")
    print(f"internal transparent gaps: {am['gap_pixels']} pixels across {am['gap_rows']} rows")
    print("hue-band image shares: " + ", ".join(f"{key} {band[key]:.2%}" for key in ("red", "cyan", "lime", "orange", "pink")))
    print(f"mint lamp pixels: {band['mint_pixels']}; violet pixels: {band['violet_pixels']}; unique RGBA colors: {colors}")
    print(f"oversaturated brown texels: {oversaturated_brown}")
    print(f"preview: {PREVIEW}")


if __name__ == "__main__":
    main()
