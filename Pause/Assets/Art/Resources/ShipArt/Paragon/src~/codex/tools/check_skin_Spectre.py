"""Check every Spectre cell against its embedded approved reference.

Default exit status enforces the requested literal, full-cell 12% change rule.
That rule conflicts with changing only red armour and preserving hit-flash; use
--ship-pixels-and-exempt-flash if the intended denominator is the visible ship.
"""

import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
POSES = (
    "key",
    "bob up",
    "lights off",
    "glint 1",
    "glint 2 off",
    "bob down",
    "bank left",
    "bank right",
    "hit flash",
)


def masks(ref, painted, state):
    src = ref.astype(np.int16)
    gen = painted.astype(np.int16)
    r, g, b = src.transpose(2, 0, 1)
    pr, pg, pb = gen.transpose(2, 0, 1)
    background = (g > r * 1.7) & (g > b * 1.7) & (g > 120)
    warm = (r > g * 1.25) & (r > b * 1.15) & (r > 50) & (g < 205)
    blue = (b > r * 1.25) & (b > g * 0.85) & (b > 35) & ~background
    ink = src.max(2) < 65
    armour = warm & ~background & ~blue & ~ink
    if state:
        generated_orange = (
            (pr > pg * 1.18)
            & (pg > pb * 1.15)
            & (pr > 110)
            & (pg > 50)
            & (pb < 140)
        )
        effect_neighbourhood = np.asarray(
            Image.fromarray((generated_orange * 255).astype(np.uint8)).filter(
                ImageFilter.MaxFilter(3)
            )
        ) > 0
        yy, xx = np.indices((1536, 1536))
        cell_x, cell_y = xx % 512, yy % 512
        damage_area = ((cell_x < 125) & (cell_y > 300)) | (
            (cell_x > 205) & (cell_x < 310) & (cell_y > 335)
        )
        if state == 2:
            damage_area |= (cell_x > 310) & (cell_y > 185)
        armour &= ~(effect_neighbourhood & damage_area)
    return background, blue, armour


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ship-pixels-and-exempt-flash",
        action="store_true",
        help="Measure change over visible ship pixels and exempt the unchanged hit flash",
    )
    args = parser.parse_args()
    practical = args.ship_pixels_and_exempt_flash
    bad = 0
    per_state = []
    print(
        "state pose         silhouette% blue-diff% silver% "
        "changed-cell% changed-ship% result"
    )
    for state in range(3):
        reference = np.asarray(
            Image.open(HERE / f"Spectre_reference_state{state}.png").convert("RGB")
        )
        painted = np.asarray(
            Image.open(HERE / f"Spectre_painted_state{state}.png")
            .convert("RGB")
            .resize((1536, 1536), Image.Resampling.NEAREST)
        )
        output_path = OUT / f"skin_Spectre_state{state}.png"
        output_image = Image.open(output_path).convert("RGB")
        if output_image.size != (3072, 3072):
            raise ValueError(f"{output_path.name}: expected 3072x3072")
        output = np.asarray(output_image.resize((1536, 1536), Image.Resampling.NEAREST))
        background, blue, armour = masks(reference, painted, state)
        reference_background = np.all(reference == [0, 255, 0], axis=2)
        output_background = np.all(output == [0, 255, 0], axis=2)
        changed = np.any(output != reference, axis=2)
        hsv = np.asarray(Image.fromarray(output).convert("HSV"))
        hue, sat, val = hsv.transpose(2, 0, 1)
        target_silver = (((hue >= 131) & (hue <= 159) & (sat <= 115)) |
                         ((sat <= 20) & (val >= 175)))
        state_values = []
        for pose in range(9):
            sl = np.s_[pose // 3 * 512:(pose // 3 + 1) * 512,
                       pose % 3 * 512:(pose % 3 + 1) * 512]
            ref_ship = ~reference_background[sl]
            silhouette = np.count_nonzero(reference_background[sl] != output_background[sl]) / max(1, np.count_nonzero(ref_ship)) * 100
            blue_diff = np.count_nonzero(changed[sl] & blue[sl]) / max(1, np.count_nonzero(blue[sl])) * 100
            silver = np.count_nonzero(target_silver[sl] & armour[sl]) / max(1, np.count_nonzero(armour[sl])) * 100
            changed_cell = np.mean(changed[sl]) * 100
            changed_ship = np.count_nonzero(changed[sl]) / max(1, np.count_nonzero(ref_ship)) * 100
            flash = pose == 8
            if flash and practical:
                ok = silhouette < 1.5 and blue_diff < 6 and np.count_nonzero(changed[sl]) == 0
                silver_text = "N/A"
                status = "EXEMPT" if ok else "FAIL"
            else:
                basis = changed_ship if practical else changed_cell
                ok = silhouette < 1.5 and blue_diff < 6 and silver > 85 and basis > 12
                silver_text = f"{silver:7.2f}"
                status = "PASS" if ok else "FAIL"
            if not ok:
                bad += 1
            if not flash:
                state_values.append((silhouette, blue_diff, silver, changed_cell, changed_ship))
            print(
                f"{state:5d} {POSES[pose]:12s} {silhouette:11.2f} {blue_diff:10.2f} "
                f"{silver_text:>7s} {changed_cell:13.2f} {changed_ship:13.2f} {status}"
            )
        per_state.append(np.array(state_values))
    print("summary (eight coloured poses per state):")
    print("state max silhouette max blue min silver min changed-cell min changed-ship")
    for state, values in enumerate(per_state):
        print(
            f"{state:5d} {values[:, 0].max():14.2f} {values[:, 1].max():8.2f} "
            f"{values[:, 2].min():10.2f} {values[:, 3].min():16.2f} {values[:, 4].min():16.2f}"
        )
    print(f"{bad} failed cells under {'ship-pixel / flash-exempt' if practical else 'literal full-cell'} rules")
    raise SystemExit(1 if bad else 0)


if __name__ == "__main__":
    main()
