#!/usr/bin/env python3
"""Run the Desert palette, layout, motion, detail, and backdrop checks."""
import sys
from audit import rows, print_table

EXPECTED = {
    "desert_alien", "desert_chaser", "desert_big",
    *(f"desert_fighter_{i}" for i in range(1, 5)),
    "desert_rock_dune", "desert_rock_spire", "desert_rock_husk", "desert_rock_slab",
}


COLOURFUL = {"desert_alien", "desert_chaser", "desert_big", "desert_fighter_1", "desert_fighter_2", "desert_fighter_3", "desert_fighter_4"}
SAND_ONLY = ("sand saturation p95", "copper material", "amber pickup collision", "violet", "tile standout")


def main():
    results = rows()
    print_table(results)
    failures = []
    keys = {r["key"] for r in results}
    if keys != EXPECTED:
        failures.append(f"roster mismatch: missing={EXPECTED-keys}, extra={keys-EXPECTED}")
    for r in results:
        key = r["key"]
        need = 6000 if key == "desert_big" else 3000
        checks = {
            "cell side": r["cell"] == (256 if key == "desert_big" else 192),
            "detail": r["tones"] >= 300 and r["boundaries"] >= need,
            "outer columns": r["side_columns"] == 0,
            "straight cut": r["cut"] < .15 * r["cell"],
            "centroid": r["drift"] <= 3,
            "idle area": r["area_lo"] >= .96 and r["area_hi"] <= 1.04,
            "margins": min(r["margins"]) >= 6,
            "motion including seam": min(r["motion"]) >= 3,
            "sand saturation p95": r["sand_p95"] <= .33,
            "copper material": r["copper_bad"] == 0,
            "amber pickup collision": r["amber"] <= .3,
            "player red": r["red"] <= (.2 if key in COLOURFUL else 0),
            "pink": r["pink"] <= .2,
            "violet": r["violet"] <= .5,
        }
        if key in COLOURFUL:   # the saturated multi-accent repaint (art/enemy-colors-desert) is deliberately off the sand ramp
            for n in SAND_ONLY: checks.pop(n, None)
        elif r["standout"]:
            checks["tile standout"] = min(r["standout"]) >= (15 if key == "desert_alien" else 10)
        failures.extend(f"{key}: {name}" for name, passed in checks.items() if not passed)
    if failures:
        for message in failures:
            print("FAIL", message)
    print(f"RESULT: {'FAIL' if failures else 'PASS'} ({len(failures)} failures; {len(results)} strips)")
    return bool(failures)


if __name__ == "__main__":
    sys.exit(main())
