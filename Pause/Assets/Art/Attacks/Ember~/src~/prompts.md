# Ember rail mine flame source

All three candidates were made with the built-in image generation tool on transparent backgrounds. `candidate_plume_b.png` supplies the six sustained frames, pilots and haze. `candidate_burst.png` supplies the ignition and impact shapes. `candidate_plume_a.png` was inspected but rejected because its broad smoke and glowing background made a narrow, transparent beam difficult to clean.

- Plume A: isolated tall upward volcanic flame for a 2D shooter; dense neon pixel clusters; pale core, orange magma body, pink hostile fringe, plum smoke; transparent background.
- Plume B: isolated seven-to-one upward jet with vertical streaks, ragged tongues, sparks, pink-white core and magenta edge; dense arcade pixel painting; transparent background.
- Burst: square ignition burst with a white-pink center, asymmetrical orange tongues, plum soot and thin magenta fringes; transparent background.

`build.py` samples these painted forms at 64 px native cell size, maps their colors to the safe ramps, animates by changing sample position, and exports x2 nearest-neighbor. No blur is used. Run `python3 build.py` here, then `python3 verify.py`.

The 6 px cell-edge transparency and a fully continuous vertically tiled flame cannot both hold. The loop is intended to stretch along the lane; the transparent top and bottom edges match, but repeated tiling would expose a 12 px clear join. The generated detail remains a little finer than the original mine atlas at game size.
