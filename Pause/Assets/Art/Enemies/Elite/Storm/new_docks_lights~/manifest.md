# Storm elite lights, launch sites, and damaged loops

Art-only staging output for Storm. The source Storm elite strips are 3840 ×
384 RGBA: ten 384 px cells. The source roles are `0 parked`, `1 parked idle`,
`2 liftoff`, `3/7/8/9 flight loop`, `4 bank left`, `5 bank right`, and `6
damaged`; the GIF previews use flight cells `3, 4, 5, 7, 8, 9` in that order.

## Hull lights

Each `storm_elite_<name>_lights.png` is 3840 × 384 (10 × 384 cells), aligned
to its source strip. Parked cells are dim, flight cells chase accent pixels,
and cell 6 flickers with partly dead lights. Each `_lights.gif` has six frames
in order `3,4,5,7,8,9`; each `_preview_lights.png` is the composited review
strip.

## Launch sites

All four bases and their registered open drawings (`*_open.png`) are 768 ×
768 RGBA. The open drawings were image-generated as edits of the matching
base, then hard-registered by copying the base over every pixel outside the
declared hatch mask. All launch strips are 4608 × 768 (six
768 × 768 cells) with this exact order: `0 closed`, `1 closed + one amber
lamp`, `2 opening`, `3 opening`, `4 fully open`, `5 closing half shut`.
The launch generator declares and enforces these exact door regions (768 px
base coordinates): floating airship dock polygon `(214,376) (505,443)
(584,508) (318,463)`; skyhook cradle ellipse `(315,349)-(453,510)`;
refinery shutter rect `(307,398)-(462,490)`; lightning-rod silo cap circle
center `(384,430)`, radius `145`. Every changed pixel is asserted to be inside
its declared region, with only an 8 px maximum vapour expansion above the rim.
The generated open drawings carry the actual bay interiors: diagonal dock roof
panels part around a lit cradle, cradle clamps swing away from a lit well, the
refinery shutter is rolled into a visible top drum, and six silo petals fold
back around a deep shaft with lit rings. Animation reveals the registered open
drawing through a mechanism-shaped wipe: cell 2 is about 40%, cell 3 about
75%, cell 4 is fully open, and cell 5 is about 50% closing. Thin shaded wipe
edges are drawn inside the hatch. Mist is slate-white/cyan-grey,
frame-varying, clipped to the door plus the allowed 8 px rim, and there are no
launch flashes, flares, halos, or bursts.

| Site | Base / strip | Pad pixel | Pad x / cell width | Door mechanism |
|---|---|---:|---:|---|
| Floating airship dock | `floating_airship_dock.png` / `_launch_v2.png` | `(384,430)` | `0.500` | long hangar roof panels slide apart lengthwise; blimp moored alongside |
| Lightning-rod silo | `lightning_rod_silo.png` / `_launch_v2.png` | `(384,430)` | `0.500` | six domed iris petals retract outward; rod mast and insulator rings |
| Skyhook cradle | `skyhook_cradle.png` / `_launch_v2.png` | `(384,430)` | `0.500` | opposing clamp doors part beneath the top-edge tether |
| Refinery bay | `refinery_bay.png` / `_launch_v2.png` | `(384,430)` | `0.500` | rectangular shutter rolls up in segmented slats |

`launch_v2_contact_sheet.png` shows all six cells per site; `launch_v2_mist_zoom.png`
shows the bay crops for cells 2–5. Open bays use rising three-tone storm-vapour
clumps with softened/dithered edges, including partial opening in cell 2. No
launch flash, lightning burst, or halo is present. `contact_sheet.png` combines
each site exactly once with one lit flight cell for each of the five Storm elites.

## Damaged burn loops

Each `damaged/storm_elite_<name>_damaged.png` is 2304 × 384 (six 384 px
cells), with the painted damaged engine flames restored from parked cell 0 and
weak ragged six-frame flames drawn per-engine at independent phases. The hull
is unchanged across the loop. Matching `_damaged_lights.png` overlays,
`_damaged_preview.png` review strips, and six-frame `_damaged.gif` files are
included, plus `damaged/contact_sheet.png`.

All new art was checked for pink/magenta and green pixels; the generated art
uses Storm slate/indigo, electric cyan/white, brass, and amber only.

## Complete file inventory

Builders: `build_art.py`, `build_launch_v2.py`, `build_damaged.py`.

Bases, registered open states, and launch strips: `floating_airship_dock.png`,
`floating_airship_dock_open.png`, `floating_airship_dock_launch_v2.png`,
`lightning_rod_silo.png`, `lightning_rod_silo_open.png`,
`lightning_rod_silo_launch_v2.png`, `skyhook_cradle.png`,
`skyhook_cradle_open.png`, `skyhook_cradle_launch_v2.png`, `refinery_bay.png`,
`refinery_bay_open.png`, `refinery_bay_launch_v2.png`,
`launch_v2_contact_sheet.png`,
`launch_v2_mist_zoom.png`, `contact_sheet.png`.

Hull light files: for each of `skyhook_harrier`, `refinery_tender`,
`rod_dancer`, `anvil_cloud`, and `static_bastion`: `storm_elite_<name>_lights.png`,
`storm_elite_<name>_preview_lights.png`, and
`storm_elite_<name>_lights.gif`.

Damaged files: under `damaged/`, for each of the same five names:
`storm_elite_<name>_damaged.png`, `_damaged_lights.png`,
`_damaged_preview.png`, and `_damaged.gif`, plus `damaged/contact_sheet.png`.
