# Desert backdrop run C: painted source prompts

Built-in image generation was used for three transparent 4×4 source sheets. The generated images are retained here as `candidate_c_smoke.png`, `candidate_c_sand.png`, and `candidate_c_lights.png`.

1. **Smoke candidate:** isolated rows of dark refinery smoke, lighter vent steam, a tall spinning dust devil, and a wind-lifted dune plume. Chunky detailed pixel art, bone-gold sand, dusty-indigo shadow, dark industrial feel, transparent background, no orange/amber/red/pink/violet/teal/green or scenery.
2. **Sand candidate:** isolated ledge sandfalls, dune slides, horizontal grit gusts, and sand trickles from broken pipes. Rugged pixel art with pale desaturated sand, muted copper and dusty-indigo shadows, transparent background; no saturated glow or UI.
3. **Lights candidate:** isolated dark industrial beacons, lamps and refinery windows; low heat-shimmer/salt glints; glass sparkles. Sun-white and pale-gold light only, transparent background; no orange/amber/red/pink/violet/teal/green.

The source sheets were visually inspected. The smoke sheet supplied the strongest vapor and dust-devil silhouettes; the sand sheet supplied the best ledge, pipe, and grain forms; the lights sheet supplied the fixtures and shimmer texture. Generated red/yellow fringe artifacts were removed during extraction, and each frame was authored at 128×128 and enlarged with nearest-neighbour scaling. The art remains generated painted content with programmatic key-frame assembly, contact positioning, particles, and controlled lamp bloom.

The output manifest is `manifest_c.json` because the task also prohibits edits to the existing `manifest.json`. It contains the requested `run_c` block and keeps the earlier runs intact.
