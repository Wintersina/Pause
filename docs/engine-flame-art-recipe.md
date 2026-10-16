# Engine flame art recipe

How the four Space elites' baked engine burn is built (`Pause/Assets/Art/Enemies/src~/space_elites_engine_burn/build_strips.py`).
Same look as the Tide elites (PR #46): clean, blocky, no dither noise. Reuse it for any elite whose strip bakes its flames.

## The rules that matter

1. **The flame goes IN FRONT of the hull, rooted at the bell's mouth centre.** When the master draws the engine seen from behind
   (a dark bell mouth facing the viewer), a flame composited under the hull and started at the hull's bottom edge reads as coming
   from behind the engine. Find each mouth, start the plume at its centre line, draw it over the hull. The dark upper part of the
   bore stays visible above the flame root.
2. **Nozzle points = the mouth centre**, not the bell body (`points.py` uses the same openings, so EliteTest / SpaceEliteTest
   measure where the flame really leaves).
3. **Keep it plain.** Things tried and rejected by eye: a flat lit disc inside the mouth, the bell's lip redrawn over the flame root
   (reads as a washer), dithered spill / halo (reads as speckle), shock diamonds, ordered-dither roots and tips.

## The plume (`flame`)

On a canvas of 4 px blocks, three stepped layers, each engine in its own colour ramp (outer, mid, core):

| Layer | Length | Width | Colour |
| --- | --- | --- | --- |
| outer | 1.00 | 1.00 | darker engine colour (blue / violet / magenta / gold) |
| mid | 0.74 | 0.66 | light body |
| core | 0.36 | 0.34 | white, a few blocks wide |

- Width: 0.9 of the mouth at the exit, widening to ~1.1 by 25% of the length (`ramp`), then tapering `(1 - t^1.8)^0.85`.
- Ragged edge: outer and mid layers drop a whole block off either edge with probability `0.12 + 0.45 t`, a different roll every drawing
  and every engine, so the silhouette flickers in hard steps.
- Tip: 2-4 detached single blocks past the tip (kept 8 px inside the cell edge).
- Halo: one very faint elliptical halo (alpha 20) of the outer colour round the plume, from the mouth centre line down.
- Per engine phase: `(f + 3 i) mod 8` into `LENGTHS` / `WIDTHS`, so each engine runs its own cycle.

## Finding the mouths

`find_openings`: dark pixels (luma < 46), eroded 2 px, component nearest a seed point; bbox -> ellipse `(cx, cy, rx, ry)`.
Give `mouths={i: (cx, cy, rx, ry)}` by hand for bells the search can't isolate (read them off a 4x zoom).

## Cells

`0` parked (bare hull, byte-identical to the master), `1` pilot lights (small lit ellipse in each mouth), `2` ignition (short plume),
`3-10` the 8-drawing burn cycle, `11` damaged (shorter, ragged plume).

## Checks

`SpaceEliteTest.EngineFlames`: per engine and cell 2-11 the flame covers the nozzle point (in front of the hull), the nozzle lies in the
dark mouth, the opaque flame runs >= 10 px past the hull's bottom edge, engines differ in phase, no runtime plume
(`exhaustScale: 0`), and every cell is 6 px clear of the border. Copy it for a new world's elites.

## New ship

1. Add the ship to `SHIPS` (master file, `nx` = rough mouth x shares, flame colours outer / mid / core).
2. `build_strips.py --out <scratch>`; look at the engine area at 3x; fix any mouth the search mis-isolates via `mouths={...}`.
3. Run it for real, then `points.py` (nozzles), `install.py`.
4. Keep the flame inside the cell with 6 px margin: shorten `LENGTHS` rather than clip.
