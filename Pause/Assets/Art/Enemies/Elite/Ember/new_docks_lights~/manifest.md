# Ember elite lights, launch sites, and damaged burn loops

Staging folder: `new_docks_lights~`. Art-only review assets; no C# or gameplay files changed. PNGs are RGBA except the RGB review sheets. Use Point filtering for all strips.

## Hull lights

All six installed Ember elite sheets are `3072 x 384`, with eight `384 x 384` cells. Their defs identify the same layout: cell `0 parked`, `1 parkedIdle`, `2 liftoff`, `3–4 flight`, `5 bankLeft`, `6 bankRight`, `7 damaged`; `tell`, `action`, and `hit` are `-1` and have no dedicated cells. The overlay keeps all eight cells. Review GIFs use source cells `3, 4, 5, 6, 7, 2` in that order, six frames at 130 ms.

| Ship | overlay / preview | GIF |
| --- | --- | --- |
| Ash Wraith | `ember_elite_ash_wraith_lights.png`, `ember_elite_ash_wraith_preview_lights.png` — 3072 x 384, 8 x 384 | `ember_elite_ash_wraith_lights.gif` — 6 x 384 x 384 |
| Brass Vulture | matching files — 3072 x 384, 8 x 384 | matching GIF — 6 frames |
| Cauterizer | matching files — 3072 x 384, 8 x 384 | matching GIF — 6 frames |
| Coalrunner | matching files — 3072 x 384, 8 x 384 | matching GIF — 6 frames |
| Kilnback | matching files — 3072 x 384, 8 x 384 | matching GIF — 6 frames |
| Sunstoke | matching files — 3072 x 384, 8 x 384 | matching GIF — 6 frames |

The overlay is additive and pixel-aligned. Parked cells are dim, flight cells carry a short running accent chase, and the damaged cell is flickering and partly dead. `build_art.py` is the reproducible builder.

## New Ember launch sites

Each base is a transparent top-down set piece normalized to a maximum long side of 768 px. Pad coordinates are the center of the launch opening; the pad `x` is also recorded as a fraction of the cell width for atlas placement. Every launch strip is six same-size cells in this exact order: `0 closed` → `1 closed + one small amber warning lamp` → `2 opening` → `3 opening` → `4 fully open` → `5 closing, half shut`. Doors are shaded, segmented iron/brass panels with seams, rivets, edge heat, and a depth-detailed bay at full open. No flash, flare, halo, glow burst, smoke, or other launch cue is added; only the single small lamp appears in cell 1.

| Base | Size | Pad pixel | Launch strip |
| --- | ---: | ---: | --- |
| `crucible_silo.png` | 768 x 767 | `(384, 399)`; x `0.500` | `crucible_silo_launch_v2.png`, 4608 x 767; 6 x 768 x 767 |
| `lava_moat_bunker.png` | 767 x 768 | `(384, 384)`; x `0.500` | `lava_moat_bunker_launch_v2.png`, 4602 x 768; 6 x 767 x 768 |
| `brass_gantry_cradle.png` | 512 x 768 | `(256, 407)`; x `0.500` | `brass_gantry_cradle_launch_v2.png`, 3072 x 768; 6 x 512 x 768 |
| `kiln_mouth.png` | 768 x 512 | `(384, 276)`; x `0.500` | `kiln_mouth_launch_v2.png`, 4608 x 512; 6 x 768 x 512 |

## Damaged burn loops

`damaged/` contains six-cell `2304 x 384` strips, matching `_damaged_lights.png`, `_damaged_preview.png`, and six-frame `384 x 384` GIFs for every ship, plus `damaged/contact_sheet.png`. The painted exhaust in source cell 7 is erased with the parked-cell bell patch before a weak ragged loop is drawn. Each engine has its own phase and occasional sputter; the hull base is identical in every loop frame. `build_damaged.py` is reproducible.

## Review and builders

`contact_sheet.png` is the RGB review sheet with the four new sites and six lit ships. `launch_v2_contact_sheet.png` is the six-frame review sheet for the corrected launch order. `build_launch_v2.py` builds the four v2 launch strips and contact sheet; `build_art.py` builds hull lights and the older staging assets; `build_damaged.py` builds the damaged companions. All generated/review assets were palette-sanitized to Ember orange, amber, molten yellow, brass, soot-grey, and dull red; no pink pixels remain.

Complete top-level inventory: `build_art.py`, `build_damaged.py`, `build_launch_v2.py`, this manifest, four base PNGs, four legacy launch strips, four corrected `*_launch_v2.png` strips, six light overlays, six lit previews, six light GIFs, `contact_sheet.png`, and `launch_v2_contact_sheet.png`. Complete damaged inventory: six damaged strips, six damaged light overlays, six damaged previews, six damaged GIFs, and `damaged/contact_sheet.png`.
