# Storm elite flight art

Five new art-only elite ships for Storm's atmosphere-level cloud run. Each ship is an exact top-down RGBA sprite facing up, with rear thrusters facing down. The painted masters and both candidates for each ship are in `src~/`; `src~/build_storm_elites.py` assembles the final strips using nearest-neighbour sampling and pixel details. No Unity resources, definitions, metadata, or code were changed.

## Cell contract

Every `storm_elite_<name>.png` is **1344 × 192 RGBA**: seven 192 × 192 cells in one row. Every `storm_elite_<name>_death.png` is **576 × 192 RGBA**: three 192 × 192 cells.

| Flight cell | State | Visible cue |
| --- | --- | --- |
| 0 | Landed | Engines and core off; compact resting silhouette. |
| 1 | Grounded idle | Restrained white-blue core. |
| 2 | Lift-off ignition | Long, hard-edged white-blue jets. |
| 3 | Straight hover | Short jets; moving rods, coils, wings, or racks. |
| 4 | Bank left | Anchored left bank; moving outer parts flex. |
| 5 | Bank right | Anchored right bank; moving outer parts flex. |
| 6 | Second-heart damage | Dark fissure, pale cracked edge, small white-blue and pale-yellow sparks. |

| Death cell | State |
| --- | --- |
| 0 | Storm-white flash on the full painted hull. |
| 1 | Hull rupture with a white-blue energy core. |
| 2 | Painted armor fragments and hard pixel sparks. |

## Registration and attack read

Coordinates are approximate **cell-local (x,y)** pixels on hover cell 3, measured from the left and top. They are guides for a later Unity wiring pass. Hearts and hostile shot colors belong in gameplay code and are absent from these hulls.

| Ship | Launch site | Silhouette and attack idea | Engine nozzles (x,y) | Weapon / payload origins (x,y) | Visible moving part |
| --- | --- | --- | --- | --- | --- |
| Skyhook Harrier | Skyhook dock | Swept hooked interceptor; hooked dash lance | (86,158), (106,158) | hook lance (96,28) | Wings flex; winch prong pulses. |
| Refinery Tender | Refinery pad | Broad barrel hauler; sinking canisters | (74,135), (118,135) | canister drop (96,126), port rack (63,92), starboard rack (129,92) | Pipe racks and cradles bob. |
| Rod Dancer | Balloon hangar | Tall twin-rod skirmisher; blink and arc fan | (87,158), (105,158) | port rod (71,24), starboard rod (121,24), arc fan (96,76) | Rods splay; coils alternate. |
| Anvil Cloud | Gantry hatch | Flat-topped T-shaped siege hull; lane-telegraphed lightning shell | (82,150), (110,150) | shell cannon (96,37) | Anvil shoulders and recoil collar pulse. |
| Static Bastion | Cloud bay | Wide shield tank with four spires; curtain of arcs / ward | (66,151), (86,151), (106,151), (126,151) | ward core (96,90), port capacitor (51,53), starboard capacitor (141,53) | Four spires pulse; shield plates breathe. |

## Measured audit

Run `python3 Pause/Assets/Art/Enemies/Elite/Storm/src~/verify_storm_elites.py` from the worktree root. The script saves full per-cell measurements to `src~/verification.json`.

| Ship | Hover bbox (x0,y0,x1,y1) | Hover size | Bank L/R size | Centroid delta L/R | Minimum clear margin, all cells | Hover RGB colors | Maximum red-band share, all cells | Border cuts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Skyhook Harrier | (40,26,152,166) | 112×140 | 114×140 / 114×140 | 1.10 / 1.11 px | 20 px | 3,551 | 0.000% | 0 |
| Refinery Tender | (34,46,157,146) | 123×100 | 120×100 / 120×100 | 1.32 / 1.32 px | 34 px | 4,530 | 0.000% | 0 |
| Rod Dancer | (46,23,146,170) | 100×147 | 100×150 / 100×150 | 0.81 / 0.81 px | 20 px | 2,442 | 0.000% | 0 |
| Anvil Cloud | (34,33,159,159) | 125×126 | 123×126 / 123×126 | 1.06 / 1.06 px | 28 px | 4,819 | 0.000% | 0 |
| Static Bastion | (32,31,159,162) | 127×131 | 124×131 / 124×131 | 1.59 / 1.66 px | 27 px | 5,836 | 0.000% | 0 |

`contact_sheet.png` stacks all five strips and the approved Space Eventide Bastion at 2× on `#0b0b1a`. `preview.gif` shows the five Storm ships at 2×, cycling flight cells **3 → 4 → 3 → 5**. The source paintings supply the dense material detail; the generator supplies registered flight states, motion parts, and death fragments without blur or a small shared palette.
