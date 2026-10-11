# Rimebreaker v2

The ship is a new image-generated painting converted to a native pixel strip with nearest-neighbour sampling. `rimebreaker_candidate_a.png`, `_b.png`, and `_c.png` are the three generated hero candidates. B was chosen for its compact hull, clear twin bells, and large readable steel and ice planes. `rimebreaker_concept.png` is the selected source painting. The source prompts are in `src~/prompts.md`.

Run `python3 src~/convert.py` followed by `python3 src~/verify.py` from this directory to rebuild and audit the deliverables.

| Cell | Pose |
| --- | --- |
| 0 | Landed, engines off |
| 1 | Ground idle, restrained coolant lights |
| 2 | Lift-off, white-hot nozzles and long hard-edged jets |
| 3 | Straight hover, short jets |
| 4 | Bank left, 8° nearest-neighbour rotation |
| 5 | Bank right, 8° nearest-neighbour rotation |
| 6 | Complete damaged hull, ice cracks and one sparking thruster |

Cell 3 gameplay points: prow muzzle `(98,43)`, left nozzle `(85,133)`, right nozzle `(109,133)`. The comparison sheet stacks the new strip, the old Rimebreaker, Floe Harrower, and Storm Rod Dancer, in that order. Each strip is shown at 2× on `#0b0b1a`.

The hull's opaque centroid in cell 3 is near `(95.5,101.0)`. Its rear stabilisers extend the hull to 124 px while the muzzle stays at y=43 and the nozzles stay at y=133; this places the visual mass 5 px below the canvas centre. The pivot remains the cell centre `(96,96)`.
