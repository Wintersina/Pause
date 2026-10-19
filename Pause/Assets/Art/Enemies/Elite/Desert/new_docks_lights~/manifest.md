# Desert elite lights, launch sites, and damaged loops

Art-only staging output for the Desert world. No C# or Unity scripts were changed.
The source header and live elite defs establish 384 px cells and 9 cells per
strip (3456 x 384): `0 parked`, `1 parked idle`, `2 liftoff`, `3/7/8 flight
loop`, `4 bank left`, `5 bank right`, `6 damaged`; `tell`, `action`, and `hit`
are `-1`. Light GIF order is `3,4,5,7,8,0` (six review frames).

## Hull lights

For every ship below, the three files are present with the listed dimensions:

| Ship | Overlay | Preview | GIF |
|---|---|---|---|
| dune_stalker | `desert_elite_dune_stalker_lights.png` 3456x384 | `desert_elite_dune_stalker_preview_lights.png` | `desert_elite_dune_stalker_lights.gif` |
| caravan_hauler | `desert_elite_caravan_hauler_lights.png` 3456x384 | `desert_elite_caravan_hauler_preview_lights.png` | `desert_elite_caravan_hauler_lights.gif` |
| glass_cannon | `desert_elite_glass_cannon_lights.png` 3456x384 | `desert_elite_glass_cannon_preview_lights.png` | `desert_elite_glass_cannon_lights.gif` |
| lens_matriarch | `desert_elite_lens_matriarch_lights.png` 3456x384 | `desert_elite_lens_matriarch_preview_lights.png` | `desert_elite_lens_matriarch_lights.gif` |
| mirror_bastion | `desert_elite_mirror_bastion_lights.png` 3456x384 | `desert_elite_mirror_bastion_preview_lights.png` | `desert_elite_mirror_bastion_lights.gif` |
| scorpion_walker | `desert_elite_scorpion_walker_lights.png` 3456x384 | `desert_elite_scorpion_walker_preview_lights.png` | `desert_elite_scorpion_walker_lights.gif` |

Parked cells are dim; flight cells chase accent pixels; cell 6 is flickering
and partly dead. Overlays are transparent and pixel-aligned to the source.

## New launch sites

Every base and registered open image is 768x768 RGBA. Every launch strip is
4608x768 RGBA, exactly six 768 px cells in this order: `0 closed`, `1 closed
+ one small amber lamp`, `2 ~40% opening`, `3 ~75% opening`, `4 fully open`,
`5 ~50% closing`. The builders copy the base over all pixels outside the hatch
mask before rendering the strip. Frames 2-5 add two/three-tone sand-grey
dithered dust/heat haze, clipped to the hatch plus an 8 px spill. No flash,
flare, or halo is used.

| Site | Base / registered open / strip | Pad pixel | Pad x / cell width | Hatch mask | Mechanism |
|---|---|---:|---:|---|---|
| buried_bunker | `buried_bunker.png` / `buried_bunker_open.png` / `buried_bunker_launch_v2.png` | `(384,430)` | `0.500` | rectangle `(220,300)-(548,555)` | two blast doors slide apart on the bunker horizontal axis |
| crawler_hangar | `crawler_hangar.png` / `crawler_hangar_open.png` / `crawler_hangar_launch_v2.png` | `(384,470)` | `0.500` | rectangle `(250,390)-(518,620)` | rear ramp/hangar hatch drops along the crawler axis |
| mirror_array_silo | `mirror_array_silo.png` / `mirror_array_silo_open.png` / `mirror_array_silo_launch_v2.png` | `(384,430)` | `0.500` | circle center `(384,430)`, radius `125` | eight lens-cap petals iris outward |
| canyon_dock | `canyon_dock.png` / `canyon_dock_open.png` / `canyon_dock_launch_v2.png` | `(360,470)` | `0.469` | rectangle `(245,330)-(520,535)` | roll-up shutter retracts into the cliff lintel |

`launch_v2_contact_sheet.png` is 960x920 and shows all four six-cell strips;
`launch_v2_mist_zoom.png` is 1120x1160 and shows cells 2-5 at each bay.
`build_launch_v2.py` asserts that changed pixels stay inside the hatch (plus
8 px dust spill) and that the declared opening coverage is monotonic:
`0.40, 0.75, 1.00, 0.50` with closing treated as the reverse reveal.

## Damaged burn loops

For every ship, the damaged directory contains the four files below:

`damaged/desert_elite_<ship>_damaged.png` (2304x384, six cells),
`damaged/desert_elite_<ship>_damaged_lights.png` (2304x384),
`damaged/desert_elite_<ship>_damaged_preview.png` (2304x384), and
`damaged/desert_elite_<ship>_damaged.gif` (six 384x384 frames).

The six ships are `dune_stalker`, `caravan_hauler`, `glass_cannon`,
`lens_matriarch`, `mirror_bastion`, and `scorpion_walker`. The painted damaged
engine flames are restored from parked cell 0, then weak ragged flames are
drawn with independent per-engine phases; hull pixels remain unchanged across
the loop. All six have engines, so no walker/vent substitution was needed.
`damaged/contact_sheet.png` shows all six loops.

## Builders and final review

`build_art.py`, `build_launch_v2.py`, and `build_damaged.py` are the three
reproducible builders. `contact_sheet.png` is 1440x960 and shows the four
sites once plus one lit cell for all six elites.

Complete top-level files: the three builders; the twelve site files named in
the launch-site table; `launch_v2_contact_sheet.png`; `launch_v2_mist_zoom.png`;
the eighteen ship light files in the hull-light table; `contact_sheet.png`.
Complete damaged files: four files for each of the six ships named above plus
`damaged/contact_sheet.png`.

Validation completed: launch strips have 6 cells, elite light strips have 9
cells, damaged strips have 6 cells, all required PNG/GIF frames were checked,
and no saturated pink/magenta or green pixels were found in the new art.
