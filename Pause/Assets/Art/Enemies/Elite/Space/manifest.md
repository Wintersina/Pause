# Space Elite Ship Concepts

Four art-only elite designs, based on the compact industrial language of Frost's
Rimebreaker and Ember's Kilnback. These are review candidates and are not wired
into the enemy roster.

Each `_strip_candidate.png` is a transparent 1344 x 192 strip of seven
192 x 192 cells:

| Cell | State |
| --- | --- |
| 0 | Engines off |
| 1 | Low idle glow |
| 2 | Ignition |
| 3 | Straight hover |
| 4 | Bank left |
| 5 | Bank right |
| 6 | Damaged |

Each `_motion_candidate.png` is a transparent 768 x 192 strip of four
192 x 192 cells: idle, bank left, bank right, and booster engaged.

| Asset stem | Design |
| --- | --- |
| `space_elite_rift_lancer` | Narrow split-prong ion interceptor |
| `space_elite_eventide_bastion` | Broad shield gunship with a cyan emitter |
| `space_elite_orbit_reaver` | Open-crescent claw raider |
| `space_elite_singularity_hauler` | Ringed salvage craft carrying a magenta core |

The high-resolution `_concept.png` files are the source masters. Regenerate
both review strip types with `python3 src~/build_strips.py` after changing a
master. Candidate suffixes keep Unity's `EliteArtSync` from installing them.
