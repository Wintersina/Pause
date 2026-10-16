# Engine flame art recipe

How the four Space elites' baked engine burn is built (`Pause/Assets/Art/Enemies/src~/space_elites_engine_burn/build_strips.py`).
Reuse it for any elite whose strip bakes its flames (the Tide elites, PR #46).

## The two rules that matter

1. **The flame goes IN FRONT of the hull, rooted inside the bell mouth.** When the master draws the engine seen from behind
   (a dark bell mouth facing the viewer), a flame composited under the hull and started at the hull's bottom edge reads as coming
   from behind the engine. Find each mouth, root the flame inside it, draw it over the hull.
2. **Nozzle points = the mouth centre**, not the bell body (`points.py` uses the same openings, so EliteTest / SpaceEliteTest
   measure where the flame really leaves).

## Pipeline per drawing (`engine_cell`)

Order, bottom to top: hull -> **spill** -> **inner glow** -> **halo** -> **flames** -> **lip**.

| Step | Trick | Notes |
| --- | --- | --- |
| Find mouths | dark pixels (luma < 46), eroded 2 px, component nearest a seed point; bbox -> ellipse `(cx, cy, rx, ry)` | `find_openings`; give `mouths={i: (cx, cy, rx, ry)}` by hand for bells the search can't isolate (read them off a 4x zoom) |
| Spill | hull pixels within 7 px of the mouth blend toward the engine colour, 0.12 steps, ordered dither (4x4 Bayer) | per-engine strength follows that engine's pulse; the hull stays recognisable, cell 0 is untouched |
| Inner glow | radial ramp inside the mouth: hot white-yellow -> core colour -> mid -> engine colour, 4 dithered steps | outermost step translucent so the dark wall shows |
| Halo | very soft quantised, dithered alpha ellipse round the mouth and the first stretch of plume (max alpha ~78) | never above the mouth's upper half; stays inside the 6 px cell margin |
| Soft root | the plume only lives inside the mouth above its centre line, fading through an ordered-dither band at the ellipse edge (`keep_mask`) | no hard clip line; below the centre line it is unclipped |
| Vena contracta | plume width is 1.0 at the exit, narrows to 0.82 by t = 0.08, flares to 1.08 by t = 0.30, then tapers (`neck`) | the neck reads as coming out of the bore |
| Layers | dark translucent sheath (2 px blocks, edge flickers +-1 px per frame), outer, mid (lightened by the pulse), white core (4 px blocks) | different noise per frame (seeded per frame), engines on their own phase `(f + 3 i) mod 8` |
| Tip | far 22% of each layer dissolves in an ordered dither; 1-3 detached 2x2 ember pixels beyond the tip (kept 8 px off the cell edge) | |
| Shock diamonds | two small white diamonds on the axis (t = 0.40, 0.64) on the longest drawings only | |
| Lip | the hull's lower rim (pixels just outside the mouth ellipse, below its centre line, <= 6.5 px) is re-composited OVER the flame | pure reordering of existing hull pixels: the plume visibly emerges from behind the lip, out of the mouth |
| Pulse | whole engine region (spill, inner glow, halo, mid colour) brightens/dims per drawing with the engine's own phase | |

## Cells

`0` parked (bare hull, byte-identical to the master), `1` pilot lights (inner glow + faint spill/halo, no plume), `2` ignition (short
plume), `3-10` the 8-drawing burn cycle, `11` damaged (shorter ragged plume, dimmer pulse). Flame length per drawing: `LENGTHS`,
widths `WIDTHS`.

## Checks

`SpaceEliteTest.EngineFlames`: per engine and cell 2-11 the flame covers the nozzle point (in front of the hull), the nozzle lies in the
dark mouth, the opaque flame runs >= 10 px past the hull's bottom edge, engines differ in phase, no runtime plume
(`exhaustScale: 0`), and every cell is 6 px clear of the border. Copy it for a new world's elites.

## Doing it for a new ship

1. Add the ship to `SHIPS` (master file, `nx` = rough mouth x shares, flame colours outer / mid / core).
2. Run `build_strips.py --out <scratch>` and look at the engine area at 3x; fix any mouth the search mis-isolates via `mouths={...}`.
3. Run it for real, then `points.py` (nozzles), `install.py`.
4. Keep the flame inside the cell with 6 px margin: shorten `LENGTHS` rather than clip.
