# Desert elite flight art

> **HD rebuild:** Dune Stalker, Caravan Hauler, Lens Matriarch, Scorpion Walker, Mirror Bastion (not the Glass Cannon: no master) are now 384 px cells (2688 x 384 flight strip, 1152 x 384 death strip), rebuilt from the same painted masters with an area-averaging downsample (`Desert/src~/build_desert_hd.py`, shared helpers `Elite/src~/elite_hd.py`, installed by `Elite/src~/install_hd.py`). Poses, frame order, hull radius and scale tier are unchanged; the 192 px coordinates below are x2 in the defs (`cellPixels` 384). See docs/ship-scale-and-lockon-analysis.md 2.6.


Five staged, art-only elite ships for Desert's atmosphere-level run. Their source hulls were painted with the built-in image-generation tool. Two candidates were made for every ship; the selected painting was cleaned, resized, registered and animated with nearest-neighbour pixels by `src~/build.py`. No Unity resource, metadata, definition or code was changed.

## Cell contract

Each `desert_elite_<name>.png` is **1344 × 192 RGBA**, seven 192 × 192 cells. Each `_death.png` is **576 × 192 RGBA**, three 192 × 192 cells. Cell coordinates below start at the left and top of the individual 192 px cell.

| Flight cell | State | Read |
| --- | --- | --- |
| 0 | Landed | Engines off; parts tucked toward the hull. |
| 1 | Grounded idle | One restrained sun-white status glint. |
| 2 | Lift-off ignition | Long sun-white rear jets; outer parts begin to flex. |
| 3 | Straight hover | Short jets and anatomy-specific moving tips. |
| 4 | Bank left | Hull stays registered while outer parts flex left. |
| 5 | Bank right | Counterpart bank with moving outer parts. |
| 6 | Second-heart damage | Dark fracture, pale exposed edge and tiny sun-white sparks. |

| Death cell | State |
| --- | --- |
| 0 | Full hull at hover anchor with a sun-white core flash. |
| 1 | Ruptured hull, missing armour sectors and exposed white-hot core. |
| 2 | Painted armour and glass fragments with desaturated dust and hard sparks; no intact hull. |

## Ships and approximate hover-cell points

These are art positions for a future wiring pass. Hostile shot and heart colours belong in gameplay code; none are painted into the hulls. All ships point up.

| Key / role | Launch site and attack idea | Engine nozzles (x,y) | Weapon or payload origins (x,y) | Moving part |
| --- | --- | --- | --- | --- |
| `desert_elite_dune_stalker` / interceptor | Sand hatch in worm-burrow hole; lance dash through a sand wake and grit needles | (88,158), (104,158) | ram lance (96,26); grit ports (55,104), (137,104) | Swept fins flex; segmented tail streams. |
| `desert_elite_caravan_hauler` / hauler | Crawler bay; drops sinking quicksand canisters | (64,145), (128,145) | canister drop (96,134); bucket-wheel (96,44) | Side bunks flex; cradle sways. |
| `desert_elite_lens_matriarch` / skirmisher | Glass pad; blink and mirage fan of real shards among hollow decoys | (90,163), (102,163) | fan (96,54); port shard (62,67); starboard shard (130,67) | Mirror petals spread; glass spine glints. |
| `desert_elite_scorpion_walker` / striker | Ruin dock; tail venom lash and claw dive | (88,160), (104,160) | sting (106,36); claws (60,132), (132,132) | Claws open, legs step and stinger curls; sting dot is two pale pink-white pixels. |
| `desert_elite_mirror_bastion` / shield tank | Buried silo; ward curtain of slow glass shards with one gap | (82,158), (96,158), (110,158) | lens cannon (96,51); ward gap (96,34) | Overlapping shield plates breathe around the gap. |

## Source selection

| Ship | Selected | Reason |
| --- | --- | --- |
| Dune Stalker | candidate B | Clear shark-like fins and segmented burrowing tail. |
| Caravan Hauler | candidate B | Three suspended canisters read inside the cargo cradle. |
| Lens Matriarch | candidate B | Six separate mirror petals form a legible fan. |
| Scorpion Walker | candidate B | Curl, claws and folded legs remain distinct. |
| Mirror Bastion | candidate A | Heavier overlapping shield curtain and clear forward gap. |

## Measured audit

Run `python3 Pause/Assets/Art/Enemies/Elite/Desert/src~/verify.py` from the worktree root. Full per-cell results are in `src~/verification.json`. Tile contrast is the share of opaque hover pixels whose HSV value differs from the central crop of a real `v1..v4/mid.png` tile by at least 0.12. It measures value separation, including dark outline pixels; it is not an in-game visibility test.

| Ship | Hover bbox size | Bank L/R size | Centroid delta L/R | Min clear margin, all flight cells | Hover colours | Min tile contrast, v1..v4 | Red / pickup-amber max | Border cuts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Dune Stalker | 116×138 | 113×138 / 113×138 | 2.04 / 1.99 px | 20 px | 2,366 | 62.9% | 0 / 0% | 0 |
| Caravan Hauler | 119×112 | 116×112 / 116×112 | 2.46 / 2.45 px | 33 px | 4,013 | 61.5% | 0 / 0% | 0 |
| Lens Matriarch | 107×147 | 104×147 / 105×147 | 1.63 / 1.69 px | 17 px | 3,435 | 71.3% | 0 / 0% | 0 |
| Scorpion Walker | 117×141 | 115×141 / 115×141 | 1.77 / 1.72 px | 18 px | 3,495 | 65.8% | 0 / 0% | 0 |
| Mirror Bastion | 120×134 | 117×134 / 117×134 | 2.53 / 2.51 px | 20 px | 4,907 | 60.4% | 0 / 0% | 0 |

No flight or death cell touches a border. Red-band pixels (hue 345–15, saturation >.5), saturated pickup-amber pixels (hue 30–48, saturation >.45), violet and hostile-pink pixels are 0% in the measured strips, aside from the Scorpion's two very pale sting pixels, whose saturation is below the pink-band threshold. `contact_sheet.png` stacks the five strips plus approved Space Eventide Bastion at 2× on `#0b0b1a`. `preview.gif` cycles flight cells **3 → 4 → 3 → 5** at 2×.

## Review caveats

The Hauler and Bastion have the closest binary silhouettes (intersection-over-union 0.631); the Bastion's shield curtain and the Hauler's canisters carry much of their distinction. Banked details are nearest-neighbour warps of painted anatomy, and the death shards reuse painted hull pieces; a later hand-authored animation pass could give them more unique poses. Muzzle coordinates are approximate until checked in Unity at game scale.
