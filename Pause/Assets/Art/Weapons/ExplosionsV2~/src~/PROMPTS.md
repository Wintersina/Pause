# Painted keyframe generation

Tool: built-in `image_gen`, transparent background. One 2 × 2 candidate image was generated for each material. The generated PNGs are stored next to this file as `<material>_key_candidates.png`; `candidate_contact.png` records the inspection contact sheet. The top two quadrants are peak candidates and the bottom two are breakup candidates. `build_explosions.py` uses selected quadrants as painted source layers for every ten-frame row.

## Common prompt

> Use case: stylized-concept. Asset type: painted keyframe source for a small top-down 2D shooter pixel-art explosion animation. Make a precise 2 by 2 grid of FOUR separate isolated effects with roomy empty gutters and no dividing lines: top left = compact initial peak fireball; top right = different peak candidate; bottom left = dispersed material debris after the core cools; bottom right = different debris candidate. Each effect circular and undirected, centered in its quadrant, occupies no more than half its quadrant width. Transparent background with true alpha. Professional realistic-leaning neon pixel art: tiny hand-placed square pixels, richly faceted material, hard luminous pixel core and stepped translucent halo, subtle smoke or vapor. Painted, tactile, intricate, like high-quality enemy death sprites. Restrained luminosity, small white-hot point only. NO comic-book burst, NO jagged star polygon, NO black outline around FX, NO radial spokes, NO thick ring, NO text, NO UI, NO checkerboard.

Each material call appended its own line and: “Color separation and debris texture should make this material immediately recognizable.”

| Row | Material line appended to common prompt | Selected peak/debris quadrants |
| --- | --- | --- |
| Metal | Material variant: metal. white-hot pin core cooling to violet-magenta plasma, blue-gray steel plates, short metallic sparks; no red. | top left / bottom right |
| Rock | Material variant: rock. amber and ochre blast cloud, fragmented gray-brown stone, mineral dust wisps and tiny gold sparks; no red. | top left / bottom left |
| Mine | Material variant: mine. dark gunmetal casing tearing apart, restrained cyan and magenta electric plasma, faceted armor bits; no red. | top left / bottom left |
| Ice | Material variant: ice. cyan-white crystalline flash, needle ice shards, soft blue frost vapor and cold glitter; no red. | top left / bottom left |
| Spore | Material variant: spore. lime green bioluminescent pollen cloud, organic seed husks and tiny spores, muted moss vapor; no red. | top right / bottom left |
| Magma | Material variant: magma. golden yellow and orange molten splash, dark basalt fragments, hot amber embers and charcoal smoke; no red. | top left / bottom right |

The chosen source keys are reduced to the 64 px game-pixel grid with nearest-neighbor sampling and row-specific ramps. The frame poses, moving debris clumps, limited-alpha glow, flashes, and ring are composed in `build_explosions.py`, then exported at 2× nearest neighbor. The script also builds all three previews and runs the atlas checks.
