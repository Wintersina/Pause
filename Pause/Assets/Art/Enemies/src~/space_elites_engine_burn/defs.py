#!/usr/bin/env python3
"""Point the four Space elite defs at the engine-burn strips: flight = the 8-drawing burn cycle (cells 3..10), the bank loops 17..19 / 20..22
(<name>_banks.png, build_banks.py; Oct 25), no extra tilt. Re-run .claude/skills/add-elite-ship/scripts/measure_frame_nozzles.py --write after."""
import json
from pathlib import Path
DEF = Path(__file__).resolve().parents[3] / "Resources/Elites/Defs"
CWS = {"rift_lancer": 2.0, "eventide_bastion": 2.3, "orbit_reaver": 2.1, "singularity_hauler": 2.2}
ENGINE = {"rift_lancer": "#6FD8FF", "eventide_bastion": "#5FD0FF", "orbit_reaver": "#FF7AE8", "singularity_hauler": "#FFD060"}
for k, col in ENGINE.items():
    p = DEF / f"space_elite_{k}.json"
    d = json.load(open(p))
    d["cells"] = {"flight": list(range(3, 11)), "parked": 0, "parkedIdle": 1, "liftoff": 2, "bankLeft": 17, "bankRight": 20,
                  "bankLeftLoop": [17, 18, 19], "bankRightLoop": [20, 21, 22],
                  "tell": -1, "action": -1, "hit": -1, "damaged": 11}
    d["maxBank"] = 0
    d["cellWorldSize"] = CWS[k]
    d["engineColor"] = col
    json.dump(d, open(p, "w"), indent=2)
    open(p, "a").write("\n")
