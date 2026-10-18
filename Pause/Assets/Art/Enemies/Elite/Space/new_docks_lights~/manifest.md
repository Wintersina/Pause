# Space elite lights and new dock bodies

Staging folder: `new_docks_lights~` (the trailing `~` deliberately keeps Unity from importing these assets until they are wired).
All PNGs are RGBA unless noted. Pixel filtering should be Point in Unity; the generated art is transparent outside each body.

## Hull-light overlays

The source strips are the four installed Space elite strips, each exactly 4608 x 384 px: twelve 384 x 384 cells in order `0 parked`, `1 pilot lights`, `2 ignition`, `3-10 burn cycle`, `11 damaged`.
Each overlay below has the same 4608 x 384 layout and is intended to be composited additively over the matching source strip without changing the hull.

| File | Size / cells | Notes |
| --- | --- | --- |
| `space_elite_rift_lancer_lights.png` | 4608 x 384; 12 x 384 cells | Cyan/magenta running lights, spine chase, beacon blink, emitter glints; damaged cell is partly dead/flickering. |
| `space_elite_eventide_bastion_lights.png` | 4608 x 384; 12 x 384 cells | Violet/cyan lights with the same timing pattern. |
| `space_elite_orbit_reaver_lights.png` | 4608 x 384; 12 x 384 cells | Magenta/pink claw, lens and spine lights with the same timing pattern. |
| `space_elite_singularity_hauler_lights.png` | 4608 x 384; 12 x 384 cells | Amber running lights and magenta core/emitter lights with the same timing pattern. |

Frame timing in cells: 0-2 are dim/off in increasing ignition states; cells 3-10 are the eight-step loop. The beacon is on for loop phases 1-2, the running light advances along detected accent/lamp points, and emitters pulse through phases 2-5. Cell 11 uses reduced, irregular output to read as damaged.

`build_art.py` derives candidate light points from bright, saturated pixels in source cell 0, keeps them inside the detected hull region, and draws only small 1-4 px emissive cores plus a 1-2 px soft glow. It is the reproducible source for all overlay and preview outputs.

| File | Size / cells | Notes |
| --- | --- | --- |
| `space_elite_<name>_preview_lights.png` | 4608 x 384; 12 x 384 cells | Matching hull strip composited with its light overlay for review. |
| `space_elite_<name>_lights.gif` | 384 x 384; 8 GIF frames | Burn-cycle preview using source cells 3-10 plus their overlays; 130 ms/frame, looped. |

## New launch bodies

Each base is a transparent, top-down/three-quarter Space body. The pad point is an exact pixel coordinate in the base canvas, at the center of the dark launch opening/iris where the elite emerges. Each launch strip uses six same-size cells in this order: `0 idle`, `1 lights blink on`, `2 doors/iris opening`, `3 flare`, `4 fully open`, `5 closing`.

| Base file | Base size | Pad/opening pixel | Launch strip |
| --- | ---: | ---: | --- |
| `orbital_shipyard_gantry.png` | 1024 x 936 | (512, 593) | `orbital_shipyard_gantry_launch.png`, 6144 x 936; six 1024 x 936 cells |
| `carrier_hangar.png` | 1024 x 512 | (577, 360) | `carrier_hangar_launch.png`, 6144 x 512; six 1024 x 512 cells |
| `ring_gate.png` | 1024 x 950 | (511, 397) | `ring_gate_launch.png`, 6144 x 950; six 1024 x 950 cells |
| `derelict_dock.png` | 1024 x 836 | (512, 516) | `derelict_dock_launch.png`, 6144 x 836; six 1024 x 836 cells |

The launch strips are generated from their transparent bases by `build_art.py`: dark mechanical shutters/iris states and small colored flare light are layered over the named pad while leaving the surrounding industrial silhouette unchanged.

## Review / tooling

| File | Description |
| --- | --- |
| `contact_sheet.png` | 1440 x 660 RGB review sheet: four docks across the top and one representative lit cell from each elite across the bottom. |
| `build_art.py` | PIL build script for overlays, 8-frame GIFs, six-cell dock launch strips, base normalization to a 1024 px maximum long side, and contact sheet. |

No C# or gameplay files were changed. The dock bodies were generated as transparent HD neon pixel art and checked beside the existing Space landmark preview for silhouette, density, palette and launch-opening readability.
